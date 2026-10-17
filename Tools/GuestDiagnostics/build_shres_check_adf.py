# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Build an original bootable ECS/AGA SuperHires guest diagnostic ADF.

The boot block enters Supervisor mode, programs two bitplanes and parks the
CPU. It contains no ROM, OS or commercial media. A new output path is required.
Usage: python build_shres_check_adf.py <new-output.adf> [--lace|--mixed-lace]
"""
import pathlib
import struct
import sys


def build_boot_block(lace=False, mixed_lace=False):
    code = bytearray()

    def word(value):
        code.extend(struct.pack(">H", value & 0xFFFF))

    def long(value):
        code.extend(struct.pack(">I", value & 0xFFFFFFFF))

    def move_word(value, address):
        word(0x33FC)  # MOVE.W #value,(absolute long)
        word(value)
        long(address)

    word(0x2C78); word(4)  # MOVEA.L 4.W,A6 (ExecBase)
    word(0x4BFA); word(6)  # LEA supervisor(PC),A5
    word(0x4EAE); word(-30)  # Exec Supervisor(A5)
    word(0x46FC); word(0x2700)  # supervisor: mask interrupts
    move_word(0x7FFF, 0xDFF096)  # Disable DMA during setup.
    move_word(0x7FFF, 0xDFF09A)
    if mixed_lace:
        # Fill enough chip memory for every PAL fetch. Alternating plane
        # bits expose the different horizontal sample rates on both sides
        # of the Copper's mid-frame resolution change.
        for address, pattern in ((0x100000, 0xAAAA), (0x110000, 0x5555)):
            word(0x41F9); long(address)  # LEA (absolute long),A0
            word(0x303C); word(pattern)  # MOVE.W #pattern,D0
            word(0x323C); word(0x3FFF)  # MOVE.W #16383,D1
            loop = len(code)
            word(0x30C0)  # MOVE.W D0,(A0)+
            word(0x51C9); word(loop - len(code))  # DBRA D1,loop
    else:
        move_word(0x8000, 0x10000)  # BPL1 first word: native sample zero.
        move_word(0x4000, 0x11000)  # BPL2 first word: native sample one.
    for offset, value in (
        (0x0E0, 0x10 if mixed_lace else 1), (0x0E2, 0),
        (0x0E4, 0x11 if mixed_lace else 1),
        (0x0E6, 0 if mixed_lace else 0x1000),
        (0x108, 0), (0x10A, 0),
        (0x08E, 0x2C81), (0x090, 0xFFC1 if mixed_lace else 0x2DC1),
        (0x092, 0x003C), (0x094, 0x00D4),
        (0x180, 0x0000 if mixed_lace else 0x0F00),
        (0x182, 0x0F00), (0x184, 0x000F),
        (0x192, 0x0C36),  # ECS SHRES packed-color fixture.
        (0x100, 0x2045 if lace else 0x2041),
    ):
        move_word(value, 0xDFF000 + offset)
    if mixed_lace:
        # Restarting Copper list: lores on the upper scanlines and SHRES
        # from PAL raster $80 onward. The final BPLCON0 stays in SHRES so
        # the native presentation path must retain both halves of the field.
        copper_words = (
            0x00E0, 0x0010,  # Reset BPL1 pointer each field.
            0x00E2, 0x0000,
            0x00E4, 0x0011,  # Reset BPL2 pointer each field.
            0x00E6, 0x0000,
            0x0180, 0x0F00,  # Red upper border.
            0x0100, 0x2005,  # MOVE BPLCON0, two-plane lores + LACE
            0x8001, 0xFFFE,  # WAIT for raster $80, horizontal position 0
            0x0180, 0x000F,  # Blue lower border.
            0x0100, 0x2045,  # MOVE BPLCON0, two-plane SHRES + LACE
            0xFFFF, 0xFFFE,  # End of list
        )
        for index, value in enumerate(copper_words):
            move_word(value, 0x12000 + 2 * index)
        move_word(0x0001, 0xDFF080)  # COP1LCH = $0001
        move_word(0x2000, 0xDFF082)  # COP1LCL = $2000
        move_word(0x0000, 0xDFF088)  # COPJMP1
    move_word(0x8380 if mixed_lace else 0x8300, 0xDFF096)
    # Master, bitplane, and (for mixed mode) Copper DMA.
    if lace and not mixed_lace:
        # Poll the hardware LOF flop. Different COLOR00 values expose the
        # two completed fields to the host interlace capture path.
        loop = len(code)
        word(0x3039); long(0xDFF004)  # MOVE.W VPOSR,D0
        word(0x0240); word(0x8000)  # ANDI.W #$8000,D0
        branch = len(code); word(0x6700)  # BEQ.S short_field
        move_word(0x0F00, 0xDFF180)  # Long-field red border.
        displacement = loop - (len(code) + 2)
        if not -128 <= displacement <= 127:
            raise ValueError("Long-field loop branch is out of range")
        word(0x6000 | (displacement & 0xFF))
        code[branch + 1] = len(code) - (branch + 2)
        move_word(0x000F, 0xDFF180)  # Short-field blue border.
        displacement = loop - (len(code) + 2)
        if not -128 <= displacement <= 127:
            raise ValueError("Short-field loop branch is out of range")
        word(0x6000 | (displacement & 0xFF))
    else:
        word(0x60FE)  # BRA.S *; leave the programmed display running.
    if len(code) > 1012:
        raise ValueError("Diagnostic does not fit the boot block")

    boot = bytearray(1024)
    struct.pack_into(">3I", boot, 0, 0x444F5300, 0, 880)
    boot[12:12 + len(code)] = code
    checksum = 0
    for value in struct.unpack(">256I", boot):
        checksum += value
        checksum = (checksum & 0xFFFFFFFF) + (checksum >> 32)
    struct.pack_into(">I", boot, 4, checksum ^ 0xFFFFFFFF)
    return boot, len(code)


def main():
    if len(sys.argv) not in (2, 3) or (len(sys.argv) == 3 and sys.argv[2] not in ("--lace", "--mixed-lace")):
        raise SystemExit("Usage: build_shres_check_adf.py <new-output.adf> [--lace|--mixed-lace]")
    output_path = pathlib.Path(sys.argv[1])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    mode = sys.argv[2] if len(sys.argv) == 3 else None
    boot, code_size = build_boot_block(lace=mode is not None, mixed_lace=mode == "--mixed-lace")
    with output_path.open("xb") as output:
        output.write(boot)
        output.write(bytes(880 * 1024 - len(boot)))
    print(f"{output_path}: {code_size} bytes of original SHRES diagnostic code")


if __name__ == "__main__":
    main()
