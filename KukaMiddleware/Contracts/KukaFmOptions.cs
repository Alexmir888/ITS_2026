namespace KukaMiddleware.Contracts;

public sealed class KukaFmOptions
{
    public const string SectionName = "KukaFm";

    public string? BaseUrl { get; set; }
    public string? AuthorizationToken { get; set; }
    public string OrgId { get; set; } = "UNIVERSAL";
    public bool AllowUntrustedCertificate { get; set; }
    public string DispatchPath { get; set; } = "/interfaces/api/amr/submitMission";
    public string CancelPath { get; set; } = "/interfaces/api/amr/missionCancel";
    public string JobsPath { get; set; } = "/interfaces/api/amr/jobQuery";
    public string RobotsPath { get; set; } = "/interfaces/api/amr/robotQuery";
    public int PollIntervalSeconds { get; set; } = 10;
}