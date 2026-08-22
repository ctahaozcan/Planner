using Planner.Core.Data;

namespace Planner.Core.Services;

public static class TimeSetting
{
    public static TimeOnly Parse(string? value, TimeOnly fallback)
        => TimeOnly.TryParse(value, out var t) ? t : fallback;

    public static DateTime OnDate(DateOnly date, TimeOnly time) => date.ToDateTime(time);

    public static DateTime NextAt(TimeOnly time, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var candidate = today.ToDateTime(time);
        return candidate > now ? candidate : today.AddDays(1).ToDateTime(time);
    }

    public static bool InQuietHours(DateTime now, bool enabled, TimeOnly start, TimeOnly end)
    {
        if (!enabled)
        {
            return false;
        }

        var t = TimeOnly.FromDateTime(now);
        if (start == end)
        {
            return true;
        }

        return start < end
            ? t >= start && t < end
            : t >= start || t < end;
    }

    public static DateTime NextQuietEnd(DateTime now, TimeOnly start, TimeOnly end)
    {
        var today = DateOnly.FromDateTime(now);
        var t = TimeOnly.FromDateTime(now);
        if (start < end)
        {
            var endAt = today.ToDateTime(end);
            return now < endAt ? endAt : today.AddDays(1).ToDateTime(end);
        }

        // overnight e.g. 23:00–07:00
        if (t >= start)
        {
            return today.AddDays(1).ToDateTime(end);
        }

        if (t < end)
        {
            return today.ToDateTime(end);
        }

        return today.ToDateTime(end).AddDays(1);
    }
}

public static class SettingsTimeExtensions
{
    public static async Task<TimeOnly> GetTimeAsync(this SettingsService settings, string key, TimeOnly fallback)
        => TimeSetting.Parse(await settings.GetAsync(key, fallback.ToString("HH\\:mm")), fallback);

    public static Task SetTimeAsync(this SettingsService settings, string key, TimeOnly value)
        => settings.SetAsync(key, value.ToString("HH\\:mm"));

    public static async Task<int> GetIntAsync(this SettingsService settings, string key, int fallback)
        => int.TryParse(await settings.GetAsync(key, fallback.ToString()), out var v) ? v : fallback;
}
