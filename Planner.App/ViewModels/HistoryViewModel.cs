using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public sealed class HistoryRangeOption
{
    public HistoryRangeOption(string name, HistoryRangeKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public string Name { get; }
    public HistoryRangeKind Kind { get; }
}

public enum HistoryRangeKind
{
    Week,
    Month,
    All
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;

    public HistoryViewModel(TaskService tasks, IAppDialogs dialogs)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        Ranges.Add(new HistoryRangeOption("Bu hafta", HistoryRangeKind.Week));
        Ranges.Add(new HistoryRangeOption("Bu ay", HistoryRangeKind.Month));
        Ranges.Add(new HistoryRangeOption("Tümü", HistoryRangeKind.All));
        SelectedRange = Ranges[0];
    }

    public ObservableCollection<HistoryRangeOption> Ranges { get; } = new();
    public ObservableCollection<HistoryRowVm> Items { get; } = new();
    public ObservableCollection<MonthlyWorkStats> MonthlyStats { get; } = new();

    [ObservableProperty] private HistoryRangeOption? _selectedRange;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _hasMonthlyStats;

    partial void OnSelectedRangeChanged(HistoryRangeOption? value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        var (from, to) = RangeBounds(SelectedRange?.Kind ?? HistoryRangeKind.Week);
        var rows = await _tasks.GetCompletedHistoryAsync(from, to);
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(new HistoryRowVm(row));
        }

        IsEmpty = Items.Count == 0;
        ResultText = IsEmpty
            ? "Bu aralıkta tamamlanan görev yok."
            : $"{Items.Count} tamamlanan görev";

        MonthlyStats.Clear();
        foreach (var month in await _tasks.GetMonthlyStatsAsync(12))
        {
            MonthlyStats.Add(month);
        }

        HasMonthlyStats = MonthlyStats.Count > 0;
    }

    [RelayCommand]
    private async Task OpenAsync(object? item)
    {
        if (item is not HistoryRowVm row)
        {
            return;
        }

        if (await _dialogs.ShowTaskDetailsAsync(row.TaskId, row.OccurrenceDate))
        {
            await LoadAsync();
        }
    }

    private static (DateOnly From, DateOnly To) RangeBounds(HistoryRangeKind kind)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return kind switch
        {
            HistoryRangeKind.Month => (new DateOnly(today.Year, today.Month, 1), today),
            HistoryRangeKind.All => (new DateOnly(2000, 1, 1), today),
            _ => (StartOfWeek(today), today)
        };
    }

    private static DateOnly StartOfWeek(DateOnly day)
    {
        var diff = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return day.AddDays(-diff);
    }
}

public sealed class HistoryRowVm
{
    public HistoryRowVm(CompletedWorkItem item)
    {
        TaskId = item.TaskId;
        Title = item.Title;
        CategoryName = item.CategoryName;
        CategoryColor = item.CategoryColor;
        DurationText = item.DurationText;
        OccurrenceDate = item.OccurrenceDate;
        CompletedText = item.CompletedAt.ToString("d MMMM yyyy HH:mm", new System.Globalization.CultureInfo("tr-TR"));
        Subtitle = item.IsRecurringOccurrence
            ? $"{item.CategoryName} · yineleyen · {item.OccurrenceDate.ToString("d MMMM", new System.Globalization.CultureInfo("tr-TR"))}"
            : item.CategoryName;
    }

    public Guid TaskId { get; }
    public DateOnly OccurrenceDate { get; }
    public string Title { get; }
    public string CategoryName { get; }
    public string CategoryColor { get; }
    public string DurationText { get; }
    public string CompletedText { get; }
    public string Subtitle { get; }
}
