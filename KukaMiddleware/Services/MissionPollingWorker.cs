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

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var kuka = scope.ServiceProvider.GetRequiredService<IKukaFmClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var robots = await kuka.QueryRobotsAsync(cancellationToken);
        var jobs = await kuka.QueryJobsAsync(cancellationToken);
        var activeMissions = await db.Missions
            .Include(mission => mission.History)
            .Where(mission => mission.StatusCode == 10
                || mission.StatusCode == 20
                || mission.StatusCode == 25
                || mission.StatusCode == 28
                || mission.StatusCode == 50)
            .ToListAsync(cancellationToken);
        var knownRobots = await db.Robots
            .Include(robot => robot.History)
            .ToDictionaryAsync(robot => robot.RobotId, StringComparer.Ordinal, cancellationToken);

        var jobsByCode = jobs
            .Where(job => !string.IsNullOrWhiteSpace(job.MissionCode))
            .GroupBy(job => job.MissionCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var mission in activeMissions)
        {
            if (!jobsByCode.TryGetValue(mission.MissionCode, out var job))
            {
                job = await kuka.QueryJobAsync(mission.MissionCode, cancellationToken);
            }

            if (job is null)
            {
                continue;
            }

            ApplyJob(mission, job, DateTime.UtcNow);
        }

        foreach (var response in robots)
        {
            if (!knownRobots.TryGetValue(response.RobotId, out var robot))
            {
                robot = CreateRobot(response, DateTime.UtcNow);
                db.Robots.Add(robot);
                knownRobots.Add(response.RobotId, robot);
                continue;
            }

            ApplyRobot(robot, response, DateTime.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "KUKA FM polling completed: {MissionCount} tracked missions, {RobotCount} robots",
            activeMissions.Count, robots.Count);
    }

    private static void ApplyJob(Mission mission, KukaJobResponse job, DateTime now)
    {
        var changed = mission.StatusCode != job.Status
            || !string.Equals(mission.RobotId, job.RobotId, StringComparison.Ordinal);
        if (!changed)
        {
            return;
        }

        if (mission.StatusCode != job.Status)
        {
            var currentHistory = mission.History
                .Where(history => history.ValidTo is null)
                .OrderByDescending(history => history.ValidFrom)
                .FirstOrDefault();
            if (currentHistory is not null)
            {
                currentHistory.ValidTo = now;
            }

            mission.History.Add(new StatusHistory
            {
                StatusCode = job.Status,
                StatusName = StatusMapping.MissionName(job.Status),
                ValidFrom = now
            });
            mission.StatusCode = job.Status;
            mission.StatusName = StatusMapping.MissionName(job.Status);
            mission.CompletedAt = StatusMapping.IsFinishedMission(job.Status) ? now : null;
        }

        mission.RobotId = job.RobotId;
        mission.UpdatedAt = now;
    }

    private static void ApplyRobot(Robot robot, KukaRobotResponse response, DateTime now)
    {
        if (robot.RobotType == response.RobotType
            && robot.StatusCode == response.Status
            && robot.BatteryLevel == response.BatteryLevel
            && robot.MapCode == response.MapCode
            && robot.NodeCode == response.NodeCode
            && robot.MissionCode == response.MissionCode)
        {
            return;
        }

        robot.History.Add(new RobotStatusHistory
        {
            RobotId = robot.RobotId,
            RobotType = robot.RobotType,
            StatusCode = robot.StatusCode,
            StatusName = robot.StatusName,
            Availability = robot.Availability,
            BatteryLevel = robot.BatteryLevel,
            MapCode = robot.MapCode,
            NodeCode = robot.NodeCode,
            MissionCode = robot.MissionCode,
            ValidFrom = robot.UpdatedAt,
            ValidTo = now
        });
        robot.RobotType = response.RobotType;
        robot.StatusCode = response.Status;
        robot.StatusName = StatusMapping.RobotName(response.Status);
        robot.Availability = StatusMapping.RobotAvailability(response.Status);
        robot.BatteryLevel = response.BatteryLevel;
        robot.MapCode = response.MapCode;
        robot.NodeCode = response.NodeCode;
        robot.MissionCode = response.MissionCode;
        robot.UpdatedAt = now;
    }

    private static Robot CreateRobot(KukaRobotResponse response, DateTime now) => new()
    {
        RobotId = response.RobotId,
        RobotType = response.RobotType,
        StatusCode = response.Status,
        StatusName = StatusMapping.RobotName(response.Status),
        Availability = StatusMapping.RobotAvailability(response.Status),
        BatteryLevel = response.BatteryLevel,
        MapCode = response.MapCode,
        NodeCode = response.NodeCode,
        MissionCode = response.MissionCode,
        UpdatedAt = now
    };
}
