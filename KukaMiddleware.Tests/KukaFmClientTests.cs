using System.Net;
using System.Text.Json;
using KukaMiddleware.Contracts;
using KukaMiddleware.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KukaMiddleware.Tests;

public sealed class KukaFmClientTests
{
    [Fact]
    public async Task DispatchUsesDocumentedEndpointAndRawAuthorizationToken()
    {
        var handler = new RecordingHandler("""
            {"data":null,"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await client.DispatchMissionAsync(new KukaSubmitMissionRequest(
            "UNIVERSAL",
            "request-1",
            "mission-1",
            "RACK_MOVE",
            ["R-01"],
            50,
            "BIN-01",
            [
                new KukaMissionDataPoint(1, "A-01", "NODE_POINT", "UP_CONTAINER"),
                new KukaMissionDataPoint(2, "A-02", "NODE_POINT", "DOWN_CONTAINER")
            ]), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/interfaces/api/amr/submitMission", handler.Path);
        Assert.Equal("access-token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("RACK_MOVE", json.RootElement.GetProperty("missionType").GetString());
        Assert.Equal("BIN-01", json.RootElement.GetProperty("containerCode").GetString());
        Assert.Equal(50, json.RootElement.GetProperty("priority").GetInt32());
        var steps = json.RootElement.GetProperty("missionData");
        Assert.Equal("A-01", steps[0].GetProperty("position").GetString());
        Assert.Equal("UP_CONTAINER", steps[0].GetProperty("actionType").GetString());
        Assert.Equal("A-02", steps[1].GetProperty("position").GetString());
        Assert.Equal("DOWN_CONTAINER", steps[1].GetProperty("actionType").GetString());
    }

    [Fact]
    public async Task JobQueryPostsAndReadsDataEnvelopeAndNumericStatus()
    {
        var handler = new RecordingHandler("""
            {"data":[{"jobCode":"mission-1","robotId":"R-01","status":20}],"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);
        var jobs = await CreateClient(httpClient).QueryJobsAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/interfaces/api/amr/jobQuery", handler.Path);
        Assert.Equal("mission-1", Assert.Single(jobs).MissionCode);
        Assert.Equal((short)20, jobs[0].Status);
    }

    [Fact]
    public async Task SpecificJobQueryReadsJobFromDataArray()
    {
        var handler = new RecordingHandler("""
            {"data":[{"jobCode":"mission-1","robotId":"R-01","status":31}],"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);
        var job = await CreateClient(httpClient).QueryJobAsync("mission-1", CancellationToken.None);

        Assert.Equal("/interfaces/api/amr/jobQuery", handler.Path);
        Assert.Equal("mission-1", Assert.IsType<KukaJobResponse>(job).MissionCode);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("mission-1", body.RootElement.GetProperty("jobCode").GetString());
    }

    [Fact]
    public async Task CancelUsesMissionCancelEndpointAndForceMode()
    {
        var handler = new RecordingHandler("""
            {"data":null,"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);

        await CreateClient(httpClient).CancelMissionAsync(
            new KukaCancelMissionRequest("request-1", "mission-1", "FORCE"),
            CancellationToken.None);

        Assert.Equal("/interfaces/api/amr/missionCancel", handler.Path);
        Assert.Equal("access-token", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("mission-1", body.RootElement.GetProperty("missionCode").GetString());
        Assert.Equal("FORCE", body.RootElement.GetProperty("cancelMode").GetString());
    }

    [Fact]
    public async Task RobotQueryReadsRobotDetails()
    {
        var handler = new RecordingHandler("""
            {"data":[{"robotId":"R-01","robotType":"LIFT","status":3,"batteryLevel":87.5,"mapCode":"M1","nodeCode":"A-01","missionCode":null}],"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);
        var robots = await CreateClient(httpClient).QueryRobotsAsync(CancellationToken.None);
        var robot = Assert.Single(robots);

        Assert.Equal("R-01", robot.RobotId);
        Assert.Equal((short)3, robot.Status);
        Assert.Equal(87.5m, robot.BatteryLevel);
    }

    private static HttpClient CreateHttpClient(RecordingHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://kuka.test") };

    private static KukaFmClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        Options.Create(new KukaFmOptions { AuthorizationToken = "access-token" }),
        NullLogger<KukaFmClient>.Instance);

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.GetValues("Authorization").Single();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
