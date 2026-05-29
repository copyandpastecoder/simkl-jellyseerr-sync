# simkl-jellyseerr-sync

Automatically syncs your [SIMKL](https://simkl.com) watchlist and "Plan to Watch" items to [Jellyseerr](https://github.com/Fallenbagel/jellyseerr) as requests — so anything you mark on SIMKL gets downloaded to your media server automatically.

- ✅ Syncs movies and TV shows (and optionally anime)
- ✅ Skips items already available, downloading, or requested in Jellyseerr
- ✅ **Per-season control via SIMKL memos** — request specific seasons of a show (e.g. `1,2,3`, `Last 3`, `Latest`) by typing a directive in the item's SIMKL memo; defaults to the first season
- ✅ **"ReadyToWatch" marking** — when a download finishes (100% available in Jellyseerr), writes a `ReadyToWatch <date>` note to the item's SIMKL memo so you can see what's ready
- ✅ **Smart polling** — uses SIMKL's activities endpoint to only fetch your library when something actually changed, so it can poll every couple of minutes while staying well under SIMKL's 1000 requests/day limit
- ✅ **Removal handling** — if you drop an item or remove it from your list, the matching Jellyseerr request is deleted (downloaded files are kept)
- ✅ Runs on a configurable schedule (default: every 2 minutes)
- ✅ Designed to run as a Docker container alongside your ARR stack

---

## How It Works

1. Calls the SIMKL **activities** endpoint to see whether your movies / TV / anime lists changed since the last check. If nothing changed, it does nothing (cheap, keeps you under the API rate limit).
2. When a list changed, fetches that full library from SIMKL (each item carries its status: `plantowatch`, `watching`, `completed`, `dropped`, etc., plus its memo).
3. For items matching your configured `SyncStatuses` that aren't already in Jellyseerr, submits a request. Jellyseerr routes it to Radarr (movies) or Sonarr (TV). For TV/anime, the seasons requested are chosen from the item's memo (see [Per-Season Control](#per-season-control-via-memos)).
4. For items it previously requested that are now **dropped** or **removed from your list**, it deletes the Jellyseerr request (downloaded files are kept).
5. On a daily pass, it checks each tracked item; once a download is 100% available in Jellyseerr it writes `ReadyToWatch <date>` to the item's SIMKL memo (the item stays in Plan to Watch).
6. Persists a small `sync_state.json` (change cursors + the items it tracks) and sleeps for the configured interval, then repeats.

---

## Per-Season Control via Memos

By default a TV show is requested with **its first season only**. To request specific seasons, type a directive on the **first line** of that show's **memo** in SIMKL (the small note field on each watchlist item).

| Memo first line | Seasons requested |
|---|---|
| *(empty, or a normal note)* | First season only |
| `All` | All seasons |
| `1,2,3` | Seasons 1, 2, 3 |
| `1-3` | Seasons 1 through 3 |
| `First 2` | The first 2 seasons |
| `Last 3` | The last 3 seasons |
| `Latest` | The newest season only |
| `1, Last 2` | Season 1 plus the last 2 |

Rules:
- The first line is treated as a directive **only if it starts with** a number, or `First` / `Last` / `Latest` / `All` (case-insensitive). Otherwise it's treated as a normal note and the show gets its first season.
- Spaces don't matter: `1, 2,3`, ` 1 - 3 `, and `last  2` all work.
- Seasons are clamped to those that actually exist; specials (season 0) are excluded.
- Only the **first line** is read — put any other notes on later lines.

> **Important:** SIMKL has no "memo changed" signal, so the tool reads the memo when the show first enters Plan to Watch. **Set the memo *before* (or at the same time as) adding the show to Plan to Watch.** Editing the memo after the show is already requested won't re-scope the existing request.

---

## ReadyToWatch Marking

When a download finishes (Jellyseerr reports the item — and all requested seasons — 100% available), the tool appends a line to that item's SIMKL memo:

```
ReadyToWatch 2026-05-29
```

- The item **stays in Plan to Watch** (SIMKL has no "owned but unwatched" status; this keeps the status truthful while giving you a visible "it's here" marker).
- Your season directive on line 1 is preserved; the marker is added on its own line. If the combined memo would exceed SIMKL's 140-character limit, only your free-note text is trimmed — never the directive or the marker.
- The date is when it first became available and is set once (not updated on later scans).
- Disable with `"MarkReadyToWatch": false`. Tune frequency with `"AvailabilityCheckHours"` (default 24).

---

## Setup

### 1. Create a SIMKL API App

1. Go to [simkl.com/settings/developer](https://simkl.com/settings/developer)
2. Click **Create New App**
3. Set Redirect URI to: `urn:ietf:wg:oauth:2.0:oob`
4. Copy the **Client ID** and **Client Secret**

### 2. Get Your Jellyseerr API Key

In Jellyseerr → **Settings → General** → copy the **API Key**

### 3. Create the Config File

On your server, create the config directory and copy the example:

```bash
mkdir -p /tank/docker/config/simkl-jellyseerr-sync
cp appsettings.example.json /tank/docker/config/simkl-jellyseerr-sync/appsettings.json
```

Edit `appsettings.json`:

```json
{
  "SimklClientId":     "your-simkl-client-id",
  "SimklClientSecret": "your-simkl-client-secret",
  "JellyseerrUrl":     "http://jellyseerr:5055",
  "JellyseerrApiKey":  "your-jellyseerr-api-key",
  "SyncStatuses":      ["plantowatch"],
  "SyncAnime":         true,
  "SyncIntervalMinutes": 2,
  "MarkReadyToWatch":  true,
  "AvailabilityCheckHours": 24,
  "ReadyMemoPrefix":   "ReadyToWatch",
  "DryRun":            false
}
```

| Field | Description |
|---|---|
| `SimklClientId` | From your SIMKL app |
| `SimklClientSecret` | From your SIMKL app |
| `JellyseerrUrl` | Use container name if on same Docker network (e.g. `http://jellyseerr:5055`) |
| `JellyseerrApiKey` | From Jellyseerr → Settings → General |
| `SyncStatuses` | SIMKL statuses to request: `plantowatch`, `watching`, `hold` |
| `SyncAnime` | Include anime from SIMKL (mapped to TV in Jellyseerr) |
| `SyncIntervalMinutes` | How often to check SIMKL for changes (default `2`) |
| `MarkReadyToWatch` | Write a `ReadyToWatch <date>` memo when a download is fully available (default `true`) |
| `AvailabilityCheckHours` | How often to scan tracked items for completed downloads, in hours (default `24`; `0` disables) |
| `ReadyMemoPrefix` | The marker text written to the memo (default `ReadyToWatch`) |
| `DryRun` | Set `true` to log what would be requested/deleted/marked without changing anything |

### 4. Add to docker-compose.yml

The image is built directly from this GitHub repo on your Docker host — no registry needed. (Your host must have `git` installed so Docker can fetch the build context.)

```yaml
simkl-jellyseerr-sync:
  build: https://github.com/copyandpastecoder/simkl-jellyseerr-sync.git#main
  image: simkl-jellyseerr-sync:local
  container_name: simkl-jellyseerr-sync
  hostname: simkl-jellyseerr-sync.mymediabox
  volumes:
    - /tank/docker/config/simkl-jellyseerr-sync:/config
  restart: unless-stopped
  networks:
    - media_net
  depends_on:
    - jellyseerr
```

To **update** later, just rebuild from the latest `main` and recreate:

```bash
docker compose build --pull simkl-jellyseerr-sync && docker compose up -d simkl-jellyseerr-sync
```

### 5. First Run — SIMKL PIN Auth

On the very first run the app needs to authorize with your SIMKL account:

```bash
docker compose up simkl-jellyseerr-sync
```

Watch the logs — you'll see:

```
══════════════════════════════════════════
  SIMKL PIN: XXXXXX
  Visit: https://simkl.com/pin
══════════════════════════════════════════
```

Enter the PIN at [simkl.com/pin](https://simkl.com/pin). The token is saved to `/tank/docker/config/simkl-jellyseerr-sync/simkl_token.txt` and reused on every subsequent run.

### 6. Run in Background

```bash
docker compose up -d simkl-jellyseerr-sync
docker compose logs -f simkl-jellyseerr-sync
```

---

## Example Output

```
SIMKL → Jellyseerr sync started  |  interval: 2min  |  statuses: plantowatch

[2026-05-29 12:00:00] movies changed — syncing...
  1 desired / 133 total movies; 0 dropped
  REQ   [movie] The Wizard of the Kremlin (2025) — tracking movie:1291659
  SKIP  [movie] Inception (2010) — already in Jellyseerr (available)
[2026-05-29 12:00:00] tv_shows changed — syncing...
  2 desired / 23 total tv_shows; 1 dropped
  REQ   [tv   ] Andor (2022) seasons[1,2] (Last 2) — tracking tv:200720
  REQ   [tv   ] Severance (2022) seasons[1] (default (first season)) — tracking tv:95396
  DEL   [tv   ] 198174 — deleted 1 request(s) for tv:198174
  Done — 3 requested, 1 tracked/skipped, 1 deleted, 0 errors

[2026-05-30 12:00:00] Availability scan — checking 4 tracked item(s) for completed downloads...
  RDY   [movie] 1291659 — marked ReadyToWatch 2026-05-30
  Availability scan done — 1 marked ReadyToWatch, 0 errors
```

When nothing has changed since the last check, you'll simply see:

```
[12:02:00] No changes — cursors match SIMKL activities
```

---

## DryRun Mode

Set `"DryRun": true` in `appsettings.json` to preview what would be requested without submitting anything to Jellyseerr. Useful for testing.

---

## Building Locally

```bash
# CONFIG_DIR tells the app where to find appsettings.json / simkl_token.txt
CONFIG_DIR=/tank/docker/config/simkl-jellyseerr-sync dotnet run
```

Or publish a standalone binary:

```bash
# Linux x64 (for Docker/OMV)
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux-x64

# Windows x64
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
```

### Build the Docker image yourself

If you prefer to build from a local checkout rather than the git URL:

```bash
docker build -t simkl-jellyseerr-sync:local .
docker compose up -d simkl-jellyseerr-sync
```

---

## License

This project is licensed under a custom **personal-use-only** license (adapted from PolyForm Strict 1.0.0). Personal use by an individual is permitted; **commercial, corporate, organizational, and government use are not**. See [LICENSE.md](LICENSE.md).
