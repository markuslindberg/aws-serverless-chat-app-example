using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using ChatApp.Events;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Functions;

public sealed class WebSocketRequestFunction : RequestResponseFunctionBase
{
    public WebSocketRequestFunction() : this(Startup.Configure().BuildServiceProvider())
    {
    }

    public WebSocketRequestFunction(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var putRequest = new PutEventsRequest
        {
            Entries = new List<PutEventsRequestEntry>
            {
                new PutEventsRequestEntry
                {
                    EventBusName = Environment.GetEnvironmentVariable("BUS_NAME"),
                    Source = "WebSocketRequestFunction",
                    DetailType = "ChatMessageReceived",
                    Detail = JsonSerializer.Serialize(new MessageEvent
                    {
                        SenderConnectionId = request.RequestContext.ConnectionId,
                        Message = request.Body,
                        ChatId = ChatId.DEFAULT
                    })
                }
            }
        };

        Logger
            .ForContext("Request", JsonSerializer.Serialize(putRequest, JsonSerializerOptions))
            .Information("Sending \"ChatMessageReceived\" to event bus");

        var eventBridgeClient = ServiceProvider.GetRequiredService<IAmazonEventBridge>();
        var putResponse = await eventBridgeClient.PutEventsAsync(putRequest);

        Logger
            .ForContext("Response", JsonSerializer.Serialize(putResponse, JsonSerializerOptions))
            .Information("Sent \"ChatMessageReceived\" to event bus");

        return new APIGatewayProxyResponse
        {
            StatusCode = 200,
            Body = "Ok"
        };
    }
}