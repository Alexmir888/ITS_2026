namespace KukaMiddleware.Models;

public sealed class StatusHistory
{
    public int Id { get; set; }
    public int MissionId { get; set; }
    public Mission Mission { get; set; } = null!;
    public short StatusCode { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidTo { get; set; }
}
