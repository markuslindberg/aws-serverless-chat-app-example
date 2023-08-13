using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2.Alpha;
using Amazon.CDK.AWS.Apigatewayv2.Integrations.Alpha;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace Cdk
{
    public class ChatAppStack : Stack
    {
        internal ChatAppStack(Construct scope, string id, ChatAppStackProps? props = null) : base(scope, id, props)
        {
            var table = new Table(this, "ChatConnectionsTable", new TableProps
            {
                TableName = "ChatConnectionsTable",
                PartitionKey = new Attribute { Name = "chatId", Type = AttributeType.STRING },
                SortKey = new Attribute { Name = "connectionId", Type = AttributeType.STRING },
                TimeToLiveAttribute = "ttl",
                RemovalPolicy = RemovalPolicy.DESTROY,
                BillingMode = BillingMode.PAY_PER_REQUEST
            });

            var eventBus = new EventBus(this, "ChatEventBus", new EventBusProps
            {
                EventBusName = "ChatEventBus"
            });

            var connectLambda = new ChatAppLambda(this, "WebSocketConnectLambda", new FunctionProps
            {
                FunctionName = "WebSocketConnectFunction",
                Description = "Handles the connect event emitted by the WebSocket API Gateway",
                Handler = "ChatApp::ChatApp.Functions.WebSocketConnectFunction::HandleAsync",
                Environment = new Dictionary<string, string>
                {
                    { "TABLE_NAME", table.TableName }
                }
            });

            var disconnectLambda = new ChatAppLambda(this, "WebSocketDisconnectLambda", new FunctionProps
            {
                FunctionName = "WebSocketDisconnectFunction",
                Description = "Handles the disconnect event emitted by the WebSocket API Gateway",
                Handler = "ChatApp::ChatApp.Functions.WebSocketDisconnectFunction::HandleAsync",
                Environment = new Dictionary<string, string>
                {
                    { "TABLE_NAME", table.TableName }
                }
            });

            var requestLambda = new ChatAppLambda(this, "WebSocketRequestLambda", new FunctionProps
            {
                FunctionName = "WebSocketRequestFunction",
                Description = "Handles requests sent via WebSocket API Gateway",
                Handler = "ChatApp::ChatApp.Functions.WebSocketRequestFunction::HandleAsync",
                Environment = new Dictionary<string, string>
                {
                    { "BUS_NAME", eventBus.EventBusName }
                }
            });

            var webSocketApi = new WebSocketApi(this, "WebSocketApi", new WebSocketApiProps
            {
                ApiName = "WebSocketApi",
                Description = "A regional WebSocket API for the multi-region chat application",
                ConnectRouteOptions = new WebSocketRouteOptions
                {
                    Integration = new WebSocketLambdaIntegration("ConnectIntegration", connectLambda.Fn)
                },
                DisconnectRouteOptions = new WebSocketRouteOptions
                {
                    Integration = new WebSocketLambdaIntegration("DisconnectIntegration", disconnectLambda.Fn)
                },
                DefaultRouteOptions = new WebSocketRouteOptions
                {
                    Integration = new WebSocketLambdaIntegration("DefaultRouteIntegration", requestLambda.Fn)
                }
            });

            var webSocketStage = new WebSocketStage(this, "WebSocketStage", new WebSocketStageProps
            {
                WebSocketApi = webSocketApi,
                StageName = "chat",
                AutoDeploy = true
            });

            var responseLambda = new ChatAppLambda(this, "WebSocketResponseLambda", new FunctionProps
            {
                FunctionName = "WebSocketResponseFunction",
                Description = "Handles \"ChatMessageReceived\" events by determining the target connection ids and pushes the message to the clients",
                Handler = "ChatApp::ChatApp.Functions.WebSocketResponseFunction::HandleAsync",
                Environment = new Dictionary<string, string>
                {
                    { "TABLE_NAME", table.TableName },
                    { "WEBSOCKET_URL", webSocketStage.CallbackUrl }
                }
            });

            var allowConnectionManagementOnApiGatewayPolicy = new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Resources = new[] { $"arn:aws:execute-api:{Region}:{Account}:{webSocketApi.ApiId}/{webSocketStage.StageName}/*" },
                Actions = new[] { "execute-api:ManageConnections" }
            });

            var crossRegionEventRole = new Role(this, "CrossRegionRole", new RoleProps
            {
                InlinePolicies = { },
                AssumedBy = new ServicePrincipal("events.amazonaws.com")
            });

            var crossRegionalEventBusTargets = props!.RegionsToReplicate!.Select(region => new Amazon.CDK.AWS.Events.Targets.EventBus(
                Amazon.CDK.AWS.Events.EventBus.FromEventBusArn(this, $"WebSocketBus-{region}", $"arn:aws:events:{region}:{this.Account}:event-bus/{eventBus.EventBusName}"),
                new Amazon.CDK.AWS.Events.Targets.EventBusProps
                {
                    Role = crossRegionEventRole
                }));

            new Amazon.CDK.AWS.Events.Rule(this, "ProcessRequestRole", new Amazon.CDK.AWS.Events.RuleProps
            {
                EventBus = eventBus,
                Enabled = true,
                RuleName = "ProcessChatMessage",
                Description = "Invokes a Lambda function for each chat message to push the event via websocket and replicates the event to event buses in other regions",
                EventPattern = new EventPattern
                {
                    DetailType = new[] { "ChatMessageReceived" },
                    Source = new[] { "WebSocketRequestFunction" }
                },
                Targets = crossRegionalEventBusTargets.Cast<IRuleTarget>()
                    .Append(new Amazon.CDK.AWS.Events.Targets.LambdaFunction(responseLambda.Fn))
                    .ToArray()
            });

            table.GrantFullAccess(connectLambda.Fn);
            table.GrantFullAccess(disconnectLambda.Fn);
            table.GrantFullAccess(requestLambda.Fn);
            table.GrantReadData(responseLambda.Fn);
            eventBus.GrantPutEventsTo(requestLambda.Fn);
            responseLambda.Fn.AddToRolePolicy(allowConnectionManagementOnApiGatewayPolicy);

            new CfnOutput(this, "bucketName", new CfnOutputProps
            {
                Value = webSocketStage.Url,
                Description = "WebSocket API Url",
                ExportName = $"websocket-api-{Region}",
            });
        }
    }

    public class ChatAppLambda : Construct
    {
        public Function Fn { get; set; }
        public ChatAppLambda(Construct scope, string id, FunctionProps props) : base(scope, id)
        {
            Fn = new Function(this, id, new FunctionProps
            {
                FunctionName = props.FunctionName,
                Description = props.Description,
                Handler = props.Handler,
                Environment = props.Environment,
                Code = Code.FromAsset("../src/ChatApp/bin/Release/net6.0/ChatApp.zip"),
                Runtime = Runtime.DOTNET_6,
                Timeout = Duration.Seconds(5),
                MemorySize = 256,
                Tracing = Tracing.ACTIVE
            });
        }
    }

    public class ChatAppStackProps : StackProps
    {
        public IList<string>? RegionsToReplicate { get; set; }
    }
}