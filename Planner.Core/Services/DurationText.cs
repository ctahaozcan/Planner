namespace Planner.Core.Services;

public static class DurationText
{
    public static string Format(DateTime start, DateTime end)
        => Format(end - start);

    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = Math.Max(span.Minutes, span.TotalMinutes < 1 ? 1 : span.Minutes);
        if (span.TotalMinutes < 1)
        {
            return "1 dk";
        }

        if (days > 0)
        {
            return hours > 0 ? $"{days} gün {hours} saat" : $"{days} gün";
        }

        if (hours > 0)
        {
            return minutes > 0 ? $"{hours} saat {minutes} dk" : $"{hours} saat";
        }

        return $"{minutes} dk";
    }
}
