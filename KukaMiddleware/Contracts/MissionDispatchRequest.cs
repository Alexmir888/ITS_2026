namespace KukaMiddleware.Contracts;

public sealed record MissionDispatchRequest(
    string? RobotId,
    string? StartNode,
    string? TargetNode,
    string? ContainerCode,
    int Priority);