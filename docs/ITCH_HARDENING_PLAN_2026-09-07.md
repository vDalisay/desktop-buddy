# itch.io hardening plan — feature exclusion and anti-decompilation

Status: **proposal, nothing implemented.** Written 2026-09-07 after the itch tutorial work
(`feature/itch-tutorial`) exposed that every held-back feature still ships in the itch build and is
switched off by a runtime boolean.

---

## 1. What we actually ship today

| Distribution | Preset | Platform | Managed code as shipped | PCK |
| --- | --- | --- | --- | --- |
| Steam Demo | `preset.0` | Windows Desktop | **Plain IL `DesktopBuddy.dll`** | `encrypt_pck=false` |
| Full Release | `preset.1` | Windows Desktop | **Plain IL `DesktopBuddy.dll`** | `encrypt_pck=false` |
| itch.io | `preset.2` | **Web (wasm)** | AOT wasm + Webcil, IL stripped, trimmed | `encrypt_pck=false` |

The itch build is a *Web* export, built in CI by a pinned Godot fork (PR #118976) and pushed with
`butler push build/itch-web`. This matters: almost every "protect your Godot game" article assumes a
native export, and the advice does not transfer cleanly.

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

### A1. Introduce a compile constant

Add to the existing `net10.0` PropertyGroup (the itch/web block) in `DesktopBuddy.csproj`:

```xml
<DefineConstants>$(DefineConstants);DESKTOP_BUDDY_ITCH</DefineConstants>
```

Do **not** reuse `DESKTOP_BUDDY_PUBLIC_WEB`. Keep "this is the browser runtime" and "this is the
reduced distribution" as separate ideas, or a future native itch build silently gets the wrong set.

### A2. Remove the source from the compile

```xml
<ItemGroup Condition=" '$(TargetFramework)' == 'net10.0' ">
  <Compile Remove="src/Work/**/*.cs" />
  <Compile Remove="src/CharacterEditor/BuddyStudio/**/*.cs" />
  <Compile Remove="src/Environment/**/*.cs" />
</ItemGroup>
```

Then fix the fallout behind `#if !DESKTOP_BUDDY_ITCH` at each composition site: `Bootstrap`,
`DesktopShellController`, `CharacterEditorHost`, `SandboxRoot`. `DemoScope.Includes*` become
compile-time constants rather than runtime reads.

Expect this to be the bulk of the work. The three subsystems are not cleanly severable today —
`src/Environment/` also owns non-Paint-Room environment code, and `DesktopShellController` threads
Work Mode through the shell. **Budget a real refactor, not an afternoon.** Each removal wants its
own commit and a green quick suite.

### A3. Drop the autoloads

Autoloads live in `project.godot`, which is data — a `#if` cannot touch it. Options:

- **A3a (preferred):** have CI rewrite `project.godot` for the itch export, stripping the
  Work/Studio/Paint-Room autoload lines. The itch job already rewrites the csproj to `net10.0`, so
  a second scripted edit fits the existing pipeline shape.
- **A3b:** move those autoloads out of `project.godot` and register them from `Bootstrap` behind the
  `#if`. Cleaner long-term, larger blast radius across all builds.

Take A3a first; revisit A3b only if the rewrite gets fragile.

### A4. Exclude the assets

Extend `exclude_filter` on `preset.2` with the scenes, `.tres` and art owned by the excluded
features. Then **verify by extracting the built `.pck` with `gdsdecomp` and diffing the file list** —
this is the acceptance test for the whole phase, not a code review.

### A5. Keep the runtime gate as a second layer

Leave `DemoScope` in place. Compile-time exclusion is the lock; the runtime check stays as
defence-in-depth and keeps editor runs and scenario tests working, exactly as `IncludesWorkshop` now
does both.

### Acceptance

- `gdsdecomp` on the shipped `.pck` lists no Work Mode / Paint Room / Buddy Studio assets.
- No Work/Studio/PaintRoom type names in the shipped assembly.
- Editing feature tags in the pck cannot restore them.
- Quick suite green; `tutorial_closure` green.

---

## 4. Phase B — reupload and sitelock (cheap, targets threat #1)

itch already ships a global JS sitelock that shows a banner when a game is loaded from a non-itch
domain, plus automated hotlink prevention. Neither stops a full rehost.

Add our own check in `html/head_include` (the preset already uses that field for the SRI cache fix):
allow `itch.zone` / `itch.io` / `localhost`, otherwise refuse to boot and link the itch page. ~10
lines of JS. Trivially removable by a determined thief — but the people mass-reuploading web games
are running scripts, not reverse engineers, so it stops most of them.

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

One thing to decide before Phase A: whether a **native itch build** is ever planned. If yes, the
`#if` constant must key off the distribution and not the web target, and the autoload rewrite (A3a)
needs to run for that export too. The plan above assumes web-only and flags A1 accordingly.

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
