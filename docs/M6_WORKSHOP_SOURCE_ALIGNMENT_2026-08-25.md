# Milestone 6 — Steam Workshop Source Alignment

Status: **AUTHORIZED**
Date: 2026-08-25
Branch: `plan/godotsteam-workshop-social-features`

The project owner explicitly authorized implementation of the Steam Workshop plan on 2026-08-25. For this branch, this supplement supersedes older wording in `AGENTS.md`, `CHARACTER_EDITOR_WORKSHOP_PLAN.md`, `ROADMAP.md`, and historical milestone notes that says Phase C / Workshop is deferred or forbidden. It does **not** authorize real-time multiplayer, lobbies, P2P networking, RPCs, arbitrary mods, or the future Damage Sprint leaderboard.

The task-level source of truth is `docs/GODOTSTEAM_WORKSHOP_AND_SOCIAL_FEATURES_IMPLEMENTATION_PLAN_2026-08-25.md`. Existing character, painting, persistence, platform, testing, and offline-first invariants remain binding unless this supplement explicitly changes them.

## Authorized scope

- GodotSteam 4.22 GDExtension at the platform edge only.
- Steam Workshop publish/download/import for the current room painting.
- Steam Workshop publish/download/import for Buddy Studio character configuration plus declared buddy paint surfaces.
- Local/offline package validation and directory-based Workshop emulator for CI/development.
- Steam unavailable/offline fallback; single-player must boot and function normally without Steam.
- A Win98-style Workshop surface for publish, browse, subscriptions, import, and explicit apply/use flows.

## Still deferred

- Damage Sprint / Steam friends leaderboard (tasks L0-L4).
- Steam lobbies, matchmaking, networking sockets, SDR, P2P, `MultiplayerPeer`, RPCs, shared live rooms, or any player-to-player simulation.
- Arbitrary Workshop Resources, scenes, scripts, shaders, DLLs, meshes, native code, or generalized mod APIs.

## Provider decision

The historical Phase C note naming Steamworks.NET is superseded. The provider is GodotSteam GDExtension 4.22 behind a project-owned GDScript bridge and typed C# interfaces. C# gameplay/domain code must not depend directly on GodotSteam API types.

## Moderation and legal policy for v1

These choices close the policy gate without requiring a custom moderation backend:

- Steam Workshop / Steam Community reporting is the primary reporting mechanism. The game may open the item page; it does not implement its own report backend.
- Downloaded content is never auto-activated and is copied to project-owned staging before validation.
- The in-game v1 subscription list does not render remote Workshop preview images. This avoids automatically displaying unreviewed image content; the Steam overlay is used for full item browsing.
- Player-authored titles/descriptions receive structural validation only (length, controls/newlines where inappropriate). There is no home-grown profanity classifier; Steam moderation remains authoritative.
- Publish UI must state that publishing is subject to the Steam Workshop Legal Agreement and must surface Steam's `needs legal agreement` result. The item is not presented as fully published while that action is outstanding.
- Imported provenance does not persist/display the author's Steam account identifier or display name in v1. The Steam item ID is sufficient provenance and the overlay remains the place to inspect authorship.
- A local hidden-item list is optional follow-up UX, not required for the first publish/import path because the app does not auto-render remote previews or auto-import subscriptions.

## Steamworks App Admin gate

The repository intentionally contains no production AppID. Live Steam validation therefore remains an external release gate. Before the real-account matrix can pass, the owner must configure the production/test AppID in Steamworks with ISteamUGC file transfer enabled, Workshop visibility/tags, preview-image Cloud quota, Workshop page metadata, and legal-agreement testing.

The code must accept the AppID through a non-secret runtime/project/depot configuration and must never require `steam_appid.txt` to be tracked.

## Definition of done

Implementation is acceptable when:

1. local/domain tests prove hostile-input validation and identity isolation;
2. headless scenarios work with the directory/fake transport;
3. normal game bootstrap works with GodotSteam absent;
4. no Valve runtime binary or `steam_appid.txt` is tracked;
5. room and buddy imports never auto-apply/auto-activate;
6. the only remaining unverified items are Steamworks Partner configuration and the manual two-account depot matrix that cannot be performed from source control.
