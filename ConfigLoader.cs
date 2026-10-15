using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ServiceList.Config;

public static class ConfigLoader
{
    private static readonly string ConfigFileName = "sl-config.json";

    public static AppConfig Load()
    {
        // Search order: current dir → exe dir → user home
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName),
            Path.Combine(AppContext.BaseDirectory, ConfigFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sl", ConfigFileName),
        };

        foreach (var path in searchPaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var builder = new ConfigurationBuilder()
                    .AddJsonFile(path, optional: false, reloadOnChange: false);
                var config = builder.Build();
                var appConfig = new AppConfig();
                config.Bind(appConfig);
                return appConfig;
            }
            catch
            {
                // fall through to default
            }
        }

        return new AppConfig(); // pure defaults
    }

    /// <summary>Write a sample config to disk so users can customize it.</summary>
    public static void WriteSampleConfig(string outputPath)
    {
        var sample = new AppConfig();
        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
    }
}
