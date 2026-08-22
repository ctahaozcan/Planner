namespace Planner.Core.Models;

public sealed class TaskOccurrence
{
    public required PlannerTask Task { get; init; }
    public required DateOnly Date { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsCompletedOccurrence { get; init; }

    public Guid TaskId => Task.Id;
    public RecurrenceKind RecurrenceKind => Task.RecurrenceKind;
    public bool IsRecurring => Task.RecurrenceKind != RecurrenceKind.None;
    public PlannerTaskStatus Status =>
        IsCompletedOccurrence ? PlannerTaskStatus.Tamamlandi : Task.Status;

    public DateTime? ReminderAtForOccurrence
    {
        get
        {
            if (Task.Time is { } time)
            {
                return Date.ToDateTime(time);
            }

            if (Task.ReminderAt is { } reminder)
            {
                return Date.ToDateTime(TimeOnly.FromDateTime(reminder));
            }

            return null;
        }
    }
}
