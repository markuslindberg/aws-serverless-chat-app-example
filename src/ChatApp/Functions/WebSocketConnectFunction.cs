using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Functions;

public sealed class WebSocketConnectFunction : RequestResponseFunctionBase
{
    public WebSocketConnectFunction() : this(Startup.Configure().BuildServiceProvider())
    {
    }

    public WebSocketConnectFunction(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var d = new Document();
        d["chatId"] = ChatId.DEFAULT;
        d["connectionId"] = request.RequestContext.ConnectionId;
        d["ttl"] = DateTimeOffset.Now.AddHours(1).ToUnixTimeSeconds();

        var dynamoDbClient = ServiceProvider.GetService<IAmazonDynamoDB>();
        var tableName = Environment.GetEnvironmentVariable("TABLE_NAME");
        var table = Table.LoadTable(dynamoDbClient, new TableConfig(tableName));
        await table.PutItemAsync(d);

        return new APIGatewayProxyResponse
        {
            StatusCode = 200,
            Body = "Connected"
        };
    }
}