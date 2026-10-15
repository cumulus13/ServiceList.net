namespace ServiceList.Config;

/// <summary>
/// Color theme configuration - loaded from sl-config.json or uses defaults.
/// </summary>
public class ColorTheme
{
    // Service status colors
    public string StatusRunning       { get; set; } = "bold cyan";
    public string StatusStopped       { get; set; } = "bold red";
    public string StatusPending       { get; set; } = "bold magenta";
    public string StatusPaused        { get; set; } = "bold blue";
    public string StatusUnknown       { get; set; } = "bold grey";

    // Start config colors
    public string StartAuto           { get; set; } = "bold cyan";
    public string StartDemand         { get; set; } = "bold magenta";
    public string StartDisabled       { get; set; } = "bold red";
    public string StartBoot           { get; set; } = "bold blue";
    public string StartSystem         { get; set; } = "bold green";
    public string StartOther          { get; set; } = "bold grey";

    // Field label colors
    public string LabelColor          { get; set; } = "grey";
    public string DependColor         { get; set; } = "magenta";
    public string NameColor           { get; set; } = "bold cyan";
    public string DisplayNameColor    { get; set; } = "bold cyan";
    public string PidColor            { get; set; } = "bold yellow";
    public string DescriptionColor    { get; set; } = "green";

    // Table colors
    public string TableBorder         { get; set; } = "grey";
    public string TableHeader         { get; set; } = "bold white";
    public string NumberColor         { get; set; } = "bold white";

    // Fast-list inline colors
    public string FastNameColor       { get; set; } = "bold cyan";
    public string FastDisplayColor    { get; set; } = "bold yellow";
    public string FastPidColor        { get; set; } = "bold green";
    public string FastStatusColor     { get; set; } = "bold magenta";
    public string FastStartColor      { get; set; } = "bold red";
}

public class AppConfig
{
    public ColorTheme Colors { get; set; } = new ColorTheme();

    /// <summary>Table display: auto | list | table</summary>
    public string DefaultDisplay { get; set; } = "auto";

    /// <summary>Terminal width override (0 = auto-detect)</summary>
    public int TerminalWidthOverride { get; set; } = 0;
}
