using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Chat;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public sealed class LeaveBannerVm
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string ColorHex { get; init; } = "#0F766E";
    public string StatusText { get; init; } = "";
    public string HalfText { get; init; } = "";
    public bool ShowHalf { get; init; }
    public bool IsHourly { get; init; }

    public static LeaveBannerVm From(LeaveRecord leave, DateOnly onDate, LeaveCountContext ctx)
    {
        var kind = LeaveMath.ResolveKind(leave);
        var half = LeaveMath.HalfLabelForDate(leave, onDate);
        var minutes = LeaveMath.CountMinutes(leave, ctx);
        var detailParts = new List<string>
        {
            LeaveMath.FormatDateTimeRange(leave),
            kind.ToDisplay(),
            leave.Status.ToDisplay(),
            LeaveMath.IsLedgerKind(kind)
                ? LeaveMath.FormatHoursMinutes(minutes)
                : LeaveMath.FormatMinutes(minutes, ctx.WorkdayHours)
        };
        if (!string.IsNullOrEmpty(half) && leave.DurationKind != LeaveDurationKind.Hourly && !LeaveMath.IsLedgerKind(kind))
        {
            detailParts.Insert(1, half);
        }

        return new LeaveBannerVm
        {
            Id = leave.Id,
            Title = LeaveMath.BannerTitle(leave),
            Detail = string.Join(" · ", detailParts),
            ColorHex = leave.Type?.ColorHex ?? "#0F766E",
            StatusText = leave.Status.ToDisplay(),
            HalfText = half,
            ShowHalf = half.Length > 0,
            IsHourly = leave.DurationKind == LeaveDurationKind.Hourly || LeaveMath.IsLedgerKind(kind)
        };
    }
}

public sealed class MonthOption
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
}

public sealed class LeaveRowVm
{
    public LeaveRowVm(LeaveRecord record, LeaveCountContext ctx)
    {
        Record = record;
        Id = record.Id;
        var kind = LeaveMath.ResolveKind(record);
        Kind = kind;
        KindText = kind.ToDisplay();
        TypeName = kind == LeaveEntryKind.Leave
            ? (record.Type?.Name ?? "İzin")
            : kind.ToDisplay();
        ColorHex = record.Type?.ColorHex ?? "#0F766E";
        DateText = LeaveMath.FormatDateTimeRange(record);
        var extra = LeaveMath.HalfSummary(record);
        var counted = LeaveMath.CountMinutes(record, ctx);
        var duration = LeaveMath.IsLedgerKind(kind)
            ? LeaveMath.FormatHoursMinutes(counted)
            : LeaveMath.FormatMinutes(counted, ctx.WorkdayHours);
        var unit = kind == LeaveEntryKind.Leave ? record.DurationKind.ToDisplay() : "Dakika dakika";
        DaysText = string.IsNullOrEmpty(extra)
            ? $"{unit} · {duration}"
            : $"{unit} · {duration} · {extra}";
        if (kind == LeaveEntryKind.TelafiliIzin)
        {
            LedgerText = record.Status.AffectsBalance()
                ? LeaveMath.FormatLedgerMinutes(-LeaveMath.CountMinutes(record, ctx))
                : "bakiyeye yansımaz";
        }
        else if (kind == LeaveEntryKind.Telafi)
        {
            LedgerText = record.Status.AffectsBalance()
                ? LeaveMath.FormatLedgerMinutes(LeaveMath.CountMinutes(record, ctx))
                : "bakiyeye yansımaz";
        }
        else
        {
            LedgerText = "";
        }

        StatusText = record.Status.ToDisplay();
        NoteText = record.Note ?? "";
        IsCancelled = record.Status == LeaveStatus.Iptal;
        IsPast = record.EndDate < DateOnly.FromDateTime(DateTime.Today);
        IsLedger = LeaveMath.IsLedgerKind(kind);
    }

