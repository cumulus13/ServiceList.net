// File: Program.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-28
// Description: sl — Windows service manager CLI (option definitions + dispatch).
// License: MIT

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using ServiceList.Cli;
using ServiceList.Config;
using ServiceList.Helpers;
using Spectre.Console;

// ── Bootstrap ────────────────────────────────────────────────────────────────

Console.OutputEncoding = System.Text.Encoding.UTF8;

var appConfig = ConfigLoader.Load();

// ── CLI definition ───────────────────────────────────────────────────────────

var root = new RootCommand("🖥 ServiceList (sl) — Windows Service Manager CLI");

static Option<T> Opt<T>(string longName, string shortAlias, string description)
{
    var o = new Option<T>(longName) { Description = description };
    o.AddAlias(shortAlias);
    return o;
}

// Array options accept ZERO or more values (beta4 default for collection types).
// An action flag given WITHOUT names (`-f spooler -st`) acts on the filtered services.
static Option<T[]> OptArr<T>(string longName, string shortAlias, string description)
{
    var o = new Option<T[]>(longName) { Description = description, AllowMultipleArgumentsPerToken = true };
    o.AddAlias(shortAlias);
    return o;
}

// -- Selection --
var filterOpt = OptArr<string>("--filter", "-f", "Filter by keyword (name/display/PID, plus description/status when listing). Supports * and ?");
var statusOnlyOpt = Opt<string?>("--status-only", "-ss", "Select only: running|stopped|paused|startpending|stoppending");
var startOnlyOpt = Opt<string?>("--start-only", "-cc", "Select only: auto|manual|disabled|boot|system");
var exactOpt = Opt<bool>("--exact", "-e", "Filter: exact (case-insensitive) match instead of substring");
var regexOpt = Opt<bool>("--regex", "-x", "Filter: treat keywords as regular expressions");

// -- Actions (names optional: without names they apply to the selection) --
var startOpt = OptArr<string>("--start", "-sa", "Start service(s): by name, or the filtered selection if no name is given");
var stopOpt = OptArr<string>("--stop", "-st", "Stop service(s): by name, or the filtered selection if no name is given");
var restartOpt = OptArr<string>("--restart", "-R", "Restart service(s): by name, or the filtered selection");
var pauseOpt = OptArr<string>("--pause", "-sp", "Pause service(s): by name, or the filtered selection");
var killOpt = OptArr<string>("--kill", "-k", "Force-kill service process(es): by name/PID, or the filtered selection");

// -- Legacy "filter + action" aliases (keywords are the filter) --
var filterStopOpt = OptArr<string>("--filter-stop", "-fs", "Filter and STOP matched services");
var filterStartOpt = OptArr<string>("--filter-start", "-fS", "Filter and START matched services");
var filterRestartOpt = OptArr<string>("--filter-restart", "-fr", "Filter and RESTART matched services");
var filterKillOpt = OptArr<string>("--filter-kill", "-fk", "Filter and KILL (force) matched services");

// -- Action modifiers --
var yesOpt = Opt<bool>("--yes", "-y", "Do not ask for confirmation when acting on several services");
var dryRunOpt = Opt<bool>("--dry-run", "-n", "Show what would happen without changing anything");
var depsOpt = Opt<bool>("--deps", "-D", "Stop/restart: include running dependent services");
var forceOpt = Opt<bool>("--force", "-F", "Kill: allow killing a process shared with other services");
var timeoutOpt = Opt<int?>("--timeout", "-to", "Seconds to wait for a state change (default 30)");

// -- Display / sort --
var sortByOpt = Opt<string?>("--sort-by", "-S", "Sort by: name|displayname|pid|status|start|description");
var tailOpt = Opt<int?>("--tail", "-T", "Show last N services");
var reverseOpt = Opt<bool>("--reverse", "-r", "Reverse sort order");
var tableOpt = Opt<bool>("--table", "-t", "Force table display mode");
var listOpt = Opt<bool>("--list", "-l", "Force detail-list display mode");
var fastOpt = Opt<bool>("--fast", "-z", "Fast compact one-liner mode");
var jsonOpt = Opt<bool>("--json", "-j", "Machine-readable JSON output");

// -- Config --
var genConfigOpt = Opt<bool>("--gen-config", "-gc", "Generate sample sl-config.json in current directory");

