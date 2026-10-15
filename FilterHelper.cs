using ServiceList.Models;

namespace ServiceList.Helpers;

public static class FilterHelper
{
    /// <summary>
    /// Filter services by keyword (matches name, displayname, pid, description, status, start config).
    /// </summary>
    public static IEnumerable<ServiceInfo> Filter(IEnumerable<ServiceInfo> services, IEnumerable<string> keywords)
    {
        var terms = keywords.Select(k => k.ToLowerInvariant()).ToArray();
        if (terms.Length == 0) return services;

        return services.Where(s => terms.Any(term =>
            s.Name.Contains(term, StringComparison.OrdinalIgnoreCase)         ||
            s.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)  ||
            s.Description.Contains(term, StringComparison.OrdinalIgnoreCase)  ||
            s.Pid.ToString().Contains(term)                                    ||
            s.StatusLabel.Contains(term, StringComparison.OrdinalIgnoreCase)  ||
            s.StartLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
        ));
    }

    /// <summary>Filter by exact ServiceStatus value.</summary>
    public static IEnumerable<ServiceInfo> FilterByStatus(IEnumerable<ServiceInfo> services, ServiceStatus status)
        => services.Where(s => s.Status == status);

    /// <summary>Filter by exact StartType value.</summary>
    public static IEnumerable<ServiceInfo> FilterByStartType(IEnumerable<ServiceInfo> services, StartType startType)
        => services.Where(s => s.StartConfig == startType);

    /// <summary>Sort services by a field name (case-insensitive).</summary>
    public static IEnumerable<ServiceInfo> Sort(IEnumerable<ServiceInfo> services, string? sortBy, bool reverse)
    {
        if (string.IsNullOrEmpty(sortBy)) return services;

        IOrderedEnumerable<ServiceInfo> ordered = sortBy.ToLowerInvariant() switch
        {
            "name"         => services.OrderBy(s => s.Name),
            "displayname"  => services.OrderBy(s => s.DisplayName),
            "pid"          => services.OrderBy(s => s.Pid),
            "status"       => services.OrderBy(s => s.Status),
            "start"        or
            "start_config" or
            "startconfig"  => services.OrderBy(s => s.StartConfig),
            "description"  => services.OrderBy(s => s.Description),
            _              => services.OrderBy(s => s.Name),
        };

        return reverse ? ordered.Reverse() : ordered;
    }

    /// <summary>Take last N entries (tail behaviour).</summary>
    public static IEnumerable<ServiceInfo> Tail(IEnumerable<ServiceInfo> services, int? tail)
    {
        if (tail is null or <= 0) return services;
        var list = services.ToList();
        return list.Skip(Math.Max(0, list.Count - tail.Value));
    }

    /// <summary>Parse a status string from CLI arg.</summary>
    public static ServiceStatus? ParseStatus(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return s.Replace(" ", "").ToLowerInvariant() switch
        {
            "running"          => ServiceStatus.Running,
            "stopped"          => ServiceStatus.Stopped,
            "startpending"     => ServiceStatus.StartPending,
            "stoppending"      => ServiceStatus.StopPending,
            "paused"           => ServiceStatus.Paused,
            "pausepending"     => ServiceStatus.PausePending,
            "continuepending"  => ServiceStatus.ContinuePending,
            _                  => null,
        };
    }

    /// <summary>Parse a start type string from CLI arg.</summary>
    public static StartType? ParseStartType(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return s.ToLowerInvariant() switch
        {
            "auto"     or "autostart"   => StartType.AutoStart,
            "manual"   or "demand"      => StartType.DemandStart,
            "disabled"                  => StartType.Disabled,
            "boot"     or "bootstart"   => StartType.BootStart,
            "system"   or "systemstart" => StartType.SystemStart,
            _                           => null,
        };
    }
}
