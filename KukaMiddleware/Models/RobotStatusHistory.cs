namespace KukaMiddleware.Models;

public sealed class RobotStatusHistory
{
    public int Id { get; set; }
    public string RobotId { get; set; } = string.Empty;
    public Robot Robot { get; set; } = null!;
    public string? RobotType { get; set; }
    public short StatusCode { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string Availability { get; set; } = string.Empty;
    public decimal? BatteryLevel { get; set; }
    public string? MapCode { get; set; }
    public string? NodeCode { get; set; }
    public string? MissionCode { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
}
