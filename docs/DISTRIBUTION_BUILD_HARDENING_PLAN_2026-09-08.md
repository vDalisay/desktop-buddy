# Desktop Buddy — distribution build hardening plan

Status: **plan only; no player-facing behavior changed on this branch**  
Recorded: 2026-09-08  
Branch: `plan/distribution-build-hardening-2026-09-08`  
Applies to: itch.io Web demo, Steam Demo, Steam full release / Windows x86_64

## Executive decision

Desktop Buddy should not rely on one DRM or obfuscation switch. The release pipeline should use layered controls, in this order:

1. **Do not ship code or assets a distribution does not need.** This is the only control that actually makes an omitted feature unavailable to an attacker.
2. **Keep public release artifacts and signing/encryption secrets out of unnecessarily public build storage.**
3. **Make casual copying/rehosting less useful:** itch sitelock; Steam launch-through-Steam; encrypted/embedded native PCK.
4. **Make managed code less trivial to reconstruct, but only within Godot-safe boundaries.** The existing whole-game Obfuscar experiment already proved that blind type renaming breaks this project.
5. **Prove authentic releases:** Authenticode signing, timestamps, immutable build identity, hashes and signed provenance.
6. **Harden the release supply chain:** pinned tools/actions, integrity checks, protected release environment, no rebuild between verification and upload.
7. **Maintain a takedown/evidence procedure.** No client-side protection can prevent a determined actor from copying files received by a player's machine/browser.

This is deterrence and provenance, not a claim that an offline PC/browser game can be made impossible to reverse engineer.

---

# 1. Current-state audit

## 1.1 itch.io Web build — already materially improved

The current `main` already has the right highest-value pattern for the itch build:

- `DesktopBuddyItchScope` removes held-back C# at compile time.
- The disposable Web project removes autoloads corresponding to excluded source.
- The itch export excludes owned assets/data for removed systems.
- Developer test/scenario code is omitted from shipping builds.
- The Web build uses trimming/AOT-oriented .NET settings and omits debug/source maps and symbols.
- The HTML shell has a fail-open itch/referrer sitelock intended to stop automated wholesale rehosts without risking a CDN hostname change breaking the official itch page.

The existing hardening plan correctly treats Web code/resources as client-delivered material. Anything needed to render/run in the browser can ultimately be downloaded and studied.

### Remaining itch gaps

- Release/build dependencies are not all pinned/verified to the same standard. In particular the publishing workflow downloads Butler from a mutable `LATEST` URL.
- The final browser artifact should gain an immutable build manifest/hash/provenance record.
- The official itch deployment still needs an explicit acceptance test for both the real embed and a copied/rehosted folder.
- PCK encryption should **not** be a priority for the browser build: the browser must receive the decrypting runtime/key material. It adds complexity while addressing a weaker threat than physical feature exclusion + sitelock.

## 1.2 Steam Demo — primary technical gap

The Steam Demo preset currently differs from the Full Release primarily through Godot feature data (`steam_demo` vs `full_release`) and `DemoScope` runtime decisions.

Examples in the current tree:

- full-release-only catalogue entries remain authored and are made invisible by `DemoScope.Includes(...)`;
- Tops/Shoes/Accessories are runtime-filtered in Buddy Studio;
- the Room Decorator code is still compiled and its bootstrap simply returns when `IncludesRoomDecorator` is false;
- both Steam presets currently use an unencrypted, separate `DesktopBuddy.pck`;
- native Windows managed assemblies are much easier to inspect than the itch Web build.

The previous scenario-tree/full-release override leak was already fixed by `DesktopBuddyShippingBuild`; that is a useful model for this plan: developer/full-only capabilities should be absent from the shipping demo, not merely unreachable by normal UI.

## 1.3 Existing obfuscation work — do not repeat it blindly

`docs/OBFUSCATION_FINDINGS_2026-09-07.md` records a real Obfuscar 2.2.50 experiment.

The important result is architectural, not tool-specific:

