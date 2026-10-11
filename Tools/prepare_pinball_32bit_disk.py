# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Create a separate 32-bit-clean Disk 1 for the exact reviewed Pinball set.

The PDII loader uses bit 31 of a block's destination as a relative-address
flag. It clears the flag in the descriptor but keeps it in the transfer
pointer. That works on 24-bit CPUs and writes outside RAM on full 020/030 CPUs.
Reorder the existing block setup to strip the flag before adding the relative
base and publishing the transfer pointer. The descriptor, decompression flag
branch, length and reader entry retain their original semantics.

Only this loader is modified. No game, ROM or OS data is distributed; other
releases are rejected. The original disk and any existing output are preserved.

Optional --repair-scores also makes the writer encode the requested physical
track instead of a scratch D2 value. This keeps its scores representable in a
standard ADF; raw tracks with deliberately mismatched headers remain unsupported.
"""

import argparse
import hashlib
import json
import pathlib

SOURCE_SHA256 = "b186b41c3ffdc245743995fac86f4aa3e7476658607be6946c7b5c1a3e2a1476"
PATCHED_SHA256 = "f4e0eacdd4908efc71dbd26625388c503134529b0adc5d6be86319e6cbccba28"
SCORES_SHA256 = "74307adc5f44748e078ab7782628de0482c1e45dda1781e5a7327a53779e23b2"
OFFSET = 0x5914
ORIGINAL = bytes.fromhex(
    "2169001EFFE408A90007001E67082229002AD3A8FFE4"
    "2369002200164268FFF44EF900002B8A"
)
# Guest $2A5A..$2A7F. D1 is already scratch in the reader; MOVE/CLR instructions
# after the flag branch replace its condition codes before any caller uses them.
# The final absolute-word JMP saves two bytes without moving code or tables.
REPLACEMENT = bytes.fromhex(
    "2229001E08A90007001E67080881001FD2A9002A2141FFE4"
    "2369002200164268FFF44EF82B8A"
)
# Guest $2D92..$2D97: MOVEQ #-1,D3; MOVE.B -7(A0),D3 replaces
# MOVE.W #$FF00,D3; OR.W D2,D3. The following SWAP/sector/count writes
# produce FF TT SS SG using the descriptor's requested track. $2F8A clears
# D2 while changing write protection; D2 is not a reliable track number.
SCORE_OFFSET = 0x5C4C
SCORE_ORIGINAL = bytes.fromhex("363CFF008642")
SCORE_REPLACEMENT = bytes.fromhex("76FF1628FFF9")


def prepare(source: pathlib.Path, output: pathlib.Path, repair_scores: bool = False) -> dict:
    original = source.read_bytes()
    if len(original) != 901120 or hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise ValueError("Unsupported Disk 1: the exact reviewed PDII image is required.")
    if len(ORIGINAL) != len(REPLACEMENT) or original[OFFSET:OFFSET + len(ORIGINAL)] != ORIGINAL:
        raise ValueError("Loader signature/position mismatch.")
    patched = bytearray(original)
    patched[OFFSET:OFFSET + len(ORIGINAL)] = REPLACEMENT
    expected_hash = PATCHED_SHA256
    if repair_scores:
        if original[SCORE_OFFSET:SCORE_OFFSET + len(SCORE_ORIGINAL)] != SCORE_ORIGINAL:
            raise ValueError("Score-writer signature/position mismatch.")
        patched[SCORE_OFFSET:SCORE_OFFSET + len(SCORE_REPLACEMENT)] = SCORE_REPLACEMENT
        expected_hash = SCORES_SHA256
    if hashlib.sha256(patched).hexdigest() != expected_hash:
        raise ValueError("Patched image does not match the reviewed result.")
    # This is a raw custom-loader payload outside the bootblock, not an OFS
    # file. ADF-to-MFM encoding regenerates each affected sector's checksum.
    with output.open("xb") as destination:
        destination.write(patched)
    report = dict(source=str(source.resolve()), output=str(output.resolve()),
                  sourceSha256=SOURCE_SHA256, patchedSha256=expected_hash,
                  repairScores=repair_scores,
                  offset=OFFSET, guestAddress="2A5A", old=ORIGINAL.hex(), new=REPLACEMENT.hex())
    if repair_scores:
        report["scorePatch"] = dict(offset=SCORE_OFFSET, guestAddress="2D92",
                                   old=SCORE_ORIGINAL.hex(), new=SCORE_REPLACEMENT.hex())
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path, help="New ADF path; parent must exist.")
    parser.add_argument("--repair-scores", action="store_true", help="Also repair the standard-ADF score writer on the new copy.")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.source, args.output, args.repair_scores), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")
