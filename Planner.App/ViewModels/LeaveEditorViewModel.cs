using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.Core.Models;

namespace Planner.App.ViewModels;

public sealed class LeaveStatusOption
{
    public LeaveStatusOption(string name, LeaveStatus value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public LeaveStatus Value { get; }
}

public sealed class HalfDayOption
{
    public HalfDayOption(string name, HalfDayKind value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public HalfDayKind Value { get; }
}

public sealed class LeaveDurationOption
{
    public LeaveDurationOption(string name, LeaveDurationKind value, string hint)
    {
        Name = name;
        Value = value;
        Hint = hint;
    }

    public string Name { get; }
    public LeaveDurationKind Value { get; }
    public string Hint { get; }
}

public sealed class LeaveKindOption
{
    public LeaveKindOption(string name, LeaveEntryKind value, string hint)
    {
        Name = name;
        Value = value;
        Hint = hint;
    }

    public string Name { get; }
    public LeaveEntryKind Value { get; }
    public string Hint { get; }
}

public partial class LeaveEditorViewModel : ObservableObject
{
    private readonly LeaveCountContext _ctx;
    private readonly IReadOnlyList<LeaveType> _allTypes;
    private readonly bool _isNew;

    public LeaveEditorViewModel(
        IReadOnlyList<LeaveType> types,
        LeaveRecord? existing,
        LeaveCountContext ctx,
        LeaveEntryKind? presetKind = null)
    {
        _ctx = ctx;
        _allTypes = types;
        _isNew = existing is null;
        Types = new ObservableCollection<LeaveType>(types.Where(t => t.Id != LeaveIds.TelafiliIzin && t.Id != LeaveIds.Telafi));
        Statuses.Add(new LeaveStatusOption("Planlandı", LeaveStatus.Planlandi));
        Statuses.Add(new LeaveStatusOption("Onaylandı", LeaveStatus.Onaylandi));
        Statuses.Add(new LeaveStatusOption("Kullanıldı", LeaveStatus.Kullanildi));
        Statuses.Add(new LeaveStatusOption("İptal", LeaveStatus.Iptal));
        HalfParts.Add(new HalfDayOption("Sabah", HalfDayKind.Morning));
        HalfParts.Add(new HalfDayOption("Öğleden sonra", HalfDayKind.Afternoon));
        KindOptions.Add(new LeaveKindOption("İzin", LeaveEntryKind.Leave, "Yıllık, mazeret ve diğer izinler. Yıllık bakiyeden düşen türler ayrı tutulur."));
        KindOptions.Add(new LeaveKindOption("Telafili izin", LeaveEntryKind.TelafiliIzin, "İşten alınan telafi izni. Süre telafi bakiyesinden düşer (borç). Yıllık izinden düşmez. Başlangıç ve bitiş tarih + saat + dakika."));
        KindOptions.Add(new LeaveKindOption("Telafi", LeaveEntryKind.Telafi, "Borcu kapatmak için çalışılan süre. Telafi bakiyesine eklenir (alacak). İzin günü değildir; takvimde zamanlı blok olarak görünür."));
        DurationOptions.Add(new LeaveDurationOption("Saat", LeaveDurationKind.Hourly, "Tarih + saat + dakika (ör. 09:00–11:30)"));
        DurationOptions.Add(new LeaveDurationOption("Gün", LeaveDurationKind.Daily, "Tek gün veya kısa aralık, isteğe bağlı yarım gün veya saat"));
        DurationOptions.Add(new LeaveDurationOption("Uzun izin / yıllık blok", LeaveDurationKind.Range, "Çok günlük aralık, hafta içi sayılır"));
        Hours = new ObservableCollection<string>(Enumerable.Range(0, 24).Select(h => h.ToString("00")));
        Minutes = new ObservableCollection<string>(Enumerable.Range(0, 60).Select(m => m.ToString("00")));

        if (existing is null)
        {
            var kind = presetKind ?? LeaveEntryKind.Leave;
            WindowTitle = TitleFor(kind, isNew: true);
            Result = new LeaveRecord
            {
                Id = Guid.NewGuid(),
                Status = LeaveStatus.Onaylandi,
                EntryKind = kind,
                DurationKind = LeaveMath.IsLedgerKind(kind) ? LeaveDurationKind.Hourly : LeaveDurationKind.Daily,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today),
                CreatedAt = DateTime.Now
            };
            SelectedKind = KindOptions.First(k => k.Value == kind);
            SelectedType = Types.FirstOrDefault(t => t.Id == LeaveIds.Annual) ?? Types.FirstOrDefault();
            SelectedStatus = Statuses.First(s => s.Value == LeaveStatus.Onaylandi);
            SelectedDuration = DurationOptions.First(d => d.Value == Result.DurationKind);
            StartDate = DateTime.Today;
            EndDate = DateTime.Today;
            SelectedStartHalf = HalfParts[0];
            SelectedEndHalf = HalfParts[0];
            StartHour = "09";
            StartMinute = "00";
            EndHour = "11";
            EndMinute = "30";
            HasStartTime = LeaveMath.IsLedgerKind(kind);
            HasEndTime = LeaveMath.IsLedgerKind(kind);
        }
        else
        {
            var kind = LeaveMath.ResolveKind(existing);
            WindowTitle = TitleFor(kind, isNew: false);
            Result = existing;
            SelectedKind = KindOptions.FirstOrDefault(k => k.Value == kind) ?? KindOptions[0];
            SelectedType = Types.FirstOrDefault(t => t.Id == existing.TypeId) ?? Types.FirstOrDefault();
            SelectedStatus = Statuses.FirstOrDefault(s => s.Value == existing.Status) ?? Statuses[1];
            SelectedDuration = DurationOptions.FirstOrDefault(d => d.Value == existing.DurationKind)
                               ?? DurationOptions[1];
            StartDate = existing.StartDate.ToDateTime(TimeOnly.MinValue);
            EndDate = existing.EndDate.ToDateTime(TimeOnly.MinValue);
            Note = existing.Note ?? "";
            FirstHalfEnabled = existing.StartHalf != HalfDayKind.None && existing.StartTime is null;
            LastHalfEnabled = existing.EndHalf != HalfDayKind.None && existing.EndTime is null;
            SelectedStartHalf = HalfParts.FirstOrDefault(h => h.Value == existing.StartHalf) ?? HalfParts[0];
            SelectedEndHalf = HalfParts.FirstOrDefault(h => h.Value == existing.EndHalf) ?? HalfParts[0];
            HasStartTime = existing.DurationKind == LeaveDurationKind.Hourly || existing.StartTime is not null || LeaveMath.IsLedgerKind(kind);
            HasEndTime = existing.DurationKind == LeaveDurationKind.Hourly || existing.EndTime is not null || LeaveMath.IsLedgerKind(kind);
            StartHour = (existing.StartTime ?? new TimeOnly(9, 0)).Hour.ToString("00");
            StartMinute = (existing.StartTime ?? new TimeOnly(9, 0)).Minute.ToString("00");
            EndHour = (existing.EndTime ?? new TimeOnly(11, 30)).Hour.ToString("00");
            EndMinute = (existing.EndTime ?? new TimeOnly(11, 30)).Minute.ToString("00");
        }

