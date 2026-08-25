using Microsoft.EntityFrameworkCore;
using Planner.Core.Models;

namespace Planner.Core.Data;

public static class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 15;

    public static async Task InitializeAsync(PlannerDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
        await SchemaMigrator.ApplyAsync(db);

        if (!await db.Categories.AnyAsync())
        {
            db.Categories.AddRange(
                new Category
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "İş",
                    ColorHex = "#2563EB",
                    IsBuiltIn = true,
                    SortOrder = 0
                },
                new Category
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Name = "Kişisel",
                    ColorHex = "#7C3AED",
                    IsBuiltIn = true,
                    SortOrder = 1
                },
                new Category
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Name = "Özel",
                    ColorHex = "#D97706",
                    IsBuiltIn = true,
                    SortOrder = 2
                });
        }

        await EnsureSettingAsync(db, SettingKeys.Theme, "System");
        await EnsureSettingAsync(db, SettingKeys.StartWithWindows, "false");
        await EnsureSettingAsync(db, SettingKeys.StartMinimized, "false");
        await EnsureSettingAsync(db, SettingKeys.TrayTipShown, "false");
        await EnsureSettingAsync(db, SettingKeys.MorningBriefingEnabled, "true");
        await EnsureSettingAsync(db, SettingKeys.MorningBriefingTime, "08:00");
        await EnsureSettingAsync(db, SettingKeys.EveningCloseEnabled, "true");
        await EnsureSettingAsync(db, SettingKeys.EveningCloseTime, "21:00");
        await EnsureSettingAsync(db, SettingKeys.QuietHoursEnabled, "false");
        await EnsureSettingAsync(db, SettingKeys.QuietHoursStart, "23:00");
        await EnsureSettingAsync(db, SettingKeys.QuietHoursEnd, "07:00");
        await EnsureSettingAsync(db, SettingKeys.WorkBandStart, "09:00");
        await EnsureSettingAsync(db, SettingKeys.WorkBandEnd, "18:00");
        await EnsureSettingAsync(db, SettingKeys.DayViewStart, "07:00");
        await EnsureSettingAsync(db, SettingKeys.DayViewEnd, "22:00");
        await EnsureSettingAsync(db, SettingKeys.GlobalHotkey, "Ctrl+Alt+N");
        await EnsureSettingAsync(db, SettingKeys.HotkeyRegisterFailed, "false");
        await EnsureSettingAsync(db, SettingKeys.PomodoroFocusMinutes, "25");
        await EnsureSettingAsync(db, SettingKeys.PomodoroBreakMinutes, "5");
        await EnsureSettingAsync(db, SettingKeys.LeaveYearStartMonth, "1");
        await EnsureSettingAsync(db, SettingKeys.LeaveAnnualAllowance, "0");
        await EnsureSettingAsync(db, SettingKeys.LeaveCarryOver, "0");
        await EnsureSettingAsync(db, SettingKeys.LeaveCountWeekends, "false");
        await EnsureSettingAsync(db, SettingKeys.LeaveWorkdayHours, "8.5");
        await EnsureSettingAsync(db, SettingKeys.LeaveCompensatoryOpeningMinutes, "0");
        await EnsureSettingAsync(db, SettingKeys.ChatServerEnabled, "false");
        await EnsureSettingAsync(db, SettingKeys.ChatLanEnabled, "true");
        await EnsureSettingAsync(db, SettingKeys.ChatServerUrl, "http://127.0.0.1:47880");
        await EnsureSettingAsync(db, SettingKeys.ChatServerToken, "");
        await EnsureSettingAsync(db, SettingKeys.ChatServerUserId, "");
        await EnsureSettingAsync(db, SettingKeys.ChatServerUsername, "");
        await EnsureSettingAsync(db, SettingKeys.ChatServerDisplayName, "");
        await EnsureSettingAsync(db, SettingKeys.RememberLogin, "false");
        await SetSettingAsync(db, SettingKeys.SchemaVersion, CurrentSchemaVersion.ToString());

        await EnsureLeaveTypeAsync(db, LeaveIds.Annual, "Yıllık izin", "#0F766E", true, 0);
        await EnsureLeaveTypeAsync(db, LeaveIds.Excuse, "Mazeret", "#2563EB", false, 1);
        await EnsureLeaveTypeAsync(db, LeaveIds.Sick, "Hastalık", "#DC2626", false, 2);
        await EnsureLeaveTypeAsync(db, LeaveIds.Unpaid, "Ücretsiz", "#64748B", false, 3);
        await EnsureLeaveTypeAsync(db, LeaveIds.Marriage, "Evlilik", "#DB2777", false, 4);
        await EnsureLeaveTypeAsync(db, LeaveIds.Parental, "Doğum / babalık", "#7C3AED", false, 5);
        await EnsureLeaveTypeAsync(db, LeaveIds.Administrative, "İdari", "#D97706", false, 6);
        await EnsureLeaveTypeAsync(db, LeaveIds.Other, "Diğer", "#475569", false, 7);
        await EnsureLeaveTypeAsync(db, LeaveIds.TelafiliIzin, "Telafili izin", "#C2410C", false, 8);
        await EnsureLeaveTypeAsync(db, LeaveIds.Telafi, "Telafi", "#059669", false, 9);

        await EnsureNetworkDefaultsAsync(db);

        await db.SaveChangesAsync();
    }

    private static async Task EnsureNetworkDefaultsAsync(PlannerDbContext db)
    {
        if (!await db.MeProfiles.AnyAsync())
        {
            db.MeProfiles.Add(new MeProfile
            {
                Id = NetworkIds.Me,
                Name = "Ben",
                Notes = null,
                UpdatedAt = DateTime.Now
            });
        }

        await EnsureSegmentAsync(db, SegmentIds.Kurum, "Kurum", SegmentKind.Kurum, "#2563EB", 0);
        await EnsureSegmentAsync(db, SegmentIds.Aile, "Aile", SegmentKind.Aile, "#7C3AED", 1);
        await EnsureSegmentAsync(db, SegmentIds.Arkadas, "Arkadaş", SegmentKind.Arkadas, "#0D9488", 2);

        if (!await db.Organizations.AnyAsync(o => o.SegmentId == SegmentIds.Kurum))
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                SegmentId = SegmentIds.Kurum,
                Name = "Kurum",
                UpdatedAt = DateTime.Now
            });
        }
    }

    private static async Task EnsureSegmentAsync(
        PlannerDbContext db,
        Guid id,
        string name,
        SegmentKind kind,
        string colorHex,
        int sortOrder)
    {
        if (await db.Segments.AnyAsync(s => s.Id == id))
        {
            return;
        }

        db.Segments.Add(new Segment
        {
            Id = id,
            Name = name,
            Kind = kind,
            ColorHex = colorHex,
            SortOrder = sortOrder,
            CreatedAt = DateTime.Now
        });
    }

    private static async Task EnsureLeaveTypeAsync(
        PlannerDbContext db,
        Guid id,
        string name,
        string colorHex,
        bool countsAgainstAnnual,
        int sortOrder)
    {
        if (await db.LeaveTypes.AnyAsync(t => t.Id == id))
        {
            return;
        }

        db.LeaveTypes.Add(new LeaveType
        {
            Id = id,
            Name = name,
            ColorHex = colorHex,
            IsBuiltIn = true,
            CountsAgainstAnnual = countsAgainstAnnual,
            SortOrder = sortOrder
        });
    }

    private static async Task EnsureSettingAsync(PlannerDbContext db, string key, string value)
    {
        if (!await db.Settings.AnyAsync(s => s.Key == key))
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
    }

    private static async Task SetSettingAsync(PlannerDbContext db, string key, string value)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }
}

