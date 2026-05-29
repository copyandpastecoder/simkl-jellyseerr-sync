using Microsoft.Extensions.Configuration;

namespace SimklJellyseerrSync;

public class AppConfig
{
    public string SimklClientId { get; init; } = "";
    public string SimklClientSecret { get; init; } = "";
    public string JellyseerrUrl { get; init; } = "";
    public string JellyseerrApiKey { get; init; } = "";

    /// <summary>SIMKL statuses to sync. Valid values: plantowatch, watching, hold</summary>
    public List<string> SyncStatuses { get; init; } = new();

    /// <summary>Include anime from SIMKL (mapped to TV in Jellyseerr)</summary>
    public bool SyncAnime { get; init; } = true;

    /// <summary>How often to run the sync (minutes)</summary>
    public int SyncIntervalMinutes { get; init; } = 2;

    /// <summary>Log what would be requested without actually requesting</summary>
    public bool DryRun { get; init; } = false;

    // Resolved at runtime — not in appsettings.json
    public string ConfigDir { get; private set; } = "";

    public static AppConfig Load()
    {
        var configDir = Environment.GetEnvironmentVariable("CONFIG_DIR")
            ?? AppContext.BaseDirectory;

        var config = new ConfigurationBuilder()
            .SetBasePath(configDir)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var appConfig = config.Get<AppConfig>()
            ?? throw new Exception("Failed to parse appsettings.json");

        appConfig.ConfigDir = configDir;

        if (appConfig.SyncStatuses is null)
        {
            typeof(AppConfig).GetProperty(nameof(SyncStatuses))?.SetValue(appConfig, new List<string>());
        }
        if (appConfig.SyncStatuses.Count == 0)
        {
            appConfig.SyncStatuses.Add("plantowatch");
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(appConfig.SimklClientId)) errors.Add("SimklClientId");
        if (string.IsNullOrWhiteSpace(appConfig.SimklClientSecret)) errors.Add("SimklClientSecret");
        if (string.IsNullOrWhiteSpace(appConfig.JellyseerrUrl)) errors.Add("JellyseerrUrl");
        if (string.IsNullOrWhiteSpace(appConfig.JellyseerrApiKey)) errors.Add("JellyseerrApiKey");

        if (errors.Count > 0)
            throw new Exception($"Missing required config: {string.Join(", ", errors)}");

        return appConfig;
    }

    public string TokenFilePath => Path.Combine(ConfigDir, "simkl_token.txt");
    public string SyncStatePath => Path.Combine(ConfigDir, "sync_state.json");

    public string? LoadToken()
    {
        if (!File.Exists(TokenFilePath)) return null;
        var token = File.ReadAllText(TokenFilePath).Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public void SaveToken(string token) => File.WriteAllText(TokenFilePath, token);
}
