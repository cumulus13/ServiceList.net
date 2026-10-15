using ServiceList.Config;
using ServiceList.Models;
using Spectre.Console;

namespace ServiceList.Helpers;

public class Renderer
{
    private readonly ColorTheme _theme;
    private readonly int _width;

    public Renderer(AppConfig config)
    {
        _theme = config.Colors;
        _width = config.TerminalWidthOverride > 0
            ? config.TerminalWidthOverride
            : Console.WindowWidth > 0 ? Console.WindowWidth : 120;
    }

    // ── Public entry ─────────────────────────────────────────────────────

    public void Render(IEnumerable<ServiceInfo> services, RenderMode mode)
    {
        var list = services.ToList();

        if (list.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠  No services matched.[/]");
            return;
        }

        // Re-number sequentially after filter/sort
        for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;

        switch (mode)
        {
            case RenderMode.Table:    RenderTable(list);    break;
            case RenderMode.FastList: RenderFastList(list); break;
            default:                  RenderDetailList(list); break;
        }

        AnsiConsole.MarkupLine($"\n[{_theme.LabelColor}]📋 Total: [bold]{list.Count}[/] service(s)[/]");
    }

    // ── Table ─────────────────────────────────────────────────────────────

    private void RenderTable(List<ServiceInfo> list)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderStyle(Style.Parse(_theme.TableBorder));
        table.Expand();

        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]#[/]")).RightAligned());
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]Name[/]")));
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]Display Name[/]")));
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]PID[/]")).RightAligned());
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]Status[/]")));
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]Start[/]")));
        table.AddColumn(new TableColumn(new Markup($"[{_theme.TableHeader}]Description[/]")));

        foreach (var svc in list)
        {
            table.AddRow(
                new Markup($"[{_theme.NumberColor}]{svc.Number}[/]"),
                new Markup($"[{_theme.NameColor}]{Escape(svc.Name)}[/]"),
                new Markup($"[{_theme.DisplayNameColor}]{Escape(svc.DisplayName)}[/]"),
                new Markup($"[{_theme.PidColor}]{(svc.Pid > 0 ? svc.Pid : "-")}[/]"),
                new Markup(FormatStatus(svc)),
                new Markup(FormatStartConfig(svc)),
                new Markup($"[{_theme.DescriptionColor}]{Escape(Truncate(svc.Description, 60))}[/]")
            );
        }

        AnsiConsole.Write(table);
    }

    private static int GetTerminalWidth()
    {
        try
        {
            // Use Console.WindowWidth which queries the actual terminal
            return Console.WindowWidth;
        }
        catch // Fallback for non-interactive environments (e.g., IDE output, piping)
        {
            return 80; // Standard default terminal width, like Python's shutil fallback
        }
    }

    // ── Detail list ───────────────────────────────────────────────────────

    private void RenderDetailList(List<ServiceInfo> list)
    {
        // var sep = new string('─', Math.Min(_width, 100));
        int terminal_width = GetTerminalWidth();
        var sep = new string('─', terminal_width);
        foreach (var svc in list)
        {
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]NAME         :[/] [{_theme.NameColor}]{Escape(svc.Name)}[/]");
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]DISPLAY NAME :[/] [{_theme.DisplayNameColor}]{Escape(svc.DisplayName)}[/]");
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]PID          :[/] [{_theme.PidColor}]{(svc.Pid > 0 ? svc.Pid : "N/A")}[/]");
            AnsiConsole.Markup    ($"[{_theme.LabelColor}]STATUS       :[/] ");
            AnsiConsole.MarkupLine(FormatStatus(svc));
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]START CONFIG :[/] {FormatStartConfig(svc)}");
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]USER         :[/] [{_theme.LabelColor}]{Escape(svc.UserAccount)}[/]");
            if (svc.Dependencies.Length > 0)
                AnsiConsole.MarkupLine($"[{_theme.LabelColor}]DEPENDS ON   :[/] [{_theme.DependColor}]{Escape(string.Join(", ", svc.Dependencies))}[/]");
            AnsiConsole.MarkupLine($"[{_theme.LabelColor}]DESCRIPTION  :[/] [{_theme.DescriptionColor}]{Escape(WrapDescription(svc.Description, "             : "))}[/]");
            AnsiConsole.MarkupLine($"[{_theme.TableBorder}]{sep}[/]");
        }
    }

    // ── Fast list (compact one-liner) ──────────────────────────────────────

    private void RenderFastList(List<ServiceInfo> list)
    {
        foreach (var svc in list)
        {
            try
            {
                var pid = svc.Pid > 0 ? svc.Pid.ToString() : "-";
                AnsiConsole.MarkupLine(
                    $"[{_theme.NumberColor}]{svc.Number}.[/] " +
                    $"[{_theme.FastNameColor}]{Escape(svc.Name)}[/] " +
                    $"[{_theme.LabelColor}]([/][{_theme.FastDisplayColor}]{Escape(svc.DisplayName)}[/][{_theme.LabelColor}])[/] " +
                    $"[{_theme.LabelColor}][[PID:[/][{_theme.FastPidColor}]{pid}[/][{_theme.LabelColor}]]][/] " +
                    FormatStatus(svc) + " " +
                    FormatStartConfig(svc)
                );
            }
            catch
            {
                // Fallback: plain console if markup fails for this entry
                Console.WriteLine($"{svc.Number}. {svc.Name} ({svc.DisplayName}) [PID:{(svc.Pid > 0 ? svc.Pid : "-")}] {svc.StatusLabel} {svc.StartLabel}");
            }
        }
    }

    // ── Formatters ────────────────────────────────────────────────────────

    private string FormatStatus(ServiceInfo svc)
    {
        var color = svc.Status switch
        {
            ServiceStatus.Running         => _theme.StatusRunning,
            ServiceStatus.Stopped         => _theme.StatusStopped,
            ServiceStatus.Paused          => _theme.StatusPaused,
            ServiceStatus.StartPending or
            ServiceStatus.StopPending or
            ServiceStatus.PausePending or
            ServiceStatus.ContinuePending => _theme.StatusPending,
            _                             => _theme.StatusUnknown,
        };
        return $"[{color}]{Escape(svc.StatusLabel)}[/]";
    }

    private string FormatStartConfig(ServiceInfo svc)
    {
        var color = svc.StartConfig switch
        {
            StartType.AutoStart   => _theme.StartAuto,
            StartType.DemandStart => _theme.StartDemand,
            StartType.Disabled    => _theme.StartDisabled,
            StartType.BootStart   => _theme.StartBoot,
            StartType.SystemStart => _theme.StartSystem,
            _                     => _theme.StartOther,
        };
        return $"[{color}]{Escape(svc.StartLabel)}[/]";
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    private static string Escape(string s) => Markup.Escape(s ?? string.Empty);

    private static string Truncate(string s, int max)
        => s.Length > max ? s[..max] + "…" : s;

    private string WrapDescription(string desc, string indent)
    {
        if (string.IsNullOrWhiteSpace(desc)) return "(none)";
        var maxW = Math.Max(40, _width - indent.Length - 5);
        if (desc.Length <= maxW) return desc;

        var words = desc.Split(' ');
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (current.Length + word.Length + 1 > maxW)
            {
                lines.Add(current.ToString());
                current.Clear().Append(indent).Append(word);
            }
            else
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return string.Join('\n', lines);
    }

}

public enum RenderMode
{
    Auto,        // ≥131 cols → Table, else DetailList
    Table,
    DetailList,
    FastList,
}
