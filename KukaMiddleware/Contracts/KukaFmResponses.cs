using System.Text.Json.Serialization;

namespace KukaMiddleware.Contracts;

public sealed record KukaApiResponse<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("success")] bool Success);

public sealed record KukaJobResponse(
    [property: JsonPropertyName("jobCode")] string MissionCode,
    [property: JsonPropertyName("robotId")] string RobotId,
    [property: JsonPropertyName("status")] short Status);

public sealed record KukaRobotResponse(
    [property: JsonPropertyName("robotId")] string RobotId,
    [property: JsonPropertyName("robotType")] string? RobotType,
    [property: JsonPropertyName("status")] short Status,
    [property: JsonPropertyName("batteryLevel")] decimal? BatteryLevel,
    [property: JsonPropertyName("mapCode")] string? MapCode,
    [property: JsonPropertyName("nodeCode")] string? NodeCode,
    [property: JsonPropertyName("missionCode")] string? MissionCode);

public sealed record KukaSubmitMissionRequest(
    string OrgId,
    string RequestId,
    string MissionCode,
    string MissionType,
    string[] RobotIds,
    int Priority,
    string ContainerCode,
    KukaMissionDataPoint[] MissionData);

public sealed record KukaMissionDataPoint(
    int Sequence,
    string Position,
    string Type,
    string ActionType);

public sealed record KukaCancelMissionRequest(
    string RequestId,
    string MissionCode,
    string CancelMode);