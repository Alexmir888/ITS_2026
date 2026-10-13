namespace KukaMiddleware.Models;

public sealed class Mission
{
    public int Id { get; set; }
    public string MissionCode { get; set; } = string.Empty;
    public string RobotId { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<StatusHistory> History { get; set; } = [];
}