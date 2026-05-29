using System.Globalization;
using System.Text.Json;
using SimklJellyseerrSync;

Console.OutputEncoding = System.Text.Encoding.UTF8;

AppConfig config;
try { config = AppConfig.Load(); }
catch (Exception ex) { Console.Error.WriteLine($"Config error: {ex.Message}"); return 1; }

var accessToken = config.LoadToken();
if (string.IsNullOrWhiteSpace(accessToken))
{
    accessToken = await SimklAuth.AuthenticateAsync(config.SimklClientId, config.SimklClientSecret);
    config.SaveToken(accessToken);
}

var simkl = new SimklClient(config, accessToken);
var jellyseerr = new JellyseerrClient(config);

Console.WriteLine($"SIMKL → Jellyseerr sync started  |  interval: {config.SyncIntervalMinutes}min  |  statuses: {string.Join(", ", config.SyncStatuses)}");
if (config.DryRun) Console.WriteLine("DRY RUN — no requests will be submitted");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.Token.IsCancellationRequested)
{
    try { await RunSyncAsync(config, simkl, jellyseerr, cts.Token); }
    catch (Exception ex) when (!cts.Token.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Sync error: {ex.Message}");
    }

    if (cts.Token.IsCancellationRequested) break;

    try { await Task.Delay(TimeSpan.FromMinutes(config.SyncIntervalMinutes), cts.Token); }
    catch (TaskCanceledException) { break; }
}

Console.WriteLine("Sync stopped.");
return 0;

static SyncState LoadState(string path)
{
    if (!File.Exists(path)) return new SyncState();

    try
    {
        var state = JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path)) ?? new SyncState();
        state.Cursors = new Dictionary<string, string>(state.Cursors ?? new(), StringComparer.OrdinalIgnoreCase);
        state.Tracked = new Dictionary<string, TrackedItem>(state.Tracked ?? new(), StringComparer.OrdinalIgnoreCase);

        // Migrate legacy "Requested" string list -> Tracked records.
        if (state.Requested is { Count: > 0 })
        {
            foreach (var key in state.Requested)
                state.Tracked.TryAdd(key, new TrackedItem());
            state.Requested = null;
        }

        return state;
    }
    catch
    {
        return new SyncState();
    }
}

static void SaveState(string path, SyncState state) =>
    File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));

static async Task RunSyncAsync(
    AppConfig config, SimklClient simkl, JellyseerrClient jellyseerr, CancellationToken ct)
{
    var state = LoadState(config.SyncStatePath);
    var tracked = state.Tracked;
    var activities = await simkl.GetActivitiesAsync();

    var types = new List<(string ActivityType, string UrlType, string JellyseerrType)>
    {
        ("movies", "movie", "movie"),
        ("tv_shows", "tv", "tv")
    };
    if (config.SyncAnime) types.Add(("anime", "anime", "tv"));

    var requestStatuses = new HashSet<string>(config.SyncStatuses, StringComparer.OrdinalIgnoreCase);
    var changedAny = false;
    var totalCreated = 0;
    var totalTracked = 0;
    var totalDeleted = 0;
    var totalErrors = 0;

    foreach (var (activityType, urlType, jellyseerrType) in types)
    {
        var current = activities.AllFor(activityType);
        if (current is null) continue;

        DateTime? last = null;
        if (state.Cursors.TryGetValue(activityType, out var cursor) &&
            DateTime.TryParse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            last = parsed;
        }

        if (last is not null && current <= last) continue;

        changedAny = true;
        Console.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {activityType} changed — syncing...");

        var all = await simkl.GetAllItemsAsync(urlType, jellyseerrType);
        var present = all.Select(i => i.TmdbId).ToHashSet();
        var desired = all.Where(i => requestStatuses.Contains(i.Status)).ToList();
        var dropped = all
            .Where(i => string.Equals(i.Status, "dropped", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.TmdbId)
            .ToHashSet();

        Console.WriteLine($"  {desired.Count} desired / {all.Count} total {activityType}; {dropped.Count} dropped");

        foreach (var item in desired)
        {
            if (ct.IsCancellationRequested) break;

            var key = $"{urlType}:{item.TmdbId}";
            if (tracked.ContainsKey(key)) continue;

            try
            {
                var seasons = new List<int>();
                var seasonDesc = "";
                if (item.MediaType != "movie")
                {
                    var available = await jellyseerr.GetSeasonNumbersAsync(item.TmdbId);
                    (seasons, seasonDesc) = SeasonSelector.Select(item.Memo, available);
                }

                var seasonNote = item.MediaType == "movie"
                    ? ""
                    : $" seasons[{string.Join(",", seasons)}] ({seasonDesc})";

                var mediaStatus = await jellyseerr.GetMediaStatusAsync(item.TmdbId, item.MediaType);
                if (mediaStatus > MediaStatus.Unknown)
                {
                    tracked[key] = new TrackedItem { Memo = item.Memo };
                    totalTracked++;
                    Console.WriteLine($"  SKIP  [{item.MediaType,-5}] {item.Title} ({item.Year}) — already in Jellyseerr ({MediaStatus.Name(mediaStatus)}); tracking {key}");
                }
                else if (config.DryRun)
                {
                    tracked[key] = new TrackedItem { Memo = item.Memo };
                    totalTracked++;
                    Console.WriteLine($"  DRYRUN [{item.MediaType,-5}] {item.Title} ({item.Year}){seasonNote} — would request; tracking {key}");
                }
                else
                {
                    var created = await jellyseerr.RequestMediaAsync(item, seasons);
                    tracked[key] = new TrackedItem { Memo = item.Memo };
                    if (created)
                    {
                        totalCreated++;
                        Console.WriteLine($"  REQ   [{item.MediaType,-5}] {item.Title} ({item.Year}){seasonNote} — tracking {key}");
                    }
                    else
                    {
                        totalTracked++;
                        Console.WriteLine($"  SKIP  [{item.MediaType,-5}] {item.Title} ({item.Year}) — already requested; tracking {key}");
                    }
                }
            }
            catch (Exception ex)
            {
                totalErrors++;
                Console.Error.WriteLine($"  ERR   [{item.MediaType,-5}] {item.Title}: {ex.Message}");
            }

            await Task.Delay(300, ct);
        }

        var prefix = $"{urlType}:";
        foreach (var key in tracked.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (ct.IsCancellationRequested) break;
            if (!int.TryParse(key[prefix.Length..], out var tmdbId)) continue;
            if (present.Contains(tmdbId) && !dropped.Contains(tmdbId)) continue;

            try
            {
                if (config.DryRun)
                {
                    Console.WriteLine($"  DRYRUN DEL [{jellyseerrType,-5}] {tmdbId} — would delete requests for {key}");
                }
                else
                {
                    var deleted = await jellyseerr.DeleteRequestsAsync(tmdbId, jellyseerrType);
                    totalDeleted += deleted;
                    Console.WriteLine($"  DEL   [{jellyseerrType,-5}] {tmdbId} — deleted {deleted} request(s) for {key}");
                }

                tracked.Remove(key);
            }
            catch (Exception ex)
            {
                totalErrors++;
                Console.Error.WriteLine($"  ERR   [{jellyseerrType,-5}] {tmdbId}: {ex.Message}");
            }

            await Task.Delay(300, ct);
        }

        state.Cursors[activityType] = current.Value.ToString("o");
    }

    if (!changedAny)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No changes — cursors match SIMKL activities");
    }
    else
    {
        Console.WriteLine($"  Done — {totalCreated} requested, {totalTracked} tracked/skipped, {totalDeleted} deleted, {totalErrors} errors");
    }

    await RunAvailabilityScanAsync(config, simkl, jellyseerr, state, ct);

    SaveState(config.SyncStatePath, state);
}

