using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
using KukaMiddleware.Models;
using KukaMiddleware.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KukaMiddleware.Tests;

public sealed class PollingTests
{
    [Fact]
    public async Task PollSyncsOnlyTrackedMissionsAndKeepsMissionAndRobotHistory()
    {
        var kuka = new PollingKukaFmClient
        {
            Jobs =
            [
                new KukaJobResponse("seen", "R-01", 31),
                new KukaJobResponse("not-tracked", "R-99", 30)
            ],
            Robots =
            [
                new KukaRobotResponse("R-01", "LIFT", 4, 72.5m, "M1", "A-02", "seen"),
                new KukaRobotResponse("R-02", "LIFT", 3, 99m, "M1", "A-03", null)
            ],
            FallbackJobs = new Dictionary<string, KukaJobResponse>
            {
                ["fallback"] = new("fallback", "R-01", 30)
            }
        };
        using var provider = CreateServices(kuka);
        await SeedAsync(provider);
        using (var seededScope = provider.CreateScope())
        {
            var seededDb = seededScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await seededDb.Missions.CountAsync());
        }

        await CreateWorker(provider).PollOnceAsync(CancellationToken.None);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seen = await db.Missions.Include(mission => mission.History)
                .SingleAsync(mission => mission.MissionCode == "seen");
            Assert.Equal((short)31, seen.StatusCode);
            Assert.Equal("Cancelled", seen.StatusName);
            Assert.NotNull(seen.CompletedAt);
            Assert.Equal(2, seen.History.Count);
            Assert.NotNull(seen.History.Single(item => item.StatusCode == 20).ValidTo);
            Assert.Null(seen.History.Single(item => item.StatusCode == 31).ValidTo);

            var fallback = await db.Missions.SingleAsync(mission => mission.MissionCode == "fallback");
            Assert.Equal((short)30, fallback.StatusCode);
            Assert.Equal(1, kuka.FallbackQueryCount);
            Assert.False(await db.Missions.AnyAsync(mission => mission.MissionCode == "not-tracked"));

            var changedRobot = await db.Robots.Include(robot => robot.History)
                .SingleAsync(robot => robot.RobotId == "R-01");
            Assert.Equal((short)4, changedRobot.StatusCode);
            Assert.Equal("Busy", changedRobot.Availability);
            var previousRobotState = Assert.Single(changedRobot.History);
            Assert.Equal((short)3, previousRobotState.StatusCode);
            Assert.Equal("Available", previousRobotState.Availability);

            var newRobot = await db.Robots.Include(robot => robot.History)
                .SingleAsync(robot => robot.RobotId == "R-02");
            Assert.Equal((short)3, newRobot.StatusCode);
            Assert.Empty(newRobot.History);
        }

        await CreateWorker(provider).PollOnceAsync(CancellationToken.None);
        using var repeatedScope = provider.CreateScope();
        var repeatedDb = repeatedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await repeatedDb.StatusHistories.CountAsync());
        Assert.Single(await repeatedDb.RobotStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task FailedKukaQueryDoesNotPersistRobotChanges()
    {
        var kuka = new PollingKukaFmClient
        {
            Robots = [new KukaRobotResponse("R-01", "LIFT", 4, 70m, "M1", "A-02", null)],
            JobsException = new HttpRequestException("KUKA unavailable.")
        };
        using var provider = CreateServices(kuka);
        await SeedAsync(provider);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateWorker(provider).PollOnceAsync(CancellationToken.None));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var robot = await db.Robots.SingleAsync(item => item.RobotId == "R-01");
        Assert.Equal((short)3, robot.StatusCode);
        Assert.Empty(await db.RobotStatusHistories.ToListAsync());
    }

    [Theory]
    [InlineData(3, "Available")]
    [InlineData(1, "Busy")]
    [InlineData(4, "Busy")]
    [InlineData(5, "Busy")]
    [InlineData(6, "Busy")]
    [InlineData(2, "Offline")]
    [InlineData(7, "Offline")]
    public void RobotStatusesMapToTheBacklogAvailability(short code, string expected) =>
        Assert.Equal(expected, StatusMapping.RobotAvailability(code));

    private static ServiceProvider CreateServices(IKukaFmClient kuka)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(kuka);
        services.AddSingleton<IKukaFmClient>(kuka);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Robots.Add(new Robot
        {
            RobotId = "R-01",
            RobotType = "LIFT",
            StatusCode = 3,
            StatusName = "Idle",
            Availability = "Available",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        db.Missions.Add(CreateMission("seen", 20));
        db.Missions.Add(CreateMission("fallback", 10));
        await db.SaveChangesAsync();
    }

    private static Mission CreateMission(string code, short status)
    {
        var now = DateTime.UtcNow.AddMinutes(-5);
        var mission = new Mission
        {
            MissionCode = code,
            RequestId = Guid.NewGuid().ToString("N"),
            RobotId = "R-01",
            MissionType = "RACK_MOVE",
            StartNode = "A-01",
            TargetNode = "A-02",
            ContainerCode = "BIN-01",
            Priority = 50,
            StatusCode = status,
            StatusName = StatusMapping.MissionName(status),
            CreatedAt = now,
            UpdatedAt = now
        };
        mission.History.Add(new StatusHistory
        {
            StatusCode = status,
            StatusName = mission.StatusName,
            ValidFrom = now
        });
        return mission;
    }

    private static MissionPollingWorker CreateWorker(IServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new KukaFmOptions()),
        NullLogger<MissionPollingWorker>.Instance);

    private sealed class PollingKukaFmClient : IKukaFmClient
    {
        public IReadOnlyList<KukaJobResponse> Jobs { get; init; } = [];
        public IReadOnlyList<KukaRobotResponse> Robots { get; init; } = [];
        public IReadOnlyDictionary<string, KukaJobResponse> FallbackJobs { get; init; } =
            new Dictionary<string, KukaJobResponse>();
        public HttpRequestException? JobsException { get; init; }
        public int FallbackQueryCount { get; private set; }

        public Task DispatchMissionAsync(KukaSubmitMissionRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CancelMissionAsync(KukaCancelMissionRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<KukaJobResponse>> QueryJobsAsync(CancellationToken cancellationToken) =>
            JobsException is null
                ? Task.FromResult(Jobs)
                : Task.FromException<IReadOnlyList<KukaJobResponse>>(JobsException);

        public Task<KukaJobResponse?> QueryJobAsync(string missionCode, CancellationToken cancellationToken)
        {
            FallbackQueryCount++;
            FallbackJobs.TryGetValue(missionCode, out var job);
            return Task.FromResult(job);
        }

        public Task<IReadOnlyList<KukaRobotResponse>> QueryRobotsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Robots);
    }
}