    public Guid Id { get; }
    public LeaveRecord Record { get; }
    public LeaveEntryKind Kind { get; }
    public string KindText { get; }
    public string TypeName { get; }
    public string ColorHex { get; }
    public string DateText { get; }
    public string DaysText { get; }
    public string LedgerText { get; }
    public string StatusText { get; }
    public string NoteText { get; }
    public bool IsCancelled { get; }
    public bool IsPast { get; }
    public bool IsLedger { get; }
}

public enum LeaveListFilter
{
    All,
    Leave,
    TelafiliIzin,
    Telafi
}

public partial class LeaveFilterOption : ObservableObject
{
    public LeaveFilterOption(string name, LeaveListFilter value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public LeaveListFilter Value { get; }
    [ObservableProperty] private bool _isSelected;
}

public partial class LeavesViewModel : ObservableObject
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly LeaveService _leaves;
    private readonly IAppDialogs _dialogs;
    private readonly ServerChatClient _server;
    private readonly UserAccountService _users;
    private bool _loading;
    private IReadOnlyList<LeaveRecord> _allRecords = [];
    private LeaveCountContext _ctx = new();
    private LeaveListFilter _filter = LeaveListFilter.All;

    public LeavesViewModel(LeaveService leaves, IAppDialogs dialogs, ServerChatClient server, UserAccountService users)
    {
        _leaves = leaves;
        _dialogs = dialogs;
        _server = server;
        _users = users;
        Months.Add(new MonthOption { Number = 1, Name = "Ocak" });
        Months.Add(new MonthOption { Number = 2, Name = "Şubat" });
        Months.Add(new MonthOption { Number = 3, Name = "Mart" });
        Months.Add(new MonthOption { Number = 4, Name = "Nisan" });
        Months.Add(new MonthOption { Number = 5, Name = "Mayıs" });
        Months.Add(new MonthOption { Number = 6, Name = "Haziran" });
        Months.Add(new MonthOption { Number = 7, Name = "Temmuz" });
        Months.Add(new MonthOption { Number = 8, Name = "Ağustos" });
        Months.Add(new MonthOption { Number = 9, Name = "Eylül" });
        Months.Add(new MonthOption { Number = 10, Name = "Ekim" });
        Months.Add(new MonthOption { Number = 11, Name = "Kasım" });
        Months.Add(new MonthOption { Number = 12, Name = "Aralık" });
        Filters.Add(new LeaveFilterOption("Tümü", LeaveListFilter.All) { IsSelected = true });
        Filters.Add(new LeaveFilterOption("İzin", LeaveListFilter.Leave));
        Filters.Add(new LeaveFilterOption("Telafili izin", LeaveListFilter.TelafiliIzin));
        Filters.Add(new LeaveFilterOption("Telafi", LeaveListFilter.Telafi));
        _loading = true;
        SelectedMonth = Months[0];
    }

