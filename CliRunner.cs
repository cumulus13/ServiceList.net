// File: CliRunner.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Description: Executes a parsed CLI request: list, filter, and act on services.
// License: MIT

using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ServiceList.Config;
using ServiceList.Helpers;
using ServiceList.Models;
using ServiceList.Services;
using Spectre.Console;

namespace ServiceList.Cli;

public enum ActionKind { Start, Stop, Restart, Pause, Kill }

public sealed class CliRequest
{
    public ActionKind? Action { get; init; }
    /// <summary>Names passed directly to the action flag, e.g. `--stop Spooler`.</summary>
    public string[] ActionNames { get; init; } = [];
    public string[] FilterTerms { get; init; } = [];
    public string? StatusOnly { get; init; }
    public string? StartOnly { get; init; }
    public string? SortBy { get; init; }
    public int? Tail { get; init; }
    public bool Reverse { get; init; }
    public bool Exact { get; init; }
    public bool UseRegex { get; init; }
    public bool Json { get; init; }
    public bool Yes { get; init; }
    public bool DryRun { get; init; }
    public bool WithDependents { get; init; }
    public bool Force { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public RenderMode? Mode { get; init; }
}

public sealed class CliRunner
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    private static readonly IAnsiConsole Err =
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppConfig _config;
    private readonly CliRequest _r;
    private Renderer? _renderer;

    public CliRunner(AppConfig config, CliRequest request)
    {
        _config = config;
        _r = request;
    }

    private bool Json => _r.Json;
    private Renderer GetRenderer() => _renderer ??= new Renderer(_config);

    public int Run()
    {
        try
        {
            return RunCore();
        }
        catch (RegexMatchTimeoutException)
        {
            return Usage("filter pattern took too long to evaluate; simplify it");
        }
        catch (Exception ex)
        {
            Err.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
            return ExitFailed;
        }
    }

    // ── Main flow ─────────────────────────────────────────────────────────

    private int RunCore()
    {
        var r = _r;

        ServiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(r.StatusOnly))
        {
            status = FilterHelper.ParseStatus(r.StatusOnly);
            if (status is null)
                return Usage($"invalid --status-only '{r.StatusOnly}' " +
                             "(running|stopped|paused|startpending|stoppending|pausepending|continuepending)");
        }

        StartType? startType = null;
        if (!string.IsNullOrWhiteSpace(r.StartOnly))
        {
            startType = FilterHelper.ParseStartType(r.StartOnly);
            if (startType is null)
                return Usage($"invalid --start-only '{r.StartOnly}' (auto|manual|disabled|boot|system)");
        }

        if (!FilterHelper.IsValidSortKey(r.SortBy))
            return Usage($"invalid --sort-by '{r.SortBy}' (name|displayname|pid|status|start|description)");

        if (r.TimeoutSeconds <= 0)
            return Usage("--timeout must be greater than 0");

        bool hasSelectors = r.FilterTerms.Length > 0 || status.HasValue || startType.HasValue;

        if (r.Action is { } kind0 && r.ActionNames.Length == 0 && !hasSelectors)
            return Usage($"--{Verb(kind0)} needs a service name or a selector (-f, -ss, -cc); " +
                         "refusing to act on every service");

        var reader = new ServiceReader();
        var all = Load(reader);
        if (reader.WmiError is not null)
            Warn($"WMI unavailable ({reader.WmiError}); PID, start type and description may be missing");

        List<ServiceInfo> selected;
        try
        {
            IEnumerable<ServiceInfo> view = all;

            if (r.FilterTerms.Length > 0)
                view = FilterHelper.Filter(view, r.FilterTerms,
                    new FilterOptions(r.Exact, r.UseRegex, Deep: r.Action is null));
            if (status.HasValue) view = FilterHelper.FilterByStatus(view, status.Value);
            if (startType.HasValue) view = FilterHelper.FilterByStartType(view, startType.Value);

            view = FilterHelper.Sort(view, r.SortBy, r.Reverse);
            view = FilterHelper.Tail(view, r.Tail);
            selected = view.ToList();
        }
        catch (ArgumentException ex)
        {
            return Usage($"invalid filter: {ex.Message}");
        }

        if (r.Action is { } kind)
            return RunAction(kind, all, hasSelectors ? selected : new List<ServiceInfo>());

        // ── Plain listing ────────────────────────────────────────────────
        if (Json)
        {
            WriteJson(selected.Select(s => new
            {
                s.Name,
                s.DisplayName,
                s.Pid,
                Status = s.Status.ToString(),
                StartType = s.StartConfig.ToString(),
                Account = s.UserAccount,
                s.Description,
                s.Dependencies,
            }).ToList());
        }
        else
        {
            GetRenderer().Render(selected, ResolveMode());
        }

