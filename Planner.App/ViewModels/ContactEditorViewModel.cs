using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.Core.Models;

namespace Planner.App.ViewModels;

public partial class ContactEditorViewModel : ObservableObject
{
    public ContactEditorViewModel(ContactRecord? existing)
    {
        if (existing is null)
        {
            Title = "Yeni kişi";
            Result = new ContactRecord { Id = Guid.NewGuid(), CreatedAt = DateTime.Now };
        }
        else
        {
            Title = "Kişiyi düzenle";
            Result = Clone(existing);
        }

        Name = Result.Name;
        Phone = Result.Phone ?? "";
        Email = Result.Email ?? "";
        Address = Result.Address ?? "";
        Relationship = Result.Relationship ?? "";
        Notes = Result.Notes ?? "";
        HasBirthday = Result.Birthday is not null;
        Birthday = (Result.Birthday ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        HasAnniversary = Result.Anniversary is not null;
        Anniversary = (Result.Anniversary ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        HasLastContact = Result.LastContactDate is not null;
        LastContact = (Result.LastContactDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        FollowUpThisWeek = Result.FollowUpThisWeek;
    }

    public string Title { get; }
    public ContactRecord? Result { get; private set; }
    public event Action<bool>? CloseRequested;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _relationship = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasBirthday;
    [ObservableProperty] private DateTime _birthday = DateTime.Today;
    [ObservableProperty] private bool _hasAnniversary;
    [ObservableProperty] private DateTime _anniversary = DateTime.Today;
    [ObservableProperty] private bool _hasLastContact;
    [ObservableProperty] private DateTime _lastContact = DateTime.Today;
    [ObservableProperty] private bool _followUpThisWeek;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Ad soyad gerekli.";
            return;
        }

        Result = new ContactRecord
        {
            Id = Result?.Id ?? Guid.NewGuid(),
            Name = Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim(),
            Relationship = string.IsNullOrWhiteSpace(Relationship) ? null : Relationship.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            CreatedAt = Result?.CreatedAt ?? DateTime.Now,
            Birthday = HasBirthday ? DateOnly.FromDateTime(Birthday) : null,
            Anniversary = HasAnniversary ? DateOnly.FromDateTime(Anniversary) : null,
            LastContactDate = HasLastContact ? DateOnly.FromDateTime(LastContact) : null,
            FollowUpThisWeek = FollowUpThisWeek
        };
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(false);
    }

    private static ContactRecord Clone(ContactRecord existing) => new()
    {
        Id = existing.Id,
        Name = existing.Name,
        Phone = existing.Phone,
        Email = existing.Email,
        Address = existing.Address,
        Relationship = existing.Relationship,
        Notes = existing.Notes,
        CreatedAt = existing.CreatedAt,
        Birthday = existing.Birthday,
        Anniversary = existing.Anniversary,
        LastContactDate = existing.LastContactDate,
        FollowUpThisWeek = existing.FollowUpThisWeek
    };
}
