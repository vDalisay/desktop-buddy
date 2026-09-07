# itch.io hardening plan — feature exclusion and anti-decompilation

Status: **Phase A implemented and verified. Phases B and C not started.** Written 2026-09-07 after
the itch tutorial work (`feature/itch-tutorial`) exposed that every held-back feature still ships in
the itch build and is switched off by a runtime boolean.

| Phase | State |
| --- | --- |
| A — exclude the features from the build | **Done.** Buddy Studio UI, Paint Room + Room Decorator, Work Mode, Steam Workshop and Gore Mode are compiled out; their autoloads and assets are dropped from the export. |
| B — reupload / sitelock | **Blocked on one fact:** the domain itch actually serves the game from. See §4. |
| C — obfuscation | Not started, still deferred, still aimed at the native Windows builds rather than the web one. |

**What Phase A actually changed.** `DemoScope` still answers the same questions, but it is no longer
the only thing standing between an itch player and a held-back feature: for four of the five, the
code is not in the assembly to switch on. A `DesktopBuddyItchScope` MSBuild property compiles the
reduced surface on the normal desktop target (`dotnet build DesktopBuddy.sln -p:DesktopBuddyItchScope=true`),
which is how the guarded code is proven to compile without the pinned Web fork.

**Two facts the verification settled**, both recorded in §1.2 and §1.3 below rather than assumed:
C# source is not packed into the `.pck` — only path strings — so the assembly is the only place the
code lives. But those path strings do list the entire source tree, including features this build no
longer ships. That leaks structure and names, not code, and it comes from Godot's own caches.

---

## 1. What we actually ship today

| Distribution | Preset | Platform | Managed code as shipped | PCK |
| --- | --- | --- | --- | --- |
| Steam Demo | `preset.0` | Windows Desktop | **Plain IL `DesktopBuddy.dll`** | `encrypt_pck=false` |
| Full Release | `preset.1` | Windows Desktop | **Plain IL `DesktopBuddy.dll`** | `encrypt_pck=false` |
| itch.io | `preset.2` | **Web (wasm)** | AOT wasm + Webcil, IL stripped, trimmed | `encrypt_pck=false` |

