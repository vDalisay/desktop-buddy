# Desktop Buddy SteamPipe Setup

This document covers the repository-side and Steamworks-side setup for publishing the Windows Steam build of Desktop Buddy.

## Repository state

Desktop Buddy already has the two Godot export presets Steam needs:

- `Windows Full Release` — full Steam product, `full_release,steam` features.
- `Windows Steam Demo` — Steam demo scope, `steam` feature without `full_release`.

The base-game Steam App ID is `5114950`. GodotSteam 4.22 is materialized at build time by the pinned/hash-verified `tools/install_godotsteam.ps1`; Valve/GodotSteam binaries and development `steam_appid.txt` remain untracked.

`.github/workflows/steam-pipe.yml` adds a manual, Linux-hosted managed Windows export and optional SteamPipe upload. It never uploads on push or pull request. The separate NativeAOT spike is manual research and does not change this production path.

## 1. Configure the full game in Steamworks

In the Steamworks App Admin for Desktop Buddy (`5114950`):

1. Open **Installation > General Installation**.
2. Give the app an install directory, for example `DesktopBuddy`.
3. Add a Windows launch option whose executable is `DesktopBuddy.exe`.
4. Open **SteamPipe > Depots**.
5. Create or identify the x86_64 Windows depot for the game.
6. Give the depot a recognizable name such as `Desktop Buddy - Windows`.
7. Leave language as **All Languages** unless there is a future reason to split localization into separate depots.
8. Make sure the depot is included in the package(s) that should install the game.

Record the Windows Depot ID.

## 2. Configure the Steam demo separately

Valve demos are separate applications associated with the base game. Do not reuse the base App ID as the final public demo identity.

From the Desktop Buddy base-app landing page:

1. Open **All associated packages, DLC, demos and tools**.
2. Choose **Add Demo**.
3. In the new demo app, confirm application type **Demo** and link it to base App ID `5114950`.
4. Configure the demo's install directory and Windows launch option (`DesktopBuddy.exe`).
5. Create or identify the demo's Windows depot.
6. Make sure the demo depot is attached to the demo package.

Record the Demo App ID and Demo Windows Depot ID.

The game keeps Workshop ownership separate from runtime identity. The base Workshop owner remains `5114950`. If the demo uses its own runtime App ID, Steamworks cross-app Workshop permissions still need to be configured and validated with live Steam accounts.

## 3. Add GitHub Actions repository variables

Open:

**GitHub repository > Settings > Secrets and variables > Actions > Variables**

Add:

| Variable | Required | Value |
| --- | --- | --- |
| `STEAM_FULL_APP_ID` | Optional | `5114950`; the workflow already defaults to this value |
| `STEAM_FULL_WINDOWS_DEPOT_ID` | Required for full-game upload | The Windows depot ID from Steamworks |
| `STEAM_DEMO_APP_ID` | Required for demo build/upload | The associated Steam demo App ID |
| `STEAM_DEMO_WINDOWS_DEPOT_ID` | Required for demo upload | The demo's Windows depot ID |

Depot/App IDs are public product configuration, so repository variables are appropriate. Do not store authentication material in repository variables.

## 4. Create a dedicated Steam build account

Use a dedicated Steam account for automated uploads rather than a personal account.

Grant only the permissions required to build this app, principally:

- **Edit App Metadata**
- **Publish App Changes To Steam**

Restrict the account to Desktop Buddy and its associated demo where possible.

## 5. Create the Steam Guard session secret

The workflow uses Valve SteamCMD and an authenticated `config.vdf`. The file contains sensitive authentication material and must never be committed.

On the local Windows machine:

1. Download/extract SteamCMD from Valve.
2. In the SteamCMD directory, authenticate the dedicated build account:

```powershell
.\steamcmd.exe +login YOUR_BUILD_ACCOUNT +quit
```

