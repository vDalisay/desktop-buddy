# Milestone 6 — Steam Workshop Source Alignment

Status: **AUTHORIZED — source-controlled implementation verified on draft PR #41; live Steam validation remains external**
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

The code accepts the AppID through the `DESKTOP_BUDDY_STEAM_APP_ID` development/runtime override or GodotSteam's canonical `steam/initialization/app_id` project/depot setting and never requires `steam_appid.txt` to be tracked.

## Verification snapshot — 2026-08-25

The implementation branch has reached the following source-controlled gates:

- `dotnet build DesktopBuddy.sln -c Debug` passes.
- Domain test suite passes with zero failures.
- The repository Steam-binary guard passes; no Valve runtime binary or `steam_appid.txt` is tracked.
- Godot 4.6.1 headless editor import passes with GodotSteam physically absent, proving the optional bridge does not become a boot/import dependency.
- `workshop_emulator_roundtrip` passes under Godot. It exercises room pixels → versioned share package → directory Workshop publish snapshot → subscription/install → hostile-input staging/validation → local room library import, including exact 1,048,576-byte RGBA pixel roundtrip and Workshop-item provenance.
- The GodotSteam 4.22 bridge call shapes were checked against the current binding; version-specific details such as the third `setItemTags` argument are contained in the GDScript anti-corruption bridge.
- The exact official Godot Asset Library 4.22 archive for revision `ac5fc8bbc3d34c203e832864e2ebab4b21f3efd9` was downloaded on a clean GitHub Actions runner and pinned at 32,103,117 bytes with SHA-256 `9ED28D9FE8CA43E769BD8E1160C0F7806B7C6337FD672F919A9103DC84829777`.
- The verified archive contains `addons/godotsteam/godotsteam.gdextension`, both Windows x86_64 GodotSteam debug/release DLLs, and `win64/steam_api64.dll`; `tools/install_godotsteam.ps1` verifies the pinned hash before copying the complete addon into the gitignored local dependency directory.
- The economy benchmark now derives its current purchasable order from `CataloguePolicy.LaunchContentIds` instead of carrying the obsolete M5 11-item timing table. The fixed 209-minute seeded trace remains a deterministic income/mechanics observation, while price order, every-item-first-purchase reachability, deduplication, active/passive balance, and fingerprint behavior are validated against the current authored catalogue.
- PR #41's full `build-test` job passes every substantive gate end-to-end, including Workshop roundtrip, current-catalogue economy calibration, three-minute idle soak, dual-profile stability, and the final M3 full tool-feel journey.
- The independent Asset Forge workflow covers its own game/import/presentation/generated-content/standalone project gates separately.

## Remaining external gates

The source-controlled dependency and offline Workshop path are verified. Live Steam verification still requires:

1. Configure the base game's production/test Steam AppID in Steamworks (ISteamUGC file transfer, Workshop visibility/tags, preview-image Cloud quota, Workshop page metadata, and legal-agreement path).
2. Run the manual two-account/depot matrix against the configured AppID: publish, legal-agreement handling, subscribe/download, import, offline reuse, update, and malformed/removed-item behavior.

If the Steam demo AppID must publish into or consume the base game's Workshop, treat that as an explicit cross-app integration requirement rather than assuming the demo and base game share Workshop context automatically.

## Definition of done

Implementation is acceptable when:

1. local/domain tests prove hostile-input validation and identity isolation;
2. headless scenarios work with the directory/fake transport;
3. normal game bootstrap works with GodotSteam absent;
4. no Valve runtime binary or `steam_appid.txt` is tracked;
5. room and buddy imports never auto-apply/auto-activate;
6. the official GodotSteam dependency is pinned/materializable with integrity verification; and
7. the only remaining unverified items after that are Steamworks Partner configuration and the manual two-account depot matrix that cannot be performed from source control.

Items 1–6 are satisfied on draft PR #41. Item 7 is the remaining release gate and requires the configured Steamworks environment rather than additional source-only implementation.