- Godot auto-names many C# nodes using reflected class names.
- Desktop Buddy also finds nodes using names baked by `nameof(...)` / literal strings.
- Obfuscating class names changed the runtime names but not the baked lookup strings.
- The game therefore booted but failed later in specific UI/tutorial paths.
- A very conservative config survived by skipping so much Godot-facing code that the protection value became low.
- a methods/fields-only attempt was not yet made shippable.

Therefore this plan does **not** assume that buying a stronger obfuscator fixes the problem. The stable reflection/serialization/Godot boundary has to be defined first.

## 1.4 Release-storage exposure

The repository is currently **public**. That has two consequences which must be treated separately:

1. Source in the public Git history must already be considered disclosed. Obfuscating a binary cannot make publicly available source confidential.
2. The Steam workflow uploads the built demo/full payload as a GitHub Actions artifact before the SteamPipe job downloads it. GitHub documents that signed-in users with repository read access can download workflow artifacts. On a public repository, Steam release binaries should therefore not use ordinary public-repository Actions artifact storage as a confidentiality boundary.

No repository visibility change is made by this plan. The owner must choose whether the source is intentionally public. If not, repository visibility and prior-history exposure are a release-management issue, not an obfuscation issue.

---

# 2. Threat model

Protect against these separately; a control useful for one can be irrelevant to another.

| Threat | Example | Best controls |
| --- | --- | --- |
| Wholesale reupload | Copy itch folder or Steam files and publish elsewhere | sitelock / Steam relaunch, publisher signature, build provenance, monitoring/takedown |
| Demo feature unlocking | Flip a build feature/data flag to expose full-game content | physical compile/resource exclusion + negative artifact tests |
| Asset ripping | Extract textures/audio/scenes from PCK | do not ship held-back assets; native PCK encryption/embedding as speed bump |
| Managed-code reconstruction | ILSpy/dnSpy on Windows assemblies | remove unused code first; targeted obfuscation; possibly stronger transforms after compatibility proof |
| Rebranding/tampering | Rename/repackage files as another publisher | Authenticode signature + timestamp + signed manifest/provenance |
| Release-pipeline compromise | Replace a downloaded tool/artifact during CI | pinned versions/hashes, protected secrets/environment, signed artifact promotion |
| Credential/key theft | PCK/code-signing key leaked from logs/PR jobs | release-environment secret isolation, cloud/HSM signing, least privilege |

Out of scope: invasive anti-cheat drivers, always-online DRM, kernel components, malware-like anti-debug loops, per-player surveillance, or pretending any client-delivered content is secret forever.

---

# 3. Target architecture

## Principle A — one source tree, multiple physically different artifacts

Keep one source tree, but each distribution produces a deliberately reduced assembly/resource graph.

```text
source tree
   |
   +-- full-release scope --------> Steam Full artifact
   |
   +-- steam-demo scope ----------> Steam Demo artifact
   |
   +-- itch scope ----------------> itch Web artifact
```

A runtime `DemoScope` remains useful for UI/product policy and defense in depth. It must no longer be the only boundary protecting code/content intentionally withheld from a demo.

## Principle B — one manifest/source of truth for exclusions

Do not maintain unrelated exclusion lists in C#, `project.godot`, export presets and CI by hand.

Introduce a small distribution manifest or MSBuild-owned list from which verification/export surgery is derived. At minimum it must be able to answer:

- source files/folders compiled into each distribution;
- autoloads permitted in each distribution;
- scenes/resources/assets permitted in each distribution;
- distribution-specific required feature tags;
- forbidden type/resource/path sentinels used by artifact tests.

The existing `apply_itch_scope.py` pattern is the starting point rather than a separate mechanism.

## Principle C — hardening transforms happen after reduction

Order matters:

```text
select scope
-> compile/remove held-back code
-> export only allowed resources
-> strip debug/developer material
-> native resource encryption/embedding (Steam)
-> managed obfuscation (where compatible)
-> code signing
-> hash/provenance manifest
-> verify exact final artifact
-> upload exact same artifact
```

Never obfuscate or encrypt content that should simply not be present.

---

# 4. Implementation phases

## H0 — close release-process exposure first

Priority: **Blocker before public Steam demo**

