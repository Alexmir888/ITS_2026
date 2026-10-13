using KukaMiddleware.Contracts;

namespace KukaMiddleware.Services;

public interface IKukaFmClient
{
    Task DispatchMissionAsync(KukaSubmitMissionRequest request, CancellationToken cancellationToken);
    Task CancelMissionAsync(KukaCancelMissionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<KukaJobResponse>> QueryJobsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<KukaRobotResponse>> QueryRobotsAsync(CancellationToken cancellationToken);
}