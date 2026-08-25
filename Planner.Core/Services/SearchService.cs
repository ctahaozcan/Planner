using Planner.Core.Models;

namespace Planner.Core.Services;

public enum SearchKind
{
    Task,
    Category,
    DailyNote,
    Habit,
    Leave,
    Document
}

public sealed class SearchHit
{
    public SearchKind Kind { get; init; }
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public DateOnly? Date { get; init; }
}

public sealed class SearchService
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly DailyNoteService _notes;
    private readonly HabitService _habits;
    private readonly LeaveService _leaves;
    private readonly DocumentService _documents;

    public SearchService(
        TaskService tasks,
        CategoryService categories,
        DailyNoteService notes,
        HabitService habits,
        LeaveService leaves,
        DocumentService documents)
    {
        _tasks = tasks;
        _categories = categories;
        _notes = notes;
        _habits = habits;
        _leaves = leaves;
        _documents = documents;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 1)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var task in await _tasks.GetAllAsync(ct))
        {
            if (Contains(task.Title, query) || Contains(task.Notes, query) || Contains(task.Category.Name, query))
            {
                hits.Add(new SearchHit
                {
                    Kind = SearchKind.Task,
                    Id = task.Id,
                    Title = task.Title,
                    Subtitle = $"{task.Category.Name} · {task.Date:dd.MM.yyyy}",
                    Date = task.Date
                });
            }
        }

        foreach (var cat in await _categories.GetAllAsync(ct))
        {
            if (Contains(cat.Name, query))
            {
                hits.Add(new SearchHit
                {
                    Kind = SearchKind.Category,
                    Id = cat.Id,
                    Title = cat.Name,
                    Subtitle = "Kategori"
                });
            }
        }

        foreach (var note in await _notes.SearchAsync(query, ct))
        {
            hits.Add(new SearchHit
            {
                Kind = SearchKind.DailyNote,
                Id = Guid.Empty,
                Title = note.Date.ToString("d MMMM yyyy"),
                Subtitle = Trim(note.Content, 80),
                Date = note.Date
            });
        }

        foreach (var habit in await _habits.GetAllAsync(ct))
        {
            if (Contains(habit.Name, query))
            {
                hits.Add(new SearchHit
                {
                    Kind = SearchKind.Habit,
                    Id = habit.Id,
                    Title = habit.Name,
                    Subtitle = "Alışkanlık"
                });
            }
        }

        foreach (var leave in await _leaves.GetAllAsync(ct))
        {
            if (Contains(leave.Type.Name, query)
                || Contains(leave.Note, query)
                || Contains(leave.Status.ToDisplay(), query)
                || Contains(LeaveMath.ResolveKind(leave).ToDisplay(), query))
            {
                hits.Add(new SearchHit
                {
                    Kind = SearchKind.Leave,
                    Id = leave.Id,
                    Title = LeaveMath.BannerTitle(leave),
                    Subtitle = $"{LeaveMath.FormatDateRange(leave.StartDate, leave.EndDate)} · {leave.Status.ToDisplay()}",
                    Date = leave.StartDate
                });
            }
        }

        foreach (var doc in await _documents.ListAsync(ct))
        {
            if (Contains(doc.Title, query) || (doc.Kind == WorkspaceDocumentKind.Text && Contains(doc.Body, query)))
            {
                hits.Add(new SearchHit
                {
                    Kind = SearchKind.Document,
                    Id = doc.Id,
                    Title = doc.Title,
                    Subtitle = doc.Kind == WorkspaceDocumentKind.Table ? "Tablo belgesi" : "Metin belgesi"
                });
            }
        }

        return hits.Take(40).ToList();
    }

    private static bool Contains(string? text, string query)
        => !string.IsNullOrEmpty(text) && text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