        RefreshUnitUi();
        RefreshPreview();
    }

    public string WindowTitle { get; private set; }
    public LeaveRecord? Result { get; private set; }
    public event Action<bool>? CloseRequested;

    public ObservableCollection<LeaveType> Types { get; }
    public ObservableCollection<LeaveStatusOption> Statuses { get; } = new();
    public ObservableCollection<HalfDayOption> HalfParts { get; } = new();
    public ObservableCollection<LeaveKindOption> KindOptions { get; } = new();
    public ObservableCollection<LeaveDurationOption> DurationOptions { get; } = new();
    public ObservableCollection<string> Hours { get; }
    public ObservableCollection<string> Minutes { get; }

    [ObservableProperty] private LeaveKindOption? _selectedKind;
    [ObservableProperty] private LeaveType? _selectedType;
    [ObservableProperty] private LeaveStatusOption? _selectedStatus;
    [ObservableProperty] private LeaveDurationOption? _selectedDuration;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private bool _firstHalfEnabled;
    [ObservableProperty] private bool _lastHalfEnabled;
    [ObservableProperty] private HalfDayOption? _selectedStartHalf;
    [ObservableProperty] private HalfDayOption? _selectedEndHalf;
    [ObservableProperty] private bool _hasStartTime;
    [ObservableProperty] private bool _hasEndTime;
    [ObservableProperty] private string _startHour = "09";
    [ObservableProperty] private string _startMinute = "00";
    [ObservableProperty] private string _endHour = "11";
    [ObservableProperty] private string _endMinute = "30";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _durationHint = "";

