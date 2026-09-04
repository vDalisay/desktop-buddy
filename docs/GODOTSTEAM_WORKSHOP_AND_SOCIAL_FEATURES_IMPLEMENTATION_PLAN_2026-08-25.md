# GodotSteam Workshop and Async Social Features — Implementation Plan

> **Activation note:** `docs/M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md` authorizes and corrects this plan. Workshop v1 is included in the Steam Demo and full Steam release, and excluded from the itch.io build; the future leaderboard remains deferred.

Status: **research-complete implementation handoff; no Steam production code is authorized by this document alone**  
Date: 2026-08-25  
Base: `main` at `e1dad4934fa1259018e40b5dc552ee502b2e207f`  
Scope owner request: Steam Workshop sharing/downloading for **room paintings** and **Buddy Studio configuration + buddy paintings**, plus a future **friends 30-second damage leaderboard**.

This plan deliberately does **not** introduce real-time multiplayer. There are no lobbies, replicated players, P2P packets, RPCs, matchmaking sessions, Steam Networking Sockets, Steam Datagram Relay, or Godot `MultiplayerPeer` requirements in the requested feature set. The architecture is asynchronous social functionality: user-generated content plus an optional future Steam leaderboard.

At drafting time, `AGENTS.md` marked Phase C / Workshop / multiplayer as deferred, so the first implementation task below was a source-of-truth gate. The Milestone 6 source-alignment supplement subsequently authorized Workshop v1 while leaving multiplayer deferred.

---

## 1. Executive architecture decision

Use **GodotSteam GDExtension 4.22** as the Steamworks integration, but keep it at the outermost platform edge behind a tiny **GDScript bridge** and typed C# interfaces.

```text
Steam client / Steamworks
        |
GodotSteam 4.22 GDExtension
        |
GodotSteamBridge.gd                 <- all dynamic GodotSteam calls/signals live here
        |
GodotSteamBridgeAdapter.cs          <- Variant/dictionary/signal -> typed C# results
        |
+----------------------+-------------------------+
| ISteamWorkshopTransport | ISteamLeaderboardTransport |
+----------------------+-------------------------+
        |                                      |
WorkshopSharingCoordinator             DamageSprintLeaderboardService
        |                                      |
package/export/import services                 challenge/domain score
        |
existing local stores
```

### Why this boundary

Desktop Buddy is currently Godot **4.6.1 .NET/C#** / .NET 8. The current Godot Asset Library release is GodotSteam GDExtension **4.22**, published 2026-08-22 for Godot 4.4+, based on Steamworks SDK 1.65. This matches the engine version well.

However, arbitrary GDExtension classes still do not have first-class automatically generated C# bindings in Godot. The Godot proposal for this remains open and explicitly describes a GDScript bridge as the current workaround. A community GodotSteam C# binding project exists, but its current README advertises support against the much older **GodotSteam 4.6.1 plugin**, not the current 4.22 release. Making that wrapper a required production dependency would add a second compatibility layer that can drift independently from both Godot and GodotSteam.

Therefore:

- GodotSteam itself is the only Steam integration dependency.
- A small project-owned GDScript bridge calls GodotSteam.
- The bridge obtains the Steam singleton dynamically (`Engine.has_singleton` / `Engine.get_singleton`) and uses dynamic calls, so the script still parses when GodotSteam is absent.
- C# never spreads raw `Variant`, `Dictionary`, GodotSteam enums, or signal payloads through the game.
- The C# adapter converts bridge events immediately into immutable typed records.
- All gameplay/editor/persistence code depends on narrow interfaces, not GodotSteam.
- A local/fake transport implements the same interfaces for CI and offline development.

This also preserves the existing architectural rule that OS/Steam code lives behind interfaces and local play remains functional when Steam is unavailable.

---

## 2. Explicit scope

### 2.1 In scope — first Workshop release

1. Publish the current **Paint Room** background to Steam Workshop.
2. Browse the game's Workshop using the Steam overlay / Community Workshop page.
3. Show the user's **subscribed items** in-game.
4. Download/install a subscribed Workshop item through Steam.
5. Validate it as untrusted data.
6. Import a room painting into a local room-paint library, then let the player explicitly apply it.
7. Publish one **Buddy Studio character configuration** plus all declared buddy-paint surfaces.
8. Download/import a Buddy Studio Workshop item as a **new local character identity**.
9. Preserve imported content locally so it remains usable offline after a successful import.
10. Never auto-activate downloaded content.
11. Handle Steam offline/unavailable, Workshop legal agreement, download/update state, cancellation where possible, and failures without disrupting single-player.

### 2.2 Future scope — friends damage challenge

A dedicated 30-second challenge where friends compare best damage scores through Steam Leaderboards. This is still asynchronous: players never enter the same simulation.

### 2.3 Explicitly out of scope

- Steam lobbies or matchmaking.
- Invitations that join another player's game.
- Direct player-to-player messages or chat.
- Steam Networking Sockets / P2P / SDR.
- Godot RPCs or `MultiplayerPeer`.
- Shared live rooms.
- Sending physics state, buddy state, tool state, input, or save-state between clients.
- General mods.
- Workshop-provided `.tscn`, `.tres`, scripts, shaders, DLLs, native libraries, meshes, arbitrary Resources, or executable content.
- Sharing room furniture/layout in the initial Workshop format; **room painting only**.
- Bundling generated cosmetic geometry/resources from Buddy Studio. Workshop character data may reference stable cosmetic IDs understood by the receiving build, but the package cannot inject new Resources or geometry.
- Steam Cloud as a storage mechanism for imported Workshop content.

