using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Planner.Core.Models;

namespace Planner.Core.Services;

public static class QuickAddParser
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly Regex TimeRx = new(
        @"\b([01]?\d|2[0-3])[:\.]([0-5]\d)\b",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.Create(Tr, true))
    {
        ["pazartesi"] = DayOfWeek.Monday,
        ["salı"] = DayOfWeek.Tuesday,
        ["sali"] = DayOfWeek.Tuesday,
        ["çarşamba"] = DayOfWeek.Wednesday,
        ["carsamba"] = DayOfWeek.Wednesday,
        ["perşembe"] = DayOfWeek.Thursday,
        ["persembe"] = DayOfWeek.Thursday,
        ["cuma"] = DayOfWeek.Friday,
        ["cumartesi"] = DayOfWeek.Saturday,
        ["pazar"] = DayOfWeek.Sunday,
        ["monday"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday
    };

    public static QuickAddParseResult Parse(string input, DateOnly today)
    {
        var raw = (input ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fallback("", today);
        }

        var tokens = Tokenize(raw);
        TimeOnly? time = null;
        DateOnly? date = null;
        var recurrence = RecurrenceKind.None;
        var weekdays = 0;
        int? monthDay = null;
        var used = new HashSet<int>();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (TryTime(tokens[i], out var t))
            {
                time = t;
                used.Add(i);
            }
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (used.Contains(i))
            {
                continue;
            }

            var tok = tokens[i];
            if (Is(tok, "her") && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                if (Is(next, "gün") || Is(next, "gun") || Is(next, "day"))
                {
                    recurrence = RecurrenceKind.Daily;
                    used.Add(i);
                    used.Add(i + 1);
                    date ??= today;
                    continue;
                }

                if (Weekdays.TryGetValue(Fold(next), out var wd))
                {
                    recurrence = RecurrenceKind.Weekly;
                    weekdays = WeekdayBits.For(wd);
                    used.Add(i);
                    used.Add(i + 1);
                    date ??= NextWeekday(today, wd);
                    continue;
                }

                if (Is(next, "ay") || Is(next, "month"))
                {
                    recurrence = RecurrenceKind.Monthly;
                    monthDay = today.Day;
                    used.Add(i);
                    used.Add(i + 1);
                    date ??= today;
                    continue;
                }
            }

            if (Is(tok, "haftaiçi") || Is(tok, "haftaici") || (Is(tok, "hafta") && i + 1 < tokens.Count && (Is(tokens[i + 1], "içi") || Is(tokens[i + 1], "ici") || Is(tokens[i + 1], "ici"))))
            {
                recurrence = RecurrenceKind.Weekly;
                weekdays = WeekdayBits.Weekdays;
                used.Add(i);
                if (Is(tok, "hafta") && i + 1 < tokens.Count)
                {
                    used.Add(i + 1);
                }

                date ??= today;
                continue;
            }

            if (Is(tok, "weekdays"))
            {
                recurrence = RecurrenceKind.Weekly;
                weekdays = WeekdayBits.Weekdays;
                used.Add(i);
                date ??= today;
            }
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (used.Contains(i))
            {
                continue;
            }

            var tok = tokens[i];
            if (Is(tok, "bugün") || Is(tok, "bugun") || Is(tok, "today"))
            {
                date = today;
                used.Add(i);
            }
            else if (Is(tok, "yarın") || Is(tok, "yarin") || Is(tok, "tomorrow"))
            {
                date = today.AddDays(1);
                used.Add(i);
            }
            else if (Weekdays.TryGetValue(Fold(tok), out var wd))
            {
                date = NextWeekday(today, wd);
                used.Add(i);
                if (recurrence == RecurrenceKind.None)
                {
                    // tek seferlik o haftanın günü
                }
            }
        }

        var titleParts = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!used.Contains(i))
            {
                titleParts.Add(tokens[i]);
            }
        }

        var title = string.Join(' ', titleParts).Trim();
        var parsed = date is not null || time is not null || recurrence != RecurrenceKind.None;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = parsed ? "Yeni kayıt" : raw;
        }

        date ??= today;
        if (recurrence == RecurrenceKind.Weekly && weekdays == 0 && date is { } d)
        {
            weekdays = WeekdayBits.For(d.DayOfWeek);
        }

        var preview = BuildPreview(title, date.Value, time, recurrence, weekdays, parsed);
        return new QuickAddParseResult
        {
            Title = title,
            Date = date.Value,
            Time = time,
            RecurrenceKind = recurrence,
            RecurrenceWeekdays = weekdays,
            RecurrenceMonthDay = monthDay,
            Parsed = parsed,
            Preview = preview
        };
    }

    private static QuickAddParseResult Fallback(string title, DateOnly today) => new()
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Yeni kayıt" : title,
        Date = today,
        Parsed = false,
        Preview = string.IsNullOrWhiteSpace(title) ? "" : $"Bugün · {title}"
    };

    private static List<string> Tokenize(string raw)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                Flush(sb, list);
            }
            else
            {
                sb.Append(ch);
            }
        }

        Flush(sb, list);
        return list;
    }

    private static void Flush(StringBuilder sb, List<string> list)
    {
        if (sb.Length == 0)
        {
            return;
        }

        list.Add(sb.ToString());
        sb.Clear();
    }

    private static bool TryTime(string token, out TimeOnly time)
    {
        var m = TimeRx.Match(token);
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var h)
            && int.TryParse(m.Groups[2].Value, out var min)
            && h is >= 0 and <= 23 && min is >= 0 and <= 59)
        {
            time = new TimeOnly(h, min);
            return true;
        }

        time = default;
        return false;
    }

    private static bool Is(string token, string expected)
        => string.Equals(Fold(token), Fold(expected), StringComparison.Ordinal);

    private static string Fold(string value) => value.Trim().ToLower(Tr);

    private static DateOnly NextWeekday(DateOnly today, DayOfWeek day)
    {
        var delta = ((int)day - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(delta == 0 ? 0 : delta);
    }

    private static string BuildPreview(
        string title,
        DateOnly date,
        TimeOnly? time,
        RecurrenceKind recurrence,
        int weekdays,
        bool parsed)
    {
        var when = date.ToString("d MMMM dddd", Tr);
        var clock = time is { } t ? t.ToString("HH\\:mm") : "saatsiz";
        var rec = recurrence switch
        {
            RecurrenceKind.Daily => " · her gün",
            RecurrenceKind.Weekly => $" · {WeekdayBits.ToDisplay(weekdays)}",
            RecurrenceKind.Monthly => " · her ay",
            _ => ""
        };
        var prefix = parsed ? "" : "Başlık olarak: ";
        return $"{prefix}{when} · {clock}{rec} · {title}";
    }
}