    public bool IsHourly => IsLedgerEntry || SelectedDuration?.Value == LeaveDurationKind.Hourly;
    public bool IsDaily => !IsLedgerEntry && SelectedDuration?.Value == LeaveDurationKind.Daily;
    public bool IsRange => !IsLedgerEntry && SelectedDuration?.Value == LeaveDurationKind.Range;
    public bool ShowHalfDay => IsDaily;
    public bool ShowOptionalTimes => !IsHourly;
    public bool ShowLastHalf => IsDaily && DateOnly.FromDateTime(StartDate) != DateOnly.FromDateTime(EndDate);
    public bool ShowEndHalfPart => ShowLastHalf && LastHalfEnabled;
    public bool ShowStartTimePickers => IsHourly || HasStartTime;
    public bool ShowEndTimePickers => IsHourly || HasEndTime;
    public bool IsRegularLeave => SelectedKind?.Value is null or LeaveEntryKind.Leave;
    public bool IsLedgerEntry => LeaveMath.IsLedgerKind(SelectedKind?.Value ?? LeaveEntryKind.Leave);
    public bool ShowTypePicker => IsRegularLeave;
    public bool ShowDurationPicker => IsRegularLeave;
    public string FirstHalfLabel => ShowLastHalf ? "İlk gün yarım" : "Yarım gün";
    public string EndDateLabel => IsHourly ? "Bitiş tarihi" : "Bitiş tarihi (dahil)";
    public string KindHint => SelectedKind?.Hint ?? "";

    partial void OnSelectedKindChanged(LeaveKindOption? value)
    {
        var kind = value?.Value ?? LeaveEntryKind.Leave;
        WindowTitle = TitleFor(kind, _isNew);
        OnPropertyChanged(nameof(WindowTitle));
        if (LeaveMath.IsLedgerKind(kind))
        {
            SelectedDuration = DurationOptions.First(d => d.Value == LeaveDurationKind.Hourly);
            HasStartTime = true;
            HasEndTime = true;
            FirstHalfEnabled = false;
            LastHalfEnabled = false;
        }

        RefreshUnitUi();
        RefreshPreview();
    }

    partial void OnSelectedStatusChanged(LeaveStatusOption? value) => RefreshPreview();

    partial void OnSelectedDurationChanged(LeaveDurationOption? value)
    {
        if (value?.Value == LeaveDurationKind.Hourly)
        {
            HasStartTime = true;
            HasEndTime = true;
            FirstHalfEnabled = false;
            LastHalfEnabled = false;
            if (EndDate.Date < StartDate.Date)
            {
                EndDate = StartDate;
            }
        }

        RefreshUnitUi();
        RefreshPreview();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (EndDate.Date < value.Date)
        {
            EndDate = value.Date;
        }

        RefreshUnitUi();
        RefreshPreview();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        RefreshUnitUi();
        RefreshPreview();
    }