---

## 3. Current codebase seams to reuse

The implementation should compose around code already on `main`, not create parallel persistence systems.

### 3.1 Room paint

`EnvironmentPaintStore` already owns failure-safe storage of the current room's `environment/background.png` and accepts exactly one **512x512 RGBA8** surface. `EnvironmentCanvasPolicy` defines the same 512x512 surface contract.

Workshop sharing must snapshot pixels through the existing room paint model/store contract. Import must decode/validate into the same pixel representation, not make a Workshop-only rendering path.

### 3.2 Buddy Studio character configuration

The existing engine-free character pipeline remains authoritative:

```text
CharacterDocumentPolicy.DecodeAndMigrate
 -> CharacterDocumentNormalizer.Normalize
 -> CharacterDocumentValidator.Validate
 -> CharacterCompiler.Compile
```

Workshop import must use this exact policy before a character can enter the local library.

### 3.3 Buddy paint

`CharacterPaintStore` already provides the correct transaction boundary:

- six whitelisted paths only;
- each surface is 512x512 RGBA8;
- max encoded PNG size: **2 MiB per part**;
- max aggregate encoded buddy paint: **12 MiB**;
- staging directory is fully validated before swapping the live directory;
- linked/reparse-point paths are rejected;
- document paint references are checked against the whitelist.

Workshop import should reuse `PaintPngCodec`, `PaintPolicy`, and `CharacterPaintStore` rather than duplicate these rules.

### 3.4 Buddy Studio cosmetics

`BuddyGeneratedCosmeticRegistry` treats generated cosmetic definitions as trusted project-owned `res://` content and combines them with stable feature IDs from `CharacterFeatureCatalog`.

A shared character may therefore contain its stable feature/configuration IDs, but the Workshop package **must not contain arbitrary generated meshes/resources/scripts**. If a receiving build does not know a referenced ID, the existing character policy/compiler fallback behavior remains authoritative. This keeps UGC visual-data-only and prevents Workshop from becoming a mod loader accidentally.

---

## 4. Steam / GodotSteam research conclusions

### 4.1 Workshop is folder-based UGC

Valve's current `ISteamUGC` flow is:

```text
CreateItem(AppId, Community)
 -> CreateItemResult(PublishedFileId, legal-agreement flag)
 -> StartItemUpdate(AppId, PublishedFileId)
 -> SetItemTitle / SetItemDescription
 -> SetItemVisibility
 -> SetItemTags / metadata
 -> SetItemContent(folder)
 -> SetItemPreview(file)
 -> SubmitItemUpdate
 -> SubmitItemUpdateResult
```

Valve notes that an ISteamUGC Workshop item represents a **folder of files**, so Workshop content does not need a ZIP inside it. Avoiding a ZIP for Workshop also eliminates an unnecessary zip-slip/decompression attack surface.

For subscribed content:

```text
GetNumSubscribedItems / GetSubscribedItems
 -> GetItemState
 -> DownloadItem(PublishedFileId, highPriority=false) when needed
 -> wait for DownloadItemResult
 -> GetItemInstallInfo
 -> copy from Steam install folder to project-owned staging
 -> validate completely
 -> atomically import a local copy
```

Do not read `GetItemInstallInfo` and consume the folder immediately after starting a download. Valve explicitly requires waiting for the download result callback.

### 4.2 Steam callback pump is required

GodotSteam is callback/signal-driven. The bridge must call `Steam.run_callbacks()` every rendered/application frame while initialized. It belongs in `_Process`, not `_PhysicsProcess`, and its node must continue processing while gameplay is paused so Workshop/editor operations cannot deadlock while a customization workspace is open.

### 4.3 Steamworks configuration is a prerequisite, not code

Before upload testing works in the real AppID:

1. Configure Steam Workshop visibility initially for developers/testers.
2. Configure Steam Cloud per-user byte/file quotas because Valve uses Steam Cloud for Workshop **preview images**.
3. Enable **ISteamUGC for file transfer** in Workshop configuration.
4. Publish those App Admin changes.
5. Configure the Workshop page branding/title/description required by Valve before public visibility.
6. Configure developer-defined Workshop tags used by Desktop Buddy.
7. Test the legal agreement flow with an account that has not accepted the current Workshop agreement.

The game does not need to use Steam Cloud for its Workshop content files merely because preview-image quota is configured.

### 4.4 Legal agreement UX is mandatory

`CreateItemResult_t` and submit-update results expose whether the author needs to accept the Workshop legal agreement. Valve recommends:

- TOS copy adjacent to the publish button;
- after publishing, open the Workshop item page in the Steam overlay using the Community File page URI so the player can review/accept terms and manage the item.

The UI must never report an item as publicly published if Steam says it still needs agreement.

### 4.5 There is no reason to add a multiplayer transport

Workshop and leaderboards are services on top of Steam APIs. They do not require a multiplayer peer. Adding a Steam MultiplayerPeer, lobby framework, or network abstraction now would be unused architecture and would create connection/error/security complexity without serving the requested features.

