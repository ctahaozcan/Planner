using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Planner.ChatServer;

public static class ChatServerMigrator
{
    public static async Task ApplyAsync(ChatServerDb db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await EnsureColumnAsync(db, "Users", "Email", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "Users", "FirstName", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "Users", "LastName", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(db, "Users", "Usage", "TEXT NOT NULL DEFAULT 'personal'");
            await EnsureColumnAsync(db, "Users", "CompanyId", "TEXT NULL");
            await EnsureColumnAsync(db, "Users", "UnitId", "TEXT NULL");
            await EnsureColumnAsync(db, "Users", "PositionId", "TEXT NULL");

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgCompanies (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Domain TEXT NOT NULL,
                    Notes TEXT NOT NULL DEFAULT '',
                    Active INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgUnits (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    ParentId TEXT NULL,
                    Name TEXT NOT NULL,
                    Kind TEXT NOT NULL DEFAULT 'unit',
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgPositions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    UnitId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    ReportsToPositionId TEXT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgWorkTasks (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Notes TEXT NULL,
                    Date TEXT NOT NULL,
                    Time TEXT NULL,
                    AssignedByUserId TEXT NOT NULL,
                    AssignedToUserId TEXT NOT NULL,
                    ParentTaskId TEXT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await EnsureColumnAsync(db, "OrgPositions", "CanApproveLeaves", "INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgInvites (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    PositionId TEXT NOT NULL,
                    Code TEXT NOT NULL UNIQUE,
                    Email TEXT NULL,
                    ExpiresAt TEXT NOT NULL,
                    UsedAt TEXT NULL,
                    UsedByUserId TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgLeaves (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    UserId TEXT NOT NULL,
                    ClientId TEXT NULL,
                    TypeName TEXT NOT NULL,
                    EntryKind INTEGER NOT NULL DEFAULT 0,
                    DurationKind INTEGER NOT NULL DEFAULT 1,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    StartTime TEXT NULL,
                    EndTime TEXT NULL,
                    StartHalf INTEGER NOT NULL DEFAULT 0,
                    EndHalf INTEGER NOT NULL DEFAULT 0,
                    Note TEXT NULL,
                    Status TEXT NOT NULL DEFAULT 'pending',
                    DecidedByUserId TEXT NULL,
                    DurationMinutes INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgWorkFiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    TaskId TEXT NOT NULL,
                    UploadedByUserId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    StoredName TEXT NOT NULL,
                    ContentType TEXT NOT NULL DEFAULT 'application/octet-stream',
                    SizeBytes INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgAuditLog (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CompanyId TEXT NOT NULL,
                    At TEXT NOT NULL,
                    ActorUserId TEXT NOT NULL,
                    ActorName TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    TargetUserId TEXT NULL,
                    TargetName TEXT NULL,
                    Detail TEXT NOT NULL DEFAULT ''
                );
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS OrgSettings (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                """);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task EnsureColumnAsync(ChatServerDb db, string table, string column, string definition)
    {
        if (await ColumnExistsAsync(db, table, column))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");
    }

    private static async Task<bool> ColumnExistsAsync(ChatServerDb db, string table, string column)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
