# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Build an original 68000 AmigaDOS hunk executable without a cross compiler.

RTCCheck uses the ROM's battclock.resource, restores the original clock, and
prints RTC PASS / RTC FAIL / NO CLOCK. No AmigaOS or third-party code is included.
Usage: python build_rtc_check.py <output-file>
"""
import datetime
import pathlib
import struct
import sys

code = bytearray()
labels = {}
fixups = []


def word(value):
    code.extend(struct.pack(">H", value & 0xFFFF))


def long(value):
    code.extend(struct.pack(">I", value))


def label(name):
    labels[name] = len(code)


def relative(opcode, target):
    word(opcode)
    fixups.append((len(code), target))
    word(0)


def call(offset):
    word(0x4EAE)  # JSR displacement(A6)
    word(offset)


seconds = int((datetime.datetime(2024, 2, 29, 12, 34, 56) - datetime.datetime(1978, 1, 1)).total_seconds())
word(0x48E7); word(0x3F3E)  # MOVEM.L D2-D7/A2-A6,-(SP)
word(0x7C02)  # MOVEQ #2,D6: no clock until proven present
word(0x2C78); word(4)  # MOVEA.L 4.W,A6 (ExecBase)
relative(0x43FA, "resource")  # LEA resource(PC),A1
call(-498)  # Exec OpenResource(A1)
word(0x4A80); relative(0x6700, "print")  # TST.L D0 / BEQ
word(0x2C40)  # MOVEA.L D0,A6
call(-12)  # ReadBattClock()
word(0x2E00)  # MOVE.L D0,D7 (original date)
word(0x7C00)  # MOVEQ #0,D6: fail until readback succeeds
word(0x203C); long(seconds)  # MOVE.L #test_date,D0
call(-18)  # WriteBattClock(D0)
call(-12)
word(0x0480); long(seconds)  # SUBI.L #test_date,D0
word(0x0C80); long(1)  # CMPI.L #1,D0: allow a second boundary
relative(0x6200, "restore")  # BHI failure, including unsigned underflow
word(0x7C01)  # MOVEQ #1,D6: success
label("restore")
word(0x2007); call(-18)  # Restore the original date on both success and failure
label("print")
relative(0x45FA, "fail")  # LEA fail(PC),A2
word(0x0C86); long(1); relative(0x6600, "no_clock")  # D6 == 1?
relative(0x45FA, "pass"); relative(0x6000, "dos")
label("no_clock")
word(0x0C86); long(2); relative(0x6600, "dos")
relative(0x45FA, "skip")
label("dos")
word(0x2C78); word(4)
relative(0x43FA, "dos_name"); word(0x7000); call(-552)  # OpenLibrary("dos.library",0)
word(0x4A80); relative(0x6700, "end")
word(0x2840); word(0x2C40)  # Preserve DOS base in A4, set A6
call(-60)  # Output()
word(0x2200); word(0x240A); word(0x7609); call(-48)  # Write(D1 handle,D2 text,D3 size)
word(0x224C); word(0x2C78); word(4); call(-414)  # CloseLibrary(A1)
label("end")
word(0x7000); word(0x4CDF); word(0x7CFC); word(0x4E75)  # D0=0, restore registers, RTS
for name, value in (("resource", b"battclock.resource\0"), ("dos_name", b"dos.library\0"),
                    ("pass", b"RTC PASS\n"), ("fail", b"RTC FAIL\n"), ("skip", b"NO CLOCK\n")):
    label(name)
    code.extend(value)
for offset, target in fixups:
    struct.pack_into(">h", code, offset, labels[target] - offset)
code.extend(bytes((-len(code)) % 4))
size = len(code) // 4
header = struct.pack(">8I", 1011, 0, 1, 0, 0, size, 1001, size)
path = pathlib.Path(sys.argv[1])
path.parent.mkdir(parents=True, exist_ok=True)
with path.open("xb") as output:
    output.write(header + code + struct.pack(">I", 1010))
print(f"{path}: {size * 4} bytes of original 68000 code/data, test date {seconds}")