---

## 5. Dependency strategy and repository hygiene

The repository's CI intentionally fails if tracked files include `steam_api.dll`, `steam_api64.dll`, corresponding `.so`/`.dylib`, or `steam_appid.txt`. Preserve that rule.

### 5.1 Pin GodotSteam

Pin exactly:

```text
GodotSteam GDExtension: 4.22
Godot compatibility: 4.4+
Steamworks SDK basis: 1.65
Desktop Buddy Godot: 4.6.1 .NET
Target: Windows x86_64
```

Do not use `latest` downloads during builds. Record artifact URL/release ID and SHA-256 in the dependency setup scripts or a dependency manifest.

### 5.2 Do not commit proprietary/dev Steam runtime files

Add setup tooling later, for example:

```text
tools/setup_godotsteam.ps1
tools/setup_godotsteam.sh
.deps/godotsteam/                 # gitignored
addons/godotsteam/                # generated/materialized, gitignored if it contains runtime binaries
```

The setup script downloads/extracts the pinned release and verifies hashes. CI jobs that launch Godot run this setup step before the headless editor import. `dotnet build` and pure domain tests remain independent from the extension.

The production export/depot pipeline stages the required runtime dependency while packaging. `steam_appid.txt` is development-only and is never shipped.

If a later licensing review permits committing the open-source GodotSteam extension binary/config while separately staging only Valve's runtime binary, that may be adopted, but the existing Steam-binary guard must remain effective for Valve SDK/runtime files.

---

## 6. Bridge and interface design

### 6.1 GDScript bridge responsibilities

Add a focused bridge, e.g.:

```text
src/Platform/Steam/GodotSteamBridge.gd
```

It owns only:

- dynamic lookup of the `Steam` singleton;
- initialization/shutdown;
- `run_callbacks()` pumping;
- method/signal capability checks;
- conversion of GodotSteam signals into project-owned bridge signals with simple scalar/dictionary payloads;
- opening Steam overlay URLs;
- no domain validation;
- no package parsing;
- no gameplay state.

Use dynamic calls through the singleton object rather than compile-time `Steam.*` identifiers. That lets the project and tests load when the GDExtension is physically absent.

At initialization, assert the expected 4.22 capability surface with `has_method` / signal-list checks. Fail closed with `UnsupportedGodotSteamVersion` if expected UGC/leaderboard capabilities are missing. This prevents subtle failure if a developer has another GodotSteam version installed.

Expected Workshop capability set should cover the GodotSteam wrappers corresponding to:

```text
steamInitEx (or the pinned 4.22 initialization method)
run_callbacks
createItem
startItemUpdate
setItemTitle
setItemDescription
setItemVisibility
setItemTags
setItemMetadata
setItemContent
setItemPreview
submitItemUpdate
getItemUpdateProgress
getNumSubscribedItems
getSubscribedItems
getItemState
downloadItem
getItemDownloadInfo
getItemInstallInfo
activateGameOverlayToWebPage (friends/overlay surface)
```

Expected callbacks/signals include create-item, item-update, and item-download completion. The implementation spike must pin the **exact 4.22 method parameter ordering and signal payloads** against the installed extension before feature code is written; the adapter contract tests then lock them.

### 6.2 Typed C# platform transport

Do not expose a broad `SteamService` God object. Apply interface segregation:

```csharp
public interface ISteamAvailability
{
    bool IsInstalled { get; }
    bool IsInitialized { get; }
    string? UnavailableReason { get; }
}

public interface ISteamWorkshopTransport
{
    bool IsAvailable { get; }

    Task<WorkshopCreateRemoteResult> CreateItemAsync(
        CancellationToken token);

    Task<WorkshopSubmitRemoteResult> SubmitUpdateAsync(
        WorkshopRemoteUpdate update,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token);

    Task<IReadOnlyList<PublishedWorkshopItem>> GetSubscribedItemsAsync(
        CancellationToken token);

    Task<WorkshopInstalledItemResult> EnsureInstalledAsync(
        ulong publishedFileId,
        IProgress<WorkshopTransferProgress>? progress,
        CancellationToken token);

    void OpenWorkshopBrowser();
    void OpenWorkshopItem(ulong publishedFileId);
}

public interface ISteamLeaderboardTransport
{
    bool IsAvailable { get; }
    Task<LeaderboardHandleResult> FindAsync(string apiName, CancellationToken token);
    Task<LeaderboardUploadResult> UploadKeepBestAsync(
        ulong handle, int score, IReadOnlyList<int> details, CancellationToken token);
    Task<IReadOnlyList<LeaderboardEntry>> DownloadFriendsAsync(
        ulong handle, CancellationToken token);
}
```

`ISteamWorkshopTransport` moves opaque folders/metadata between Desktop Buddy and Steam. It must not know `CharacterDocument`, `EnvironmentCanvas`, paint parts, or gameplay.

### 6.3 Operation serialization

Steam callbacks are global to the Steam API instance. Use a small operation actor/queue:

