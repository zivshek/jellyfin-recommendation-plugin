# Implementation Progress

Last updated: 2026-08-14

## Current Status

The code-level MVP implementation is complete and builds/tests locally. Live Jellyfin smoke testing remains to confirm plugin loading, dashboard page behavior, playback events, and collection updates against a running server.

## Completed

- Chose the MVP display strategy: managed Jellyfin collection instead of Jellyfin Web homepage modification.
- Captured the implementation plan in `docs/IMPLEMENTATION_PLAN.md`.
- Reviewed `daymade/claude-code-skills` `douban-skill` as a Douban export/sync reference.
- Added `docs/DOUBAN_SKILL_INTEGRATION.md` to track how to incorporate its CSV, RSS, and Frodo ideas without making the Jellyfin plugin depend on Claude Code/Python/Node at runtime.
- Added local environment scaffolding:
  - `.env.example` is committed as a safe template.
  - `.env.local` is ignored and reserved for real local testing values.
- Added `scripts/Load-LocalEnv.ps1` and `scripts/test-jellyfin-status.ps1` for non-mutating local smoke commands that consume `.env.local`.
- Hardened `scripts/test-jellyfin-status.ps1` with duplicated-scheme normalization and actionable unreachable-server errors.
- Added GitHub Actions CI and release workflows that test, package, create release assets, and generate a Jellyfin plugin repository manifest served from GitHub Releases.
- Added a future-conversation prompt in `docs/NEW_CONVERSATION_PROMPT.md`.
- Scaffolded `Jellyfin.Plugin.Recommendations.sln`.
- Added the `Jellyfin.Plugin.Recommendations` C# plugin project targeting `net9.0`.
- Added the Jellyfin plugin entry point, configuration model, and embedded configuration page.
- Added README build/package instructions and local PowerShell scripts.
- Added SQLite persistence under Jellyfin `DataPath/recommendations/recommendations.db`.
- Added schema/table coverage for:
  - `PlaybackEvents`
  - `UserItemStats`
  - `LibraryItems`
  - `ExternalRatings`
  - `DoubanItems`
  - `ItemMatches`
  - `RecommendationRuns`
  - `RecommendationItems`
  - `ManagedCollections`
  - `ManagedCollectionItems`
- Added hosted startup database initialization.
- Added Jellyfin playback event monitor for playback start, progress, and stopped events.
- Added user-data monitor for explicit Jellyfin ratings, likes, favorites, played state, play count, and resume progress.
- Added library candidate indexing for movies and series.
- Added people/director/actor metadata to the library candidate cache.
- Added a Douban import adapter interface, douban-skill-compatible CSV import, and compatible JSON import path.
- Added Douban subject ID extraction, star rating conversion, UTF-8 BOM CSV handling, and local cache persistence.
- Added Douban-to-Jellyfin matching by provider IDs when available, then normalized title/year, with review-required title-only matches.
- Added deterministic recommendation engine and strict recommendation validator.
- Added OpenAI-compatible LLM client with strict JSON output parsing, compact taste/profile metadata, and deterministic fallback.
- Added recommendation orchestration with persisted runs/items.
- Added eligibility validation so LLM output cannot include watched items when watched recommendations are disabled.
- Added managed collection creation/update and stale plugin-managed item removal.
- Added admin API endpoints:
  - `GET /Recommendations/Status`
  - `POST /Recommendations/RebuildIndex`
  - `POST /Recommendations/ImportDouban`
  - `POST /Recommendations/MatchDouban`
  - `POST /Recommendations/Generate`
  - `POST /Recommendations/UpdateCollection`
  - `POST /Recommendations/Refresh`
