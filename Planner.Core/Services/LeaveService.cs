using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Planner.Core.Data;
using Planner.Core.Models;

namespace Planner.Core.Services;

public sealed class LeaveService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly IDbContextFactory<PlannerDbContext> _factory;
    private readonly ITaskChangeSignal _signal;
    private readonly SettingsService _settings;
    private readonly UserAccountService _users;

    public LeaveService(
        IDbContextFactory<PlannerDbContext> factory,
        ITaskChangeSignal signal,
        SettingsService settings,
        UserAccountService users)
    {
        _factory = factory;
        _signal = signal;
        _settings = settings;
        _users = users;
    }

    private Guid? Me => _users.Current?.Id;

    private static IQueryable<LeaveRecord> OwnedBy(IQueryable<LeaveRecord> query, Guid? owner)
    {
        if (owner is null)
        {
            return query;
        }

        return query.Where(r => r.OwnerUserId == null || r.OwnerUserId == owner);
    }

    public async Task<IReadOnlyList<LeaveType>> GetTypesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.LeaveTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task<LeaveType> AddTypeAsync(string name, bool countsAgainstAnnual, CancellationToken ct = default)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("İzin türü adı boş olamaz.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.LeaveTypes.AnyAsync(t => t.Name == name, ct))
        {
            throw new InvalidOperationException("Bu isimde bir izin türü zaten var.");
        }

        var maxOrder = await db.LeaveTypes.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;
        var type = new LeaveType
        {
            Id = Guid.NewGuid(),
            Name = name,
            ColorHex = "#0F766E",
            IsBuiltIn = false,
            CountsAgainstAnnual = countsAgainstAnnual,
            SortOrder = maxOrder + 1
        };
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
        return type;
    }

    public async Task DeleteTypeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new InvalidOperationException("İzin türü bulunamadı.");
        if (type.IsBuiltIn)
        {
            throw new InvalidOperationException("Varsayılan izin türleri silinemez.");
        }

        if (await db.LeaveRecords.AnyAsync(r => r.TypeId == id, ct))
        {
            throw new InvalidOperationException("Bu türe bağlı izin kayıtları var. Önce kayıtları silin veya türünü değiştirin.");
        }

        db.LeaveTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task<IReadOnlyList<LeaveRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveRecord>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .Where(r => r.Status != LeaveStatus.Iptal && r.Status != LeaveStatus.Reddedildi && r.StartDate <= date && r.EndDate >= date)
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveRecord>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .Where(r => r.Status != LeaveStatus.Iptal && r.Status != LeaveStatus.Reddedildi && r.StartDate <= to && r.EndDate >= from)
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveRecord>> GetOverlapsAsync(
        DateOnly start,
        DateOnly end,
        Guid? excludeId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .Where(r => r.Status != LeaveStatus.Iptal && r.Status != LeaveStatus.Reddedildi && r.StartDate <= end && r.EndDate >= start);
        if (excludeId is { } id)
        {
            query = query.Where(r => r.Id != id);
        }

        return await query.OrderBy(r => r.StartDate).ToListAsync(ct);
    }

    public async Task<LeaveRecord> SaveAsync(LeaveRecord draft, CancellationToken ct = default)
    {
        if (draft.EndDate < draft.StartDate)
        {
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
        }

        var entryKind = LeaveMath.ResolveKind(draft);
        if (LeaveMath.IsLedgerKind(entryKind))
        {
            draft.EntryKind = entryKind;
            draft.TypeId = LeaveMath.TypeIdForKind(entryKind);
            draft.DurationKind = LeaveDurationKind.Hourly;
        }

        if (draft.DurationKind == LeaveDurationKind.Hourly || LeaveMath.IsLedgerKind(entryKind))
        {
            if (draft.StartTime is null || draft.EndTime is null)
            {
                throw new ArgumentException("Başlangıç ve bitiş için tarih, saat ve dakika gerekli.");
            }

            if (LeaveMath.MinutesBetween(
                    draft.StartDate.ToDateTime(draft.StartTime.Value),
                    draft.EndDate.ToDateTime(draft.EndTime.Value)) <= 0)
            {
                throw new ArgumentException("Bitiş, başlangıçtan sonra olmalı (tarih, saat ve dakika).");
            }
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == draft.TypeId, ct)
                   ?? throw new InvalidOperationException("İzin türü bulunamadı.");

        LeaveRecord entity;
        if (draft.Id == Guid.Empty || !await db.LeaveRecords.AnyAsync(r => r.Id == draft.Id, ct))
        {
            entity = new LeaveRecord { Id = draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id };
            db.LeaveRecords.Add(entity);
            entity.CreatedAt = DateTime.Now;
        }
        else
        {
            entity = await db.LeaveRecords.FirstAsync(r => r.Id == draft.Id, ct);
        }

        entity.TypeId = type.Id;
        entity.EntryKind = entryKind;
        entity.DurationKind = LeaveMath.IsLedgerKind(entryKind) ? LeaveDurationKind.Hourly : draft.DurationKind;
        entity.StartDate = draft.StartDate;
        entity.EndDate = entity.DurationKind == LeaveDurationKind.Hourly && draft.EndDate < draft.StartDate
            ? draft.StartDate
            : draft.EndDate;
        entity.StartTime = draft.StartTime;
        entity.EndTime = draft.EndTime;
        entity.StartHalf = entity.DurationKind == LeaveDurationKind.Hourly
            ? HalfDayKind.None
            : (draft.StartDate == draft.EndDate ? draft.StartHalf : draft.StartHalf);
        entity.EndHalf = entity.DurationKind == LeaveDurationKind.Hourly || draft.StartDate == draft.EndDate
            ? HalfDayKind.None
            : draft.EndHalf;
        entity.Note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim();
        entity.Status = draft.Status;
        entity.OwnerUserId = draft.OwnerUserId ?? entity.OwnerUserId ?? Me;
        entity.ServerLeaveId = draft.ServerLeaveId ?? entity.ServerLeaveId;
        entity.UpdatedAt = DateTime.Now;
        var ctx = await GetCountContextAsync(ct);
        entity.DurationMinutes = LeaveMath.CountMinutes(entity, ctx);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();

        await db.Entry(entity).Reference(e => e.Type).LoadAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.LeaveRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (existing is null)
        {
            return;
        }

        db.LeaveRecords.Remove(existing);
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    public async Task<bool> GetCountWeekendsAsync(CancellationToken ct = default)
        => await _settings.GetBoolAsync(SettingKeys.LeaveCountWeekends);

    public async Task<decimal> GetWorkdayHoursAsync(CancellationToken ct = default)
    {
        _ = ct;
        return await GetDecimalSettingAsync(SettingKeys.LeaveWorkdayHours, LeaveMath.DefaultWorkdayHours);
    }

    public async Task<LeaveCountContext> GetCountContextAsync(CancellationToken ct = default)
    {
        var countWeekends = await GetCountWeekendsAsync(ct);
        var hours = await GetWorkdayHoursAsync(ct);
        var workStart = await _settings.GetTimeAsync(SettingKeys.WorkBandStart, new TimeOnly(9, 0));
        var workEnd = await _settings.GetTimeAsync(SettingKeys.WorkBandEnd, new TimeOnly(18, 0));
        return new LeaveCountContext
        {
            CountWeekends = countWeekends,
            WorkdayHours = hours <= 0 ? LeaveMath.DefaultWorkdayHours : hours,
            WorkStart = workStart,
            WorkEnd = workEnd <= workStart ? workStart.AddMinutes(1) : workEnd
        };
    }

    public async Task<LeaveBalance> GetBalanceAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var date = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var month = await _settings.GetIntAsync(SettingKeys.LeaveYearStartMonth, 1);
        var entitlement = await GetDecimalSettingAsync(SettingKeys.LeaveAnnualAllowance, 0m);
        var carry = await GetDecimalSettingAsync(SettingKeys.LeaveCarryOver, 0m);
        var ctx = await GetCountContextAsync(ct);
        var (periodStart, periodEnd) = LeaveMath.PeriodFor(date, month);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var records = await OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .Where(r => r.Type.CountsAgainstAnnual
                        && (r.Status == LeaveStatus.Onaylandi || r.Status == LeaveStatus.Kullanildi)
                        && r.StartDate <= periodEnd
                        && r.EndDate >= periodStart)
            .ToListAsync(ct);

        var usedMinutes = records.Sum(r => LeaveMath.CountMinutesInPeriod(r, periodStart, periodEnd, ctx));
        var entitlementMinutes = (int)decimal.Round(entitlement * ctx.WorkdayHours * 60m, 0, MidpointRounding.AwayFromZero);
        var carryMinutes = (int)decimal.Round(carry * ctx.WorkdayHours * 60m, 0, MidpointRounding.AwayFromZero);
        return new LeaveBalance
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Entitlement = entitlement,
            CarryOver = carry,
            WorkdayHours = ctx.WorkdayHours,
            UsedMinutes = usedMinutes,
            RemainingMinutes = entitlementMinutes + carryMinutes - usedMinutes,
            CountWeekends = ctx.CountWeekends
        };
    }

    public async Task<CompensatoryBalance> GetCompensatoryBalanceAsync(CancellationToken ct = default)
    {
        var ctx = await GetCountContextAsync(ct);
        var opening = await GetOpeningMinutesAsync(ct);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var records = await OwnedBy(db.LeaveRecords.AsNoTracking(), Me)
            .Include(r => r.Type)
            .Where(r => r.Status == LeaveStatus.Onaylandi || r.Status == LeaveStatus.Kullanildi)
            .ToListAsync(ct);

        var debit = 0;
        var credit = 0;
        foreach (var record in records)
        {
            var kind = LeaveMath.ResolveKind(record);
            if (kind == LeaveEntryKind.TelafiliIzin)
            {
                debit += LeaveMath.CountMinutes(record, ctx);
            }
            else if (kind == LeaveEntryKind.Telafi)
            {
                credit += LeaveMath.CountMinutes(record, ctx);
            }
        }

        return new CompensatoryBalance
        {
            WorkdayHours = ctx.WorkdayHours,
            OpeningMinutes = opening,
            DebitMinutes = debit,
            CreditMinutes = credit,
            NetMinutes = opening + credit - debit
        };
    }

    public async Task<int> GetOpeningMinutesAsync(CancellationToken ct = default)
    {
        _ = ct;
        var raw = await _settings.GetAsync(SettingKeys.LeaveCompensatoryOpeningMinutes, "0");
        return int.TryParse(raw, NumberStyles.Integer, Invariant, out var value) ? value : 0;
    }

    public async Task SaveOpeningMinutesAsync(int minutes, CancellationToken ct = default)
    {
        _ = ct;
        await _settings.SetAsync(SettingKeys.LeaveCompensatoryOpeningMinutes, minutes.ToString(Invariant));
        _signal.NotifyChanged();
    }

    public async Task SaveBalanceSettingsAsync(
        int yearStartMonth,
        decimal entitlement,
        decimal carryOver,
        bool countWeekends,
        decimal workdayHours,
        CancellationToken ct = default)
    {
        _ = ct;
        if (workdayHours <= 0)
        {
            workdayHours = LeaveMath.DefaultWorkdayHours;
        }

        workdayHours = Math.Clamp(workdayHours, 0.25m, 24m);
        await _settings.SetAsync(SettingKeys.LeaveYearStartMonth, Math.Clamp(yearStartMonth, 1, 12).ToString(Invariant));
        await _settings.SetAsync(SettingKeys.LeaveAnnualAllowance, Math.Max(0, entitlement).ToString("0.##", Invariant));
        await _settings.SetAsync(SettingKeys.LeaveCarryOver, Math.Max(0, carryOver).ToString("0.##", Invariant));
        await _settings.SetBoolAsync(SettingKeys.LeaveCountWeekends, countWeekends);
        await _settings.SetAsync(SettingKeys.LeaveWorkdayHours, workdayHours.ToString("0.##", Invariant));
        _signal.NotifyChanged();
    }

    public static LeaveStatus MapServerStatus(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "approved" => LeaveStatus.Onaylandi,
        "rejected" => LeaveStatus.Reddedildi,
        "used" => LeaveStatus.Kullanildi,
        _ => LeaveStatus.Planlandi
    };

    public async Task ApplyRemoteAsync(
        Guid serverId,
        Guid? clientId,
        Guid typeId,
        LeaveEntryKind entryKind,
        LeaveDurationKind durationKind,
        DateOnly start,
        DateOnly end,
        TimeOnly? startTime,
        TimeOnly? endTime,
        string? note,
        LeaveStatus status,
        int durationMinutes,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.LeaveRecords.FirstOrDefaultAsync(r => r.ServerLeaveId == serverId, ct);
        if (entity is null && clientId is Guid localId)
        {
            entity = await db.LeaveRecords.FirstOrDefaultAsync(r => r.Id == localId, ct);
        }

        if (entity is null)
        {
            entity = new LeaveRecord
            {
                Id = clientId is Guid cid && cid != Guid.Empty ? cid : Guid.NewGuid(),
                CreatedAt = DateTime.Now
            };
            db.LeaveRecords.Add(entity);
        }

        entity.TypeId = typeId;
        entity.EntryKind = entryKind;
        entity.DurationKind = durationKind;
        entity.StartDate = start;
        entity.EndDate = end;
        entity.StartTime = startTime;
        entity.EndTime = endTime;
        entity.Note = string.IsNullOrWhiteSpace(note) ? entity.Note : note.Trim();
        entity.Status = status;
        entity.DurationMinutes = durationMinutes;
        entity.OwnerUserId = Me ?? entity.OwnerUserId;
        entity.ServerLeaveId = serverId;
        entity.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        _signal.NotifyChanged();
    }

    private async Task<decimal> GetDecimalSettingAsync(string key, decimal fallback)
    {
        var raw = await _settings.GetAsync(key, fallback.ToString("0.##", Invariant));
        return ParseDecimal(raw, fallback);
    }

    public static decimal ParseDecimal(string? text, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, Invariant, out var value)
            ? value
            : fallback;
    }
}
