#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Audit and optionally add SPDX headers to repository-owned source files.

Third-party files keep their original licenses; JSON and Markdown cannot carry
portable source comments, so they are covered by the repository LICENSE instead.
"""

import argparse
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COMMENT_PREFIX = {
    ".cs": "//",
    ".py": "#",
    ".ps1": "#",
    ".sh": "#",
    ".yml": "#",
    ".iss": ";",
    ".csproj": "<!--",
}
THIRD_PARTY = {
    "SharpPixels/HqxScaler.cs",
}


def tracked_sources():
    paths = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).split(b"\0")
    paths.append(b"Tools/audit_source_headers.py")
    seen = set()
    for raw in paths:
        if not raw:
            continue
        name = raw.decode("utf-8")
        if name in seen:
            continue
        seen.add(name)
        if name.startswith("external-tests/") or name in THIRD_PARTY:
            continue
        suffix = Path(name).suffix
        if suffix in COMMENT_PREFIX:
            yield name, ROOT / name, COMMENT_PREFIX[suffix]


def add_header(content, prefix):
    newline = "\r\n" if b"\r\n" in content else "\n"
    text = content.decode("utf-8-sig")
    bom = b"\xef\xbb\xbf" if content.startswith(b"\xef\xbb\xbf") else b""
    if prefix == "<!--":
        header = "<!-- Copyright 2026 Nils Kopal; SPDX-License-Identifier: Apache-2.0 -->"
    else:
        header = f"{prefix} Copyright 2026 Nils Kopal{newline}{prefix} SPDX-License-Identifier: Apache-2.0"
    if text.startswith("#!"):
        first, separator, rest = text.partition("\n")
        # Preserve a CRLF shebang without creating a mixed line ending.
        return bom + (first + separator + header + newline + rest).encode("utf-8")
    return bom + (header + newline + text).encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fix", action="store_true", help="Add missing headers")
    args = parser.parse_args()
    missing = []
    for name, path, prefix in tracked_sources():
        content = path.read_bytes()
        if b"Apache License, Version 2.0" in content[:2000] or b"SPDX-License-Identifier: Apache-2.0" in content[:2000]:
            continue
        missing.append(name)
        if args.fix:
            path.write_bytes(add_header(content, prefix))
    for name in missing:
        print(name)
    print(f"{len(missing)} source files {'fixed' if args.fix else 'missing Apache-2.0 headers'}")
    return 0 if args.fix or not missing else 1


if __name__ == "__main__":
    raise SystemExit(main())
