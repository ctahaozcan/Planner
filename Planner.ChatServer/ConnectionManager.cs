using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Planner.Chat;

namespace Planner.ChatServer;

public sealed class ConnectionManager
{
    private readonly IDbContextFactory<ChatServerDb> _factory;
    private readonly ConcurrentDictionary<Guid, List<ClientConnection>> _byUser = new();
    private readonly object _gate = new();

    public ConnectionManager(IDbContextFactory<ChatServerDb> factory)
    {
        _factory = factory;
    }

    public bool IsOnline(Guid userId) => _byUser.ContainsKey(userId);

    public IReadOnlySet<Guid> OnlineIds()
        => _byUser.Keys.ToHashSet();

    public async Task RunAsync(ServerUser user, WebSocket socket, CancellationToken ct)
    {
        var conn = new ClientConnection(user.Id, socket);
        Add(conn);
        try
        {
            await SendAsync(conn, ChatCodec.Pack(ChatTypes.Hello, new HelloPayload
            {
                Protocol = ChatRoutes.ProtocolVersion,
                UserId = user.Id.ToString("N")
            }), ct);
            await BroadcastPresenceAsync();
            await FlushInboxAsync(conn, ct);
            await ReceiveLoopAsync(user, conn, ct);
        }
        finally
        {
            Remove(conn);
            await BroadcastPresenceAsync();
        }
    }

