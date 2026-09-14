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

2. **Configure App Settings:**
   Update `appsettings.json` (or set environment variables) with your Discord Bot Token:
```
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN"
  }
}
```

3. **Build & Run:**
```
dotnet build
dotnet run --project Singularity.csproj
```
---

## Music Command Reference

| Command | Description |
| :--- | :--- |
| `/play <query>` | Play a track via URL or search query (supports optional `next` flag). |
| `/skip` | Skip the currently playing track. |
| `/stop` | Stop playback and clear the guild queue. |
| `/queue` | Display the top 10 upcoming tracks in queue. |
| `/status` | View real-time playback position, track info, and source URL. |
| `/loop` | Toggle looping on the current song. |
| `/shuffle` | Randomize the order of the current queue. |
| `/leave` | Disconnect the bot from the voice channel. |

---

## Booru Command Reference
| Command | Description |
| :--- | :--- |
| `/e621 <tags>` | Searches for a random post. Supports optional `<type>` and `<rating>` filters.
