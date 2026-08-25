using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly IAppDialogs _dialogs;
    private List<PlannerTask> _all = [];

    public TasksViewModel(TaskService tasks, CategoryService categories, IAppDialogs dialogs)
    {
        _tasks = tasks;
        _categories = categories;
        _dialogs = dialogs;
        StatusFilters.Add(new StatusFilter("Tümü", null));
        StatusFilters.Add(new StatusFilter("Başlamadı", PlannerTaskStatus.Baslamadi));
        StatusFilters.Add(new StatusFilter("Devam Ediyor", PlannerTaskStatus.DevamEdiyor));
        StatusFilters.Add(new StatusFilter("Duraklatıldı", PlannerTaskStatus.Duraklatildi));
        StatusFilters.Add(new StatusFilter("Tamamlandı", PlannerTaskStatus.Tamamlandi));
        SelectedStatus = StatusFilters[0];
    }

    public ObservableCollection<TaskCardVm> Items { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<StatusFilter> StatusFilters { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private StatusFilter? _selectedStatus;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _resultText = "";

    public TaskCardCallbacks Callbacks => new()
    {
        SetStatus = SetStatusAsync,
        Edit = EditAsync,
        Details = ShowDetailsAsync,
        Delete = DeleteAsync,
        Skip = SkipAsync,
        Snooze = SnoozeAsync
    };

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(Category? value) => ApplyFilter();
    partial void OnSelectedStatusChanged(StatusFilter? value) => ApplyFilter();

    [RelayCommand]
    private async Task NewTaskAsync()
    {
        if (await _dialogs.EditTaskAsync(null, DateOnly.FromDateTime(DateTime.Today)))
        {
            await LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        var cats = await _categories.GetAllAsync();
        Categories.Clear();
        Categories.Add(new Category { Id = Guid.Empty, Name = "Tüm kategoriler" });
        foreach (var cat in cats)
        {
            Categories.Add(cat);
        }

        if (SelectedCategory is null)
        {
            SelectedCategory = Categories[0];
        }

        _all = (await _tasks.GetAllAsync()).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<PlannerTask> query = _all;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(t =>
                t.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                (t.Notes?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        if (SelectedCategory is { } cat && cat.Id != Guid.Empty)
        {
            query = query.Where(t => t.CategoryId == cat.Id);
        }

        if (SelectedStatus?.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        var list = query.ToList();
        Items.Clear();
        foreach (var task in list)
        {
            Items.Add(new TaskCardVm(task, Callbacks));
        }

        IsEmpty = Items.Count == 0;
        ResultText = $"{Items.Count} kayıt";
    }

    private async Task SetStatusAsync(TaskCardVm card, PlannerTaskStatus status)
    {
        await _tasks.SetStatusAsync(card.Id, status, card.OccurrenceDate);
        await LoadAsync();
    }

    private async Task EditAsync(TaskCardVm card)
    {
        if (await _dialogs.EditTaskAsync(card.Id, card.Date, card.OccurrenceDate))
        {
            await LoadAsync();
        }
    }

    private async Task ShowDetailsAsync(TaskCardVm card)
    {
        if (await _dialogs.ShowTaskDetailsAsync(card.Id, card.OccurrenceDate))
        {
            await EditAsync(card);
        }
    }

    private async Task DeleteAsync(TaskCardVm card)
    {
        if (card.IsRecurring)
        {
            var series = _dialogs.ConfirmSeries($"\"{card.Title}\" silinsin mi?");
            if (series is null) return;
            await _tasks.DeleteAsync(card.Id, series.Value, card.OccurrenceDate);
        }
        else
        {
            if (!_dialogs.Confirm($"\"{card.Title}\" silinsin mi?", "Görevi sil")) return;
            await _tasks.DeleteAsync(card.Id);
        }

        await LoadAsync();
    }

    private async Task SkipAsync(TaskCardVm card)
    {
        await _tasks.SkipOccurrenceAsync(card.Id, card.OccurrenceDate);
        await LoadAsync();
    }

    private async Task SnoozeAsync(TaskCardVm card, string preset)
    {
        await _tasks.SnoozeAsync(card.Id, SnoozePresets.Resolve(preset, DateTime.Now, new TimeOnly(18, 0)));
        await LoadAsync();
    }
}

public sealed class StatusFilter
{
    public StatusFilter(string name, PlannerTaskStatus? status)
    {
        Name = name;
        Status = status;
    }

    public string Name { get; }
    public PlannerTaskStatus? Status { get; }
    public override string ToString() => Name;
}