foreach (var option in new Option[]
{
    filterOpt, statusOnlyOpt, startOnlyOpt, exactOpt, regexOpt,
    startOpt, stopOpt, restartOpt, pauseOpt, killOpt,
    filterStopOpt, filterStartOpt, filterRestartOpt, filterKillOpt,
    yesOpt, dryRunOpt, depsOpt, forceOpt, timeoutOpt,
    sortByOpt, tailOpt, reverseOpt, tableOpt, listOpt, fastOpt, jsonOpt,
    genConfigOpt,
})
{
    root.AddOption(option);
}

// ── Handler ───────────────────────────────────────────────────────────────────

root.SetHandler((InvocationContext ctx) =>
{
    var pr = ctx.ParseResult;

    if (pr.GetValueForOption(genConfigOpt))
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "sl-config.json");
        ConfigLoader.WriteSampleConfig(path);
        AnsiConsole.MarkupLine($"[green]✅ Sample config written to:[/] [cyan]{Markup.Escape(path)}[/]");
        ctx.ExitCode = 0;
        return;
    }

    var filterTerms = new List<string>(pr.GetValueForOption(filterOpt) ?? []);
    var actions = new List<(ActionKind Kind, string[] Names)>();

    // An action flag counts as "used" when it is PRESENT, even with zero values.
    // (HasOption is how `-f NAME -st` is told apart from "no -st at all".)
    void Collect(ActionKind kind, Option<string[]> opt, bool valuesAreFilter)
    {
        if (!pr.HasOption(opt)) return;
        var values = pr.GetValueForOption(opt) ?? [];

        if (valuesAreFilter)
        {
            filterTerms.AddRange(values);
            actions.Add((kind, Array.Empty<string>()));
        }
        else
        {
            actions.Add((kind, values));
        }
    }

    Collect(ActionKind.Start, startOpt, false);
    Collect(ActionKind.Stop, stopOpt, false);
    Collect(ActionKind.Restart, restartOpt, false);
    Collect(ActionKind.Pause, pauseOpt, false);
    Collect(ActionKind.Kill, killOpt, false);
    Collect(ActionKind.Start, filterStartOpt, true);
    Collect(ActionKind.Stop, filterStopOpt, true);
    Collect(ActionKind.Restart, filterRestartOpt, true);
    Collect(ActionKind.Kill, filterKillOpt, true);

    if (actions.Count > 1)
    {
        Console.Error.WriteLine("error: use only one action at a time (--start/--stop/--restart/--pause/--kill).");
        ctx.ExitCode = CliRunner.ExitUsage;
        return;
    }

    ActionKind? action = null;
    string[] actionNames = [];
    if (actions.Count == 1)
    {
        action = actions[0].Kind;
        actionNames = actions[0].Names;
    }

    RenderMode? mode = null;
    if (pr.GetValueForOption(tableOpt)) mode = RenderMode.Table;
    else if (pr.GetValueForOption(listOpt)) mode = RenderMode.DetailList;
    else if (pr.GetValueForOption(fastOpt)) mode = RenderMode.FastList;

    var request = new CliRequest
    {
        Action = action,
        ActionNames = actionNames,
        FilterTerms = filterTerms.ToArray(),
        StatusOnly = pr.GetValueForOption(statusOnlyOpt),
        StartOnly = pr.GetValueForOption(startOnlyOpt),
        SortBy = pr.GetValueForOption(sortByOpt),
        Tail = pr.GetValueForOption(tailOpt),
        Reverse = pr.GetValueForOption(reverseOpt),
        Exact = pr.GetValueForOption(exactOpt),
        UseRegex = pr.GetValueForOption(regexOpt),
        Json = pr.GetValueForOption(jsonOpt),
        Yes = pr.GetValueForOption(yesOpt),
        DryRun = pr.GetValueForOption(dryRunOpt),
        WithDependents = pr.GetValueForOption(depsOpt),
        Force = pr.GetValueForOption(forceOpt),
        TimeoutSeconds = pr.GetValueForOption(timeoutOpt) ?? 30,
        Mode = mode,
    };

    ctx.ExitCode = new CliRunner(appConfig, request).Run();
});

return await root.InvokeAsync(args);
