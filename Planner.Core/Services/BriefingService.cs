using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class BriefingContent
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<TaskOccurrence> TodayTasks { get; init; } = [];
    public IReadOnlyList<TaskOccurrence> TimedReminders { get; init; } = [];
    public IReadOnlyList<PlannerTask> Overdue { get; init; } = [];
    public IReadOnlyList<TaskOccurrence> Priorities { get; init; } = [];
    public IReadOnlyList<HabitSnapshot> Habits { get; init; } = [];
    public IReadOnlyList<LeaveRecord> TodayLeaves { get; init; } = [];
    public string Summary { get; init; } = "";
    public string ToastBody { get; init; } = "";
}

public sealed class BriefingService
{
    private readonly TaskService _tasks;
    private readonly PriorityService _priorities;
    private readonly HabitService _habits;
    private readonly LeaveService _leaves;

    public BriefingService(
        TaskService tasks,
        PriorityService priorities,
        HabitService habits,
        LeaveService leaves)
    {
        _tasks = tasks;
        _priorities = priorities;
        _habits = habits;
        _leaves = leaves;
    }

    public async Task<BriefingContent> BuildAsync(DateOnly date, CancellationToken ct = default)
    {
        var today = await _tasks.GetOccurrencesForDateAsync(date, ct);
        var overdue = await _tasks.GetOverdueAsync(date, ct);
        var pins = await _priorities.GetAsync(date, ct);
        var pinIds = pins.Select(p => p.TaskId).ToHashSet();
        var prio = today.Where(o => pinIds.Contains(o.TaskId)).OrderBy(o => pins.First(p => p.TaskId == o.TaskId).Slot).ToList();
        var timed = today.Where(o => o.Task.Time is not null && o.Status != PlannerTaskStatus.Tamamlandi).ToList();
        var habits = await _habits.GetSnapshotsAsync(date, ct);
        var leaves = await _leaves.GetForDateAsync(date, ct);

        var open = today.Count(t => t.Status != PlannerTaskStatus.Tamamlandi);
        var leaveTitles = leaves.Select(LeaveMath.BannerTitle).ToList();
        var summaryParts = new List<string>();
        if (leaveTitles.Count > 0)
        {
            summaryParts.Add(string.Join(" · ", leaveTitles));
        }

        summaryParts.Add($"{open} açık görev");
        summaryParts.Add($"{overdue.Count} gecikmiş");
        var summary = string.Join(" · ", summaryParts);

        var toastParts = new List<string>();
        if (leaveTitles.Count > 0)
        {
            toastParts.Add(leaveTitles[0]);
        }

        toastParts.Add($"{open} açık görev");
        if (overdue.Count > 0) toastParts.Add($"{overdue.Count} gecikmiş");
        if (prio.Count > 0) toastParts.Add($"{prio.Count} öncelik");

        return new BriefingContent
        {
            Date = date,
            TodayTasks = today,
            TimedReminders = timed,
            Overdue = overdue,
            Priorities = prio,
            Habits = habits,
            TodayLeaves = leaves,
            Summary = summary,
            ToastBody = string.Join(" · ", toastParts)
        };
    }
}
