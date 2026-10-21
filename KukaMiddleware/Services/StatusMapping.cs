namespace KukaMiddleware.Services;

public static class StatusMapping
{
    public static string MissionName(short status) => status switch
    {
        10 => "Created",
        20 => "Executing",
        25 => "Waiting",
        28 => "Cancelling",
        30 => "Completed",
        31 => "Cancelled",
        35 => "Manual completed",
        50 => "Warning",
        60 => "Startup error",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported KUKA mission status.")
    };

    public static bool IsActiveMission(short status) => status is 10 or 20 or 25 or 28 or 50;

    public static bool IsFinishedMission(short status) => status is 30 or 31 or 35 or 60;

    public static string RobotName(short status) => status switch
    {
        1 => "Departure",
        2 => "Offline",
        3 => "Idle",
        4 => "Executing",
        5 => "Charging",
        6 => "Updating",
        7 => "Abnormal",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported KUKA robot status.")
    };

    public static string RobotAvailability(short status) => status switch
    {
        3 => "Available",
        1 or 4 or 5 or 6 => "Busy",
        2 or 7 => "Offline",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported KUKA robot status.")
    };
}
