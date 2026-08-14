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
- Update docs/IMPLEMENTATION_PROGRESS.md after meaningful work.
- Keep .env.local local and uncommitted.
- .env.example is the committed template.

Current planned milestones:
1. Scaffold C# Jellyfin plugin project targeting the user's Jellyfin version.
2. Add plugin entry point, configuration model, and config page.
3. Capture playback events and persist watch history.
4. Read Jellyfin user data such as Rating, Likes, IsFavorite, PlayCount, Played, and PlayedPercentage.
5. Index library candidates.
6. Add Douban CSV/JSON import and local cache.
7. Add deterministic pre-ranker and LLM reranker.
8. Create/update the managed recommendation collection.
9. Add admin actions and scheduled refresh.

Local testing:
Use .env.local for Jellyfin URL, API key, test user ID, LLM settings, and Douban import settings. Never commit real secrets.

Engineering constraints:
- Follow existing repo style as it emerges.
- Keep changes scoped.
- Avoid committing generated build output, secrets, or local machine config.
- Prefer deterministic validation around all external/LLM results.
```
