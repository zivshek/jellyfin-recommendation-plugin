# Douban Skill Integration Notes

## Summary

The `daymade/claude-code-skills` `douban-skill` is a strong reference implementation for Douban export and sync. It supports:

- Full historical export through Douban's Frodo mobile API.
- RSS incremental sync for recent updates.
- No login, cookies, or browser automation.
- CSV output with UTF-8 BOM.
- Preflight user ID validation.

This fits the plugin well, but it should not be treated as runtime Jellyfin plugin code as-is. The skill is designed for Claude Code workflows and ships Python/Node scripts. A Jellyfin server plugin should remain self-contained C# at runtime where possible.

## Recommended Incorporation Strategy

Use a two-stage approach:

1. **MVP compatibility:** support importing the CSV files produced by `douban-skill`.
2. **Native plugin sync:** port the useful Frodo and RSS logic into a C# Douban adapter after the rest of the recommendation pipeline is working.

This gives us quick access to Douban ratings without making the Jellyfin plugin depend on Python, Node, `uv`, Claude Code, or a user-installed external skill.

## Why Not Shell Out From Jellyfin

Avoid running `douban-skill` scripts directly from the Jellyfin plugin because:

- Jellyfin plugins should be portable across Windows, Linux, Docker, and NAS installs.
- Python/Node may not exist in the server environment.
- Process execution from a server plugin creates avoidable security and support risk.
- External script failures would be harder to surface clearly in Jellyfin's admin UI.

Shelling out can still be useful for local development tools, but not as the primary plugin runtime path.

## MVP Import Format

The skill writes four CSV files per user:

- `书.csv`
- `影视.csv`
- `音乐.csv`
- `游戏.csv`

For this plugin, the first supported file should be:

- `影视.csv`

Expected columns:

- `title`
- `url`
- `date`
- `rating`
- `status`
- `comment`

The `url` should contain the Douban subject ID, for example:

```text
https://movie.douban.com/subject/1292052/
```

For plugin storage, parse:

- `DoubanSubjectId`: `1292052`
- `Title`: CSV `title`
- `MarkedDate`: CSV `date`
- `Rating`: convert `★` through `★★★★★` to `1` through `5`
- `Status`: CSV `status`, such as `看过`, `在看`, `想看`
- `Comment`: CSV `comment`
- `Source`: `douban-skill-csv`

## RSS Incremental Sync

RSS sync should be treated as a supplement to full export.

Important constraint:

- RSS only returns the newest recent items, roughly the latest 10 items per feed.

Recommended behavior:

1. Require one full CSV import first.
2. Allow RSS sync to update newly marked or newly rated items.
3. Keep local cache rows immutable enough to preserve historical imports.
4. De-duplicate by `DoubanSubjectId` and latest update timestamp.

## Native C# Adapter Plan

After the MVP CSV import works, port the following concepts from `douban-skill`:

- User ID extraction from full Douban profile URLs.
- Frodo endpoint: `/api/v2/user/{userId}/interests`.
- HMAC-SHA1 signature generation.
- Category/status iteration for movie interests.
- Pagination using `start` and `count`.
- Conservative request delays and retry handling.
- Preflight user validation.
- Clear error messages for invalid ID, private profile, network failure, rate limiting, and signature errors.

The native adapter should fetch movies first. Books, music, and games can stay out of scope unless the recommender later benefits from cross-media taste.

## Configuration Additions

Add or keep these local/plugin settings:

- `DOUBAN_USER_ID`
- `DOUBAN_EXPORT_PATH`
- `DOUBAN_SYNC_PROVIDER`, values:
  - `none`
  - `csv`
  - `rss`
  - `frodo`
- `DOUBAN_SYNC_INTERVAL_HOURS`

For MVP, `csv` is enough.

## Licensing and Attribution

The `daymade/claude-code-skills` marketplace is MIT licensed according to its repository README. If we vendor or port meaningful code from the skill, add attribution in a `NOTICE` or source comment and preserve the MIT license requirements.

Until we vendor code, this repository should reference the skill as implementation research only.

## Implementation Tasks

1. Add a `DoubanCsvImportService`.
2. Add tests for parsing UTF-8 BOM CSV files.
3. Add tests for star rating conversion.
4. Add tests for Douban subject ID extraction from URLs.
5. Store imported rows in `DoubanItems` / `ExternalRatings`.
6. Wire imported Douban ratings into taste profile building.
7. Add admin UI upload/path import flow.
8. Later, add `DoubanRssSyncService`.
9. Later, add native `DoubanFrodoClient`.

## Open Questions

- Should CSV import accept only `影视.csv`, or a full folder containing all skill outputs?
- Should `想看` items affect recommendations as explicit interest even if not watched?
- Should low-rated `看过` items be used as negative examples even when Jellyfin watch history says they were completed?
- Should imported Douban comments be sent to the LLM, or only used locally for explainability?
