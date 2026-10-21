namespace KukaMiddleware.Contracts;

public sealed record MissionResponse(
    string MissionCode,
    string RequestId,
    string RobotId,
    string MissionType,
    string StartNode,
    string TargetNode,
    string ContainerCode,
    short Priority,
    short StatusCode,
    string StatusName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public sealed record MissionHistoryResponse(
    short StatusCode,
    string StatusName,
    DateTime ValidFrom,
    DateTime? ValidTo);

public sealed record RobotResponse(
    string RobotId,
    string? RobotType,
    short StatusCode,
    string StatusName,
    string Availability,
    decimal? BatteryLevel,
    string? MapCode,
    string? NodeCode,
    string? MissionCode,
    DateTime UpdatedAt);

public sealed record RobotHistoryResponse(
    string? RobotType,
    short StatusCode,
    string StatusName,
    string Availability,
    decimal? BatteryLevel,
    string? MapCode,
    string? NodeCode,
    string? MissionCode,
    DateTime ValidFrom,
    DateTime ValidTo);
