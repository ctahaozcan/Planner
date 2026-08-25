using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class DocumentService
{
    public const int MaxTableRows = 400;
    public const int MaxTableCols = 40;
    public const int MaxSheets = 12;
    public const int DefaultTableCols = 26;
    public const int DefaultTableRows = 60;

    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly UserAccountService _users;

    public DocumentService(IDbContextFactory<PlannerDbContext> factory, UserAccountService users)
    {
        _factory = factory;
        _users = users;
    }

    public async Task<IReadOnlyList<WorkspaceDocument>> ListAsync(CancellationToken ct = default)
    {
        var me = _users.Current?.Id;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.WorkspaceDocuments.AsNoTracking();
        if (me is Guid uid)
        {
            var sharedIds = await db.DocumentShares.AsNoTracking()
                .Where(s => s.SharedWithUserId == uid)
                .Select(s => s.DocumentId)
                .ToListAsync(ct);
            query = query.Where(d => d.OwnerUserId == null || d.OwnerUserId == uid || sharedIds.Contains(d.Id));
        }

        return await query.OrderByDescending(d => d.UpdatedAt).ToListAsync(ct);
    }

    public async Task<WorkspaceDocument?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WorkspaceDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<WorkspaceDocument> CreateAsync(WorkspaceDocumentKind kind, string? title = null, CancellationToken ct = default)
    {
        var doc = new WorkspaceDocument
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(title)
                ? (kind == WorkspaceDocumentKind.Table ? "Adsız e-tablo" : "Adsız belge")
                : title.Trim(),
            Kind = kind,
            Body = kind == WorkspaceDocumentKind.Table ? TableDocument.EmptyJson() : "",
            OwnerUserId = _users.Current?.Id,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.WorkspaceDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }

    public async Task SaveAsync(Guid id, string title, string body, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkspaceDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.Title = string.IsNullOrWhiteSpace(title) ? row.Title : title.Trim();
        row.Body = body ?? "";
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(Guid id, string title, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkspaceDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null)
        {
            return;
        }

        row.Title = string.IsNullOrWhiteSpace(title) ? row.Title : title.Trim();
        row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkspaceDocument?> DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var src = await db.WorkspaceDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (src is null)
        {
            return null;
        }

        var copy = new WorkspaceDocument
        {
            Id = Guid.NewGuid(),
            Title = $"{src.Title} kopyası",
            Kind = src.Kind,
            Body = src.Body,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            OwnerUserId = _users.Current?.Id
        };
        db.WorkspaceDocuments.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy;
    }

    public async Task ShareAsync(Guid documentId, Guid withUserId, CancellationToken ct = default)
    {
        var me = _users.Current?.Id ?? throw new InvalidOperationException("Oturum yok.");
        if (me == withUserId)
        {
            throw new InvalidOperationException("Kendinize paylaşamazsınız.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = await db.WorkspaceDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                  ?? throw new InvalidOperationException("Belge yok.");
        if (doc.OwnerUserId is Guid owner && owner != me)
        {
            throw new InvalidOperationException("Yalnızca sahibi paylaşabilir.");
        }

        doc.OwnerUserId ??= me;
        var exists = await db.DocumentShares.AnyAsync(s => s.DocumentId == documentId && s.SharedWithUserId == withUserId, ct);
        if (exists)
        {
            return;
        }

        db.DocumentShares.Add(new DocumentShare
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            OwnerUserId = me,
            SharedWithUserId = withUserId,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.WorkspaceDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null)
        {
            return;
        }

        db.WorkspaceDocuments.Remove(row);
        var shares = db.DocumentShares.Where(s => s.DocumentId == id);
        db.DocumentShares.RemoveRange(shares);
        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<Guid>> SharedOutIdsAsync(CancellationToken ct = default)
    {
        var me = _users.Current?.Id;
        if (me is null)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = await db.DocumentShares.AsNoTracking()
            .Where(s => s.OwnerUserId == me)
            .Select(s => s.DocumentId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<WorkspaceDocument> ImportSharedAsync(string title, WorkspaceDocumentKind kind, string body, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "Paylaşılan dosya" : title.Trim();
        if (!name.Contains("paylaşılan", StringComparison.CurrentCultureIgnoreCase))
        {
            name += " (paylaşılan)";
        }

        var doc = await CreateAsync(kind, name, ct);
        await SaveAsync(doc.Id, doc.Title, body ?? "", ct);
        return await GetAsync(doc.Id, ct) ?? doc;
    }
}

public sealed class TableSheet
{
    public string Name { get; set; } = "Sayfa1";
    public List<string> Headers { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
}

public sealed class TableDocument
{
    public List<TableSheet> Sheets { get; set; } = [];
    public int ActiveIndex { get; set; }
    public List<string> Headers { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];

    public IReadOnlyList<TableSheet> GetSheets()
    {
        if (Sheets.Count > 0)
        {
            return Sheets;
        }

        if (Headers.Count > 0)
        {
            return [new TableSheet { Name = "Sayfa1", Headers = Headers, Rows = Rows }];
        }

        return Empty().Sheets;
    }

    public TableSheet ActiveSheet()
    {
        var sheets = GetSheets().ToList();
        if (sheets.Count == 0)
        {
            sheets = Empty().Sheets;
        }

        var i = Math.Clamp(ActiveIndex, 0, sheets.Count - 1);
        return sheets[i];
    }

    public static TableDocument Empty(int cols = DocumentService.DefaultTableCols, int rows = DocumentService.DefaultTableRows)
    {
        var sheet = EmptySheet("Sayfa1", cols, rows);
        return new TableDocument
        {
            Sheets = [sheet],
            ActiveIndex = 0,
            Headers = sheet.Headers,
            Rows = sheet.Rows
        };
    }

    public static TableSheet EmptySheet(string name, int cols = DocumentService.DefaultTableCols, int rows = DocumentService.DefaultTableRows)
    {
        cols = Math.Clamp(cols, 1, DocumentService.MaxTableCols);
        rows = Math.Clamp(rows, 1, DocumentService.MaxTableRows);
        var headers = Enumerable.Range(0, cols).Select(ColumnName).ToList();
        var data = Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Repeat("", cols).ToList())
            .ToList();
        return new TableSheet { Name = string.IsNullOrWhiteSpace(name) ? "Sayfa1" : name.Trim(), Headers = headers, Rows = data };
    }

    public static string EmptyJson() => System.Text.Json.JsonSerializer.Serialize(Empty());

    public static TableDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty();
        }

        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize<TableDocument>(json) ?? Empty();
            if (doc.Sheets.Count == 0)
            {
                if (doc.Headers.Count == 0)
                {
                    return Empty();
                }

                doc.Sheets = [NormalizeSheet(new TableSheet { Name = "Sayfa1", Headers = doc.Headers, Rows = doc.Rows })];
            }
            else
            {
                doc.Sheets = doc.Sheets.Take(DocumentService.MaxSheets).Select(NormalizeSheet).ToList();
            }

            doc.ActiveIndex = Math.Clamp(doc.ActiveIndex, 0, doc.Sheets.Count - 1);
            var active = doc.Sheets[doc.ActiveIndex];
            doc.Headers = active.Headers;
            doc.Rows = active.Rows;
            return doc;
        }
        catch
        {
            return Empty();
        }
    }

    private static TableSheet NormalizeSheet(TableSheet sheet)
    {
        var headers = (sheet.Headers ?? []).Take(DocumentService.MaxTableCols).ToList();
        if (headers.Count == 0)
        {
            headers = Enumerable.Range(0, DocumentService.DefaultTableCols).Select(ColumnName).ToList();
        }

        var rows = (sheet.Rows ?? []).Take(DocumentService.MaxTableRows)
            .Select(r =>
            {
                var row = (r ?? []).Take(headers.Count).Select(c => c ?? "").ToList();
                while (row.Count < headers.Count)
                {
                    row.Add("");
                }

                return row;
            })
            .ToList();
        if (rows.Count == 0)
        {
            rows = Enumerable.Range(0, DocumentService.DefaultTableRows)
                .Select(_ => Enumerable.Repeat("", headers.Count).ToList())
                .ToList();
        }

        var name = string.IsNullOrWhiteSpace(sheet.Name) ? "Sayfa1" : sheet.Name.Trim();
        if (name.Length > 31)
        {
            name = name[..31];
        }

        return new TableSheet { Name = name, Headers = headers, Rows = rows };
    }

    public void SyncLegacy()
    {
        if (Sheets.Count == 0)
        {
            return;
        }

        ActiveIndex = Math.Clamp(ActiveIndex, 0, Sheets.Count - 1);
        var active = Sheets[ActiveIndex];
        Headers = active.Headers;
        Rows = active.Rows;
    }

    public string ToJson()
    {
        SyncLegacy();
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    public static string ColumnName(int index)
    {
        var n = index + 1;
        var name = "";
        while (n > 0)
        {
            n--;
            name = (char)('A' + n % 26) + name;
            n /= 26;
        }

        return name;
    }

    public static string UniqueSheetName(IEnumerable<string> existing, string baseName = "Sayfa")
    {
        var set = existing.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!set.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; i < 200; i++)
        {
            var name = $"{baseName}{i}";
            if (!set.Contains(name))
            {
                return name;
            }
        }

        return $"{baseName}{DateTime.Now.Ticks % 1000}";
    }
}
