using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core;

namespace Planner.Core.Services;

public sealed class AttachmentService
{
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public AttachmentService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
        Directory.CreateDirectory(AppPaths.AttachmentsDirectory);
    }

    public async Task<IReadOnlyList<TaskAttachment>> GetForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TaskAttachments.AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<TaskAttachment> AddAsync(Guid taskId, string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Dosya bulunamadı.", sourcePath);
        }

        var info = new FileInfo(sourcePath);
        if (info.Length > MaxFileBytes)
        {
            throw new InvalidOperationException("Dosya 20 MB sınırını aşıyor.");
        }

        var id = Guid.NewGuid();
        var stored = id.ToString("N") + Path.GetExtension(sourcePath);
        var dest = Path.Combine(AppPaths.AttachmentsDirectory, stored);
        File.Copy(sourcePath, dest, overwrite: false);

        var row = new TaskAttachment
        {
            Id = id,
            TaskId = taskId,
            OriginalName = Path.GetFileName(sourcePath),
            StoredFileName = stored,
            SizeBytes = info.Length,
            CreatedAt = DateTime.Now
        };

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TaskAttachments.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public string GetFullPath(TaskAttachment attachment)
        => Path.Combine(AppPaths.AttachmentsDirectory, attachment.StoredFileName);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return;
        }

        var path = GetFullPath(row);
        db.TaskAttachments.Remove(row);
        await db.SaveChangesAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // dosya kilitli olabilir
        }
    }
}