    partial void OnFirstHalfEnabledChanged(bool value) => RefreshPreview();
    partial void OnLastHalfEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEndHalfPart));
        RefreshPreview();
    }

    partial void OnSelectedStartHalfChanged(HalfDayOption? value) => RefreshPreview();
    partial void OnSelectedEndHalfChanged(HalfDayOption? value) => RefreshPreview();
    partial void OnHasStartTimeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStartTimePickers));
        RefreshPreview();
    }

    partial void OnHasEndTimeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEndTimePickers));
        RefreshPreview();
    }

    partial void OnStartHourChanged(string value) => RefreshPreview();
    partial void OnStartMinuteChanged(string value) => RefreshPreview();
    partial void OnEndHourChanged(string value) => RefreshPreview();
    partial void OnEndMinuteChanged(string value) => RefreshPreview();

    [RelayCommand]
    private void Save()
    {
        var entryKind = SelectedKind?.Value ?? LeaveEntryKind.Leave;
        var isLedger = LeaveMath.IsLedgerKind(entryKind);
        LeaveType? type;
        if (isLedger)
        {
            var typeId = LeaveMath.TypeIdForKind(entryKind);
            type = _allTypes.FirstOrDefault(t => t.Id == typeId);
            if (type is null)
            {
                ErrorMessage = "Telafi türü yüklenemedi. Uygulamayı yeniden açmayı dene.";
                return;
            }
        }
        else
        {
            type = SelectedType;
            if (type is null)
            {
                ErrorMessage = "İzin türü seç.";
                return;
            }
        }

        var start = DateOnly.FromDateTime(StartDate);
        var end = DateOnly.FromDateTime(EndDate);
        if (end < start)
        {
            ErrorMessage = "Bitiş tarihi başlangıçtan önce olamaz.";
            return;
        }

        var durationKind = isLedger ? LeaveDurationKind.Hourly : (SelectedDuration?.Value ?? LeaveDurationKind.Daily);
        var startTime = durationKind == LeaveDurationKind.Hourly || HasStartTime ? ParseTime(StartHour, StartMinute) : null;
        var endTime = durationKind == LeaveDurationKind.Hourly || HasEndTime ? ParseTime(EndHour, EndMinute) : null;
        if (durationKind == LeaveDurationKind.Hourly)
        {
            if (startTime is null || endTime is null)
            {
                ErrorMessage = "Saat ve dakikayı seç.";
                return;
            }

            if (LeaveMath.MinutesBetween(start.ToDateTime(startTime.Value), end.ToDateTime(endTime.Value)) <= 0)
            {
                ErrorMessage = "Bitiş, başlangıçtan sonra olmalı (tarih, saat ve dakika).";
                return;
            }
        }

        Result = new LeaveRecord
        {
            Id = Result?.Id ?? Guid.NewGuid(),
            TypeId = type.Id,
            Type = type,
            EntryKind = entryKind,
            DurationKind = durationKind,
            StartDate = start,
            EndDate = end,
            StartTime = startTime,
            EndTime = endTime,
            StartHalf = durationKind == LeaveDurationKind.Daily && FirstHalfEnabled && startTime is null
                ? (SelectedStartHalf?.Value ?? HalfDayKind.Morning)
                : HalfDayKind.None,
            EndHalf = durationKind == LeaveDurationKind.Daily && start != end && LastHalfEnabled && endTime is null
                ? (SelectedEndHalf?.Value ?? HalfDayKind.Afternoon)
                : HalfDayKind.None,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            Status = SelectedStatus?.Value ?? LeaveStatus.Onaylandi,
            CreatedAt = Result?.CreatedAt ?? DateTime.Now
        };
        Result.DurationMinutes = LeaveMath.CountMinutes(Result, _ctx);
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(false);
    }

    private void RefreshUnitUi()
    {
        DurationHint = IsLedgerEntry ? KindHint : (SelectedDuration?.Hint ?? "");
        OnPropertyChanged(nameof(IsHourly));
        OnPropertyChanged(nameof(IsDaily));
        OnPropertyChanged(nameof(IsRange));
        OnPropertyChanged(nameof(ShowHalfDay));
        OnPropertyChanged(nameof(ShowOptionalTimes));
        OnPropertyChanged(nameof(ShowLastHalf));
        OnPropertyChanged(nameof(ShowEndHalfPart));
        OnPropertyChanged(nameof(ShowStartTimePickers));
        OnPropertyChanged(nameof(ShowEndTimePickers));
        OnPropertyChanged(nameof(FirstHalfLabel));
        OnPropertyChanged(nameof(IsRegularLeave));
        OnPropertyChanged(nameof(IsLedgerEntry));
        OnPropertyChanged(nameof(ShowTypePicker));
        OnPropertyChanged(nameof(ShowDurationPicker));
        OnPropertyChanged(nameof(EndDateLabel));
        OnPropertyChanged(nameof(KindHint));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshPreview()
    {
        var start = DateOnly.FromDateTime(StartDate);
        var end = DateOnly.FromDateTime(EndDate);
        if (end < start)
        {
            PreviewText = "Tarih aralığı geçersiz.";
            return;
        }

        var entryKind = SelectedKind?.Value ?? LeaveEntryKind.Leave;
        var isLedger = LeaveMath.IsLedgerKind(entryKind);
        var durationKind = isLedger ? LeaveDurationKind.Hourly : (SelectedDuration?.Value ?? LeaveDurationKind.Daily);
        var startTime = durationKind == LeaveDurationKind.Hourly || HasStartTime ? ParseTime(StartHour, StartMinute) : null;
        var endTime = durationKind == LeaveDurationKind.Hourly || HasEndTime ? ParseTime(EndHour, EndMinute) : null;
        var draft = new LeaveRecord
        {
            EntryKind = entryKind,
            DurationKind = durationKind,
            StartDate = start,
            EndDate = end,
            StartTime = startTime,
            EndTime = endTime,
            StartHalf = durationKind == LeaveDurationKind.Daily && FirstHalfEnabled && startTime is null
                ? (SelectedStartHalf?.Value ?? HalfDayKind.Morning)
                : HalfDayKind.None,
            EndHalf = durationKind == LeaveDurationKind.Daily && start != end && LastHalfEnabled && endTime is null
                ? (SelectedEndHalf?.Value ?? HalfDayKind.Afternoon)
                : HalfDayKind.None,
            Status = SelectedStatus?.Value ?? LeaveStatus.Onaylandi
        };
        var minutes = LeaveMath.CountMinutes(draft, _ctx);
        var workday = LeaveMath.FormatWorkdayHours(_ctx.WorkdayHours);
        if (isLedger)
        {
            var signed = entryKind == LeaveEntryKind.TelafiliIzin ? -minutes : minutes;
            var effect = entryKind == LeaveEntryKind.TelafiliIzin
                ? "telafi bakiyesinden düşer (borç)"
                : "telafi bakiyesine eklenir (alacak)";
            var statusNote = draft.Status.AffectsBalance()
                ? "Onaylandı/Kullanıldı bakiyeye yansır"
                : "Planlandı veya İptal bakiyeye yansımaz";
            PreviewText = $"Süre: {LeaveMath.FormatMinutes(minutes, _ctx.WorkdayHours)} · {LeaveMath.FormatLedgerMinutes(signed, _ctx.WorkdayHours)} · {effect} · {statusNote}";
            return;
        }

        var weekendNote = _ctx.CountWeekends ? "hafta sonu dahil" : "yalnızca hafta içi";
        PreviewText = $"Sayılan süre: {LeaveMath.FormatMinutes(minutes, _ctx.WorkdayHours)} · iş günü {workday} · {weekendNote}";
    }

    private static string TitleFor(LeaveEntryKind kind, bool isNew) => (kind, isNew) switch
    {
        (LeaveEntryKind.TelafiliIzin, true) => "Yeni telafili izin",
        (LeaveEntryKind.TelafiliIzin, false) => "Telafili izni düzenle",
        (LeaveEntryKind.Telafi, true) => "Yeni telafi",
        (LeaveEntryKind.Telafi, false) => "Telafiyi düzenle",
        (_, true) => "Yeni izin",
        _ => "İzni düzenle"
    };

    private static TimeOnly? ParseTime(string hour, string minute)
    {
        if (!int.TryParse(hour, out var h) || !int.TryParse(minute, out var m))
        {
            return null;
        }

        h = Math.Clamp(h, 0, 23);
        m = Math.Clamp(m, 0, 59);
        return new TimeOnly(h, m);
    }
}
