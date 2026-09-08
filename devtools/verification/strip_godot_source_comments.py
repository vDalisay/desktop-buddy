#!/usr/bin/env python3
"""Strip project-owned Godot source comments from disposable shipping copies.

C# comments never enter IL, but Godot text sources such as GDScript and shader files can be
packaged as source/text resources. This tool removes comments from those project-owned files only
in the CI/export copy. It deliberately skips dependency/vendor trees so third-party license notices
are not rewritten.

The scrubbers are lexical rather than regex-based: comment delimiters inside quoted strings are
preserved, line endings are retained, and removed characters are replaced with spaces so line and
column positions remain stable for diagnostics.

Usage:
    python3 devtools/verification/strip_godot_source_comments.py --root <project-copy>
    python3 devtools/verification/strip_godot_source_comments.py --root <project-copy> --check
    python3 devtools/verification/strip_godot_source_comments.py --self-test
"""

from __future__ import annotations

import argparse
import pathlib
import sys
from dataclasses import dataclass

SOURCE_SUFFIXES = {".gd", ".gdshader"}
SKIPPED_DIRECTORY_NAMES = {
    ".git",
    ".godot",
    ".protected",
    ".web-build",
    "addons",
    "build",
    "external",
    "third-party",
    "third_party",
    "vendor",
}


class StripError(ValueError):
    pass


@dataclass(frozen=True)
class StripResult:
    text: str
    comments: int


def _blank_range(out: list[str], text: str, start: int, end: int) -> None:
    """Blank a comment while preserving CR/LF and therefore source coordinates."""
    for index in range(start, end):
        if text[index] not in "\r\n":
            out[index] = " "


def strip_gdscript(text: str) -> StripResult:
    """Remove ``#`` comments outside single/double/triple-quoted strings."""
    out = list(text)
    length = len(text)
    index = 0
    comments = 0
    quote: str | None = None
    triple = False

    while index < length:
        char = text[index]

        if quote is not None:
            if char == "\\":
                index += 2
                continue

            if triple:
                delimiter = quote * 3
                if text.startswith(delimiter, index):
                    index += 3
                    quote = None
                    triple = False
                else:
                    index += 1
                continue

            if char == quote:
                quote = None
            index += 1
            continue

        if char in ("'", '"'):
            quote = char
            triple = text.startswith(char * 3, index)
            index += 3 if triple else 1
            continue

        if char == "#":
            end = index
            while end < length and text[end] not in "\r\n":
                end += 1
            _blank_range(out, text, index, end)
            comments += 1
            index = end
            continue

        index += 1

    if quote is not None:
        raise StripError("unterminated GDScript string literal")

    return StripResult("".join(out), comments)


def strip_shader(text: str) -> StripResult:
    """Remove ``//`` and ``/* ... */`` comments outside quoted strings."""
    out = list(text)
    length = len(text)
    index = 0
    comments = 0
    quote: str | None = None

    while index < length:
        char = text[index]

        if quote is not None:
            if char == "\\":
                index += 2
                continue
            if char == quote:
                quote = None
            index += 1
            continue

        if char in ("'", '"'):
            quote = char
            index += 1
            continue

        if text.startswith("//", index):
            end = index + 2
            while end < length and text[end] not in "\r\n":
                end += 1
            _blank_range(out, text, index, end)
            comments += 1
            index = end
            continue

        if text.startswith("/*", index):
            end_marker = text.find("*/", index + 2)
            if end_marker < 0:
                raise StripError("unterminated shader block comment")
            end = end_marker + 2
            _blank_range(out, text, index, end)
            comments += 1
            index = end
            continue

        index += 1

    if quote is not None:
        raise StripError("unterminated shader string literal")

    return StripResult("".join(out), comments)


def _is_project_owned(path: pathlib.Path, root: pathlib.Path) -> bool:
    relative = path.relative_to(root)
    return not any(part.lower() in SKIPPED_DIRECTORY_NAMES for part in relative.parts[:-1])


def source_files(root: pathlib.Path) -> list[pathlib.Path]:
    return sorted(
        path
        for path in root.rglob("*")
        if path.is_file()
        and path.suffix.lower() in SOURCE_SUFFIXES
        and _is_project_owned(path, root)
    )


def scrub(path: pathlib.Path) -> StripResult:
    with path.open("r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    if path.suffix.lower() == ".gd":
        return strip_gdscript(text)
    return strip_shader(text)


def write_exact(path: pathlib.Path, text: str) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def run_self_test() -> None:
    gd = (
        "# file comment\n"
        "var number = 3 # inline comment\r\n"
        'var double_q = "# not a comment" # remove me\n'
        "var single_q = '# not a comment' # remove me too\n"
        'var triple_q = """doc # remains\nsecond line""" # final\n'
        'var escaped = "quote: \\\" # remains" # gone\n'
    )
    gd_result = strip_gdscript(gd)
    assert gd_result.comments == 6
    assert '"# not a comment"' in gd_result.text
    assert "'# not a comment'" in gd_result.text
    assert "doc # remains" in gd_result.text
    assert "# file comment" not in gd_result.text
    assert "# inline comment" not in gd_result.text
    assert gd_result.text.count("\n") == gd.count("\n")
    assert strip_gdscript(gd_result.text).comments == 0

    shader = (
        "// header\n"
        "shader_type canvas_item; /* explanation */\n"
        'const char_like = "https://example.invalid/a//b"; // remove\n'
        "/* multi\nline\ncomment */\n"
    )
    shader_result = strip_shader(shader)
    assert shader_result.comments == 4
    assert '"https://example.invalid/a//b"' in shader_result.text
    assert "explanation" not in shader_result.text
    assert shader_result.text.count("\n") == shader.count("\n")
    assert strip_shader(shader_result.text).comments == 0

    print("Godot source comment stripper self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        run_self_test()
        return 0
    if args.root is None:
        parser.error("--root is required unless --self-test is used")

    root = args.root.resolve()
    if not root.is_dir():
        raise SystemExit(f"{root}: project root does not exist")

    scanned = 0
    changed = 0
    comments = 0
    pending: list[str] = []

    for path in source_files(root):
        scanned += 1
        try:
            result = scrub(path)
        except (OSError, UnicodeError, StripError) as exc:
            raise SystemExit(f"{path}: comment scrub failed: {exc}") from exc
        if result.comments == 0:
            continue

        relative = path.relative_to(root).as_posix()
        changed += 1
        comments += result.comments
        if args.check:
            pending.append(f"{relative}: {result.comments} comment(s)")
        else:
            write_exact(path, result.text)
            print(f"stripped {result.comments} comment(s): {relative}")

    if args.check and pending:
        raise SystemExit(
            "project-owned Godot source comments remain in shipping copy:\n  "
            + "\n  ".join(pending)
        )

    action = "verified" if args.check else "scrubbed"
    print(
        f"Godot source comments {action}: files_scanned={scanned}, "
        f"files_changed={changed}, comments={comments}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
