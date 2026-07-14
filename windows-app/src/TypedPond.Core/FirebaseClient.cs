using System.Text.Json;

namespace TypedPond.Core;

/// <summary>
/// Minimal Firebase Realtime Database REST client for reading step data.
/// Returns null on any error rather than throwing.
/// </summary>
public class FirebaseClient : IDisposable
{
    private readonly Config _config;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public FirebaseClient(Config config)
        : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    /// <summary>Constructor allowing an injected HttpClient (e.g. for testing).</summary>
    public FirebaseClient(Config config, HttpClient httpClient)
        : this(config, httpClient, ownsHttpClient: false)
    {
    }

    private FirebaseClient(Config config, HttpClient httpClient, bool ownsHttpClient)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>
    /// Reads today's step count for the configured user.
    /// GET {FirebaseUrl}/users/{uid}/steps/{yyyy-MM-dd}.json?auth={apiKey}
    /// </summary>
    public Task<int?> GetTodayStepsAsync()
    {
        // The Firebase key uses UTC so it always matches the key the Android app
        // writes (which also uses UTC). Both devices share a canonical day
        // boundary regardless of their local time zones — critical because the
        // Firebase path is the fallback used when the phone is away from home
        // and possibly in a different zone than the laptop.
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string url = $"{BaseUrl()}/users/{Uri.EscapeDataString(_config.FirebaseUserId)}/steps/{today}.json?auth={Uri.EscapeDataString(_config.FirebaseApiKey)}";
        return GetIntegerAsync(url, "today's steps");
    }

    /// <summary>
    /// Reads the remotely configured step goal.
    /// GET {FirebaseUrl}/config/stepGoal.json?auth={apiKey}
    /// </summary>
    public Task<int?> GetStepGoalAsync()
    {
        string url = $"{BaseUrl()}/config/stepGoal.json?auth={Uri.EscapeDataString(_config.FirebaseApiKey)}";
        return GetIntegerAsync(url, "step goal");
    }

    private string BaseUrl() => _config.FirebaseUrl.TrimEnd('/');

    private async Task<int?> GetIntegerAsync(string url, string description)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FirebaseClient] Failed to fetch {description}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return null;
            }

            string body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null")
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out int value))
            {
                return value;
            }

            // Tolerate values stored as strings, e.g. "12345".
            if (root.ValueKind == JsonValueKind.String &&
                int.TryParse(root.GetString(), out int parsed))
            {
                return parsed;
            }

            Console.WriteLine($"[FirebaseClient] Unexpected JSON for {description}: {body}");
            return null;
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or TaskCanceledException
                or JsonException
                or InvalidOperationException  // relative/empty URI when FirebaseUrl is unset
                or UriFormatException)        // malformed FirebaseUrl
        {
            Console.WriteLine($"[FirebaseClient] Error fetching {description}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