- one create/update publish operation at a time;
- one leaderboard lookup operation at a time;
- downloads can be correlated by `PublishedFileId` and may run concurrently only after that path is proven reliable;
- every operation has an internal GUID for logs and UI state;
- duplicate/out-of-order callbacks are ignored after terminal state;
- callback payload AppID and PublishedFileId must match the pending operation before completing a `TaskCompletionSource`.

No `async void` except Godot event entry points that immediately delegate to a task and observe errors.

### 6.4 Offline implementation

Provide:

```text
NullSteamWorkshopTransport       -> IsAvailable=false, typed Unavailable results
DirectoryWorkshopTransport       -> deterministic local emulator for tests/dev
NullSteamLeaderboardTransport
InMemoryLeaderboardTransport     -> deterministic fake friend scores for tests
```

The application chooses the real adapter only when GodotSteam initializes successfully. Steam being offline, the client not running, a missing extension, or a failed initialization never prevents bootstrap, Paint Room, Buddy Studio, saves, or gameplay.

---

## 7. Data-only Workshop content format

Use a **folder manifest**, not a nested ZIP, for Workshop.

### 7.1 Common manifest

`manifest.json` schema v1:

```json
{
  "schemaVersion": 1,
  "contentType": "room-painting",
  "formatId": "desktop-buddy-share",
  "minimumAppContentVersion": 1,
  "createdWithAppVersion": "...",
  "sourceId": "...",
  "files": [
    {
      "path": "environment/background.png",
      "sha256": "...",
      "encodedBytes": 12345
    }
  ]
}
```

Rules:

- manifest <= 64 KiB;
- UTF-8 JSON only;
- current schema exact match or explicit sequential migration;
- `contentType` is authoritative; Steam tags are discovery metadata only;
- only declared whitelisted relative paths;
- no absolute paths, `..`, alternate separators that escape root, duplicate normalized paths, links/reparse points, or undeclared files;
- SHA-256 every imported data file before decode;
- enforce encoded byte caps before decode;
- validate dimensions after decode;
- no paths from the manifest are ever used without canonical containment checks;
- a future schema version is reported as unsupported and not destructively rewritten;
- Workshop install directories are read-only/untrusted inputs.

### 7.2 Room painting item

Steam content folder:

```text
content/
  manifest.json
  environment/
    background.png
preview.png               # SetItemPreview source, not imported gameplay data
```

Validation:

- exactly 512x512 RGBA8 after decode;
- use the existing PNG codec / environment paint policy;
- one background PNG only;
- no wallpaper/furniture/environment layout data in v1.

Tags/metadata:

```text
DesktopBuddy.RoomPainting
FormatVersion.1
```

Steam item metadata may additionally contain a compact `desktop-buddy:room:1` discriminator so an in-game UGC query can filter without downloading the item, but the downloaded manifest remains authoritative.

### 7.3 Buddy Studio item

Steam content folder:

```text
content/
  manifest.json
  character.json
  paint/
    head.png              # only if declared/non-blank
    torso.png
    left_hand.png
    right_hand.png
    left_foot.png
    right_foot.png
preview.png
```

Validation order:

1. Validate the directory shape and manifest.
2. Check declared file lengths + SHA-256.
3. Decode/migrate `character.json` with `CharacterDocumentPolicy`.
4. Normalize then validate with the existing character policy.
5. Verify every paint path is one of `PaintPolicy.WhitelistedPaths`.
6. Enforce max 2 MiB per encoded part and 12 MiB aggregate.
7. Decode each declared PNG and require exactly 512x512 RGBA8.
8. Compile once using the current feature catalog to surface unsupported/fallback IDs as warnings.
9. Only after the entire package passes, create a fresh local character GUID and save it through `CharacterPaintStore`.

The Workshop author's original character GUID is **provenance only**. It must never become the receiving player's local identity and can never shadow/replace a local character with the same GUID.

Do not bundle project Resources used by generated cosmetics. Stable feature IDs may resolve to the receiving build's trusted catalog. Unknown IDs follow the existing preservation/fallback rules.

### 7.4 Local import identity and provenance

Imported content becomes an owned local copy:

```text
Room import:
user://shared_rooms/<new-guid>/
  room.json
  background.png
  workshop-provenance.json

Character import:
user://characters/<new-guid>/
  character.json
  paint/...
  workshop-provenance.json
```

Provenance contains only safe metadata such as:

```text
PublishedFileId
Steam time-updated value
imported UTC timestamp
manifest hash
source content type
```

Core gameplay/domain documents do not depend on PublishedFileId.

A successful imported local copy stays usable if the player later unsubscribes or goes offline. Unsubscribing cleans only Steam/cache state; it does not silently delete an imported local creation.

---

## 8. Staging and transaction model

Never point `SetItemContent` at a live character directory or current room save.

### Publish snapshot

```text
user://sharing/workshop/publish/<operation-guid>/
  content/
    manifest.json
    ...data snapshot...
  preview.png
```

Flow:

1. Read/capture a consistent local source snapshot.
2. Normalize/validate it using the same rules used for import.
3. Write all files into a unique staging directory.
4. Re-read and hash the staged files.
5. Generate preview on the main/render thread.
6. Only then call Steam Create/Update APIs with the staging paths.
7. Keep staging alive through Steam's terminal submit callback.
8. Cleanup after terminal success/failure or on next-start stale-operation recovery.

This prevents the user editing a character/room halfway through an upload from changing what Steam reads.

