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
        using var missionData = JsonDocument.Parse("[{\"sequence\":1,\"position\":\"A-02\",\"type\":\"NODE_POINT\"}]");

        await client.DispatchMissionAsync(new KukaSubmitMissionRequest(
            "UNIVERSAL", "request-1", "mission-1", "MOVE", ["R-01"], null, 1,
            null, null, missionData.RootElement), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/interfaces/api/amr/submitMission", handler.Path);
        Assert.Equal("access-token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("UNIVERSAL", json.RootElement.GetProperty("orgId").GetString());
        Assert.Equal("mission-1", json.RootElement.GetProperty("missionCode").GetString());
        Assert.False(json.RootElement.TryGetProperty("robotType", out _));
    }

    [Fact]
    public async Task JobQueryPostsAndReadsKukaDataEnvelopeAndNumericStatus()
    {
        var handler = new RecordingHandler("""
            {"data":[{"jobCode":"mission-1","robotId":"R-01","status":20}],"code":"0","message":null,"success":true}
            """);
        using var httpClient = CreateHttpClient(handler);
        var jobs = await CreateClient(httpClient).QueryJobsAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/interfaces/api/amr/jobQuery", handler.Path);
        Assert.Equal("mission-1", Assert.Single(jobs).MissionCode);
        Assert.Equal(20, jobs[0].Status);
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