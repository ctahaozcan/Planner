using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Planner.Core.Data;
using Planner.Core.Services;

namespace Planner.App.Services;

public sealed class HotkeyService : IDisposable
{
    public const int HotkeyId = 0x4901;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const int WmHotkey = 0x0312;

    private readonly SettingsService _settings;
    private HwndSource? _source;
    private bool _registered;

    public HotkeyService(SettingsService settings)
    {
        _settings = settings;
    }

    public event Action? Activated;

    public bool IsRegistered => _registered;
    public string? LastError { get; private set; }

    public async Task<bool> RegisterFromSettingsAsync(IntPtr hwnd)
    {
        Unregister(hwnd);
        var combo = await _settings.GetAsync(SettingKeys.GlobalHotkey, "Ctrl+Alt+N");
        if (!TryParse(combo, out var mods, out var key))
        {
            LastError = "Kısayol okunamadı.";
            await _settings.SetBoolAsync(SettingKeys.HotkeyRegisterFailed, true);
            return false;
        }

        _source?.RemoveHook(Hook);
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
        _registered = RegisterHotKey(hwnd, HotkeyId, mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!_registered)
        {
            LastError = $"«{combo}» sistemde kayıtlı olamaz (başka bir uygulama kullanıyor olabilir). Ayarlar’dan başka kombinasyon seçin.";
            await _settings.SetBoolAsync(SettingKeys.HotkeyRegisterFailed, true);
            return false;
        }

        LastError = null;
        await _settings.SetBoolAsync(SettingKeys.HotkeyRegisterFailed, false);
        return true;
    }

    public void Unregister(IntPtr hwnd)
    {
        if (_registered && hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(hwnd, HotkeyId);
        }

        _registered = false;
        if (_source is not null)
        {
            _source.RemoveHook(Hook);
            _source = null;
        }
    }

    public static bool TryParse(string combo, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(combo))
        {
            return false;
        }

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts.Take(parts.Length - 1))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    return false;
            }
        }

        if (!Enum.TryParse(parts[^1], ignoreCase: true, out key) || key == Key.None)
        {
            return false;
        }

        return modifiers != 0;
    }

    public static string Format(bool ctrl, bool alt, bool shift, Key key)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Activated?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source?.RemoveHook(Hook);
        _source = null;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
