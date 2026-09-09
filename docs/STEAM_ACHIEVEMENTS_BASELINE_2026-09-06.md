# Desktop Buddy — Steam Achievements Baseline

Status: **Owner-approved baseline (2026-09-06), distribution scope amended 2026-09-08**

This document is the authoritative launch achievement list for the current implementation. It
supersedes the older ten-achievement list in FR-018.5–FR-018.14 where the two conflict.

## Distribution contract

- Full-game Steam AppID: `5114950`.
- Steam Demo AppID: `5228990`.
- Create the achievement definitions below in **the full game's Steamworks app only**.
- **itch.io and the Initial Steam Demo do not ship the Desktop Buddy achievement implementation.**
  Their game/domain assemblies must physically omit the catalog, qualification rules, counters,
  reconciler, publisher, and Steam achievement adapter; this is compile-time distribution scope,
  not a runtime UI flag.
- The **Steam Next Fest Demo** is the first demo build that ships achievements. It evaluates the
  conditions below and stores local qualification in `progress.json`, but it must never call
  Steam's achievement-unlock API under Demo AppID `5228990`.
- When the full game runs with the shared/carried Next Fest save, every locally-qualified
  achievement is reconciled to Steam as desired state.
- A newly changed desired set receives an immediate publish attempt. A failed unchanged batch backs
  off at 60s, 120s, 240s, then 300s. A successful batch becomes an in-process no-op until another
  achievement qualifies. This respects Steam's `StoreStats` rate-limit guidance while still
  publishing genuine unlocks promptly.
- If the GodotSteam addon and binding surface are valid and `steamInitEx` specifically reports
  `NoConnection` (status `2`) at startup, the shared Steam composition keeps the same bridge and
  Workshop transport alive and retries at 30s, 60s, 120s, 240s, then 300s. Recovery makes Workshop
  and full-game achievement reconciliation available in the same process without rebinding
  services. Generic initialization failure, client-update-required, configuration, and capability
  failures are not polled indefinitely.
- `Reset Progress` in an achievement-enabled build keeps achievements that are already qualified,
  because Steam achievements cannot be revoked and Next-Fest-qualified awards still need to
  reconcile. Partial counters/working state reset with ordinary progress.
- Local achievement IDs and Steam API names are permanent persisted/platform identifiers. Do not
  rename or reuse them after release; add a new achievement instead.

### Build-scope contract

The achievement project seam intentionally matches the distribution planning/hardening work:

| Build | MSBuild selection | Achievement implementation |
|---|---|---|
| itch.io | `DesktopBuddyItchScope=true` | **Excluded** |
| Initial Steam Demo | `DesktopBuddySteamDemoScope=true`, `DesktopBuddyNextFestDemoScope=false` | **Excluded** |
| Steam Next Fest Demo | `DesktopBuddySteamDemoScope=true`, `DesktopBuddyNextFestDemoScope=true` | **Included; local qualification only** |
| Full Release | `DesktopBuddySteamDemoScope=false` | **Included; full Steam publishing** |

`DesktopBuddyAchievementsScope` is the derived capability. Release tooling may set it explicitly,
but normal profile selection should derive it from the properties above. Developer/test builds keep
it enabled by default.

Low-scope builds compile a tiny inert `AchievementBootstrap.Absent` composition seam only so shared
Workshop composition does not need a second source tree. The actual achievement engine is absent.
CI verifies the compiled DLLs do not contain stable implementation sentinels such as
`ACH_FIRST_IMPRESSION`, `AchievementCoordinator`, `AchievementReconciler`,
`SteamAchievementPublisher`, or `GodotSteamAchievementRemote`.

The Initial and Next Fest demos are expected to remain the same Steam Demo AppID; changing a Godot
feature tag in an Initial Demo artifact must therefore never be enough to materialize achievements.

## Baseline — 24 achievements

