using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.App.Views;
using Planner.Chat;
using Planner.Core.Models;
using Planner.Core.Services;
using WpfAlign = System.Windows.HorizontalAlignment;

namespace Planner.App.ViewModels;

public sealed class ChatBubbleVm
{
    public required Guid Id { get; init; }
    public required string FromName { get; init; }
    public required string Body { get; init; }
    public required string DisplayText { get; init; }
    public required string TimeText { get; init; }
    public required bool IsOutgoing { get; init; }
    public required string FromKey { get; init; }
    public bool IsImage { get; init; }
    public bool IsFile { get; init; }
    public string FileName { get; init; } = "";
    public BitmapImage? Picture { get; init; }
    public bool ShowAccept { get; init; }
    public bool CanEdit { get; init; }
    public bool CanReact { get; init; }
    public bool IThumbed { get; init; }
    public int ThumbCount { get; init; }
    public bool IsEdited { get; init; }
    public bool ShowText => !IsImage && !IsFile;
    public string ThumbLabel => ThumbCount == 0 ? "👍" : "👍 " + ThumbCount;
    public WpfAlign Align => IsOutgoing ? WpfAlign.Right : WpfAlign.Left;
}

public sealed class PendingFriendVm
{
    public required string Key { get; init; }
    public required string Name { get; init; }
}

public sealed class UserSearchHit
{
    public required string Key { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Source { get; init; }
    public required ChatPeerKind Kind { get; init; }
    public bool Online { get; init; }
    public bool AlreadyFriend { get; set; }
    public bool AlreadyPending { get; set; }
    public bool CanRequest => !AlreadyFriend && !AlreadyPending;
    public string ActionLabel => AlreadyFriend ? "Arkadaş" : AlreadyPending ? "Bekliyor" : "İstek gönder";
    public string Detail => "@" + Username + " · " + Source + (Online ? " · çevrimiçi" : "");
}

public partial class ChatViewModel : ObservableObject
{
    private readonly ChatStore _store;
    private readonly UserAccountService _users;
    private readonly ChatHub _hub;
    private readonly FriendshipService _friends;
    private readonly CallService _calls;
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly DocumentService _docs;
    private readonly IAppDialogs _dialogs;
    private readonly IReminderNotifier _toast;
    private readonly Dictionary<string, TaskCompletionSource<AgendaSignal>> _agendaWait = new(StringComparer.Ordinal);
    private bool _suppressPeerLoad;
    private CallWindow? _callWindow;

    public ChatViewModel(
        ChatStore store,
        UserAccountService users,
        ChatHub hub,
        FriendshipService friends,
        CallService calls,
        TaskService tasks,
        CategoryService categories,
        DocumentService docs,
        IAppDialogs dialogs,
        IReminderNotifier toast)
    {
        _store = store;
        _users = users;
        _hub = hub;
        _friends = friends;
        _calls = calls;
        _tasks = tasks;
        _categories = categories;
        _docs = docs;
        _dialogs = dialogs;
        _toast = toast;
        _hub.Changed += () => Dispatch(() =>
        {
            _ = RefreshPeersAsync();
            _ = LoadPendingAsync();
            ConnectionHint = ConnectionText();
            _ = LoadThreadAsync();
        });
        _hub.MessageReceived += message => Dispatch(() => _ = OnIncomingAsync(message));
        _calls.IncomingInvite += (signal, message) => Dispatch(() => _ = OnIncomingCallAsync(signal, message));
        _calls.Ended += () => Dispatch(() =>
        {
            if (_callWindow is { IsVisible: true })
            {
                _callWindow.Close();
            }

            _callWindow = null;
        });
    }

    public CallService Calls => _calls;
    public ObservableCollection<ChatPeer> People { get; } = new();
    public ObservableCollection<ChatBubbleVm> Messages { get; } = new();
    public ObservableCollection<UserSearchHit> SearchHits { get; } = new();
    public ObservableCollection<PendingFriendVm> PendingRequests { get; } = new();

    [ObservableProperty] private ChatPeer? _selectedPeer;
    [ObservableProperty] private string _draft = "";
    [ObservableProperty] private string _header = "Sohbet";
    [ObservableProperty] private string _meName = "Ben";
    [ObservableProperty] private bool _isEmptyThread = true;
    [ObservableProperty] private bool _hasPeer;
    [ObservableProperty] private bool _hasPeople;
    [ObservableProperty] private bool _isFriend;
    [ObservableProperty] private bool _canMessage;
    [ObservableProperty] private string _connectionHint = "";
    [ObservableProperty] private string _userQuery = "";
    [ObservableProperty] private bool _hasSearchHits;
    [ObservableProperty] private string _searchStatus = "Kullanıcı adı yazıp arayın. Sunucudaki kayıtlı kişiler bulunur.";
    [ObservableProperty] private bool _hasPendingRequests;
    [ObservableProperty] private bool _isPeerWork;
    [ObservableProperty] private bool _isPeerPersonal;
    [ObservableProperty] private string _peerClassLabel = "";
    [ObservableProperty] private Guid? _editingMessageId;
    [ObservableProperty] private bool _emojiOpen;