### H0.1 Decide repository confidentiality

Owner decision, no automatic repo-setting change:

- If Desktop Buddy is intentionally open source, record that explicitly and stop treating source obfuscation as source confidentiality. Binary obfuscation can still deter repackaging/modification.
- If the game is intended to remain proprietary, move the active repository/release pipeline to private before relying on hardening. Audit which commits/assets have already been public. Making a repository private later does not retract clones or copies made while it was public.

### H0.2 Do not persist confidential Steam builds as ordinary artifacts in the public repository

Change `.github/workflows/steam-pipe.yml` so Steam release binaries are not a long-lived Actions artifact in a public repo.

Preferred designs, in order:

1. **One release job** in the protected `steam-release` environment builds/verifies/signs and uploads the exact files directly to SteamPipe, without `actions/upload-artifact` for the final depot payload.
2. If separation is needed, send the finalized build to a **private release repository/artifact store**, then promote that exact digest.
3. If the repository becomes private, short-retention Actions artifacts are acceptable, but still limit access and retention.

Do not solve this by putting release secrets into PR/build jobs.

### H0.3 Release environment

For Steam and signing/encryption secrets:

- keep `steam-release` as the credential boundary;
- require explicit/manual release approval if the GitHub plan supports environment reviewers;
- release secrets are unavailable to fork PRs and ordinary CI;
- least-privilege Steam account where possible;
- no self-hosted runner on this public repository (existing project rule remains correct).

Acceptance:

- a normal PR/push can build/test but cannot retrieve signing/PCK/Steam credentials;
- no final Steam depot payload is downloadable from a public workflow artifact page;
- an upload uses the same final digest that passed verification.

---

## H1 — Steam Demo compile-time/resource exclusion

Priority: **Blocker before public Steam demo**

Goal: deleting/changing `steam_demo` / `full_release` feature data cannot reveal held-back full-game implementation because it is not present.

### H1.1 Add an explicit Steam Demo compile scope

Introduce an MSBuild property such as:

```text
DesktopBuddySteamDemoScope=true
```

and a compile constant such as:

```text
DESKTOP_BUDDY_STEAM_DEMO
```

The Steam Demo release pipeline must set it explicitly. A missing/invalid distribution selection should fail closed during release CI.

Do not infer paid/demo code presence solely from mutable Godot custom-feature data inside the exported project.

### H1.2 Inventory demo-vs-full surfaces

Create a checked-in distribution inventory with four classifications:

- `shared`: intentionally in Demo + Full;
- `steam-demo`: Demo-specific implementation;
- `full-only`: must not be in Steam Demo assembly/PCK;
- `itch-excluded`: current stricter Web exclusion.

Start with the **current** known runtime-gated surfaces:

- Room Decorator/environment editing implementation (currently full-only);
- held-back cosmetic entries/assets/renderers where they can be removed without breaking shared persistence/render contracts;
- Tops/Shoes/Accessories catalogue/editor content that the current Demo intentionally withholds;
- every future feature added under `FULL_RELEASE_EXPANSION_ROADMAP` or later owner-approved full-release plans.

Do not blindly remove a whole directory if it also owns a data type or renderer needed to deserialize/display legitimate demo content. Follow the same partial-seam approach already used by the itch scope.

### H1.3 Remove code at compile time

Use `<Compile Remove=...>` / focused partial-class `Present` vs `Absent` seams to make full-only types unreferenceable in Steam Demo builds.

Rules:

- a Demo compile must succeed without the full-only tree;
- a Full compile must still compile the full tree;
- shared code must not name a removed type;
- no release-only override/test seam exists in shipping assembly.

### H1.4 Remove autoloads/scenes/resources/assets

Extend the existing disposable-project approach to Steam Demo:

- remove autoloads whose implementation is compiled out;
- apply distribution-specific export exclusions;
- remove full-only `.tres`, textures, audio, scenes, shaders and generated imports;
- keep one manifest/source of truth to avoid drift.

### H1.5 Negative artifact verification

The acceptance test is the **built Steam Demo**, not a source review.