    public ObservableCollection<LeaveRowVm> Items { get; } = new();
    public ObservableCollection<LeaveType> CustomTypes { get; } = new();
    public ObservableCollection<MonthOption> Months { get; } = new();
    public ObservableCollection<LeaveFilterOption> Filters { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private string _balanceDetail = "";
    [ObservableProperty] private string _periodText = "";
    [ObservableProperty] private string _allowanceText = "0";
    [ObservableProperty] private string _carryOverText = "0";
    [ObservableProperty] private bool _countWeekends;
    [ObservableProperty] private string _workdayHoursText = "8,5";
    [ObservableProperty] private string _openingHoursText = "0";
    [ObservableProperty] private string _openingMinutesText = "0";
    [ObservableProperty] private bool _openingIsDebt;
    [ObservableProperty] private MonthOption? _selectedMonth;
    [ObservableProperty] private string _newTypeName = "";
    [ObservableProperty] private bool _newTypeCountsAnnual;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isNegativeRemaining;
    [ObservableProperty] private LeaveType? _selectedCustomType;
    [ObservableProperty] private bool _hasCustomTypes;
    [ObservableProperty] private string _compensatoryText = "0 saat";
    [ObservableProperty] private string _compensatoryDetail = "";
    [ObservableProperty] private bool _isNegativeCompensatory;
    [ObservableProperty] private bool _isPositiveCompensatory;
    [ObservableProperty] private string _emptyTitle = "Henüz kayıt yok";
    [ObservableProperty] private string _emptyDetail = "";

    public async Task LoadAsync()
    {
        _loading = true;
        var balance = await _leaves.GetBalanceAsync();
        RemainingText = LeaveMath.FormatMinutes(balance.RemainingMinutes, balance.WorkdayHours);
        IsNegativeRemaining = balance.RemainingMinutes < 0;
        BalanceDetail =
            $"Hak {LeaveMath.FormatMinutes((int)decimal.Round(balance.Entitlement * balance.WorkdayHours * 60m, 0, MidpointRounding.AwayFromZero), balance.WorkdayHours)}" +
            $" · Devir {LeaveMath.FormatMinutes((int)decimal.Round(balance.CarryOver * balance.WorkdayHours * 60m, 0, MidpointRounding.AwayFromZero), balance.WorkdayHours)}" +
            $" · Kullanılan {LeaveMath.FormatMinutes(balance.UsedMinutes, balance.WorkdayHours)}" +
            $" · iş günü {LeaveMath.FormatWorkdayHours(balance.WorkdayHours)}";
        PeriodText =
            $"{balance.PeriodStart.ToString("d MMMM yyyy", Tr)} – {balance.PeriodEnd.ToString("d MMMM yyyy", Tr)}";
        AllowanceText = balance.Entitlement.ToString("0.##", Tr);
        CarryOverText = balance.CarryOver.ToString("0.##", Tr);
        WorkdayHoursText = balance.WorkdayHours.ToString("0.##", Tr);
        CountWeekends = balance.CountWeekends;
        SelectedMonth = Months.FirstOrDefault(m => m.Number == balance.PeriodStart.Month) ?? Months[0];

        var ctx = await _leaves.GetCountContextAsync();
        _ctx = ctx;
        var compensatory = await _leaves.GetCompensatoryBalanceAsync();
        CompensatoryText = LeaveMath.FormatLedgerMinutes(compensatory.NetMinutes);
        IsNegativeCompensatory = compensatory.NetMinutes < 0;
        IsPositiveCompensatory = compensatory.NetMinutes > 0;
        CompensatoryDetail =
            $"Açılış {LeaveMath.FormatLedgerMinutes(compensatory.OpeningMinutes)}" +
            $" · Telafi {LeaveMath.FormatHoursMinutes(compensatory.CreditMinutes)}" +
            $" · Telafili izin {LeaveMath.FormatHoursMinutes(compensatory.DebitMinutes)}";

        var openingAbs = Math.Abs(compensatory.OpeningMinutes);
        OpeningIsDebt = compensatory.OpeningMinutes < 0;
        OpeningHoursText = (openingAbs / 60).ToString(Tr);
        OpeningMinutesText = (openingAbs % 60).ToString(Tr);

        _allRecords = await _leaves.GetAllAsync();
        ApplyFilter();

        var types = await _leaves.GetTypesAsync();
        CustomTypes.Clear();
        foreach (var type in types.Where(t => !t.IsBuiltIn))
        {
            CustomTypes.Add(type);
        }

        HasCustomTypes = CustomTypes.Count > 0;
        _loading = false;
    }

    [RelayCommand]
    private Task AddAsync() => AddWithKindAsync(LeaveEntryKind.Leave);

    [RelayCommand]
    private Task AddTelafiAsync() => AddWithKindAsync(LeaveEntryKind.Telafi);

    [RelayCommand]
    private Task AddTelafiliAsync() => AddWithKindAsync(LeaveEntryKind.TelafiliIzin);

    private async Task AddWithKindAsync(LeaveEntryKind kind)
    {
        var draft = await _dialogs.EditLeaveAsync(null, kind);
        if (draft is null)
        {
            return;
        }

        if (!await ConfirmOverlapAsync(draft))
        {
            return;
        }

        try
        {
            var saved = await _leaves.SaveAsync(draft);
            await SubmitWorkLeaveAsync(saved);
            StatusMessage = $"{LeaveMath.ResolveKind(saved).ToDisplay()} kaydedildi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task EditAsync(object? rowObj)
    {
        if (rowObj is not LeaveRowVm row)
        {
            return;
        }

        var draft = await _dialogs.EditLeaveAsync(row.Record);
        if (draft is null)
        {
            return;
        }

        if (!await ConfirmOverlapAsync(draft))
        {
            return;
        }

        try
        {
            await _leaves.SaveAsync(draft);
            StatusMessage = $"{LeaveMath.ResolveKind(draft).ToDisplay()} güncellendi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(object? rowObj)
    {
        if (rowObj is not LeaveRowVm row)
        {
            return;
        }

        var label = row.KindText;
        if (!_dialogs.Confirm($"\"{row.TypeName}\" kaydı ({row.DateText}) silinsin mi?", $"{label} sil"))
        {
            return;
        }

        await _leaves.DeleteAsync(row.Id);
        StatusMessage = $"{label} silindi.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddTypeAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTypeName))
        {
            return;
        }

        try
        {
            await _leaves.AddTypeAsync(NewTypeName, NewTypeCountsAnnual);
            NewTypeName = "";
            NewTypeCountsAnnual = false;
            StatusMessage = "Özel izin türü eklendi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteTypeAsync()
    {
        if (SelectedCustomType is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"\"{SelectedCustomType.Name}\" türü silinsin mi?", "Türü sil"))
        {
            return;
        }

        try
        {
            await _leaves.DeleteTypeAsync(SelectedCustomType.Id);
            StatusMessage = "Tür silindi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    partial void OnAllowanceTextChanged(string value) => _ = PersistBalanceAsync();
    partial void OnCarryOverTextChanged(string value) => _ = PersistBalanceAsync();
    partial void OnCountWeekendsChanged(bool value) => _ = PersistBalanceAsync();
    partial void OnWorkdayHoursTextChanged(string value) => _ = PersistBalanceAsync();
    partial void OnSelectedMonthChanged(MonthOption? value) => _ = PersistBalanceAsync();
    partial void OnOpeningHoursTextChanged(string value) => _ = PersistOpeningAsync();
    partial void OnOpeningMinutesTextChanged(string value) => _ = PersistOpeningAsync();
    partial void OnOpeningIsDebtChanged(bool value) => _ = PersistOpeningAsync();

    private async Task PersistOpeningAsync()
    {
        if (_loading)
        {
            return;
        }

        var hours = 0;
        var minutes = 0;
        _ = int.TryParse(OpeningHoursText.Trim().Replace('−', '-'), NumberStyles.Integer, Tr, out hours);
        _ = int.TryParse(OpeningMinutesText.Trim(), NumberStyles.Integer, Tr, out minutes);
        hours = Math.Abs(hours);
        minutes = Math.Clamp(Math.Abs(minutes), 0, 59);
        var total = hours * 60 + minutes;
        if (OpeningIsDebt)
        {
            total = -total;
        }

        await _leaves.SaveOpeningMinutesAsync(total);
        await LoadAsync();
    }

    private async Task PersistBalanceAsync()
    {
        if (_loading)
        {
            return;
        }

        var month = SelectedMonth?.Number ?? 1;
        var entitlement = LeaveService.ParseDecimal(AllowanceText, 0m);
        var carry = LeaveService.ParseDecimal(CarryOverText, 0m);
        var workday = LeaveService.ParseDecimal(WorkdayHoursText, LeaveMath.DefaultWorkdayHours);
        await _leaves.SaveBalanceSettingsAsync(month, entitlement, carry, CountWeekends, workday);
        await LoadAsync();
    }

    private async Task<bool> ConfirmOverlapAsync(LeaveRecord draft)
    {
        var overlaps = await _leaves.GetOverlapsAsync(draft.StartDate, draft.EndDate, draft.Id == Guid.Empty ? null : draft.Id);
        if (overlaps.Count == 0)
        {
            return true;
        }

        var names = string.Join(", ", overlaps.Select(o => $"{LeaveMath.ResolveKind(o).ToDisplay()} ({LeaveMath.FormatDateRange(o.StartDate, o.EndDate)})"));
        return _dialogs.Confirm(
            $"Bu tarihlerde başka kayıt var: {names}. Yine de kaydedilsin mi?",
            "Çakışan kayıt");
    }

    private async Task SubmitWorkLeaveAsync(LeaveRecord saved)
    {
        if (!_users.UsesWork || saved.ServerLeaveId is not null)
        {
            return;
        }

        try
        {
            var remote = await _server.CreateLeaveAsync(new OrgLeaveCreateRequest
            {
                ClientId = saved.Id,
                TypeName = saved.Type?.Name ?? saved.EntryKind.ToDisplay(),
                EntryKind = (int)saved.EntryKind,
                DurationKind = (int)saved.DurationKind,
                StartDate = saved.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = saved.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StartTime = saved.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                EndTime = saved.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                StartHalf = (int)saved.StartHalf,
                EndHalf = (int)saved.EndHalf,
                Note = saved.Note,
                DurationMinutes = saved.DurationMinutes
            });
            saved.ServerLeaveId = remote.Id;
            saved.Status = LeaveService.MapServerStatus(remote.Status);
            await _leaves.SaveAsync(saved);
            StatusMessage = $"{LeaveMath.ResolveKind(saved).ToDisplay()} üst amire iletildi.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Yerel kaydedildi; sunucuya gidemedi: " + ex.Message;
        }
    }

    [RelayCommand]
    private void SetFilter(object? optionObj)
    {
        if (optionObj is not LeaveFilterOption option)
        {
            return;
        }

        _filter = option.Value;
        foreach (var item in Filters)
        {
            item.IsSelected = item.Value == option.Value;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var filtered = _allRecords.Where(r => _filter switch
        {
            LeaveListFilter.Leave => LeaveMath.ResolveKind(r) == LeaveEntryKind.Leave,
            LeaveListFilter.TelafiliIzin => LeaveMath.ResolveKind(r) == LeaveEntryKind.TelafiliIzin,
            LeaveListFilter.Telafi => LeaveMath.ResolveKind(r) == LeaveEntryKind.Telafi,
            _ => true
        });

        Items.Clear();
        foreach (var record in filtered
                     .OrderBy(r => r.EndDate < today)
                     .ThenBy(r => r.StartDate)
                     .ThenByDescending(r => r.CreatedAt))
        {
            Items.Add(new LeaveRowVm(record, _ctx));
        }

        IsEmpty = Items.Count == 0;
        EmptyTitle = _filter switch
        {
            LeaveListFilter.TelafiliIzin => "Henüz telafili izin yok",
            LeaveListFilter.Telafi => "Henüz telafi kaydı yok",
            LeaveListFilter.Leave => "Henüz izin kaydı yok",
            _ => "Henüz izin veya telafi kaydı yok"
        };
        EmptyDetail = _filter switch
        {
            LeaveListFilter.TelafiliIzin =>
                "Telafili izin aldığında başlangıç ve bitiş tarih + saat + dakikayı gir. Süre telafi bakiyesinden düşer (borç). Yıllık izinden düşmez. Yalnızca Onaylandı ve Kullanıldı bakiyeyi değiştirir.",
            LeaveListFilter.Telafi =>
                "Eksik saatleri telafi ettiğinde aynı şekilde başlangıç ve bitiş gir. Süre bakiyeye eklenir (alacak). İzin günü değildir; takvimde «Telafi» bloğu olarak görünür.",
            LeaveListFilter.Leave =>
                "İşyerinden aldığın izinleri buraya işle. Saatlik (tarih + saat + dakika), günlük, yarım gün veya uzun yıllık blok ekleyebilirsin.",
            _ =>
                "Telafili izin borç yazılır, telafi alacak yazar. İkisi de dakika dakika ölçülür (başlangıç ve bitiş tarih + saat + dakika). Negatif bakiye = borç, pozitif = alacak, 0 = denk. Yıllık izin bakiyesinden ayrıdır. İptal ve Planlandı bakiyeyi değiştirmez."
        };
    }
}