    public bool IsEditing => EditingMessageId is not null;
    public string SendLabel => IsEditing ? "Kaydet" : "Gönder";
    public IReadOnlyList<string> Emojis { get; } = ChatEmoji.All;

    public async Task LoadAsync()
    {
        MeName = _hub.DisplayName;
        ConnectionHint = ConnectionText();
        var locals = await _users.ListAsync();
        _hub.AddLocalUsers(locals);
        await RefreshPeersAsync();
        await LoadPendingAsync();
        await LoadThreadAsync();
    }

    [RelayCommand]
    private async Task SearchUsersAsync()
    {
        var q = (UserQuery ?? "").Trim();
        SearchHits.Clear();
        HasSearchHits = false;
        if (q.Length < 2)
        {
            SearchStatus = "En az 2 karakter yazın.";
            return;
        }

        SearchStatus = "Aranıyor…";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var me = _users.Current;
        var locals = (await _users.ListAsync())
            .Where(user => me is null || user.Id != me.Id)
            .Where(user => user.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                           || user.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(user => user.Username.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0
                : user.Username.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(user => user.Username)
            .ToList();
        foreach (var user in locals)
        {
            var key = user.Id.ToString("N");
            seen.Add(key);
            SearchHits.Add(await ToHitAsync(key, user.Username, user.DisplayName, "Bu bilgisayar", ChatPeerKind.Local, false));
        }

        var serverNote = "";
        try
        {
            foreach (var user in await _hub.SearchServerUsersAsync(q))
            {
                if (!seen.Add(user.UserId))
                {
                    continue;
                }

                SearchHits.Add(await ToHitAsync(
                    user.UserId,
                    user.Username,
                    string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                    "Sunucu",
                    ChatPeerKind.Server,
                    user.Online));
            }
        }
        catch (Exception ex)
        {
            serverNote = " Sunucu: " + ex.Message;
        }

        HasSearchHits = SearchHits.Count > 0;
        SearchStatus = HasSearchHits
            ? SearchHits.Count + " kişi bulundu." + serverNote
            : (_hub.ServerConnected || !string.IsNullOrWhiteSpace(_hub.Server.UserId)
                ? "Bu kullanıcı adıyla kayıt yok."
                : "Yerelde yok. Ayarlar’dan sohbet sunucusuna bağlanınca sunucu tablosu da aranır.") + serverNote;
    }

    private async Task<UserSearchHit> ToHitAsync(
        string key, string username, string displayName, string source, ChatPeerKind kind, bool online)
    {
        var status = await _friends.GetStatusByKeyAsync(_users.CurrentKey, key);
        return new UserSearchHit
        {
            Key = key,
            Username = username,
            DisplayName = displayName,
            Source = source,
            Kind = kind,
            Online = online,
            AlreadyFriend = status == FriendshipStatus.Accepted,
            AlreadyPending = status == FriendshipStatus.Pending
        };
    }

    [RelayCommand]
    private async Task RequestByHitAsync(UserSearchHit? hit)
    {
        if (hit is null || hit.AlreadyFriend)
        {
            ConnectionHint = hit is null ? "" : "Zaten arkadaşsınız.";
            return;
        }

        var peer = new ChatPeer
        {
            Key = hit.Key,
            Name = hit.DisplayName,
            Username = hit.Username,
            Kind = hit.Kind,
            IsOnline = hit.Online,
            LastSeen = DateTime.Now
        };
        if (hit.Kind == ChatPeerKind.Server)
        {
            _hub.RememberServerUser(new DirectoryUser
            {
                UserId = hit.Key,
                Username = hit.Username,
                DisplayName = hit.DisplayName,
                Online = hit.Online
            });
        }

        try
        {
            await _friends.RequestByKeyAsync(hit.Key);
            var payload = JsonSerializer.Serialize(new FriendSignal { T = "req", Name = _hub.DisplayName });
            await _hub.SendAsync(peer, CollabPayload.Friend + payload, bypassFriendCheck: true);
            ConnectionHint = "@" + hit.Username + " kullanıcısına istek gönderildi.";
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await RefreshPeersAsync();
        SelectedPeer = People.FirstOrDefault(p => p.Key == hit.Key);
        await SearchUsersAsync();
        await LoadThreadAsync();
    }

    partial void OnEditingMessageIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SendLabel));
    }

