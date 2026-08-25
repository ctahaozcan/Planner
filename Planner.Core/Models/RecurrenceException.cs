namespace Planner.Core.Models;

public sealed class RecurrenceException
{
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public DateOnly Date { get; set; }
    public OccurrenceMarkKind Kind { get; set; }
    public DateTime? CompletedAt { get; set; }
}
