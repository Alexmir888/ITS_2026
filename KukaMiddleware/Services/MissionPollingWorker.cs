using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
using KukaMiddleware.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KukaMiddleware.Services;

public sealed class MissionPollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<KukaFmOptions> options,
    ILogger<MissionPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "KUKA FM polling failed; next attempt in {Interval}", interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var kuka = scope.ServiceProvider.GetRequiredService<IKukaFmClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jobs = await kuka.QueryJobsAsync(cancellationToken);
        foreach (var job in jobs)
        {
            var mission = await db.Missions
                .Include(item => item.History)
                .SingleOrDefaultAsync(item => item.MissionCode == job.MissionCode, cancellationToken);
            if (mission is null)
            {
                mission = new Mission
                {
                    MissionCode = job.MissionCode,
                    RobotId = job.RobotId,
                    Status = MapJobStatus(job.Status)
                };
                mission.History.Add(new StatusHistory { Status = mission.Status });
                db.Missions.Add(mission);
                continue;
            }

            mission.RobotId = job.RobotId;
            var jobStatus = MapJobStatus(job.Status);
            if (!string.Equals(mission.Status, jobStatus, StringComparison.OrdinalIgnoreCase))
            {
                mission.Status = jobStatus;
                mission.History.Add(new StatusHistory { Status = jobStatus });
            }
        }

        var robots = await kuka.QueryRobotsAsync(cancellationToken);
        foreach (var item in robots)
        {
            var robot = await db.Robots.FindAsync([item.RobotId], cancellationToken);
            if (robot is null)
            {
                db.Robots.Add(new Robot { RobotId = item.RobotId, Status = MapRobotStatus(item.Status) });
            }
            else
            {
                robot.Status = MapRobotStatus(item.Status);
                robot.LastUpdated = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("KUKA FM polling completed: {MissionCount} jobs, {RobotCount} robots",
            jobs.Count, robots.Count);
    }

    private static string MapJobStatus(int status) => status switch
    {
        10 => "CREATED",
        20 => "EXECUTING",
        25 => "WAITING",
        28 => "CANCELLING",
        30 => "COMPLETED",
        31 => "CANCELLED",
        35 => "MANUAL_COMPLETED",
        50 => "WARNING",
        60 => "STARTUP_ERROR",
        _ => $"UNKNOWN_{status}"
    };

    private static string MapRobotStatus(int status) => status switch
    {
        1 => "DEPARTURE",
        2 => "OFFLINE",
        3 => "AVAILABLE",
        4 => "BUSY",
        5 => "CHARGING",
        6 => "UPDATING",
        7 => "ABNORMAL",
        _ => $"UNKNOWN_{status}"
    };
}