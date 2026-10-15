using System.Diagnostics;
using System.ServiceProcess;
using ServiceList.Models;
using Spectre.Console;

namespace ServiceList.Services;

public class ServiceController2
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public bool StartService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠  Service [bold]{name}[/] is already running.[/]");
                return true;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, _timeout);
            AnsiConsole.MarkupLine($"[green]✅ Service [bold]{name}[/] started successfully.[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to start [bold]{name}[/]: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    public bool StopService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠  Service [bold]{name}[/] is already stopped.[/]");
                return true;
            }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, _timeout);
            AnsiConsole.MarkupLine($"[green]⛔ Service [bold]{name}[/] stopped successfully.[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to stop [bold]{name}[/]: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    public bool RestartService(string name)
    {
        AnsiConsole.MarkupLine($"[cyan]🔄 Restarting [bold]{name}[/]...[/]");
        var stopped = StopService(name);
        if (!stopped) return false;
        Thread.Sleep(500);
        return StartService(name);
    }

    public bool PauseService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (!sc.CanPauseAndContinue)
            {
                AnsiConsole.MarkupLine($"[red]❌ Service [bold]{name}[/] does not support pause.[/]");
                return false;
            }
            sc.Pause();
            sc.WaitForStatus(ServiceControllerStatus.Paused, _timeout);
            AnsiConsole.MarkupLine($"[yellow]⏸  Service [bold]{name}[/] paused.[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to pause [bold]{name}[/]: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    public bool KillService(ServiceInfo svc)
    {
        // Try graceful stop first, then kill process by PID
        AnsiConsole.MarkupLine($"[red]💀 Force-killing [bold]{svc.Name}[/] (PID: {svc.Pid})...[/]");
        try
        {
            using var sc = new ServiceController(svc.Name);
            try { sc.Stop(); } catch { /* ignore */ }
        }
        catch { /* ignore */ }

        if (svc.Pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(svc.Pid);
                proc.Kill(entireProcessTree: true);
                AnsiConsole.MarkupLine($"[red]💥 Process {svc.Pid} killed.[/]");
                return true;
            }
            catch (ArgumentException)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠  Process {svc.Pid} already gone.[/]");
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Kill failed: {Markup.Escape(ex.Message)}[/]");
                return false;
            }
        }
        return false;
    }
}
