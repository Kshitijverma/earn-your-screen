using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypedPond.Core;

/// <summary>
/// Application configuration loaded from a JSON file (appsettings.json).
/// </summary>
public class Config
{
    /// <summary>Daily step goal required to unlock. Default 10000.</summary>
    public int StepGoal { get; set; } = 10000;

    /// <summary>Firebase Realtime Database base URL (e.g. https://myapp.firebaseio.com).</summary>
    public string FirebaseUrl { get; set; } = string.Empty;

    /// <summary>Firebase API key used for authenticated REST reads.</summary>
    public string FirebaseApiKey { get; set; } = string.Empty;

    /// <summary>Firebase UID whose step data is read.</summary>
    public string FirebaseUserId { get; set; } = string.Empty;

    /// <summary>Shared secret for HMAC validation of local HTTP push updates.</summary>
    public string HmacSecret { get; set; } = string.Empty;

    /// <summary>Port for the local HTTP listener. Default 8787.</summary>
    public int LocalHttpPort { get; set; } = 8787;

    /// <summary>How often to poll Firebase for step data, in seconds. Default 300.</summary>
    public int FirebasePollIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Hour of the local day (0-23) after which the "use yesterday's data"
    /// grace unlock is allowed when today's data is genuinely unavailable.
    /// Before this hour, a day with no recorded steps stays locked (a fresh day
    /// has no data yet — that is not the same as the phone being offline).
    /// Default 12 (noon). Set to 24 to disable the yesterday fallback entirely.
    /// </summary>
    public int FallbackAfterHour { get; set; } = 12;

    /// <summary>Directory where the SQLite database lives. Defaults to the app directory.</summary>
    public string DataDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>Full path to the lock screen executable launched by the service.</summary>
    public string LockScreenExePath { get; set; } =
        @"C:\Program Files\TypedPond\TypedPond.LockScreen.exe";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads configuration from the given JSON file path.
    /// Missing properties fall back to their defaults.
    /// </summary>
    public static Config Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}", path);
        }

        string json = File.ReadAllText(path);
        Config? config = JsonSerializer.Deserialize<Config>(json, SerializerOptions);
        if (config is null)
        {
            throw new InvalidOperationException($"Configuration file could not be parsed: {path}");
        }

        if (string.IsNullOrWhiteSpace(config.DataDirectory))
        {
            config.DataDirectory = AppContext.BaseDirectory;
        }

        return config;
    }

    /// <summary>
    /// Loads configuration from appsettings.json in the application directory.
    /// </summary>
    public static Config LoadDefault()
    {
        return Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
    }
}
