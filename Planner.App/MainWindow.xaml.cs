using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Planner.App.Services;
using Planner.App.ViewModels;
using Planner.App.Views;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App;

public partial class MainWindow : Window
{
    private readonly TrayIconService _tray;
    private readonly SettingsService _settings;
    private readonly MainViewModel _vm;
    private readonly HotkeyService _hotkey;
    private readonly ReminderScheduler _scheduler;
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private readonly DocumentService _documents;
    private readonly DocumentExportService _export;
    private readonly DispatcherTimer _focusUiTimer;
    private readonly Dictionary<Guid, Window> _documentWindows = new();
    private bool _hotkeyHooked;

    public MainWindow(
        MainViewModel viewModel,
        TrayIconService tray,
        SettingsService settings,
        HotkeyService hotkey,
        ReminderScheduler scheduler,
        TaskService tasks,
        IAppDialogs dialogs,
        DocumentService documents,
        DocumentExportService export)
    {
        InitializeComponent();
        _vm = viewModel;
        _tray = tray;
        _settings = settings;
        _hotkey = hotkey;
        _scheduler = scheduler;
        _tasks = tasks;
        _dialogs = dialogs;
        _documents = documents;
        _export = export;
        DataContext = viewModel;
        viewModel.FocusQuickAddRequested += FocusQuickAdd;
        viewModel.ShowQuickAddPopupRequested += ShowQuickAddPopup;
        viewModel.Documents.OpenDocumentRequested += OpenDocument;
        viewModel.Settings.HotkeyChanged += () => _ = RegisterHotkeyAsync();
        _hotkey.Activated += () => Dispatcher.Invoke(ShowQuickAddPopup);
        _scheduler.TaskReminderRaised += OnTaskReminder;
        _scheduler.EveningCloseRaised += () => Dispatcher.Invoke(ShowEveningClose);
        _scheduler.BriefingRaised += content => Dispatcher.Invoke(() => _vm.ApplyBriefing(content));
        _focusUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusUiTimer.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                _vm.TickFocusUi();
            }
        };
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _focusUiTimer.Start();
            else _focusUiTimer.Stop();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeAsync();
        await _vm.ShowMorningBriefingIfNeededAsync(fromTray: false);
        QuickAddBox.Focus();
        _focusUiTimer.Start();
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        await RegisterHotkeyAsync();
        _hotkeyHooked = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmQueryEndSession = 0x0011;
        const int wmEndSession = 0x0016;
        if (msg is wmQueryEndSession or wmEndSession)
        {
            _tray.RequestExit();
        }

        return IntPtr.Zero;
    }

    private async Task RegisterHotkeyAsync()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        await _hotkey.RegisterFromSettingsAsync(hwnd);
    }

    public void FocusQuickAdd()
    {
        Show();
        Activate();
        QuickAddBox.Focus();
    }

    public void ShowQuickAddPopup()
    {
        if (!IsVisible)
        {
            var popup = new QuickAddWindow(_vm) { Owner = this };
            popup.Show();
            popup.Activate();
            return;
        }

        FocusQuickAdd();
    }

    private void OnSearchOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            _vm.CloseSearchCommand.Execute(null);
        }
    }

    private void OnSearchResultActivate(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox box && box.SelectedItem is SearchHit hit)
        {
            _vm.OpenSearchHitCommand.Execute(hit);
        }
    }

    private void OnTaskReminder(PlannerTask task, DateOnly date)
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible)
            {
                return;
            }

            var win = new ReminderActionWindow(task.Title, async preset =>
            {
                var until = SnoozePresets.Resolve(preset, DateTime.Now, new TimeOnly(18, 0));
                await _tasks.SnoozeAsync(task.Id, until);
            })
            {
                Owner = this
            };
            win.Show();
        });
    }

    public void ShowEveningClose()
    {
        var win = new EveningCloseWindow(_vm.Today, _tasks, _dialogs)
        {
            Owner = IsVisible ? this : null
        };
        win.Show();
    }

    private void OpenDocument(WorkspaceDocument document)
    {
        if (_documentWindows.TryGetValue(document.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Show();
            existing.Activate();
            return;
        }

        try
        {
            Window window;
            if (document.Kind == WorkspaceDocumentKind.Table)
            {
                var vm = new TableDocumentViewModel(document.Id, _documents, _export, _dialogs);
                window = new TableDocumentWindow(vm, _dialogs);
            }
            else
            {
                var vm = new TextDocumentViewModel(document.Id, _documents, _export, _dialogs);
                window = new TextDocumentWindow(vm);
            }

            window.ShowInTaskbar = true;
            window.ResizeMode = ResizeMode.CanResize;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _documentWindows[document.Id] = window;
            window.Closed += async (_, _) =>
            {
                _documentWindows.Remove(document.Id);
                if (_vm.CurrentPage == AppPage.Documents)
                {
                    await _vm.Documents.LoadAsync();
                }
            };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            _dialogs.Info("Belge açılamadı: " + ex.Message, "Belgeler");
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_tray.ExitRequested)
        {
            if (_hotkeyHooked)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                _hotkey.Unregister(hwnd);
            }

            return;
        }

        e.Cancel = true;
        var shown = await _settings.GetBoolAsync(SettingKeys.TrayTipShown);
        await _vm.ResetToHomeAsync();
        _tray.HideToTray(!shown);
        if (!shown)
        {
            await _settings.SetBoolAsync(SettingKeys.TrayTipShown, true);
        }
    }
}
