# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Create a separate 020/030-compatible copy of the reviewed Mega lo Mania Disk 1.

The packed mlm program saves user-mode SR with MOVE SR, which is privileged on
020/030. Its saved user status has no upper bits; MOVE CCR preserves that value.
One unique IMP literal changes 40F8 to 42F8 at guest $5E84. The packed checksum is
recalculated. No ROMs or games are supplied. This copy is unsuitable for a 68000,
where MOVE CCR is unavailable; use the original disk on that CPU.
"""

import argparse
import hashlib
import json
import pathlib
import struct

SOURCE_SHA256 = "96b851ffcd62710d53ccb356407f813dbe0458dd58a9f57e19533c39b67ceaa5"
PATCHED_SHA256 = "c296c827b34a8861489ec096b424956bfa2be3fae67eb94e0bc8a0b3ccca6814"
FILE_OFFSET = 0xADE00
LITERAL_OFFSET = 0xAEB99
PACKED_END = 0x11E54


def packed_checksum(image: bytes) -> int:
    end = FILE_OFFSET + PACKED_END + 46
    payload = image[FILE_OFFSET:end]
    return (7 + sum(struct.unpack(f">{len(payload) // 2}H", payload))) & 0xFFFFFFFF


def prepare(source: pathlib.Path, output: pathlib.Path) -> dict:
    original = source.read_bytes()
    if len(original) != 901120 or hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise ValueError("Unsupported Disk 1: the exact reviewed Mega lo Mania image is required.")
    directory = original[0x880:0x8A0]
    if (directory[:4] != b"mlm\0" or
            struct.unpack_from(">I", directory, 16)[0] != 0x24A2A or
            struct.unpack_from(">HBB", directory, 24) != (0x4000, 126, 5)):
        raise ValueError("Custom mlm directory entry mismatch.")
    if (original[FILE_OFFSET:FILE_OFFSET + 4] != b"IMP!" or
            struct.unpack_from(">II", original, FILE_OFFSET + 4) != (0x25000, PACKED_END) or
            original[LITERAL_OFFSET] != 0x40):
        raise ValueError("Reviewed packed mlm signature/literal mismatch.")
    checksum_offset = FILE_OFFSET + PACKED_END + 46
    if struct.unpack_from(">I", original, checksum_offset)[0] != packed_checksum(original):
        raise ValueError("Invalid source IMP checksum.")
    patched = bytearray(original)
    patched[LITERAL_OFFSET] = 0x42
    struct.pack_into(">I", patched, checksum_offset, packed_checksum(patched))
    if hashlib.sha256(patched).hexdigest() != PATCHED_SHA256:
        raise ValueError("Patched image does not match the reviewed result.")
    # Exclusive creation also rejects the source path and existing user media.
    with output.open("xb") as destination:
        destination.write(patched)
    return dict(source=str(source.resolve()), output=str(output.resolve()),
                sourceSha256=SOURCE_SHA256, patchedSha256=PATCHED_SHA256,
                file="mlm", literalOffset=LITERAL_OFFSET, checksumOffset=checksum_offset,
                guestAddress="5e84", old="40f85e2c", new="42f85e2c",
                processors=["68020", "68EC020", "68030", "68EC030"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path, help="New ADF path; parent must exist.")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.source, args.output), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f"{error}\n")