        // grep-style: a filter that matches nothing is exit code 1.
        return selected.Count == 0 && hasSelectors ? ExitFailed : ExitOk;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private int RunAction(ActionKind kind, List<ServiceInfo> all, List<ServiceInfo> selected)
    {
        var r = _r;
        var verb = Verb(kind);
        var results = new List<ActionResult>();
        var targets = new List<ServiceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Report(ActionResult res)
        {
            results.Add(res);
            if (Json) return;

            var (icon, color) = r.DryRun
                ? ("🧪", "grey")
                : res.Outcome switch
                {
                    ActionOutcome.Success => ("✅", "green"),
                    ActionOutcome.NoChange => ("⚠", "yellow"),
                    _ => ("❌", "red"),
                };

            AnsiConsole.MarkupLine(
                $"[{color}]{icon} {Markup.Escape(res.Action)} [bold]{Markup.Escape(res.Name)}[/]: {Markup.Escape(res.Message)}[/]");
        }

        // 1) Explicit names given to the action flag (e.g. --stop Spooler).
        if (r.ActionNames.Length > 0)
        {
            var (found, missing) = FilterHelper.ResolveNames(all, r.ActionNames, allowPid: kind == ActionKind.Kill);

            foreach (var m in missing)
            {
                var hint = FilterHelper.Suggest(all, m, 5);
                var msg = hint.Count > 0
                    ? $"not found. Did you mean: {string.Join(", ", hint)}? (or select by keyword with -f {m})"
                    : "not found";
                Report(new ActionResult(m, verb, ActionOutcome.Failed, msg));
            }

            foreach (var s in found)
                if (seen.Add(s.Name)) targets.Add(s);
        }

        // 2) Services selected by -f / -ss / -cc (and the --filter-* aliases).
        foreach (var s in selected)
            if (seen.Add(s.Name)) targets.Add(s);

        if (targets.Count == 0)
        {
            if (results.Count == 0) Err.MarkupLine("[yellow]⚠ No services matched.[/]");
            if (Json) WriteJson(results);
            return ExitFailed;
        }

        if (!r.DryRun && !IsElevated())
            Warn("not running as Administrator; service control will most likely fail (access denied)");

        // Confirm bulk actions.
        if (!r.DryRun && !r.Yes && targets.Count > 1)
        {
            if (Json || Console.IsInputRedirected)
                return Usage($"refusing to {verb} {targets.Count} services non-interactively; pass --yes");

            GetRenderer().Render(targets, RenderMode.FastList);
            if (!AnsiConsole.Confirm($"[yellow]{verb} {targets.Count} services?[/]", false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                return ExitFailed;
            }
        }

        var controller = new ServiceController2(TimeSpan.FromSeconds(r.TimeoutSeconds), r.WithDependents, r.DryRun);

        var targetNames = new HashSet<string>(targets.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var pidOwners = all.Where(s => s.Pid > 0)
                           .GroupBy(s => s.Pid)
                           .ToDictionary(g => g.Key, g => g.Select(s => s.Name).ToList());

        IReadOnlyCollection<string> SharedWith(ServiceInfo s)
            => pidOwners.TryGetValue(s.Pid, out var owners)
                ? owners.Where(n => !targetNames.Contains(n)).ToList()
                : new List<string>();

        foreach (var svc in targets)
        {
            Report(kind switch
            {
                ActionKind.Start => controller.Start(svc.Name),
                ActionKind.Stop => controller.Stop(svc.Name),
                ActionKind.Restart => controller.Restart(svc.Name),
                ActionKind.Pause => controller.Pause(svc.Name),
                ActionKind.Kill => controller.Kill(svc, SharedWith(svc), r.Force),
                _ => throw new InvalidOperationException($"unknown action {kind}"),
            });
        }

        int ok = results.Count(x => x.Outcome == ActionOutcome.Success);
        int unchanged = results.Count(x => x.Outcome == ActionOutcome.NoChange);
        int failed = results.Count(x => x.Outcome == ActionOutcome.Failed);

        if (Json)
            WriteJson(results);
        else if (results.Count > 1)
            AnsiConsole.MarkupLine($"[grey]Done: {ok} ok, {unchanged} unchanged, {failed} failed[/]");

        return failed > 0 ? ExitFailed : ExitOk;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private List<ServiceInfo> Load(ServiceReader reader)
    {
        if (Json || Console.IsOutputRedirected)
            return reader.GetAllServices().ToList();

        List<ServiceInfo> list = [];
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start("🔍 Loading services...", _ => { list = reader.GetAllServices().ToList(); });
        return list;
    }

    private RenderMode ResolveMode()
    {
        if (_r.Mode is { } m && m != RenderMode.Auto) return m;

        return _config.DefaultDisplay?.ToLowerInvariant() switch
        {
            "table" => RenderMode.Table,
            "list" => RenderMode.DetailList,
            _ => TerminalWidth() >= 131 ? RenderMode.Table : RenderMode.DetailList,
        };
    }

    private static int TerminalWidth()
    {
        try { return Console.WindowWidth; }
        catch { return 80; } // redirected / no console
    }

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Verb(ActionKind k) => k.ToString().ToLowerInvariant();

    private static void WriteJson<T>(T value) => Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOpts));

    private static int Usage(string message)
    {
        Err.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");
        Err.MarkupLine("[grey]Try 'sl --help'.[/]");
        return ExitUsage;
    }

    private static void Warn(string message) => Err.MarkupLine($"[yellow]⚠ {Markup.Escape(message)}[/]");
}
