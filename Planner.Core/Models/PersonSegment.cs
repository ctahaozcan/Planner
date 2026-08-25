namespace Planner.Core.Models;

public sealed class PersonSegment
{
    public Guid PersonId { get; set; }
    public Guid SegmentId { get; set; }
}

public sealed class PersonOrganization
{
    public Guid PersonId { get; set; }
    public Guid OrganizationId { get; set; }
    public bool IsPrimary { get; set; }
    public Guid? ManagerPersonId { get; set; }
    public string? Title { get; set; }
}
