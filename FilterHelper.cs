// File: FilterHelper.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Description: Filtering, name resolution, sorting and parsing helpers.
// License: MIT

using System.Text.RegularExpressions;
using ServiceList.Models;

namespace ServiceList.Helpers;

/// <param name="Exact">Case-insensitive equality instead of substring.</param>
/// <param name="UseRegex">Treat keywords as regular expressions.</param>
/// <param name="Deep">Also match description / account / status / start labels.
/// Listing uses deep matching; ACTIONS use shallow matching (name, display name,
/// exact PID) so that `-f mysql -st` can never stop a service that merely
/// mentions "mysql" in its description.</param>
public sealed record FilterOptions(bool Exact = false, bool UseRegex = false, bool Deep = true);

public static class FilterHelper
{
    private static readonly HashSet<string> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "displayname", "pid", "status", "start", "start_config", "startconfig", "description",
    };

    public static bool IsValidSortKey(string? key)
        => string.IsNullOrWhiteSpace(key) || SortKeys.Contains(key.Trim());

    // ── Filtering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Keep services matching ANY keyword. Keywords may contain * and ? wildcards.
    /// A purely numeric keyword also matches an exact PID.
    /// Throws <see cref="ArgumentException"/> for an invalid regex.
    /// </summary>
    public static IEnumerable<ServiceInfo> Filter(
        IEnumerable<ServiceInfo> services,
        IEnumerable<string> keywords,
        FilterOptions? options = null)
    {
        options ??= new FilterOptions();

        var terms = keywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0) return services;

        // Compiled eagerly so a bad regex fails here, not mid-enumeration.
        var matchers = terms.Select(t => (Term: t, Match: BuildMatcher(t, options))).ToArray();

        return services
            .Where(s => matchers.Any(m => Matches(s, m.Term, m.Match, options.Deep)))
            .ToList();
    }

    private static bool Matches(ServiceInfo s, string term, Func<string, bool> m, bool deep)
    {
        if (m(s.Name) || m(s.DisplayName)) return true;
        if (int.TryParse(term, out var pid) && pid > 0 && s.Pid == pid) return true;
        if (!deep) return false;
        return m(s.Description) || m(s.UserAccount) || m(s.StatusLabel) || m(s.StartLabel);
    }

    private static Func<string, bool> BuildMatcher(string term, FilterOptions o)
    {
        const RegexOptions ro = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var timeout = TimeSpan.FromSeconds(1);

        if (o.UseRegex)
        {
            var rx = new Regex(term, ro, timeout);
            return s => rx.IsMatch(s);
        }

        if (term.Contains('*') || term.Contains('?'))
        {
            var pattern = "^" + Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var rx = new Regex(pattern, ro, timeout);
            return s => rx.IsMatch(s);
        }

        if (o.Exact)
            return s => s.Equals(term, StringComparison.OrdinalIgnoreCase);

        return s => s.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve explicit service names (as given to --stop NAME ...). Exact name or
    /// display name (case-insensitive), wildcards allowed, PID allowed when asked.
    /// </summary>
    public static (List<ServiceInfo> Found, List<string> Missing) ResolveNames(
        IReadOnlyList<ServiceInfo> all, IEnumerable<string> tokens, bool allowPid)
    {
        var found = new List<ServiceInfo>();
        var missing = new List<string>();

        foreach (var raw in tokens)
        {
            var token = raw?.Trim();
            if (string.IsNullOrEmpty(token)) continue;

            List<ServiceInfo> hits;
            if (token.Contains('*') || token.Contains('?'))
            {
                var m = BuildMatcher(token, new FilterOptions());
                hits = all.Where(s => m(s.Name) || m(s.DisplayName)).ToList();
            }
            else
            {
                hits = all.Where(s =>
                    s.Name.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                    s.DisplayName.Equals(token, StringComparison.OrdinalIgnoreCase)).ToList();

                if (hits.Count == 0 && allowPid && int.TryParse(token, out var pid) && pid > 0)
                    hits = all.Where(s => s.Pid == pid).ToList();
            }

            if (hits.Count == 0) missing.Add(token);
            else found.AddRange(hits);
        }

        return (found.DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(), missing);
    }

    /// <summary>"Did you mean" candidates for a name that did not resolve.</summary>
    public static List<string> Suggest(IEnumerable<ServiceInfo> all, string token, int max)
        => all.Where(s => s.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                          s.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase))
              .Select(s => s.Name)
              .Take(max)
              .ToList();

    public static IEnumerable<ServiceInfo> FilterByStatus(IEnumerable<ServiceInfo> services, ServiceStatus status)
        => services.Where(s => s.Status == status);

    public static IEnumerable<ServiceInfo> FilterByStartType(IEnumerable<ServiceInfo> services, StartType startType)
        => services.Where(s => s.StartConfig == startType);

    // ── Sorting / tail ────────────────────────────────────────────────────

    /// <summary>Sort by field (case-insensitive). `reverse` alone sorts by name, descending.</summary>
    public static IEnumerable<ServiceInfo> Sort(IEnumerable<ServiceInfo> services, string? sortBy, bool reverse)
    {
        var key = string.IsNullOrWhiteSpace(sortBy)
            ? (reverse ? "name" : null)
            : sortBy.Trim().ToLowerInvariant();

        if (key is null) return services;

        IOrderedEnumerable<ServiceInfo> Ord<TKey>(Func<ServiceInfo, TKey> k, IComparer<TKey>? c = null)
            => reverse ? services.OrderByDescending(k, c) : services.OrderBy(k, c);

        return key switch
        {
            "displayname" => Ord(s => s.DisplayName, StringComparer.OrdinalIgnoreCase),
            "pid" => Ord(s => s.Pid),
            "status" => Ord(s => s.Status),
            "start" or "start_config" or "startconfig" => Ord(s => s.StartConfig),
            "description" => Ord(s => s.Description, StringComparer.OrdinalIgnoreCase),
            _ => Ord(s => s.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    public static IEnumerable<ServiceInfo> Tail(IEnumerable<ServiceInfo> services, int? tail)
    {
        if (tail is null or <= 0) return services;
        var list = services.ToList();
        return list.Skip(Math.Max(0, list.Count - tail.Value));
    }

    // ── CLI value parsing ─────────────────────────────────────────────────

    public static ServiceStatus? ParseStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Replace(" ", "").ToLowerInvariant() switch
        {
            "running" => ServiceStatus.Running,
            "stopped" => ServiceStatus.Stopped,
            "startpending" => ServiceStatus.StartPending,
            "stoppending" => ServiceStatus.StopPending,
            "paused" => ServiceStatus.Paused,
            "pausepending" => ServiceStatus.PausePending,
            "continuepending" => ServiceStatus.ContinuePending,
            _ => null,
        };
    }

    public static StartType? ParseStartType(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().ToLowerInvariant() switch
        {
            "auto" or "autostart" => StartType.AutoStart,
            "manual" or "demand" or "demandstart" => StartType.DemandStart,
            "disabled" => StartType.Disabled,
            "boot" or "bootstart" => StartType.BootStart,
            "system" or "systemstart" => StartType.SystemStart,
            _ => null,
        };
    }
}
