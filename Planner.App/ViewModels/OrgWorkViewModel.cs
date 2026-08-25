using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Chat;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public sealed class OrgLeaveInboxVm
{
    public OrgLeaveInboxVm(OrgLeaveDto dto)
    {
        Dto = dto;
        Who = dto.UserName;
        Type = dto.TypeName;
        When = dto.StartDate == dto.EndDate ? dto.StartDate : dto.StartDate + " – " + dto.EndDate;
        StatusText = dto.Status switch
        {
            "pending" => "Bekliyor",
            "approved" => "Onaylı",
            "rejected" => "Reddedildi",
            _ => dto.Status
        };
    }

    public OrgLeaveDto Dto { get; }
    public string Who { get; }
    public string Type { get; }
    public string When { get; }
    public string StatusText { get; }
}

public sealed class OrgPersonLeaveVm
{
    public OrgPersonLeaveVm(OrgLeavePersonRow row)
    {
        Name = row.Person.DisplayName;
        Role = string.Join(" · ", new[] { row.Person.PositionTitle, row.Person.UnitName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        TodayStatus = row.TodayStatus;
        NextLeave = row.NextLeave;
        PendingText = row.PendingCount == 0 ? "—" : row.PendingCount + " talep";
    }

    public string Name { get; }
    public string Role { get; }
    public string TodayStatus { get; }
    public string NextLeave { get; }
    public string PendingText { get; }
}

public sealed class OrgAuditRowVm
{
    public OrgAuditRowVm(AuditEventDto dto)
    {
        When = dto.At.ToLocalTime().ToString("dd.MM.yyyy HH:mm", new CultureInfo("tr-TR"));
        Actor = dto.ActorName;
        Action = dto.Action;
        Target = string.IsNullOrWhiteSpace(dto.TargetName) ? "—" : dto.TargetName;
        Detail = dto.Detail;
    }

    public string When { get; }
    public string Actor { get; }
    public string Action { get; }
    public string Target { get; }
    public string Detail { get; }
}

public partial class OrgWorkViewModel : ObservableObject
{
    private readonly ServerChatClient _server;
    private readonly UserAccountService _users;
    private readonly OrgWorkService _sync;
    private readonly IAppDialogs _dialogs;

    public OrgWorkViewModel(ServerChatClient server, UserAccountService users, OrgWorkService sync, IAppDialogs dialogs)
    {
        _server = server;
        _users = users;
        _sync = sync;
        _dialogs = dialogs;
    }

    public ObservableCollection<OrgPersonDto> DirectReports { get; } = new();
    public ObservableCollection<WorkTaskDto> Inbox { get; } = new();
    public ObservableCollection<WorkTaskDto> TeamTasks { get; } = new();
    public ObservableCollection<OrgPersonLeaveVm> LeavePeople { get; } = new();
    public ObservableCollection<OrgLeaveInboxVm> LeaveInbox { get; } = new();
    public ObservableCollection<OrgAuditRowVm> AuditEvents { get; } = new();
    public ObservableCollection<WorkFileDto> TaskFiles { get; } = new();

    [ObservableProperty] private OrgPersonDto? _selectedReport;
    [ObservableProperty] private WorkTaskDto? _selectedInbox;
    [ObservableProperty] private WorkTaskDto? _selectedTeamTask;
    [ObservableProperty] private OrgPersonDto? _distributeTo;
    [ObservableProperty] private OrgLeaveInboxVm? _selectedLeave;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _dateText = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _roleLabel = "";
    [ObservableProperty] private bool _canAssign;
    [ObservableProperty] private bool _isEmptyReports = true;
    [ObservableProperty] private bool _canManageLeaves;
    [ObservableProperty] private bool _hasLeaveInbox;

    partial void OnSelectedInboxChanged(WorkTaskDto? value) => ShowFiles(value);

    partial void OnSelectedTeamTaskChanged(WorkTaskDto? value)
    {
        if (value is not null)
        {
            SelectedInbox = null;
        }

        ShowFiles(value ?? SelectedInbox);
    }

    private void ShowFiles(WorkTaskDto? value)
    {
        TaskFiles.Clear();
        if (value?.Files is null)
        {
            return;
        }

        foreach (var file in value.Files)
        {
            TaskFiles.Add(file);
        }
    }

    public async Task LoadAsync()
    {
        DirectReports.Clear();
        Inbox.Clear();
        TeamTasks.Clear();
        LeavePeople.Clear();
        LeaveInbox.Clear();
        AuditEvents.Clear();
        TaskFiles.Clear();
        CanAssign = false;
        CanManageLeaves = false;
        HasLeaveInbox = false;
        if (!_users.UsesWork)
        {
            Hint = "Bu hesap özel. Kurum görevleri için iş veya ikisi birlikte kaydı gerekir.";
            RoleLabel = "Özel hesap";
            return;
        }

        RoleLabel = string.Join(" · ", new[]
        {
            _users.Current?.CompanyName,
            _users.Current?.UnitName,
            _users.Current?.PositionTitle
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        try
        {
            await _sync.SyncInboxAsync();
            var team = await _server.GetTeamAsync();
            foreach (var person in team.DirectReports)
            {
                DirectReports.Add(person);
            }

            CanAssign = DirectReports.Count > 0;
            IsEmptyReports = DirectReports.Count == 0;
            Hint = CanAssign
                ? "Yalnızca bir altınızdaki kişiye görev verebilirsiniz. O kişi kendi altına dağıtır. Görev ekleri sunucuda durur (20 MB)."
                : "Şemada doğrudan altınız yok. Size atanan görevler aşağıda; üstünüze dağıtım onlar yapar.";
            foreach (var task in team.Tasks.Where(t => t.AssignedToUserId == _server.UserId))
            {
                Inbox.Add(task);
            }

            foreach (var task in team.Tasks.Where(t => t.AssignedToUserId != _server.UserId))
            {
                TeamTasks.Add(task);
            }

            var board = await _server.GetLeaveBoardAsync();
            CanManageLeaves = board.CanManage;
            foreach (var person in board.People)
            {
                LeavePeople.Add(new OrgPersonLeaveVm(person));
            }

            foreach (var leave in board.Inbox)
            {
                LeaveInbox.Add(new OrgLeaveInboxVm(leave));
            }

            HasLeaveInbox = LeaveInbox.Count > 0;
            foreach (var ev in await _server.GetAuditAsync())
            {
                AuditEvents.Add(new OrgAuditRowVm(ev));
            }
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        if (SelectedReport is null || string.IsNullOrWhiteSpace(Title))
        {
            Hint = "Kişi ve başlık gerekli.";
            return;
        }

        if (!Guid.TryParse(SelectedReport.UserId, out var to))
        {
            return;
        }

        try
        {
            await _server.AssignWorkAsync(new WorkTaskCreateRequest
            {
                Title = Title.Trim(),
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Date = DateText.Trim(),
                ToUserId = to
            });
            Title = "";
            Notes = "";
            Hint = "Görev atandı. Karşı tarafa bildirim gider.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DistributeAsync()
    {
        if (SelectedInbox is null || DistributeTo is null)
        {
            Hint = "Dağıtılacak görevi ve bir altınızı seçin.";
            return;
        }

        if (!Guid.TryParse(DistributeTo.UserId, out var to))
        {
            return;
        }

        try
        {
            await _server.DistributeWorkAsync(new WorkTaskDistributeRequest
            {
                TaskId = SelectedInbox.Id,
                ToUserId = to,
                Title = SelectedInbox.Title,
                Notes = SelectedInbox.Notes,
                Date = SelectedInbox.Date,
                Time = SelectedInbox.Time
            });
            Hint = "Görev bir altınıza dağıtıldı.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ApproveLeaveAsync() => await DecideLeaveAsync(true);

    [RelayCommand]
    private async Task RejectLeaveAsync() => await DecideLeaveAsync(false);

    private async Task DecideLeaveAsync(bool approve)
    {
        if (SelectedLeave is null)
        {
            Hint = "Karar için bir izin talebi seçin.";
            return;
        }

        try
        {
            await _server.DecideLeaveAsync(SelectedLeave.Dto.Id, approve);
            Hint = approve ? "İzin onaylandı." : "İzin reddedildi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        var target = SelectedInbox ?? SelectedTeamTask;
        if (target is null)
        {
            Hint = "Dosya eklemek için bir görev seçin.";
            return;
        }

        var path = _dialogs.OpenAnyFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _server.UploadWorkFileAsync(target.Id, path);
            Hint = "Kurum dosyası sunucuya yüklendi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(object? fileObj)
    {
        if (fileObj is not WorkFileDto file)
        {
            return;
        }

        try
        {
            var path = await _server.DownloadWorkFileAsync(file.Id, file.Name);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Hint = ex.Message;
        }
    }
}