CI should fail if a Steam Demo artifact contains forbidden sentinels, for example:

- full-only type/namespace names in `DesktopBuddy.dll`/project assemblies;
- full-only resource paths or IDs in the PCK;
- a full-release autoload;
- developer scenario/test classes;
- source/project/docs/build scripts;
- an unexpected `full_release` custom feature.

Maintain a small explicit forbidden-sentinel list per full-only feature. Each new full-only feature must add its own negative artifact assertion as part of Definition of Done.

### H1.6 Preserve runtime gates

Keep `DemoScope` checks as defense in depth and to keep local/scenario tests expressive. They become policy/UI checks, not the security boundary.

Acceptance:

- changing feature tags in a Steam Demo PCK cannot materialize full-only code/assets;
- the Demo and Full artifacts have measurably different managed/resource inventories;
- Demo smoke/journey tests pass against the actual reduced export;
- Full build remains unchanged functionally.

---

## H2 — Steam launch-through-Steam deterrence

Priority: **Must before public Steam demo**

Valve explicitly says the Steam DRM wrapper does not support C#/.NET applications. Do **not** add `drm_wrap` to Desktop Buddy.

Valve recommends `SteamAPI_RestartAppIfNecessary` for .NET titles.

### H2.1 Expose the restart call through the GodotSteam bridge

In shipping Steam builds only:

1. invoke GodotSteam/Steamworks' equivalent of `SteamAPI_RestartAppIfNecessary(expectedAppId)` **before** normal Steam initialization;
2. if it reports that a relaunch was requested, exit immediately;
3. when already launched by Steam, continue normally.

The existing workflow's rejection of `steam_appid.txt` is mandatory because Valve documents that its presence disables the relaunch behavior.

### H2.2 Do not turn this into always-online DRM

Preserve the project's offline-first rule:

- editor/development workflows remain usable;
- a legitimate Steam installation should work with Steam's offline mode;
- transient Workshop/network failure must not corrupt or disable local single-player state;
- do not add a custom web entitlement server just to launch the game.

This control stops the lowest-effort “copy folder and double-click EXE” path. Valve itself describes comparable Steam DRM as protection against extremely casual copying, not motivated piracy.

Acceptance:

- direct launch of the depot EXE relaunches through Steam;
- launch from Steam does not loop;
- `steam_appid.txt` is absent from depot;
- Steam offline-mode acceptance passes;
- editor/local development path remains available.

---

## H3 — Steam native resource encryption + embedding

Priority: **Strongly recommended before public Steam demo**

Godot 4.6 officially supports PCK encryption with a 256-bit AES key, but it requires a custom export template compiled with the same key. Godot also explicitly warns that the key ultimately resides in the binary: this raises extraction cost; it is not unbreakable DRM.

### H3.1 Custom production export template

Build/pin a Windows x86_64 Godot 4.6.1 .NET release template from exact source/tag/commit with the PCK encryption key injected by the protected release environment.

Cache/template identity must include at least:

```text
Godot version + source commit + target + key ID/fingerprint + build flags
```

Never commit or echo the actual key.

### H3.2 Steam presets

For Steam Demo and Full Release, evaluate and enable after a compatibility spike:

```text
encrypt_pck=true
encrypt_directory=true
encryption_include_filters="*"
binary_format/embed_pck=true
```

The exact Godot export fields must be verified against the pinned 4.6.1 template before merging.

Embedding hides the obvious loose PCK and encryption prevents trivial off-the-shelf extraction. Neither is a substitute for H1.

### H3.3 Key management

- use a cryptographically random 256-bit key;
- keep it in the protected release environment / secret manager, never `export_presets.cfg`;
- never print it in CI logs;
- document rotation and custom-template rebuild procedure;
- store only a non-secret key ID/fingerprint in build metadata.

### H3.4 itch decision

Do **not** make browser PCK encryption a release blocker. The browser receives the runtime needed to consume the encrypted content. Revisit only if measurement shows it materially slows automated asset scraping without destabilizing the pinned Web exporter.

Acceptance:

