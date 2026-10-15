using System.ServiceProcess;
using System.Management;
using ServiceList.Models;

namespace ServiceList.Services;

/// <summary>
/// Reads Windows service information via ServiceController + WMI.
/// WMI (Win32_Service) provides StartType, Description, ProcessId, etc.
/// </summary>
public class ServiceReader
{
    public IReadOnlyList<ServiceInfo> GetAllServices()
    {
        var wmiData = LoadWmiData();
        var services = new List<ServiceInfo>();
        int n = 1;

        var controllers = ServiceController.GetServices();
        foreach (var sc in controllers.OrderBy(s => s.ServiceName))
        {
            try
            {
                var name = sc.ServiceName;
                wmiData.TryGetValue(name.ToLowerInvariant(), out var wmi);

                string[] deps;
                try { deps = sc.ServicesDependedOn.Select(d => d.ServiceName).ToArray(); }
                catch  { deps = []; }

                ServiceStatus status;
                try { status = MapStatus(sc.Status); }
                catch { status = ServiceStatus.Unknown; }

                var info = new ServiceInfo
                {
                    Number      = n++,
                    Name        = name,
                    DisplayName = sc.DisplayName,
                    Status      = status,
                    StartConfig = wmi != null ? MapStartType(wmi.StartMode) : StartType.Unknown,
                    Pid         = wmi != null ? wmi.ProcessId : 0,
                    Description = wmi != null ? wmi.Description : string.Empty,
                    UserAccount = wmi != null ? wmi.StartName : string.Empty,
                    Dependencies= deps,
                };

                services.Add(info);
            }
            catch
            {
                // Skip any service that throws during property access (e.g. access denied, not found)
            }
        }

        return services;
    }

    // ── WMI helpers ──────────────────────────────────────────────────────

    private record WmiEntry(string StartMode, int ProcessId, string Description, string StartName);

    private static Dictionary<string, WmiEntry> LoadWmiData()
    {
        var result = new Dictionary<string, WmiEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,StartMode,ProcessId,Description,StartName FROM Win32_Service");

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                result[name.ToLowerInvariant()] = new WmiEntry(
                    StartMode   : obj["StartMode"]?.ToString()  ?? string.Empty,
                    ProcessId   : Convert.ToInt32(obj["ProcessId"] ?? 0),
                    Description : obj["Description"]?.ToString() ?? string.Empty,
                    StartName   : obj["StartName"]?.ToString()   ?? string.Empty
                );
            }
        }
        catch
        {
            // WMI unavailable — caller handles missing fields gracefully
        }
        return result;
    }

    private static ServiceStatus MapStatus(ServiceControllerStatus s) => s switch
    {
        ServiceControllerStatus.Stopped            => ServiceStatus.Stopped,
        ServiceControllerStatus.StartPending       => ServiceStatus.StartPending,
        ServiceControllerStatus.StopPending        => ServiceStatus.StopPending,
        ServiceControllerStatus.Running            => ServiceStatus.Running,
        ServiceControllerStatus.ContinuePending    => ServiceStatus.ContinuePending,
        ServiceControllerStatus.PausePending       => ServiceStatus.PausePending,
        ServiceControllerStatus.Paused             => ServiceStatus.Paused,
        _                                          => ServiceStatus.Unknown,
    };

    private static StartType MapStartType(string mode) => mode.ToLowerInvariant() switch
    {
        "auto"     => StartType.AutoStart,
        "manual"   => StartType.DemandStart,
        "disabled" => StartType.Disabled,
        "boot"     => StartType.BootStart,
        "system"   => StartType.SystemStart,
        _          => StartType.Unknown,
    };
}
