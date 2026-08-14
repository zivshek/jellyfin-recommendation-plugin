# Jellyfin Recommendation Plugin Implementation Plan

## Goal

Build a Jellyfin server plugin that tracks user watch history, enriches taste signals with explicit Jellyfin and optional Douban ratings, asks an LLM to recommend existing library items, and keeps those recommendations visible through a managed Jellyfin collection.

The MVP should avoid Jellyfin Web modification. Recommendations will appear as normal Jellyfin collections, which is stable and client-compatible.

## Guiding Decisions

- Use a Jellyfin collection as the first UI surface.
- Keep recommendations per user.
- Recommend only existing Jellyfin library items.
- Treat explicit ratings and favorites as stronger signals than watch completion.
- Keep Douban optional, cached, and isolated behind an adapter.
- Validate every LLM output against the candidate item set before updating Jellyfin.
- Make the plugin useful without Douban or without an LLM by keeping deterministic scoring as a fallback.

## Target Stack

- Jellyfin server plugin in C#.
- Target Jellyfin 10.11.x first.
- Use the Jellyfin plugin template structure.
- Persist plugin state in a local SQLite database under the plugin data path.
- Use hosted services for background sync and scheduled recommendation refreshes.
- Use plugin configuration pages for setup and manual actions.

## Milestone 1: Repository and Plugin Skeleton

Deliverables:

- Solution and project files for `Jellyfin.Plugin.Recommendations`.
- Plugin entry class with ID, name, description, and configuration.
- Plugin configuration model.
- Configuration page scaffold.
- Build instructions in `README.md`.
- Basic CI or local build script once the project compiles.

Acceptance checks:

- `dotnet build` succeeds.
- Plugin package can be copied into a Jellyfin plugin folder.
- Jellyfin lists the plugin as installed and supported.

## Milestone 2: Local Configuration and Secrets

Deliverables:

- `.env.example` committed.
- `.env.local` ignored for local testing.
- Configuration fields for:
  - Jellyfin base URL.
  - Test API key.
  - Test user ID.
  - LLM provider, base URL, API key, and model.
  - Douban user ID or export path.
  - Recommendation collection name.

Acceptance checks:

- No real keys are committed.
- Local test commands can read `.env.local`.
- Plugin runtime secrets are saved through Jellyfin plugin configuration or environment variables, not hardcoded.

## Milestone 3: Watch History Capture

Deliverables:

- Hosted service that subscribes to playback start, progress, and stopped events.
- SQLite tables for playback sessions and user-item aggregates.
- Logic to track:
  - User ID.
  - Item ID.
  - Media type.
  - Started/stopped timestamps.
  - Last played date.
  - Play count.
  - Played percentage.
  - Finished status.
  - Abandoned/short-play hints.

Acceptance checks:

- Playing and stopping an item creates a durable history record.
- Rewatching increments or updates the aggregate.
- Partial plays do not look the same as completed watches.

## Milestone 4: Jellyfin User Taste Signals

Deliverables:

- Read user data for library items.
- Cache explicit Jellyfin signals:
  - `Rating`.
  - `Likes`.
  - `IsFavorite`.
  - `Played`.
  - `PlayCount`.
  - `PlayedPercentage`.
  - `LastPlayedDate`.
- Merge these into the local user-item aggregate.

Taste weighting draft:

- Explicit rating: strongest signal.
- Favorite: strong positive signal.
- Like: strong positive signal.
- Multiple complete watches: strong positive signal.
- Completed once: moderate positive signal.
- Stopped very early and not resumed: weak negative signal.
- Watched but rated low: strong negative signal.

Acceptance checks:

- Items with explicit high ratings rank as strong positive examples.
- Items with low ratings are not treated as liked merely because they were completed.

## Milestone 5: Library Candidate Index

Deliverables:

- Index candidate library items for movies and shows.
- Cache fields useful for recommendations:
  - Jellyfin item ID.
  - Name.
  - Original title.
  - Year.
  - Type.
  - Genres.
  - People/directors/actors.
  - Studios.
  - Overview.
  - Community rating.
  - Provider IDs such as IMDb, TMDb, TVDb.
- Exclude watched items by default, with an option to include loved rewatches.

Acceptance checks:

- Candidate pool contains only playable library items.
- Recommendations cannot point to unavailable media.

## Milestone 6: Douban Import and Cache

Deliverables:

