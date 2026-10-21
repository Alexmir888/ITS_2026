using System.Net.Security;
using KukaMiddleware.Auth;
using KukaMiddleware.Contracts;
using KukaMiddleware.Data;
using KukaMiddleware.Models;
using KukaMiddleware.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);
var seqUrl = builder.Configuration["Seq:ServerUrl"];
builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new JsonFormatter(renderMessage: true));
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        logger.WriteTo.Seq(seqUrl, apiKey: builder.Configuration["Seq:ApiKey"]);
    }
});
builder.Services.AddProblemDetails();
builder.Services.Configure<KukaFmOptions>(builder.Configuration.GetSection(KukaFmOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient<IKukaFmClient, KukaFmClient>((serviceProvider, client) =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<KukaFmOptions>>().Value;
    if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseAddress))
    {
        client.BaseAddress = baseAddress;
    }

    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<KukaFmOptions>>().Value;
    var handler = new HttpClientHandler();
    if (settings.AllowUntrustedCertificate)
    {
        var host = new Uri(settings.BaseUrl ?? "https://kukafm.its.fei").Host;
        handler.ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) =>
            sslPolicyErrors == SslPolicyErrors.None
            || string.Equals(host, "kukafm.its.fei", StringComparison.OrdinalIgnoreCase);
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
    if (db.Database.IsRelational())
    {
        await LegacyDatabaseUpgrade.UpgradeIfNeededAsync(db);
        await db.Database.MigrateAsync();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/missions", async (
    string? state,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var missions = db.Missions.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(state))
    {
        if (string.Equals(state, "active", StringComparison.OrdinalIgnoreCase))
        {
            missions = missions.Where(mission => mission.StatusCode == 10
                || mission.StatusCode == 20
                || mission.StatusCode == 25
                || mission.StatusCode == 28
                || mission.StatusCode == 50);
        }
        else if (string.Equals(state, "finished", StringComparison.OrdinalIgnoreCase))
        {
            missions = missions.Where(mission => mission.StatusCode != 10
                && mission.StatusCode != 20
                && mission.StatusCode != 25
                && mission.StatusCode != 28
                && mission.StatusCode != 50);
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["state"] = ["Allowed values are 'active' and 'finished'."]
            });
        }
    }

    var result = await missions.OrderByDescending(mission => mission.CreatedAt)
        .Select(mission => new MissionResponse(
            mission.MissionCode,
            mission.RequestId,
            mission.RobotId,
            mission.MissionType,
            mission.StartNode,
            mission.TargetNode,
            mission.ContainerCode,
            mission.Priority,
            mission.StatusCode,
            mission.StatusName,
            mission.CreatedAt,
            mission.UpdatedAt,
            mission.CompletedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(result);
});

api.MapGet("/missions/{missionCode}/history", async (
    string missionCode,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var exists = await db.Missions.AsNoTracking()
        .AnyAsync(mission => mission.MissionCode == missionCode, cancellationToken);
    if (!exists)
    {
        return Results.NotFound();
    }

    var history = await db.StatusHistories.AsNoTracking()
        .Where(item => item.Mission.MissionCode == missionCode)
        .OrderByDescending(item => item.ValidFrom)
        .Select(item => new MissionHistoryResponse(
            item.StatusCode, item.StatusName, item.ValidFrom, item.ValidTo))
        .ToListAsync(cancellationToken);
    return Results.Ok(history);
});