    public async Task DeliverOrStoreAsync(ChatMessageDto dto, CancellationToken ct)
    {
        if (!Guid.TryParse(dto.FromUserId, out var from) || !Guid.TryParse(dto.ToUserId, out var to))
        {
            throw new InvalidOperationException("Geçersiz kullanıcı.");
        }

        dto.Body = (dto.Body ?? "").Trim();
        if (dto.Body.Length == 0)
        {
            return;
        }

        if (dto.Body.Length > ChatRoutes.MaxBodyChars)
        {
            dto.Body = dto.Body[..ChatRoutes.MaxBodyChars];
        }

        if (dto.Id == Guid.Empty)
        {
            dto.Id = Guid.NewGuid();
        }

        if (dto.SentAt == default)
        {
            dto.SentAt = DateTime.UtcNow;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (!await db.Messages.AnyAsync(m => m.Id == dto.Id, ct))
        {
            db.Messages.Add(new ServerMessage
            {
                Id = dto.Id,
                FromUserId = from,
                ToUserId = to,
                FromName = dto.FromName,
                Body = dto.Body,
                SentAt = dto.SentAt,
                Delivered = false
            });
            await db.SaveChangesAsync(ct);
        }

        var delivered = await PushToUserAsync(to, ChatCodec.Pack(ChatTypes.Message, dto), ct);
        if (delivered)
        {
            var row = await db.Messages.FirstOrDefaultAsync(m => m.Id == dto.Id, ct);
            if (row is not null)
            {
                row.Delivered = true;
                await db.SaveChangesAsync(ct);
            }
        }

        await PushToUserAsync(from, ChatCodec.Pack(ChatTypes.Ack, new AckPayload { MessageId = dto.Id }), ct);
    }

    private async Task ReceiveLoopAsync(ServerUser user, ClientConnection conn, CancellationToken ct)
    {
        var buffer = new byte[ChatRoutes.MaxFrameBytes];
        while (conn.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await conn.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await conn.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text || result.Count <= 0)
            {
                continue;
            }

            if (!result.EndOfMessage)
            {
                await conn.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame", CancellationToken.None);
                return;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var envelope = ChatCodec.Parse(json);
            if (envelope is null)
            {
                continue;
            }

            switch (envelope.Type)
            {
                case ChatTypes.Ping:
                    await SendAsync(conn, ChatCodec.Pack(ChatTypes.Pong, new { }), ct);
                    break;
                case ChatTypes.Message:
                    var dto = ChatCodec.Payload<ChatMessageDto>(envelope);
                    if (dto is null)
                    {
                        break;
                    }

                    dto.FromUserId = user.Id.ToString("N");
                    dto.FromName = string.IsNullOrWhiteSpace(dto.FromName) ? user.DisplayName : dto.FromName;
                    await DeliverOrStoreAsync(dto, ct);
                    break;
            }
        }
    }

    private async Task FlushInboxAsync(ClientConnection conn, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var pending = await db.Messages.AsNoTracking()
            .Where(m => m.ToUserId == conn.UserId && !m.Delivered)
            .OrderBy(m => m.SentAt)
            .Take(200)
            .ToListAsync(ct);
        foreach (var row in pending)
        {
            var dto = ToDto(row);
            var ok = await SendAsync(conn, ChatCodec.Pack(ChatTypes.Message, dto), ct);
            if (!ok)
            {
                return;
            }

            var tracked = await db.Messages.FirstOrDefaultAsync(m => m.Id == row.Id, ct);
            if (tracked is not null)
            {
                tracked.Delivered = true;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task BroadcastPresenceAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var snapshot = await BuildSnapshotAsync(db);
        var json = ChatCodec.Pack(ChatTypes.Presence, snapshot);
        foreach (var conn in All())
        {
            await SendAsync(conn, json, CancellationToken.None);
        }
    }

    public async Task<PresenceSnapshot> BuildSnapshotAsync(ChatServerDb db)
    {
        var online = OnlineIds();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync();
        return new PresenceSnapshot
        {
            Users = users.Select(u => ToDirectory(u, online)).ToList()
        };
    }

    /// <summary>
    /// Kayıtlı Users tablosunda kullanıcı adı / görünen ad arar. Çevrimiçi olmak gerekmez.
    /// </summary>
    public async Task<PresenceSnapshot> SearchDirectoryAsync(ChatServerDb db, Guid exceptUserId, string query, int take = 50)
    {
        var q = (query ?? "").Trim();
        var online = OnlineIds();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Id != exceptUserId)
            .ToListAsync();
        return new PresenceSnapshot
        {
            Users = users
                .Where(u => MatchesDirectory(u, q))
                .OrderBy(u => DirectoryRank(u, q))
                .ThenBy(u => u.Username)
                .Take(take)
                .Select(u => ToDirectory(u, online))
                .ToList()
        };
    }

    private static bool MatchesDirectory(ServerUser user, string q)
        => user.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
           || user.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase);

    private static int DirectoryRank(ServerUser user, string q)
    {
        if (user.Username.Equals(q, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (user.Username.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (user.DisplayName.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static DirectoryUser ToDirectory(ServerUser user, IReadOnlySet<Guid> online) => new()
    {
        UserId = user.Id.ToString("N"),
        Username = user.Username,
        DisplayName = user.DisplayName,
        Online = online.Contains(user.Id)
    };

    private void Add(ClientConnection conn)
    {
        lock (_gate)
        {
            var list = _byUser.GetOrAdd(conn.UserId, _ => []);
            list.Add(conn);
        }
    }

    private void Remove(ClientConnection conn)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(conn.UserId, out var list))
            {
                return;
            }

            list.Remove(conn);
            if (list.Count == 0)
            {
                _byUser.TryRemove(conn.UserId, out _);
            }
        }
    }

    private IReadOnlyList<ClientConnection> All()
    {
        lock (_gate)
        {
            return _byUser.Values.SelectMany(v => v).ToList();
        }
    }

    public async Task<bool> NotifyUserAsync(Guid userId, string type, object payload, CancellationToken ct)
        => await PushToUserAsync(userId, ChatCodec.Pack(type, payload), ct);

    private async Task<bool> PushToUserAsync(Guid userId, string json, CancellationToken ct)
    {
        List<ClientConnection> list;
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var found) || found.Count == 0)
            {
                return false;
            }

            list = found.ToList();
        }

        var any = false;
        foreach (var conn in list)
        {
            if (await SendAsync(conn, json, ct))
            {
                any = true;
            }
        }

        return any;
    }

    private static async Task<bool> SendAsync(ClientConnection conn, string json, CancellationToken ct)
    {
        if (conn.Socket.State != WebSocketState.Open)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        await conn.SendLock.WaitAsync(ct);
        try
        {
            await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            conn.SendLock.Release();
        }
    }

    private static ChatMessageDto ToDto(ServerMessage row) => new()
    {
        Id = row.Id,
        FromUserId = row.FromUserId.ToString("N"),
        ToUserId = row.ToUserId.ToString("N"),
        FromName = row.FromName,
        Body = row.Body,
        SentAt = row.SentAt
    };

    private sealed class ClientConnection
    {
        public ClientConnection(Guid userId, WebSocket socket)
        {
            UserId = userId;
            Socket = socket;
        }

        public Guid UserId { get; }
        public WebSocket Socket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}
