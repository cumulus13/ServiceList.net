// File: ServiceController2.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Description: Start/stop/restart/pause/kill with structured results (no console I/O).
// License: MIT

using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using ServiceList.Models;
using SCS = System.ServiceProcess.ServiceControllerStatus;

namespace ServiceList.Services;

public enum ActionOutcome { Success, NoChange, Failed }

public sealed record ActionResult(string Name, string Action, ActionOutcome Outcome, string Message);

public sealed class ServiceController2
{
    private readonly TimeSpan _timeout;
    private readonly bool _withDependents;
    private readonly bool _dryRun;

    public ServiceController2(TimeSpan? timeout = null, bool withDependents = false, bool dryRun = false)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _withDependents = withDependents;
        _dryRun = dryRun;
    }

    public ActionResult Start(string name) => Execute("start", name, StartCore);
    public ActionResult Stop(string name) => Execute("stop", name, StopCore);
    public ActionResult Restart(string name) => Execute("restart", name, RestartCore);
    public ActionResult Pause(string name) => Execute("pause", name, PauseCore);

    // ── Core operations ───────────────────────────────────────────────────

    private (ActionOutcome, string) StartCore(ServiceController sc)
    {
        sc.Refresh();
        var status = sc.Status;

        if (status == SCS.Running) return (ActionOutcome.NoChange, "already running");

        if (status == SCS.StopPending)
        {
            sc.WaitForStatus(SCS.Stopped, _timeout);
            sc.Refresh();
            status = sc.Status;
        }

        if (status is SCS.StartPending or SCS.ContinuePending)
        {
            sc.WaitForStatus(SCS.Running, _timeout);
            return (ActionOutcome.Success, "started");
        }

        if (status is SCS.Paused or SCS.PausePending)
        {
            if (!sc.CanPauseAndContinue) return (ActionOutcome.Failed, "service is paused and cannot be continued");
            sc.Continue();
            sc.WaitForStatus(SCS.Running, _timeout);
            return (ActionOutcome.Success, "resumed from paused state");
        }

        sc.Start();
        sc.WaitForStatus(SCS.Running, _timeout);
        return (ActionOutcome.Success, "started");
    }

    private (ActionOutcome, string) StopCore(ServiceController sc)
    {
        var dependents = new List<string>();
        if (!StopTree(sc, dependents, isRoot: true))
            return (ActionOutcome.NoChange, "already stopped");

        return (ActionOutcome.Success,
            dependents.Count == 0 ? "stopped" : $"stopped (+{dependents.Count} dependent: {string.Join(", ", dependents)})");
    }

    private (ActionOutcome, string) RestartCore(ServiceController sc)
    {
        var dependents = new List<string>();
        StopTree(sc, dependents, isRoot: true);

        sc.Refresh();
        sc.Start();
        sc.WaitForStatus(SCS.Running, _timeout);

        // Dependents were stopped deepest-first; bring them back in reverse order.
        var failed = new List<string>();
        foreach (var dep in Enumerable.Reverse(dependents))
        {
            try
            {
                using var d = new ServiceController(dep);
                if (d.Status == SCS.Stopped)
                {
                    d.Start();
                    d.WaitForStatus(SCS.Running, _timeout);
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{dep}: {Describe(ex)}");
            }
        }

        if (failed.Count > 0)
            return (ActionOutcome.Failed, $"restarted, but dependents failed to start: {string.Join("; ", failed)}");

        return (ActionOutcome.Success,
            dependents.Count == 0 ? "restarted" : $"restarted (+{dependents.Count} dependent)");
    }

    private (ActionOutcome, string) PauseCore(ServiceController sc)
    {
        sc.Refresh();
        if (sc.Status == SCS.Paused) return (ActionOutcome.NoChange, "already paused");
        if (!sc.CanPauseAndContinue) return (ActionOutcome.Failed, "service does not support pause");
        if (sc.Status != SCS.Running) return (ActionOutcome.Failed, $"cannot pause a service that is {sc.Status}");

        sc.Pause();
        sc.WaitForStatus(SCS.Paused, _timeout);
        return (ActionOutcome.Success, "paused");
    }

    /// <summary>
    /// Stop a service, first stopping running dependents when allowed (--deps).
    /// Returns false when it was already stopped. Dependents stopped are appended
    /// to <paramref name="stoppedDependents"/>, deepest first.
    /// </summary>
    private bool StopTree(ServiceController sc, List<string> stoppedDependents, bool isRoot)
    {
        sc.Refresh();
        if (sc.Status == SCS.Stopped) return false;

        if (sc.Status != SCS.StopPending)
        {
            var running = sc.DependentServices.Where(d => d.Status != SCS.Stopped).ToArray();
            try
            {
                if (running.Length > 0)
                {
                    if (!_withDependents)
                        throw new InvalidOperationException(
                            $"running dependent services: {string.Join(", ", running.Select(d => d.ServiceName))} (use --deps to stop them too)");

                    foreach (var d in running)
                        StopTree(d, stoppedDependents, isRoot: false);
                }
            }
            finally
            {
                foreach (var d in running) d.Dispose();
            }

            if (!sc.CanStop) throw new InvalidOperationException("service does not accept stop requests");
            sc.Stop();
        }

        sc.WaitForStatus(SCS.Stopped, _timeout);
        if (!isRoot) stoppedDependents.Add(sc.ServiceName);
        return true;
    }

    // ── Kill ──────────────────────────────────────────────────────────────

    /// <param name="sharedWith">Other services (not being killed) hosted by the same PID.</param>
    public ActionResult Kill(ServiceInfo svc, IReadOnlyCollection<string> sharedWith, bool force)
    {
        const string action = "kill";

        if (svc.Pid <= 0)
            return new(svc.Name, action, ActionOutcome.NoChange, "no running process (PID 0)");

        if (svc.Pid <= 4)
            return new(svc.Name, action, ActionOutcome.Failed, $"refusing to kill protected system PID {svc.Pid}");

        if (sharedWith.Count > 0 && !force)
        {
            var list = string.Join(", ", sharedWith.Take(5)) + (sharedWith.Count > 5 ? ", ..." : "");
            return new(svc.Name, action, ActionOutcome.Failed,
                $"PID {svc.Pid} is shared with other services ({list}); use --force to kill anyway");
        }

        if (_dryRun)
            return new(svc.Name, action, ActionOutcome.Success, $"dry-run: would kill PID {svc.Pid}");

        try
        {
            using var proc = Process.GetProcessById(svc.Pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit((int)_timeout.TotalMilliseconds);
            return new(svc.Name, action, ActionOutcome.Success, $"process {svc.Pid} killed");
        }
        catch (ArgumentException)
        {
            return new(svc.Name, action, ActionOutcome.NoChange, $"process {svc.Pid} already gone");
        }
        catch (InvalidOperationException)
        {
            return new(svc.Name, action, ActionOutcome.NoChange, $"process {svc.Pid} already exited");
        }
        catch (Exception ex)
        {
            return new(svc.Name, action, ActionOutcome.Failed, Describe(ex));
        }
    }

    // ── Plumbing ──────────────────────────────────────────────────────────

    private ActionResult Execute(string action, string name, Func<ServiceController, (ActionOutcome, string)> body)
    {
        if (_dryRun)
            return new(name, action, ActionOutcome.Success, $"dry-run: would {action}");

        try
        {
            using var sc = new ServiceController(name);
            var (outcome, message) = body(sc);
            return new(name, action, outcome, message);
        }
        catch (Exception ex)
        {
            return new(name, action, ActionOutcome.Failed, Describe(ex));
        }
    }

    private static string Describe(Exception ex)
    {
        var win32 = ex as Win32Exception ?? ex.InnerException as Win32Exception;
        if (win32 is not null)
        {
            return win32.NativeErrorCode switch
            {
                5 => "access denied (run as Administrator)",
                1051 => "dependent services are running (use --deps)",
                1052 => "the requested control is not valid for this service",
                1053 => "the service did not respond in time",
                1056 => "service is already running",
                1058 => "service is disabled",
                1060 => "service not found",
                1061 => "service cannot accept control messages right now",
                1062 => "service is not started",
                _ => win32.Message,
            };
        }

        if (ex is System.ServiceProcess.TimeoutException)
            return "timed out waiting for the service to change state (see --timeout)";

        return ex.Message;
    }
}
