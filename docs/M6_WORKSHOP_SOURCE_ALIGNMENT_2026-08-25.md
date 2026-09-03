# Milestone 6 — Steam Workshop Source Alignment

Status: **AUTHORIZED — source-controlled implementation is CI-gated on draft PR #41; live Steam validation remains external**
Authorization date: 2026-08-25
Latest source hardening: 2026-08-28
Branch: `plan/godotsteam-workshop-social-features`
Base-game Steam AppID / Workshop owner: **5114950**

The project owner explicitly authorized implementation of the Steam Workshop plan on 2026-08-25. For Workshop/package/platform scope, this supplement supersedes older wording in `AGENTS.md`, `DECISIONS.md`, `CHARACTER_EDITOR_WORKSHOP_PLAN.md`, `ROADMAP.md`, and historical milestone notes that says Phase C / Workshop is deferred or forbidden. It does **not** authorize real-time multiplayer, lobbies, P2P networking, RPCs, arbitrary mods, or the future Damage Sprint leaderboard.

The detailed task contracts and research are in `docs/GODOTSTEAM_WORKSHOP_AND_SOCIAL_FEATURES_IMPLEMENTATION_PLAN_2026-08-25.md`. Existing character, painting, persistence, platform, testing, and offline-first invariants remain binding unless this supplement explicitly changes them.

## Authorized scope

- GodotSteam 4.22 GDExtension at the platform edge only.
- Steam Workshop publish/download/import for the current room painting.
- Steam Workshop publish/download/import for Buddy Studio character configuration plus declared buddy paint surfaces.
- Local/offline package validation and directory-based Workshop emulator for CI/development.
- Steam unavailable/offline fallback; single-player must boot and function normally without Steam or without the GodotSteam addon installed.
- A Win98-style Workshop surface for publish, browse, subscriptions, import, and explicit apply/use flows.
- Base-game runtime AppID and Workshop-owner AppID are currently both `5114950`.
- Runtime identity and Workshop ownership remain separate in code so a future demo can use its own runtime AppID while retaining base-game Workshop ownership if Steamworks cross-app configuration permits it.

## Still deferred

- Damage Sprint / Steam friends leaderboard (tasks L0-L4).
- Steam lobbies, matchmaking, networking sockets, SDR, P2P, `MultiplayerPeer`, RPCs, shared live rooms, or any player-to-player simulation.
- Arbitrary Workshop Resources, scenes, scripts, shaders, DLLs, meshes, native code, or generalized mod APIs.

## Provider decision

The historical Phase C note naming Steamworks.NET is superseded. The provider is GodotSteam GDExtension 4.22 behind a project-owned dynamic GDScript bridge and typed C# interfaces. C# gameplay/domain code must not depend directly on GodotSteam API types.

GodotSteam v4.20+ registers the primary application AppID at:

```text
steam/initialization/app_data/app_id
```

Desktop Buddy therefore stores `5114950` at that canonical project setting. The older `steam/initialization/app_id` key is intentionally not used; GodotSteam migrates and clears that legacy key at startup.

## Moderation and legal policy for v1

These choices close the policy gate without requiring a custom moderation backend:

- Steam Workshop / Steam Community reporting is the primary reporting mechanism. The game may open the item page; it does not implement its own report backend.
- Downloaded content is never auto-activated and is copied to project-owned staging before validation.
- The in-game v1 subscription list does not render remote Workshop preview images. This avoids automatically displaying unreviewed image content; the Steam overlay is used for full item browsing.
- Player-authored titles/descriptions receive structural validation only (length, controls/newlines where inappropriate). There is no home-grown profanity classifier; Steam moderation remains authoritative.
- Publish UI must state that publishing is subject to the Steam Workshop Legal Agreement and must surface Steam's `needs legal agreement` result. The item is not presented as fully published while that action is outstanding.
- Imported provenance does not persist/display the author's Steam account identifier or display name in v1. The Steam item ID is sufficient provenance and the overlay remains the place to inspect authorship.
- A local hidden-item list is optional follow-up UX, not required for the first publish/import path because the app does not auto-render remote previews or auto-import subscriptions.

## Workshop content contract

V1 Workshop content is hostile **data**, never trusted project content.

