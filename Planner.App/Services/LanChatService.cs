using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

public sealed class LanChatService : IChatTransport, IDisposable
{
    public const int UdpPort = 47821;
    public const int TcpPort = 47822;

    private readonly ChatStore _store;
    private readonly UserAccountService _users;
    private CancellationTokenSource? _cts;
    private UdpClient? _udp;
    private TcpListener? _tcp;

    public event Action? Changed;
    public event Action<ChatMessage>? MessageReceived;

    public LanChatService(ChatStore store, UserAccountService users)
    {
        _store = store;
        _users = users;
    }

    public string Name => "Yerel ağ";
    public Dictionary<string, ChatPeer> Peers { get; } = new(StringComparer.Ordinal);
    IReadOnlyDictionary<string, ChatPeer> IChatTransport.Peers => Peers;

    public Task StartAsync(CancellationToken ct = default)
    {
        Start();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, UdpPort))
            {
                EnableBroadcast = true
            };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _tcp = new TcpListener(IPAddress.Any, TcpPort);
            _tcp.Start();
            _ = Task.Run(() => BroadcastLoop(_cts.Token));
            _ = Task.Run(() => ListenUdp(_cts.Token));
            _ = Task.Run(() => ListenTcp(_cts.Token));
        }
        catch
        {
            // Ağ kapalı / port meşgul: yerel sohbet yine çalışır.
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _udp?.Dispose();
        _udp = null;
        try { _tcp?.Stop(); } catch { /* ignore */ }
        _tcp = null;
        _cts?.Dispose();
        _cts = null;
    }

    public async Task SendAsync(ChatPeer peer, string body)
    {
        var me = _users.Current;
        if (me is null || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationKey = ChatStore.ConversationKey(me.Id.ToString("N"), peer.Key),
            FromKey = me.Id.ToString("N"),
            ToKey = peer.Key,
            FromName = me.DisplayName,
            Body = body.Trim(),
            SentAt = DateTime.Now,
            IsOutgoing = true
        };
        await _store.SaveAsync(message);
        Changed?.Invoke();
        await DeliverAsync(peer, message);
    }

    public async Task DeliverAsync(ChatPeer peer, ChatMessage message, CancellationToken ct = default)
    {
        if (peer.Kind == ChatPeerKind.Local || string.IsNullOrWhiteSpace(peer.Endpoint))
        {
            return;
        }

        if (!IPEndPoint.TryParse(peer.Endpoint, out var ep))
        {
            return;
        }

        await TryPush(ep, message);
    }

    public void AddLocalUsers(IEnumerable<AppUser> users)
    {
        var me = _users.CurrentKey;
        foreach (var user in users)
        {
            var key = user.Id.ToString("N");
            if (key == me)
            {
                continue;
            }

            Peers[key] = new ChatPeer
            {
                Key = key,
                Name = user.DisplayName,
                Username = user.Username,
                Kind = ChatPeerKind.Local,
                IsOnline = true,
                LastSeen = DateTime.Now
            };
        }

        Changed?.Invoke();
    }

    private async Task BroadcastLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var me = _users.Current;
                if (me is not null && _udp is not null)
                {
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new PresencePacket
                    {
                        Key = me.Id.ToString("N"),
                        Name = me.DisplayName,
                        Username = me.Username,
                        Port = TcpPort
                    });
                    await _udp.SendAsync(payload, new IPEndPoint(IPAddress.Broadcast, UdpPort));
                }
            }
            catch
            {
                // yayın başarısız olabilir
            }

            try { await Task.Delay(8000, ct); } catch { return; }
        }
    }

    private async Task ListenUdp(CancellationToken ct)
    {
        if (_udp is null) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                var packet = JsonSerializer.Deserialize<PresencePacket>(result.Buffer);
                var me = _users.CurrentKey;
                if (packet is null || packet.Key == me)
                {
                    continue;
                }

                var ep = new IPEndPoint(result.RemoteEndPoint.Address, packet.Port);
                Peers[packet.Key] = new ChatPeer
                {
                    Key = packet.Key,
                    Name = packet.Name,
                    Username = packet.Username ?? "",
                    Endpoint = ep.ToString(),
                    Kind = ChatPeerKind.Lan,
                    IsOnline = true,
                    LastSeen = DateTime.Now
                };
                Changed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // bozuk paket
            }
        }
    }

    private async Task ListenTcp(CancellationToken ct)
    {
        if (_tcp is null) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcp.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClient(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(400, ct);
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                ChatMessage? incoming;
                try { incoming = JsonSerializer.Deserialize<ChatMessage>(line); }
                catch { return; }
                if (incoming is null)
                {
                    return;
                }

                incoming.IsOutgoing = false;
                incoming.ConversationKey = ChatStore.ConversationKey(incoming.FromKey, _users.CurrentKey);
                await _store.SaveAsync(incoming);
                MessageReceived?.Invoke(incoming);
                Changed?.Invoke();
            }
        }
        catch
        {
            // karşı taraf kapandı
        }
    }

    private static async Task TryPush(IPEndPoint ep, ChatMessage message)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ep.Address, ep.Port);
            await using var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(message));
        }
        catch
        {
            // karşı taraf kapalı
        }
    }

    public void Dispose() => Stop();

    private sealed class PresencePacket
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Username { get; set; } = "";
        public int Port { get; set; }
    }
}
