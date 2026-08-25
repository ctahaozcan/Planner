namespace Planner.Core.Models;

public sealed record CompletedWorkItem
{
    public Guid TaskId { get; init; }
    public string Title { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public string CategoryColor { get; init; } = "#0F766E";
    public DateTime CompletedAt { get; init; }
    public DateTime StartedAt { get; init; }
    public DateOnly OccurrenceDate { get; init; }
    public string DurationText { get; init; } = "";
    public bool IsRecurringOccurrence { get; init; }
}