- Douban adapter interface.
- Manual CSV/JSON import provider for MVP.
- Compatibility with `douban-skill` `影视.csv` exports.
- Local cache table for Douban items:
  - Douban subject ID.
  - Title.
  - Original title.
  - Year.
  - Type.
  - User status.
  - User rating.
  - User tags.
  - User comment.
  - Updated timestamp.
  - Source format/version.
- Matching table between Douban subjects and Jellyfin items.

Matching order:

1. IMDb ID exact match when available.
2. TMDb or TVDb exact match when available.
3. Normalized title plus year plus media type.
4. Fuzzy title match requiring manual review above an uncertainty threshold.

Future providers:

- Public Douban feed sync for recent changes.
- Frodo-style full sync behind an explicit experimental flag.
- Native C# port of the useful `douban-skill` Frodo/RSS behavior once CSV import is stable.

Acceptance checks:

- Imported Douban ratings influence the local taste profile.
- Uncertain matches do not silently affect recommendations.
- The plugin works when Douban is disabled.

Reference:

- See `docs/DOUBAN_SKILL_INTEGRATION.md` for the concrete incorporation strategy.

## Milestone 7: Recommendation Engine

Deliverables:

- Taste profile builder per user.
- Deterministic candidate pre-ranker.
- LLM prompt builder.
- LLM client abstraction.
- Strict response schema requiring existing Jellyfin item IDs only.
- Output validator and fallback strategy.

LLM input should include:

- A compact summary of the user's liked and disliked media.
- Explicit ratings from Jellyfin and Douban.
- Rewatch/completion patterns.
- Candidate items with item IDs and metadata.
- Constraints: recommend only candidate IDs, avoid already-watched items unless allowed, diversify results.

LLM output should include:

- Ordered list of item IDs.
- Short reason per item.
- Confidence score.

Acceptance checks:

- Invalid IDs are rejected.
- Empty or malformed LLM responses fall back to deterministic ranking.
- The same candidate set can be tested without mutating Jellyfin.

## Milestone 8: Managed Recommendation Collection

Deliverables:

- Create or find a per-user recommendation collection.
- Store collection ID in plugin state.
- Update collection items after recommendation generation.
- Avoid duplicate items.
- Remove stale plugin-managed recommendations.

Collection naming draft:

- Single-user server: `Recommended For You`.
- Multi-user server: `Recommended for <username>`.

Acceptance checks:

- Manual refresh creates the collection if missing.
- Re-running refresh updates the collection contents.
- The collection only contains validated existing item IDs.

## Milestone 9: Admin Page and Manual Actions

Deliverables:

- Plugin configuration page.
- Manual buttons:
  - Test Jellyfin connection.
  - Import Douban export.
  - Rebuild local index.
  - Generate recommendations.
  - Update collection.
- Status display for last sync and last recommendation run.

Acceptance checks:

- Admin can run the full MVP flow without shell access.
- Errors are visible and actionable.

## Milestone 10: Scheduling and Operational Hardening

Deliverables:

- Scheduled task for nightly or weekly refresh.
- Rate limits for LLM and Douban adapters.
- Structured logs.
- Migration/version handling for SQLite schema.
- Basic unit tests for scoring, matching, validation, and collection diffing.

Acceptance checks:

- Scheduled refresh does not hammer external services.
- Plugin survives Jellyfin restart without losing state.
- Schema upgrades are repeatable.

## Initial Data Model Draft

Tables:

- `PlaybackEvents`
- `UserItemStats`
- `LibraryItems`
- `ExternalRatings`
- `DoubanItems`
- `ItemMatches`
- `RecommendationRuns`
- `RecommendationItems`
- `ManagedCollections`

Important relationships:

- `UserItemStats` belongs to one Jellyfin user and one Jellyfin item.
- `ExternalRatings` can point to a Douban subject and optionally to a matched Jellyfin item.
- `RecommendationRuns` stores inputs hash, provider/model, status, and created time.
- `RecommendationItems` stores ordered output and explanation per run.

## Open Questions

- Which Jellyfin server version is the first target on the user's server?
- Should shows be recommended as series, next unwatched episodes, or both?
- Should watched-but-highly-rated items appear as rewatch recommendations?
- Should each Jellyfin user map to a separate Douban account?
- Should Douban import be admin-only or per-user self-service?
- Which LLM provider should be the first supported provider?
