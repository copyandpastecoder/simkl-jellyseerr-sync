using System.Net.Http.Headers;
using System.Text.Json;

namespace SimklJellyseerrSync;

public class SimklClient(AppConfig config, string accessToken)
{
    private const string BaseUrl = "https://api.simkl.com";
    private readonly HttpClient _http = BuildClient(accessToken);

    private static HttpClient BuildClient(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    public async Task<SimklActivities> GetActivitiesAsync()
    {
        var url = $"{BaseUrl}/sync/activities?client_id={Uri.EscapeDataString(config.SimklClientId)}";
        var json = await _http.GetStringAsync(url);
        var activities = new SimklActivities();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return activities;

        activities.Set("movies", ParseActivityAll(root, "movies"));
        activities.Set("tv_shows", ParseActivityAll(root, "tv_shows"));
        activities.Set("anime", ParseActivityAll(root, "anime"));
        return activities;
    }

    public async Task<List<SimklItem>> GetAllItemsAsync(string urlType, string jellyseerrType)
    {
        var url = $"{BaseUrl}/sync/all-items/{Uri.EscapeDataString(urlType)}?client_id={Uri.EscapeDataString(config.SimklClientId)}&extended=full&memos=yes";
        var json = await _http.GetStringAsync(url);
        var items = new List<SimklItem>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return items;

        if (!TryGetFirstArray(root, ["movies", "shows", "anime"], out var entries)) return items;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetFirstObject(entry, ["movie", "show", "anime"], out var media)) continue;

            var tmdbId = ParseTmdbId(media);
            if (tmdbId is null) continue;

            var title = media.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "" : "";
            var year = media.TryGetProperty("year", out var yearElement) ? ParseYear(yearElement) : 0;
            var status = entry.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "" : "";
            var memo = ParseMemo(entry);
            items.Add(new SimklItem(title, year, tmdbId.Value, jellyseerrType, status, memo));
        }

        return items;
    }

    public async Task WriteMemoAsync(int tmdbId, string mediaType, string memoText)
    {
        var url = $"{BaseUrl}/sync/history?client_id={Uri.EscapeDataString(config.SimklClientId)}";
        var item = new Dictionary<string, object?>
        {
            ["ids"] = new Dictionary<string, object?> { ["tmdb"] = tmdbId },
            ["status"] = "plantowatch",
            ["memo"] = new Dictionary<string, object?> { ["text"] = memoText, ["is_private"] = true },
        };
        var arrayKey = mediaType == "movie" ? "movies" : "shows";
        var body = new Dictionary<string, object?> { [arrayKey] = new[] { item } };

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content);
        resp.EnsureSuccessStatusCode();
    }

    private static string? ParseMemo(JsonElement entry)
    {
        if (!entry.TryGetProperty("memo", out var memo) || memo.ValueKind != JsonValueKind.Object) return null;
        if (!memo.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) return null;
        var value = text.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryGetFirstArray(JsonElement root, string[] names, out JsonElement array)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array) return true;
        }

        array = default;
        return false;
    }

    private static bool TryGetFirstObject(JsonElement root, string[] names, out JsonElement obj)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out obj) && obj.ValueKind == JsonValueKind.Object) return true;
        }

        obj = default;
        return false;
    }

    private static DateTime? ParseActivityAll(JsonElement root, string section)
    {
        if (!root.TryGetProperty(section, out var sec)) return null;
        if (sec.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (!sec.TryGetProperty("all", out var all)) return null;
        if (all.ValueKind == JsonValueKind.Null) return null;
        return DateTime.TryParse(all.GetString(), out var dt) ? dt.ToUniversalTime() : null;
    }

    private static int ParseYear(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var year)) return year;
        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed)) return parsed;
        return 0;
    }

    private static int? ParseTmdbId(JsonElement element)
    {
        if (!element.TryGetProperty("ids", out var ids)) return null;
        if (!ids.TryGetProperty("tmdb", out var tmdb)) return null;
        if (tmdb.ValueKind == JsonValueKind.Number && tmdb.TryGetInt32(out var number)) return number;
        if (tmdb.ValueKind == JsonValueKind.String && int.TryParse(tmdb.GetString(), out var parsed)) return parsed;
        return null;
    }
}
