namespace Planner.Core.Models;

public enum SegmentKind
{
    Kurum = 0,
    Aile = 1,
    Arkadas = 2,
    Custom = 3
}

public sealed class Segment
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public SegmentKind Kind { get; set; }
    public string ColorHex { get; set; } = "#64748B";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public string KindLabel => Kind switch
    {
        SegmentKind.Kurum => "Kurum",
        SegmentKind.Aile => "Aile",
        SegmentKind.Arkadas => "Arkadaş",
        _ => "Özel"
    };
}

public static class SegmentIds
{
    public static readonly Guid Kurum = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Aile = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Arkadas = Guid.Parse("66666666-6666-6666-6666-666666666666");
}
