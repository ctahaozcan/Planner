using Microsoft.EntityFrameworkCore;

namespace Planner.ChatServer;

public sealed class ChatServerDb : DbContext
{
    public ChatServerDb(DbContextOptions<ChatServerDb> options) : base(options)
    {
    }

    public DbSet<ServerUser> Users => Set<ServerUser>();
    public DbSet<ServerSession> Sessions => Set<ServerSession>();
    public DbSet<ServerMessage> Messages => Set<ServerMessage>();
    public DbSet<OrgCompany> Companies => Set<OrgCompany>();
    public DbSet<OrgUnit> Units => Set<OrgUnit>();
    public DbSet<OrgPosition> Positions => Set<OrgPosition>();
    public DbSet<OrgWorkTask> WorkTasks => Set<OrgWorkTask>();
    public DbSet<OrgInvite> Invites => Set<OrgInvite>();
    public DbSet<OrgLeave> Leaves => Set<OrgLeave>();
    public DbSet<OrgWorkFile> WorkFiles => Set<OrgWorkFile>();
    public DbSet<OrgAuditEvent> AuditLog => Set<OrgAuditEvent>();
    public DbSet<OrgSetting> Settings => Set<OrgSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerUser>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(32).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(160);
            e.Property(x => x.Usage).HasMaxLength(16);
        });

        modelBuilder.Entity<ServerSession>(e =>
        {
            e.ToTable("Sessions");
            e.HasKey(x => x.Token);
            e.Property(x => x.Token).HasMaxLength(80);
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<ServerMessage>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ToUserId, x.SentAt });
            e.HasIndex(x => new { x.FromUserId, x.ToUserId, x.SentAt });
            e.Property(x => x.Body).HasMaxLength(700000).IsRequired();
        });

        modelBuilder.Entity<OrgCompany>(e =>
        {
            e.ToTable("OrgCompanies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<OrgUnit>(e =>
        {
            e.ToTable("OrgUnits");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(16);
        });

        modelBuilder.Entity<OrgPosition>(e =>
        {
            e.ToTable("OrgPositions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.UnitId);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<OrgWorkTask>(e =>
        {
            e.ToTable("OrgWorkTasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.AssignedToUserId);
            e.HasIndex(x => x.AssignedByUserId);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.Date).HasMaxLength(16);
            e.Property(x => x.Time).HasMaxLength(8);
        });

        modelBuilder.Entity<OrgInvite>(e =>
        {
            e.ToTable("OrgInvites");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Email).HasMaxLength(160);
        });

        modelBuilder.Entity<OrgLeave>(e =>
        {
            e.ToTable("OrgLeaves");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TypeName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.StartDate).HasMaxLength(16);
            e.Property(x => x.EndDate).HasMaxLength(16);
        });

        modelBuilder.Entity<OrgWorkFile>(e =>
        {
            e.ToTable("OrgWorkFiles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TaskId);
            e.Property(x => x.Name).HasMaxLength(260).IsRequired();
            e.Property(x => x.StoredName).HasMaxLength(80).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120);
        });

        modelBuilder.Entity<OrgAuditEvent>(e =>
        {
            e.ToTable("OrgAuditLog");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.At });
            e.Property(x => x.Action).HasMaxLength(40).IsRequired();
            e.Property(x => x.ActorName).HasMaxLength(120).IsRequired();
            e.Property(x => x.TargetName).HasMaxLength(120);
            e.Property(x => x.Detail).HasMaxLength(2000);
        });

        modelBuilder.Entity<OrgSetting>(e =>
        {
            e.ToTable("OrgSettings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Value).HasMaxLength(4000);
        });
    }
}

public sealed class ServerUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Usage { get; set; } = "personal";
    public Guid? CompanyId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? PositionId { get; set; }
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordVerifier { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class ServerSession
{
    public string Token { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public sealed class ServerMessage
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string FromName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; }
    public bool Delivered { get; set; }
}

public sealed class OrgCompany
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class OrgUnit
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "unit";
    public int SortOrder { get; set; }
}

public sealed class OrgPosition
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UnitId { get; set; }
    public string Title { get; set; } = "";
    public Guid? ReportsToPositionId { get; set; }
    public int SortOrder { get; set; }
    public bool CanApproveLeaves { get; set; }
}

public sealed class OrgWorkTask
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string Date { get; set; } = "";
    public string? Time { get; set; }
    public Guid AssignedByUserId { get; set; }
    public Guid AssignedToUserId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OrgInvite
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PositionId { get; set; }
    public string Code { get; set; } = "";
    public string? Email { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OrgLeave
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ClientId { get; set; }
    public string TypeName { get; set; } = "";
    public int EntryKind { get; set; }
    public int DurationKind { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public int StartHalf { get; set; }
    public int EndHalf { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? DecidedByUserId { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OrgWorkFile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid TaskId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string Name { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OrgAuditEvent
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime At { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? TargetUserId { get; set; }
    public string? TargetName { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class OrgSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
