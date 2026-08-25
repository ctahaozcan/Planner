using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public sealed class DocumentRowVm
{
    public DocumentRowVm(WorkspaceDocument document)
    {
        Document = document;
        IsTable = document.Kind == WorkspaceDocumentKind.Table;
        KindLabel = IsTable ? "E-tablo" : "Belge";
        Tint = IsTable ? "#188038" : "#1A73E8";
        TintSoft = IsTable ? "#E6F4EA" : "#E8F0FE";
        IconGlyph = IsTable ? "▦" : "¶";
        UpdatedText = document.UpdatedAt.ToString("d MMMM yyyy HH:mm", new System.Globalization.CultureInfo("tr-TR"));
        ShareHint = "";
    }

    public WorkspaceDocument Document { get; }
    public Guid Id => Document.Id;
    public string Title => Document.Title;
    public WorkspaceDocumentKind Kind => Document.Kind;
    public bool IsTable { get; }
    public string KindLabel { get; }
    public string Tint { get; }
    public string TintSoft { get; }
    public string IconGlyph { get; }
    public string UpdatedText { get; }
    public string ShareHint { get; set; }
}

public partial class DocumentsViewModel : ObservableObject
{
    private readonly DocumentService _docs;
    private readonly IAppDialogs _dialogs;
    private readonly FriendshipService _friends;
    private readonly UserAccountService _users;
    private readonly ChatHub _hub;
    private readonly List<DocumentRowVm> _all = [];

    public DocumentsViewModel(
        DocumentService docs,
        IAppDialogs dialogs,
        FriendshipService friends,
        UserAccountService users,
        ChatHub hub)
    {
        _docs = docs;
        _dialogs = dialogs;
        _friends = friends;
        _users = users;
        _hub = hub;
    }

    public ObservableCollection<DocumentRowVm> Items { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _noSearchHits;
    [ObservableProperty] private DocumentRowVm? _selected;
    [ObservableProperty] private string _query = "";

    public event Action<WorkspaceDocument>? OpenDocumentRequested;

    partial void OnQueryChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        var list = await _docs.ListAsync();
        var sharedOut = await _docs.SharedOutIdsAsync();
        var me = _users.Current?.Id;
        _all.Clear();
        foreach (var doc in list)
        {
            var row = new DocumentRowVm(doc);
            if (me is Guid uid && doc.OwnerUserId is Guid owner && owner != uid)
            {
                row.ShareHint = "Sizinle paylaşıldı";
            }
            else if (sharedOut.Contains(doc.Id))
            {
                row.ShareHint = "Paylaşıldı";
            }

            _all.Add(row);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = Query?.Trim() ?? "";
        Items.Clear();
        foreach (var item in _all)
        {
            if (q.Length == 0 || item.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            {
                Items.Add(item);
            }
        }

        IsEmpty = _all.Count == 0;
        NoSearchHits = _all.Count > 0 && Items.Count == 0;
        if (Selected is not null && Items.All(i => i.Id != Selected.Id))
        {
            Selected = Items.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task NewTextAsync()
    {
        var doc = await _docs.CreateAsync(WorkspaceDocumentKind.Text);
        await LoadAsync();
        OpenDocumentRequested?.Invoke(doc);
    }

    [RelayCommand]
    private async Task NewTableAsync()
    {
        var doc = await _docs.CreateAsync(WorkspaceDocumentKind.Table);
        await LoadAsync();
        OpenDocumentRequested?.Invoke(doc);
    }

    [RelayCommand]
    private void OpenItem(DocumentRowVm? row)
    {
        row ??= Selected;
        if (row is { } item)
        {
            Selected = item;
            OpenDocumentRequested?.Invoke(item.Document);
        }
    }

    [RelayCommand]
    private void Open() => OpenItem(Selected);

    [RelayCommand]
    private async Task RenameAsync(DocumentRowVm? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        var name = _dialogs.Prompt("Yeniden adlandır", "Dosya adı", row.Title);
        if (string.IsNullOrWhiteSpace(name) || name == row.Title)
        {
            return;
        }

        await _docs.RenameAsync(row.Id, name);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DuplicateAsync(DocumentRowVm? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        var copy = await _docs.DuplicateAsync(row.Id);
        await LoadAsync();
        if (copy is not null)
        {
            OpenDocumentRequested?.Invoke(copy);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(DocumentRowVm? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"«{row.Title}» çöp kutusuna taşınmadan silinsin mi?", "Sil"))
        {
            return;
        }

        await _docs.DeleteAsync(row.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ShareAsync(DocumentRowVm? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        var friendIds = (await _friends.AcceptedFriendIdsAsync()).ToList();
        var users = await _users.ListAsync();
        var labels = new List<string>();
        var targets = new List<(Guid Id, ChatPeer? Peer)>();
        foreach (var friendId in friendIds)
        {
            var user = users.FirstOrDefault(u => u.Id == friendId);
            var peer = _hub.FindPeer(friendId.ToString("N")) ?? _hub.FindPeer(friendId.ToString());
            labels.Add(user?.DisplayName ?? peer?.Name ?? friendId.ToString("N")[..8]);
            targets.Add((friendId, peer));
        }

        foreach (var peer in _hub.SnapshotPeers())
        {
            if (!await _friends.AreFriendsByKeyAsync(_users.CurrentKey, peer.Key))
            {
                continue;
            }

            if (targets.Any(t => t.Peer?.Key == peer.Key || (FriendshipService.TryKey(peer.Key, out var gid) && t.Id == gid)))
            {
                continue;
            }

            FriendshipService.TryKey(peer.Key, out var parsed);
            labels.Add(peer.Name);
            targets.Add((parsed, peer));
        }

        var index = _dialogs.PickIndex("Paylaş", "Drive gibi: aynı PC’de dosya listesinde görünür. Başka Yaver’a kopya gönderilir.", labels);
        if (index < 0 || index >= targets.Count)
        {
            return;
        }

        var (id, peerHit) = targets[index];
        try
        {
            if (id != Guid.Empty)
            {
                await _docs.ShareAsync(row.Id, id);
            }

            if (peerHit is { Kind: not ChatPeerKind.Local })
            {
                var doc = await _docs.GetAsync(row.Id);
                if (doc is not null)
                {
                    var body = doc.Body ?? "";
                    if (body.Length > 90_000)
                    {
                        await _hub.SendAsync(peerHit, $"«{doc.Title}» paylaşıldı. Dosya büyük olduğu için kopya gönderilemedi; Word veya PDF dışa aktarın.");
                    }
                    else
                    {
                        var payload = System.Text.Json.JsonSerializer.Serialize(new ShareSignal
                        {
                            Title = doc.Title,
                            Kind = (int)doc.Kind,
                            Body = body
                        });
                        await _hub.SendAsync(peerHit, CollabPayload.Share + payload);
                    }
                }
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message, "Paylaş");
        }
    }
}
