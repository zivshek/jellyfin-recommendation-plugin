# New Conversation Prompt

Use this prompt to start a new Codex conversation for this repository.

```text
You are working in F:\dev\jellyfin-recommendation-plugin.

Project goal:
Build a Jellyfin server plugin that tracks watch history, reads explicit Jellyfin ratings/favorites/likes, optionally imports and caches Douban ratings, asks an LLM to recommend existing Jellyfin library items, and maintains a Jellyfin collection with those recommendations.

MVP direction:
- Do not modify Jellyfin Web yet.
- Use a managed Jellyfin collection as the first display surface.
- Recommend only items that already exist in the Jellyfin library.
- Validate every LLM-returned item ID before writing to Jellyfin.
- Keep Douban support optional, cache-first, and isolated behind an adapter.

Important docs:
- Read docs/IMPLEMENTATION_PLAN.md first.
- Read docs/DOUBAN_SKILL_INTEGRATION.md before implementing Douban import or sync.
- Update docs/IMPLEMENTATION_PROGRESS.md after meaningful work.
- Keep .env.local local and uncommitted.
- .env.example is the committed template.

Current status:
- The code-level MVP is implemented.
- `dotnet test Jellyfin.Plugin.Recommendations.sln --configuration Release` passes with 20 tests.
- `dotnet publish Jellyfin.Plugin.Recommendations\Jellyfin.Plugin.Recommendations.csproj --configuration Release --output artifacts\Recommendations` creates a copyable plugin folder with `meta.json` and SQLite dependencies.
- Remaining checks require a running Jellyfin server: install the artifact, confirm the plugin loads, verify the dashboard page, play/stop a test item, and confirm the managed collection updates.

Local testing:
Use .env.local for Jellyfin URL, API key, test user ID, LLM settings, and Douban import settings. Never commit real secrets. `scripts\test-jellyfin-status.ps1` is a read-only smoke probe for the configured Jellyfin URL.

Engineering constraints:
- Follow existing repo style as it emerges.
- Keep changes scoped.
- Avoid committing generated build output, secrets, or local machine config.
- Prefer deterministic validation around all external/LLM results.
- Do not delete anything from the Jellyfin server!
```
