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
    [property: JsonPropertyName("status")] int Status);

public sealed record KukaRobotResponse(
    [property: JsonPropertyName("robotId")] string RobotId,
    [property: JsonPropertyName("status")] int Status);

public sealed record KukaSubmitMissionRequest(
    string OrgId,
    string RequestId,
    string MissionCode,
    string MissionType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string[]? RobotIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RobotType,
    int Priority,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContainerCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TemplateCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    System.Text.Json.JsonElement? MissionData);

public sealed record KukaCancelMissionRequest(
    string RequestId,
    string MissionCode,
    string CancelMode);