### Download/import

Never import directly from Steam's Workshop install folder.

```text
Steam install folder
 -> exact expected root containment checks
 -> copy into user://sharing/workshop/incoming/<operation-guid>/
 -> validate hashes/schema/images/domain data
 -> write to a brand-new local destination through existing stores
 -> only then expose in local library
```

This avoids time-of-check/time-of-use issues and ensures the active save tree is never partially populated from an untrusted folder.

---

## 9. Workshop publish state machine

```text
Idle
 -> Snapshotting
 -> Validating
 -> CreatingRemoteItem          (new item only)
 -> WaitingForCreateResult
 -> PreparingUpdate
 -> Submitting
 -> Uploading
 -> Published
 -> NeedsLegalAgreement         (terminal-success-with-action-required)

Any non-terminal state -> Failed / CancelledBeforeSubmit
```

Important Steam behavior: once `SubmitItemUpdate` starts, Valve documents no API to cancel that upload. A UI cancel after submit therefore means **stop waiting/showing modal**, not cancel the remote transfer. Keep observing the callback in the background of the running process and reconcile the item's status later.

Publishing fields:

- title: player-entered, locally length/safety validated;
- description: optional, player-entered;
- visibility: default Public only after legal/UX sign-off; during developer testing use private/friends-only as appropriate;
- tags: project-owned content type/version tags;
- metadata: compact content discriminator/schema version;
- content folder: immutable operation snapshot;
- preview: generated snapshot image.

For v1, support **Publish New** first. Updating an existing authored Workshop item is a second step once provenance/binding UX is accepted. When update is added, the local binding stores `PublishedFileId` outside core character/room documents, checks authorship through Steam query metadata, and uses the same snapshot pipeline.

---

## 10. Subscription/download/import state machine

The app does not need a full custom Workshop browser for the first version.

### Discovery MVP

- `Browse Steam Workshop` -> open the game's Steam Workshop in overlay.
- `My Subscriptions` -> in-game list from `GetSubscribedItems` plus item metadata query as needed.
- `Refresh` -> re-query subscription/install states.
- The Workshop view is lazy: do not enumerate/download/import during ordinary game startup.

### Per-item flow

```text
Subscribed
 -> InspectState
 -> InstalledAndCurrent ------------------+
 -> NeedsUpdate -> Downloading -> Installed|
                                            v
                                      CopyToStaging
                                            |
                                         Validate
                                      /           \
                              Quarantined       Importable
                                                  |
                                            Explicit Import
                                                  |
                                            Local Library
```

Rules:

- wait for the download-result callback before install-info access;
- verify callback AppID and PublishedFileId;
- never auto-activate content;
- never auto-overwrite a local item;
- invalid content stays out of the local libraries and surfaces a readable quarantine reason;
- source Workshop folders are never modified;
- if an installed subscribed item updates later, show `Update available` / `Re-import` rather than mutating the already imported local copy silently.

---

## 11. UI integration

Keep the Win98/Desktop Buddy presentation style; do not make Steam a mandatory front door.

### Paint Room

Add a focused `Workshop...` action with:

- Publish Current Painting
- Browse Workshop
- My Subscriptions
- Imported Room Paintings

Applying an imported room is explicit and uses the same environment persistence/runtime path as a locally painted room.

### Buddy Studio

Add a focused `Workshop...` / `Share...` action with:

- Publish Current Buddy
- Browse Workshop
- My Subscriptions
- Import selected subscribed buddy

Imported buddies appear in the normal local character library with a small provenance indicator. They are ordinary local copies after import and may be duplicated/edited under local identity.

### Availability states

Steam unavailable must be calm and non-blocking:

```text
Steam Workshop unavailable
Local painting and Buddy Studio still work normally.
```

Do not repeatedly modal-error during startup. Workshop UI can be disabled with a reason. Retry initialization only at deliberate lifecycle points / user action rather than every frame.

---

## 12. Security / trust model

Workshop content is hostile input even if Steam delivered it successfully.

### Required protections

- Data-only formats: JSON + PNG.
- No ResourceLoader on Workshop files.
- No `GD.Load` on Workshop paths.
- No scenes/scripts/shaders/native content.
- Canonical containment checks for every path.
- Exact filename whitelist.
- Reparse point/symlink rejection.
- Encoded byte caps before image decode.
- Existing image dimension/pixel-format validation after decode.
- SHA-256 manifest verification.
- Aggregate byte cap.
- Copy to private staging before validation/import.
- Fresh local GUID on every character import.
- No automatic activation.
- Unsupported future schema -> safe refusal, no destructive migration.
- Detailed validation result for UI/logs; never throw an unhandled exception through the game loop.

### Moderation/product policy gate

Before public Workshop launch, owner decisions must be recorded for:

- allowed UGC/content rating wording;
- reporting instructions / Steam report deep link;
- whether Desktop Buddy also keeps a local hidden-item list;
- title/description profanity handling, if any beyond Steam's own systems;
- legal/TOS copy;
- whether imported items expose author Steam name/ID in-game;
- whether Workshop preview screenshots may include user-drawn offensive content in the in-game list.

Steam's Workshop page/reporting remains the primary moderation system; Desktop Buddy should not build a custom moderation backend for this scope.

---

