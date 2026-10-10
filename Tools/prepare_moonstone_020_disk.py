# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Create a separate 020/030-compatible copy of the reviewed local CSL Disk 1.

The loader installs an illegal-instruction handler to enter Supervisor mode,
then executes user-mode MOVEC. On 020/030 MOVEC takes privilege vector 8 instead.
Use ILLEGAL followed by NOP to enter the installed vector 4 on each CPU, without
changing the emulated hardware. This is specific to the exact reviewed image.
No ROMs or game images are supplied by this tool.
"""

import argparse
import hashlib
import json
import pathlib
import struct

SOURCE_SHA256 = "a5742db6ee76af830f374421d41ba6ed234486c07bac3b6c50a6845cfc13ca52"
PATCHED_SHA256 = "fba2aa85a63922b4549e0f7d2883e2df6571a9f3ebab21448d5de2767d056665"
SIGNATURE = bytes.fromhex("487A001821DF0010204F4E7A0002")
OFFSET = 692206


def prepare(source: pathlib.Path, output: pathlib.Path) -> dict:
    original = source.read_bytes()
    if len(original) != 901120 or hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise ValueError("Unsupported Disk 1: the exact reviewed CSL image is required.")
    if original.count(SIGNATURE) != 1 or original.find(SIGNATURE) + 10 != OFFSET:
        raise ValueError("Loader signature/position mismatch.")
    block_start = OFFSET // 512 * 512
    words = struct.unpack_from(">128I", original, block_start)
    if words[0] != 8 or sum(words) & 0xFFFFFFFF:
        raise ValueError("Invalid source OFS data block.")
    if not 24 <= OFFSET - block_start <= 24 + words[3] - 4:
        raise ValueError("Patch is outside the OFS payload.")
    owner = words[1]
    if not 0 < owner < len(original) // 512:
        raise ValueError("Invalid OFS file-header owner.")
    header = original[owner * 512 : (owner + 1) * 512]
    hw = struct.unpack(">128I", header)
    if hw[0] != 2 or hw[1] != owner or hw[127] != 0xFFFFFFFD or sum(hw) & 0xFFFFFFFF:
        raise ValueError("Invalid owning OFS file header.")
    if header[433 : 433 + header[432]] != b"mog":
        raise ValueError("Patch does not belong to the reviewed mog loader.")
    patched = bytearray(original)
    patched[OFFSET : OFFSET + 4] = bytes.fromhex("4AFC4E71")
    patched[block_start + 20 : block_start + 24] = bytes(4)
    checksum = -sum(struct.unpack_from(">128I", patched, block_start)) & 0xFFFFFFFF
    struct.pack_into(">I", patched, block_start + 20, checksum)
    if sum(struct.unpack_from(">128I", patched, block_start)) & 0xFFFFFFFF:
        raise ValueError("Patched OFS checksum mismatch.")
    if hashlib.sha256(patched).hexdigest() != PATCHED_SHA256:
        raise ValueError("Patched image does not match the reviewed result.")
    # Exclusive creation rejects existing destinations, including the source.
    with output.open("xb") as destination:
        destination.write(patched)
    return dict(source=str(source.resolve()), output=str(output.resolve()),
                sourceSha256=SOURCE_SHA256, patchedSha256=PATCHED_SHA256,
                file="mog", sector=block_start // 512, offset=OFFSET,
                old="4e7a0002", new="4afc4e71")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path, help="New ADF path; parent must exist.")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.source, args.output), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")