public static class SettingKeys
{
    public const string Theme = "Theme";
    public const string StartWithWindows = "StartWithWindows";
    public const string StartMinimized = "StartMinimized";
    public const string TrayTipShown = "TrayTipShown";
    public const string MorningBriefingEnabled = "MorningBriefingEnabled";
    public const string MorningBriefingTime = "MorningBriefingTime";
    public const string LastBriefingDate = "LastBriefingDate";
    public const string EveningCloseEnabled = "EveningCloseEnabled";
    public const string EveningCloseTime = "EveningCloseTime";
    public const string LastEveningCloseDate = "LastEveningCloseDate";
    public const string QuietHoursEnabled = "QuietHoursEnabled";
    public const string QuietHoursStart = "QuietHoursStart";
    public const string QuietHoursEnd = "QuietHoursEnd";
    public const string WorkBandStart = "WorkBandStart";
    public const string WorkBandEnd = "WorkBandEnd";
    public const string DayViewStart = "DayViewStart";
    public const string DayViewEnd = "DayViewEnd";
    public const string GlobalHotkey = "GlobalHotkey";
    public const string HotkeyRegisterFailed = "HotkeyRegisterFailed";
    public const string PomodoroFocusMinutes = "PomodoroFocusMinutes";
    public const string PomodoroBreakMinutes = "PomodoroBreakMinutes";
    public const string LastBirthdayNotifyDate = "LastBirthdayNotifyDate";
    public const string LeaveYearStartMonth = "LeaveYearStartMonth";
    public const string LeaveAnnualAllowance = "LeaveAnnualAllowance";
    public const string LeaveCarryOver = "LeaveCarryOver";
    public const string LeaveCountWeekends = "LeaveCountWeekends";
    public const string LeaveWorkdayHours = "LeaveWorkdayHours";
    public const string LeaveCompensatoryOpeningMinutes = "LeaveCompensatoryOpeningMinutes";
    public const string CurrentUserId = "CurrentUserId";
    public const string ChatServerEnabled = "ChatServerEnabled";
    public const string ChatLanEnabled = "ChatLanEnabled";
    public const string ChatServerUrl = "ChatServerUrl";
    public const string ChatServerToken = "ChatServerToken";
    public const string ChatServerUserId = "ChatServerUserId";
    public const string ChatServerUsername = "ChatServerUsername";
    public const string ChatServerDisplayName = "ChatServerDisplayName";
    public const string RememberLogin = "RememberLogin";
    public const string SchemaVersion = "SchemaVersion";
    public const string LastPage = "LastPage";
    public const string RolloverDate = "RolloverDate";
}