- native Steam build runs from an encrypted/embedded custom template;
- ordinary PCK extraction no longer yields readable resources;
- no key appears in source, artifacts, logs or workflow definitions;
- protected build passes Paint/Studio/Work/Workshop resource-load paths.

---

## H4 — managed-code obfuscation, compatibility first

Priority: **Strongly recommended, staged**

### H4.1 Start where Godot reflection is not the boundary

First protect the least engine-coupled assemblies:

- `DesktopBuddy.Domain.dll`;
- `DesktopBuddy.Visuals.dll` only after checking its Godot/resource/reflection surface;
- other future engine-independent assemblies.

Keep the Godot-facing `DesktopBuddy.dll` on a conservative profile initially.

Measure success with a decompiler: an obfuscation job that runs but leaves clean type/method structure is not a pass.

### H4.2 Stabilize explicit names before broad type renaming

If broader `DesktopBuddy.dll` protection is still desired, first remove the known rename hazard:

- nodes depended on by name get explicit stable literal IDs/names independent of C# class names;
- Godot `[Signal]`, `[Export]`, generated bindings and callable method names are inventoried/preserved;
- JSON/save contracts that rely on member/type names are explicitly preserved or use stable serialized property names;
- reflection-by-name sites are inventoried and covered by tests;
- required exclusions use source attributes/rules rather than an ever-growing accidental skip list where practical.

This is a prerequisite/refactor, not an obfuscator setting.

### H4.3 Tool evaluation

Use the existing Obfuscar work as the free baseline. Compare at most one mature commercial option if stronger transforms are wanted (for example Babel or Eazfuscator) against the **same** compatibility matrix.

Evaluate separately:

- symbol/type/member renaming;
- string encryption;
- control-flow transforms;
- anti-tamper;
- code encryption/virtualization if offered.

Do not enable all aggressive options at once.

Recommended initial profile:

- rename safe private/internal implementation names;
- no anti-debug loop;
- no virtualization in v1;
- no transformation that produces AV/SmartScreen regressions without measurable benefit;
- preserve public/Godot/reflection/serialization contracts;
- keep obfuscation maps private for crash-symbol remapping.

### H4.4 Shipping-artifact test

The scenario suite alone cannot prove an obfuscated shipping assembly is safe because shipping intentionally excludes scenarios.

Required verification:

- export real reduced shipping artifact;
- obfuscate it;
- launch exact transformed executable;
- run automated black-box journeys where possible;
- manual pass through every major screen/system;
- decompile and verify meaningful degradation of readability;
- archive private mapping + exact tool/config version with release record.

Acceptance:

- no startup/tutorial/node-name regression;
- saves/load/JSON remain compatible;
- Workshop bridge and callbacks work;
- decompiled safe assemblies are materially less readable;
- transforms that do not provide measurable value are removed.

---

## H5 — publisher authenticity, code signing, build provenance

Priority: **Must before public Steam demo/full release**

These controls do not stop copying, but they make an official build cryptographically distinguishable from a modified/rebranded one and improve the evidence available for takedowns.

### H5.1 Authenticode

Sign the final Windows executable and project-owned PE binaries after all transforms and before packaging/upload.

Microsoft currently recommends Azure Artifact Signing (formerly Trusted Signing) for non-Store Windows distribution where eligible; a conventional OV code-signing certificate is the fallback. Do not use a self-signed certificate for public releases.

Always timestamp with RFC 3161/SHA-256 so signatures remain verifiable after certificate expiry.

Prefer cloud/HSM-backed signing. Do not store an exportable PFX/private key in the repository or ordinary CI artifact.

### H5.2 Immutable build identity

Embed non-secret metadata into every public distribution:

- product: Desktop Buddy;
- publisher/studio identity;
- distribution: `itch-web`, `steam-demo`, `steam-full`;
- semantic/game version;
- Git commit SHA;
- CI run/build ID;
- expected Steam AppID for Steam distributions.

Expose a player-support build ID somewhere low-key (About/diagnostics/log), not as intrusive DRM.

### H5.3 Final manifest/provenance

After encryption/obfuscation/signing, generate a final release record containing:

- path + SHA-256 for every shipped file;
- distribution + version + Git SHA;
- Godot version/template commit;
- dependency/tool versions;
- PCK key **ID only**, never key;
- Authenticode certificate thumbprint/subject and timestamp metadata;
- build/run identifier.

Sign or otherwise attest the manifest and retain it with release evidence.

Optional: a few non-user-specific, harmless provenance markers in owned assets/metadata can make ownership disputes easier. Do not introduce per-player fingerprinting without a separate privacy/product decision.

Acceptance:

- `signtool verify` / equivalent succeeds on final files;
- changing a signed binary invalidates its signature;
- final hashes match the files sent to Steam;
- support can map a reported build to exact source/release record.

---

## H6 — CI/CD and dependency integrity

Priority: **Must before public Steam demo**

OWASP recommends signing/verifying artifacts and hash-validating third-party resources consumed by pipelines.

### H6.1 Pin release workflow dependencies

For release-sensitive workflows:

- pin GitHub Actions to immutable commit SHAs rather than only floating major tags;
- pin Butler to a reviewed version instead of `LATEST`, and verify its official checksum/signature where itch publishes one;
- verify checksums for downloaded Godot editor/templates rather than trusting the release URL alone;
- preserve the existing pinned/hash-verified GodotSteam installer approach;
- keep the itch custom Godot fork pinned to an exact commit as it is today.

Renovation/upgrades should be explicit reviewed changes, not silent release-time drift.

### H6.2 No rebuild after approval

The artifact that passes hardening verification is the artifact that gets uploaded.

Never:

```text
verify build A -> rebuild B in release job -> upload B
```

Use:

```text
build once -> transform -> sign -> hash -> verify -> upload those exact bytes
```

### H6.3 Scope and secret checks

Add release gates that fail if:

- Steam Demo was not built with Steam Demo compile scope;
- Steam Full was accidentally built with Demo/itch scope;
- itch contains Steam/GodotSteam code/assets;
- release artifact contains `.pdb`, `.map`, source/project/build scripts or `steam_appid.txt`;
- a signing/encryption secret-shaped value appears in generated logs/config;
- dependency checksum differs from the pinned value.

### H6.4 Branch/release policy

Before shipping, configure GitHub rules so release-relevant changes cannot bypass required CI/review. At minimum protect:

- `.github/workflows/**`;
- export presets;
- distribution manifest/scope scripts;
- obfuscation config;
- signing/provenance scripts;
- Steam/itch publishing scripts.

This is an account/repository setting task and is not changed by this planning branch.

---

## H7 — itch reupload hardening and evidence

Priority: **Keep current protection; add verification/provenance**

### H7.1 Keep compile exclusion + sitelock

The current fail-open host/referrer check is appropriately aimed at low-effort mirrored HTML5 sites. Do not convert it to an always-online API dependency.

Acceptance test each public itch candidate in two contexts:

1. official itch project embed — must boot;
2. copied folder embedded from an unrelated test host with a clear non-itch referrer — must refuse to boot and point to the official page.

Remember: a motivated reuploader can remove JavaScript sitelock code. It is automation friction, not DRM.

### H7.2 Harden publishing toolchain

- pin/verify Butler;
- preserve `push-preview` before real push;
- validate the exact source SHA/run chosen for publication;
- generate/retain the same build provenance record used elsewhere;
- never accidentally publish the Steam/full artifact to the itch channel.

### H7.3 Optional visible provenance

Ensure the About/support surface clearly names the official studio/project and official itch/Steam page. A thief can edit it, but wholesale reuploads often do not.

---

## H8 — monitoring, takedown, and evidence packet

Priority: **Operational release requirement**

Technical deterrence does not replace enforcement.

Maintain a private release evidence packet per version:

- signed/hash manifest;
- Git commit and timestamp;
- code-signing certificate identity;
- screenshots/store publication timestamps;
- proof/licenses for owned or licensed assets;
- official Steam AppID / itch project identity;
- obfuscation map retained privately if used.

Create `docs/release/REUPLOAD_RESPONSE_RUNBOOK.md` during implementation with:

