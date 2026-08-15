# jellyfin-recommendation-plugin

A Jellyfin server plugin experiment for LLM-assisted recommendations from existing library media.

The MVP plan is to track local watch history, enrich it with explicit Jellyfin ratings and optional Douban ratings, generate recommendations, and publish them through a managed Jellyfin collection.

## Current Capabilities

- Tracks playback start/progress/stop events into local SQLite storage.
- Syncs explicit Jellyfin ratings, likes, favorites, play counts, played state, and resume progress.
- Indexes movie and series library candidates.
- Imports douban-skill movie/TV CSV exports plus compatible JSON exports, and matches confident provider-ID or title/year rows to Jellyfin items.
- Generates recommendations with a deterministic fallback and optional OpenAI-compatible LLM reranking.
- Validates recommendations against existing candidate IDs before saving them.
- Rejects watched LLM recommendations unless watched items are explicitly enabled.
- Skips scheduled Douban imports when the Douban provider is disabled.
- Creates and updates per-user managed recommendation collections.
- Uses a Jellyfin user picker for manual per-user actions; the plugin does not need a Jellyfin URL or API key at runtime.
- Provides admin API actions, configuration-page buttons, and a scheduled refresh task.
- Includes unit tests for storage, playback aggregation, Douban import/matching, validation, scoring, and collection diffing.

## Build

This repository currently targets Jellyfin 10.11.x and builds against `Jellyfin.Controller`/`Jellyfin.Model` `10.11.11`.

```powershell
dotnet restore .\Jellyfin.Plugin.Recommendations.sln
dotnet build .\Jellyfin.Plugin.Recommendations.sln --configuration Release
dotnet test .\Jellyfin.Plugin.Recommendations.sln --configuration Release
```

Or run:

```powershell
.\scripts\build.ps1
```

Local smoke scripts read private values from `.env.local` when present:

```powershell
Copy-Item .\.env.example .\.env.local
.\scripts\test-jellyfin-status.ps1
```

To collect a copyable plugin folder with SQLite dependencies, run:

```powershell
.\scripts\package.ps1
```

The plugin package is emitted under:

```text
artifacts\Recommendations\
```

The package includes `meta.json`, the plugin DLL, and required SQLite dependencies.

To build the ZIP and Jellyfin plugin repository manifest used by releases, run:

```powershell
.\scripts\package-release.ps1 -Repository zivshek/jellyfin-recommendation-plugin -Tag v0.1.0
```

This emits:

```text
artifacts\repository\Jellyfin.Plugin.Recommendations_0.1.0.0.zip
artifacts\repository\manifest.json
```

## Install From Plugin Repository

After a tagged GitHub release workflow has run, add this repository URL in Jellyfin under **Dashboard -> Plugins -> Repositories**:

```text
https://github.com/zivshek/jellyfin-recommendation-plugin/releases/latest/download/manifest.json
```

Then open **Catalog**, install **Recommendations**, and restart Jellyfin if prompted.

To publish a new catalog release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow runs tests, creates the plugin ZIP, and uploads `manifest.json` plus the ZIP to the GitHub Release.

For local Jellyfin testing, copy the built plugin files into a subfolder of your Jellyfin plugin directory, then restart Jellyfin. The direct-install default on Windows is:

```text
%UserProfile%\AppData\Local\jellyfin\plugins\Recommendations
```

## Planning

- [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
- [Implementation progress](docs/IMPLEMENTATION_PROGRESS.md)
- [New conversation prompt](docs/NEW_CONVERSATION_PROMPT.md)
