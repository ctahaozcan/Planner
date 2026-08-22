using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace Planner.App.Services;

public sealed class ThemeService
{
    private const int ThemeIndex = 0;

    public async Task ApplyFromSettingsAsync(Planner.Core.Services.SettingsService settings)
    {
        var value = await settings.GetAsync(Planner.Core.Data.SettingKeys.Theme, "System");
        Apply(value);
    }

    public void Apply(string theme)
    {
        var dark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Insert(0, dict);
            return;
        }

        merged[ThemeIndex] = dict;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void TrySetDarkTitleBar(Window window, bool dark)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
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
