#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Create a repaired copy of the reviewed PDX Siedler Intro Disk 1.

The original OFS bitmap marks the occupied zero-length .info header (block 879)
as free and its root is not validated. Kickstart 1.3 starts an asynchronous
Validator that can reply after the intro has changed disks. This tool repairs
only filesystem allocation metadata, preserving all boot and game code.
No ROMs or game images are supplied; the original is never overwritten.
"""

import argparse
import hashlib
import json
import pathlib
import struct


SOURCE_SHA256 = "ce24f8af192683c6b143bb9c4d6d3e3da707dc79541da99398e62ad11fe38de0"
REPAIRED_SHA256 = "5810ceafc46757a7d39b3693fb05afdaf041621e5f1463fa618aac0c21dce375"
ROOT_BLOCK = 880
BITMAP_BLOCK = 881


def words(data, block):
    return struct.unpack_from(">128I", data, block * 512)


def checksum(data, block, index):
    struct.pack_into(">I", data, block * 512 + index * 4, 0)
    struct.pack_into(">I", data, block * 512 + index * 4, -sum(words(data, block)) & 0xFFFFFFFF)
    if sum(words(data, block)) & 0xFFFFFFFF:
        raise ValueError("Repaired block checksum mismatch.")


def prepare(source: pathlib.Path, output: pathlib.Path) -> dict:
    if source.stat().st_size != 901120:
        raise ValueError("Unsupported Disk 1: an 880-KiB ADF is required.")
    original = source.read_bytes()
    if len(original) != 901120 or hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise ValueError("Unsupported Disk 1: the exact reviewed PDX Intro image is required.")
    root, bitmap, info = (words(original, block) for block in (ROOT_BLOCK, BITMAP_BLOCK, 879))
    if any(sum(block) & 0xFFFFFFFF for block in (root, bitmap, info)):
        raise ValueError("Source filesystem checksum mismatch.")
    if (root[0], root[3], root[78], root[79], root[127]) != (2, 72, 0, BITMAP_BLOCK, 1):
        raise ValueError("Unexpected root block or bitmap pointer.")
    header = original[879 * 512 : 880 * 512]
    if (info[0], info[1], info[4], info[81], info[125], info[126], info[127]) != (
        2, 879, 0, 0, ROOT_BLOCK, 0, 0xFFFFFFFD
    ) or header[432:438] != b"\x05.info":
        raise ValueError("The occupied .info file header does not match.")
    # Bitmap bit 13 of data word 27 represents block 2 + 27*32 + 13 = 879.
    if bitmap[28] != 0x2000:
        raise ValueError("Unexpected bitmap allocation; refusing an unreviewed repair.")
    repaired = bytearray(original)
    struct.pack_into(">I", repaired, ROOT_BLOCK * 512 + 78 * 4, 0xFFFFFFFF)
    struct.pack_into(">I", repaired, BITMAP_BLOCK * 512 + 28 * 4, 0)
    checksum(repaired, ROOT_BLOCK, 5)
    checksum(repaired, BITMAP_BLOCK, 0)
    if hashlib.sha256(repaired).hexdigest() != REPAIRED_SHA256:
        raise ValueError("Repaired image does not match the reviewed result.")
    with output.open("xb") as destination:
        destination.write(repaired)
    return dict(source=str(source.resolve()), output=str(output.resolve()),
                sourceSha256=SOURCE_SHA256, repairedSha256=REPAIRED_SHA256,
                changedBlocks=[ROOT_BLOCK, BITMAP_BLOCK], allocatedFileHeader=879)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path, help="New ADF path; parent must exist.")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.source, args.output), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")
