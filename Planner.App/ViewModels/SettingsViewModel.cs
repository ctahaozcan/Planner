using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly CategoryService _categories;
    private readonly TaskService _tasks;
    private readonly VaultService _vault;
    private readonly BackupService _backup;
    private readonly IAppDialogs _dialogs;
    private readonly ITaskChangeSignal _signal;
    private bool _loading;

    public SettingsViewModel(
        SettingsService settings,
        ThemeService theme,
        CategoryService categories,
        TaskService tasks,
        VaultService vault,
        BackupService backup,
        IAppDialogs dialogs,
        ITaskChangeSignal signal)
    {
        _settings = settings;
        _theme = theme;
        _categories = categories;
        _tasks = tasks;
        _vault = vault;
        _backup = backup;
        _dialogs = dialogs;
        _signal = signal;
        DataFolder = AppPaths.Root;
        Themes.Add("System");
        Themes.Add("Light");
        Themes.Add("Dark");
        ThemeLabels.Add("Sistem");
        ThemeLabels.Add("Açık");
        ThemeLabels.Add("Koyu");
        foreach (var k in "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()))
        {
            HotkeyKeys.Add(k);
        }
        HotkeyKeys.Add("N");
    }

    public ObservableCollection<string> Themes { get; } = new();
    public ObservableCollection<string> ThemeLabels { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<string> TimeOptions { get; } = new(BuildTimes());
    public ObservableCollection<string> HotkeyKeys { get; } = new();

    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private string _selectedThemeLabel = "Sistem";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private string _dataFolder = "";
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private string _newCategoryColor = "#0F766E";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private bool _morningBriefingEnabled = true;
    [ObservableProperty] private string _morningBriefingTime = "08:00";
    [ObservableProperty] private bool _eveningCloseEnabled = true;
    [ObservableProperty] private string _eveningCloseTime = "21:00";
    [ObservableProperty] private bool _quietHoursEnabled;
    [ObservableProperty] private string _quietHoursStart = "23:00";
    [ObservableProperty] private string _quietHoursEnd = "07:00";
    [ObservableProperty] private string _workBandStart = "09:00";
    [ObservableProperty] private string _workBandEnd = "18:00";
    [ObservableProperty] private string _dayViewStart = "07:00";
    [ObservableProperty] private string _dayViewEnd = "22:00";
    [ObservableProperty] private bool _hotkeyCtrl = true;
    [ObservableProperty] private bool _hotkeyAlt = true;
    [ObservableProperty] private bool _hotkeyShift;
    [ObservableProperty] private string _hotkeyKey = "N";
    [ObservableProperty] private string _hotkeyStatus = "";
    [ObservableProperty] private int _pomodoroFocus = 25;
    [ObservableProperty] private int _pomodoroBreak = 5;

    public string HotkeyPreview => HotkeyService.Format(HotkeyCtrl, HotkeyAlt, HotkeyShift, ParseKey(HotkeyKey));

    public async Task LoadAsync()
    {
        _loading = true;
        SelectedTheme = await _settings.GetAsync(SettingKeys.Theme, "System");
        SelectedThemeLabel = SelectedTheme switch
        {
            "Dark" => "Koyu",
            "Light" => "Açık",
            _ => "Sistem"
        };
        StartWithWindows = await _settings.GetBoolAsync(SettingKeys.StartWithWindows);
        StartMinimized = await _settings.GetBoolAsync(SettingKeys.StartMinimized);
        MorningBriefingEnabled = await _settings.GetBoolAsync(SettingKeys.MorningBriefingEnabled, true);
        MorningBriefingTime = (await _settings.GetTimeAsync(SettingKeys.MorningBriefingTime, new TimeOnly(8, 0))).ToString("HH\\:mm");
        EveningCloseEnabled = await _settings.GetBoolAsync(SettingKeys.EveningCloseEnabled, true);
        EveningCloseTime = (await _settings.GetTimeAsync(SettingKeys.EveningCloseTime, new TimeOnly(21, 0))).ToString("HH\\:mm");
        QuietHoursEnabled = await _settings.GetBoolAsync(SettingKeys.QuietHoursEnabled);
        QuietHoursStart = (await _settings.GetTimeAsync(SettingKeys.QuietHoursStart, new TimeOnly(23, 0))).ToString("HH\\:mm");
        QuietHoursEnd = (await _settings.GetTimeAsync(SettingKeys.QuietHoursEnd, new TimeOnly(7, 0))).ToString("HH\\:mm");
        WorkBandStart = (await _settings.GetTimeAsync(SettingKeys.WorkBandStart, new TimeOnly(9, 0))).ToString("HH\\:mm");
        WorkBandEnd = (await _settings.GetTimeAsync(SettingKeys.WorkBandEnd, new TimeOnly(18, 0))).ToString("HH\\:mm");
        DayViewStart = (await _settings.GetTimeAsync(SettingKeys.DayViewStart, new TimeOnly(7, 0))).ToString("HH\\:mm");
        DayViewEnd = (await _settings.GetTimeAsync(SettingKeys.DayViewEnd, new TimeOnly(22, 0))).ToString("HH\\:mm");
        PomodoroFocus = await _settings.GetIntAsync(SettingKeys.PomodoroFocusMinutes, 25);
        PomodoroBreak = await _settings.GetIntAsync(SettingKeys.PomodoroBreakMinutes, 5);
        var combo = await _settings.GetAsync(SettingKeys.GlobalHotkey, "Ctrl+Alt+N");
        HotkeyCtrl = combo.Contains("Ctrl", StringComparison.OrdinalIgnoreCase);
        HotkeyAlt = combo.Contains("Alt", StringComparison.OrdinalIgnoreCase);
        HotkeyShift = combo.Contains("Shift", StringComparison.OrdinalIgnoreCase);
        HotkeyKey = combo.Split('+').LastOrDefault() ?? "N";
        if (await _settings.GetBoolAsync(SettingKeys.HotkeyRegisterFailed))
        {
            HotkeyStatus = "Kısayol kaydı başarısız. Başka bir kombinasyon deneyin.";
        }
        else
        {
            HotkeyStatus = $"Kayıtlı: {combo}";
        }

        await ReloadCategoriesAsync();
        _loading = false;
        OnPropertyChanged(nameof(HotkeyPreview));
    }

    partial void OnSelectedThemeLabelChanged(string value)
    {
        SelectedTheme = value switch
        {
            "Koyu" => "Dark",
            "Açık" => "Light",
            _ => "System"
        };
        _ = ApplyThemeAsync();
    }

    partial void OnStartWithWindowsChanged(bool value) { if (!_loading) _ = ApplyStartupAsync(); }
    partial void OnStartMinimizedChanged(bool value) { if (!_loading) _ = _settings.SetBoolAsync(SettingKeys.StartMinimized, value); }
    partial void OnMorningBriefingEnabledChanged(bool value) { if (!_loading) _ = SaveBool(SettingKeys.MorningBriefingEnabled, value); }
    partial void OnMorningBriefingTimeChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.MorningBriefingTime, value); }
    partial void OnEveningCloseEnabledChanged(bool value) { if (!_loading) _ = SaveBool(SettingKeys.EveningCloseEnabled, value); }
    partial void OnEveningCloseTimeChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.EveningCloseTime, value); }
    partial void OnQuietHoursEnabledChanged(bool value) { if (!_loading) _ = SaveBool(SettingKeys.QuietHoursEnabled, value); }
    partial void OnQuietHoursStartChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.QuietHoursStart, value); }
    partial void OnQuietHoursEndChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.QuietHoursEnd, value); }
    partial void OnWorkBandStartChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.WorkBandStart, value); }
    partial void OnWorkBandEndChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.WorkBandEnd, value); }
    partial void OnDayViewStartChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.DayViewStart, value); }
    partial void OnDayViewEndChanged(string value) { if (!_loading) _ = SaveTime(SettingKeys.DayViewEnd, value); }
    partial void OnPomodoroFocusChanged(int value) { if (!_loading) _ = _settings.SetAsync(SettingKeys.PomodoroFocusMinutes, Math.Clamp(value, 1, 180).ToString()); }
    partial void OnPomodoroBreakChanged(int value) { if (!_loading) _ = _settings.SetAsync(SettingKeys.PomodoroBreakMinutes, Math.Clamp(value, 1, 60).ToString()); }
    partial void OnHotkeyCtrlChanged(bool value) { OnPropertyChanged(nameof(HotkeyPreview)); if (!_loading) _ = SaveHotkeyAsync(); }
    partial void OnHotkeyAltChanged(bool value) { OnPropertyChanged(nameof(HotkeyPreview)); if (!_loading) _ = SaveHotkeyAsync(); }
    partial void OnHotkeyShiftChanged(bool value) { OnPropertyChanged(nameof(HotkeyPreview)); if (!_loading) _ = SaveHotkeyAsync(); }
    partial void OnHotkeyKeyChanged(string value) { OnPropertyChanged(nameof(HotkeyPreview)); if (!_loading) _ = SaveHotkeyAsync(); }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            return;
        }

        await _categories.AddAsync(NewCategoryName, NewCategoryColor);
        NewCategoryName = "";
        StatusMessage = "Kategori eklendi.";
        await ReloadCategoriesAsync();
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null)
        {
            return;
        }

        if (SelectedCategory.IsBuiltIn)
        {
            _dialogs.Info("Varsayılan kategoriler (İş, Kişisel, Özel) silinemez.");
            return;
        }

        if (!_dialogs.Confirm($"\"{SelectedCategory.Name}\" kategorisi silinsin mi? Görevler 'İş' kategorisine taşınır."))
        {
            return;
        }

        var fallback = await _categories.GetFallbackAsync();
        await _tasks.ReassignCategoryAsync(SelectedCategory.Id, fallback.Id);
        await _categories.DeleteAsync(SelectedCategory.Id);
        StatusMessage = "Kategori silindi.";
        await ReloadCategoriesAsync();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = DataFolder,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task ResetVaultAsync()
    {
        if (!_dialogs.Confirm(
                "Kişiler kasası sıfırlansın mı? Tüm kayıtlı kişiler kalıcı olarak silinir. Unutulan şifreyi kurtarmanın tek yolu budur.",
                "Kasayı sıfırla"))
        {
            return;
        }

        await _vault.ResetVaultAsync();
        StatusMessage = "Kişiler kasası sıfırlandı. Yeni şifre belirlemek için Kişiler sayfasına gidin.";
    }

    [RelayCommand]
    private async Task ExportVaultAsync()
    {
        if (!_dialogs.Confirm("Şifreyi unutursanız kişiler kurtarılamaz. Yedek dosyasını güvenli saklayın.", "Kasa yedeği"))
        {
            return;
        }

        var password = _dialogs.PromptPassword("Kasa yedeği", "Kasa şifrenizi girin (yedek bu şifreyle korunur).");
        if (password is null) return;
        var path = _dialogs.SaveFile("Kasa yedeği (*.plnvault)|*.plnvault", $"yaver-kasa-{DateTime.Now:yyyyMMdd}.plnvault");
        if (path is null) return;
        try
        {
            await _backup.ExportVaultAsync(path, password);
            StatusMessage = "Kasa yedeği kaydedildi.";
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportVaultAsync()
    {
        if (!_dialogs.Confirm("Mevcut kişiler bu yedekle değiştirilir. Devam edilsin mi?", "Kasa geri yükle"))
        {
            return;
        }

        var path = _dialogs.OpenFile("Kasa yedeği (*.plnvault)|*.plnvault");
        if (path is null) return;
        var password = _dialogs.PromptPassword("Kasa geri yükle", "Yedek şifresini girin.");
        if (password is null) return;
        try
        {
            await _backup.ImportVaultAsync(path, password);
            StatusMessage = "Kasa geri yüklendi. Kişiler sayfasından kilidi açın.";
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportDatabaseAsync()
    {
        if (!_dialogs.Confirm("Tüm veritabanı şifrelenerek dışa aktarılır. Şifreyi unutursanız bu yedek açılamaz.", "Veritabanı yedeği"))
        {
            return;
        }

        var password = _dialogs.PromptPassword("Veritabanı yedeği", "En az 8 karakterlik bir yedek şifresi girin.");
        if (password is null) return;
        var path = _dialogs.SaveFile("Veritabanı yedeği (*.plnbak)|*.plnbak", $"yaver-db-{DateTime.Now:yyyyMMdd}.plnbak");
        if (path is null) return;
        try
        {
            await _backup.ExportDatabaseAsync(path, password);
            StatusMessage = "Veritabanı yedeği kaydedildi.";
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task RestoreDatabaseAsync()
    {
        if (!_dialogs.Confirm("Mevcut veritabanının üzerine yazılır. Uygulama yeniden başlatılmalıdır.", "Veritabanını geri yükle"))
        {
            return;
        }

        var path = _dialogs.OpenFile("Veritabanı yedeği (*.plnbak)|*.plnbak");
        if (path is null) return;
        var password = _dialogs.PromptPassword("Geri yükle", "Yedek şifresini girin.");
        if (password is null) return;
        try
        {
            await _backup.RestoreDatabaseAsync(path, password);
            _dialogs.Info("Geri yükleme tamam. Yaver'ı kapatıp yeniden açın.");
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var path = _dialogs.SaveFile("JSON (*.json)|*.json", $"yaver-gorevler-{DateTime.Now:yyyyMMdd}.json");
        if (path is null) return;
        await _backup.ExportPublicJsonAsync(path);
        StatusMessage = "Görev/ajanda JSON dışa aktarıldı (kişiler dahil değil).";
    }

    private async Task SaveHotkeyAsync()
    {
        var combo = HotkeyPreview;
        await _settings.SetAsync(SettingKeys.GlobalHotkey, combo);
        HotkeyStatus = $"Kaydedildi: {combo} (yeniden kaydetmek için pencereyi bir kez gizleyip açın veya uygulamayı yeniden başlatın).";
        _signal.NotifyChanged();
        HotkeyChanged?.Invoke();
    }

    public event Action? HotkeyChanged;

    private async Task SaveBool(string key, bool value)
    {
        await _settings.SetBoolAsync(key, value);
        _signal.NotifyChanged();
    }

    private async Task SaveTime(string key, string value)
    {
        if (TimeOnly.TryParse(value, out var t))
        {
            await _settings.SetTimeAsync(key, t);
            _signal.NotifyChanged();
        }
    }

    private async Task ApplyThemeAsync()
    {
        await _settings.SetAsync(SettingKeys.Theme, SelectedTheme);
        _theme.Apply(SelectedTheme);
    }

    private async Task ApplyStartupAsync()
    {
        await _settings.SetBoolAsync(SettingKeys.StartWithWindows, StartWithWindows);
        StartupRegistration.Apply(StartWithWindows);
    }

    private async Task ReloadCategoriesAsync()
    {
        var list = await _categories.GetAllAsync();
        Categories.Clear();
        foreach (var c in list)
        {
            Categories.Add(c);
        }
    }

    private static Key ParseKey(string value)
        => Enum.TryParse<Key>(value, true, out var k) ? k : Key.N;

    private static IEnumerable<string> BuildTimes()
    {
        for (var h = 0; h < 24; h++)
        {
            yield return $"{h:00}:00";
            yield return $"{h:00}:30";
        }
    }
}
