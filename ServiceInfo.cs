namespace ServiceList.Models;

public enum ServiceStatus
{
    Unknown         = 0,
    Stopped         = 1,
    StartPending    = 2,
    StopPending     = 3,
    Running         = 4,
    ContinuePending = 5,
    PausePending    = 6,
    Paused          = 7,
}

public enum StartType
{
    BootStart   = 0,
    SystemStart = 1,
    AutoStart   = 2,
    DemandStart = 3,
    Disabled    = 4,
    Unknown     = 99,
}

public class ServiceInfo
{
    public int      Number      { get; set; }
    public string   Name        { get; set; } = string.Empty;
    public string   DisplayName { get; set; } = string.Empty;
    public int      Pid         { get; set; }
    public ServiceStatus Status { get; set; }
    public StartType StartConfig { get; set; }
    public string   Description { get; set; } = string.Empty;
    public string[] Dependencies { get; set; } = [];
    public string   UserAccount { get; set; } = string.Empty;

    // ── Display helpers ──────────────────────────────────────────────────

    public string StatusLabel => Status switch
    {
        ServiceStatus.Stopped         => "⛔ STOPPED",
        ServiceStatus.Running         => "✅ RUNNING",
        ServiceStatus.StartPending    => "⏳ START PENDING",
        ServiceStatus.StopPending     => "⏳ STOP PENDING",
        ServiceStatus.Paused          => "⏸  PAUSED",
        ServiceStatus.PausePending    => "⏳ PAUSE PENDING",
        ServiceStatus.ContinuePending => "⏳ CONTINUE PENDING",
        _                             => "❓ UNKNOWN",
    };

    public string StartLabel => StartConfig switch
    {
        StartType.AutoStart   => "🔴 AUTO",
        StartType.DemandStart => "🟡 DEMAND",
        StartType.Disabled    => "🟢 DISABLED",
        StartType.BootStart   => "🟣 BOOT",
        StartType.SystemStart => "🔵 SYSTEM",
        _                     => "⚪ UNKNOWN",
    };
}
