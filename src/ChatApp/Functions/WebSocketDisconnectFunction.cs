using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Functions;

public sealed class WebSocketDisconnectFunction : RequestResponseFunctionBase
{
    public WebSocketDisconnectFunction() : this(Startup.Configure().BuildServiceProvider())
    {
    }

    public WebSocketDisconnectFunction(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var dynamoDbClient = ServiceProvider.GetService<IAmazonDynamoDB>();
        var tableName = Environment.GetEnvironmentVariable("TABLE_NAME");
        var table = Table.LoadTable(dynamoDbClient, new TableConfig(tableName));
        await table.DeleteItemAsync(ChatId.DEFAULT, request.RequestContext.ConnectionId);

        return new APIGatewayProxyResponse
        {
            StatusCode = 200,
            Body = "Disconnected"
        };
    }
}