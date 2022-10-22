using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;

namespace ChatApp.Functions;

public abstract class CloudWatchEventFunctionBase
{
    private bool _isColdStart = true;
    protected IServiceProvider ServiceProvider { get; init; }
    protected ILogger Logger { get; init; }
    protected JsonSerializerOptions JsonSerializerOptions { get; init; }

    protected CloudWatchEventFunctionBase(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        JsonSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    protected async Task InvokeWrapper<T>(
        CloudWatchEvent<T> @event,
        ILambdaContext context,
        Func<CloudWatchEvent<T>, ILambdaContext, Task> handler)
    {
        using (LogContext.PushProperty("EventId", @event.Id))
        using (LogContext.PushProperty("EventSource", @event.Source))
        using (LogContext.PushProperty("RequestId", context.AwsRequestId))
        using (LogContext.PushProperty("FunctionArn", context.InvokedFunctionArn))
        using (LogContext.PushProperty("ColdStart", _isColdStart))
        {
            _isColdStart = false;
            var sw = Stopwatch.StartNew();

            try
            {
                await handler(@event, context);

                Logger
                    .ForContext("Event", JsonSerializer.Serialize(@event, JsonSerializerOptions))
                    .Information("Function completed in {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Logger
                    .ForContext("Event", JsonSerializer.Serialize(@event, JsonSerializerOptions))
                    .Error(ex, "Function failed after {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            }
        }
    }
}