- how to hash a suspicious download and compare it to releases;
- how to verify Authenticode publisher/signature;
- how to identify build metadata;
- evidence template for a DMCA/copyright report;
- platform/host/CDN reporting steps;
- incident log fields so repeat hosts can be tracked.

Optional later automation: periodic searches for exact title/executable/store-text combinations and notify only on plausible unauthorized mirrors.

---

# 5. Recommended delivery order

## Gate 1 — before the next public Steam Demo

1. **H0** — decide source confidentiality and remove Steam final binaries from public artifact storage.
2. **H1** — Steam Demo physical code/resource exclusion + negative artifact tests.
3. **H2** — `SteamAPI_RestartAppIfNecessary` equivalent for shipping Steam builds; retain offline behavior.
4. **H5** — Authenticode + timestamp + immutable build identity + hash/provenance manifest.
5. **H6** — release dependency pinning/integrity checks and exact-artifact promotion.

## Gate 2 — strongly recommended before/with public Steam Demo

6. **H3** — encrypted + embedded PCK via a custom pinned Godot 4.6.1 .NET export template.
7. **H4.1/H4.3/H4.4** — targeted obfuscation on engine-safe assemblies, with actual exported-build verification.

## Gate 3 — only if the protection gain justifies architectural churn

8. **H4.2** — explicit stable Godot node/reflection naming refactor, then broader main-assembly obfuscation.
9. consider aggressive commercial transforms one at a time only after compatibility, AV and performance tests.

## Do not prioritize

- Steam DRM wrapper (`drm_wrap`) — Valve documents that it does not support .NET applications;
- always-online entitlement servers for a primarily offline single-player game;
- browser PCK encryption as a substitute for removing content;
- anti-debug polling, packers or virtualization before simpler measures pass;
- secret client-side keys advertised as unextractable.

---

# 6. Verification matrix

Every release candidate should produce a machine-readable hardening report.

| Check | itch Web | Steam Demo | Steam Full |
| --- | ---: | ---: | ---: |
| shipping/test code stripped | yes | yes | yes |
| distribution compile scope asserted | yes | yes | yes |
| forbidden feature code absent | yes | yes | n/a |
| forbidden feature resources absent | yes | yes | n/a |
| source/docs/scripts absent | yes | yes | yes |
| symbols/maps absent from player payload | yes | yes | yes |
| sitelock official-host test | yes | n/a | n/a |
| copied-host/rehost test | yes | n/a | n/a |
| Steam relaunch test | n/a | yes | yes |
| Steam offline-mode test | n/a | yes | yes |
| encrypted/embedded PCK | optional/defer | yes | yes |
| targeted obfuscation verified by decompiler | AOT path only | yes | yes |
| Authenticode + timestamp | n/a | yes | yes |
| SHA-256 release manifest | yes | yes | yes |
| exact artifact promoted/uploaded | yes | yes | yes |
| tool/dependency checksum validation | yes | yes | yes |
| full smoke/journey on transformed artifact | browser | yes | yes |

For Steam Demo specifically, add a malicious-tampering regression: edit/remove the exported `steam_demo`/`full_release` feature data and prove that physically omitted full-only types/assets still cannot appear.

---

# 7. Implementation task breakdown

Suggested implementation sequence for a follow-up coding branch/PR:

- `HARDEN-01` — distribution inventory + Steam Demo MSBuild scope.
- `HARDEN-02` — compile-time remove current full-only Steam Demo implementation.
- `HARDEN-03` — derive Demo autoload/resource exclusion from the same scope manifest.
- `HARDEN-04` — negative assembly/PCK artifact scanner and CI report.
- `HARDEN-05` — Steam restart-through-client bridge + direct/offline launch tests.
- `HARDEN-06` — replace public Steam artifact handoff with protected exact-byte release flow.
- `HARDEN-07` — pin/hash release dependencies (Godot/templates/Butler/Actions).
- `HARDEN-08` — custom encrypted Godot 4.6.1 .NET Windows template + embedded PCK.
- `HARDEN-09` — targeted Domain/Visuals obfuscation spike against final exports.
- `HARDEN-10` — Authenticode cloud/HSM signing + RFC3161 timestamp + verification.
- `HARDEN-11` — build identity + SHA-256 signed release manifest/provenance.
- `HARDEN-12` — itch official/rehost acceptance test and publish integrity report.
- `HARDEN-13` — reupload evidence/takedown runbook.
- `HARDEN-14` — optional explicit-node-name/reflection stabilization for broader obfuscation.

