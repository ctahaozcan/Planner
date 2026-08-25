using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Planner.Chat;
using Planner.Core;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

public sealed class ServerChatClient : IChatTransport, IDisposable
{
    private readonly ChatStore _store;
    private readonly SettingsService _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, ChatPeer> _peers = new(StringComparer.Ordinal);
    private readonly object _peerLock = new();
    private CancellationTokenSource? _cts;
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ServerChatClient(ChatStore store, SettingsService settings)
    {
        _store = store;
        _settings = settings;
    }

    public string Name => "Sunucu";
    public IReadOnlyDictionary<string, ChatPeer> Peers
    {
        get
        {
            lock (_peerLock)
            {
                return new Dictionary<string, ChatPeer>(_peers, StringComparer.Ordinal);
            }
        }
    }

    public event Action? Changed;
    public event Action<ChatMessage>? MessageReceived;

    public string UserId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string Username { get; private set; } = "";
    public string Status { get; private set; } = "Kapalı";
    public bool IsConnected { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await StopAsync();
        await RestoreSessionAsync();
        var enabled = await _settings.GetBoolAsync(SettingKeys.ChatServerEnabled);
        if (!enabled || string.IsNullOrWhiteSpace(await TokenAsync()) || string.IsNullOrWhiteSpace(await UrlAsync()))
        {
            Status = enabled ? "Adres ve hesap gerekli" : "Kapalı";
            IsConnected = false;
            Changed?.Invoke();
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        await CloseSocketAsync();
        _cts?.Dispose();
        _cts = null;
        IsConnected = false;
        lock (_peerLock) { _peers.Clear(); }
        if (Status is not "Kapalı" and not "Adres ve hesap gerekli")
        {
            Status = "Kapalı";
        }

        Changed?.Invoke();
    }

    public async Task DeliverAsync(ChatPeer peer, ChatMessage message, CancellationToken ct = default)
    {
        var dto = ToDto(message);
        if (_ws is { State: WebSocketState.Open })
        {
            await SendFrameAsync(ChatCodec.Pack(ChatTypes.Message, dto), ct);
            return;
        }

        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.Messages);
        req.Content = JsonContent.Create(dto, options: ChatJson.Options);
        using var response = await _http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
    }

    public event Action<WorkTaskDto>? WorkAssigned;
    public event Action<OrgLeaveDto>? LeaveUpdated;

    public async Task<AuthResponse> RegisterAsync(
        string username,
        string password,
        string? displayName,
        string? email = null,
        string? firstName = null,
        string? lastName = null,
        string? usage = null,
        Guid? companyId = null,
        Guid? unitId = null,
        Guid? positionId = null,
        string? inviteCode = null,
        CancellationToken ct = default)
    {
        var url = await UrlAsync();
        using var response = await _http.PostAsJsonAsync(Combine(url, ChatRoutes.Register), new AuthRequest
        {
            Username = username,
            Password = password,
            DisplayName = displayName,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Usage = usage,
            CompanyId = companyId,
            UnitId = unitId,
            PositionId = positionId,
            InviteCode = inviteCode
        }, ChatJson.Options, ct);
        return await ReadAuthAsync(response, ct);
    }