- Room packages contain only the versioned manifest plus the 512×512 RGBA8 `environment/background.png` payload and a generated preview outside the imported content payload.
- Buddy packages contain only the versioned manifest, `character.json`, and declared whitelisted 512×512 RGBA8 paint PNGs.
- A room preview is a complete capture of the current Win98 Play window with its painted background visible and the buddy omitted. A buddy preview uses the canonical frontal Paint Buddy rig/camera without editor controls.
- A successful publish shows an in-game confirmation with an explicit action that opens the new Steam Workshop item page for editing.
- Manifest paths are exact-whitelist relative paths. Absolute paths, traversal, duplicates, links/reparse points, undeclared files, size-cap violations, hash mismatches, malformed/future schemas, and invalid image dimensions are rejected before local import.
- Steam install/cache folders are copied **once** into project-owned incoming staging. Content detection and final import validate that same immutable snapshot; no later stage rereads Steam's mutable cache.
- Hostile-data validation failures remain typed validation results. Expected malformed/oversized Workshop data does not escape the validation boundary as exception-driven application control flow.
- Imported characters always receive a fresh local GUID; remote/source identity is provenance only.
- Successfully imported content is locally owned and remains usable offline. Subscription removal must not silently delete the imported local copy.
- No Workshop `.tscn`, `.tres`, arbitrary Godot Resource, script, shader, DLL, native library, mesh, or executable payload is loaded.

## Async, callback, and cancellation contract

Steam persistence has explicit commit points; cancellation must never claim an operation was undone when a persistent side effect already exists.

- A project-owned `WorkshopPublishCallbackLane` owns the single in-flight Create/Update callback lane. GodotSteam's update callback does not carry a request token, so concurrent publishes are rejected until the callback owner completes, is synchronously rejected before submission, or the transport shuts down.
- Duplicate/late CreateItem and SubmitItemUpdate callbacks are ignored after their owner has released the lane.
- `CreateItem` is the remote publish commit point. Once Steam accepts it, Desktop Buddy waits for the real PublishedFileId and completes the initial item update even if caller cancellation arrives while waiting; this avoids knowingly abandoning an empty Workshop item.
- If cancellation arrives after `SubmitItemUpdate` begins, the caller may stop waiting while Steam continues. The callback lane stays owned until Steam's real callback, and the immutable publish staging is retained rather than deleting bytes Steam may still consume.
- Room and buddy imports distinguish pre-commit cancellation from generic failure. They clean owned staging on pre-commit cancellation rather than quarantining otherwise-valid content.
- A successful `CharacterPaintStore` transaction is the local buddy-import commit point. Cancellation after that swap cannot be reported as `Cancelled`; bookkeeping/provenance is completed best-effort and the result remains a successful local import.
- Subscription enumeration returns a typed `Success / Unavailable / Failed / Cancelled` result. There is no public raw-list API that can collapse Steam-unavailable into a legitimate zero-subscription result.
- The deterministic directory transport follows the same typed cancellation contract as the real transport.

## Steamworks App Admin gate

The base-game AppID is source-controlled as public product configuration: **5114950**. It is not a secret and the main game is not yet released.

Live Workshop verification still requires the Steamworks configuration for AppID `5114950` to be published and available to developer/test accounts. Before the real-account matrix can pass, confirm:

1. ISteamUGC file transfer is enabled and the App Admin changes are published.
2. Workshop visibility is appropriate for developer/test access.
3. Ready-to-Use Workshop tags **Room Painting** and **Buddy** are configured/published.
4. Steam Cloud quota required for Workshop preview images is configured.
5. Workshop page metadata/branding is configured sufficiently for the intended visibility.
6. The Workshop Legal Agreement path can be exercised with an account that has not yet accepted the current agreement.

The 2026-09-03 Windows live-publish attempt confirmed that item 1 is still open: Steam created
the remote item, then `workshop_log.txt` rejected its upload with `no workshop depot found`.
The Workshop file-transfer/depot configuration must be corrected and published before rerunning
the real-account matrix.

Desktop Buddy resolves Steam identity in this order:

- runtime override `DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID`;
- legacy development override `DESKTOP_BUDDY_STEAM_APP_ID`;
- canonical GodotSteam project setting `steam/initialization/app_data/app_id`;
- Workshop owner override `DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID`, then project setting/default `5114950`.

No tracked `steam_appid.txt` is required.

## Source-controlled verification — refreshed 2026-08-28

PR #41's verification contract now includes all of the following on one exact head before handoff:

