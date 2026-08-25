namespace Planner.Core.Models;

public sealed class Organization
{
    public Guid Id { get; set; }
    public Guid? SegmentId { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; }
}
