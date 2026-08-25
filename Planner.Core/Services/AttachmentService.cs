using System.Diagnostics;
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
        var staged = StageCopy(sourcePath);
        return await CommitStagedAsync(taskId, staged, ct);
    }

    /// <summary>
    /// Kaynağı hemen uygulama klasörüne kopyalar; veritabanına yazmaz.
    /// Yeni görev kaydedilmeden önce açılabilsin diye kullanılır.
    /// </summary>
    public TaskAttachment StageCopy(string sourcePath)
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

        Directory.CreateDirectory(AppPaths.AttachmentsDirectory);
        var id = Guid.NewGuid();
        var stored = id.ToString("N") + Path.GetExtension(sourcePath);
        var dest = Path.Combine(AppPaths.AttachmentsDirectory, stored);
        File.Copy(sourcePath, dest, overwrite: false);

        return new TaskAttachment
        {
            Id = id,
            TaskId = Guid.Empty,
            OriginalName = Path.GetFileName(sourcePath),
            StoredFileName = stored,
            SizeBytes = info.Length,
            CreatedAt = DateTime.Now
        };
    }

    public async Task<TaskAttachment> CommitStagedAsync(Guid taskId, TaskAttachment staged, CancellationToken ct = default)
    {
        staged.TaskId = taskId;
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.TaskAttachments.Add(staged);
        await db.SaveChangesAsync(ct);
        return staged;
    }

    public string GetFullPath(TaskAttachment attachment)
    {
        var stored = attachment.StoredFileName;
        if (string.IsNullOrWhiteSpace(stored))
        {
            return "";
        }

        if (Path.IsPathRooted(stored) && File.Exists(stored))
        {
            return stored;
        }

        var name = Path.GetFileName(stored);
        return Path.Combine(AppPaths.AttachmentsDirectory, name);
    }

    public string? ResolveExistingPath(TaskAttachment attachment)
    {
        var primary = GetFullPath(attachment);
        if (!string.IsNullOrWhiteSpace(primary) && File.Exists(primary))
        {
            return primary;
        }

        if (!string.IsNullOrWhiteSpace(attachment.StoredFileName))
        {
            var raw = attachment.StoredFileName.Trim();
            if (File.Exists(raw))
            {
                return raw;
            }
        }

        return null;
    }

    public bool TryOpen(TaskAttachment attachment, out string error)
    {
        var path = ResolveExistingPath(attachment);
        if (path is null)
        {
            var expected = GetFullPath(attachment);
            error = string.IsNullOrWhiteSpace(attachment.OriginalName)
                ? "Ek dosyası bulunamadı. Dosya uygulama klasörüne kopyalanmamış veya silinmiş olabilir."
                : $"«{attachment.OriginalName}» açılamadı. Dosya uygulama klasöründe yok (silinmiş veya kopyalanmamış olabilir).\n\nAranan konum:\n{expected}";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
                ErrorDialog = true
            });
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"«{attachment.OriginalName}» varsayılan uygulamayla açılamadı.\n{ex.Message}";
            return false;
        }
    }

    public void DeleteStoredFile(TaskAttachment attachment)
    {
        var path = GetFullPath(attachment);
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // dosya kilitli olabilir
        }
    }

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
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
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