- `dotnet build DesktopBuddy.sln -c Debug`.
- Complete domain/managed test suite, including callback-lane state transitions, typed transport cancellation, immutable incoming staging, hostile-data validation boundaries, and local room cancellation behavior.
- Repository Steam-binary guard; no Valve runtime binary, GodotSteam runtime binary, or `steam_appid.txt` may be tracked.
- Godot 4.6.1 headless editor import with GodotSteam physically absent, proving the optional bridge does not become a boot/import dependency.
- `workshop_emulator_roundtrip` under Godot for **both authorized content types**:
  - room: exact 1,048,576-byte RGBA pixels → versioned package → emulator publish snapshot → subscription/install → immutable hostile-input staging/validation → locally owned room preset + provenance;
  - buddy: character configuration plus a real declared non-blank `paint/head.png` surface → versioned package → emulator publish snapshot → subscription/install → immutable hostile-input staging/validation → fresh local GUID → exact decoded paint roundtrip + Workshop provenance.
- Full existing gameplay/scenario suite after the Workshop gate so the optional platform feature cannot regress normal single-player behavior.
- Asset Forge CI, including its deterministic core tests, generated fixture validation, game import/boot, capture gates, generated customization assets, and standalone project checks.
- Dedicated `GodotSteam Native Smoke`, which installs the real pinned addon, imports it with Godot 4.6.1, dynamically discovers the native Steam API, verifies the required 4.22 Workshop capability/signal surface, and reaches `steamInitEx`.

The exact official Godot Asset Library 4.22 archive for revision `ac5fc8bbc3d34c203e832864e2ebab4b21f3efd9` is pinned at 32,103,117 bytes with SHA-256 `9ED28D9FE8CA43E769BD8E1160C0F7806B7C6337FD672F919A9103DC84829777`. `tools/install_godotsteam.ps1` verifies that hash before materializing the complete addon into the gitignored local dependency directory. The verified archive contains `addons/godotsteam/godotsteam.gdextension`, Windows x86_64 GodotSteam debug/release DLLs, and `win64/steam_api64.dll`.

The native smoke resolves **runtime=5114950 / Workshop owner=5114950**. On a GitHub-hosted Linux runner, `steamInitEx` then fails for the expected external reason that no Steam client/`~/.steam/sdk64/steamclient.so` exists. The bridge must classify this as Steam unavailable/offline and the scenario must pass, proving addon-present/no-Steam fallback rather than masking a binding failure.

A PR head is not considered source-verified merely because an earlier head passed. The final handoff head must have the PR `CI`, `GodotSteam Native Smoke`, and `Asset Forge CI` workflows green.

## Local Windows live-test entrypoint

`devtools/play_game_steam.bat` is the supported developer launcher for the next live gate. It:

1. materializes verified GodotSteam 4.22 if missing;
2. checks that `steam.exe` is running;
3. defaults runtime and Workshop-owner AppIDs to `5114950`;
4. launches the normal game through `play_game.bat`;
5. permits a future demo runtime AppID override without changing Workshop ownership; and
6. does not create or track `steam_appid.txt`.

`devtools/play_game_steam_diagnostics.bat` provides the same verified dependency/environment setup with persistent build/runtime diagnostics for a live Steam session.

## Remaining external gates

The source-controlled dependency, package pipeline, emulator path, addon-present capability path, and offline fallbacks are CI-verifiable. Live Steam verification still requires:

1. confirm/publish the Steamworks configuration listed above for AppID `5114950`;
2. run Desktop Buddy through `devtools/play_game_steam.bat` with Steam signed in to an account that has developer/test access;
3. run the manual two-account/depot matrix: publish, legal-agreement handling, subscribe/download, import, offline reuse, update, and malformed/removed-item behavior.

If the future Steam demo AppID must publish into or consume the base game's Workshop, treat that as an explicit cross-app integration requirement and validate it once the demo AppID exists. No demo AppID is required for the current base-game implementation or its source-controlled verification.

## Definition of done

Implementation is acceptable when:

1. local/domain tests prove hostile-input validation, callback ownership/cancellation behavior, and identity isolation;
2. headless scenarios prove room and buddy share roundtrips with the directory/fake transport;
3. normal game bootstrap works with GodotSteam absent;
4. the real pinned GodotSteam addon imports and its required Workshop capability surface is verified;
5. addon-present/no-Steam initialization fails safely into the offline path;
6. no Valve/GodotSteam runtime binary or `steam_appid.txt` is tracked;
7. room and buddy imports never auto-apply/auto-activate; and
8. the only remaining unverified items after those source gates are Steamworks Partner configuration and the manual real-account/depot matrix that cannot be performed from source-controlled CI.

Once the final PR head passes all three required workflows, items 1–7 are source-verified. Item 8 remains a release gate requiring the configured Steamworks/Steam-client environment rather than additional source-only implementation.
