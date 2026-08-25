using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Chat;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class AuthViewModel : ObservableObject
{
    private readonly UserAccountService _users;
    private readonly SettingsService _settings;
    private readonly ServerChatClient _server;

    public AuthViewModel(UserAccountService users, SettingsService settings, ServerChatClient server)
    {
        _users = users;
        _settings = settings;
        _server = server;
    }

    public event Action<bool>? CloseRequested;

    [ObservableProperty] private bool _isRegister = true;
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _rememberMe = true;
    [ObservableProperty] private string _modeLabel = "Hesap oluştur";
    [ObservableProperty] private string _usage = AccountUsageKinds.Personal;
    [ObservableProperty] private string _serverUrl = ChatRoutes.DefaultClientUrl;
    [ObservableProperty] private string _companyHint = "İş kaydı için yönetim panelinden davet kodu gerekir. Boş kadroyu herkes seçemez.";
    [ObservableProperty] private string _inviteCode = "";
    [ObservableProperty] private string _inviteSummary = "";
    [ObservableProperty] private InvitePreviewDto? _invitePreview;

    public bool IsLogin => !IsRegister;
    public bool NeedsCompany => IsRegister && AccountUsageKinds.IncludesWork(Usage);
    public bool HasInvitePreview => InvitePreview is not null;
    public bool UsageIsPersonal => AccountUsageKinds.Normalize(Usage) == AccountUsageKinds.Personal;
    public bool UsageIsWork => AccountUsageKinds.Normalize(Usage) == AccountUsageKinds.Work;
    public bool UsageIsBoth => AccountUsageKinds.Normalize(Usage) == AccountUsageKinds.Both;

    partial void OnIsRegisterChanged(bool value)
    {
        ModeLabel = value ? "Hesap oluştur" : "Giriş yap";
        Error = "";
        OnPropertyChanged(nameof(IsLogin));
        OnPropertyChanged(nameof(NeedsCompany));
    }

    partial void OnUsageChanged(string value)
    {
        OnPropertyChanged(nameof(NeedsCompany));
        OnPropertyChanged(nameof(UsageIsPersonal));
        OnPropertyChanged(nameof(UsageIsWork));
        OnPropertyChanged(nameof(UsageIsBoth));
        if (NeedsCompany)
        {
            CompanyHint = "Davet kodunu yazıp «Kadroya bak» deyin. E-posta şirket alan adına ait olmalı.";
        }
    }

    public async Task InitializeAsync()
    {
        RememberMe = await _settings.GetBoolAsync(SettingKeys.RememberLogin, false);
        ServerUrl = await _settings.GetAsync(SettingKeys.ChatServerUrl, ChatRoutes.DefaultClientUrl);
        if (await _users.HasAnyAsync())
        {
            IsRegister = false;
            var last = await _settings.GetAsync(SettingKeys.ChatServerUsername, "");
            if (string.IsNullOrWhiteSpace(last))
            {
                var list = await _users.ListAsync();
                last = list.FirstOrDefault()?.Username ?? "";
            }

            Username = last;
        }
    }

    [RelayCommand]
    private void ShowRegister() => IsRegister = true;

    [RelayCommand]
    private void ShowLogin() => IsRegister = false;

    [RelayCommand]
    private void UsePersonal() => Usage = AccountUsageKinds.Personal;

    [RelayCommand]
    private void UseWork() => Usage = AccountUsageKinds.Work;

    [RelayCommand]
    private void UseBoth() => Usage = AccountUsageKinds.Both;

    [RelayCommand]
    private async Task LookupInviteAsync()
    {
        Error = "";
        InvitePreview = null;
        InviteSummary = "";
        OnPropertyChanged(nameof(HasInvitePreview));
        if (string.IsNullOrWhiteSpace(InviteCode))
        {
            CompanyHint = "Yönetim panelinden davet kodu alın.";
            return;
        }

        try
        {
            await _settings.SetAsync(SettingKeys.ChatServerUrl, (ServerUrl ?? "").Trim());
            InvitePreview = await _server.PreviewInviteAsync(InviteCode);
            InviteSummary = InvitePreview.CompanyName + " · " + InvitePreview.UnitName + " · " + InvitePreview.PositionTitle
                            + (string.IsNullOrWhiteSpace(InvitePreview.Email) ? "" : " · kilit: " + InvitePreview.Email);
            CompanyHint = "E-posta @" + InvitePreview.Domain + " ile eşleşmeli.";
            OnPropertyChanged(nameof(HasInvitePreview));
        }
        catch (Exception ex)
        {
            CompanyHint = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Error = "";
        Status = "";
        try
        {
            await _settings.SetAsync(SettingKeys.ChatServerUrl, (ServerUrl ?? "").Trim());
            if (IsRegister)
            {
                await RegisterAsync();
            }
            else
            {
                await LoginAsync();
            }

            if (_users.Current is null)
            {
                return;
            }

            await _settings.SetBoolAsync(SettingKeys.RememberLogin, RememberMe);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private async Task RegisterAsync()
    {
        UserAccountService.ValidateRegister(FirstName, LastName, Email, UserAccountService.NormalizeUsername(Username), Password);
        var usage = AccountUsageKinds.Normalize(Usage);
        LocalOrgMembership? membership = null;
        if (AccountUsageKinds.IncludesWork(usage))
        {
            if (InvitePreview is null)
            {
                await LookupInviteAsync();
            }

            if (InvitePreview is null)
            {
                throw new InvalidOperationException("Geçerli bir davet kodu girin. Kadroya yönetim paneli oturtur.");
            }

            var auth = await _server.RegisterAsync(
                UserAccountService.NormalizeUsername(Username),
                Password,
                null,
                Email,
                FirstName,
                LastName,
                usage,
                InvitePreview.CompanyId,
                InvitePreview.UnitId,
                InvitePreview.PositionId,
                InviteCode);
            await _server.SaveSessionAsync(auth);
            membership = FromAuth(auth);
            Status = "Kurum hesabı davetle kaydedildi: " + InvitePreview.PositionTitle;
        }

        await _users.RegisterAsync(FirstName, LastName, Email, Username, Password, membership);
        if (membership is null)
        {
            Status = await TryServerRegisterPersonalAsync();
        }
    }

    private async Task LoginAsync()
    {
        var localOk = await _users.LoginAsync(Username, Password);
        string serverNote;
        try
        {
            var auth = await _server.LoginAsync(UserAccountService.NormalizeUsername(Username), Password);
            await _server.SaveSessionAsync(auth);
            if (!localOk)
            {
                var existing = await _users.FindByUsernameAsync(Username);
                if (existing is not null)
                {
                    await _users.SignInUserAsync(existing);
                }
                else
                {
                    var parts = (auth.DisplayName ?? Username).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    await _users.RegisterAsync(
                        parts.ElementAtOrDefault(0) ?? Username,
                        parts.ElementAtOrDefault(1) ?? "-",
                        Email.Length > 0 ? Email : $"{Username}@yerel",
                        Username,
                        Password,
                        FromAuth(auth));
                }
            }
            else
            {
                await _users.ApplyMembershipAsync(FromAuth(auth));
            }

            serverNote = "Sunucu oturumu açıldı.";
        }
        catch
        {
            serverNote = localOk ? "Yerel giriş (sunucu yok veya reddetti)." : "";
        }

        if (_users.Current is null)
        {
            Error = "Kullanıcı adı veya şifre yanlış.";
        }
        else
        {
            Status = serverNote;
        }
    }

    private async Task<string> TryServerRegisterPersonalAsync()
    {
        try
        {
            var auth = await _server.RegisterAsync(
                UserAccountService.NormalizeUsername(Username),
                Password,
                null,
                Email,
                FirstName,
                LastName,
                AccountUsageKinds.Personal);
            await _server.SaveSessionAsync(auth);
            return "Yerel hesap hazır. Sunucuya da kayıt olundu.";
        }
        catch
        {
            return "Yerel hesap hazır. Sunucu yoksa veya reddederse yine de giriş yapılır.";
        }
    }

    private static LocalOrgMembership FromAuth(AuthResponse auth)
        => new()
        {
            UsageKind = AccountUsageKinds.Normalize(auth.Usage),
            CompanyId = auth.CompanyId,
            UnitId = auth.UnitId,
            PositionId = auth.PositionId,
            CompanyName = auth.CompanyName,
            UnitName = auth.UnitName,
            PositionTitle = auth.PositionTitle
        };
}
