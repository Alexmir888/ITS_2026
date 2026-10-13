using System.Net.Security;
using KukaMiddleware.Auth;
using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
using KukaMiddleware.Models;
using KukaMiddleware.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.Configure<KukaFmOptions>(builder.Configuration.GetSection(KukaFmOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient<IKukaFmClient, KukaFmClient>((serviceProvider, client) =>
{
    var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KukaFmOptions>>().Value;
    if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseAddress))
    {
        client.BaseAddress = baseAddress;
    }
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KukaFmOptions>>().Value;
    var handler = new HttpClientHandler();
    if (settings.AllowUntrustedCertificate)
    {
        var host = new Uri(settings.BaseUrl ?? "https://kukafm.its.fei").Host;
        handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            if (string.Equals(host, "kukafm.its.fei", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        };
    }
    return handler;
});
builder.Services.AddHostedService<MissionPollingWorker>();
builder.Services
    .AddAuthentication(ApiKeyHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    finally
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ApiAudit")
                .LogInformation("HTTP {Method} {Path} returned {StatusCode}",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode);
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/missions", async (AppDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Missions.AsNoTracking()
        .OrderByDescending(mission => mission.CreatedAt)
        .ToListAsync(cancellationToken)));

api.MapGet("/missions/{missionCode}/history", async (
    string missionCode, AppDbContext db, CancellationToken cancellationToken) =>
{
    var history = await db.StatusHistories.AsNoTracking()
        .Include(item => item.Mission)
        .Where(item => item.Mission.MissionCode == missionCode)
        .OrderBy(item => item.Timestamp)
        .Select(item => new { item.Status, item.Timestamp })
        .ToListAsync(cancellationToken);
    return history.Count == 0 ? Results.NotFound() : Results.Ok(history);
});

api.MapPost("/missions", async (
    MissionDispatchRequest request,
    AppDbContext db,
    IKukaFmClient kuka,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.RobotId) || string.IsNullOrWhiteSpace(request.MissionType))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["mission"] = ["robotId and missionType are required."]
        });
    }

    if (string.IsNullOrWhiteSpace(request.OrgId)
        || (request.MissionData is null && string.IsNullOrWhiteSpace(request.TemplateCode)))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["mission"] = ["orgId is required, and either missionData or templateCode must be supplied."]
        });
    }

    var missionCode = Guid.NewGuid().ToString("N");
    try
    {
        await kuka.DispatchMissionAsync(new KukaSubmitMissionRequest(
            request.OrgId,
            Guid.NewGuid().ToString("N"),
            missionCode,
            request.MissionType,
            string.IsNullOrWhiteSpace(request.RobotId) ? null : [request.RobotId],
            request.RobotType,
            request.Priority,
            request.ContainerCode,
            request.TemplateCode,
            request.MissionData), cancellationToken);
    }
    catch (HttpRequestException exception)
    {
        loggerFactory.CreateLogger("MissionApi").LogWarning(exception,
            "KUKA FM rejected mission for robot {RobotId}", request.RobotId);
        return Results.Problem(
            title: "KUKA Fleet Manager не принял миссию",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var mission = new Mission
    {
        MissionCode = missionCode,
        RobotId = request.RobotId,
        Status = "PENDING"
    };
    mission.History.Add(new StatusHistory { Status = mission.Status });
    db.Missions.Add(mission);
    await db.SaveChangesAsync(cancellationToken);
    loggerFactory.CreateLogger("MissionApi").LogInformation(
        "Mission {MissionCode} dispatched to robot {RobotId}", missionCode, request.RobotId);
    return Results.Created($"/api/missions/{Uri.EscapeDataString(missionCode)}", mission);
});

api.MapDelete("/missions/{missionCode}", async (
    string missionCode,
    AppDbContext db,
    IKukaFmClient kuka,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var mission = await db.Missions.Include(item => item.History)
        .SingleOrDefaultAsync(item => item.MissionCode == missionCode, cancellationToken);
    if (mission is null)
    {
        return Results.NotFound();
    }

    await kuka.CancelMissionAsync(new KukaCancelMissionRequest(
        Guid.NewGuid().ToString("N"), missionCode, "FORCE"), cancellationToken);
    if (!string.Equals(mission.Status, "CANCEL_REQUESTED", StringComparison.OrdinalIgnoreCase))
    {
        mission.Status = "CANCEL_REQUESTED";
        mission.History.Add(new StatusHistory { Status = mission.Status });
        await db.SaveChangesAsync(cancellationToken);
    }

    loggerFactory.CreateLogger("MissionApi").LogInformation("Cancellation requested for {MissionCode}", missionCode);
    return Results.Accepted($"/api/missions/{Uri.EscapeDataString(missionCode)}", mission);
});

api.MapGet("/robots", async (AppDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Robots.AsNoTracking().OrderBy(robot => robot.RobotId).ToListAsync(cancellationToken)));

app.Run();

public partial class Program { }