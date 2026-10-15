// File: Program.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-28
// Description: 
// License: MIT

using System.CommandLine;
using System.CommandLine.Invocation;
using ServiceList.Config;
using ServiceList.Helpers;
using ServiceList.Models;
using ServiceList.Services;
using Spectre.Console;

// ── Bootstrap ────────────────────────────────────────────────────────────────
Console.OutputEncoding = System.Text.Encoding.UTF8;

var appConfig   = ConfigLoader.Load();
var reader      = new ServiceReader();
var controller  = new ServiceController2();
var renderer    = new Renderer(appConfig);

// ── CLI definition ───────────────────────────────────────────────────────────

var root = new RootCommand("🖥  ServiceList (sl) — Windows Service Manager CLI");

// ── Helper: create option + register short alias in one call ─────────────────
static Option<T> Opt<T>(string longName, string shortAlias, string description)
{
    var o = new Option<T>(longName) { Description = description };
    o.AddAlias(shortAlias);
    return o;
}
static Option<T[]> OptArr<T>(string longName, string shortAlias, string description)
{
    var o = new Option<T[]>(longName) { Description = description, AllowMultipleArgumentsPerToken = true };
    o.AddAlias(shortAlias);
    return o;
}

// -- Filter options --
var filterOpt        = OptArr<string>("--filter",         "-f",  "Filter by keyword (name/PID/description/status/start)");
var filterStopOpt    = OptArr<string>("--filter-stop",    "-fs", "Filter and STOP matched services");
var filterStartOpt   = OptArr<string>("--filter-start",   "-fS", "Filter and START matched services");
var filterRestartOpt = OptArr<string>("--filter-restart", "-fr", "Filter and RESTART matched services");
var filterKillOpt    = OptArr<string>("--filter-kill",    "-fk", "Filter and KILL (force) matched services");

// -- Service control options --
var startOpt    = OptArr<string>("--start",   "-sa", "Start service(s) by name");
var stopOpt     = OptArr<string>("--stop",    "-st", "Stop service(s) by name");
var restartOpt  = OptArr<string>("--restart", "-R",  "Restart service(s) by name");
var pauseOpt    = OptArr<string>("--pause",   "-sp", "Pause service(s) by name");
var killOpt     = OptArr<string>("--kill",    "-k",  "Force-kill service(s) by name or PID");

// -- Display / sort options --
var sortByOpt      = Opt<string?>("--sort-by",      "-S",  "Sort by: name|displayname|pid|status|start|description");
var statusOnlyOpt  = Opt<string?>("--status-only",  "-ss", "Show only: running|stopped|paused|startpending|stoppending");
var startOnlyOpt   = Opt<string?>("--start-only",   "-cc", "Show only: auto|manual|disabled|boot|system");
var tailOpt        = Opt<int?>   ("--tail",          "-T",  "Show last N services");
var reverseOpt     = Opt<bool>   ("--reverse",       "-r",  "Reverse sort order");

// -- Render mode options --
var tableOpt     = Opt<bool>("--table", "-t", "Force table display mode");
var listOpt      = Opt<bool>("--list",  "-l", "Force detail-list display mode");
var fastOpt      = Opt<bool>("--fast",  "-z", "Fast compact one-liner mode");

// -- Config option --
var genConfigOpt = Opt<bool>("--gen-config", "-gc", "Generate sample sl-config.json in current directory");

root.AddOption(filterOpt);
root.AddOption(filterStopOpt);
root.AddOption(filterStartOpt);
root.AddOption(filterRestartOpt);
root.AddOption(filterKillOpt);
root.AddOption(startOpt);
root.AddOption(stopOpt);
root.AddOption(restartOpt);
root.AddOption(pauseOpt);
root.AddOption(killOpt);
root.AddOption(sortByOpt);
root.AddOption(statusOnlyOpt);
root.AddOption(startOnlyOpt);
root.AddOption(tailOpt);
root.AddOption(reverseOpt);
root.AddOption(tableOpt);
root.AddOption(listOpt);
root.AddOption(fastOpt);
root.AddOption(genConfigOpt);

// ── Handler ───────────────────────────────────────────────────────────────────

