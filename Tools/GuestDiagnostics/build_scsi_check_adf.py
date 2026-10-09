"""Build an original, bootable A3000 SCSI/DMA diagnostic ADF.

No ROM or OS content is included. The boot block stops the OS, reads sector
zero from SCSI target 0 through Super DMAC into Chip RAM and checks RDSK.
COLOR00: blue=running, green=pass, red=fail. Use an RDB HDF mounted read-only.
Usage: python build_scsi_check_adf.py <new-output.adf>
"""
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


def branch(opcode, name):
    word(opcode)
    fixups.append((len(code), name))
    word(0)


def move(size, value, address):
    word({1: 0x13FC, 2: 0x33FC, 4: 0x23FC}[size])
    (long if size == 4 else word)(value)
    long(address)


def register(index, values):
    move(1, index, 0xDD0041)
    for value in values:
        move(1, value, 0xDD0043)


word(0x2C78); word(4)  # MOVEA.L ExecBase,A6. Boot blocks enter in user mode.
branch(0x4BFA, "supervisor")  # LEA supervisor(PC),A5.
word(0x4EAE); word(-30)  # Exec Supervisor(A5); diagnostic never returns.
label("supervisor")
word(0x46FC); word(0x2700)  # MOVE #$2700,SR: no interrupt handler dependencies.
move(2, 0x7FFF, 0xDFF096)  # Disable custom-chip DMA.
move(2, 0x7FFF, 0xDFF09A)
move(2, 0, 0xDFF100)  # No bitplanes; the border color displays the result.
move(2, 0x00F, 0xDFF180)
move(2, 4, 0xDD000A)  # DMA direction SCSI -> memory; interrupt gate enabled.
move(4, 0x10000, 0xDD000C)
move(2, 0, 0xDD0012)  # ST_DMA strobe.
register(3, [8, 0, 0, 0, 1, 0])  # READ(6), one sector at LBA zero.
register(0x0F, [0, 0])  # LUN and command phase.
register(0x12, [0, 2, 0])  # Transfer count 512.
register(0x15, [0])  # Target zero.
register(0x18, [9])  # SELECT AND TRANSFER.
move(1, 0x17, 0xDD0041)  # Read status on completion.
word(0x203C); long(0x100000)  # Bounded polling timeout.
label("poll")
word(0x0839); word(7); long(0xDD0041)  # BTST #7,ASR.
branch(0x6600, "done")
word(0x5380)  # SUBQ.L #1,D0.
branch(0x6600, "poll")
branch(0x6000, "fail")
label("done")
word(0x0C39); word(0x16); long(0xDD0043)  # CMP.B #combined completion,status.
branch(0x6600, "fail")
word(0x0CB9); long(0x5244534B); long(0x10000)  # CMPI.L #RDSK,DMA buffer.
branch(0x6600, "fail")
move(2, 0x0F0, 0xDFF180)
branch(0x6000, "halt")
label("fail")
move(2, 0xF00, 0xDFF180)
label("halt")
branch(0x6000, "halt")
for offset, name in fixups:
    struct.pack_into(">h", code, offset, labels[name] - offset)
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
path = pathlib.Path(sys.argv[1])
path.parent.mkdir(parents=True, exist_ok=True)
with path.open("xb") as output:
    output.write(boot)
    output.write(bytes(880 * 1024 - len(boot)))
print(f"{path}: {len(code)} bytes of original diagnostic code")
