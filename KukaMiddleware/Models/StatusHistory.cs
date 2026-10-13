using System.Text.Json.Serialization;

namespace KukaMiddleware.Models;

public sealed class StatusHistory
{
    public int Id { get; set; }
    public int MissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonIgnore]
    public Mission Mission { get; set; } = null!;
}