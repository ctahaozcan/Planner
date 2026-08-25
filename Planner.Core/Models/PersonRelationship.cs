namespace Planner.Core.Models;

public static class RelationshipLabels
{
    public static readonly string[] Presets =
    [
        "kız arkadaş",
        "erkek arkadaş",
        "eş",
        "abla",
        "ağabey",
        "kardeş",
        "ebeveyn",
        "çocuk",
        "iş arkadaşı",
        "arkadaş",
        "diğer"
    ];
}

public sealed class PersonRelationship
{
    public Guid Id { get; set; }
    public Guid FromPersonId { get; set; }
    public Guid ToPersonId { get; set; }
    public string Label { get; set; } = "";
    public bool IsDirected { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