- Added configuration-page controls and manual action buttons for the MVP flow.
- Added separate admin buttons for test connection, rebuild index, Douban import/match, recommendation generation, collection update, and full refresh.
- Removed self-server URL/API key/test user settings from plugin runtime configuration; manual actions now use a Jellyfin user picker.
- Added per-user managed collection naming with `{username}`/`{user}` template support and a multi-user default.
- Added admin status fields for last library index, last Douban import, and last recommendation run.
- Added admin-page error handling so failed manual actions surface actionable status text.
- Exposed the Recommendations configuration page in Jellyfin's dashboard sidebar under the Plugins section.
- Added a plugin diagnostic log file plus admin-page endpoints/link for opening it.
- Hardened admin-page status/action parsing so string JSON responses do not render `undefined` counts.
- Versioned the internal settings-page route to reduce stale cached Jellyfin Web plugin UI after upgrades.
- Added LLM diagnostic logging for skip reasons, HTTP responses, parsed recommendation counts, fallback path selection, and opt-in raw request/response bodies.
- Added package `meta.json` so the publish output follows Jellyfin plugin manifest conventions.
- Stamped release/local package versions consistently across the manifest, packaged `meta.json`, and plugin assembly.
- Added scheduled refresh task.
- Renamed the scheduled task display text to "Generate and update recommendations" and added plugin-log diagnostics for scheduled runs.
- Added scheduled/full-refresh guard so Douban import is skipped when the Douban provider is disabled.
- Added a minimal LLM request throttle and structured LLM request/response-count logs.
- Added repeatable schema upgrade handling for Douban provider-ID columns.
- Added unit tests for storage, playback aggregation, Douban CSV/JSON parsing/import, provider-ID matching, schema upgrades, recommendation validation/scoring, LLM eligibility validation, and collection diffing.
- Verified `dotnet test Jellyfin.Plugin.Recommendations.sln --configuration Release` succeeds with 24 passing tests.
- Verified `dotnet publish Jellyfin.Plugin.Recommendations/Jellyfin.Plugin.Recommendations.csproj --configuration Release --output artifacts/Recommendations` creates a copyable plugin folder with SQLite dependencies.
- Verified the publish output includes `meta.json`.

## Remaining Live Checks

1. Confirm the target Jellyfin server version is compatible with `Jellyfin.Controller`/`Jellyfin.Model` `10.11.11`.
2. Copy `artifacts/Recommendations` into the Jellyfin plugin folder and restart Jellyfin.
3. Confirm Jellyfin lists the plugin as installed and supported.
4. Confirm the configuration page loads and saves settings.
5. Play/stop a test item and confirm playback history rows are created.
6. Run the admin refresh flow against a real library and confirm the managed collection is created/updated.

## Decisions

- Recommendations must only include existing media from the Jellyfin library.
- Explicit user ratings outrank inferred completion signals in scoring.
- Douban support starts with manual CSV import and local cache.
- The first Douban import target is the douban-skill movie/TV CSV output format.
- Do not shell out to the `douban-skill` scripts from the Jellyfin plugin runtime; port needed sync logic to C# later.
- Uncertain Douban-to-Jellyfin matches are stored with `RequiresReview = true` and do not attach external ratings automatically.
- LLM output is validated against candidate IDs before persistence or collection updates.
- Deterministic ranking is always available as a fallback when the LLM is disabled, fails, or returns no valid IDs.

## Risks

- Live plugin loading can still expose Jellyfin runtime behavior not covered by compile-time packages.
- The admin page JavaScript uses Jellyfin Web plugin-page conventions and needs browser verification in a running server.
- SQLite native dependency loading should be smoke-tested on the deployment OS/container image.
- Native Douban RSS/Frodo sync is intentionally out of MVP scope.

## Local Testing Notes

Use `.env.local` for private local values:

```dotenv
JELLYFIN_BASE_URL=http://localhost:8096
JELLYFIN_API_KEY=
LLM_PROVIDER=openai-compatible
LLM_BASE_URL=
LLM_API_KEY=
LLM_MODEL=
DOUBAN_USER_ID=
DOUBAN_EXPORT_PATH=
DOUBAN_SYNC_PROVIDER=csv
DOUBAN_SYNC_INTERVAL_HOURS=24
SCHEDULED_REFRESH_INTERVAL_HOURS=24
PLUGIN_TEST_DATA_DIR=
```
