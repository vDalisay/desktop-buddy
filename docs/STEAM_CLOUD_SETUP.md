# Steam Cloud setup — Desktop Buddy

Desktop Buddy uses **Steam Auto-Cloud**. The Steam client synchronizes the selected local files before launch and after exit; the game does not upload saves through `ISteamRemoteStorage` itself.

## Product identities

- Full game App ID: `5114950`
- Demo App ID: `5228990`
- Godot `user://` custom directory: `DesktopBuddy`
- Windows local root: `%APPDATA%\DesktopBuddy`

Both Steam exports use the same Godot custom user directory. This is intentional: on the same PC, installing the full game after playing the demo already sees the same local progress and authored Buddy data even when Steam Cloud is unavailable.

## What belongs in Cloud

Cloud-safe player state:

- `progress.json` — money, unlocks, selected tool/character, mood/history, work progress, environment/decorator progress, counters and other semantic progression.
- `characters/<character-id>/character.json` — Buddy Studio/custom-character document.
- `characters/<character-id>/paint/*.png` — authored Buddy paint surfaces.
- `environment/background.png` — the active painted room background.

Keep these local only:

- `settings.json` — window position/size, monitor, UI scale/colors, audio/display and other machine preferences.
- `*.bak`, `*.tmp`, `*.invalid-*`, `*.corrupt-*` — recovery artifacts.
- `shared_rooms/**` — Workshop-derived local room copies, not authoritative save state.
- `sharing/workshop/**` — Workshop staging/provenance/cache data.
- `steam_appid.txt`, logs and diagnostics.

The code contract for this list is `SteamCloudSavePolicy`; unit tests intentionally fail if the canonical policy changes without being reviewed.

## Quota

Workshop preview images already require a non-zero Cloud quota for both App IDs. Keeping the currently recommended values is sufficient for both Workshop previews and saves:

- Byte quota per user: `1073741824` (1 GiB)
- Number of files allowed per user: `1000`

Save and publish the Steamworks changes after editing Cloud settings.

## Auto-Cloud root paths

Configure the same four Auto-Cloud rows on **both** App `5114950` and App `5228990`.

All four use Root = `WinAppDataRoaming` and OS = `Windows`.

| # | Root | Subdirectory | Pattern | Recursive |
|---|---|---|---|---|
| 1 | `WinAppDataRoaming` | `DesktopBuddy` | `progress.json` | No |
| 2 | `WinAppDataRoaming` | `DesktopBuddy/characters` | `character.json` | Yes |
| 3 | `WinAppDataRoaming` | `DesktopBuddy/characters` | `*.png` | Yes |
| 4 | `WinAppDataRoaming` | `DesktopBuddy/environment` | `background.png` | No |

Do **not** use one broad `DesktopBuddy / * / Recursive` rule: that would sync `settings.json`, save backups and Workshop cache/staging files as well.

## Demo -> full-game transition

Valve supports a `Shared cloud APP ID` specifically for cases such as demos carrying saves into a full game.

Desired release-state configuration:

- Full game `5114950`: `Shared cloud APP ID = 0`
- Demo `5228990`: `Shared cloud APP ID = 5114950`

That makes the demo write/read the full game's Cloud storage, so after purchase the full game receives the same save files.

### Important pre-release limitation

Valve documents that shared Cloud data should not be used against an **unreleased target/base app**: those files will not sync. Desktop Buddy's full game is currently unreleased, so do not rely on the cross-App cloud handoff yet.

Until the full game is released:

1. Keep the Demo's `Shared cloud APP ID` at `0` while validating ordinary Demo Cloud sync.
2. The Demo and full game still share `%APPDATA%\DesktopBuddy` locally, so same-PC upgrade testing works now.
3. When the full game is released, set Demo `Shared cloud APP ID = 5114950`, publish the Steamworks change, and run the final cross-App/cloud-machine acceptance test.

If the demo launches publicly before the full game, preserve this distinction in the release checklist: normal Demo Cloud can be enabled, but the shared-App handoff is activated only when Valve's released-base-app prerequisite is satisfied.

## Testing ordinary Cloud sync now

Use a Steam account with the appropriate Developer Comp license.

1. Publish the Cloud settings for the App ID under test.
2. Restart Steam or allow several minutes for the published configuration to propagate.
3. Open the Steam console (`steam://open/console`).
4. Run `testappcloudpaths 5228990` for the Demo, or `testappcloudpaths 5114950` for the full game.
5. Run `set_spew_level 4 4`.
6. Launch Desktop Buddy through Steam.
7. Change progress, create/edit a Buddy, paint that Buddy, and paint the room background.
8. Exit the game cleanly and wait for Steam Cloud synchronization to finish.
9. Inspect `%Steam Install%\logs\cloud_log.txt` if synchronization does not occur.
10. Test on a second Windows PC (or a clean Windows user profile): Steam should restore the four cloud-safe groups before launch while retaining that machine's own `settings.json`.
11. Finish with `testappcloudpaths 0` and `set_spew_level 0 0`.

## Final demo -> full acceptance test (after full game release)

After setting Demo `Shared cloud APP ID = 5114950` and publishing it:

1. On PC A, launch the Demo and create recognizable state: unique Buddy name/paint, room paint, money/unlocks.
2. Exit and wait for Cloud to report synchronized.
3. On PC B, install the full game without copying `%APPDATA%\DesktopBuddy` manually.
4. Launch the full game.
5. Confirm progress, Buddy document/paint and room background are present.
6. Confirm machine-local settings are **not** copied from PC A.
7. Make progress in the full game, exit, then relaunch the Demo only for a controlled compatibility check if the Demo remains available; both apps must interpret the shared schema safely.

## Conflict policy

Steam Auto-Cloud owns cross-machine conflict handling before the process starts. Desktop Buddy continues to use its existing atomic primary/backup save logic locally. Only canonical primaries are clouded; backup/quarantine files stay local so a stale recovery artifact from another machine cannot overwrite a healthy primary.
