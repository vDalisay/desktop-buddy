# Desktop Buddy — protected Steam demo build

Status: implemented on `agent/steam-demo-hardening`  
Base: `agent/steam-demo-polish`  
Target: Windows x86_64 / Godot 4.6.1 .NET

## Goal and threat model

This pipeline is a deterrence layer for the public Steam demo. It is designed to make casual asset ripping and straightforward ILSpy/dnSpy reconstruction materially more expensive without moving protection work into the gameplay hot path.

No offline PC build can be made impossible to reverse engineer. The machine must eventually receive executable code, assets and the PCK key. The goal is therefore layered cost: do not ship developer material, encrypt/embed Godot resources, strip symbols/source content, and rename managed implementation details while preserving Godot and save-format contracts.

Source confidentiality still assumes the source repository is private before the public demo ships.

## Build entry point

From a Windows checkout of this branch:

```bat
tools\build_protected_demo.bat
```

The final package is written to:

```text
build\steam-demo-protected\
```

The ordinary `Windows Desktop` export preset remains unchanged. The protection pipeline uses the separate `Windows Steam Demo Protected` preset only.

## First-build prerequisites

The protected build needs the same pinned Godot 4.6.1 .NET editor as development plus the native toolchain required to compile a Windows Godot .NET export template:

- Git;
- Python and SCons available on `PATH` (`py -m pip install scons` if needed);
- Visual Studio 2022 / Build Tools with the Desktop development with C++ workload and a Windows SDK;
- .NET SDK already used by this project;
- Godot 4.6.1 .NET, resolvable by `tools/resolve_godot.bat` or `GODOT_PATH`.

The first run clones the exact `4.6.1-stable` Godot source into the ignored `.protected` cache, generates Mono glue, and builds a `production=yes`, `module_mono_enabled=yes` Windows x86_64 release template. Later builds reuse that template as long as the encryption-key fingerprint still matches.

## Encryption key handling

When no key is supplied, `build_protected_demo.ps1` creates a cryptographically random 256-bit key at:

```text
.protected\pck-encryption.gdkey
```

`.protected/` and `*.gdkey` are git-ignored. The scripts never print the key. Back this file up in a secure private location: the custom export template is compiled with this key, so losing it means rebuilding a new template with a new key for subsequent packages.

For automated/private release machines the key may instead be passed as `-EncryptionKey` or through `DESKTOP_BUDDY_PCK_KEY`. Do not add the key to `export_presets.cfg` or source control.

## Protection layers

### 1. Demo export boundary

The protected preset retains the owner-approved demo runtime but excludes development/test/authoring/tooling/source-adjacent files. A validation gate rejects source files, project/build files, Markdown, scripts, PDBs, loose PCKs and known developer directory trees if they appear in the final package.

### 2. Encrypted and embedded Godot resources

The protected preset enables:

```text
encrypt_pck=true
encrypt_directory=true
encryption_include_filters="*"
binary_format/embed_pck=true
```

Godot requires a custom export template compiled with the same AES-256 key; the pipeline creates that template and supplies the same key during export. Both the pack directory and the resource payloads are encrypted. The PCK is embedded into `DesktopBuddyDemo.exe` instead of being shipped as an obvious loose archive.

### 3. Managed-code obfuscation

After Godot exports its .NET assemblies, the pipeline installs the stable `Obfuscar.GlobalTool` 2.2.50 into `.protected` and processes:

- `DesktopBuddy.dll`
- `DesktopBuddy.Domain.dll`
- `DesktopBuddy.Visuals.dll`

The default profile follows the conservative .NET-library boundary: public APIs remain stable while private/internal implementation names are renamed. Fields in the Godot-facing main assembly and Godot-generated bootstrap entry points are retained because the .NET runtime consumes that metadata during startup. This is important because Godot bindings and `System.Text.Json` persistence depend on externally visible names remaining compatible.

The profile deliberately disables string hiding and method optimization:

```text
HideStrings=false
OptimizeMethods=false
```

Those transformations provide relatively little additional protection for this project while adding runtime work or compatibility risk. Unicode-name tricks, runtime anti-debug loops, packers and control-flow virtualization are also intentionally absent.

If a future Godot callback proves incompatible with additional private-member renaming, a fallback package can be built with:

```bat
tools\build_protected_demo.bat -ConservativeMainAssembly
```

That preserves all members inside the Godot-facing main assembly while still obfuscating the less engine-coupled assemblies. Treat this as a compatibility fallback, not the preferred release mode.

### 4. Symbol/source stripping

The protected preset sets:

```text
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
debug/export_console_wrapper=0
```

The pipeline also deletes accidental Desktop Buddy PDB/XML documentation leftovers before package validation.

### 5. Package validation and smoke test

Every protected build:

1. checks that the hardening configuration has not drifted;
2. ensures the keyed custom template exists and matches the key fingerprint;
3. exports the encrypted/embedded release;
4. obfuscates the three project assemblies and verifies that obfuscation changed output;
5. strips accidental symbols/docs;
6. rejects forbidden content or a loose PCK;
7. launches `DesktopBuddyDemo.exe` and requires it to stay alive through a short startup smoke test.

`-SkipSmokeTest` exists for controlled automation only. Do not use it for a release candidate without replacing it with an equivalent launch test.

## Runtime-performance policy

| Layer | Expected runtime impact |
| --- | --- |
| Private/internal name obfuscation | Effectively none; metadata names change, gameplay IL is not virtualized |
| String hiding | Disabled; no lookup/decode cost |
| Method optimization/control-flow rewriting | Disabled |
| Symbol/source stripping | None |
| Embedded PCK | No meaningful per-frame cost |
| AES resource encryption | Work occurs while encrypted files are read; not a per-frame protection loop |
| `production=yes` custom Godot template | Production-oriented engine build; debug symbols/checks are not added to the player |
| Anti-debug/anti-tamper polling | Not implemented |

The pipeline therefore favors startup/resource-load protection over permanent CPU cost. Performance-sensitive gameplay, physics and rendering code is not wrapped in protection checks.

## Release verification

The short automatic smoke test proves startup, not the whole game. Before uploading a protected demo build to Steam, run the normal clean-save demo acceptance pass against the **protected executable**, especially:

- normal grab and physical tools;
- purchase/equip/save/reload;
- Paint Buddy save/reset;
- Buddy Studio;
- Work Mode enter/type/exit;
- dropped-tool round trip;
- audio and runtime-loaded assets.

This catches any engine/reflection boundary that a managed obfuscator could expose only after a specific feature is opened.

## What this does not promise

A determined reverse engineer can still eventually recover behavior. The AES key must exist in the native executable, public .NET contracts remain intentionally readable, and machine code/IL can always be studied dynamically. The intended outcome is that copying Desktop Buddy is no longer a one-click resource extraction plus clean managed decompile, while normal players pay essentially no ongoing performance tax for the protection.