api.MapPost("/missions", async (
    MissionDispatchRequest request,
    AppDbContext db,
    IKukaFmClient kuka,
    IOptions<KukaFmOptions> options,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var errors = ValidateDispatchRequest(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var robotId = request.RobotId!.Trim();
    var robot = await db.Robots.AsNoTracking()
        .SingleOrDefaultAsync(item => item.RobotId == robotId, cancellationToken);
    if (robot is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["robotId"] = ["The selected robot does not exist."]
        });
    }

    if (!string.Equals(robot.Availability, "Available", StringComparison.Ordinal))
    {
        return Results.Conflict(new ProblemDetails
        {
            Title = "Robot is not available",
            Detail = $"Robot '{robotId}' is currently {robot.Availability.ToLowerInvariant()}."
        });
    }

    var missionCode = Guid.NewGuid().ToString("N");
    var requestId = Guid.NewGuid().ToString("N");
    var dispatch = new KukaSubmitMissionRequest(
        options.Value.OrgId,
        requestId,
        missionCode,
        "RACK_MOVE",
        [robotId],
        request.Priority,
        request.ContainerCode!.Trim(),
        [
            new KukaMissionDataPoint(1, request.StartNode!.Trim(), "NODE_POINT", "UP_CONTAINER"),
            new KukaMissionDataPoint(2, request.TargetNode!.Trim(), "NODE_POINT", "DOWN_CONTAINER")
        ]);

    try
    {
        await kuka.DispatchMissionAsync(dispatch, cancellationToken);
    }
    catch (HttpRequestException exception)
    {
        loggerFactory.CreateLogger("MissionApi").LogWarning(exception,
            "KUKA FM rejected mission for robot {RobotId}", robotId);
        return Results.Problem(
            title: "KUKA Fleet Manager did not accept the mission.",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException exception)
    {
        loggerFactory.CreateLogger("MissionApi").LogError(exception,
            "KUKA FM dispatch configuration is invalid.");
        return Results.Problem(
            title: "KUKA Fleet Manager is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var now = DateTime.UtcNow;
    var mission = new Mission
    {
        MissionCode = missionCode,
        RequestId = requestId,
        RobotId = robotId,
        MissionType = "RACK_MOVE",
        StartNode = request.StartNode!.Trim(),
        TargetNode = request.TargetNode!.Trim(),
        ContainerCode = request.ContainerCode!.Trim(),
        Priority = (short)request.Priority,
        StatusCode = 10,
        StatusName = StatusMapping.MissionName(10),
        CreatedAt = now,
        UpdatedAt = now
    };
    mission.History.Add(new StatusHistory
    {
        StatusCode = mission.StatusCode,
        StatusName = mission.StatusName,
        ValidFrom = now
    });
    db.Missions.Add(mission);
    await db.SaveChangesAsync(cancellationToken);
    loggerFactory.CreateLogger("MissionApi").LogInformation(
        "Mission {MissionCode} dispatched to robot {RobotId}", missionCode, robotId);
    return Results.Created(
        $"/api/missions/{Uri.EscapeDataString(missionCode)}",
        ToMissionResponse(mission));
});

api.MapPost("/missions/{missionCode}/cancel", CancelMissionAsync);
api.MapDelete("/missions/{missionCode}", CancelMissionAsync);

api.MapGet("/robots", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var robots = await db.Robots.AsNoTracking()
        .OrderBy(robot => robot.RobotId)
        .Select(robot => new RobotResponse(
            robot.RobotId,
            robot.RobotType,
            robot.StatusCode,
            robot.StatusName,
            robot.Availability,
            robot.BatteryLevel,
            robot.MapCode,
            robot.NodeCode,
            robot.MissionCode,
            robot.UpdatedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(robots);
});

api.MapGet("/robots/{robotId}/history", async (
    string robotId,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var exists = await db.Robots.AsNoTracking()
        .AnyAsync(robot => robot.RobotId == robotId, cancellationToken);
    if (!exists)
    {
        return Results.NotFound();
    }

    var history = await db.RobotStatusHistories.AsNoTracking()
        .Where(item => item.RobotId == robotId)
        .OrderByDescending(item => item.ValidTo)
        .Select(item => new RobotHistoryResponse(
            item.RobotType,
            item.StatusCode,
            item.StatusName,
            item.Availability,
            item.BatteryLevel,
            item.MapCode,
            item.NodeCode,
            item.MissionCode,
            item.ValidFrom,
            item.ValidTo))
        .ToListAsync(cancellationToken);
    return Results.Ok(history);
});

app.Run();

static Dictionary<string, string[]> ValidateDispatchRequest(MissionDispatchRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.RobotId))
    {
        errors["robotId"] = ["A robot is required."];
    }

    if (string.IsNullOrWhiteSpace(request.StartNode))
    {
        errors["startNode"] = ["A start node is required."];
    }

    if (string.IsNullOrWhiteSpace(request.TargetNode))
    {
        errors["targetNode"] = ["A target node is required."];
    }

    if (string.IsNullOrWhiteSpace(request.ContainerCode))
    {
        errors["containerCode"] = ["A container code is required."];
    }

    if (request.RobotId is { } robotId && robotId.Trim().Length > 120)
    {
        errors["robotId"] = ["Robot ID must be at most 120 characters."];
    }

    if (request.StartNode is { } startNode && startNode.Trim().Length > 64)
    {
        errors["startNode"] = ["Start node must be at most 64 characters."];
    }

    if (request.TargetNode is { } targetNode && targetNode.Trim().Length > 64)
    {
        errors["targetNode"] = ["Target node must be at most 64 characters."];
    }

    if (request.ContainerCode is { } containerCode && containerCode.Trim().Length > 64)
    {
        errors["containerCode"] = ["Container code must be at most 64 characters."];
    }

    if (request.Priority is < 1 or > 99)
    {
        errors["priority"] = ["Priority must be between 1 and 99."];
    }

    return errors;
}

static MissionResponse ToMissionResponse(Mission mission) => new(
    mission.MissionCode,
    mission.RequestId,
    mission.RobotId,
    mission.MissionType,
    mission.StartNode,
    mission.TargetNode,
    mission.ContainerCode,
    mission.Priority,
    mission.StatusCode,
    mission.StatusName,
    mission.CreatedAt,
    mission.UpdatedAt,
    mission.CompletedAt);

static async Task<IResult> CancelMissionAsync(
    string missionCode,
    AppDbContext db,
    IKukaFmClient kuka,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken)
{
    var mission = await db.Missions.SingleOrDefaultAsync(
        item => item.MissionCode == missionCode, cancellationToken);
    if (mission is null)
    {
        return Results.NotFound();
    }

    if (!StatusMapping.IsActiveMission(mission.StatusCode))
    {
        return Results.Conflict(new ProblemDetails
        {
            Title = "Mission is not active.",
            Detail = $"Mission '{missionCode}' is already {mission.StatusName.ToLowerInvariant()}."
        });
    }

    try
    {
        await kuka.CancelMissionAsync(new KukaCancelMissionRequest(
            Guid.NewGuid().ToString("N"), missionCode, "FORCE"), cancellationToken);
    }
    catch (HttpRequestException exception)
    {
        loggerFactory.CreateLogger("MissionApi").LogWarning(exception,
            "KUKA FM rejected cancellation for mission {MissionCode}", missionCode);
        return Results.Problem(
            title: "KUKA Fleet Manager did not accept the cancellation.",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException exception)
    {
        loggerFactory.CreateLogger("MissionApi").LogError(exception,
            "KUKA FM cancellation configuration is invalid.");
        return Results.Problem(
            title: "KUKA Fleet Manager is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    loggerFactory.CreateLogger("MissionApi").LogInformation(
        "Force cancellation requested for {MissionCode}", missionCode);
    return Results.Ok(ToMissionResponse(mission));
}

public partial class Program { }
