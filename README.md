# Singularity

> A high-performance, general-purpose Discord bot built with C# and NetCord.

---

## Features

* **Fast Music:** A music feature capable of YouTube and most major services through YT-DLP.
* **DM Reminders (In Development):** Direct message scheduled alert system.
* **Booru Integration:** Image search through E621.

---

## Architecture & Project Structure

Singularity avoids monolithic layer folders (e.g. putting all commands or models in global directories). Instead, features are organized by **Vertical Slices** under `Features/`:

```text
Singularity/
├── Configuration/               # Application configuration models
├── Features/
│   ├── General/                 # Standard bot utilities (/ping, /info)
│   ├── Music/                   # Self-contained Audio Domain Slice
│   ├── Reminders/               # DM Reminder Slice (Scheduled tasks)
│   └── Booru/                   # Booru Browser Slice
├── Infrastructure/              # Cross-cutting concerns & DI extension methods
└── Program.cs                   # Entry point and bot setup
```

---

## Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
* [FFmpeg](https://ffmpeg.org/)
* [yt-dlp](https://github.com/yt-dlp/yt-dlp)

> These must be available in your system PATH.

---

## Configuration & Setup

1. **Clone the repository:**
```
git clone https://github.com/sunryze-git/Singularity.git
cd Singularity
```

2. **Configure the bot token** (never commit it). Sources, lowest to highest precedence:
   `appsettings.json` (placeholder only), .NET user-secrets, then the `Discord__Token` environment variable.

   For local development, use the per-user secret store (stored outside the repo in
   `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`; keep it `chmod 600`):
```
dotnet user-secrets set "Discord:Token" "<token>" --project Singularity/Singularity.csproj
```
   For servers (systemd/NixOS), use `Discord__Token` in the service's environment file instead.
   Other settings (see below) work the same way: in `appsettings.json` or as environment variables with `__` for nesting.

3. **Build & Run:**
```
dotnet build
dotnet run --project Singularity/Singularity.csproj
dotnet test
```

For a faster-starting standalone binary: `dotnet publish -c Release -o publish` (self-contained, single file, ReadyToRun).
### Nix

`nix build` produces a framework-dependent build (x86_64-linux only); a NixOS
module is exposed as `nixosModules.default`. After changing any `PackageReference`,
regenerate the offline NuGet lockfile with:

```
nix run .#fetch-deps -- deps.json
```

---

## Settings

All optional; defaults are in `MusicOptions` / `BooruOptions`.

| Setting | Default | |
| :--- | :--- | :--- |
| `Music:Normalize` | `true` | EBU R128 loudness normalisation (about 1% of a core per playing stream). |
| `Music:MaxQueueLength` | `500` | Tracks waiting per server. |
| `Music:MaxPlaylistTracks` | `200` | Tracks taken from one playlist. |
| `Music:IdleTimeout` | `00:10:00` | Leave after this long with nothing to play. |
| `Music:AloneTimeout` | `00:01:00` | Leave after this long alone in the voice channel. |
| `Music:PlayCooldown` | `00:00:02` | Per-user cooldown on `/play`. |
| `Music:StreamInfoLifetime` | `00:20:00` | How long a pre-fetched YouTube stream is trusted. |
| `Music:ResolverOrder` | `["youtube","ytdlp"]` | Which lookup is tried first. yt-dlp is slower but survives YouTube changes. |
| `Booru:Cooldown` | `00:00:03` | Per-user cooldown on `/e621`. |

---

## Errors

Problems are sorted by who can do something about them, and each goes to the right place:

| Kind | Examples | The person who ran the command sees | Logged as | Counted as |
| :--- | :--- | :--- | :--- | :--- |
| Expected (user-facing) | nothing found, queue full, not in voice, e621 rate-limiting | the specific reason | Info (Warning if there is an underlying cause) | `<command>.user` |
| Refused | failed permission or voice-channel check | the check's message, privately | Info | `<command>.refused` |
| Track trouble while playing | private video, download failed, undecodable audio | a message in the text channel saying why | Info/Warning/Error | `playback.*` |
| Bugs and outages | anything unexpected | a generic apology only (never internals) | Error with stack trace | `<command>.unexpected`, `playback.unexpected` |

Startup problems (missing token, ffmpeg/yt-dlp/libopus) print one clear line and exit with status 78; any other fatal error exits 1 after logging at Critical. Unhandled and unobserved task exceptions are logged and counted (`unhandled.*`).

The dashboard's **Errors** panel shows the counts by type. Note that errors in a deferred reply (`/play`, `/e621`) are posted in the channel, because Discord fixes a deferred reply's visibility when it is deferred.

## Metrics

All statistics are standard `System.Diagnostics.Metrics` instruments on the meter `Singularity` (names like `singularity.tracks.started`, `singularity.errors`), so any collector can read them, for example `dotnet-counters monitor --name Singularity Singularity`, or OpenTelemetry. The dashboard is just one consumer. Tags are low-cardinality (command name, resolver, error type); no instrument carries a guild, channel or user.

---

## Logging & Dashboard

**Console logs** use a compact, coloured one-line format (`NO_COLOR=1` disables colour; `LOG_FORMAT=json` switches to JSON lines for log shippers). Noisy framework and library output is
hidden by default; override any level with the usual settings, e.g. `Logging__LogLevel__Singularity=Debug` or `Logging__LogLevel__NetCord=Information`.
Guild/user context appears after each line.

**Dashboard:** a read-only status page at <http://127.0.0.1:8787/> (loopback only by default) showing aggregate statistics: uptime, connected servers (a count), voice sessions, queued and
played tracks, errors, command usage, resolver stats, CPU/memory and a feed of recent events. It never shows guild, channel or user identifiers: only totals, and log lines with long numeric IDs removed.

| Setting | Default | |
| :--- | :--- | :--- |
| `Dashboard:Enabled` | `true` | Set `false` to turn it off. |
| `Dashboard:Port` | `8787` | |
| `Dashboard:Host` | `127.0.0.1` | Any other address requires `Dashboard:Token`; open the page with `?key=<token>`. |
| `Dashboard:Token` | _(none)_ | Bearer token (`Authorization: Bearer …` or `?key=`). |

JSON is available at `/api/stats` and a health check at `/healthz`.

---

## Music Command Reference

| Command | Description |
| :--- | :--- |
| `/play <query>` | Play a track via URL or search query (supports optional `next` flag). |
| `/skip` | Skip the current track. The requester, someone with Move Members, or whoever is alone with the bot skips immediately; otherwise it is a majority vote. |
| `/stop` | Stop playback and clear the guild queue (Move Members, or alone with the bot). |
| `/queue [page]` | Display upcoming tracks, 10 per page. |
| `/status` | View real-time playback position, track info, and source URL. |
| `/loop` | Toggle looping on the current song. |
| `/shuffle` | Randomize the order of the current queue. |
| `/leave` | Disconnect the bot from the voice channel (Move Members, or alone with the bot). |

---

## Booru Command Reference
| Command | Description |
| :--- | :--- |
| `/e621 <tags>` | Searches for a random post. Supports optional `<type>` and `<rating>` filters.
