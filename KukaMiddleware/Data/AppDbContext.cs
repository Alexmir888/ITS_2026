using KukaMiddleware.Models;
using Microsoft.EntityFrameworkCore;

namespace KukaMiddleware.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<StatusHistory> StatusHistories => Set<StatusHistory>();
    public DbSet<Robot> Robots => Set<Robot>();
    public DbSet<RobotStatusHistory> RobotStatusHistories => Set<RobotStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mission>(entity =>
        {
            entity.HasKey(mission => mission.Id);
            entity.HasIndex(mission => mission.MissionCode).IsUnique();
            entity.HasIndex(mission => mission.RequestId).IsUnique();
            entity.Property(mission => mission.MissionCode).HasMaxLength(160).IsRequired();
            entity.Property(mission => mission.RequestId).HasMaxLength(32).IsRequired();
            entity.Property(mission => mission.RobotId).HasMaxLength(120).IsRequired();
            entity.Property(mission => mission.MissionType).HasMaxLength(20).IsRequired();
            entity.Property(mission => mission.StartNode).HasMaxLength(64).IsRequired();
            entity.Property(mission => mission.TargetNode).HasMaxLength(64).IsRequired();
            entity.Property(mission => mission.ContainerCode).HasMaxLength(64).IsRequired();
            entity.Property(mission => mission.StatusName).HasMaxLength(32).IsRequired();
            entity.HasOne(mission => mission.Robot)
                .WithMany()
                .HasForeignKey(mission => mission.RobotId)
                .HasPrincipalKey(robot => robot.RobotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(mission => mission.History)
                .WithOne(history => history.Mission)
                .HasForeignKey(history => history.MissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusHistory>(entity =>
        {
            entity.HasKey(history => history.Id);
            entity.Property(history => history.StatusName).HasMaxLength(32).IsRequired();
            entity.HasIndex(history => new { history.MissionId, history.ValidFrom });
        });

        modelBuilder.Entity<Robot>(entity =>
        {
            entity.HasKey(robot => robot.RobotId);
            entity.Property(robot => robot.RobotId).HasMaxLength(120);
            entity.Property(robot => robot.RobotType).HasMaxLength(32);
            entity.Property(robot => robot.StatusName).HasMaxLength(32).IsRequired();
            entity.Property(robot => robot.Availability).HasMaxLength(16).IsRequired();
            entity.Property(robot => robot.BatteryLevel).HasPrecision(5, 2);
            entity.Property(robot => robot.MapCode).HasMaxLength(32);
            entity.Property(robot => robot.NodeCode).HasMaxLength(64);
            entity.Property(robot => robot.MissionCode).HasMaxLength(160);
            entity.HasMany(robot => robot.History)
                .WithOne(history => history.Robot)
                .HasForeignKey(history => history.RobotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RobotStatusHistory>(entity =>
        {
            entity.HasKey(history => history.Id);
            entity.Property(history => history.RobotType).HasMaxLength(32);
            entity.Property(history => history.StatusName).HasMaxLength(32).IsRequired();
            entity.Property(history => history.Availability).HasMaxLength(16).IsRequired();
            entity.Property(history => history.BatteryLevel).HasPrecision(5, 2);
            entity.Property(history => history.MapCode).HasMaxLength(32);
            entity.Property(history => history.NodeCode).HasMaxLength(64);
            entity.Property(history => history.MissionCode).HasMaxLength(160);
            entity.HasIndex(history => new { history.RobotId, history.ValidTo });
        });
    }
}