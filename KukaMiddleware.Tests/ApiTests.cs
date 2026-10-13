using System.Net;
using System.Net.Http.Json;
using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
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
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
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
    public async Task DispatchStoresMissionAndInitialHistory()
    {
        var response = await _client.PostAsJsonAsync("/api/missions", new
        {
            orgId = "UNIVERSAL",
            robotId = "R-01",
            missionType = "MOVE",
            missionData = new[]
            {
                new { sequence = 1, position = "A-02", type = "NODE_POINT" }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mission = await response.Content.ReadFromJsonAsync<MissionView>();
        Assert.NotNull(mission);
        var missions = await _client.GetFromJsonAsync<List<MissionView>>("/api/missions");
        Assert.Contains(missions!, item => item.MissionCode == mission.MissionCode);
        var history = await _client.GetAsync($"/api/missions/{mission.MissionCode}/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var entries = await history.Content.ReadFromJsonAsync<List<StatusHistoryView>>();
        Assert.NotNull(entries);
        Assert.Single(entries);
        Assert.Equal("PENDING", entries[0].Status);
    }

    [Fact]
    public async Task DispatchReturnsKukaRejectionDetails()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        ((FakeKukaFmClient)factory.Services.GetRequiredService<IKukaFmClient>()).DispatchException =
            new HttpRequestException("No such node in the graph.[bad-node]");

        var response = await client.PostAsJsonAsync("/api/missions", new
        {
            orgId = "UNIVERSAL",
            robotId = "3",
            missionType = "MOVE",
            missionData = new[] { new { sequence = 1, position = "bad-node", type = "NODE_POINT" } }
        });
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("No such node in the graph.[bad-node]", problem?.Detail);
    }

    [Fact]
    public async Task RootPageServesUiHtml()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("KUKA Fleet Manager", content);
        Assert.Contains("Новая миссия", content);
    }

    private sealed record StatusHistoryView(string Status, DateTime Timestamp);
    private sealed record MissionView(string MissionCode);
    private sealed record ProblemView(string? Detail);
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKey"] = "test-key"
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

    public Task DispatchMissionAsync(KukaSubmitMissionRequest request, CancellationToken cancellationToken) =>
        DispatchException is null ? Task.CompletedTask : Task.FromException(DispatchException);

    public Task CancelMissionAsync(KukaCancelMissionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<KukaJobResponse>> QueryJobsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KukaJobResponse>>([]);

    public Task<IReadOnlyList<KukaRobotResponse>> QueryRobotsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KukaRobotResponse>>([]);
}