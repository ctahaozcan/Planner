namespace Planner.App.Services;

public interface IChatTransport
{
    string Name { get; }
    IReadOnlyDictionary<string, ChatPeer> Peers { get; }
    event Action? Changed;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task DeliverAsync(ChatPeer peer, Planner.Core.Models.ChatMessage message, CancellationToken ct = default);
}