The itch build is a *Web* export, built in CI by a pinned Godot fork (PR #118976). CI zips
`build/itch-web/` verbatim into `DesktopBuddy-itch-web-experimental.zip`, which is uploaded to itch
and served with "This file will be played in the browser" (owner, 2026-09-07). So the shipped
artifact is exactly `index.html`, `index.pck`, the wasm and `_framework/` — every byte of it handed
to the browser, and re-downloadable by anyone who loads the page.

This matters: almost every "protect your Godot game" article assumes a native export, and the advice
does not transfer cleanly.

### 1.1 The feature leak

`DemoScope` gates Work Mode, Paint Room, Buddy Studio, Gore Mode, Room Decorator and two cosmetics
on `OS.HasFeature("itch_io")`. All of it still ships:

- The C# compiles into the assembly. Only `Workshop` and `src/Testing/**` are compile-time excluded,
  via `#if !DESKTOP_BUDDY_PUBLIC_WEB` and a `<Compile Remove>` — and those apply to the *Web target*,
  not to the itch scope.
- Their autoloads are registered in `project.godot` for **every** build: `BuddyStudioBootstrap`,
  `WorkMilestoneProgressBootstrap`, and sixteen `Win98Paint*Bootstrap` entries (27 autoloads total).
- `export_filter="all_resources"` packs their scenes and art; none are in `exclude_filter`.
- `ItchDistributionScopeBootstrap` *removes at runtime* what shipped — `QueueFree()`s the milestone
  autoload, `HideControl`s the Work button.

**Feature tags are data, not code.** They are baked into the exported project settings inside the
`.pck`, which the web build serves as a plain downloadable file with no encryption. Deleting the
`itch_io` tag drops the build to *Demo* scope and turns on Work Mode, Paint Room, Buddy Studio and
Gore. Adding `full_release` turns on the rest. No code patching, no wasm work — a data edit with
public tooling.

### 1.2 What the current wasm hardening is and isn't worth

`DesktopBuddy.csproj` already sets `PublishTrimmed`, `TrimMode=full`, `WasmStripILAfterAOT`,
`WasmEnableWebcil`, `DebugType=none`. Its own comment says this "cannot make client-delivered WASM
secret", which is right, and the research confirms it is weaker than the name suggests:

- A .NET maintainer on [dotnet/runtime#105635](https://github.com/dotnet/runtime/discussions/105635)
  states WASM AOT "cannot always completely remove IL"; the published `.wasm` files still contain
  Webcil. Complete IL removal needs NativeAOT-LLVM, which is experimental. The original poster
  called the Microsoft docs "quite misleading" on this point.
- Webcil is a WebAssembly wrapper around a standard .NET assembly. Metadata, member names and string
  literals survive; the files load in .NET decompilers, and `wasm-decompile`/Ghidra read wasm.

So: the wasm path is *harder* than a plain DLL, but it is not opaque, and it protects none of the
assets.

### 1.3 The softer target is Steam, not itch

The two Windows presets ship `DesktopBuddy.dll` as **plain IL with no trimming, no AOT and no
obfuscation**. That is a one-click ILSpy job — strictly easier than anything on the itch build. If
the concern is code theft, the native builds are the exposed ones. Worth deciding whether that
changes the priority order below.

---

## 2. Threat model — who actually attacks an itch web game

Ranked by how often it really happens, not by how alarming it sounds.

1. **Wholesale reupload.** Someone downloads `build/itch-web/` and rehosts the whole game on an ad
   site (playminigames.net, horrorgames.io and similar). This is *the* itch HTML5 problem and it is
   entirely unaffected by obfuscation — they do not read your code, they copy the folder.
2. **Asset theft.** The `.pck` is served unencrypted. `gdsdecomp`, `godotdec` and Godot PCK Explorer
   extract the full project structure and every exported asset. Obfuscation does nothing here.
3. **Feature unlocking.** The gap in §1.1. Low payoff today (the hidden content is a free demo's
   remainder), high payoff later if paid/full-release content is ever gated the same way.
4. **Logic/source theft.** Rarest. This is the only one obfuscation addresses, and on the web build
   it is already the hardest of the four.

**Conclusion: obfuscation is aimed at the least likely threat, and does nothing for the top two.**
That should drive sequencing.

---

## 3. Phase A — exclude the features from the build (recommended, do first)

Goal: the itch artifact contains no Work Mode, Paint Room, Buddy Studio or Gore code or assets, so
there is no boolean to flip. This is the only work that actually closes §1.1.

Surface: ~30 files in `src/Work/`, 12 in `src/CharacterEditor/BuddyStudio/`, 18 in
`src/Environment/`, 5 gore-related, plus autoload entries and assets.

### A1. Reuse the existing compile constant

**No new constant.** Owner confirmed 2026-09-07: itch is served from
`DesktopBuddy-itch-web-experimental.zip` via itch's "play in the browser" hosting, and there is no
native itch build. `preset.2` is also the only Web preset. So *web ⟺ itch* — `DESKTOP_BUDDY_PUBLIC_WEB`
already means exactly "the public itch web build", and a second constant would be an abstraction
with one implementation.

Split it later, if and only if a second web distribution or a native itch build ever appears. The
cost of splitting then is a rename; the cost of carrying two constants now is two things to keep in
step forever.

### A2. Remove the source from the compile — as implemented

The sketch below was two-thirds right. `src/Work` and `src/Environment` did come out as whole
folders; `src/CharacterEditor/BuddyStudio` did not, because that folder mixes the workspace UI with
cosmetic data types the renderer, editor session and character persistence all still need. The buddy
wears and saves cosmetics on itch — it just cannot edit them there — so `BuddyGeneratedCosmeticRegistry`
and the two `GeneratedBuddyCosmetic*Resource` types stay and the nine UI files are listed explicitly.

`src/Sharing` came out too: Workshop was already `#if`'d out of the composition, so only its source
was still being compiled in.

The fastest way to find each seam turned out to be removing the whole folder and letting the
compiler name every dependency. `src/Environment` looked entangled at 18 files with 22 external
references; the compiler reduced it to exactly three breakages.

```xml
<ItemGroup Condition=" '$(DesktopBuddyItchScope)' == 'true' ">
  <Compile Remove="src/Work/**/*.cs" />
  <Compile Remove="src/Environment/**/*.cs" />
  <Compile Remove="src/Sharing/**/*.cs" />
  <!-- Buddy Studio: the nine workspace-UI files only. -->
</ItemGroup>
```

The fallout came in two shapes. Small ones are one-line `#if !DESKTOP_BUDDY_PUBLIC_WEB` guards:
`Bootstrap`, `Bootstrap.CharacterEditor`, `SandboxRoot`'s Reset Progress, the audio bootstrap's
coordinator hook.

The larger ones are three two-halves **partial-class seams** — `.Studio` / `.StudioAbsent`,
`.Background` / `.BackgroundAbsent`, `.Work` / `.WorkAbsent` on the guidance controller, and
`.Environment` / `.EnvironmentAbsent` on `RoomInterestBootstrap`. Each pair puts every reference to
an excluded type in one file and answers the same questions with "there is no such thing" in the
other, so the consumer never names the type and compiles against both surfaces. Scattering `#if`
through a 2100-line controller would have been the smaller diff and the much worse file.

Gore Mode needed a third shape. `sandbox.tscn` binds `GoreComponent` by script path and wires three
exports by NodePath, so it cannot be an excluded file — the scene would fail to load. Removing it
would mean programmatic surgery on a 500-line scene: an `ext_resource`, an entry in a 47-element
`PackedStringArray`, and a node. Instead the node stays and every line that draws leaves, behind two
`#if` regions, with `Initialize`/`ApplyEffectsSettings`/`ClearAll` kept as no-ops.

**Build both scopes, always.** The itch scope alone hid a missing `using` and a `Node`/`Control`
mismatch that only the normal build caught.

### A3. Drop the autoloads

Autoloads live in `project.godot`, which is data — a `#if` cannot touch it. Options:

- **A3a — taken.** `devtools/verification/apply_itch_scope.py` runs against the disposable export
  copy, right after the step that rewrites the project to `net10.0`. It does *not* repeat the
  excluded set: it reads the `<Compile Remove>` entries out of the csproj's itch `ItemGroup`, so the
  compile list stays the single source of truth and the two cannot drift. Adding a file there drops
  its autoload too, with nothing to keep in step by hand. `--check` reports without writing.

  One detail worth keeping: it reads and writes without newline translation. The first version
  normalised CRLF and rewrote all 148 lines, which would have made every CI diff unreadable.

- **A3b:** still available if the rewrite ever gets fragile — move those autoloads out of
  `project.godot` and register them from `Bootstrap` behind the `#if`.

### A4. Exclude the assets

Extend `exclude_filter` on `preset.2` with the scenes, `.tres` and art owned by the excluded
features. Then **verify by extracting the built `.pck` with `gdsdecomp` and diffing the file list** —
this is the acceptance test for the whole phase, not a code review.

### A5. Keep the runtime gate as a second layer

Leave `DemoScope` in place. Compile-time exclusion is the lock; the runtime check stays as
defence-in-depth and keeps editor runs and scenario tests working, exactly as `IncludesWorkshop` now
does both.

### Acceptance — met 2026-09-07

Verified by exporting a pack with and without the filters and diffing the contents:

- `data/environment` — 14 decoration resources and all 14 exported `.res` blobs gone.
- `assets/work` — `retro_pc.ctex`, the actual image data, gone.
- Three autoloads dropped: `WorkMilestoneProgressBootstrap`, `EnvironmentCustomizationBootstrap`,
  `BuddyStudioBootstrap` — derived from the csproj exclusion list, not a second hand-kept list.
- Both scopes build 0 warnings / 0 errors. Domain 1542/1542.
- Green: `tutorial_closure`, `buddy_studio_ui_composition`, `environment_background_editor`,
  `environment_decorator`, `environment_startup_registration`, `environment_trusted_definitions`,
  `character_paint_save_use_restart`, `work_mode_resilience`, `work_play_window_behavior`,
  `gore_mode`.

**Gaps in that acceptance, stated plainly.** The real Web export was never built locally — it needs
the pinned Godot fork, .NET 10 and `wasm-tools`, so CI is the first place the itch pack is produced
with these changes. The pack verification above used the Windows preset, which shares
`export_filter="all_resources"` and the same filter syntax. And no scenario covers
`RoomInterestBootstrap`, so that extraction rests on the two compiles and on being a straight move
of two method bodies.

---

## 4. Phase B — reupload and sitelock (cheap, targets threat #1)

itch already ships a global JS sitelock that shows a banner when a game is loaded from a non-itch
domain, plus automated hotlink prevention. Neither stops a full rehost.

Add our own check in `html/head_include` (the preset already uses that field for the SRI cache fix):
allow the itch serving domain plus `localhost`, otherwise refuse to boot and link the itch page. ~10
lines of JS. Trivially removable by a determined thief — but the people mass-reuploading web games
are running scripts, not reverse engineers, so it stops most of them.

**Not implemented, deliberately — this needs one fact I cannot get from the repo.** A blocking
sitelock with the wrong allowlist does not degrade, it refuses to boot the real game on the real
store page. itch has served HTML5 builds from more than one host over the years
(`html-classic.itch.zone`, `*.ssl.hwcdn.net`, and the CDN name varies), and the plan's own guidance
is that guessing here is the failure mode.

To unblock: open the published game on itch, and from the browser's dev tools read the origin the
game frame is actually served from (`location.origin` inside the game iframe). With that one string
this is an afternoon. Two things to keep in the implementation:

- Allow `localhost`/`127.0.0.1`, or CI's browser smoke test — which serves the build from
  `127.0.0.1:8123` — starts failing.
- Fail open on anything unexpected rather than closed. A sitelock that wrongly blocks a paying
  audience costs more than one that lets a thief through.

Also worth having: a written DMCA/reporting routine, since itch's own guidance is that reporting is
the remedy once a build is rehosted.

---

## 5. Phase C — obfuscation (do last, and only where it pays)

### What is available

- **Obfuscar** — free, open source, XML-configured, the tool the Godot community actually uses.
- **ConfuserEx / Babel / Eazfuscator / .NET Reactor** — stronger (string encryption, control-flow
  obfuscation, virtualization) but commercial and/or unmaintained.
- Godot has **no official obfuscator support**; [godot-proposals#1407](https://github.com/godotengine/godot-proposals/issues/1407)
  is open and unresolved. Anything we do is a bolt-on to the export pipeline.

### Known breakages in a Godot C# assembly

From a developer who shipped this
([Minerva Labyrinth devlog](https://midnightspiregames.itch.io/minerva-labyrinth/devlog/899501/c-obfuscation-in-godot)),
Obfuscar against a Godot assembly needs at minimum:

```xml
<SkipNamespace name="GodotPlugins\*" />
<SkipMethod type="*" name="*Godot*" />
<SkipType name="*SerialBlock*" skipFields="true" />
```

covering Godot's generated bindings, inspector-connected signals, and JSON serialization.

### Why this codebase is a hard case

Measured on `src/`:

- **286** `FindChild("literal")` lookups and 10 string `GetNodeOrNull<T>("...")` paths
- **535** `[Signal]` / `[Export]` attributes
- **38** `Name = nameof(T)` node namings
- **8** `.tscn` files binding scripts by path

Most of this is *probably* survivable — `nameof` and the source generators bake literals at compile
time, so renaming types afterwards leaves the strings intact — but "probably" across 535 attributes
and 286 string lookups is a large verification surface with failures that appear only at runtime, in
specific screens. This is exactly the kind of change that ships a broken build to players.

### Where it would actually pay

Not on the web build: obfuscation would have to run on the IL *before* AOT, inside a CI pipeline
that compiles a pinned Godot fork, to protect the least-likely threat on the hardest-to-read target.
Poor trade.

**On the two Windows presets it is a much better trade** — they ship plain IL today, one ILSpy click
from full source. If we obfuscate anything, obfuscate those.

### Suggested sequencing if pursued

1. Obfuscar on the **Windows Steam Demo** preset only, post-build, pre-package.
2. Verify with ILSpy that names are actually mangled.
3. Full manual pass over every screen, plus the whole scenario/journey suite against the obfuscated
   build.
4. Only then consider the full release; skip the web build entirely.

### PCK encryption

`encrypt_pck=false` on all three presets. Turning it on is tempting but near-worthless on its own:
the key sits as a static 32-byte blob in the binary's `.data` section, unobfuscated and not derived
at runtime, and public tools recover it in seconds. For the web build the key would live in the
delivered wasm — same problem, more visible. Treat as speed bump only, and note this needs a check
that the pinned Web fork even supports it.

---

## 6. Recommendation

1. **Phase A** — the only work that closes a real hole. Worth doing properly even though it is the
   biggest job, because it is what protects *future* paid content, not just today's demo.
2. **Phase B** — an afternoon, targets the threat that actually happens to itch HTML5 games.
3. **Phase C** — defer. If pursued, aim it at the **native Windows builds**, which are the soft
   target, not at the web build the question started from.

Resolved 2026-09-07: itch is **web-only**, hosted from the CI zip with "play in the browser", and
`preset.2` is the only Web preset. Phase A therefore keys off the existing `DESKTOP_BUDDY_PUBLIC_WEB`
and needs no new constant (§A1). Nothing else in the plan is blocked.

### Honest ceiling

No client-delivered build can be made secret. Everything here raises cost and reduces casual theft;
none of it makes the game safe from a determined attacker. The realistic goal is: make unlocking
held-back features impossible (Phase A genuinely achieves this, because the code is not there), and
make everything else annoying enough that the low-effort attackers move on.

---

## Sources

- [dotnet/runtime discussion #105635 — does AOT + IL removal really work with Blazor WASM](https://github.com/dotnet/runtime/discussions/105635)
- [Softanics — Blazor WebAssembly obfuscation](https://www.softanics.com/net-obfuscation/platforms/blazor-webassembly)
- [godot-proposals#1407 — provide obfuscator support for C#](https://github.com/godotengine/godot-proposals/issues/1407)
- [Minerva Labyrinth devlog — C# obfuscation in Godot](https://midnightspiregames.itch.io/minerva-labyrinth/devlog/899501/c-obfuscation-in-godot)
- [Alice GG — protecting Godot games against reverse engineering](https://alicegg.tech/2026/04/03/godot-encryption)
- [gdsdecomp (Godot RE Tools)](https://github.com/bruvzg/gdsdecomp)
- [godotdec — Godot pck unpacker](https://github.com/Bioruebe/godotdec)
- [itch.io — sitelock update](https://itch.io/t/614172/sitelock-update)
- [itch.io — PSA: unauthorized reuploads, reporting guide](https://itch.io/devlog/998149/psa-unauthorized-reuploads-step-by-step-guide-for-reporting)
