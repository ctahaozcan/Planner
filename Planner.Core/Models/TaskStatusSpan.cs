namespace Planner.Core.Models;

public sealed class TaskStatusSpan
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public PlannerTaskStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public sealed class MonthlyWorkStats
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthLabel { get; init; } = "";
    public int CreatedCount { get; init; }
    public int CompletedCount { get; init; }
    public TimeSpan AverageInProgress { get; init; }
    public string AverageText { get; init; } = "—";
}
