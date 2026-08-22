using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class ContactsViewModel : ObservableObject
{
    private readonly VaultService _vault;
    private readonly IAppDialogs _dialogs;
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private List<ContactRecord> _all = [];

    public ContactsViewModel(
        VaultService vault,
        IAppDialogs dialogs,
        TaskService tasks,
        CategoryService categories)
    {
        _vault = vault;
        _dialogs = dialogs;
        _tasks = tasks;
        _categories = categories;
    }

    public ObservableCollection<ContactRecord> Contacts { get; } = new();

    [ObservableProperty] private bool _hasPassword;
    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ContactRecord? _selectedContact;
    [ObservableProperty] private DateTime _followUpDate = DateTime.Today.AddDays(1);

    public bool ShowSetup => !HasPassword;
    public bool ShowUnlock => HasPassword && !IsUnlocked;
    public bool ShowList => IsUnlocked;

    public event Action? VaultUnlocked;

    partial void OnHasPasswordChanged(bool value) => RaisePanels();
    partial void OnIsUnlockedChanged(bool value) => RaisePanels();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        HasPassword = await _vault.HasPasswordAsync();
        IsUnlocked = _vault.IsUnlocked;
        ErrorMessage = "";
        if (IsUnlocked)
        {
            await ReloadContactsAsync();
        }
        else
        {
            Contacts.Clear();
        }
    }

    public async Task SetupAsync(string password, string confirm)
    {
        ErrorMessage = "";
        if (password != confirm)
        {
            ErrorMessage = "Şifreler eşleşmiyor.";
            return;
        }

        try
        {
            IsBusy = true;
            await _vault.SetupPasswordAsync(password);
            HasPassword = true;
            IsUnlocked = true;
            await ReloadContactsAsync();
            VaultUnlocked?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UnlockAsync(string password)
    {
        ErrorMessage = "";
        try
        {
            IsBusy = true;
            var ok = await _vault.UnlockAsync(password);
            if (!ok)
            {
                ErrorMessage = "Şifre yanlış.";
                return;
            }

            IsUnlocked = true;
            await ReloadContactsAsync();
            VaultUnlocked?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Lock()
    {
        _vault.Lock();
        IsUnlocked = false;
        Contacts.Clear();
        SelectedContact = null;
        StatusText = "Kasa kilitlendi.";
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!IsUnlocked) return;
        var editor = new ContactEditorViewModel(null);
        if (await ShowEditorAsync(editor) && editor.Result is { } contact)
        {
            await _vault.AddAsync(contact);
            await ReloadContactsAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedContact is null) return;
        var editor = new ContactEditorViewModel(SelectedContact);
        if (await ShowEditorAsync(editor) && editor.Result is { } contact)
        {
            await _vault.UpdateAsync(contact);
            await ReloadContactsAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedContact is null) return;
        if (!_dialogs.Confirm($"\"{SelectedContact.Name}\" kişisi silinsin mi? Bu işlem geri alınamaz.", "Kişiyi sil"))
        {
            return;
        }

        await _vault.DeleteAsync(SelectedContact.Id);
        await ReloadContactsAsync();
    }

    [RelayCommand]
    private async Task FollowUpAsync()
    {
        if (SelectedContact is null) return;
        var cats = await _categories.GetAllAsync();
        var cat = cats.FirstOrDefault(c => c.Name == "Kişisel") ?? cats.First();
        var date = DateOnly.FromDateTime(FollowUpDate);
        await _tasks.AddAsync(new PlannerTask
        {
            Title = $"Takip: {SelectedContact.Name}",
            Notes = "Kişi kartından oluşturuldu. Kasa verisi içermez.",
            CategoryId = cat.Id,
            Date = date,
            ReminderAt = date.ToDateTime(new TimeOnly(10, 0)),
            Status = PlannerTaskStatus.Baslamadi,
            LinkedContactId = SelectedContact.Id
        });
        StatusText = $"Takip görevi {date:dd.MM.yyyy} için eklendi.";
    }

    private static Task<bool> ShowEditorAsync(ContactEditorViewModel editor)
    {
        var window = new Views.ContactEditorWindow
        {
            DataContext = editor,
            Owner = System.Windows.Application.Current.MainWindow
        };
        editor.CloseRequested += result =>
        {
            try { window.DialogResult = result; } catch { window.Close(); }
        };
        return Task.FromResult(window.ShowDialog() == true);
    }

    private async Task ReloadContactsAsync()
    {
        _all = (await _vault.GetContactsAsync()).ToList();
        ApplyFilter();
        StatusText = $"{_all.Count} kişi (diskte şifreli)";
    }

    private void ApplyFilter()
    {
        IEnumerable<ContactRecord> query = _all;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(c =>
                c.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                (c.Phone?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (c.Email?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (c.Relationship?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        Contacts.Clear();
        foreach (var c in query)
        {
            Contacts.Add(c);
        }
    }

    private void RaisePanels()
    {
        OnPropertyChanged(nameof(ShowSetup));
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowList));
    }
}
