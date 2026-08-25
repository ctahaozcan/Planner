using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Planner.App.Services;

public sealed class ThemeService
{
    private const int ThemeIndex = 0;
    private static bool _classHandlerRegistered;

    public static event EventHandler? Changed;

    public bool IsDark { get; private set; }
    public string CurrentKey { get; private set; } = "Light";

    public ThemeService()
    {
        if (_classHandlerRegistered)
        {
            return;
        }

        _classHandlerRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, e) =>
            {
                if (e.Source is Window window)
                {
                    TrySetDarkTitleBar(window, IsDark);
                }
            }));
    }

    public async Task ApplyFromSettingsAsync(Planner.Core.Services.SettingsService settings)
    {
        var value = await settings.GetAsync(Planner.Core.Data.SettingKeys.Theme, "System");
        Apply(value);
    }

    public void Apply(string theme)
    {
        CurrentKey = string.IsNullOrWhiteSpace(theme) ? "System" : theme;
        IsDark = CurrentKey switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };

        var uri = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Insert(0, dict);
        }
        else
        {
            merged[ThemeIndex] = dict;
        }

        foreach (Window window in Application.Current.Windows)
        {
            TrySetDarkTitleBar(window, IsDark);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    public static SolidColorBrush Brush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush found)
        {
            return found;
        }

        var brush = new SolidColorBrush(fallback);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    public static Color ColorOf(SolidColorBrush brush) => brush.Color;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void TrySetDarkTitleBar(Window window, bool dark)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
                window.SourceInitialized += (_, _) => TrySetDarkTitleBar(window, dark);
                return;
            }

            var useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(helper.Handle, 20, ref useDark, sizeof(int));
        }
        catch
        {
            // Başlık çubuğu teması isteğe bağlıdır.
        }
    }
}
