using Microsoft.EntityFrameworkCore;
using Planner.Core.Models;

namespace Planner.Core.Data;

public sealed class PlannerDbContext : DbContext
{
    public PlannerDbContext(DbContextOptions<PlannerDbContext> options) : base(options)
    {
    }

    public DbSet<PlannerTask> Tasks => Set<PlannerTask>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<EncryptedContact> Contacts => Set<EncryptedContact>();
    public DbSet<VaultMeta> Vault => Set<VaultMeta>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<RecurrenceException> RecurrenceExceptions => Set<RecurrenceException>();
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();
    public DbSet<DailyNote> DailyNotes => Set<DailyNote>();
    public DbSet<DayPriority> DayPriorities => Set<DayPriority>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<QueuedNotification> QueuedNotifications => Set<QueuedNotification>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<EncryptedSocialAccount> SocialAccounts => Set<EncryptedSocialAccount>();
    public DbSet<PersonRelationship> Relationships => Set<PersonRelationship>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<MeProfile> MeProfiles => Set<MeProfile>();
    public DbSet<PersonSegment> PersonSegments => Set<PersonSegment>();
    public DbSet<PersonOrganization> PersonOrganizations => Set<PersonOrganization>();
    public DbSet<TaskStatusSpan> TaskStatusSpans => Set<TaskStatusSpan>();
    public DbSet<WorkspaceDocument> WorkspaceDocuments => Set<WorkspaceDocument>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<PlannerTask>(e =>
        {
            e.ToTable("Tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.Date, x.SortOrder });
            e.HasIndex(x => new { x.ReminderFired, x.ReminderAt });
            e.HasIndex(x => x.OwnerUserId);
            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EncryptedContact>(e =>
        {
            e.ToTable("Contacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).IsRequired();
        });

        modelBuilder.Entity<VaultMeta>(e =>
        {
            e.ToTable("Vault");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<RecurrenceException>(e =>
        {
            e.ToTable("RecurrenceExceptions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SeriesId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<Habit>(e =>
        {
            e.ToTable("Habits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<HabitLog>(e =>
        {
            e.ToTable("HabitLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<DailyNote>(e =>
        {
            e.ToTable("DailyNotes");
            e.HasKey(x => x.Date);
            e.Property(x => x.Content).HasMaxLength(8000);
        });

        modelBuilder.Entity<DayPriority>(e =>
        {
            e.ToTable("DayPriorities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Date, x.Slot }).IsUnique();
            e.HasIndex(x => new { x.Date, x.TaskId }).IsUnique();
        });

        modelBuilder.Entity<TaskAttachment>(e =>
        {
            e.ToTable("TaskAttachments");
            e.HasKey(x => x.Id);
            e.Property(x => x.OriginalName).HasMaxLength(260).IsRequired();
            e.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
            e.HasIndex(x => x.TaskId);
        });

        modelBuilder.Entity<QueuedNotification>(e =>
        {
            e.ToTable("QueuedNotifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<LeaveType>(e =>
        {
            e.ToTable("LeaveTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<LeaveRecord>(e =>
        {
            e.ToTable("LeaveRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasIndex(x => new { x.StartDate, x.EndDate });
            e.HasIndex(x => x.TypeId);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.ServerLeaveId);
            e.HasOne(x => x.Type)
                .WithMany()
                .HasForeignKey(x => x.TypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EncryptedSocialAccount>(e =>
        {
            e.ToTable("SocialAccounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).IsRequired();
            e.HasIndex(x => x.ContactId);
        });

        modelBuilder.Entity<PersonRelationship>(e =>
        {
            e.ToTable("Relationships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.FromPersonId, x.ToPersonId });
        });

        modelBuilder.Entity<Segment>(e =>
        {
            e.ToTable("Segments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<Organization>(e =>
        {
            e.ToTable("Organizations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Role).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(80);
            e.Property(x => x.Address).HasMaxLength(400);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.SegmentId);
        });

        modelBuilder.Entity<MeProfile>(e =>
        {
            e.ToTable("MeProfile");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.PhotoFileName).HasMaxLength(80);
        });

        modelBuilder.Entity<PersonSegment>(e =>
        {
            e.ToTable("PersonSegments");
            e.HasKey(x => new { x.PersonId, x.SegmentId });
            e.HasIndex(x => x.SegmentId);
        });

        modelBuilder.Entity<PersonOrganization>(e =>
        {
            e.ToTable("PersonOrganizations");
            e.HasKey(x => new { x.PersonId, x.OrganizationId });
            e.HasIndex(x => x.OrganizationId);
            e.Property(x => x.Title).HasMaxLength(160);
        });

        modelBuilder.Entity<TaskStatusSpan>(e =>
        {
            e.ToTable("TaskStatusSpans");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TaskId);
        });

        modelBuilder.Entity<WorkspaceDocument>(e =>
        {
            e.ToTable("WorkspaceDocuments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).IsRequired();
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("AppUsers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(80).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.Email).HasMaxLength(160);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Friendship>(e =>
        {
            e.ToTable("Friendships");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
        });

        modelBuilder.Entity<DocumentShare>(e =>
        {
            e.ToTable("DocumentShares");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DocumentId, x.SharedWithUserId }).IsUnique();
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("ChatMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).IsRequired();
            e.HasIndex(x => new { x.ConversationKey, x.SentAt });
        });
    }
}
