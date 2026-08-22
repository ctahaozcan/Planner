using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Planner.Core.Data;

public static class SchemaMigrator
{
    public static async Task ApplyAsync(PlannerDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await EnsureColumnAsync(db, "Tasks", "RecurrenceKind", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "Tasks", "RecurrenceWeekdays", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "Tasks", "RecurrenceMonthDay", "INTEGER NULL");
            await EnsureColumnAsync(db, "Tasks", "RecurrenceEndDate", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "SeriesId", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "IsSeriesException", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "Tasks", "LinkedContactId", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS RecurrenceExceptions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    SeriesId TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    Kind INTEGER NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_RecurrenceExceptions_SeriesId_Date
                ON RecurrenceExceptions (SeriesId, Date);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Habits (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ScheduleKind INTEGER NOT NULL,
                    ReminderTime TEXT NULL,
                    ReminderEnabled INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS HabitLogs (
                    Id TEXT NOT NULL PRIMARY KEY,
                    HabitId TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    CompletedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_HabitLogs_HabitId_Date
                ON HabitLogs (HabitId, Date);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS DailyNotes (
                    Date TEXT NOT NULL PRIMARY KEY,
                    Content TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS DayPriorities (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Date TEXT NOT NULL,
                    TaskId TEXT NOT NULL,
                    Slot INTEGER NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DayPriorities_Date_Slot
                ON DayPriorities (Date, Slot);
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DayPriorities_Date_TaskId
                ON DayPriorities (Date, TaskId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS TaskAttachments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    TaskId TEXT NOT NULL,
                    OriginalName TEXT NOT NULL,
                    StoredFileName TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_TaskAttachments_TaskId
                ON TaskAttachments (TaskId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS QueuedNotifications (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Kind INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    Payload TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS LeaveTypes (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ColorHex TEXT NOT NULL,
                    IsBuiltIn INTEGER NOT NULL,
                    CountsAgainstAnnual INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS LeaveRecords (
                    Id TEXT NOT NULL PRIMARY KEY,
                    TypeId TEXT NOT NULL,
                    DurationKind INTEGER NOT NULL DEFAULT 1,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    StartTime TEXT NULL,
                    EndTime TEXT NULL,
                    StartHalf INTEGER NOT NULL,
                    EndHalf INTEGER NOT NULL,
                    Note TEXT NULL,
                    Status INTEGER NOT NULL,
                    DurationMinutes INTEGER NOT NULL DEFAULT 0,
                    EntryKind INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_LeaveRecords_StartDate_EndDate
                ON LeaveRecords (StartDate, EndDate);
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_LeaveRecords_TypeId
                ON LeaveRecords (TypeId);
                """);
            await EnsureColumnAsync(db, "LeaveRecords", "DurationKind", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(db, "LeaveRecords", "StartTime", "TEXT NULL");
            await EnsureColumnAsync(db, "LeaveRecords", "EndTime", "TEXT NULL");
            await EnsureColumnAsync(db, "LeaveRecords", "EntryKind", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "LeaveRecords", "DurationMinutes", "INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_LeaveRecords_EntryKind
                ON LeaveRecords (EntryKind);
                """);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task EnsureColumnAsync(PlannerDbContext db, string table, string column, string definition)
    {
        if (await ColumnExistsAsync(db, table, column))
        {
            return;
        }

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");
#pragma warning restore EF1002
    }

    private static async Task<bool> ColumnExistsAsync(PlannerDbContext db, string table, string column)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
