namespace ChatApp.Events;

public sealed class MessageEvent
{
    public string? SenderConnectionId { get; set; }
    public string? Message { get; set; }
    public string? ChatId { get; set; }
}