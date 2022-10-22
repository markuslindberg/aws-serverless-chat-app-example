using System.Text;
using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using ChatApp.Events;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Functions;

public sealed class WebSocketResponseFunction : CloudWatchEventFunctionBase
{
    public WebSocketResponseFunction() : this(Startup.Configure().BuildServiceProvider())
    {
    }

    public WebSocketResponseFunction(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    [LambdaSerializer(typeof(SourceGeneratorLambdaJsonSerializer<LambdaJsonSerializerContext>))]
    public Task HandleAsync(CloudWatchEvent<MessageEvent> @event, ILambdaContext context)
    {
        return InvokeWrapper(@event, context, HandleEvent);
    }

    private async Task HandleEvent(CloudWatchEvent<MessageEvent> @event, ILambdaContext context)
    {
        var apiGatewayClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
        {
            ServiceURL = Environment.GetEnvironmentVariable("WEBSOCKET_URL")
        });

        var connections = await GetConnections(@event.Detail.SenderConnectionId, ChatId.DEFAULT);

        foreach (var connectionId in connections)
        {
            await apiGatewayClient.PostToConnectionAsync(new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new { Data = @event.Detail.Message }, JsonSerializerOptions)))
            });
        }
    }

    private async Task<IEnumerable<string>> GetConnections(string? senderConnectionId, string chatId)
    {
        var dynamoDbClient = ServiceProvider.GetService<IAmazonDynamoDB>();

        var tableName = Environment.GetEnvironmentVariable("TABLE_NAME");
        var table = Table.LoadTable(dynamoDbClient, new TableConfig(tableName));
        var search = table.Query(new QueryOperationConfig
        {
            AttributesToGet = new List<string> { "connectionId" },
            Filter = new QueryFilter("chatId", QueryOperator.Equal, chatId),
            Select = SelectValues.SpecificAttributes
        });

        var docs = await search.GetNextSetAsync();
        return docs.Select(d => d["connectionId"].AsString())
            .Where(x => x != senderConnectionId);
    }
}