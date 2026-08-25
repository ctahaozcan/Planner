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
            await EnsureColumnAsync(db, "Tasks", "SortOrder", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "Tasks", "StartedAt", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "CompletedAt", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS RecurrenceExceptions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    SeriesId TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    Kind INTEGER NOT NULL,
                    CompletedAt TEXT NULL
                );
                """);
            await EnsureColumnAsync(db, "RecurrenceExceptions", "CompletedAt", "TEXT NULL");
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
            await EnsureColumnAsync(db, "LeaveRecords", "OwnerUserId", "TEXT NULL");
            await EnsureColumnAsync(db, "LeaveRecords", "ServerLeaveId", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS SocialAccounts (
                    Id TEXT NOT NULL PRIMARY KEY,
                    ContactId TEXT NOT NULL,
                    Payload BLOB NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_SocialAccounts_ContactId
                ON SocialAccounts (ContactId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Relationships (
                    Id TEXT NOT NULL PRIMARY KEY,
                    FromPersonId TEXT NOT NULL,
                    ToPersonId TEXT NOT NULL,
                    Label TEXT NOT NULL,
                    IsDirected INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_Relationships_From_To
                ON Relationships (FromPersonId, ToPersonId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Segments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Kind INTEGER NOT NULL,
                    ColorHex TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Organizations (
                    Id TEXT NOT NULL PRIMARY KEY,
                    SegmentId TEXT NULL,
                    Name TEXT NOT NULL,
                    Role TEXT NULL,
                    Phone TEXT NULL,
                    Address TEXT NULL,
                    Notes TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_Organizations_SegmentId
                ON Organizations (SegmentId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS MeProfile (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Notes TEXT NULL,
                    PhotoFileName TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
            await EnsureColumnAsync(db, "MeProfile", "PhotoFileName", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS PersonSegments (
                    PersonId TEXT NOT NULL,
                    SegmentId TEXT NOT NULL,
                    PRIMARY KEY (PersonId, SegmentId)
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_PersonSegments_SegmentId
                ON PersonSegments (SegmentId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS PersonOrganizations (
                    PersonId TEXT NOT NULL,
                    OrganizationId TEXT NOT NULL,
                    IsPrimary INTEGER NOT NULL,
                    PRIMARY KEY (PersonId, OrganizationId)
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_PersonOrganizations_OrganizationId
                ON PersonOrganizations (OrganizationId);
                """);
            await EnsureColumnAsync(db, "PersonOrganizations", "ManagerPersonId", "TEXT NULL");
            await EnsureColumnAsync(db, "PersonOrganizations", "Title", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS TaskStatusSpans (
                    Id TEXT NOT NULL PRIMARY KEY,
                    TaskId TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    StartedAt TEXT NOT NULL,
                    EndedAt TEXT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_TaskStatusSpans_TaskId
                ON TaskStatusSpans (TaskId);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS WorkspaceDocuments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Kind INTEGER NOT NULL,
                    Body TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    OwnerUserId TEXT NULL
                );
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS AppUsers (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Username TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    PasswordSalt BLOB NOT NULL,
                    PasswordVerifier BLOB NOT NULL,
                    HasPassword INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Username
                ON AppUsers (Username);
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id TEXT NOT NULL PRIMARY KEY,
                    ConversationKey TEXT NOT NULL,
                    FromKey TEXT NOT NULL,
                    ToKey TEXT NOT NULL,
                    FromName TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    SentAt TEXT NOT NULL,
                    IsOutgoing INTEGER NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_ChatMessages_ConversationKey_SentAt
                ON ChatMessages (ConversationKey, SentAt);
                """);

            await EnsureColumnAsync(db, "AppUsers", "FirstName", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "AppUsers", "LastName", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "AppUsers", "Email", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "WorkspaceDocuments", "OwnerUserId", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "OwnerUserId", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "AssignedToUserId", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "AssignedByUserId", "TEXT NULL");
            await EnsureColumnAsync(db, "ChatMessages", "MediaName", "TEXT NULL");
            await EnsureColumnAsync(db, "ChatMessages", "EditedAt", "TEXT NULL");
            await EnsureColumnAsync(db, "ChatMessages", "Thumbs", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "AppUsers", "UsageKind", "TEXT NOT NULL DEFAULT 'personal'");
            await EnsureColumnAsync(db, "AppUsers", "CompanyId", "TEXT NULL");
            await EnsureColumnAsync(db, "AppUsers", "UnitId", "TEXT NULL");
            await EnsureColumnAsync(db, "AppUsers", "PositionId", "TEXT NULL");
            await EnsureColumnAsync(db, "AppUsers", "CompanyName", "TEXT NULL");
            await EnsureColumnAsync(db, "AppUsers", "UnitName", "TEXT NULL");
            await EnsureColumnAsync(db, "AppUsers", "PositionTitle", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "ServerWorkTaskId", "TEXT NULL");
            await EnsureColumnAsync(db, "Tasks", "AssignedByName", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Friendships (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RequesterId TEXT NOT NULL,
                    AddresseeId TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    CanViewAgenda INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Friendships_Pair
                ON Friendships (RequesterId, AddresseeId);
                """);
            await EnsureColumnAsync(db, "Friendships", "RequesterKind", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(db, "Friendships", "AddresseeKind", "INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE Friendships
                SET RequesterKind = 1, AddresseeKind = 1
                WHERE CanViewAgenda = 1 AND RequesterKind = 0 AND AddresseeKind = 0;
                """);

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS DocumentShares (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DocumentId TEXT NOT NULL,
                    OwnerUserId TEXT NOT NULL,
                    SharedWithUserId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DocumentShares_Doc_User
                ON DocumentShares (DocumentId, SharedWithUserId);
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
