namespace Planner.Core.Models;

public sealed class ContactRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Relationship { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly? Birthday { get; set; }
    public DateOnly? Anniversary { get; set; }
    public DateOnly? LastContactDate { get; set; }
    public bool FollowUpThisWeek { get; set; }
}
