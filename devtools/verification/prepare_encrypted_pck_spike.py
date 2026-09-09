#!/usr/bin/env python3
"""Prepare the Windows Steam Demo preset for the H3 encrypted/embedded-PCK spike.

This helper edits only a disposable CI checkout. Production release presets stay unchanged until
compatibility is proven with a custom Godot .NET template compiled with the same AES-256 key used
for export.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

PRESET_PATH = Path("export_presets.cfg")
PRESET_HEADER = "[preset.0]"
OPTIONS_HEADER = "[preset.0.options]"
EXPECTED_NAME = 'name="Windows Steam Demo"'
EXPECTED_PLATFORM = 'platform="Windows Desktop"'


def _replace_or_insert(section: str, key: str, value: str) -> str:
    pattern = re.compile(rf"(?m)^{re.escape(key)}=.*$")
    matches = list(pattern.finditer(section))
    if len(matches) > 1:
        raise ValueError(f"Expected at most one {key!r} entry, found {len(matches)}.")

    replacement = f"{key}={value}"
    if matches:
        match = matches[0]
        return section[: match.start()] + replacement + section[match.end() :]

    if not section.endswith("\n"):
        section += "\n"
    return section + replacement + "\n"


def _split_preset(text: str) -> tuple[str, str, str, str]:
    if text.count(PRESET_HEADER) != 1 or text.count(OPTIONS_HEADER) != 1:
        raise ValueError("Expected exactly one Windows Steam Demo preset.0/options section.")
    preset_start = text.find(PRESET_HEADER)
    options_start = text.find(OPTIONS_HEADER)
    if preset_start < 0 or options_start < 0 or options_start <= preset_start:
        raise ValueError("Windows Steam Demo preset.0/options sections are missing or malformed.")

    next_preset = text.find("\n[preset.", options_start + len(OPTIONS_HEADER))
    end = len(text) if next_preset < 0 else next_preset + 1
    return (
        text[:preset_start],
        text[preset_start:options_start],
        text[options_start:end],
        text[end:],
    )


def _template_value(custom_template: str) -> str:
    return Path(custom_template).resolve().as_posix()


def prepare(text: str, custom_template: str) -> str:
    prefix, preset, options, suffix = _split_preset(text)
    if EXPECTED_NAME not in preset or EXPECTED_PLATFORM not in preset:
        raise ValueError(
            "preset.0 is no longer the expected Windows Steam Demo preset; refusing to edit it."
        )

    preset = _replace_or_insert(preset, "encryption_include_filters", '"*"')
    preset = _replace_or_insert(preset, "encryption_exclude_filters", '""')
    preset = _replace_or_insert(preset, "encrypt_pck", "true")
    preset = _replace_or_insert(preset, "encrypt_directory", "true")
    options = _replace_or_insert(
        options, "custom_template/release", f'"{_template_value(custom_template)}"'
    )
    options = _replace_or_insert(options, "binary_format/embed_pck", "true")
    return prefix + preset + options + suffix


def verify(text: str, custom_template: str) -> list[str]:
    errors: list[str] = []
    try:
        _, preset, options, _ = _split_preset(text)
    except ValueError as exc:
        return [str(exc)]

    if EXPECTED_NAME not in preset or EXPECTED_PLATFORM not in preset:
        errors.append("preset.0 is not the expected Windows Steam Demo preset.")

    for section, key in (
        (preset, "encryption_include_filters"),
        (preset, "encryption_exclude_filters"),
        (preset, "encrypt_pck"),
        (preset, "encrypt_directory"),
        (options, "custom_template/release"),
        (options, "binary_format/embed_pck"),
    ):
        if len(re.findall(rf"(?m)^{re.escape(key)}=.*$", section)) != 1:
            errors.append(f"Expected exactly one {key!r} entry.")

    required = [
        (preset, 'encryption_include_filters="*"', "all packed resources are selected for encryption"),
        (preset, 'encryption_exclude_filters=""', "no resource encryption exclusions are configured"),
        (preset, "encrypt_pck=true", "PCK encryption is enabled"),
        (preset, "encrypt_directory=true", "PCK directory encryption is enabled"),
        (
            options,
            f'custom_template/release="{_template_value(custom_template)}"',
            "the custom release template is selected",
        ),
        (options, "binary_format/embed_pck=true", "the PCK is embedded in the executable"),
    ]
    for section, needle, description in required:
        if needle not in section:
            errors.append(f"Missing {description}: {needle}")
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", default=".")
    parser.add_argument("--custom-template", required=True)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args(argv)

    path = Path(args.root) / PRESET_PATH
    if not path.is_file():
        print(f"Missing {path}", file=sys.stderr)
        return 2

    text = path.read_text(encoding="utf-8")
    if args.check:
        errors = verify(text, args.custom_template)
        if errors:
            for error in errors:
                print(error, file=sys.stderr)
            return 1
        print("Encrypted embedded-PCK spike preset is configured fail-closed.")
        return 0

    try:
        updated = prepare(text, args.custom_template)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    path.write_text(updated, encoding="utf-8")
    print(f"Prepared {path} for encrypted embedded-PCK compatibility testing.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
