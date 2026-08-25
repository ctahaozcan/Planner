using System.Globalization;

namespace Planner.Core.Models;

public enum LeaveStatus
{
    Planlandi = 0,
    Onaylandi = 1,
    Kullanildi = 2,
    Iptal = 3,
    Reddedildi = 4
}

public enum HalfDayKind
{
    None = 0,
    Morning = 1,
    Afternoon = 2
}

public enum LeaveDurationKind
{
    Hourly = 0,
    Daily = 1,
    Range = 2
}

public enum LeaveEntryKind
{
    Leave = 0,
    TelafiliIzin = 1,
    Telafi = 2
}

public sealed class LeaveType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#0F766E";
    public bool IsBuiltIn { get; set; }
    public bool CountsAgainstAnnual { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LeaveRecord
{
    public Guid Id { get; set; }
    public Guid TypeId { get; set; }
    public LeaveType Type { get; set; } = null!;
    public LeaveEntryKind EntryKind { get; set; } = LeaveEntryKind.Leave;
    public LeaveDurationKind DurationKind { get; set; } = LeaveDurationKind.Daily;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public HalfDayKind StartHalf { get; set; }
    public HalfDayKind EndHalf { get; set; }
    public string? Note { get; set; }
    public LeaveStatus Status { get; set; }
    public int DurationMinutes { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? ServerLeaveId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class LeaveCountContext
{
    public bool CountWeekends { get; init; }
    public decimal WorkdayHours { get; init; } = 8.5m;
    public TimeOnly WorkStart { get; init; } = new(9, 0);
    public TimeOnly WorkEnd { get; init; } = new(18, 0);

    public int WorkdayMinutes => LeaveMath.WorkdayMinutes(WorkdayHours);
}

public sealed class LeaveBalance
{
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal Entitlement { get; init; }
    public decimal CarryOver { get; init; }
    public decimal WorkdayHours { get; init; }
    public int UsedMinutes { get; init; }
    public int RemainingMinutes { get; init; }
    public bool CountWeekends { get; init; }

    public decimal UsedDays => WorkdayHours <= 0 ? 0 : UsedMinutes / 60m / WorkdayHours;
    public decimal RemainingDays => WorkdayHours <= 0 ? 0 : RemainingMinutes / 60m / WorkdayHours;
}

public sealed class CompensatoryBalance
{
    public decimal WorkdayHours { get; init; }
    public int OpeningMinutes { get; init; }
    public int DebitMinutes { get; init; }
    public int CreditMinutes { get; init; }
    public int NetMinutes { get; init; }
}

public static class LeaveIds
{
    public static readonly Guid Annual = Guid.Parse("44444444-4444-4444-4444-444444444441");
    public static readonly Guid Excuse = Guid.Parse("44444444-4444-4444-4444-444444444442");
    public static readonly Guid Sick = Guid.Parse("44444444-4444-4444-4444-444444444443");
    public static readonly Guid Unpaid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Marriage = Guid.Parse("44444444-4444-4444-4444-444444444445");
    public static readonly Guid Parental = Guid.Parse("44444444-4444-4444-4444-444444444446");
    public static readonly Guid Administrative = Guid.Parse("44444444-4444-4444-4444-444444444447");
    public static readonly Guid Other = Guid.Parse("44444444-4444-4444-4444-444444444448");
    public static readonly Guid TelafiliIzin = Guid.Parse("44444444-4444-4444-4444-444444444449");
    public static readonly Guid Telafi = Guid.Parse("44444444-4444-4444-4444-44444444444A");
}

public static class LeaveStatusExtensions
{
    public static string ToDisplay(this LeaveStatus status) => status switch
    {
        LeaveStatus.Planlandi => "Planlandı",
        LeaveStatus.Onaylandi => "Onaylandı",
        LeaveStatus.Kullanildi => "Kullanıldı",
        LeaveStatus.Iptal => "İptal",
        LeaveStatus.Reddedildi => "Reddedildi",
        _ => status.ToString()
    };

    public static string ToDisplay(this HalfDayKind kind) => kind switch
    {
        HalfDayKind.Morning => "sabah",
        HalfDayKind.Afternoon => "öğleden sonra",
        _ => ""
    };

    public static string ToDisplay(this LeaveDurationKind kind) => kind switch
    {
        LeaveDurationKind.Hourly => "Saatlik",
        LeaveDurationKind.Daily => "Günlük",
        LeaveDurationKind.Range => "Uzun izin",
        _ => kind.ToString()
    };

    public static string ToDisplay(this LeaveEntryKind kind) => kind switch
    {
        LeaveEntryKind.TelafiliIzin => "Telafili izin",
        LeaveEntryKind.Telafi => "Telafi",
        _ => "İzin"
    };

    public static bool AffectsBalance(this LeaveStatus status)
        => status is LeaveStatus.Onaylandi or LeaveStatus.Kullanildi;
}

public static class LeaveMath
{
    private static readonly CultureInfo Tr = new("tr-TR");
    public const decimal DefaultWorkdayHours = 8.5m;

    public static int WorkdayMinutes(decimal workdayHours)
    {
        if (workdayHours <= 0)
        {
            workdayHours = DefaultWorkdayHours;
        }

        return (int)decimal.Round(workdayHours * 60m, 0, MidpointRounding.AwayFromZero);
    }

    public static int MinutesBetween(DateTime start, DateTime end)
    {
        if (end <= start)
        {
            return 0;
        }

        return (int)((end - start).Ticks / TimeSpan.TicksPerMinute);
    }

    public static int MinutesBetween(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            return 0;
        }

        return (int)((end - start).Ticks / TimeSpan.TicksPerMinute);
    }

    public static DateTime StartDateTime(LeaveRecord leave)
        => leave.StartDate.ToDateTime(leave.StartTime ?? TimeOnly.MinValue);

    public static DateTime EndDateTime(LeaveRecord leave)
        => leave.EndDate.ToDateTime(leave.EndTime ?? TimeOnly.MaxValue);

    public static bool IsCountedDay(DateOnly date, bool countWeekends)
        => countWeekends || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static bool Overlaps(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd)
        => aStart <= bEnd && bStart <= aEnd;

    public static bool Covers(LeaveRecord leave, DateOnly date)
        => leave.Status is not LeaveStatus.Iptal and not LeaveStatus.Reddedildi && leave.StartDate <= date && date <= leave.EndDate;

    public static bool CoversHour(LeaveRecord leave, DateOnly date, int hour)
    {
        if (!Covers(leave, date)
            || (leave.DurationKind != LeaveDurationKind.Hourly && !IsLedgerKind(ResolveKind(leave)))
            || leave.StartTime is null || leave.EndTime is null)
        {
            return false;
        }

        var start = StartDateTime(leave);
        var end = EndDateTime(leave);
        var slotStart = date.ToDateTime(new TimeOnly(hour, 0));
        var slotEnd = hour == 23 ? slotStart.AddHours(1).AddTicks(-1) : slotStart.AddHours(1);
        return start < slotEnd && end > slotStart;
    }

    public static int CountMinutes(LeaveRecord leave, LeaveCountContext ctx)
    {
        if (leave.DurationKind == LeaveDurationKind.Hourly || IsLedgerKind(ResolveKind(leave)))
        {
            var exact = CountExactSpan(leave);
            return exact > 0 ? exact : Math.Max(0, leave.DurationMinutes);
        }

        var calendar = CountCalendarMinutes(leave, ctx);
        return calendar > 0 ? calendar : Math.Max(0, leave.DurationMinutes);
    }

    public static int CountMinutesInPeriod(
        LeaveRecord leave,
        DateOnly periodStart,
        DateOnly periodEnd,
        LeaveCountContext ctx)
    {
        if (leave.DurationKind == LeaveDurationKind.Hourly || IsLedgerKind(ResolveKind(leave)))
        {
            if (leave.StartTime is null || leave.EndTime is null)
            {
                return leave.DurationMinutes > 0 && Overlaps(leave.StartDate, leave.EndDate, periodStart, periodEnd)
                    ? leave.DurationMinutes
                    : 0;
            }

            var start = leave.StartDate.ToDateTime(leave.StartTime.Value);
            var end = leave.EndDate.ToDateTime(leave.EndTime.Value);
            var clipStart = periodStart.ToDateTime(TimeOnly.MinValue);
            var clipEnd = periodEnd.ToDateTime(TimeOnly.MaxValue);
            var from = start < clipStart ? clipStart : start;
            var to = end > clipEnd ? clipEnd : end;
            return MinutesBetween(from, to);
        }

        var clippedStart = leave.StartDate < periodStart ? periodStart : leave.StartDate;
        var clippedEnd = leave.EndDate > periodEnd ? periodEnd : leave.EndDate;
        if (clippedEnd < clippedStart)
        {
            return 0;
        }

        var clipped = new LeaveRecord
        {
            DurationKind = leave.DurationKind,
            StartDate = clippedStart,
            EndDate = clippedEnd,
            StartTime = leave.StartDate == clippedStart ? leave.StartTime : null,
            EndTime = leave.EndDate == clippedEnd ? leave.EndTime : null,
            StartHalf = leave.StartDate == clippedStart ? leave.StartHalf : HalfDayKind.None,
            EndHalf = leave.EndDate == clippedEnd ? leave.EndHalf : HalfDayKind.None
        };
        return CountCalendarMinutes(clipped, ctx);
    }

    public static (DateOnly Start, DateOnly End) PeriodFor(DateOnly asOf, int startMonth)
    {
        startMonth = Math.Clamp(startMonth, 1, 12);
        var candidate = new DateOnly(asOf.Year, startMonth, 1);
        var start = asOf >= candidate ? candidate : candidate.AddYears(-1);
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }

    public static string FormatMinutes(int totalMinutes, decimal workdayHours)
    {
        var sign = totalMinutes < 0 ? "−" : "";
        var remaining = Math.Abs(totalMinutes);
        var dayMins = WorkdayMinutes(workdayHours);
        if (dayMins <= 0)
        {
            dayMins = WorkdayMinutes(DefaultWorkdayHours);
        }

        var days = remaining / dayMins;
        remaining %= dayMins;
        var hours = remaining / 60;
        var mins = remaining % 60;
        var parts = new List<string>();
        if (days > 0)
        {
            parts.Add($"{days} gün");
        }

        if (hours > 0)
        {
            parts.Add($"{hours} saat");
        }

        if (mins > 0)
        {
            parts.Add($"{mins} dk");
        }

        if (parts.Count == 0)
        {
            return "0 dk";
        }

        return sign + string.Join(" ", parts);
    }

    public static string FormatHoursMinutes(int totalMinutes)
    {
        var sign = totalMinutes < 0 ? "-" : "";
        var remaining = Math.Abs(totalMinutes);
        var hours = remaining / 60;
        var mins = remaining % 60;
        if (mins == 0)
        {
            return $"{sign}{hours} saat";
        }

        return $"{sign}{hours} saat {mins} dk";
    }

    public static string FormatLedgerMinutes(int signedMinutes)
    {
        if (signedMinutes == 0)
        {
            return "0 saat";
        }

        var body = FormatHoursMinutes(Math.Abs(signedMinutes));
        return signedMinutes < 0 ? "-" + body : "+" + body;
    }

    public static bool IsLedgerKind(LeaveEntryKind kind)
        => kind is LeaveEntryKind.TelafiliIzin or LeaveEntryKind.Telafi;

    public static LeaveEntryKind ResolveKind(LeaveRecord leave)
    {
        if (IsLedgerKind(leave.EntryKind))
        {
            return leave.EntryKind;
        }

        if (leave.TypeId == LeaveIds.TelafiliIzin || leave.Type?.Id == LeaveIds.TelafiliIzin)
        {
            return LeaveEntryKind.TelafiliIzin;
        }

        if (leave.TypeId == LeaveIds.Telafi || leave.Type?.Id == LeaveIds.Telafi)
        {
            return LeaveEntryKind.Telafi;
        }

        return LeaveEntryKind.Leave;
    }

    public static Guid TypeIdForKind(LeaveEntryKind kind) => kind switch
    {
        LeaveEntryKind.TelafiliIzin => LeaveIds.TelafiliIzin,
        LeaveEntryKind.Telafi => LeaveIds.Telafi,
        _ => Guid.Empty
    };

    public static int LedgerDeltaMinutes(LeaveRecord leave, LeaveCountContext ctx)
    {
        var kind = ResolveKind(leave);
        if (!IsLedgerKind(kind) || !leave.Status.AffectsBalance())
        {
            return 0;
        }

        var minutes = CountMinutes(leave, ctx);
        return kind == LeaveEntryKind.TelafiliIzin ? -minutes : minutes;
    }

    public static string FormatWorkdayHours(decimal hours)
        => FormatMinutes(WorkdayMinutes(hours), hours);

    public static string FormatDateRange(DateOnly start, DateOnly end)
    {
        if (start == end)
        {
            return start.ToString("d MMMM yyyy", Tr);
        }

        if (start.Year == end.Year && start.Month == end.Month)
        {
            return $"{start.Day}–{end.ToString("d MMMM yyyy", Tr)}";
        }

        if (start.Year == end.Year)
        {
            return $"{start.ToString("d MMMM", Tr)} – {end.ToString("d MMMM yyyy", Tr)}";
        }

        return $"{start.ToString("d MMMM yyyy", Tr)} – {end.ToString("d MMMM yyyy", Tr)}";
    }

    public static string FormatDateTimeRange(LeaveRecord leave)
    {
        var dates = FormatDateRange(leave.StartDate, leave.EndDate);
        if (leave.StartTime is { } st && leave.EndTime is { } et)
        {
            if (leave.StartDate == leave.EndDate)
            {
                return $"{leave.StartDate.ToString("d MMMM yyyy", Tr)} {st:HH\\:mm}–{et:HH\\:mm}";
            }

            return $"{leave.StartDate.ToString("d MMMM yyyy", Tr)} {st:HH\\:mm} – {leave.EndDate.ToString("d MMMM yyyy", Tr)} {et:HH\\:mm}";
        }

        if (leave.StartTime is { } onlyStart)
        {
            return $"{dates} · başlama {onlyStart:HH\\:mm}";
        }

        if (leave.EndTime is { } onlyEnd)
        {
            return $"{dates} · bitiş {onlyEnd:HH\\:mm}";
        }

        return dates;
    }

    public static string BannerTitle(LeaveRecord leave)
    {
        var kind = ResolveKind(leave);
        return kind switch
        {
            LeaveEntryKind.TelafiliIzin => "Telafili izin",
            LeaveEntryKind.Telafi => "Telafi",
            _ => $"İzin — {leave.Type?.Name ?? "İzin"}"
        };
    }

    public static string HalfLabelForDate(LeaveRecord leave, DateOnly date)
    {
        if (leave.DurationKind == LeaveDurationKind.Hourly && leave.StartTime is { } st && leave.EndTime is { } et)
        {
            if (leave.StartDate == leave.EndDate)
            {
                return $"{st:HH\\:mm}–{et:HH\\:mm}";
            }

            if (date == leave.StartDate)
            {
                return $"{st:HH\\:mm}’den itibaren";
            }

            if (date == leave.EndDate)
            {
                return $"{et:HH\\:mm}’e kadar";
            }
        }

        if (date == leave.StartDate && leave.StartTime is { } first)
        {
            return $"başlama {first:HH\\:mm}";
        }

        if (date == leave.EndDate && leave.EndTime is { } last)
        {
            return $"bitiş {last:HH\\:mm}";
        }

        if (date == leave.StartDate && leave.StartHalf != HalfDayKind.None)
        {
            return $"yarım gün ({leave.StartHalf.ToDisplay()})";
        }

        if (date == leave.EndDate && leave.StartDate != leave.EndDate && leave.EndHalf != HalfDayKind.None)
        {
            return $"yarım gün ({leave.EndHalf.ToDisplay()})";
        }

        return "";
    }

    public static string HalfSummary(LeaveRecord leave)
    {
        if (leave.DurationKind == LeaveDurationKind.Hourly)
        {
            return leave.StartTime is { } st && leave.EndTime is { } et
                ? $"{st:HH\\:mm}–{et:HH\\:mm}"
                : "";
        }

        var parts = new List<string>();
        if (leave.StartTime is { } first)
        {
            parts.Add($"başlama {first:HH\\:mm}");
        }
        else if (leave.StartHalf != HalfDayKind.None)
        {
            parts.Add(leave.StartDate == leave.EndDate
                ? $"yarım gün ({leave.StartHalf.ToDisplay()})"
                : $"ilk gün {leave.StartHalf.ToDisplay()}");
        }

        if (leave.EndTime is { } last && (leave.StartDate != leave.EndDate || leave.StartTime is null))
        {
            parts.Add($"bitiş {last:HH\\:mm}");
        }
        else if (leave.EndHalf != HalfDayKind.None && leave.StartDate != leave.EndDate)
        {
            parts.Add($"son gün {leave.EndHalf.ToDisplay()}");
        }

        return string.Join(" · ", parts);
    }

    private static int CountExactSpan(LeaveRecord leave)
    {
        if (leave.StartTime is null || leave.EndTime is null)
        {
            return 0;
        }

        return MinutesBetween(
            leave.StartDate.ToDateTime(leave.StartTime.Value),
            leave.EndDate.ToDateTime(leave.EndTime.Value));
    }

    private static int CountCalendarMinutes(LeaveRecord leave, LeaveCountContext ctx)
    {
        var dayMins = ctx.WorkdayMinutes;
        var total = 0;
        for (var d = leave.StartDate; d <= leave.EndDate; d = d.AddDays(1))
        {
            if (!IsCountedDay(d, ctx.CountWeekends))
            {
                continue;
            }

            total += MinutesForDay(leave, d, ctx, dayMins);
        }

        return total;
    }

    private static int MinutesForDay(LeaveRecord leave, DateOnly date, LeaveCountContext ctx, int dayMins)
    {
        var isFirst = date == leave.StartDate;
        var isLast = date == leave.EndDate;
        var startT = isFirst ? leave.StartTime : null;
        var endT = isLast ? leave.EndTime : null;

        if (isFirst && isLast && startT is not null && endT is not null)
        {
            return MinutesBetween(date.ToDateTime(startT.Value), date.ToDateTime(endT.Value));
        }

        if (isFirst && startT is not null)
        {
            var to = isLast && endT is not null ? endT.Value : ctx.WorkEnd;
            return MinutesBetween(startT.Value, to);
        }

        if (isLast && endT is not null)
        {
            return MinutesBetween(ctx.WorkStart, endT.Value);
        }

        if (isFirst && leave.StartHalf != HalfDayKind.None)
        {
            return dayMins / 2;
        }

        if (isLast && date != leave.StartDate && leave.EndHalf != HalfDayKind.None)
        {
            return dayMins / 2;
        }

        return dayMins;
    }
}
