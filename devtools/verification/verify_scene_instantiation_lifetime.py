#!/usr/bin/env python3
"""Export a NativeAOT regression probe for the production PackedScene lifetime boundary.

Requires the pinned Godot .NET editor, .NET SDK and Windows NativeAOT build tools.
Uses installed export templates unless --custom-template is supplied. No game saves are used.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import tempfile
from textwrap import dedent

from run_checked_process import run


def verify(godot: Path, custom_template: Path | None, dotnet: str) -> None:
    source = Path(__file__).resolve().parents[2] / "src/App/SceneInstantiation.cs"
    # Godot's export runs `dotnet` from PATH, and the NativeAOT linker step needs vswhere there to
    # locate the MSVC linker. CI has both; a local x86 dotnet on PATH silently fails the publish.
    prefix = [] if dotnet == "dotnet" else [Path(dotnet).resolve().parent]
    installer = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer"
    if (installer / "vswhere.exe").is_file():
        prefix.append(installer)
    if prefix:
        os.environ["PATH"] = os.pathsep.join([str(path) for path in prefix] + [os.environ["PATH"]])
    with tempfile.TemporaryDirectory(prefix="desktop-buddy-scene-lifetime-") as directory:
        root = Path(directory)
        project = root / "project"
        output = root / "export"
        project.mkdir()
        output.mkdir()

        def write(name: str, text: str) -> None:
            (project / name).write_text(dedent(text).strip() + "\n", encoding="utf-8")

        def checked(name: str, command: list[str], timeout: float = 120) -> Path:
            log = root / f"{name}.log"
            if run(command, timeout, log) != 0:
                raise SystemExit(f"Scene lifetime regression failed during {name}.")
            return log

        shutil.copyfile(source, project / source.name)
        write("Probe.csproj", """
            <Project Sdk="Godot.NET.Sdk/4.6.1">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <PublishAot>true</PublishAot>
              </PropertyGroup>
              <ItemGroup>
                <TrimmerRootAssembly Include="GodotSharp" />
                <TrimmerRootAssembly Include="Probe" />
              </ItemGroup>
            </Project>
        """)
        write("project.godot", """
            config_version=5
            [application]
            config/name="DesktopBuddySceneLifetimeProbe"
            run/main_scene="res://main.tscn"
            [dotnet]
            project/assembly_name="Probe"
            [rendering]
            renderer/rendering_method="gl_compatibility"
        """)
        write("export_presets.cfg", """
            [preset.0]
            name="Windows"
            platform="Windows Desktop"
            runnable=true
            export_filter="all_resources"
            include_filter=""
            exclude_filter=""
            [preset.0.options]
            binary_format/architecture="x86_64"
        """)
        if custom_template:
            with (project / "export_presets.cfg").open("a", encoding="utf-8") as handle:
                handle.write(f'custom_template/release="{custom_template.as_posix()}"\n')
        write("Main.cs", """
            using Godot;
            using DesktopBuddy.App;
            public partial class Main : Node
            {
                public override void _Ready()
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var scene = GD.Load<PackedScene>("res://child.tscn");
                        Node node = SceneInstantiation.Instantiate<Node>(scene);
                        if (node.GetChildCount() != 100)
                        {
                            GetTree().Quit(1);
                            return;
                        }
                        node.Free();
                    }
                    GD.Print("SCENE_LIFETIME_PASS");
                    GetTree().Quit();
                }
            }
        """)
        write("Child.cs", """
            using System;
            using Godot;
            public partial class Child : Node
            {
                public Child()
                {
                    // Collect while native Instantiate still needs the parent's SceneState.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        """)
        for name, script in (("main", "Main"), ("child", "Child")):
            write(f"{name}.tscn", f"""
                [gd_scene load_steps=2 format=3]
                [ext_resource type="Script" path="res://{script}.cs" id="1"]
                [node name="{script}" type="Node"]
                script=ExtResource("1")
            """)
        with (project / "child.tscn").open("a", encoding="utf-8") as handle:
            for i in range(100):
                handle.write(f'[node name="N{i}" type="Node" parent="."]\n')

        checked("solution", [dotnet, "new", "sln", "-n", "Probe", "-o", str(project)])
        checked("project", [dotnet, "sln", str(project / "Probe.sln"), "add", str(project / "Probe.csproj")])
        checked("build", [dotnet, "build", str(project / "Probe.csproj"), "-c", "Debug"])
        executable = output / "Probe.exe"
        export = checked("export", [str(godot), "--headless", "--path", str(project),
                                    "--export-release", "Windows", str(executable)], 600)
        if "Failed to build project" in export.read_text(encoding="utf-8"):
            raise SystemExit("Scene lifetime probe could not publish its NativeAOT assembly.")
        log = checked("startup", [str(executable), "--headless", "--quit-after", "120"], 60)
        text = log.read_text(encoding="utf-8")
        if "SCENE_LIFETIME_PASS" not in text or "ERROR:" in text:
            raise SystemExit("NativeAOT scene lifetime probe did not complete cleanly.")
        print("NativeAOT scene lifetime regression passed (3 instantiations with forced GC).")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--custom-template", type=Path)
    parser.add_argument("--dotnet", default="dotnet", help="Path to the x64 .NET CLI")
    args = parser.parse_args()
    verify(args.godot.resolve(), args.custom_template.resolve() if args.custom_template else None, args.dotnet)
