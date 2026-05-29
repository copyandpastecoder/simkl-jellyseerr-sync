using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimklJellyseerrSync;

public class JellyseerrClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JellyseerrClient(AppConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.JellyseerrUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("X-Api-Key", config.JellyseerrApiKey);
    }

    public async Task<int> GetMediaStatusAsync(int tmdbId, string mediaType)
    {
        var resp = await _http.GetAsync(GetMediaEndpoint(tmdbId, mediaType));

        if (resp.StatusCode == HttpStatusCode.NotFound) return MediaStatus.Unknown;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("mediaInfo", out var info) || info.ValueKind == JsonValueKind.Null)
            return MediaStatus.Unknown;

        return info.TryGetProperty("status", out var status) ? status.GetInt32() : MediaStatus.Unknown;
    }

    public async Task<bool> RequestMediaAsync(SimklItem item, IReadOnlyList<int>? seasons = null)
    {
        object body;

        if (item.MediaType == "movie")
        {
            body = new { mediaType = "movie", mediaId = item.TmdbId };
        }
        else
        {
            var seasonList = (seasons is { Count: > 0 } ? seasons : await GetSeasonNumbersAsync(item.TmdbId)).ToArray();
            body = new { mediaType = "tv", mediaId = item.TmdbId, seasons = seasonList };
        }

        var resp = await _http.PostAsJsonAsync("api/v1/request", body, JsonOpts);
        if (resp.StatusCode == HttpStatusCode.Conflict) return false;

        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<int> DeleteRequestsAsync(int tmdbId, string mediaType)
    {
        var resp = await _http.GetAsync(GetMediaEndpoint(tmdbId, mediaType));
        if (resp.StatusCode == HttpStatusCode.NotFound) return 0;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("mediaInfo", out var info) || info.ValueKind == JsonValueKind.Null) return 0;
        if (!info.TryGetProperty("requests", out var requests) || requests.ValueKind != JsonValueKind.Array) return 0;

        var deleted = 0;
        foreach (var request in requests.EnumerateArray())
        {
            if (!request.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id)) continue;

            var deleteResp = await _http.DeleteAsync($"api/v1/request/{id}");
            if (deleteResp.StatusCode == HttpStatusCode.NotFound) continue;
            deleteResp.EnsureSuccessStatusCode();
            deleted++;
        }

        return deleted;
    }

    public async Task<List<int>> GetSeasonNumbersAsync(int tmdbId)
    {
        var resp = await _http.GetAsync($"api/v1/tv/{tmdbId}");
        if (!resp.IsSuccessStatusCode) return new();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array) return new();

        var numbers = new List<int>();
        foreach (var season in seasons.EnumerateArray())
        {
            if (season.TryGetProperty("seasonNumber", out var seasonNumber) &&
                seasonNumber.TryGetInt32(out var number) &&
                number > 0)
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private static string GetMediaEndpoint(int tmdbId, string mediaType) =>
        mediaType == "movie" ? $"api/v1/movie/{tmdbId}" : $"api/v1/tv/{tmdbId}";
}
