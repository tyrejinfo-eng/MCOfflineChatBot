# MC Offline Chat — Audit & Roadmap

## What this extract gives you
- Renamed mobile-first MAUI app namespace, project names, and visible branding.
- Default startup now opens the shell directly instead of the login gate.
- The main visible navigation is reduced to offline-friendly areas: Chat, Dashboard, Git Search, Telemetry, Settings, and FAQ.
- The chat persona is rewritten toward stories, journaling, creativity, and offline use.
- Offline mode is the default preference.

## What is already strong
- Local GGUF inference is already present.
- TTS is already wired in.
- Model selection, download, and switching are already abstracted.
- Git search/crawler support is already separate enough to keep.
- Telemetry and app observability are already modeled as their own concerns.
- The codebase is modular enough to carve out a cleaner offline runtime.

## Main issues still left to clean up
- Some services still carry legacy server/sync code paths.
- A few hidden pages and view models still mention old security/server concepts internally.
- Dashboard and settings still expose optional sync data structures, even though the shell no longer foregrounds them.
- Several feature areas are still coupled to the older SyntheticAI security identity.

## Roadmap
### Phase 1 — Branding and startup
- Finish renaming any remaining internal symbols to MC Offline Chat.
- Replace old app icons / splash text if you want a fully new product identity.
- Keep startup on the chat shell by default.

### Phase 2 — Offline core
- Replace the remaining server-dependent chat paths with local-only model selection.
- Add a proper local model registry and capability detector.
- Persist conversation history locally.

### Phase 3 — Creative assistant layer
- Add journals, story prompts, memory summaries, and writing modes.
- Add prompt presets for creativity, planning, and reflection.
- Keep TTS as an optional user setting.

### Phase 4 — Keep the useful extras
- Keep Git search / crawler as an optional research tool.
- Keep telemetry as a local observability tool.
- Keep model management separated from the chat UI.

### Phase 5 — Cleanup and hardening
- Remove dead security pages from navigation and DI once you are ready.
- Split local-only services from optional network services.
- Add smoke tests for startup, model switching, and offline chat.

## Recommended next move
Turn the current chat + model selection path into the core product, then pull Git search and telemetry in as separate tabs or modules.
