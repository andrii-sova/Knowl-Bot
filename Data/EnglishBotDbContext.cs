using VocabifyBot.Models;
using Microsoft.EntityFrameworkCore;

namespace VocabifyBot.Data;

public class EnglishBotDbContext : DbContext
{
    public DbSet<User>              Users              => Set<User>();
    public DbSet<TeacherStudent>    TeacherStudents    => Set<TeacherStudent>();
    public DbSet<Word>              Words              => Set<Word>();
    public DbSet<WordStat>          WordStats          => Set<WordStat>();
    public DbSet<PendingInvitation> PendingInvitations => Set<PendingInvitation>();

    public EnglishBotDbContext(DbContextOptions<EnglishBotDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.TelegramId);
            b.Property(u => u.TelegramId).ValueGeneratedNever();
            b.Property(u => u.Username).HasMaxLength(255).IsRequired();
            b.Property(u => u.FirstName).HasMaxLength(255).IsRequired();
            b.Property(u => u.DisplayNameOverride).HasMaxLength(60);
            b.Property(u => u.Role).HasMaxLength(20).IsRequired();
            b.Property(u => u.IsActivated).HasDefaultValue(false);
            b.Property(u => u.CreatedAt).HasDefaultValueSql("GETDATE()");
            b.Ignore(u => u.DisplayName);
        });

        modelBuilder.Entity<TeacherStudent>(b =>
        {
            b.HasKey(ts => new { ts.TeacherId, ts.StudentId });

            b.HasOne(ts => ts.Teacher)
             .WithMany(u => u.LinkedStudents)
             .HasForeignKey(ts => ts.TeacherId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(ts => ts.Student)
             .WithMany(u => u.LinkedTeachers)
             .HasForeignKey(ts => ts.StudentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Word>(b =>
        {
            b.HasKey(w => w.Id);
            b.Property(w => w.OriginalWord).HasMaxLength(500).IsRequired();
            b.Property(w => w.Translation).IsRequired();
            b.Property(w => w.Topic).HasMaxLength(60);
            b.Property(w => w.EnglishLevel).HasMaxLength(2);
            b.Property(w => w.BatchId);
            b.Property(w => w.CreatedAt).HasDefaultValueSql("GETDATE()");

            b.HasOne(w => w.AddedBy)
             .WithMany(u => u.WordsSent)
             .HasForeignKey(w => w.AddedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(w => w.ForStudent)
             .WithMany(u => u.WordsReceived)
             .HasForeignKey(w => w.ForStudentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WordStat>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.WordId, s.StudentId }).IsUnique();

            b.HasOne(s => s.Word)
             .WithMany()
             .HasForeignKey(s => s.WordId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Student)
             .WithMany()
             .HasForeignKey(s => s.StudentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PendingInvitation>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasIndex(p => new { p.TeacherId, p.StudentUsername }).IsUnique();
            b.Property(p => p.StudentUsername).HasMaxLength(255).IsRequired();
            b.Property(p => p.CreatedAt).HasDefaultValueSql("GETDATE()");

            b.HasOne(p => p.Teacher)
             .WithMany()
             .HasForeignKey(p => p.TeacherId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
