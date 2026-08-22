using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
using Planner.App.Services;
using Planner.App.ViewModels;
using Planner.Core;
using Planner.Core.Data;
using Planner.Core.Services;

namespace Planner.App;

public partial class App : System.Windows.Application
{
    public const string AppUserModelId = ToastNotificationService.AppUserModelId;
    private const string MutexName = @"Local\Yaver.SingleInstance";
    private const string ShowEventName = @"Local\Yaver.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showLoopCts;
    private IServiceProvider? _services;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!created)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        _services = collection.BuildServiceProvider();

        AppPaths.EnsureCreated();
        await using (var db = await _services.GetRequiredService<IDbContextFactory<PlannerDbContext>>().CreateDbContextAsync())
        {
            await DatabaseInitializer.InitializeAsync(db);
        }

        var settings = _services.GetRequiredService<SettingsService>();
        var theme = _services.GetRequiredService<ThemeService>();
        await theme.ApplyFromSettingsAsync(settings);
        StartupRegistration.ClearLegacy();
        if (await settings.GetBoolAsync(SettingKeys.StartWithWindows))
        {
            StartupRegistration.Apply(true);
        }

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _services.GetRequiredService<ReminderScheduler>().Start();

        var tray = _services.GetRequiredService<TrayIconService>();
        tray.Initialize();

        var window = _services.GetRequiredService<MainWindow>();
        tray.Attach(window);
        MainWindow = window;

        _showLoopCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenForShowRequests(_showLoopCts.Token));

        var startMinimized = e.Args.Any(a => string.Equals(a, "--min", StringComparison.OrdinalIgnoreCase))
                             || await settings.GetBoolAsync(SettingKeys.StartMinimized);
        if (startMinimized)
        {
            window.ShowInTaskbar = false;
            window.Hide();
        }
        else
        {
            window.Show();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<PlannerDbContext>(options =>
            options.UseSqlite(AppPaths.ConnectionString));

        services.AddSingleton<ITaskChangeSignal, TaskChangeSignal>();
        services.AddSingleton<TaskService>();
        services.AddSingleton<CategoryService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<VaultService>();
        services.AddSingleton<HabitService>();
        services.AddSingleton<LeaveService>();
        services.AddSingleton<DailyNoteService>();
        services.AddSingleton<PriorityService>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<FocusTimerService>();
        services.AddSingleton<BriefingService>();
        services.AddSingleton<ReminderScheduler>();
        services.AddSingleton<ToastNotificationService>();
        services.AddSingleton<IReminderNotifier>(sp => sp.GetRequiredService<ToastNotificationService>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<IAppDialogs, AppDialogs>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<AgendaViewModel>();
        services.AddSingleton<WeekViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<HabitsViewModel>();
        services.AddSingleton<LeavesViewModel>();
        services.AddSingleton<ContactsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<TaskEditorViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        Dispatcher.Invoke(() =>
        {
            args.TryGetValue("action", out var action);
            if (action == "snooze"
                && args.TryGetValue("taskId", out var idText) && Guid.TryParse(idText, out var id)
                && args.TryGetValue("preset", out var preset))
            {
                var until = SnoozePresets.Resolve(preset, DateTime.Now, new TimeOnly(18, 0));
                _ = _services?.GetRequiredService<TaskService>().SnoozeAsync(id, until);
                return;
            }

            _services?.GetService<TrayIconService>()?.ShowMainWindow();
        });
    }

    private void ListenForShowRequests(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_showEvent?.WaitOne(TimeSpan.FromSeconds(2)) == true)
                {
                    Dispatcher.Invoke(() => _services?.GetService<TrayIconService>()?.ShowMainWindow());
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showLoopCts?.Cancel();
        try
        {
            _services?.GetService<ReminderScheduler>()?.Dispose();
            _services?.GetService<VaultService>()?.Lock();
            _services?.GetService<HotkeyService>()?.Dispose();
            _services?.GetService<TrayIconService>()?.Dispose();
            ToastNotificationManagerCompat.History.Clear();
        }
        catch
        {
            // kapanışta yut
        }

        _showEvent?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