## 13. Future friends leaderboard — 30-second Damage Sprint

This feature needs **Steam Leaderboards**, not multiplayer networking.

### 13.1 Steam configuration

Create a versioned leaderboard in Steamworks App Admin, e.g.:

```text
desktop_buddy_damage_30s_v1
Sort: Descending
Display: Numeric
Writes: client/untrusted (required if there is no backend)
Reads: Friends if owner wants friend-only read enforcement
```

Steam leaderboard scores are signed 32-bit integers. Each player has one entry. Steam supports `KeepBest`, which is appropriate for a high-score challenge. `DownloadLeaderboardEntries` with `k_ELeaderboardDataRequestFriends` returns entries for friends of the current user and ignores the range arguments.

If challenge balance/scoring rules materially change, create `..._v2`; never mix incomparable rule sets in the same leaderboard.

### 13.2 Challenge architecture

Add a dedicated challenge coordinator rather than bolting a timer onto normal sandbox play:

```text
DamageSprintRuleset v1
 -> reset/prepare challenge state
 -> exactly 30.000 s of simulation-clock challenge time
 -> listen to accepted damage/pain events
 -> accumulate score in deterministic fixed-point units
 -> terminal score snapshot
 -> upload KeepBest
 -> fetch friend entries
```

The score must derive from the game's **accepted damage/pain semantic event**, not raw collision contacts and not money/reward payouts. Existing architecture already has `AcceptedPainEvent`; the challenge should consume that semantic boundary read-only.

The challenge must not modify persistent money, mood history, tool progression, achievements, or the player's normal buddy state unless separately approved. Entering/exiting should snapshot/restore or use an isolated challenge scene/composition.

### 13.3 Fairness decisions required before implementation

Owner must lock a v1 ruleset for:

- which tools are available during the 30 seconds;
- whether challenge grants a fixed temporary loadout independent of progression;
- buddy initial pose/state;
- whether fire/status damage after the timer boundary counts;
- whether knockout state changes scoring;
- whether current window/room size is normalized for the challenge;
- exact conversion from accepted damage/pain to the int32 Steam score.

Recommendation: use a dedicated standardized ruleset and temporary loadout so friend scores compare the same challenge rather than the player's progression state.

### 13.4 Anti-cheat limitation

With no authoritative backend, a client-written Steam leaderboard cannot be cheat-proof. Local guardrails can ensure ordinary gameplay only uploads scores produced by a completed challenge session, but a modified client can still fabricate a score.

Options:

1. **Casual friends leaderboard** — client writes, `KeepBest`, simple local validation. Lowest complexity and fits this game's asynchronous social scope.
2. **Trusted leaderboard later** — Steam's Trusted setting requires server-side Web API score submission, which means introducing a backend/authentication/validation service. Do not add this unless competitive integrity becomes a real product requirement.

Do not add kernel anti-cheat, replay verification, or networking solely for this casual feature.

---

## 14. Implementation tasks and sequencing

### G0 — Source-of-truth and Steamworks gate

**No production Steam code before this task.**

Update after owner scheduling/approval:

- `docs/DECISIONS.md`
- `AGENTS.md`
- `docs/PRODUCT_REQUIREMENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/TEST_PLAN.md`
- `docs/ROADMAP.md`
- old `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` with a cross-reference that its Steamworks.NET provider choice is superseded by this plan

Record Workshop moderation/legal decisions and exact AppID configuration status.

Done when an agent can implement G1 without conflicting with the active milestone rules.

### G1 — GodotSteam 4.22 dependency spike + bridge contract

Add:

- pinned dependency manifest/setup scripts;
- `GodotSteamBridge.gd`;
- a C# bridge adapter and capability result types;
- null/local transport;
- development-only Steam status diagnostics.

Acceptance:

- Godot 4.6.1 standalone can initialize GodotSteam 4.22 when launched through Steam;
- game boots normally with Steam client closed/unavailable;
- bridge pumps callbacks during paused editor UI;
- expected 4.22 UGC methods/signals are runtime capability-checked;
- no Steam SDK runtime or `steam_appid.txt` becomes tracked;
- CI pure/domain tests require no Steam client.

### G2 — Pure share manifests, validation, and staging

Add under engine-free/domain ownership where possible:

```text
domain/DesktopBuddy.Domain/Sharing/
  ShareContentType.cs
  ShareManifest.cs
  ShareManifestPolicy.cs
  ShareValidationResult.cs
  Sha256FileEntry.cs

src/Persistence/Sharing/
  WorkshopStagingStore.cs
  WorkshopProvenanceStore.cs
  RoomPaintingLibraryStore.cs
```

Tests:

- path traversal;
- duplicate normalized path;
- unknown/extra file;
- symlink/reparse;
- bad hash;
- oversized manifest;
- unsupported schema;
- wrong content type;
- truncated PNG;
- wrong dimensions;
- per-part/aggregate buddy paint caps.

### G3 — Room export/import pipeline

Add:

```text
src/Sharing/RoomShareExporter.cs
src/Sharing/RoomShareImporter.cs
```

Acceptance:

- active room -> staged v1 folder -> validate -> import -> pixel-identical local room preset;
- importing never changes active room until explicit Apply;
- corrupt Workshop source cannot alter current room;
- imported room remains usable offline.

