namespace Planner.Core.Models;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2
}

public sealed class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public FriendshipStatus Status { get; set; }
    public bool CanViewAgenda { get; set; }
    public FriendClassKind RequesterKind { get; set; }
    public FriendClassKind AddresseeKind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum FriendClassKind
{
    Personal = 0,
    Work = 1
}

public sealed class DocumentShare
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid SharedWithUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class CollabPayload
{
    public const string Image = "[[img]]";
    public const string Call = "[[call]]";
    public const string Friend = "[[friend]]";
    public const string Task = "[[task]]";
    public const string Share = "[[share]]";
    public const string Agenda = "[[agenda]]";
    public const string File = "[[file]]";
    public const string Edit = "[[edit]]";
    public const string React = "[[react]]";

    public static bool IsImage(string? body) => body?.StartsWith(Image, StringComparison.Ordinal) == true;
    public static bool IsCall(string? body) => body?.StartsWith(Call, StringComparison.Ordinal) == true;
    public static bool IsFriend(string? body) => body?.StartsWith(Friend, StringComparison.Ordinal) == true;
    public static bool IsTask(string? body) => body?.StartsWith(Task, StringComparison.Ordinal) == true;
    public static bool IsShare(string? body) => body?.StartsWith(Share, StringComparison.Ordinal) == true;
    public static bool IsAgenda(string? body) => body?.StartsWith(Agenda, StringComparison.Ordinal) == true;
    public static bool IsFile(string? body) => body?.StartsWith(File, StringComparison.Ordinal) == true;
    public static bool IsEdit(string? body) => body?.StartsWith(Edit, StringComparison.Ordinal) == true;
    public static bool IsReact(string? body) => body?.StartsWith(React, StringComparison.Ordinal) == true;
    public static bool IsHidden(string? body) => IsEdit(body) || IsReact(body);

    public static string ImageBody(string fileName, string? data = null)
        => string.IsNullOrEmpty(data) ? Image + fileName : Image + fileName + "|" + data;

    public static (string FileName, string? Data) ParseImage(string body)
    {
        if (!IsImage(body))
        {
            return ("", null);
        }

        var rest = body[Image.Length..];
        var bar = rest.IndexOf('|');
        return bar < 0 ? (rest, null) : (rest[..bar], rest[(bar + 1)..]);
    }

    public static string FileBody(string fileName, string? data = null)
        => string.IsNullOrEmpty(data) ? File + fileName : File + fileName + "|" + data;

    public static (string FileName, string? Data) ParseFile(string body)
    {
        if (!IsFile(body))
        {
            return ("", null);
        }

        var rest = body[File.Length..];
        var bar = rest.IndexOf('|');
        return bar < 0 ? (rest, null) : (rest[..bar], rest[(bar + 1)..]);
    }
}

public sealed class FriendSignal
{
    public string T { get; set; } = "req";
    public string Name { get; set; } = "";
    public bool CanViewAgenda { get; set; }
}

public sealed class TaskSignal
{
    public string T { get; set; } = "assign";
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string? Notes { get; set; }
    public string Status { get; set; } = "";
}

public sealed class ShareSignal
{
    public string T { get; set; } = "doc";
    public string Title { get; set; } = "";
    public int Kind { get; set; }
    public string Body { get; set; } = "";
}

public sealed class AgendaItemSignal
{
    public string Title { get; set; } = "";
    public string When { get; set; } = "";
}

public sealed class AgendaSignal
{
    public string T { get; set; } = "ask";
    public List<AgendaItemSignal>? Items { get; set; }
    public string? Hint { get; set; }
}

public sealed class EditSignal
{
    public Guid Id { get; set; }
    public string Body { get; set; } = "";
}

public sealed class ReactSignal
{
    public Guid Id { get; set; }
}
