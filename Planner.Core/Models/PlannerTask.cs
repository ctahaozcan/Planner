namespace Planner.Core.Models;

public sealed class PlannerTask
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public DateOnly Date { get; set; }
    public TimeOnly? Time { get; set; }
    public DateTime? ReminderAt { get; set; }
    public bool ReminderFired { get; set; }
    public PlannerTaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsQuickAdd { get; set; }
    public RecurrenceKind RecurrenceKind { get; set; }
    public int RecurrenceWeekdays { get; set; }
    public int? RecurrenceMonthDay { get; set; }
    public DateOnly? RecurrenceEndDate { get; set; }
    public Guid? SeriesId { get; set; }
    public bool IsSeriesException { get; set; }
    public Guid? LinkedContactId { get; set; }

    public Guid EffectiveSeriesId => SeriesId ?? Id;
    public bool IsRecurring => RecurrenceKind != RecurrenceKind.None && !IsSeriesException;
}

public sealed record QuickAddParseResult
{
    public required string Title { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly? Time { get; init; }
    public RecurrenceKind RecurrenceKind { get; init; }
    public int RecurrenceWeekdays { get; init; }
    public int? RecurrenceMonthDay { get; init; }
    public DateOnly? RecurrenceEndDate { get; init; }
    public bool Parsed { get; init; }
    public string Preview { get; init; } = "";
}