    partial void OnSelectedPeerChanged(ChatPeer? value)
    {
        HasPeer = value is not null;
        CancelEdit();
        if (!_suppressPeerLoad)
        {
            _ = OpenPeerAsync();
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (SelectedPeer is null || string.IsNullOrWhiteSpace(Draft) || !IsFriend)
        {
            if (SelectedPeer is not null && !IsFriend)
            {
                ConnectionHint = "Mesaj yalnızca arkadaşlar arasında.";
            }

            return;
        }

        if (EditingMessageId is { } editId)
        {
            var text = Draft.Trim();
            Draft = "";
            EditingMessageId = null;
            try
            {
                await _store.ApplyEditAsync(editId, text);
                var payload = JsonSerializer.Serialize(new EditSignal { Id = editId, Body = text });
                await _hub.SendAsync(SelectedPeer, CollabPayload.Edit + payload);
            }
            catch (Exception ex)
            {
                ConnectionHint = ex.Message;
            }

            await LoadThreadAsync();
            return;
        }

        var outgoing = Draft;
        Draft = "";
        try
        {
            await _hub.SendAsync(SelectedPeer, outgoing);
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await LoadThreadAsync();
    }

    [RelayCommand]
    private async Task SendImageAsync()
    {
        if (SelectedPeer is null || !IsFriend)
        {
            ConnectionHint = "Resim yalnızca arkadaşlarla paylaşılır.";
            return;
        }

        var path = _dialogs.OpenFile("Görseller|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Tüm dosyalar|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            await _hub.SendAsync(SelectedPeer, ChatImaging.PrepareBody(path));
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await LoadThreadAsync();
    }

    [RelayCommand]
    private async Task SendFileAsync()
    {
        if (SelectedPeer is null || !IsFriend)
        {
            ConnectionHint = "Dosya yalnızca arkadaşlarla paylaşılır.";
            return;
        }

        var path = _dialogs.OpenAnyFile();
        if (path is null)
        {
            return;
        }

        try
        {
            await _hub.SendAsync(SelectedPeer, ChatImaging.PrepareFileBody(path));
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await LoadThreadAsync();
    }

    [RelayCommand]
    private void InsertEmoji(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji) || !CanMessage)
        {
            return;
        }

        Draft += emoji;
        EmojiOpen = false;
    }

    [RelayCommand]
    private void ToggleEmoji() => EmojiOpen = !EmojiOpen;

    [RelayCommand]
    private void StartEdit(ChatBubbleVm? bubble)
    {
        if (bubble is null || !bubble.CanEdit)
        {
            return;
        }

        EditingMessageId = bubble.Id;
        Draft = bubble.DisplayText;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (EditingMessageId is null)
        {
            return;
        }

        EditingMessageId = null;
        Draft = "";
    }

    [RelayCommand]
    private async Task ToggleThumbAsync(ChatBubbleVm? bubble)
    {
        if (bubble is null || !bubble.CanReact || SelectedPeer is null || !IsFriend)
        {
            return;
        }

        var me = SelectedPeer.Kind == ChatPeerKind.Server ? _hub.Server.UserId : _users.CurrentKey;
        try
        {
            await _store.ToggleThumbAsync(bubble.Id, me);
            var payload = JsonSerializer.Serialize(new ReactSignal { Id = bubble.Id });
            await _hub.SendAsync(SelectedPeer, CollabPayload.React + payload);
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await LoadThreadAsync();
    }

    [RelayCommand]
    private void OpenChatFile(ChatBubbleVm? bubble)
    {
        if (bubble is null || !bubble.IsFile)
        {
            return;
        }

        var path = ChatImaging.MaterializeFile(bubble.Body);
        if (path is null)
        {
            ConnectionHint = "Dosya bulunamadı.";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SetPeerWorkAsync() => await SetPeerClassAsync(FriendClassKind.Work);

    [RelayCommand]
    private async Task SetPeerPersonalAsync() => await SetPeerClassAsync(FriendClassKind.Personal);

    private async Task SetPeerClassAsync(FriendClassKind kind)
    {
        if (SelectedPeer is null || !FriendshipService.TryKey(SelectedPeer.Key, out var id) || !IsFriend)
        {
            return;
        }

        try
        {
            await _friends.SetClassByPeerAsync(id, kind);
            ConnectionHint = kind == FriendClassKind.Work
                ? "İş olarak sınıflandırıldı — ajandada yalnızca İş görevleri görünür."
                : "Özel olarak sınıflandırıldı — ajanda paylaşılmaz.";
            await RefreshPeerClassAsync();
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddFriendAsync()
    {
        if (SelectedPeer is null)
        {
            return;
        }

        try
        {
            await _friends.RequestByKeyAsync(SelectedPeer.Key);
            var payload = JsonSerializer.Serialize(new FriendSignal { T = "req", Name = _hub.DisplayName });
            await _hub.SendAsync(SelectedPeer, CollabPayload.Friend + payload, bypassFriendCheck: true);
            ConnectionHint = "Arkadaşlık isteği gönderildi.";
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await RefreshPeersAsync();
        await LoadThreadAsync();
    }

    [RelayCommand]
    private async Task AcceptFriendAsync(ChatBubbleVm? bubble)
        => await RespondToRequestAsync(bubble?.FromKey ?? SelectedPeer?.Key, bubble?.FromName ?? SelectedPeer?.Name, true, true);

    [RelayCommand]
    private async Task DeclineFriendAsync(ChatBubbleVm? bubble)
        => await RespondToRequestAsync(bubble?.FromKey ?? SelectedPeer?.Key, bubble?.FromName ?? SelectedPeer?.Name, false, false);

    [RelayCommand]
    private async Task AcceptPendingAsync(PendingFriendVm? item)
        => await RespondToRequestAsync(item?.Key, item?.Name, true, true);

    [RelayCommand]
    private async Task DeclinePendingAsync(PendingFriendVm? item)
        => await RespondToRequestAsync(item?.Key, item?.Name, false, false);

    public async Task HandleFriendToastAsync(string action, string peerKey, string name)
    {
        if (string.Equals(action, "friendDecline", StringComparison.OrdinalIgnoreCase))
        {
            await RespondToRequestAsync(peerKey, name, false, false);
            return;
        }

        if (string.Equals(action, "friendAccept", StringComparison.OrdinalIgnoreCase))
        {
            await RespondToRequestAsync(peerKey, name, true, false);
            return;
        }

        EnsurePeer(peerKey, name);
        await RefreshPeersAsync();
        await LoadPendingAsync();
        SelectedPeer = People.FirstOrDefault(p => p.Key == peerKey);
        await LoadThreadAsync();
    }

    private async Task RespondToRequestAsync(string? key, string? name, bool accept, bool askAgenda)
    {
        if (string.IsNullOrWhiteSpace(key) || !FriendshipService.TryKey(key, out var id))
        {
            return;
        }

        var peer = EnsurePeer(key, name ?? "Kişi");
        try
        {
            if (accept)
            {
                var pick = askAgenda
                    ? _dialogs.PickIndex(
                        "Sınıflandırma",
                        "Bu kişiyi iş mi özel mi olarak görüyorsunuz? İş: ajandada yalnızca İş görevleri. Özel: ajanda görünmez.",
                        ["İş", "Özel"])
                    : 1;
                if (askAgenda && pick < 0)
                {
                    return;
                }

                var kind = pick == 0 ? FriendClassKind.Work : FriendClassKind.Personal;
                await _friends.AcceptFromPeerAsync(id, kind == FriendClassKind.Work);
                await _friends.SetClassByPeerAsync(id, kind);
                var payload = JsonSerializer.Serialize(new FriendSignal
                {
                    T = "ok",
                    Name = _hub.DisplayName,
                    CanViewAgenda = kind == FriendClassKind.Work
                });
                await _hub.SendAsync(peer, CollabPayload.Friend + payload, bypassFriendCheck: true);
                ConnectionHint = kind == FriendClassKind.Work
                    ? "Arkadaş eklendi · İş."
                    : "Arkadaş eklendi · Özel.";
            }
            else
            {
                await _friends.DeclineFromPeerAsync(id);
                var payload = JsonSerializer.Serialize(new FriendSignal { T = "no", Name = _hub.DisplayName });
                await _hub.SendAsync(peer, CollabPayload.Friend + payload, bypassFriendCheck: true);
                ConnectionHint = "İstek reddedildi.";
            }
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        await RefreshPeersAsync();
        await LoadPendingAsync();
        await LoadThreadAsync();
    }

    private ChatPeer EnsurePeer(string key, string name)
    {
        var found = _hub.FindPeer(key);
        if (found is not null)
        {
            return found;
        }

        if (!string.IsNullOrWhiteSpace(_hub.Server.UserId))
        {
            _hub.RememberServerUser(new DirectoryUser
            {
                UserId = key,
                Username = "",
                DisplayName = name,
                Online = false
            });
            return _hub.FindPeer(key) ?? new ChatPeer
            {
                Key = key,
                Name = name,
                Kind = ChatPeerKind.Server,
                LastSeen = DateTime.Now
            };
        }

        return _hub.PeerFor(key, name);
    }

    [RelayCommand]
    private async Task CallAudioAsync() => await StartCallAsync("audio");

    [RelayCommand]
    private async Task CallVideoAsync() => await StartCallAsync("video");

    [RelayCommand]
    private async Task AssignTaskAsync()
    {
        if (SelectedPeer is null || !IsFriend)
        {
            ConnectionHint = "Görev yalnızca arkadaşa atanır.";
            return;
        }

        var title = _dialogs.Prompt("Görev ata", "Görev başlığı");
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        FriendshipService.TryKey(SelectedPeer.Key, out var to);
        var category = (await _categories.GetAllAsync()).FirstOrDefault()
                       ?? throw new InvalidOperationException("Kategori yok.");
        var date = DateOnly.FromDateTime(DateTime.Today);
        await _tasks.AddAsync(new PlannerTask
        {
            Title = title.Trim(),
            CategoryId = category.Id,
            Date = date,
            AssignedToUserId = to == Guid.Empty ? null : to,
            AssignedByUserId = _users.Current?.Id
        });
        try
        {
            var payload = JsonSerializer.Serialize(new TaskSignal
            {
                T = "assign",
                Title = title.Trim(),
                Date = date.ToString("yyyy-MM-dd")
            });
            await _hub.SendAsync(SelectedPeer, CollabPayload.Task + payload);
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }

        _toast.ShowInfo("Görev atandı", title.Trim());
        await LoadThreadAsync();
    }

    private async Task StartCallAsync(string mode)
    {
        if (SelectedPeer is null || !IsFriend)
        {
            ConnectionHint = "Arama yalnızca arkadaşlar arasında.";
            return;
        }

        if (SelectedPeer.Kind == ChatPeerKind.Local)
        {
            ConnectionHint = "Aynı bilgisayarda iki Yaver açılamaz; arama yerel ağ veya sunucu ile yapılır.";
            return;
        }

        try
        {
            await _calls.StartCallAsync(SelectedPeer, mode);
            ShowCallWindow();
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ViewAgendaAsync()
    {
        if (SelectedPeer is null || !IsFriend)
        {
            ConnectionHint = "Ajanda yalnızca arkadaşlar için.";
            return;
        }

        if (!FriendshipService.TryKey(SelectedPeer.Key, out var ownerId) || _users.Current is null)
        {
            return;
        }

        if (SelectedPeer.Kind == ChatPeerKind.Local)
        {
            var kind = await _friends.GetClassByKeyAsync(SelectedPeer.Key, _users.CurrentKey);
            var from = DateOnly.FromDateTime(DateTime.Today);
            var occ = kind == FriendClassKind.Work
                ? (await _tasks.GetOccurrencesRangeAsync(from, from.AddDays(13), agendaOwnerId: ownerId)).Where(IsWorkTask)
                : [];
            var lines = occ.Select(o => new AgendaLine
            {
                Title = o.Task.Title,
                When = o.Date.ToString("d MMMM", new System.Globalization.CultureInfo("tr-TR"))
                       + (o.Task.Time is { } t ? " · " + t.ToString("HH\\:mm") : "")
            });
            var hint = kind == FriendClassKind.Work
                ? "Salt okunur. Yalnızca İş görevleri."
                : "Özel olarak sınıflandırılmış; ajanda paylaşılmıyor.";
            new FriendAgendaWindow(SelectedPeer.Name, lines, hint).Show();
            return;
        }

        var wait = new TaskCompletionSource<AgendaSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        _agendaWait[SelectedPeer.Key] = wait;
        try
        {
            await _hub.SendAsync(SelectedPeer, CollabPayload.Agenda + JsonSerializer.Serialize(new AgendaSignal { T = "ask" }));
            var completed = await Task.WhenAny(wait.Task, Task.Delay(8000));
            if (completed != wait.Task)
            {
                _dialogs.Info("Ajanda yanıtı gelmedi. Karşı tarafın Yaver’ı açık ve izin vermiş olmalı.");
                return;
            }

            var data = await wait.Task;
            new FriendAgendaWindow(
                SelectedPeer.Name,
                FriendAgendaWindow.FromSignals(data.Items ?? []),
                data.Hint).Show();
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
        finally
        {
            _agendaWait.Remove(SelectedPeer.Key);
        }
    }

    [RelayCommand]
    private async Task AllowAgendaAsync()
    {
        if (SelectedPeer is null || !FriendshipService.TryKey(SelectedPeer.Key, out var id))
        {
            return;
        }

        var allow = _dialogs.Confirm("Ajandanızı bu arkadaş görüntüleyebilsin mi?", "Ajanda izni");
        try
        {
            await _friends.SetAgendaPermissionAsync(id, allow);
            ConnectionHint = allow ? "Ajanda paylaşımı açık." : "Ajanda paylaşımı kapalı.";
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
    }

    private async Task OnIncomingCallAsync(CallSignal signal, ChatMessage message)
    {
        var name = string.IsNullOrWhiteSpace(signal.Name) ? message.FromName : signal.Name;
        if (!_dialogs.Confirm($"{name} sizi arıyor. Cevapla?", "Arama"))
        {
            return;
        }

        var peer = _hub.PeerFor(message.FromKey, name);
        try
        {
            await _calls.AcceptAsync(signal, peer);
            ShowCallWindow();
        }
        catch (Exception ex)
        {
            ConnectionHint = ex.Message;
        }
    }

    private void ShowCallWindow()
    {
        if (_callWindow is { IsVisible: true })
        {
            _callWindow.Activate();
            return;
        }

        _callWindow = new CallWindow(_calls);
        _callWindow.Closed += (_, _) => _callWindow = null;
        _callWindow.Show();
        _callWindow.Activate();
    }

    private async Task OnIncomingAsync(ChatMessage message)
    {
        if (message.IsOutgoing)
        {
            return;
        }

        try
        {
            if (CollabPayload.IsFriend(message.Body))
            {
                var signal = JsonSerializer.Deserialize<FriendSignal>(message.Body[CollabPayload.Friend.Length..]);
                if (signal is null)
                {
                    return;
                }

                if (FriendshipService.TryKey(message.FromKey, out var from))
                {
                    EnsurePeer(message.FromKey, message.FromName);
                    if (signal.T == "req")
                    {
                        if (await _friends.IncomingRequestAsync(from))
                        {
                            _toast.ShowFriendRequest(message.FromKey, message.FromName);
                        }
                    }
                    else if (signal.T == "ok")
                    {
                        await _friends.AcceptFromPeerAsync(from, signal.CanViewAgenda);
                        _toast.ShowInfo("Arkadaş eklendi", message.FromName);
                    }
                    else if (signal.T == "no")
                    {
                        await _friends.DeclineFromPeerAsync(from);
                        _toast.ShowInfo("Arkadaşlık isteği reddedildi", message.FromName);
                    }
                }

                await RefreshPeersAsync();
                await LoadPendingAsync();
            }
            else if (CollabPayload.IsTask(message.Body))
            {
                var signal = JsonSerializer.Deserialize<TaskSignal>(message.Body[CollabPayload.Task.Length..]);
                if (signal is { T: "assign" } && !string.IsNullOrWhiteSpace(signal.Title))
                {
                    DateOnly.TryParse(signal.Date, out var date);
                    if (date == default)
                    {
                        date = DateOnly.FromDateTime(DateTime.Today);
                    }

                    FriendshipService.TryKey(message.FromKey, out var by);
                    var cats = (await _categories.GetAllAsync()).FirstOrDefault();
                    if (cats is null)
                    {
                        return;
                    }

                    await _tasks.AddAsync(new PlannerTask
                    {
                        Title = signal.Title,
                        Notes = signal.Notes,
                        CategoryId = cats.Id,
                        Date = date,
                        OwnerUserId = _users.Current?.Id,
                        AssignedToUserId = _users.Current?.Id,
                        AssignedByUserId = by == Guid.Empty ? null : by
                    });
                    _toast.ShowInfo("Görev atandı", signal.Title);
                }
                else if (signal is { T: "status" })
                {
                    _toast.ShowInfo("Görev durumu", $"{signal.Title} → {signal.Status}");
                }
            }
            else if (CollabPayload.IsShare(message.Body))
            {
                var signal = JsonSerializer.Deserialize<ShareSignal>(message.Body[CollabPayload.Share.Length..]);
                if (signal is not null)
                {
                    await _docs.ImportSharedAsync(signal.Title, (WorkspaceDocumentKind)signal.Kind, signal.Body);
                    _toast.ShowInfo("Dosya paylaşıldı", signal.Title);
                }
            }
            else if (CollabPayload.IsAgenda(message.Body))
            {
                var signal = JsonSerializer.Deserialize<AgendaSignal>(message.Body[CollabPayload.Agenda.Length..]);
                if (signal is null)
                {
                    return;
                }

                if (signal.T == "ask" && _users.Current is not null && FriendshipService.TryKey(message.FromKey, out var viewer))
                {
                    var kind = await _friends.GetClassAsync(_users.Current.Id, viewer);
                    var from = DateOnly.FromDateTime(DateTime.Today);
                    var occ = kind == FriendClassKind.Work
                        ? (await _tasks.GetOccurrencesRangeAsync(from, from.AddDays(13))).Where(IsWorkTask)
                        : [];
                    var reply = new AgendaSignal
                    {
                        T = "data",
                        Hint = kind == FriendClassKind.Work
                            ? "Salt okunur. Yalnızca İş görevleri."
                            : "Özel olarak sınıflandırılmış; ajanda paylaşılmıyor.",
                        Items = occ.Select(o => new AgendaItemSignal
                        {
                            Title = o.Task.Title,
                            When = o.Date.ToString("d MMMM") + (o.Task.Time is { } t ? " · " + t.ToString("HH\\:mm") : "")
                        }).ToList()
                    };
                    var peer = _hub.PeerFor(message.FromKey, message.FromName);
                    await _hub.SendAsync(peer, CollabPayload.Agenda + JsonSerializer.Serialize(reply));
                }
                else if (signal.T == "data" && _agendaWait.TryGetValue(message.FromKey, out var wait))
                {
                    wait.TrySetResult(signal);
                }
            }
            else if (CollabPayload.IsImage(message.Body))
            {
                ChatImaging.Materialize(message.Body);
            }
            else if (CollabPayload.IsFile(message.Body))
            {
                ChatImaging.MaterializeFile(message.Body);
            }
            else if (CollabPayload.IsEdit(message.Body))
            {
                var signal = JsonSerializer.Deserialize<EditSignal>(message.Body[CollabPayload.Edit.Length..]);
                if (signal is not null && signal.Id != Guid.Empty)
                {
                    await _store.ApplyEditAsync(signal.Id, signal.Body ?? "");
                }
            }
            else if (CollabPayload.IsReact(message.Body))
            {
                var signal = JsonSerializer.Deserialize<ReactSignal>(message.Body[CollabPayload.React.Length..]);
                if (signal is not null && signal.Id != Guid.Empty)
                {
                    await _store.ToggleThumbAsync(signal.Id, message.FromKey);
                }
            }
        }
        catch
        {
            // bozuk sinyal
        }

        if (SelectedPeer is not null &&
            (message.FromKey == SelectedPeer.Key || message.ToKey == SelectedPeer.Key))
        {
            await LoadThreadAsync();
        }
    }

    private async Task OpenPeerAsync()
    {
        if (SelectedPeer is { Kind: ChatPeerKind.Server } peer)
        {
            try { await _hub.SyncThreadAsync(peer); }
            catch { /* çevrimdışı önbellek yeter */ }
        }

        await LoadThreadAsync();
        await RefreshPeerClassAsync();
    }

    private async Task RefreshPeersAsync()
    {
        var selected = SelectedPeer?.Key;
        var me = _users.CurrentKey;
        _suppressPeerLoad = true;
        People.Clear();
        foreach (var peer in _hub.SnapshotPeers())
        {
            peer.IsFriend = await _friends.AreFriendsByKeyAsync(me, peer.Key);
            People.Add(peer);
        }

        SelectedPeer = People.FirstOrDefault(p => p.Key == selected);
        _suppressPeerLoad = false;
        HasPeople = People.Count > 0;
        HasPeer = SelectedPeer is not null;
        IsFriend = SelectedPeer is { IsFriend: true };
        CanMessage = IsFriend;
        Header = SelectedPeer is null ? "Sohbet" : SelectedPeer.Name;
        MeName = _hub.DisplayName;
        await RefreshPeerClassAsync();
    }

    private async Task RefreshPeerClassAsync()
    {
        if (SelectedPeer is null || !IsFriend)
        {
            IsPeerWork = false;
            IsPeerPersonal = false;
            PeerClassLabel = "";
            return;
        }

        var kind = await _friends.GetClassByKeyAsync(_users.CurrentKey, SelectedPeer.Key);
        IsPeerWork = kind == FriendClassKind.Work;
        IsPeerPersonal = kind == FriendClassKind.Personal;
        PeerClassLabel = kind == FriendClassKind.Work ? "Sınıf: İş" : "Sınıf: Özel";
    }

    private async Task LoadPendingAsync()
    {
        PendingRequests.Clear();
        foreach (var id in await _friends.ListPendingIncomingAsync())
        {
            var key = id.ToString("N");
            var peer = _hub.FindPeer(key);
            PendingRequests.Add(new PendingFriendVm
            {
                Key = key,
                Name = peer?.Name ?? "Kullanıcı"
            });
        }

        HasPendingRequests = PendingRequests.Count > 0;
    }

    private async Task LoadThreadAsync()
    {
        Messages.Clear();
        if (SelectedPeer is null)
        {
            IsEmptyThread = true;
            Header = "Sohbet";
            HasPeer = false;
            IsFriend = false;
            CanMessage = false;
            IsPeerWork = false;
            IsPeerPersonal = false;
            PeerClassLabel = "";
            return;
        }

        Header = SelectedPeer.Name;
        HasPeer = true;
        IsFriend = SelectedPeer.IsFriend;
        CanMessage = IsFriend;
        var culture = new System.Globalization.CultureInfo("tr-TR");
        var key = _hub.ThreadKey(SelectedPeer);
        var me = SelectedPeer.Kind == ChatPeerKind.Server ? _hub.Server.UserId : _users.CurrentKey;
        foreach (var msg in await _store.GetThreadAsync(key))
        {
            if (CollabPayload.IsHidden(msg.Body))
            {
                continue;
            }

            var outgoing = msg.FromKey == me;
            var isImage = CollabPayload.IsImage(msg.Body);
            var isFile = CollabPayload.IsFile(msg.Body);
            var showAccept = !outgoing && CollabPayload.IsFriend(msg.Body) && !SelectedPeer.IsFriend
                             && IsFriendReq(msg.Body);
            var canReact = IsFriend && !showAccept && !CollabPayload.IsFriend(msg.Body)
                           && !CollabPayload.IsCall(msg.Body) && !CollabPayload.IsTask(msg.Body)
                           && !CollabPayload.IsAgenda(msg.Body) && !CollabPayload.IsShare(msg.Body);
            var thumbs = (msg.Thumbs ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Messages.Add(new ChatBubbleVm
            {
                Id = msg.Id,
                FromName = msg.FromName,
                Body = msg.Body,
                DisplayText = DisplayFor(msg.Body),
                TimeText = msg.SentAt.ToString("HH:mm", culture) + (msg.EditedAt is not null ? " · düzenlendi" : ""),
                IsOutgoing = outgoing,
                FromKey = msg.FromKey,
                IsImage = isImage,
                IsFile = isFile,
                FileName = isFile ? ChatImaging.FileDisplayName(msg.Body) : "",
                Picture = isImage ? ChatImaging.Load(ChatImaging.Materialize(msg.Body)) : null,
                ShowAccept = showAccept,
                CanEdit = outgoing && IsFriend && !isImage && !isFile
                          && !CollabPayload.IsFriend(msg.Body) && !CollabPayload.IsCall(msg.Body)
                          && !CollabPayload.IsTask(msg.Body) && !CollabPayload.IsAgenda(msg.Body)
                          && !CollabPayload.IsShare(msg.Body),
                CanReact = canReact,
                IThumbed = thumbs.Contains(me, StringComparer.Ordinal),
                ThumbCount = thumbs.Length,
                IsEdited = msg.EditedAt is not null
            });
        }

        IsEmptyThread = Messages.Count == 0;
    }

    private static bool IsFriendReq(string body)
    {
        try
        {
            var signal = JsonSerializer.Deserialize<FriendSignal>(body[CollabPayload.Friend.Length..]);
            return signal?.T == "req";
        }
        catch
        {
            return false;
        }
    }

    private static string DisplayFor(string body)
    {
        if (CollabPayload.IsImage(body))
        {
            return "";
        }

        if (CollabPayload.IsFile(body))
        {
            return ChatImaging.FileDisplayName(body);
        }

        if (CollabPayload.IsCall(body))
        {
            return "Arama";
        }

        if (CollabPayload.IsFriend(body))
        {
            return IsFriendReq(body) ? "Arkadaşlık isteği" : signalText(body);

            static string signalText(string body)
            {
                try
                {
                    var signal = JsonSerializer.Deserialize<FriendSignal>(body[CollabPayload.Friend.Length..]);
                    return signal?.T == "no" ? "Arkadaşlık isteği reddedildi" : "Arkadaşlık kabul edildi";
                }
                catch
                {
                    return "Arkadaşlık";
                }
            }
        }

        if (CollabPayload.IsTask(body))
        {
            try
            {
                var signal = JsonSerializer.Deserialize<TaskSignal>(body[CollabPayload.Task.Length..]);
                return signal?.T == "status"
                    ? $"Görev durumu: {signal.Title} → {signal.Status}"
                    : "Görev: " + (signal?.Title ?? "");
            }
            catch
            {
                return "Görev";
            }
        }

        if (CollabPayload.IsShare(body))
        {
            try
            {
                var signal = JsonSerializer.Deserialize<ShareSignal>(body[CollabPayload.Share.Length..]);
                return "Dosya: " + (signal?.Title ?? "paylaşıldı");
            }
            catch
            {
                return "Dosya paylaşıldı";
            }
        }

        if (CollabPayload.IsAgenda(body))
        {
            return "Ajanda";
        }

        return body;
    }

    private static bool IsWorkTask(TaskOccurrence occ)
        => occ.Task.CategoryId == Guid.Parse("11111111-1111-1111-1111-111111111111")
           || string.Equals(occ.Task.Category?.Name, "İş", StringComparison.OrdinalIgnoreCase);

    private string ConnectionText()
    {
        if (_hub.ServerConnected)
        {
            return "Sunucu bağlı — kullanıcı adı ile arayıp istek gönderin. Mesaj ve arama yalnızca arkadaşlar arasında.";
        }

        var status = _hub.ServerStatus;
        if (status is "Kapalı" or "")
        {
            return "Yerel ağ ve bu bilgisayar. Arkadaş olmayanlara mesaj gidemez.";
        }

        return "Sunucu: " + status;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
