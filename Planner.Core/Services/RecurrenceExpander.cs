using Planner.Core.Models;

namespace Planner.Core.Services;

public static class RecurrenceExpander
{
    public static bool OccursOn(PlannerTask task, DateOnly date)
    {
        if (task.IsSeriesException)
        {
            return task.Date == date;
        }

        if (task.RecurrenceKind == RecurrenceKind.None)
        {
            return task.Date == date;
        }

        if (date < task.Date)
        {
            return false;
        }

        if (task.RecurrenceEndDate is { } end && date > end)
        {
            return false;
        }

        return task.RecurrenceKind switch
        {
            RecurrenceKind.Daily => true,
            RecurrenceKind.Weekly => WeekdayBits.Includes(EffectiveWeekdays(task), date.DayOfWeek),
            RecurrenceKind.Monthly => date.Day == EffectiveMonthDay(task, date),
            _ => false
        };
    }

    public static IEnumerable<DateOnly> Enumerate(PlannerTask task, DateOnly from, DateOnly to)
    {
        if (task.IsSeriesException)
        {
            if (task.Date >= from && task.Date <= to)
            {
                yield return task.Date;
            }

            yield break;
        }

        if (task.RecurrenceKind == RecurrenceKind.None)
        {
            if (task.Date >= from && task.Date <= to)
            {
                yield return task.Date;
            }

            yield break;
        }

        var start = task.Date > from ? task.Date : from;
        var end = to;
        if (task.RecurrenceEndDate is { } recEnd && recEnd < end)
        {
            end = recEnd;
        }

        if (start > end)
        {
            yield break;
        }

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (OccursOn(task, d))
            {
                yield return d;
            }
        }
    }

    public static DateOnly? NextOccurrence(PlannerTask task, DateOnly after, IReadOnlySet<DateOnly>? skip)
    {
        var cap = task.RecurrenceEndDate ?? after.AddYears(2);
        for (var d = after.AddDays(1); d <= cap; d = d.AddDays(1))
        {
            if (OccursOn(task, d) && (skip is null || !skip.Contains(d)))
            {
                return d;
            }
        }

        return null;
    }

    public static DateOnly? NextOnOrAfter(PlannerTask task, DateOnly from, IReadOnlySet<DateOnly>? skip)
    {
        if (OccursOn(task, from) && (skip is null || !skip.Contains(from)))
        {
            return from;
        }

        return NextOccurrence(task, from, skip);
    }

    private static int EffectiveWeekdays(PlannerTask task)
        => task.RecurrenceWeekdays == 0 ? WeekdayBits.For(task.Date.DayOfWeek) : task.RecurrenceWeekdays;

    private static int EffectiveMonthDay(PlannerTask task, DateOnly date)
    {
        var day = task.RecurrenceMonthDay ?? task.Date.Day;
        var dim = DateTime.DaysInMonth(date.Year, date.Month);
        return Math.Min(day, dim);
    }
}