| # | Name | Steam API name | Hidden | Qualification |
|---:|---|---|:---:|---|
| 1 | First Impression | `ACH_FIRST_IMPRESSION` | No | Earn positive damage money for the first time. |
| 2 | Lights Out | `ACH_LIGHTS_OUT` | No | Cause the first knockout. |
| 3 | Retail Therapy | `ACH_RETAIL_THERAPY` | No | Buy the first launch tool/care item. |
| 4 | Full Toybox | `ACH_FULL_TOYBOX` | No | Own every entry in the authoritative launch interaction catalogue. |
| 5 | Best Friends | `ACH_BEST_FRIENDS` | No | Reach `+100` mood. |
| 6 | Forgiven | `ACH_FORGIVEN` | No | Trigger the trust reset that clears harmful history. |
| 7 | Nice Catch | `ACH_NICE_CATCH` | No | Reach 25 successful catches. |
| 8 | Variety Hour | `ACH_VARIETY_HOUR` | No | Use every authoritative launch interaction at least once. |
| 9 | Fire Drill | `ACH_FIRE_DRILL` | Yes | Clear Burning by having Buddy use the Repair Kit. |
| 10 | Desktop Shift | `ACH_DESKTOP_SHIFT` | No | Accumulate 2 hours of running time. |
| 11 | Air Bud | `ACH_AIR_BUD` | Yes | Keep Buddy airborne for 30 continuous seconds in Play without actively grabbing them. Paused/editor/Work time does not count. |
| 12 | Bank Shot | `ACH_BANK_SHOT` | Yes | Hit Buddy with a thrown baseball after that same baseball has touched a side wall. |
| 13 | Character Arc | `ACH_CHARACTER_ARC` | Yes | Reach `-100` mood and later `+100` while the same Buddy/character is active. |
| 14 | Employee of the Day | `ACH_EMPLOYEE_DAY` | No | Reach 100 lifetime Work Mode actions. |
| 15 | Employee of the Week | `ACH_EMPLOYEE_WEEK` | No | Reach 1,000 lifetime Work Mode actions. |
| 16 | Employee of the Month | `ACH_EMPLOYEE_MONTH` | No | Reach 10,000 lifetime Work Mode actions. |
| 17 | Employee of the Year | `ACH_EMPLOYEE_YEAR` | No | Reach 100,000 lifetime Work Mode actions. |
| 18 | Employee for Life | `ACH_EMPLOYEE_FOR_LIFE` | No | Reach 1,000,000 lifetime Work Mode actions. |
| 19 | Make It Yours | `ACH_MAKE_IT_YOURS` | No | For the same Buddy/character, register Buddy Studio, Paint Buddy, Paint Background, and Environment Decorator customization. |
| 20 | Punching Bag | `ACH_PUNCHING_BAG` | No | Land 100 accepted positive-pain Boxing Glove hits. |
| 21 | Fully Dressed | `ACH_FULLY_DRESSED` | No | Have headwear, a top, shoes, and glasses equipped at the same time. |
| 22 | Home Sweet Home | `ACH_HOME_SWEET_HOME` | No | Place/save at least one decoration from every Environment Decorator category across room edits. |
| 23 | Try Everything Once | `ACH_TRY_EVERYTHING_ONCE` | No | Cause positive pain with every damaging launch tool at least once. |
| 24 | Rube Goldberg Would Be Proud | `ACH_RUBE_GOLDBERG` | Yes | Damage Buddy with three different semantic damage sources within a rolling five-second window. |

The damaging-tool set for **Try Everything Once** is intentionally separate from Variety Hour and
currently consists of Boxing Glove, Baseball, Baseball Bat, Nerf Blaster, Pistol, Soccer Ball,
Grenade, Shotgun, Fire Sprayer, and Sword. The code uses stable content IDs, not display names.

## Implementation ownership

The implementation follows a local-first **Ports & Adapters / desired-state reconciliation** shape:

- `domain/DesktopBuddy.Domain/Achievements/AchievementCatalog.cs` owns local IDs, Steam API names,
  player-facing names/descriptions, hidden flags, and the damaging-tool set.
- `domain/DesktopBuddy.Domain/Achievements/AchievementProgressStore.cs` owns typed access to
  qualification and bounded working state under the versioned `achievements.v1.*` extension
  namespace in `progress.json`.
- `domain/DesktopBuddy.Domain/Achievements/AchievementCoordinator.cs` is the pure rule engine. It
  converts semantic observations and durable progress into monotonic qualification and has no
  Godot or Steam dependency.
- `domain/DesktopBuddy.Domain/Achievements/AchievementReconciler.cs` owns the idempotent desired-state
  reconciliation algorithm behind the `IAchievementRemote` platform port.
