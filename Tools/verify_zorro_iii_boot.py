#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Check real Kickstart/Exec Zorro III boot evidence; images still need manual review."""
import argparse
import json
from pathlib import Path


def covered(lower, upper, ranges):
    cursor = lower
    for start, end in sorted(ranges):
        if start > cursor:
            break
        if end > cursor:
            cursor = end
        if cursor >= upper:
            return True
    return False


def verify(root):
    checked = 0
    for path in sorted(root.rglob("steps.jsonl")):
        last = None
        for line in path.open(encoding="utf-8"):
            row = json.loads(line)
            if row.get("exceptionObservations", {}).get("nonRomExceptions", 0):
                raise ValueError(f"{path.parent.name}: guest exception observed")
            last = row
        cards = last.get("zorroIIICards", []) if last else []
        if not cards:
            continue
        if any(not c["configured"] or not c["address"] or c["assignments"] != 1 for c in cards):
            raise ValueError(f"{path.parent.name}: not every card is assigned exactly once")
        physical = [(c["address"], c["address"] + c["size"]) for c in cards]
        regions = [(m["Lower"], m["Upper"]) for m in last.get("zorroIIIGuestMemory", []) or []
                   if 0x10000000 <= m["Lower"] < 0x80000000]
        if not regions:
            raise ValueError(f"{path.parent.name}: no Exec Zorro III memory headers")
        for lower, upper in regions:
            if lower >= upper or not covered(lower, upper, physical):
                raise ValueError(f"{path.parent.name}: Exec links unmapped RAM {lower:08X}..{upper:08X}")
        for lower, upper in physical:
            # A classic Exec MemHeader can occupy the first 32 bytes of a region.
            if not covered(lower + 32, upper, regions):
                raise ValueError(f"{path.parent.name}: assigned card is absent from the usable Exec pool")
        checked += 1
        print(f"OK {path.parent.name}: {len(cards)} cards, {sum(c['size'] for c in cards) // 1048576} MiB physical RAM, no phantom pool bytes")
    if not checked:
        raise ValueError("No complete Zorro III boot evidence found")
    return checked


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        print(f"Verified {verify(args.output)} native boot cases; visual review remains separate.")
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"FAIL {error}\n")
