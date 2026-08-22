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
            e.HasIndex(x => new { x.ReminderFired, x.ReminderAt });
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
            e.HasOne(x => x.Type)
                .WithMany()
                .HasForeignKey(x => x.TypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
