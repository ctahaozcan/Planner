using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Resources;
using Planner.Core.Models;
using Planner.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfApp = System.Windows.Application;

namespace Planner.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskService _tasks;
    private Forms.NotifyIcon? _icon;
    private MainWindow? _window;
    private bool _exitRequested;

    public TrayIconService(TaskService tasks)
    {
        _tasks = tasks;
    }

    public bool ExitRequested => _exitRequested;

    public void Initialize()
    {
        _icon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "Yaver — arka planda anımsatıcılar açık",
            Icon = LoadIcon()
        };

        _icon.DoubleClick += (_, _) => ShowMainWindow();
        _icon.ContextMenuStrip = BuildMenu();
        _ = RefreshTooltipAsync();
    }

    public void Attach(MainWindow window)
    {
        _window = window;
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Dispatcher.Invoke(() =>
        {
            var wasHidden = !_window.IsVisible || _window.WindowState == WindowState.Minimized;
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.ShowInTaskbar = true;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            if (wasHidden && _window.DataContext is ViewModels.MainViewModel vm)
            {
                _ = vm.ResetToHomeAsync();
            }
        });
    }

    public void HideToTray(bool showBalloon)
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowInTaskbar = false;
        _window.Hide();
        if (showBalloon && _icon is not null)
        {
            _icon.BalloonTipTitle = "Yaver arka planda";
            _icon.BalloonTipText = "Pencere kapandı; anımsatıcılar tepsi simgesinden çalışmaya devam eder. Çıkmak için sağ tıklayıp Çıkış'ı seçin.";
            _icon.ShowBalloonTip(4000);
        }
    }

    public async Task RefreshTooltipAsync()
    {
        if (_icon is null)
        {
            return;
        }

        var count = await _tasks.CountForDateAsync(DateOnly.FromDateTime(DateTime.Today));
        var text = count == 0
            ? "Yaver — bugün açık görev yok"
            : $"Yaver — bugün {count} açık görev";
        _icon.Text = text.Length > 63 ? text[..63] : text;
        RebuildMenu(count);
    }

    public void RequestExit()
    {
        _exitRequested = true;
        Dispose();
        WpfApp.Current.Shutdown();
    }

    private void RebuildMenu(int todayCount)
    {
        if (_icon is null)
        {
            return;
        }

        _icon.ContextMenuStrip = BuildMenu(todayCount);
    }

    private Forms.ContextMenuStrip BuildMenu(int? todayCount = null)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Yaver'ı aç", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Hızlı ekle", null, (_, _) =>
        {
            ShowMainWindow();
            _window?.FocusQuickAdd();
        });
        var todayText = todayCount is null ? "Bugünün özeti" : $"Bugün · {todayCount} açık görev";
        menu.Items.Add(todayText, null, (_, _) => ShowMainWindow());
        menu.Items.Add("Odak başlat / durdur", null, (_, _) =>
        {
            ShowMainWindow();
            if (_window?.DataContext is ViewModels.MainViewModel vm)
            {
                vm.ToggleFocusCommand.Execute(null);
            }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => RequestExit());
        return menu;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            StreamResourceInfo? resource = WpfApp.GetResourceStream(uri);
            if (resource is not null)
            {
                return new Icon(resource.Stream);
            }
        }
        catch
        {
            // yedek
        }

        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Drawing.Color.FromArgb(15, 118, 110));
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
