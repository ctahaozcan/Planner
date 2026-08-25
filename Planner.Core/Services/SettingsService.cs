using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;

namespace Planner.Core.Services;

public sealed class SettingsService
{
    private readonly IDbContextFactory<PlannerDbContext> _factory;

    public SettingsService(IDbContextFactory<PlannerDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<string> GetAsync(string key, string fallback = "")
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        return row?.Value ?? fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback = false)
        => bool.TryParse(await GetAsync(key, fallback.ToString()), out var v) ? v : fallback;

    public async Task SetAsync(string key, string value)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.Settings.Add(new Models.AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
    }

    public Task SetBoolAsync(string key, bool value) => SetAsync(key, value ? "true" : "false");

    public async Task<DateOnly?> GetDateAsync(string key)
        => DateOnly.TryParse(await GetAsync(key), out var d) ? d : null;

    public Task SetDateAsync(string key, DateOnly date) => SetAsync(key, date.ToString("yyyy-MM-dd"));

    public async Task RemoveAsync(string key)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            return;
        }

        db.Settings.Remove(row);
        await db.SaveChangesAsync();
    }
}
