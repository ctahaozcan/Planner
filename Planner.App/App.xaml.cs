using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
using Planner.App.Services;
using Planner.App.ViewModels;
using Planner.App.Views;
using Planner.Core;
using Planner.Core.Data;
using Planner.Core.Services;

namespace Planner.App;

public partial class App : System.Windows.Application
{
    public const string AppUserModelId = ToastNotificationService.AppUserModelId;
    public const string MutexName = @"Local\Yaver.SingleInstance";
    public const string ShutdownEventName = @"Local\Yaver.Shutdown";
    private const string ShowEventName = @"Local\Yaver.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _shutdownEvent;
    private CancellationTokenSource? _showLoopCts;
    private IServiceProvider? _services;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var created);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName);

        var shutdownRequested = e.Args.Any(a =>
            string.Equals(a, "--shutdown", StringComparison.OrdinalIgnoreCase));
        if (shutdownRequested)
        {
            _shutdownEvent.Set();
            Shutdown();
            return;
        }

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

        var users = _services.GetRequiredService<UserAccountService>();
        await users.EnsureDefaultAsync(Environment.UserName);
        if (!users.IsSignedIn)
        {
            var auth = new AuthWindow(_services.GetRequiredService<AuthViewModel>())
            {
                ShowInTaskbar = true
            };
            var ok = auth.ShowDialog() == true && users.IsSignedIn;
            if (!ok)
            {
                Shutdown();
                return;
            }
        }

        var toast = _services.GetRequiredService<ToastNotificationService>();
        _services.GetRequiredService<ITaskChangeSignal>().Info += toast.ShowInfo;

        await _services.GetRequiredService<TaskService>().BackfillStatusSpansAsync();
        _ = _services.GetRequiredService<OrgWorkService>();
        await _services.GetRequiredService<ChatHub>().StartAsync();
        await _services.GetRequiredService<OrgWorkService>().SyncInboxAsync();
        _ = _services.GetRequiredService<CallService>();

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
        services.AddSingleton<UserAccountService>();
        services.AddSingleton<LeaveService>();
        services.AddSingleton<DailyNoteService>();
        services.AddSingleton<PriorityService>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<DocumentExportService>();
        services.AddSingleton<FriendshipService>();
        services.AddSingleton<ChatStore>();
        services.AddSingleton<LanChatService>();
        services.AddSingleton<ServerChatClient>();
        services.AddSingleton<ChatHub>();
        services.AddSingleton<CallService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<FocusTimerService>();
        services.AddSingleton<BriefingService>();
        services.AddSingleton<TaskRolloverService>();
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
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<HabitsViewModel>();
        services.AddSingleton<LeavesViewModel>();
        services.AddSingleton<DocumentsViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<OrgWorkService>();
        services.AddSingleton<OrgWorkViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<AuthViewModel>();
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
            if (action is "friendAccept" or "friendDecline" or "friendRequest"
                && args.TryGetValue("peerKey", out var peerKey))
            {
                args.TryGetValue("name", out var name);
                _ = HandleFriendToastAsync(action, peerKey, name ?? "");
            }
        });
    }

    private async Task HandleFriendToastAsync(string action, string peerKey, string name)
    {
        if (_services is null)
        {
            return;
        }

        var main = _services.GetRequiredService<MainViewModel>();
        await main.OpenChatAsync();
        await main.Chat.HandleFriendToastAsync(action, peerKey, name);
    }

    private void ListenForShowRequests(CancellationToken ct)
    {
        var handles = new WaitHandle[] { _showEvent!, _shutdownEvent! };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var signaled = WaitHandle.WaitAny(handles, TimeSpan.FromSeconds(2));
                if (signaled == 0)
                {
                    Dispatcher.Invoke(() => _services?.GetService<TrayIconService>()?.ShowMainWindow());
                }
                else if (signaled == 1)
                {
                    Dispatcher.Invoke(() => _services?.GetService<TrayIconService>()?.RequestExit());
                    break;
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
            _services?.GetService<CallService>()?.Dispose();
            _services?.GetService<ChatHub>()?.Dispose();
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
        _shutdownEvent?.Dispose();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // mutex bu süreçte alınmamış olabilir (--shutdown)
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
