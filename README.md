# simkl-jellyseerr-sync

Automatically syncs your [SIMKL](https://simkl.com) watchlist and "Plan to Watch" items to [Jellyseerr](https://github.com/Fallenbagel/jellyseerr) as requests — so anything you mark on SIMKL gets downloaded to your media server automatically.

- ✅ Syncs movies and TV shows (and optionally anime)
- ✅ Skips items already available, downloading, or requested in Jellyseerr
- ✅ Requests all seasons of a TV show
- ✅ **Smart polling** — uses SIMKL's activities endpoint to only fetch your library when something actually changed, so it can poll every couple of minutes while staying well under SIMKL's 1000 requests/day limit
- ✅ **Removal handling** — if you drop an item or remove it from your list, the matching Jellyseerr request is deleted (downloaded files are kept)
- ✅ Runs on a configurable schedule (default: every 2 minutes)
- ✅ Designed to run as a Docker container alongside your ARR stack

---

## How It Works

1. Calls the SIMKL **activities** endpoint to see whether your movies / TV / anime lists changed since the last check. If nothing changed, it does nothing (cheap, keeps you under the API rate limit).
2. When a list changed, fetches that full library from SIMKL (each item carries its status: `plantowatch`, `watching`, `completed`, `dropped`, etc.).
3. For items matching your configured `SyncStatuses` that aren't already in Jellyseerr, submits a request. Jellyseerr routes it to Radarr (movies) or Sonarr (TV).
4. For items it previously requested that are now **dropped** or **removed from your list**, it deletes the Jellyseerr request (downloaded files are kept).
5. Persists a small `sync_state.json` (change cursors + the set of items it requested) and sleeps for the configured interval, then repeats.

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
| `DryRun` | Set `true` to log what would be requested/deleted without changing anything |

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
  1 desired / 23 total tv_shows; 1 dropped
  REQ   [tv   ] Andor (2022) — tracking tv:200720
  DEL   [tv   ] 198174 — deleted 1 request(s) for tv:198174
  Done — 2 requested, 1 tracked/skipped, 1 deleted, 0 errors
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
