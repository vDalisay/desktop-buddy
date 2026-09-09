#!/usr/bin/env python3
"""Prepare a disposable Initial Steam Demo checkout for the NativeAOT compatibility spike.

The repository keeps normal CoreCLR/Godot C# settings for development. This helper is intentionally
run only inside a disposable release checkout after the H1 Initial Steam Demo scope has been applied.
It adds the conservative NativeAOT settings needed by Godot's Windows exporter without changing the
checked-in DesktopBuddy.csproj used by normal builds.
"""

from __future__ import annotations

import argparse
from pathlib import Path

MARKER = "<!-- Desktop Buddy NativeAOT Initial Steam Demo spike -->"
BLOCK = r'''

  <!-- Desktop Buddy NativeAOT Initial Steam Demo spike -->
  <PropertyGroup Condition=" '$(GodotTargetPlatform)' != 'web' AND '$(DesktopBuddyInitialSteamDemoScope)' == 'true' ">
    <PublishAOT>true</PublishAOT>
    <DebugSymbols>false</DebugSymbols>
    <DebugType>none</DebugType>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <!--
    Keep the first spike conservative. Godot and Desktop Buddy contain reflection-sensitive paths,
    so root the application assemblies first and prove compatibility before narrowing preservation.
    This still removes the normal project IL assembly from the exported player.
  -->
  <ItemGroup Condition=" '$(GodotTargetPlatform)' != 'web' AND '$(DesktopBuddyInitialSteamDemoScope)' == 'true' ">
    <TrimmerRootAssembly Include="GodotSharp" />
    <TrimmerRootAssembly Include="DesktopBuddy" />
    <TrimmerRootAssembly Include="DesktopBuddy.Domain" />
    <TrimmerRootAssembly Include="DesktopBuddy.Visuals" />
  </ItemGroup>
'''


def prepare(root: Path) -> bool:
    project = root / "DesktopBuddy.csproj"
    if not project.is_file():
        raise SystemExit(f"Missing project file: {project}")

    text = project.read_text(encoding="utf-8")
    if MARKER in text:
        return False

    closing = "\n</Project>"
    if closing not in text:
        raise SystemExit("DesktopBuddy.csproj has no expected closing </Project> tag.")

    project.write_text(text.replace(closing, BLOCK + closing, 1), encoding="utf-8", newline="")
    return True


def check(root: Path) -> None:
    project = root / "DesktopBuddy.csproj"
    if not project.is_file():
        raise SystemExit(f"Missing project file: {project}")
    text = project.read_text(encoding="utf-8")

    required = (
        MARKER,
        "<PublishAOT>true</PublishAOT>",
        "'$(DesktopBuddyInitialSteamDemoScope)' == 'true'",
        '<TrimmerRootAssembly Include="GodotSharp" />',
        '<TrimmerRootAssembly Include="DesktopBuddy" />',
        '<TrimmerRootAssembly Include="DesktopBuddy.Domain" />',
        '<TrimmerRootAssembly Include="DesktopBuddy.Visuals" />',
    )
    missing = [value for value in required if value not in text]
    if missing:
        raise SystemExit("NativeAOT spike preparation is incomplete: " + ", ".join(missing))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--check", action="store_true", help="Fail unless the spike settings are present")
    args = parser.parse_args()
    root = Path(args.root).resolve()

    if args.check:
        check(root)
        print("NativeAOT Initial Steam Demo spike configuration verified.")
        return 0

    changed = prepare(root)
    check(root)
    print("Prepared NativeAOT Initial Steam Demo spike." if changed else "NativeAOT spike configuration already present.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
