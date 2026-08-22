namespace Planner.Core.Models;

public sealed class EncryptedContact
{
    public Guid Id { get; set; }
    public byte[] Payload { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
