// File: ServiceReader.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Description: Reads Windows services via ServiceController + WMI (Win32_Service).
// License: MIT

using System.Management;
using System.ServiceProcess;
using ServiceList.Models;

namespace ServiceList.Services;

public sealed class ServiceReader
{
    /// <summary>Set when WMI failed; PID / start type / description are then missing.</summary>
    public string? WmiError { get; private set; }

    public IReadOnlyList<ServiceInfo> GetAllServices()
    {
        var wmiData = LoadWmiData();
        var services = new List<ServiceInfo>();
        int n = 1;

        var controllers = ServiceController.GetServices();
        try
        {
            foreach (var sc in controllers.OrderBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var name = sc.ServiceName;
                    wmiData.TryGetValue(name, out var wmi);

                    string[] deps;
                    try
                    {
                        var dependsOn = sc.ServicesDependedOn;
                        deps = dependsOn.Select(d => d.ServiceName).ToArray();
                        foreach (var d in dependsOn) d.Dispose();
                    }
                    catch { deps = []; }

                    ServiceStatus status;
                    try { status = MapStatus(sc.Status); }
                    catch { status = ServiceStatus.Unknown; }

                    services.Add(new ServiceInfo
                    {
                        Number = n++,
                        Name = name,
                        DisplayName = sc.DisplayName,
                        Status = status,
                        StartConfig = wmi != null ? MapStartType(wmi.StartMode) : StartType.Unknown,
                        Pid = wmi != null ? wmi.ProcessId : 0,
                        Description = wmi != null ? wmi.Description : string.Empty,
                        UserAccount = wmi != null ? wmi.StartName : string.Empty,
                        Dependencies = deps,
                    });
                }
                catch
                {
                    // Skip a service that throws during property access (access denied, vanished).
                }
            }
        }
        finally
        {
            foreach (var c in controllers) c.Dispose();
        }

        return services;
    }

    // ── WMI ───────────────────────────────────────────────────────────────

    private sealed record WmiEntry(string StartMode, int ProcessId, string Description, string StartName);

    private Dictionary<string, WmiEntry> LoadWmiData()
    {
        var result = new Dictionary<string, WmiEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,StartMode,ProcessId,Description,StartName FROM Win32_Service");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;

                    result[name] = new WmiEntry(
                        StartMode: obj["StartMode"]?.ToString() ?? string.Empty,
                        ProcessId: Convert.ToInt32(obj["ProcessId"] ?? 0),
                        Description: obj["Description"]?.ToString() ?? string.Empty,
                        StartName: obj["StartName"]?.ToString() ?? string.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            WmiError = ex.Message;
        }
        return result;
    }

    private static ServiceStatus MapStatus(ServiceControllerStatus s) => s switch
    {
        ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
        ServiceControllerStatus.StartPending => ServiceStatus.StartPending,
        ServiceControllerStatus.StopPending => ServiceStatus.StopPending,
        ServiceControllerStatus.Running => ServiceStatus.Running,
        ServiceControllerStatus.ContinuePending => ServiceStatus.ContinuePending,
        ServiceControllerStatus.PausePending => ServiceStatus.PausePending,
        ServiceControllerStatus.Paused => ServiceStatus.Paused,
        _ => ServiceStatus.Unknown,
    };

    private static StartType MapStartType(string mode) => mode.ToLowerInvariant() switch
    {
        "auto" => StartType.AutoStart,
        "manual" => StartType.DemandStart,
        "disabled" => StartType.Disabled,
        "boot" => StartType.BootStart,
        "system" => StartType.SystemStart,
        _ => StartType.Unknown,
    };
}
