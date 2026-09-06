# Steam Cloud save-scope decision — 2026-09-06

This decision supersedes the older Phase-A/architecture statements that only `progress.json` or no character files are Cloud-eligible.

The product requirement is now that a player's meaningful authored state follows them between PCs and from the Steam Demo into the full game. Therefore Steam Auto-Cloud includes:

- `progress.json`;
- canonical `character.json` files;
- canonical Buddy paint PNGs;
- the active room-background painting.

Machine-specific `settings.json`, recovery artifacts, Workshop staging/provenance, and imported Workshop room caches remain local-only.

The Demo and full game intentionally retain the same Godot custom `user://` directory (`DesktopBuddy`) for same-PC continuity. Cross-App Steam Cloud handoff uses Demo App `5228990` -> shared Cloud App `5114950` only once the base/full App is released, because Valve documents that shared Cloud data does not sync correctly against an unreleased target/base App.

The executable does not manually upload these files through `ISteamRemoteStorage`; Steam Auto-Cloud owns transfer/conflict handling around process launch/exit. The canonical configuration is documented in `docs/STEAM_CLOUD_SETUP.md` and represented in code by `SteamCloudSavePolicy`.
