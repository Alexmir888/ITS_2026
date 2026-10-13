using System.Text.Json;

namespace KukaMiddleware.Contracts;

public sealed record MissionDispatchRequest(
    string OrgId,
    string MissionType,
    JsonElement? MissionData = null,
    string? TemplateCode = null,
    string? RobotId = null,
    string? RobotType = null,
    string? ContainerCode = null,
    int Priority = 1);