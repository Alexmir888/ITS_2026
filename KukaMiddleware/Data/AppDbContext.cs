using KukaMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace KukaMiddleware.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<StatusHistory> StatusHistories => Set<StatusHistory>();
    public DbSet<Robot> Robots => Set<Robot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mission>(entity =>
        {
            entity.HasKey(mission => mission.Id);
            entity.HasIndex(mission => mission.MissionCode).IsUnique();
            entity.Property(mission => mission.MissionCode).HasMaxLength(160).IsRequired();
            entity.Property(mission => mission.RobotId).HasMaxLength(120).IsRequired();
            entity.Property(mission => mission.Status).HasMaxLength(80).IsRequired();
            entity.HasMany(mission => mission.History)
                .WithOne(history => history.Mission)
                .HasForeignKey(history => history.MissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusHistory>(entity =>
        {
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Status).HasMaxLength(80).IsRequired();
            entity.HasIndex(history => new { history.MissionId, history.Timestamp });
        });

        modelBuilder.Entity<Robot>(entity =>
        {
            entity.HasKey(robot => robot.RobotId);
            entity.Property(robot => robot.RobotId).HasMaxLength(120);
            entity.Property(robot => robot.Status).HasMaxLength(80).IsRequired();
        });
    }
}