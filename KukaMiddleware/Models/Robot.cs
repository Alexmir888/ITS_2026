namespace KukaMiddleware.Models;

public sealed class Robot
{
    public string RobotId { get; set; } = string.Empty;
    public string? RobotType { get; set; }
    public short StatusCode { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string Availability { get; set; } = "Offline";
    public decimal? BatteryLevel { get; set; }
    public string? MapCode { get; set; }
    public string? NodeCode { get; set; }
    public string? MissionCode { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<RobotStatusHistory> History { get; set; } = [];
}