    public async Task<AuthResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var url = await UrlAsync();
        using var response = await _http.PostAsJsonAsync(Combine(url, ChatRoutes.Login), new AuthRequest
        {
            Username = username,
            Password = password
        }, ChatJson.Options, ct);
        return await ReadAuthAsync(response, ct);
    }

    public async Task SaveSessionAsync(AuthResponse auth)
    {
        UserId = auth.UserId;
        DisplayName = auth.DisplayName;
        Username = auth.Username;
        await _settings.SetAsync(SettingKeys.ChatServerToken, auth.Token);
        await _settings.SetAsync(SettingKeys.ChatServerUserId, auth.UserId);
        await _settings.SetAsync(SettingKeys.ChatServerUsername, auth.Username);
        await _settings.SetAsync(SettingKeys.ChatServerDisplayName, auth.DisplayName);
        await _settings.SetBoolAsync(SettingKeys.ChatServerEnabled, true);
    }

    public async Task<IReadOnlyList<CompanyOptionDto>> ListCompaniesAsync(CancellationToken ct = default)
    {
        var url = await UrlAsync();
        using var response = await _http.GetAsync(Combine(url, ChatRoutes.OrgCompanies), ct);
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<CompanyListResponse>(ChatJson.Options, ct);
        return list?.Items ?? [];
    }

    public async Task<OrgCatalogResponse> GetCatalogAsync(Guid companyId, CancellationToken ct = default)
    {
        var url = await UrlAsync();
        using var response = await _http.GetAsync(Combine(url, ChatRoutes.OrgCatalog + "?companyId=" + companyId), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<OrgCatalogResponse>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Şirket kataloğu alınamadı.");
    }

    public async Task<OrgTeamResponse> GetTeamAsync(CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.OrgTeam);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<OrgTeamResponse>(ChatJson.Options, ct)
               ?? new OrgTeamResponse();
    }

    public async Task<WorkTaskDto> AssignWorkAsync(WorkTaskCreateRequest body, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.OrgWorkTasks);
        req.Content = JsonContent.Create(body, options: ChatJson.Options);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<WorkTaskDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Görev kaydı alınamadı.");
    }

    public async Task<WorkTaskDto> DistributeWorkAsync(WorkTaskDistributeRequest body, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.OrgDistribute);
        req.Content = JsonContent.Create(body, options: ChatJson.Options);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<WorkTaskDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Dağıtım kaydı alınamadı.");
    }

    public async Task<IReadOnlyList<WorkTaskDto>> ListWorkInboxAsync(CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.OrgWorkTasks + "?scope=inbox");
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await response.Content.ReadFromJsonAsync<List<WorkTaskDto>>(ChatJson.Options, ct) ?? [];
    }

    public async Task<InvitePreviewDto> PreviewInviteAsync(string code, CancellationToken ct = default)
    {
        var url = await UrlAsync();
        using var response = await _http.GetAsync(Combine(url, ChatRoutes.OrgInvite + "?code=" + Uri.EscapeDataString(code.Trim())), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<InvitePreviewDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Davet okunamadı.");
    }

    public async Task<OrgLeaveBoardResponse> GetLeaveBoardAsync(CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.OrgLeaves);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<OrgLeaveBoardResponse>(ChatJson.Options, ct)
               ?? new OrgLeaveBoardResponse();
    }

    public async Task<OrgLeaveDto> CreateLeaveAsync(OrgLeaveCreateRequest body, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.OrgLeaves);
        req.Content = JsonContent.Create(body, options: ChatJson.Options);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<OrgLeaveDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("İzin talebi alınamadı.");
    }

    public async Task<OrgLeaveDto> DecideLeaveAsync(Guid id, bool approve, string? note = null, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.OrgLeaveDecide);
        req.Content = JsonContent.Create(new OrgLeaveDecideRequest { Id = id, Approve = approve, Note = note }, options: ChatJson.Options);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<OrgLeaveDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Karar kaydı alınamadı.");
    }

    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.OrgAudit);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await response.Content.ReadFromJsonAsync<List<AuditEventDto>>(ChatJson.Options, ct) ?? [];
    }

    public async Task<WorkFileDto> UploadWorkFileAsync(Guid taskId, string path, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Post, ChatRoutes.OrgWorkTasks + "/" + taskId.ToString("D") + "/files");
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(path);
        var part = new StreamContent(stream);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "file", Path.GetFileName(path));
        req.Content = content;
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<WorkFileDto>(ChatJson.Options, ct)
               ?? throw new InvalidOperationException("Dosya kaydı alınamadı.");
    }

    public async Task<string> DownloadWorkFileAsync(Guid fileId, string fileName, CancellationToken ct = default)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.OrgFile + "/" + fileId.ToString("D"));
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        }

        var safe = string.Concat((fileName ?? "dosya").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var target = Path.Combine(AppPaths.AttachmentsDirectory, "org-" + fileId.ToString("N")[..8] + "-" + safe);
        await using var fs = File.Create(target);
        await response.Content.CopyToAsync(fs, ct);
        return target;
    }

    public async Task ClearSessionAsync()
    {
        UserId = "";
        DisplayName = "";
        Username = "";
        await _settings.SetAsync(SettingKeys.ChatServerToken, "");
        await _settings.SetAsync(SettingKeys.ChatServerUserId, "");
        await StopAsync();
    }

    public async Task SyncThreadAsync(string peerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(await TokenAsync()))
        {
            return;
        }

        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.Messages + "?peer=" + Uri.EscapeDataString(peerKey));
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var list = await response.Content.ReadFromJsonAsync<MessageListResponse>(ChatJson.Options, ct);
        if (list is null)
        {
            return;
        }

        foreach (var dto in list.Items)
        {
            var msg = FromDto(dto, UserId);
            if (CollabPayload.IsHidden(msg.Body))
            {
                await _store.ApplyHiddenIfNewAsync(msg, ct);
            }
            else
            {
                await _store.SaveAsync(msg, ct);
            }
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _http.Dispose();
        _sendLock.Dispose();
        _ws?.Dispose();
    }

    private async Task RestoreSessionAsync()
    {
        UserId = await _settings.GetAsync(SettingKeys.ChatServerUserId, "");
        Username = await _settings.GetAsync(SettingKeys.ChatServerUsername, "");
        DisplayName = await _settings.GetAsync(SettingKeys.ChatServerDisplayName, "");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var delay = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Status = "Bağlanıyor…";
                Changed?.Invoke();
                await ConnectAsync(ct);
                delay = 1000;
                Status = "Bağlı";
                IsConnected = true;
                Changed?.Invoke();
                await ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Status = "Kopuk: " + Short(ex.Message);
                Changed?.Invoke();
            }

            await CloseSocketAsync();
            IsConnected = false;
            try { await Task.Delay(delay, ct); } catch { return; }
            delay = Math.Min(delay * 2, 30_000);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var token = await TokenAsync();
        var url = await UrlAsync();
        using (var health = new HttpRequestMessage(HttpMethod.Get, Combine(url, ChatRoutes.Health)))
        using (var healthResponse = await _http.SendAsync(health, ct))
        {
            healthResponse.EnsureSuccessStatusCode();
        }

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(ChatCodec.ToWebSocketUri(url, token), ct);
        _ws = socket;
        await RefreshDirectoryAsync(ct);
    }

    private async Task ReceiveAsync(CancellationToken ct)
    {
        if (_ws is null) return;
        var buffer = new byte[ChatRoutes.MaxFrameBytes];
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var envelope = ChatCodec.Parse(json);
            if (envelope is null)
            {
                continue;
            }

            switch (envelope.Type)
            {
                case ChatTypes.Presence:
                    ApplyPresence(ChatCodec.Payload<PresenceSnapshot>(envelope));
                    break;
                case ChatTypes.Message:
                    var dto = ChatCodec.Payload<ChatMessageDto>(envelope);
                    if (dto is not null)
                    {
                        var msg = FromDto(dto, UserId);
                        await _store.SaveAsync(msg, ct);
                        MessageReceived?.Invoke(msg);
                        Changed?.Invoke();
                    }

                    break;
                case ChatTypes.WorkTask:
                    var work = ChatCodec.Payload<WorkTaskDto>(envelope);
                    if (work is not null)
                    {
                        WorkAssigned?.Invoke(work);
                    }

                    break;
                case ChatTypes.Leave:
                    var leave = ChatCodec.Payload<OrgLeaveDto>(envelope);
                    if (leave is not null)
                    {
                        LeaveUpdated?.Invoke(leave);
                    }

                    break;
                case ChatTypes.Ping:
                    await SendFrameAsync(ChatCodec.Pack(ChatTypes.Pong, new { }), ct);
                    break;
            }
        }
    }

    private async Task RefreshDirectoryAsync(CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, ChatRoutes.Users);
        using var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var snapshot = await response.Content.ReadFromJsonAsync<PresenceSnapshot>(ChatJson.Options, ct);
        ApplyPresence(snapshot);
    }

    private void ApplyPresence(PresenceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        lock (_peerLock)
        {
            _peers.Clear();
            foreach (var user in snapshot.Users)
            {
                if (user.UserId == UserId)
                {
                    continue;
                }

                _peers[user.UserId] = new ChatPeer
                {
                    Key = user.UserId,
                    Name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                    Username = user.Username,
                    Kind = ChatPeerKind.Server,
                    IsOnline = user.Online,
                    LastSeen = DateTime.Now
                };
            }
        }

        Changed?.Invoke();
    }

    public void RememberPeer(DirectoryUser user)
    {
        if (string.IsNullOrWhiteSpace(user.UserId) || user.UserId == UserId)
        {
            return;
        }

        lock (_peerLock)
        {
            _peers[user.UserId] = new ChatPeer
            {
                Key = user.UserId,
                Name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                Username = user.Username,
                Kind = ChatPeerKind.Server,
                IsOnline = user.Online,
                LastSeen = DateTime.Now
            };
        }

        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<DirectoryUser>> SearchDirectoryAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(await TokenAsync()) || string.IsNullOrWhiteSpace(await UrlAsync()))
        {
            return [];
        }

        var snapshot = await FetchDirectoryAsync(ChatRoutes.UsersSearch + "?q=" + Uri.EscapeDataString(query), ct)
                       ?? await FetchDirectoryAsync(ChatRoutes.Users + "?q=" + Uri.EscapeDataString(query), ct);
        var users = snapshot?.Users ?? [];
        return users
            .Where(u => u.UserId != UserId
                        && (u.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(u => u.Username.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
                : u.Username.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(u => u.Username)
            .Take(50)
            .ToList();
    }

    private async Task<PresenceSnapshot?> FetchDirectoryAsync(string path, CancellationToken ct)
    {
        using var req = await AuthorizedAsync(HttpMethod.Get, path);
        using var response = await _http.SendAsync(req, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            ErrorResponse? err = null;
            try { err = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(json, ChatJson.Options); }
            catch { /* ignore */ }
            throw new InvalidOperationException(err?.Error ?? $"Sunucu {((int)response.StatusCode)}");
        }

        return System.Text.Json.JsonSerializer.Deserialize<PresenceSnapshot>(json, ChatJson.Options);
    }

    private async Task SendFrameAsync(string json, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open })
        {
            throw new InvalidOperationException("Sunucu bağlantısı yok.");
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task CloseSocketAsync()
    {
        var socket = _ws;
        _ws = null;
        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
        catch
        {
            // yut
        }

        socket.Dispose();
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Combine(await UrlAsync(), path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        return req;
    }

    private async Task<string> UrlAsync()
        => (await _settings.GetAsync(SettingKeys.ChatServerUrl, ChatRoutes.DefaultClientUrl)).Trim().TrimEnd('/');

    private Task<string> TokenAsync() => _settings.GetAsync(SettingKeys.ChatServerToken, "");

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var err = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(json, ChatJson.Options);
            if (!string.IsNullOrWhiteSpace(err?.Error))
            {
                return err.Error;
            }
        }
        catch
        {
            // düz metin
        }

        return "Sunucu " + (int)response.StatusCode;
    }

    private static async Task<AuthResponse> ReadAuthAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            ErrorResponse? err = null;
            try { err = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(json, ChatJson.Options); }
            catch { /* ignore */ }
            throw new InvalidOperationException(err?.Error ?? $"Sunucu {((int)response.StatusCode)}");
        }

        var auth = System.Text.Json.JsonSerializer.Deserialize<AuthResponse>(json, ChatJson.Options);
        if (auth is null || string.IsNullOrWhiteSpace(auth.Token))
        {
            throw new InvalidOperationException("Sunucu oturumu alınamadı.");
        }

        return auth;
    }

    private static string Combine(string baseUrl, string path)
        => baseUrl.TrimEnd('/') + path;

    private static string Short(string message)
        => message.Length <= 80 ? message : message[..80] + "…";

    private static ChatMessageDto ToDto(ChatMessage message) => new()
    {
        Id = message.Id,
        FromUserId = message.FromKey,
        ToUserId = message.ToKey,
        FromName = message.FromName,
        Body = message.Body,
        SentAt = message.SentAt.ToUniversalTime()
    };

    public static ChatMessage FromDto(ChatMessageDto dto, string meKey)
    {
        var sent = dto.SentAt;
        if (sent.Kind == DateTimeKind.Utc)
        {
            sent = sent.ToLocalTime();
        }

        return new ChatMessage
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            ConversationKey = ChatStore.ConversationKey(dto.FromUserId, dto.ToUserId),
            FromKey = dto.FromUserId,
            ToKey = dto.ToUserId,
            FromName = dto.FromName,
            Body = dto.Body,
            SentAt = sent,
            IsOutgoing = dto.FromUserId == meKey
        };
    }
}
