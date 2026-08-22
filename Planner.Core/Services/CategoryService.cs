using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class CategoryService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public CategoryService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Category> AddAsync(string name, string colorHex, CancellationToken ct = default)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Kategori adı boş olamaz.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var maxOrder = await db.Categories.Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#0F766E" : colorHex,
            IsBuiltIn = false,
            SortOrder = maxOrder + 1
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task RenameAsync(Guid id, string name, string colorHex, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new InvalidOperationException("Kategori bulunamadı.");
        category.Name = name.Trim();
        category.ColorHex = colorHex;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return;
        }

        if (category.IsBuiltIn)
        {
            throw new InvalidOperationException("Varsayılan kategoriler silinemez.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Category> GetFallbackAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).FirstAsync(ct);
    }
}
