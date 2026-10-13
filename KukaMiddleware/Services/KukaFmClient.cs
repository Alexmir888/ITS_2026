using System.Net.Http.Json;
using System.Text.Json;
using KukaMiddleware.Contracts;
using Microsoft.Extensions.Options;

namespace KukaMiddleware.Services;

public sealed class KukaFmClient(
    HttpClient httpClient,
    IOptions<KukaFmOptions> options,
    ILogger<KukaFmClient> logger) : IKukaFmClient
{
    private readonly KukaFmOptions _options = options.Value;

    public async Task DispatchMissionAsync(
        KukaSubmitMissionRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement?>(HttpMethod.Post, _options.DispatchPath, request, cancellationToken);
    }

    public async Task CancelMissionAsync(KukaCancelMissionRequest request, CancellationToken cancellationToken)
    {
        await SendAsync<JsonElement?>(HttpMethod.Post, _options.CancelPath, request, cancellationToken);
    }

    public async Task<IReadOnlyList<KukaJobResponse>> QueryJobsAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<IReadOnlyList<KukaJobResponse>>(
            HttpMethod.Post, _options.JobsPath, new { limit = _options.JobsLimit }, cancellationToken);
        return result.Data ?? [];
    }

    public async Task<IReadOnlyList<KukaRobotResponse>> QueryRobotsAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<IReadOnlyList<KukaRobotResponse>>(
            HttpMethod.Post, _options.RobotsPath, new { }, cancellationToken);
        return result.Data ?? [];
    }

    private async Task<KukaApiResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AuthorizationToken))
        {
            throw new InvalidOperationException("KukaFm:AuthorizationToken is not configured.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", _options.AuthorizationToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("KUKA FM request {Method} {Path} failed with {StatusCode}",
                method, path, (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<KukaApiResponse<T>>(cancellationToken);
        var isSuccessful = result is not null && result.Success &&
            (string.IsNullOrWhiteSpace(result.Code) || string.Equals(result.Code, "0", StringComparison.Ordinal));

        if (!isSuccessful)
        {
            throw new HttpRequestException(
                $"KUKA FM returned an unsuccessful response (code: {result?.Code ?? "missing"}, message: {result?.Message ?? "none"}).");
        }

        return result;
    }
}