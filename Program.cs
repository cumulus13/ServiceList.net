#define WINDOWS_ONLY

using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
#if WINDOWS_ONLY
using System.Management;
using System.ServiceProcess;
#endif

namespace ServiceList
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunOptionsAndReturnExitCode(opts))
                .WithNotParsed<Options>((errs) => HandleParseError(errs));
        }

#if WINDOWS_ONLY
        static void RunOptionsAndReturnExitCode(Options opts)
        {
            var services = ServiceController.GetServices().ToList();

            // Apply filters and actions based on options
            services = FilterServices(services, opts);

            DisplayServices(services, opts);
        }

        static List<ServiceController> FilterServices(List<ServiceController> services, Options opts)
        {
            if (!string.IsNullOrEmpty(opts.Filter))
            {
                services = services.Where(s => s.ServiceName.Contains(opts.Filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (opts.Start.Any())
            {
                ControlServices(services, opts.Start, ServiceControllerStatus.Stopped, s => s.Start());
            }
            if (opts.Stop.Any())
            {
                ControlServices(services, opts.Stop, ServiceControllerStatus.Running, s => s.Stop());
            }
            if (opts.Pause.Any())
            {
                ControlServices(services, opts.Pause, ServiceControllerStatus.Running, s => s.Pause());
            }
            if (opts.Kill.Any())
            {
                KillServices(services, opts.Kill);
            }
            if (opts.Restart.Any())
            {
                RestartServices(services, opts.Restart);
            }

            return services;
        }

        static void ControlServices(List<ServiceController> services, IEnumerable<string> targets, ServiceControllerStatus requiredStatus, Action<ServiceController> action)
        {
            foreach (var target in targets)
            {
                try
                {
                    var service = services.FirstOrDefault(s => s.ServiceName.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (service != null && service.Status == requiredStatus)
                    {
                        action(service);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Error controlling service {target}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error controlling service {target}: {ex.Message}");
                }
            }
        }

        static void KillServices(List<ServiceController> services, IEnumerable<string> targets)
        {
            foreach (var target in targets)
            {
                try
                {
                    var service = services.FirstOrDefault(s => s.ServiceName.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (service != null)
                    {
                        var processId = GetProcessId(service);
                        if (processId != 0)
                        {
                            var process = System.Diagnostics.Process.GetProcessById(processId);
                            process.Kill();
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Error killing service {target}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error killing service {target}: {ex.Message}");
                }
            }
        }

        static int GetProcessId(ServiceController service)
        {
            var wmiQuery = $"SELECT ProcessId FROM Win32_Service WHERE Name = '{service.ServiceName}'";
            using (var searcher = new ManagementObjectSearcher(wmiQuery))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    return Convert.ToInt32(obj["ProcessId"]);
                }
            }
            return 0;
        }

        static void RestartServices(List<ServiceController> services, IEnumerable<string> targets)
        {
            foreach (var target in targets)
            {
                try
                {
                    var service = services.FirstOrDefault(s => s.ServiceName.Equals(target, StringComparison.OrdinalIgnoreCase));
                    if (service != null)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped);
                        service.Start();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Error restarting service {target}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error restarting service {target}: {ex.Message}");
                }
            }
        }

        static void DisplayServices(List<ServiceController> services, Options opts)
        {
            if (opts.SortBy != null)
            {
                services = opts.SortBy.ToLower() switch
                {
                    "name" => services.OrderBy(s => s.ServiceName).ToList(),
                    "pid" => services.OrderBy(s => GetProcessId(s)).ToList(),
                    "status" => services.OrderBy(s => s.Status).ToList(),
                    "start" => services.OrderBy(s => s.StartType).ToList(),
                    _ => services
                };
            }

            int terminalWidth = Console.WindowWidth;
            if (terminalWidth < 131)
            {
                foreach (var service in services)
                {
                    Console.WriteLine("NAME           : " + service.ServiceName);
                    Console.WriteLine("DISPLAY_NAME   : " + service.DisplayName);
                    Console.WriteLine("PID            : " + GetProcessId(service));
                    Console.WriteLine("STATUS         : " + service.Status);
                    Console.WriteLine("DESCRIPTIONS   : " + service.Description);
                    Console.WriteLine("START CONFIG   : " + service.StartType);
                    Console.WriteLine("-------------------------------------------------------------------------------------------");
                }
            }
            else
            {
                Console.WriteLine("+----------+------------------------------------------+-------------------------------------------------------+---------------+------------------+------------------------------------------------------------------------------------------+--------------------+");
                Console.WriteLine("|    No    |                   Name                   |                     Display Name                      |      PID      |      STATUS      |                                       DESCRIPTIONS                                       |       START        |");
                Console.WriteLine("+==========+==========================================+=======================================================+===============+==================+==========================================================================================+====================+");

                int counter = 1;
                foreach (var service in services)
                {
                    Console.WriteLine($"| {counter,8} | {service.ServiceName,-40} | {service.DisplayName,-55} | {GetProcessId(service),13} | {service.Status,-16} | {service.Description,-90} | {service.StartType,-20} |");
                    Console.WriteLine("+----------+------------------------------------------+-------------------------------------------------------+---------------+------------------+------------------------------------------------------------------------------------------+--------------------+");
                    counter++;
                }
            }
        }
#endif

        static void HandleParseError(IEnumerable<Error> errs)
        {
            foreach (var error in errs)
            {
                Console.WriteLine(error.ToString());
            }
        }

        public class Options
        {
            public Options()
            {
                Filter = string.Empty;
                FilterStop = string.Empty;
                FilterStart = string.Empty;
                FilterRestart = string.Empty;
                Start = Enumerable.Empty<string>();
                Pause = Enumerable.Empty<string>();
                Stop = Enumerable.Empty<string>();
                Kill = Enumerable.Empty<string>();
                SortBy = string.Empty;
                SortStatusOnly = string.Empty;
                SortStartConfigOnly = string.Empty;
                Restart = Enumerable.Empty<string>();
                Search = Enumerable.Empty<string>();
                SearchStop = Enumerable.Empty<string>();
                SearchStart = Enumerable.Empty<string>();
                SearchPause = Enumerable.Empty<string>();
                SearchKill = Enumerable.Empty<string>();
            }

            [Option('f', "filter", HelpText = "Filter services by name.")]
            public string Filter { get; set; }

            [Option('fs', "filter-stop", HelpText = "Filter the stop.")]
            public string FilterStop { get; set; }

            [Option('fS', "filter-start", HelpText = "Filter the start.")]
            public string FilterStart { get; set; }

            [Option('fr', "filter-restart", HelpText = "Filter the restart.")]
            public string FilterRestart { get; set; }

            [Option('sa', "start", HelpText = "Start services by name or PID.")]
            public IEnumerable<string> Start { get; set; }

            [Option('sp', "pause", HelpText = "Pause services by name or PID.")]
            public IEnumerable<string> Pause { get; set; }

            [Option('st', "stop", HelpText = "Stop services by name or PID.")]
            public IEnumerable<string> Stop { get; set; }

            [Option('k', "kill", HelpText = "Kill process/Hard Stop with name or PID if available.")]
            public IEnumerable<string> Kill { get; set; }

            [Option('S', "sort-by", HelpText = "Sort list by name, pid, status, start.")]
            public string SortBy { get; set; }

            [Option("sort-status-only", HelpText = "Sort list and only show sorted by status.")]
            public string SortStatusOnly { get; set; }

            [Option("sort-start-config-only", HelpText = "Sort list and only show sorted by config start type.")]
            public string SortStartConfigOnly { get; set; }

            [Option('T', "tail", HelpText = "Sort list by time and show last N.")]
            public int Tail { get; set; }

            [Option('z', "fast", HelpText = "Search by the last of list service.")]
            public bool Fast { get; set; }

            [Option('r', "reverse", HelpText = "List reverse.")]
            public bool Reverse { get; set; }

            [Option("recursive", HelpText = "Recursive service and child.")]
            public bool Recursive { get; set; }

            [Option('R', "restart", HelpText = "Restart services.")]
            public IEnumerable<string> Restart { get; set; }

            [Option('F', "fast-list-details", HelpText = "Show all details of service.")]
            public bool FastListDetails { get; set; }

            [Option('x', "search", HelpText = "Search by input (name, pid, status, start, display_name, description).")]
            public IEnumerable<string> Search { get; set; }

            [Option("search-stop", HelpText = "Search by input and stop.")]
            public IEnumerable<string> SearchStop { get; set; }

            [Option("search-start", HelpText = "Search by input and start.")]
            public IEnumerable<string> SearchStart { get; set; }

            [Option("search-pause", HelpText = "Search by input and pause.")]
            public IEnumerable<string> SearchPause { get; set; }

            [Option("search-kill", HelpText = "Search by input and kill by PID if available.")]
            public IEnumerable<string> SearchKill { get; set; }

            [Option('l', "list-details", HelpText = "Show all details of process.")]
            public bool ListDetails { get; set; }
        }
    }
}