Keep these separate enough that a compatibility problem in PCK encryption or obfuscation does not block the higher-value H0/H1/H2/H5/H6 protections.

---

# 8. Definition of done

Distribution hardening is ready for a public Steam Demo when:

- no full-only implementation/resource can be enabled by editing a Demo feature flag;
- the actual Steam Demo export passes negative code/resource inspection;
- direct copied-EXE launch is redirected through Steam without breaking legitimate offline behavior;
- player payload contains no source, developer test tree, PDB/maps or `steam_appid.txt`;
- final Windows files are Authenticode-signed and timestamped;
- release hashes/build identity tie those files to a specific commit/build;
- the exact verified bytes, not a rebuild, are uploaded to Steam;
- release secrets and final Steam binaries are not exposed by public CI artifact storage;
- release dependencies are pinned/hash-verified;
- encrypted PCK/obfuscation, if enabled, pass the complete transformed-artifact acceptance suite;
- itch continues to boot on its official page and blocks the tested low-effort copied-host case;
- a release evidence/takedown packet can be produced without reconstructing history after an incident.

---

# 9. External evidence / primary references

Valve / Steamworks:

- Steam DRM overview and .NET FAQ: https://partner.steamgames.com/doc/features/drm
- `SteamAPI_RestartAppIfNecessary`: https://partner.steamgames.com/doc/sdk/api

Godot 4.6:

- PCK encryption key/custom export-template requirements: https://docs.godotengine.org/en/4.6/engine_details/development/compiling/compiling_with_script_encryption_key.html
- Export configuration and secret `export_credentials.cfg`: https://docs.godotengine.org/en/stable/tutorials/export/exporting_projects.html
- PCK security/copyright notes: https://docs.godotengine.org/en/stable/tutorials/export/exporting_pcks.html

Microsoft / Windows:

- Code-signing choices (Azure Artifact Signing recommended for non-Store distribution where eligible): https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Authenticode timestamp guidance: https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures

GitHub Actions:

- Workflow artifact access: https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts

OWASP supply-chain guidance:

- Software Supply Chain Security Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Software_Supply_Chain_Security_Cheat_Sheet.html
- CI/CD artifact integrity: https://owasp.org/www-project-top-10-ci-cd-security-risks/CICD-SEC-09-Improper-Artifact-Integrity-Validation

itch.io:

- HTML5 distribution model: https://itch.io/docs/creators/html5

.NET obfuscators evaluated as references, not automatic recommendations:

- Obfuscar: https://github.com/obfuscar/obfuscar
- Babel Obfuscator: https://docs.babelfor.net/obfuscator
- Eazfuscator.NET MSBuild integration: https://learn.gapotchenko.com/eazfuscator.net/docs/2025.2/integration/msbuild-integration

---

# 10. Repo evidence used for this plan

- `DesktopBuddy.csproj`
- `export_presets.cfg`
- `.github/workflows/steam-pipe.yml`
- `.github/workflows/itch-io-build.yml`
- `.github/workflows/itch-io-publish-web.yml`
- `src/App/DemoScope.cs`
- `src/Environment/EnvironmentCustomizationBootstrap.cs`
- `src/Platform/Steam/GodotSteamBridge.gd`
- `docs/ITCH_HARDENING_PLAN_2026-09-07.md`
- `docs/OBFUSCATION_FINDINGS_2026-09-07.md`
- `docs/FULL_RELEASE_EXPANSION_ROADMAP.md`
- historical/diverged `agent/steam-demo-hardening` branch, used only as prior-art evidence; do not merge it wholesale because it is hundreds of commits behind `main`.
