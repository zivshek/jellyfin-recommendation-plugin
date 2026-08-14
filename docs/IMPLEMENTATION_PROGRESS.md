# Implementation Progress

Last updated: 2026-08-14

## Current Status

Planning and repository setup are in progress.

## Completed

- Chose the MVP display strategy: managed Jellyfin collection instead of Jellyfin Web homepage modification.
- Captured the implementation plan in `docs/IMPLEMENTATION_PLAN.md`.
- Added local environment scaffolding:
  - `.env.example` is committed as a safe template.
  - `.env.local` is ignored and reserved for real local testing values.
- Added a future-conversation prompt in `docs/NEW_CONVERSATION_PROMPT.md`.

## In Progress

- Initial documentation commit.

## Next Steps

1. Confirm target Jellyfin server version.
2. Scaffold the plugin solution and project.
3. Add the plugin entry point and configuration model.
4. Add a minimal configuration page.
5. Verify the plugin builds.

## Decisions

- Recommendations must only include existing media from the Jellyfin library.
- Explicit user ratings should outrank inferred completion signals.
- Douban support should start with manual import and local cache, then grow into public feed or Frodo-style sync later.
- Uncertain Douban-to-Jellyfin matches should require review before affecting recommendations.
- LLM output must be validated before collection updates.

## Risks

- Jellyfin plugin API compatibility can shift between server versions.
- Douban does not provide a clean current official API path for all desired data.
- LLM output can be invalid or unstable without strict schema validation.
- Collections are a stable MVP UI, but they are less prominent than a true custom homepage row.

## Local Testing Notes

Use `.env.local` for private local values:

```dotenv
JELLYFIN_BASE_URL=http://localhost:8096
JELLYFIN_API_KEY=
JELLYFIN_TEST_USER_ID=
JELLYFIN_RECOMMENDATION_COLLECTION_NAME=Recommended For You
LLM_PROVIDER=openai-compatible
LLM_BASE_URL=
LLM_API_KEY=
LLM_MODEL=
DOUBAN_USER_ID=
DOUBAN_EXPORT_PATH=
PLUGIN_TEST_DATA_DIR=
```
