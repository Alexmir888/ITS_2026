using System.Net;
using System.Net.Http.Json;
using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
using KukaMiddleware.Models;
using KukaMiddleware.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace KukaMiddleware.Tests;

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        SeedRobot(factory);
    }

    [Fact]
    public async Task ProtectedEndpointsRejectRequestsWithoutApiKey()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/robots");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidDispatchIsRejectedBeforeCallingKuka()
    {
        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        var initialCallCount = kuka!.DispatchCount;
        var response = await _client.PostAsJsonAsync("/api/missions", new
        {
            robotId = "R-01",
            startNode = " ",
            targetNode = "A-02",
            containerCode = "BIN-01",
            priority = 100
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(initialCallCount, kuka.DispatchCount);
    }

    [Fact]
    public async Task DispatchStoresRACKMOVEAndInitialHistoryAfterKukaAccepts()
    {
        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        var response = await _client.PostAsJsonAsync("/api/missions", ValidDispatchRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mission = await response.Content.ReadFromJsonAsync<MissionResponse>();
        Assert.NotNull(mission);
        Assert.Equal("RACK_MOVE", mission.MissionType);
        Assert.Equal((short)10, mission.StatusCode);
        Assert.Equal("Created", mission.StatusName);
        Assert.Equal("A-01", mission.StartNode);
        Assert.Equal("A-02", mission.TargetNode);
        Assert.Equal("BIN-01", mission.ContainerCode);
        Assert.Equal(50, mission.Priority);

        var dispatch = Assert.IsType<KukaSubmitMissionRequest>(kuka!.LastDispatch);
        Assert.Equal("RACK_MOVE", dispatch.MissionType);
        Assert.Equal("R-01", Assert.Single(dispatch.RobotIds));
        Assert.Equal("BIN-01", dispatch.ContainerCode);
        Assert.Equal("UP_CONTAINER", dispatch.MissionData[0].ActionType);
        Assert.Equal("DOWN_CONTAINER", dispatch.MissionData[1].ActionType);

        var history = await _client.GetFromJsonAsync<List<MissionHistoryResponse>>(
            $"/api/missions/{mission.MissionCode}/history");
        Assert.NotNull(history);
        var initialState = Assert.Single(history);
        Assert.Equal((short)10, initialState.StatusCode);
        Assert.Null(initialState.ValidTo);

        var active = await _client.GetFromJsonAsync<List<MissionResponse>>("/api/missions?state=active");
        Assert.Contains(active!, item => item.MissionCode == mission.MissionCode);
    }

    [Fact]
    public async Task DispatchRejectsUnknownRobotWithoutCallingKuka()
    {
        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        var initialCallCount = kuka!.DispatchCount;
        var response = await _client.PostAsJsonAsync("/api/missions", new
        {
            startNode = "A-01",
            targetNode = "A-02",
            containerCode = "BIN-01",
            priority = 50,
            robotId = "unknown"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(initialCallCount, kuka.DispatchCount);
    }

    [Fact]
    public async Task CancelOnlyAcceptsKnownActiveMissions()
    {
        var activeMission = await CreateMissionAsync(20);
        var response = await _client.PostAsync($"/api/missions/{activeMission}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        Assert.Equal("FORCE", kuka!.LastCancel?.CancelMode);
        Assert.Equal(activeMission, kuka.LastCancel?.MissionCode);

        var unknown = await _client.PostAsync("/api/missions/missing/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var finishedMission = await CreateMissionAsync(31);
        var finished = await _client.PostAsync($"/api/missions/{finishedMission}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, finished.StatusCode);
    }

    [Fact]
    public async Task CancelKukaFailureReturns502()
    {
        var missionCode = await CreateMissionAsync(20);
        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        kuka!.CancelException = new HttpRequestException("KUKA cancel rejected.");

        var response = await _client.PostAsync($"/api/missions/{missionCode}/cancel", null);

        kuka.CancelException = null;
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task MissionFiltersSeparateActiveAndFinishedMissions()
    {
        var activeCode = await CreateMissionAsync(10);
        var finishedCode = await CreateMissionAsync(30);

        var active = await _client.GetFromJsonAsync<List<MissionResponse>>("/api/missions?state=active");
        var finished = await _client.GetFromJsonAsync<List<MissionResponse>>("/api/missions?state=finished");
        var invalid = await _client.GetAsync("/api/missions?state=unknown");

        Assert.Contains(active!, mission => mission.MissionCode == activeCode);
        Assert.DoesNotContain(active!, mission => mission.MissionCode == finishedCode);
        Assert.Contains(finished!, mission => mission.MissionCode == finishedCode);
        Assert.DoesNotContain(finished!, mission => mission.MissionCode == activeCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task RobotHistoryReturnsPreviousStateIntervals()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var robot = await db.Robots.SingleAsync(item => item.RobotId == "R-01");
            var now = DateTime.UtcNow;
            db.RobotStatusHistories.Add(new RobotStatusHistory
            {
                RobotId = robot.RobotId,
                RobotType = "LIFT",
                StatusCode = 3,
                StatusName = "Idle",
                Availability = "Available",
                ValidFrom = now.AddMinutes(-2),
                ValidTo = now.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        var history = await _client.GetFromJsonAsync<List<RobotHistoryResponse>>(
            "/api/robots/R-01/history");

        var previousState = Assert.Single(history!);
        Assert.Equal("Available", previousState.Availability);
        Assert.True(previousState.ValidTo > previousState.ValidFrom);
    }

    [Fact]
    public async Task DispatchRejectsUnavailableRobot()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var robot = await db.Robots.SingleAsync(item => item.RobotId == "R-01");
            robot.Availability = "Busy";
            await db.SaveChangesAsync();
        }

        var kuka = _factory.Services.GetRequiredService<IKukaFmClient>() as FakeKukaFmClient;
        var callCount = kuka!.DispatchCount;
        var response = await _client.PostAsJsonAsync("/api/missions", ValidDispatchRequest());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var robot = await db.Robots.SingleAsync(item => item.RobotId == "R-01");
            robot.Availability = "Available";
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(callCount, kuka.DispatchCount);
    }

    [Fact]
    public async Task KukaDispatchFailureReturns502WithoutPersistingMission()
    {
        using var factory = new ApiFactory();
        SeedRobot(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        var kuka = (FakeKukaFmClient)factory.Services.GetRequiredService<IKukaFmClient>();
        kuka.DispatchException = new HttpRequestException("KUKA rejected the node.");

        var response = await client.PostAsJsonAsync("/api/missions", ValidDispatchRequest());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var missions = await client.GetFromJsonAsync<List<MissionResponse>>("/api/missions");
        Assert.Empty(missions!);
    }

    [Fact]
    public async Task MissionAndRobotHistoryReturnNotFoundForUnknownIds()
    {
        var mission = await _client.GetAsync("/api/missions/missing/history");
        var robot = await _client.GetAsync("/api/robots/missing/history");

        Assert.Equal(HttpStatusCode.NotFound, mission.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, robot.StatusCode);
    }

    [Fact]
    public async Task RootAndHealthRoutesAreAvailable()
    {
        var response = await _client.GetAsync("/");
        var health = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("Dispatch RACK_MOVE", await response.Content.ReadAsStringAsync());
    }

    private async Task<string> CreateMissionAsync(short statusCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var missionCode = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var mission = new Mission
        {
            MissionCode = missionCode,
            RequestId = Guid.NewGuid().ToString("N"),
            RobotId = "R-01",
            MissionType = "RACK_MOVE",
            StartNode = "A-01",
            TargetNode = "A-02",
            ContainerCode = "BIN-01",
            Priority = 50,
            StatusCode = statusCode,
            StatusName = StatusMapping.MissionName(statusCode),
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = StatusMapping.IsFinishedMission(statusCode) ? now : null
        };
        mission.History.Add(new StatusHistory
        {
            StatusCode = statusCode,
            StatusName = mission.StatusName,
            ValidFrom = now
        });
        db.Missions.Add(mission);
        await db.SaveChangesAsync();
        return missionCode;
    }

    private static object ValidDispatchRequest() => new
    {
        robotId = "R-01",
        startNode = "A-01",
        targetNode = "A-02",
        containerCode = "BIN-01",
        priority = 50
    };

    private static void SeedRobot(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Robots.Any(robot => robot.RobotId == "R-01"))
        {
            db.Robots.Add(new Robot
            {
                RobotId = "R-01",
                StatusCode = 3,
                StatusName = StatusMapping.RobotName(3),
                Availability = "Available"
            });
            db.SaveChanges();
        }
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKey"] = "test-key",
                ["KukaFm:AuthorizationToken"] = "test-token"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            var databaseName = $"tests-{Guid.NewGuid()}";
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IKukaFmClient>();
            services.AddSingleton<IKukaFmClient, FakeKukaFmClient>();
        });
    }
}

public sealed class FakeKukaFmClient : IKukaFmClient
{
    public HttpRequestException? DispatchException { get; set; }
    public HttpRequestException? CancelException { get; set; }
    public int DispatchCount { get; private set; }
    public KukaSubmitMissionRequest? LastDispatch { get; private set; }
    public KukaCancelMissionRequest? LastCancel { get; private set; }

    public Task DispatchMissionAsync(KukaSubmitMissionRequest request, CancellationToken cancellationToken)
    {
        DispatchCount++;
        LastDispatch = request;
        return DispatchException is null ? Task.CompletedTask : Task.FromException(DispatchException);
    }

    public Task CancelMissionAsync(KukaCancelMissionRequest request, CancellationToken cancellationToken)
    {
        LastCancel = request;
        return CancelException is null ? Task.CompletedTask : Task.FromException(CancelException);
    }

    public Task<IReadOnlyList<KukaJobResponse>> QueryJobsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KukaJobResponse>>([]);

    public Task<KukaJobResponse?> QueryJobAsync(string missionCode, CancellationToken cancellationToken) =>
        Task.FromResult<KukaJobResponse?>(null);

    public Task<IReadOnlyList<KukaRobotResponse>> QueryRobotsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KukaRobotResponse>>([]);
}