### G4 — Buddy Studio export/import pipeline

Add:

```text
src/Sharing/CharacterShareExporter.cs
src/Sharing/CharacterShareImporter.cs
```

Acceptance:

- configuration + all declared nonblank paint surfaces round-trip;
- import runs existing character decode/normalize/validate/compile path;
- fresh local GUID every import;
- GUID collision cannot shadow local character;
- unknown stable cosmetic ID follows existing safe fallback policy;
- package cannot introduce Resource/mesh/script content;
- local character activation remains explicit.

### G5 — Workshop publish service

Add `WorkshopSharingCoordinator` and publish UI.

Acceptance:

- room publish-new through real Steam test account;
- character publish-new through real Steam test account;
- immutable staging folder is used for `SetItemContent`;
- preview captured from room/Buddy Studio presentation;
- legal agreement flag handled;
- overlay opens resulting Workshop item;
- Steam failure does not alter local content;
- upload progress can be shown;
- application shutdown with an in-flight upload leaves recoverable stale staging and no save corruption.

### G6 — Subscriptions, download, and import UI

Acceptance:

- account B subscribes to account A's room and buddy item;
- Desktop Buddy lists subscribed items;
- `NeedsUpdate` downloads and waits for callback;
- install path is copied to project staging;
- valid item imports;
- invalid/tampered item is quarantined with reason;
- no auto-activation;
- unsubscribing does not delete already imported local content;
- Workshop UI works after reconnect/refresh without restarting the game where Steam permits it.

### G7 — Hardening and release matrix

Automated emulator tests:

- offline init;
- unsupported GodotSteam capability set;
- duplicate callbacks;
- callback for wrong AppID/file ID;
- create success + submit failure;
- legal agreement required;
- download interrupted then retried;
- Steam install folder disappears mid-import;
- current subscription update vs already imported copy;
- 100+ fake subscriptions paging/list behavior;
- app exits between snapshot and submit;
- stale staging cleanup.

Manual Steam depot matrix:

```text
Account A: create/publish room
Account B: subscribe/download/import room
Account A: create/publish buddy with all six paint files
Account B: subscribe/download/import buddy
Account B: use imported buddy offline
Account A: publish changed item/update flow when G5 update support lands
Account B: receive NeedsUpdate/re-import prompt
Legal-agreement-not-accepted account
Steam client offline / network offline
Windows 10 x86_64
Windows 11 x86_64
```

### L0 — Future leaderboard policy/rules gate

Only when the feature is scheduled: lock `DamageSprintRuleset v1` and Steam App Admin leaderboard configuration.

### L1 — Challenge session and pure score model

Implement challenge timer/state isolation and deterministic int32 score conversion with no Steam dependency. Unit/headless test timing boundaries and score accumulation.

### L2 — GodotSteam leaderboard adapter

Bridge the GodotSteam wrappers for Steam User Stats/Leaderboards, pin exact 4.22 methods/signals through the same capability-contract approach, and expose `Find`, `UploadKeepBest`, `DownloadFriends`.

### L3 — Friends leaderboard UI

Show own best plus friend entries. If Steam is unavailable, the challenge itself may still run locally but upload/friend results are unavailable.

### L4 — Leaderboard verification

Two-account friend matrix, KeepBest behavior, no-score friend handling, offline completion/retry UX, balance-version migration to a second API name.

---

## 15. Testing strategy

### Pure unit tests

Keep all package/schema/hash/path logic engine-free. No Steam client or Godot needed.

### Headless Godot tests

Use `DirectoryWorkshopTransport` / fake bridge signals. CI must never publish or mutate live Workshop items.

Candidate scenarios:

```text
workshop_bridge_offline_fallback
workshop_room_roundtrip
workshop_character_roundtrip
workshop_character_guid_collision
workshop_invalid_hash_quarantine
workshop_path_traversal_rejected
workshop_oversize_paint_rejected
workshop_callback_correlation
workshop_subscription_update_state
workshop_import_never_auto_activates
workshop_import_survives_unsubscribe
```

Future leaderboard:

```text
damage_sprint_exact_30_seconds
damage_sprint_score_semantics
leaderboard_keep_best_fake
leaderboard_friends_only_fake
leaderboard_offline_completion
```

### Real Steam tests

Live API tests are manual/depot tests because they mutate persistent Steam state and require authenticated accounts. Keep dedicated test items clearly named and clean them through Steam UI/API after the matrix.

---

## 16. Logging and diagnostics

Structured Steam logs should include:

- operation GUID;
- operation kind;
- AppID;
- PublishedFileId where available;
- Steam `EResult` / bridge status;
- state transition;
- bytes processed/total;
- validation error code.

Do not log:

- credentials/tokens;
- raw personal Steam profile data unnecessarily;
- full arbitrary user-authored descriptions;
- absolute local user paths in normal telemetry.

Add a development-only Steam diagnostics panel/command that shows:

```text
GodotSteam expected: 4.22
Steam singleton present: yes/no
Steam initialized: yes/no
AppID: ...
Workshop transport: real/local/null
Pending operations: N
Last Steam result: ...
```

---

## 17. Performance and lifecycle rules

