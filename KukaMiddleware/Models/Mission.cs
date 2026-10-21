namespace KukaMiddleware.Models;

public sealed class Mission
{
    public int Id { get; set; }
    public string MissionCode { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string RobotId { get; set; } = string.Empty;
    public Robot Robot { get; set; } = null!;
    public string MissionType { get; set; } = "RACK_MOVE";
    public string StartNode { get; set; } = string.Empty;
    public string TargetNode { get; set; } = string.Empty;
    public string ContainerCode { get; set; } = string.Empty;
    public short Priority { get; set; }
    public short StatusCode { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<StatusHistory> History { get; set; } = [];
}
