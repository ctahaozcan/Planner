using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly IAppDialogs _dialogs;
    private readonly TrayIconService _tray;
    private readonly SearchService _search;
    private readonly FocusTimerService _focus;
    private readonly SettingsService _settings;
    private readonly BriefingService _briefingService;

    public MainViewModel(
        TodayViewModel today,
        AgendaViewModel agenda,
        WeekViewModel week,
        TasksViewModel tasksView,
        HabitsViewModel habits,
        LeavesViewModel leaves,
        ContactsViewModel contacts,
        SettingsViewModel settingsVm,
        TaskService tasks,
        CategoryService categories,
        IAppDialogs dialogs,
        TrayIconService tray,
        SearchService search,
        FocusTimerService focus,
        SettingsService settings,
        BriefingService briefing)
    {
        Today = today;
        Agenda = agenda;
        Week = week;
        Tasks = tasksView;
        Habits = habits;
        Leaves = leaves;
        Contacts = contacts;
        Settings = settingsVm;
        _tasks = tasks;
        _categories = categories;
        _dialogs = dialogs;
        _tray = tray;
        _search = search;
        _focus = focus;
        _settings = settings;
        _briefingService = briefing;
        CurrentView = today;
        CurrentPage = AppPage.Today;
        Week.OpenDayRequested += d => _ = OpenDayAsync(d);
        Contacts.VaultUnlocked += () => _ = OnVaultUnlockedAsync();
        _focus.Changed += RefreshFocusUi;
    }

    public TodayViewModel Today { get; }
    public AgendaViewModel Agenda { get; }
    public WeekViewModel Week { get; }
    public TasksViewModel Tasks { get; }
    public HabitsViewModel Habits { get; }
    public LeavesViewModel Leaves { get; }
    public ContactsViewModel Contacts { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private AppPage _currentPage;
    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _quickAddText = "";
    [ObservableProperty] private string _quickAddPreview = "";
    [ObservableProperty] private string _headerTitle = "Bugün";
    [ObservableProperty] private string _headerSubtitle = "";
    [ObservableProperty] private bool _showQuickAdd = true;
    [ObservableProperty] private bool _searchOpen;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _focusText = "Odak";
    [ObservableProperty] private bool _focusRunning;
    [ObservableProperty] private string _focusPhaseText = "";
    [ObservableProperty] private BriefingContent? _briefing;
    [ObservableProperty] private bool _showBriefingBanner;

    public ObservableCollection<SearchHit> SearchResults { get; } = new();

    public event Action? FocusQuickAddRequested;
    public event Action? ShowQuickAddPopupRequested;

    public bool IsTodayPage => CurrentPage == AppPage.Today;
    public bool IsAgendaPage => CurrentPage == AppPage.Agenda;
    public bool IsWeekPage => CurrentPage == AppPage.Week;
    public bool IsTasksPage => CurrentPage == AppPage.Tasks;
    public bool IsHabitsPage => CurrentPage == AppPage.Habits;
    public bool IsLeavesPage => CurrentPage == AppPage.Leaves;
    public bool IsContactsPage => CurrentPage == AppPage.Contacts;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    public async Task InitializeAsync()
    {
        await Today.LoadAsync();
        UpdateHeader();
        RefreshFocusUi();
        await _tray.RefreshTooltipAsync();
    }

    public async Task ShowMorningBriefingIfNeededAsync(bool fromTray)
    {
        var enabled = await _settings.GetBoolAsync(SettingKeys.MorningBriefingEnabled, true);
        if (!enabled) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (await _settings.GetDateAsync(SettingKeys.LastBriefingDate) == today && Briefing is not null)
        {
            return;
        }

        Briefing = await _briefingService.BuildAsync(today);
        ShowBriefingBanner = true;
        if (fromTray)
        {
            // toast already from scheduler; banner when they open
        }
    }

    public void ApplyBriefing(BriefingContent content)
    {
        Briefing = content;
        ShowBriefingBanner = true;
    }

    [RelayCommand]
    private void DismissBriefing() => ShowBriefingBanner = false;

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(IsTodayPage));
        OnPropertyChanged(nameof(IsAgendaPage));
        OnPropertyChanged(nameof(IsWeekPage));
        OnPropertyChanged(nameof(IsTasksPage));
        OnPropertyChanged(nameof(IsHabitsPage));
        OnPropertyChanged(nameof(IsLeavesPage));
        OnPropertyChanged(nameof(IsContactsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        ShowQuickAdd = value is AppPage.Today or AppPage.Agenda or AppPage.Week or AppPage.Tasks;
        UpdateHeader();
    }

    partial void OnQuickAddTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            QuickAddPreview = "";
            return;
        }

        var parsed = QuickAddParser.Parse(value, DateOnly.FromDateTime(DateTime.Today));
        QuickAddPreview = parsed.Preview;
    }

    [RelayCommand] private async Task GoTodayAsync() { CurrentPage = AppPage.Today; CurrentView = Today; await Today.LoadAsync(); }
    [RelayCommand] private async Task GoAgendaAsync() { CurrentPage = AppPage.Agenda; CurrentView = Agenda; await Agenda.LoadAsync(); }
    [RelayCommand] private async Task GoWeekAsync() { CurrentPage = AppPage.Week; CurrentView = Week; await Week.LoadAsync(); }
    [RelayCommand] private async Task GoTasksAsync() { CurrentPage = AppPage.Tasks; CurrentView = Tasks; await Tasks.LoadAsync(); }
    [RelayCommand] private async Task GoHabitsAsync() { CurrentPage = AppPage.Habits; CurrentView = Habits; await Habits.LoadAsync(); }
    [RelayCommand] private async Task GoLeavesAsync() { CurrentPage = AppPage.Leaves; CurrentView = Leaves; await Leaves.LoadAsync(); }
    [RelayCommand] private async Task GoContactsAsync() { CurrentPage = AppPage.Contacts; CurrentView = Contacts; await Contacts.LoadAsync(); }
    [RelayCommand] private async Task GoSettingsAsync() { CurrentPage = AppPage.Settings; CurrentView = Settings; await Settings.LoadAsync(); }

    public async Task OpenDayAsync(DateOnly date)
    {
        Today.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        await GoTodayAsync();
    }

    [RelayCommand]
    private async Task QuickAddAsync()
    {
        var text = QuickAddText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await NewDetailedAsync();
            return;
        }

        var cats = await _categories.GetAllAsync();
        var fallback = cats.FirstOrDefault(c => c.Name == "İş") ?? cats.First();
        var parsed = QuickAddParser.Parse(text, DateOnly.FromDateTime(DateTime.Today));
        if (CurrentPage == AppPage.Today && !parsed.Parsed)
        {
            parsed = parsed with { Date = DateOnly.FromDateTime(Today.SelectedDate) };
        }

        DateTime? reminder = parsed.Time is { } t ? parsed.Date.ToDateTime(t) : null;
        await _tasks.AddAsync(new PlannerTask
        {
            Title = parsed.Title,
            CategoryId = fallback.Id,
            Date = parsed.Date,
            Time = parsed.Time,
            ReminderAt = reminder,
            Status = PlannerTaskStatus.Baslamadi,
            IsQuickAdd = true,
            RecurrenceKind = parsed.RecurrenceKind,
            RecurrenceWeekdays = parsed.RecurrenceWeekdays,
            RecurrenceMonthDay = parsed.RecurrenceMonthDay,
            RecurrenceEndDate = parsed.RecurrenceEndDate
        });

        QuickAddText = "";
        QuickAddPreview = "";
        await RefreshCurrentAsync();
    }

    [RelayCommand]
    private async Task FocusQuickAddAsync()
    {
        if (!ShowQuickAdd)
        {
            await GoTodayAsync();
        }

        FocusQuickAddRequested?.Invoke();
    }

    public void RequestGlobalQuickAdd() => ShowQuickAddPopupRequested?.Invoke();

    [RelayCommand]
    private async Task NewDetailedAsync()
    {
        DateOnly? date = CurrentPage == AppPage.Today
            ? DateOnly.FromDateTime(Today.SelectedDate)
            : DateOnly.FromDateTime(DateTime.Today);
        if (await _dialogs.EditTaskAsync(null, date))
        {
            await RefreshCurrentAsync();
        }
    }

    [RelayCommand]
    private void OpenSearch()
    {
        SearchOpen = true;
        SearchQuery = "";
        SearchResults.Clear();
    }

    [RelayCommand]
    private void CloseSearch()
    {
        SearchOpen = false;
        SearchQuery = "";
        SearchResults.Clear();
    }

    partial void OnSearchQueryChanged(string value) => _ = RunSearchAsync(value);

    private async Task RunSearchAsync(string value)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var hit in await _search.SearchAsync(value))
        {
            SearchResults.Add(hit);
        }
    }

    [RelayCommand]
    private async Task OpenSearchHitAsync(object? hitObj)
    {
        if (hitObj is not SearchHit hit)
        {
            return;
        }

        SearchOpen = false;
        switch (hit.Kind)
        {
            case SearchKind.Task:
                if (hit.Date is { } td)
                {
                    await OpenDayAsync(td);
                }
                else
                {
                    await GoTasksAsync();
                }

                break;
            case SearchKind.DailyNote:
                if (hit.Date is { } nd) await OpenDayAsync(nd);
                break;
            case SearchKind.Habit:
                await GoHabitsAsync();
                break;
            case SearchKind.Leave:
                await GoLeavesAsync();
                break;
            case SearchKind.Contact:
                await GoContactsAsync();
                break;
            case SearchKind.Category:
                await GoTasksAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task ToggleFocusAsync()
    {
        if (_focus.IsRunning)
        {
            _focus.Stop();
            return;
        }

        var minutes = await _settings.GetIntAsync(SettingKeys.PomodoroFocusMinutes, 25);
        Guid? taskId = null;
        string? title = null;
        var today = await _tasks.GetOccurrencesForDateAsync(DateOnly.FromDateTime(DateTime.Today));
        var current = today.FirstOrDefault(o => o.Status == PlannerTaskStatus.DevamEdiyor)
                      ?? today.FirstOrDefault(o => o.Status != PlannerTaskStatus.Tamamlandi);
        if (current is not null)
        {
            taskId = current.TaskId;
            title = current.Task.Title;
            await _tasks.SetStatusAsync(current.TaskId, PlannerTaskStatus.DevamEdiyor, current.Date);
        }

        _focus.StartFocus(minutes, taskId, title);
        await RefreshCurrentAsync();
    }

    [RelayCommand]
    private async Task StartBreakAsync()
    {
        var minutes = await _settings.GetIntAsync(SettingKeys.PomodoroBreakMinutes, 5);
        _focus.StartBreak(minutes);
    }

    public void TickFocusUi()
    {
        if (_focus.IsRunning)
        {
            RefreshFocusUi();
        }
    }

    private void RefreshFocusUi()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshFocusUi);
            return;
        }

        FocusRunning = _focus.IsRunning;
        if (!_focus.IsRunning)
        {
            FocusText = "Odak";
            FocusPhaseText = "";
            return;
        }

        var left = _focus.Remaining;
        FocusText = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
        FocusPhaseText = _focus.Phase == FocusPhase.Focus
            ? (_focus.LinkedTaskTitle is { } t ? $"Odak · {t}" : "Odak")
            : "Mola";
    }

    public async Task RefreshCurrentAsync()
    {
        switch (CurrentPage)
        {
            case AppPage.Today: await Today.LoadAsync(); break;
            case AppPage.Agenda: await Agenda.LoadAsync(); break;
            case AppPage.Week: await Week.LoadAsync(); break;
            case AppPage.Tasks: await Tasks.LoadAsync(); break;
            case AppPage.Habits: await Habits.LoadAsync(); break;
            case AppPage.Leaves: await Leaves.LoadAsync(); break;
            case AppPage.Contacts: await Contacts.LoadAsync(); break;
            case AppPage.Settings: await Settings.LoadAsync(); break;
        }

        UpdateHeader();
        await _tray.RefreshTooltipAsync();
    }

    private async Task OnVaultUnlockedAsync()
    {
        await Today.LoadAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var events = await _briefingService.GetContactEventsAsync(today);
        if (events.Count > 0 && await _settings.GetDateAsync(SettingKeys.LastBirthdayNotifyDate) != today)
        {
            await _settings.SetDateAsync(SettingKeys.LastBirthdayNotifyDate, today);
            Briefing = await _briefingService.BuildAsync(today);
            ShowBriefingBanner = true;
        }
    }

    private void UpdateHeader()
    {
        (HeaderTitle, HeaderSubtitle) = CurrentPage switch
        {
            AppPage.Today => (Today.IsToday ? "Bugün" : Today.DateLabel,
                Today.IsToday ? Today.DateLabel : "Başka bir günün ajandası"),
            AppPage.Agenda => ("Ajanda", "Önümüzdeki 14 gün — iş, kişisel ve özel durumlar"),
            AppPage.Week => ("Hafta", Week.RangeLabel),
            AppPage.Tasks => ("Görevler", "Tüm kayıtlar, durum ve kategori süzgeçleri"),
            AppPage.Habits => ("Alışkanlıklar", "Günlük / hafta içi işaretler ve seri"),
            AppPage.Leaves => ("İzinler", "İzin, telafili izin, telafi ve bakiyeler"),
            AppPage.Contacts => ("Kişiler", "Şifreli kasa — telefon kaybında başvuru defteri"),
            AppPage.Settings => ("Ayarlar", "Tema, kısayol, sessiz saat, yedekleme"),
            _ => ("Yaver", "")
        };
    }
}