- `run_callbacks()` once per application/render frame, never 120 Hz physics authority work.
- No Workshop file IO, hashing, PNG encode/decode, JSON work, Steam queries, or async waits on the fixed physics tick.
- Snapshot PNG encoding/hashing runs off the physics path using the existing filesystem/codec boundaries.
- Godot texture/viewport preview capture remains main-thread/render-thread coordinated.
- Do not scan subscriptions during normal startup.
- Do not decode Workshop previews/content for off-screen list rows eagerly.
- Coalesce refresh requests and use cancellation tokens for local staging/validation work.
- Steam submit itself is not cancellable after submit; represent this truth in the state model.

---

## 18. Failure behavior

| Failure | Required behavior |
| --- | --- |
| GodotSteam absent | Game/local editors operate normally; Workshop disabled |
| Steam client not running | Same; non-modal status |
| Steam init failure | Same; log typed reason |
| Offline after init | Pending remote op fails/retries explicitly; local data untouched |
| CreateItem succeeds, submit fails | Retain PublishedFileId in pending binding so retries update same empty/item rather than spam new items |
| Legal agreement required | Mark item `NeedsLegalAgreement`; open item page/TOS action |
| Download interrupted | No import; retry from Steam state |
| Download callback wrong AppID/item | Ignore; never complete wrong task |
| Installed source invalid | Quarantine result; never load Resource; local libraries untouched |
| Imported item conflicts with local GUID | Fresh local GUID; no overwrite |
| Unknown cosmetic ID | Existing character fallback/preservation policy |
| Future manifest schema | Refuse as unsupported; do not mutate source |
| Unsubscribe after import | Keep local imported copy |
| Steam unavailable during gameplay | Zero gameplay behavior change |

---

## 19. Modern design-pattern mapping

This plan intentionally uses small patterns already compatible with Desktop Buddy's architecture:

- **Ports and adapters / hexagonal boundary:** Steamworks is an external adapter behind interfaces.
- **Anti-corruption layer:** GDScript bridge + C# adapter prevents GodotSteam's dynamic API from leaking inward.
- **State machine:** explicit publish/download states make asynchronous callback behavior observable/testable.
- **Command/operation queue:** serializes ambiguous global callback families and provides correlation IDs.
- **Transactional staging:** validate complete snapshots before remote upload/local commit.
- **Copy-on-import:** Workshop subscription/cache cannot become local save authority.
- **Schema versioning:** content formats and challenge leaderboards are versioned independently.
- **Capability probing:** pin GodotSteam 4.22 behavior at runtime instead of assuming extension compatibility.
- **Null object / local emulator:** single-player and CI do not depend on Steam availability.
- **Interface segregation:** Workshop and leaderboard APIs stay separate; no generic multiplayer manager.

Avoid:

- service locator;
- global mutable `SteamManager` with gameplay knowledge;
- generic event bus;
- direct GodotSteam calls from UI/editor code;
- direct Workshop paths in CharacterDocument;
- networking abstractions for non-network features.

---

## 20. Research references verified 2026-08-25

### Godot / GodotSteam

- Godot Asset Library — GodotSteam GDExtension 4.4+ v4.22 (2026-08-22):  
  https://godotengine.org/asset-library/asset/2445
- Godot proposal #8191 — Automatically generate C# bindings for GDExtensions; documents GodotSteam/GDScript bridge limitation:  
  https://github.com/godotengine/godot-proposals/issues/8191
- Community GodotSteam C# bindings README — useful evidence of a wrapper approach, but currently states GodotSteam plugin 4.6.1 support and is therefore not the chosen dependency boundary:  
  https://github.com/LauraWebdev/GodotSteam_CSharpBindings/blob/dev/README.md
- GodotSteam documentation root (pin exact 4.22 signatures during G1):  
  https://godotsteam.com/

### Valve Steamworks

- Steam Workshop Implementation Guide:  
  https://partner.steamgames.com/doc/features/workshop/implementation
- `ISteamUGC`:  
  https://partner.steamgames.com/doc/api/ISteamUGC
- Steam Leaderboards overview:  
  https://partner.steamgames.com/doc/features/leaderboards
- `ISteamUserStats` leaderboard APIs:  
  https://partner.steamgames.com/doc/api/ISteamUserStats

---

## 21. Exit criteria for the Workshop feature

Workshop is ready to merge/release only when all are true:

1. Local gameplay/editing boots and functions with Steam unavailable.
2. No real-time networking dependency exists.
3. GodotSteam is pinned and version capability-checked.
4. No prohibited Steam runtime/dev files are tracked.
5. Room Workshop round-trip is pixel-identical through existing 512x512 policy.
6. Buddy Workshop round-trip preserves normalized configuration and all declared paint pixels.
7. Every imported character gets a fresh local GUID.
8. Workshop content cannot inject Godot Resources/scenes/scripts/native code.
9. Invalid UGC cannot modify active local content.
10. No subscribed item auto-activates.
11. Imported content remains usable offline.
12. Legal agreement flow and Steam overlay item page work.
13. Account A -> account B real-depot publish/subscribe/import matrix passes on Windows 10/11.
14. Existing domain, headless scenario, journey, and Steam-binary guard suites remain green.
15. Owner accepts the Workshop UI and moderation/legal copy.

The future leaderboard has a separate exit gate and must not block Workshop shipping.