root.SetHandler((InvocationContext ctx) =>
{
    var filter        = ctx.ParseResult.GetValueForOption(filterOpt)        ?? [];
    var filterStop    = ctx.ParseResult.GetValueForOption(filterStopOpt)    ?? [];
    var filterStart   = ctx.ParseResult.GetValueForOption(filterStartOpt)   ?? [];
    var filterRestart = ctx.ParseResult.GetValueForOption(filterRestartOpt) ?? [];
    var filterKill    = ctx.ParseResult.GetValueForOption(filterKillOpt)    ?? [];

    var startNames    = ctx.ParseResult.GetValueForOption(startOpt)    ?? [];
    var stopNames     = ctx.ParseResult.GetValueForOption(stopOpt)     ?? [];
    var restartNames  = ctx.ParseResult.GetValueForOption(restartOpt)  ?? [];
    var pauseNames    = ctx.ParseResult.GetValueForOption(pauseOpt)    ?? [];
    var killNames     = ctx.ParseResult.GetValueForOption(killOpt)     ?? [];

    var sortBy        = ctx.ParseResult.GetValueForOption(sortByOpt);
    var statusOnly    = ctx.ParseResult.GetValueForOption(statusOnlyOpt);
    var startOnly     = ctx.ParseResult.GetValueForOption(startOnlyOpt);
    var tail          = ctx.ParseResult.GetValueForOption(tailOpt);
    var reverse       = ctx.ParseResult.GetValueForOption(reverseOpt);

    var forceTable    = ctx.ParseResult.GetValueForOption(tableOpt);
    var forceList     = ctx.ParseResult.GetValueForOption(listOpt);
    var forceFast     = ctx.ParseResult.GetValueForOption(fastOpt);
    var genConfig     = ctx.ParseResult.GetValueForOption(genConfigOpt);

    // ── generate-config shortcut ──────────────────────────────────────────
    if (genConfig)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "sl-config.json");
        ConfigLoader.WriteSampleConfig(path);
        AnsiConsole.MarkupLine($"[green]✅ Sample config written to:[/] [cyan]{path}[/]");
        return;
    }

    // ── Direct control commands (no listing needed) ────────────────────────
    if (startNames.Length > 0)
    {
        foreach (var n in startNames) controller.StartService(n);
        return;
    }
    if (stopNames.Length > 0)
    {
        foreach (var n in stopNames) controller.StopService(n);
        return;
    }
    if (restartNames.Length > 0)
    {
        foreach (var n in restartNames) controller.RestartService(n);
        return;
    }
    if (pauseNames.Length > 0)
    {
        foreach (var n in pauseNames) controller.PauseService(n);
        return;
    }

    // ── Load all services ─────────────────────────────────────────────────
    IEnumerable<ServiceInfo> services = [];

    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .Start("🔍 Loading services...", _ =>
        {
            services = reader.GetAllServices();
        });

    // ── Kill by name/PID (direct, no listing) ─────────────────────────────
    if (killNames.Length > 0)
    {
        var all = services.ToList();
        foreach (var token in killNames)
        {
            var matches = all.Where(s =>
                s.Name.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                s.Pid.ToString() == token);
            foreach (var svc in matches) controller.KillService(svc);
        }
        return;
    }

    // ── Apply filters ─────────────────────────────────────────────────────
    var activeFilter = filter
        .Concat(filterStop)
        .Concat(filterStart)
        .Concat(filterRestart)
        .Concat(filterKill)
        .ToArray();

    if (activeFilter.Length > 0)
        services = FilterHelper.Filter(services, activeFilter);

    if (!string.IsNullOrEmpty(statusOnly))
    {
        var s = FilterHelper.ParseStatus(statusOnly);
        if (s.HasValue) services = FilterHelper.FilterByStatus(services, s.Value);
    }

    if (!string.IsNullOrEmpty(startOnly))
    {
        var st = FilterHelper.ParseStartType(startOnly);
        if (st.HasValue) services = FilterHelper.FilterByStartType(services, st.Value);
    }

    // ── Sort & tail ───────────────────────────────────────────────────────
    services = FilterHelper.Sort(services, sortBy, reverse);
    services = FilterHelper.Tail(services, tail);

    // ── Perform action on filtered results ─────────────────────────────────
    var resultList = services.ToList();

    if (filterStop.Length > 0)
    {
        foreach (var svc in resultList) controller.StopService(svc.Name);
        return;
    }
    if (filterStart.Length > 0)
    {
        foreach (var svc in resultList) controller.StartService(svc.Name);
        return;
    }
    if (filterRestart.Length > 0)
    {
        foreach (var svc in resultList) controller.RestartService(svc.Name);
        return;
    }
    if (filterKill.Length > 0)
    {
        foreach (var svc in resultList) controller.KillService(svc);
        return;
    }

    // ── Determine render mode ─────────────────────────────────────────────
    var mode = RenderMode.Auto;
    if (forceTable) mode = RenderMode.Table;
    else if (forceList) mode = RenderMode.DetailList;
    else if (forceFast) mode = RenderMode.FastList;
    else if (appConfig.DefaultDisplay == "table") mode = RenderMode.Table;
    else if (appConfig.DefaultDisplay == "list")  mode = RenderMode.DetailList;
    else
    {
        // Auto: table when terminal is wide enough
        mode = Console.WindowWidth >= 131 ? RenderMode.Table : RenderMode.DetailList;
    }

    renderer.Render(resultList, mode);
});

return await root.InvokeAsync(args);