static async Task RunAvailabilityScanAsync(
    AppConfig config, SimklClient simkl, JellyseerrClient jellyseerr, SyncState state, CancellationToken ct)
{
    if (!config.MarkReadyToWatch || config.AvailabilityCheckHours <= 0) return;

    var now = DateTime.UtcNow;
    if (state.LastAvailabilityScan is { } lastScan &&
        now - lastScan < TimeSpan.FromHours(config.AvailabilityCheckHours))
    {
        return;
    }

    var pending = state.Tracked.Where(kv => !kv.Value.ReadyMarked).ToList();
    if (pending.Count == 0)
    {
        state.LastAvailabilityScan = now;
        return;
    }

    Console.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Availability scan — checking {pending.Count} tracked item(s) for completed downloads...");
    var marked = 0;
    var errors = 0;

    foreach (var (key, info) in pending)
    {
        if (ct.IsCancellationRequested) break;

        var sep = key.IndexOf(':');
        if (sep < 0) continue;
        var urlPrefix = key[..sep];
        if (!int.TryParse(key[(sep + 1)..], out var tmdbId)) continue;
        var mediaType = urlPrefix == "movie" ? "movie" : "tv";

        try
        {
            var status = await jellyseerr.GetMediaStatusAsync(tmdbId, mediaType);
            if (status != MediaStatus.Available) continue;

            if (ReadyMemo.AlreadyMarked(info.Memo, config.ReadyMemoPrefix))
            {
                info.ReadyMarked = true;
                continue;
            }

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var newMemo = ReadyMemo.Merge(info.Memo, config.ReadyMemoPrefix, date);

            if (config.DryRun)
            {
                Console.WriteLine($"  DRYRUN RDY [{mediaType,-5}] {tmdbId} — would set memo \"{newMemo.Replace("\n", " | ")}\"");
                continue;
            }

            await simkl.WriteMemoAsync(tmdbId, mediaType, newMemo);
            info.Memo = newMemo;
            info.ReadyMarked = true;
            info.ReadyDate = date;
            marked++;
            Console.WriteLine($"  RDY   [{mediaType,-5}] {tmdbId} — marked {config.ReadyMemoPrefix} {date}");
        }
        catch (Exception ex)
        {
            errors++;
            Console.Error.WriteLine($"  ERR   [{mediaType,-5}] {tmdbId}: {ex.Message}");
        }

        await Task.Delay(300, ct);
    }

    state.LastAvailabilityScan = now;
    Console.WriteLine($"  Availability scan done — {marked} marked {config.ReadyMemoPrefix}, {errors} errors");
}

class SyncState
{
    public Dictionary<string, string> Cursors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TrackedItem> Tracked { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastAvailabilityScan { get; set; }

    // Legacy field kept for one-time migration from older state files.
    public List<string>? Requested { get; set; }
}

class TrackedItem
{
    public string? Memo { get; set; }
    public bool ReadyMarked { get; set; }
    public string? ReadyDate { get; set; }
}