3. Complete the Steam Guard prompt if one appears.
4. Run the command again and confirm it can authenticate without another Steam Guard prompt.
5. Base64-encode the resulting `config\config.vdf`:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("config\config.vdf")) | Set-Content -NoNewline steam_config_vdf_base64.txt
```

6. Copy the contents of `steam_config_vdf_base64.txt` into a GitHub Actions secret named `STEAM_CONFIG_VDF`.
7. Add the build-account username as a GitHub Actions secret named `STEAM_USERNAME`.
8. Delete the temporary base64 text file after the secret is stored.

If Steam later invalidates the login session, authenticate locally again and replace `STEAM_CONFIG_VDF` with a fresh encoded `config.vdf`.

## 6. Optionally protect the GitHub environment

The upload job uses the GitHub environment `steam-release`.

For additional protection, configure that environment with required reviewers. This lets build-only runs complete normally while an actual Steam upload requires explicit approval.

## 7. First build: do not upload yet

Open **Actions > SteamPipe Windows Build > Run workflow**.

First run:

- `target`: `full`
- `upload`: `false`
- `release_branch`: leave empty
- `build_description`: optional

The workflow will:

1. build the shipping Godot project in `ExportRelease` with the selected distribution scope;
2. materialize the verified GodotSteam 4.22 addon;
3. download pinned Godot 4.6.1 .NET editor/export templates;
4. stamp the target runtime App ID into the disposable CI checkout;
5. export the correct Windows Godot preset;
6. remove PDB files;
7. inspect the shipped managed assembly and PCK, then reject accidental `steam_appid.txt`, source/project, debug, or map leakage;
8. verify that a Windows Steam/GodotSteam DLL is present; and
9. generate and verify a provenance manifest for the complete payload; and
10. record the manifest hash in the workflow summary.

The build-only payload stays on the ephemeral runner and is not published as a GitHub Actions artifact. A successful `upload=false` run is a release preflight, not a retained release candidate. Record its commit, workflow run ID and manifest hash from the summary.

## 8. First SteamPipe upload

Run the same workflow again with:

- `target`: `full`
- `upload`: `true`
- `release_branch`: leave empty for the safest first upload

The workflow generates an `AppBuild` VDF at runtime and calls Valve SteamCMD `+run_app_build`. The depot maps the complete exported Windows payload recursively and excludes PDB/development App-ID files.

Leaving `release_branch` empty means the build is uploaded but no Steam branch is changed automatically. After the upload succeeds, inspect the Build ID/manifests in **SteamPipe > Builds** and promote the desired build in Steamworks.

For a private beta branch such as `internal`, the workflow can set it live automatically by entering that branch name in `release_branch`.

Do not enter `default` as `release_branch`; Valve requires the default branch to be promoted through Steamworks rather than `SetLive` automation.

## 9. Demo candidate and upload

After `STEAM_DEMO_APP_ID` and `STEAM_DEMO_WINDOWS_DEPOT_ID` are configured, use the same workflow with `target=demo`.

The workflow chooses the `Windows Steam Demo` Godot preset and stamps the demo runtime App ID into the disposable export, while the source-controlled Workshop owner remains the base game (`5114950`).

Use `upload=false` first as the managed export preflight. It validates the assembly, PCK, physical Demo scope, required native libraries, forbidden-file policy and provenance manifest, but its runner-local payload cannot be downloaded or used for Windows acceptance.

The manual **Encrypted Embedded PCK Steam Demo Spike** is the contained H3 compatibility gate. It builds a Godot 4.6.1 .NET Windows template from pinned source with a run-local AES key, applies the disposable encrypted/embedded preset, verifies the final executable footer/header and shipped managed assembly, rejects plaintext resource sentinels and loose fallback packs, and performs startup smoke. It retains only non-secret source/tool/template/key identities in the summary; the key, custom template, logs, and candidate bytes are not uploaded. A green spike does not enable production encryption: interactive Paint, Studio, Work, Workshop, save, and Windows acceptance are still required on retained exact bytes before owner approval changes the Demo or Full presets.

After the 2026-09-09 owner hardening decision, this workflow composes NativeAOT with encrypted/embedded PCK and is named **Hardened NativeAOT + Encrypted PCK Steam Demo** in Actions. Its default `retain_candidate=false` is the compatibility run. The owner may manually dispatch it with `retain_candidate=true` to retain the exact manifest-verified candidate as a seven-day Actions artifact for testing. On the owner's explicitly chosen public-repository path, that artifact is publicly downloadable; the owner accepts that distribution risk in exchange for public-repository Actions billing. The ephemeral encryption key, keyed custom template, and build logs are never artifacts.

Run `34395965535` is rejected evidence: it encrypted the embedded pack, but supplied only the exporter key variable and built the custom template without `SCRIPT_AES256_ENCRYPTION_KEY`. Its process-alive smoke mistook Godot's error dialog for a successful boot. The workflow now supplies the same masked key to both compiler and exporter and requires the exact executable to load the project and exit cleanly after 120 headless frames. Never distribute or test the artifact from that rejected run.

For an exact retained candidate, use one of these existing paths:

- with release authorization, rerun the same commit with `target=demo`, `upload=true` and an empty `release_branch`; Steam retains the depot build without changing a live branch;
- build the same managed Demo preset locally with the pinned Godot 4.6.1 editor/templates and verified GodotSteam 4.22 addon, then run the same scope, payload and manifest checks before Windows testing.

Record the candidate commit, workflow/local build ID, runtime Demo App ID (`5228990`), Workshop owner App ID (`5114950`), manifest SHA-256 and payload file hashes together. Never treat the unretained preflight as the candidate tested on Windows.

## 10. Steam-side validation after upload

Before release, install each build through the Steam client rather than running the exported EXE directly.

Verify at minimum:

- Steam launches `DesktopBuddy.exe` from a clean depot install.
- No `steam_appid.txt`, source tree, test scenes, authoring data, or project files are present in the install directory.
- GodotSteam initializes through the real Steam client.
- Overlay behavior works.
- Achievements/stats and Steam Cloud behave as expected.
- Workshop browse/publish/download/import paths work with the intended account permissions.
- Demo/full feature boundaries match the selected export preset.
- A clean user can uninstall/reinstall without relying on files left by a developer checkout.

## Security rules

Never commit or upload as ordinary build artifacts:

- Steam account passwords;
- `config.vdf` or its base64 form;
- Steam Guard/shared secrets;
- `ssfn*` files;
- development `steam_appid.txt`;
- the Steamworks SDK;
- locally materialized GodotSteam/Valve runtime binaries outside the exported release payload.

The final exported depot is expected to contain the Steam runtime DLLs required to run the game; the rule above concerns source control and credential/build-tool material, not legitimate redistributables inside the shipped game build.
