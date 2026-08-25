using Planner.Chat;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

/// <summary>
/// Yerel hesap, LAN ve sunucu taşımalarını tek listede birleştirir.
/// Farklı ağlar için yol: sunucuya çıkış bağlantısı (WebSocket).
/// </summary>
public sealed class ChatHub : IDisposable
{
    private readonly LanChatService _lan;
    private readonly ServerChatClient _server;
    private readonly ChatStore _store;
    private readonly UserAccountService _users;
    private readonly SettingsService _settings;
    private readonly FriendshipService _friends;

    public ChatHub(
        LanChatService lan,
        ServerChatClient server,
        ChatStore store,
        UserAccountService users,
        SettingsService settings,
        FriendshipService friends)
    {
        _lan = lan;
        _server = server;
        _store = store;
        _users = users;
        _settings = settings;
        _friends = friends;
        _lan.Changed += () => Changed?.Invoke();
        _lan.MessageReceived += m => MessageReceived?.Invoke(m);
        _server.Changed += () => Changed?.Invoke();
        _server.MessageReceived += m => MessageReceived?.Invoke(m);
    }

    public event Action? Changed;
    public event Action<ChatMessage>? MessageReceived;

    public ServerChatClient Server => _server;
    public string ServerStatus => _server.Status;
    public bool ServerConnected => _server.IsConnected;

    public string IdentityKey =>
        !string.IsNullOrWhiteSpace(_server.UserId) ? _server.UserId : _users.CurrentKey;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(_server.DisplayName) ? _server.DisplayName : _users.CurrentName;

    public async Task StartAsync()
    {
        var lanOn = await _settings.GetBoolAsync(SettingKeys.ChatLanEnabled, true);
        if (lanOn)
        {
            _lan.Start();
        }
        else
        {
            _lan.Stop();
        }

        await _server.StartAsync();
        Changed?.Invoke();
    }

    public async Task RestartAsync()
    {
        await _server.StopAsync();
        await StartAsync();
    }

    public void AddLocalUsers(IEnumerable<AppUser> users) => _lan.AddLocalUsers(users);

    public void RememberServerUser(DirectoryUser user) => _server.RememberPeer(user);

    public Task<IReadOnlyList<DirectoryUser>> SearchServerUsersAsync(string query, CancellationToken ct = default)
        => _server.SearchDirectoryAsync(query, ct);

    public IReadOnlyList<ChatPeer> SnapshotPeers()
    {
        var map = new Dictionary<string, ChatPeer>(StringComparer.Ordinal);
        foreach (var peer in _lan.Peers.Values)
        {
            map[peer.Key] = peer;
        }

        foreach (var peer in _server.Peers.Values)
        {
            map[peer.Key] = peer;
        }

        return map.Values
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public async Task SendAsync(ChatPeer peer, string body, bool bypassFriendCheck = false)
    {
        var me = peer.Kind == ChatPeerKind.Server ? _server.UserId : _users.CurrentKey;
        var name = peer.Kind == ChatPeerKind.Server ? _server.DisplayName : _users.CurrentName;
        if (string.IsNullOrWhiteSpace(me) || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (!bypassFriendCheck && !await _friends.AreFriendsByKeyAsync(_users.CurrentKey, peer.Key))
        {
            throw new InvalidOperationException("Mesaj, arama ve paylaşım yalnızca arkadaşlar arasında.");
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationKey = ChatStore.ConversationKey(me, peer.Key),
            FromKey = me,
            ToKey = peer.Key,
            FromName = string.IsNullOrWhiteSpace(name) ? "Ben" : name,
            Body = body.Trim(),
            SentAt = DateTime.Now,
            IsOutgoing = true
        };
        if (message.Body.Length > ChatRoutes.MaxBodyChars
            && !CollabPayload.IsImage(message.Body)
            && !CollabPayload.IsFile(message.Body)
            && !CollabPayload.IsCall(message.Body)
            && !CollabPayload.IsShare(message.Body)
            && !CollabPayload.IsAgenda(message.Body)
            && !CollabPayload.IsEdit(message.Body)
            && !CollabPayload.IsReact(message.Body))
        {
            message.Body = message.Body[..ChatRoutes.MaxBodyChars];
        }

        await _store.SaveAsync(message);
        Changed?.Invoke();

        switch (peer.Kind)
        {
            case ChatPeerKind.Local:
                return;
            case ChatPeerKind.Server:
                await _server.DeliverAsync(peer, message);
                return;
            default:
                await _lan.DeliverAsync(peer, message);
                return;
        }
    }

    public async Task SyncThreadAsync(ChatPeer peer)
    {
        if (peer.Kind == ChatPeerKind.Server)
        {
            await _server.SyncThreadAsync(peer.Key);
        }
    }

    public string ThreadKey(ChatPeer peer)
    {
        var me = peer.Kind == ChatPeerKind.Server ? _server.UserId : _users.CurrentKey;
        return ChatStore.ConversationKey(me, peer.Key);
    }

    public ChatPeer? FindPeer(string key)
        => SnapshotPeers().FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    public ChatPeer PeerFor(string key, string name)
    {
        return FindPeer(key) ?? new ChatPeer
        {
            Key = key,
            Name = string.IsNullOrWhiteSpace(name) ? "Kişi" : name,
            Kind = ChatPeerKind.Lan,
            IsOnline = false,
            LastSeen = DateTime.Now
        };
    }

    public async Task<bool> AreFriendsWithAsync(string peerKey)
        => await _friends.AreFriendsByKeyAsync(_users.CurrentKey, peerKey);

    public void Dispose()
    {
        _lan.Stop();
        _server.Dispose();
    }
}
