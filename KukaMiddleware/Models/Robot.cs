namespace KukaMiddleware.Models;

public sealed class Robot
{
    public string RobotId { get; set; } = string.Empty;
    public string Status { get; set; } = "OFFLINE";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}