- `src/Platform/Steam/GodotSteamAchievementRemote.cs` is the GodotSteam adapter. It enforces the
  `steam + full_release + 5114950` publishing boundary and delegates only `setAchievement` and
  `storeStats` through the project-owned GodotSteam bridge.
- `src/Achievements/AchievementBootstrap.cs` observes existing gameplay/character/environment events
  and translates them into semantic achievement observations; it does not own qualification rules.
- Paint Background attribution is explicitly event-driven: `EnvironmentBackgroundEditor` emits a
  semantic commit only after a genuinely changed canvas is successfully saved,
  `EnvironmentCustomizationBootstrap` exposes that through `IEnvironmentCustomizationEvents`, and
  achievements consume that injected port. Achievement code does not poll the PNG or inspect editor UI.
- `src/Achievements/SteamAchievementPublisher.cs` supplies monotonic retry/backoff scheduling around
  the domain reconciler. It never owns gameplay state or achievement rules.
- `src/Sharing/WorkshopBootstrap.cs` owns the single live GodotSteam bridge and composes Workshop and
  achievements around it. Retryable Steam-client initialization is recovered in place with bounded
  backoff; no second Steam initialization object or service-locator lookup is introduced.
- `DesktopBuddy.csproj` and `DesktopBuddy.Domain.csproj` own the compile-time inclusion boundary.
  `devtools/verification/verify_achievement_scope.py` is the artifact-level negative/positive check.

This separation is intentional: pure rules and reconciliation are covered by ordinary `dotnet test`;
GodotSteam binding compatibility remains covered by the native-addon smoke scenario.

The Initial Steam Demo still needs the same native GodotSteam addon for Workshop. That third-party
addon inherently exposes Steamworks stats/achievement APIs; the distribution boundary guarantees
that **Desktop Buddy's achievement catalog/rules/state/publisher/adapter are absent**, not that Valve
or GodotSteam's general-purpose binary has been surgically rebuilt to remove those APIs.

## Steamworks setup

In Steamworks for AppID `5114950`:

1. Create exactly 24 achievements using the Steam API names in the table above.
2. Copy the display names and qualification descriptions above (or localized player-facing copy
   with the same meaning) into the Steam achievement definitions.
3. Mark only the five rows labelled Hidden as hidden: Fire Drill, Air Bud, Bank Shot,
   Character Arc, and Rube Goldberg Would Be Proud.
4. Upload locked/unlocked artwork for every achievement before release.
5. Publish the Steamworks changes.
6. Do **not** duplicate these definitions under Demo AppID `5228990`; the Next Fest Demo's job is
   local qualification only. The Initial Steam Demo contains no achievement implementation.

## Release verification

Before merge/release, verify all of the following:

- `dotnet build DesktopBuddy.sln -c Debug` passes.
- `dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj -c Debug --no-build`
  passes, including the 24-definition catalog, Work thresholds, cumulative/trick rules, reset
  semantics, Character Arc identity handling, and reconciliation idempotency/failure replay.
- The four-profile `Achievement Build Scope` CI matrix passes: itch + Initial Demo exclude the
  implementation; Next Fest + Full include it.
- In a Steam Next Fest Demo build, qualifying an achievement changes `progress.json` but does not
  create a Steam achievement unlock for AppID `5228990`.
- Launching the full game with that carried qualification unlocks the matching achievement under
  AppID `5114950` after Steam becomes available.
- Starting the full game while GodotSteam reports `NoConnection` still qualifies locally; if the
  Steam client becomes reachable while the process remains open, shared Steam initialization
  recovers in-session and the qualified achievement reconciles without restarting. Other permanent
  initialization failures remain local-first and reconcile on a later valid launch.
- Reset Progress keeps qualified awards but clears partial achievement counters and working values.
- A Steam overlay pause, Work Mode, and editor time cannot advance Air Bud.
- Bank Shot requires the same thrown baseball to touch a side wall before its accepted Buddy hit.
- Rube Goldberg requires three distinct semantic damage source IDs inside five seconds.
- Repeated runtime polling after a successful Steam reconciliation causes no additional
  `SetAchievement`/`StoreStats` calls until local qualification changes.
- Paint Background credit for Make It Yours is raised only by a successful dirty editor commit;
  opening/saving unchanged art, Reset Progress, and applying a Workshop room do not grant it.

Steamworks configuration remains an external release step; the repository intentionally contains no
Steam credentials or proprietary Steam SDK binaries.
