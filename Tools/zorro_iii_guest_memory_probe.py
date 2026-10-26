#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Generate a synthetic DF0 Exec memory test; verify guest-written reports.

No ROM, user disk or installed filesystem is modified. The native Kickstart
boot task enters our checksum-valid bootblock with SysBase in A6. All test
storage is obtained through AllocAbs and returned through FreeMem. The
bootblock's own allocated buffer holds its report. It spins after completion;
this is a bounded diagnostic, not an installed application or a DOS boot.

Sources: https://amigadev.elowar.com/read/ADCD_2.1/Devices_Manual_guide/node007D.html
https://developer.amigaos3.net/autodocs/exec.library/AllocAbs.html
https://developer.amigaos3.net/autodocs/exec.library/FreeMem.html
ABI: Commodore RKM Devices / Bootblock Booting; exec AllocAbs D0/A1,
FreeMem A1/D0, AvailMem D1. LVOs -204, -210, -216 (classic exec_lib.fd).
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

MAGIC = b"Z3EXECMEMPROBE1!"
BLOCK_SIZE = 4096
REPORT_SIZE = 64


class Code:
    """Only the explicitly documented 68000 encodings used by this probe."""
    def __init__(self):
        self.data = bytearray(b"DOS\0" + bytes(8))
        self.labels = {}
        self.fixups = []

    def words(self, *values):
        for value in values:
            self.data += struct.pack(">H", value & 0xffff)

    def long(self, value):
        self.data += struct.pack(">I", value & 0xffffffff)

    def label(self, name):
        self.labels[name] = len(self.data)

    def relative(self, opcode, name):
        self.words(opcode, 0)
        self.fixups.append((len(self.data) - 2, name))

    def immediate(self, opcode, value):
        self.words(opcode)
        self.long(value)

    def resolve(self):
        for offset, name in self.fixups:
            delta = self.labels[name] - offset
            if not -32768 <= delta <= 32767:
                raise ValueError("PC-relative target out of range")
            struct.pack_into(">h", self.data, offset, delta)


def build(sizes, corrupt_after_write=False):
    if not 1 <= len(sizes) <= 4 or any(s not in (8, 16, 32) for s in sizes):
        raise ValueError("Require one to four 8/16/32 MiB development cards")
    # Kickstart's current Zorro III autoconfig placement, independently checked
    # against each native card/Exec MemHeader in the captured runner evidence.
    bases, cursor = [], 0x40000000
    for size in sizes:
        aperture = max(16, size) * 1048576
        cursor = (cursor + aperture - 1) & -aperture
        bases.append(cursor)
        cursor += aperture
    addresses = [a for base, size in zip(bases, sizes)
                 for a in (base + 0x10000, base + size * 1048576 - 0x2000)]
    c = Code()
    c.relative(0x4bfa, "report")        # lea report(pc),a5
    c.words(0x2b4e, 44)                 # move.l a6,44(a5)
    c.words(0x2b7c); c.long(1); c.words(20)  # status=running
    c.words(0x7204, 0x4eae, -216)        # AvailMem(MEMF_FAST)
    c.words(0x2b40, 24)                 # before
    c.words(0x7c00)                     # moveq #0,d6 (pass)
    c.label("allocate")
    c.relative(0x49fa, "table")         # lea table(pc),a4
    c.words(0x7e00 | (len(addresses) - 1))
    c.label("alloc_loop")
    c.words(0x2254)                     # movea.l (a4),a1
    c.immediate(0x203c, BLOCK_SIZE)      # move.l #size,d0
    c.words(0x4eae, -204)               # jsr AllocAbs(a6)
    c.words(0xb094)                     # cmp.l (a4),d0; AllocAbs may clobber A1
    c.relative(0x6600, "failed_alloc")
    c.words(0x2940, 4, 0xd8fc, 16)      # save allocation; next entry
    c.relative(0x51cf, "alloc_loop")    # dbf d7
    c.relative(0x49fa, "table")
    c.words(0x7e00 | (len(addresses) - 1))
    c.label("write_block")
    c.words(0x2054, 0x2414)             # movea.l (a4),a0; move.l (a4),d2
    c.immediate(0x0a82, 0xa5c35a3c)     # eori.l #address seed,d2
    c.words(0xbd82, 0x363c, 1023)       # eor.l d6,d2; move.w #1023,d3
    c.label("write_word")
    c.words(0x20c2, 0x5282)             # move.l d2,(a0)+; addq.l #1,d2
    c.relative(0x51cb, "write_word")
    c.immediate(0x06ac, 1024); c.words(8)  # addi.l #1024,8(a4)
    c.words(0xd8fc, 16)
    c.relative(0x51cf, "write_block")
    if corrupt_after_write:
        # Deliberate guest-side corruption: the verifier must report BAD00002.
        c.immediate(0x23fc, 0x12345678)  # move.l #value,(absolute).l
        c.long(addresses[0])
    c.relative(0x49fa, "table")
    c.words(0x7e00 | (len(addresses) - 1))
    c.label("verify_block")
    c.words(0x2054, 0x2414)
    c.immediate(0x0a82, 0xa5c35a3c)
    c.words(0xbd82, 0x363c, 1023)
    c.label("verify_word")
    c.words(0xb498)                     # cmp.l (a0)+,d2
    c.relative(0x6600, "failed_data")
    c.words(0x5282)
    c.relative(0x51cb, "verify_word")
    c.immediate(0x06ac, 1024); c.words(12)
    c.words(0xd8fc, 16)
    c.relative(0x51cf, "verify_block")
    c.relative(0x49fa, "table")
    c.words(0x7e00 | (len(addresses) - 1))
    c.label("free_loop")
    c.words(0x226c, 4)                  # movea.l 4(a4),a1
    c.immediate(0x203c, BLOCK_SIZE)
    c.words(0x4eae, -210, 0x52ad, 32)    # FreeMem; addq.l #1,32(a5)
    c.words(0xd8fc, 16)
    c.relative(0x51cf, "free_loop")
    c.words(0x7204, 0x4eae, -216, 0x2b40, 28)
    c.words(0xb0ad, 24)                 # cmp.l 24(a5),d0
    c.relative(0x6600, "failed_free")
    c.words(0x4a86)                     # tst.l d6
    c.relative(0x6600, "holes")
    c.words(0x7c01)                     # pass 1 reuses every freed location
    c.relative(0x6000, "allocate")
    c.label("holes")
    for base, size in zip(bases, sizes):
        if size == 8:
            # An 8 MiB card occupies a 16 MiB autoconfig aperture. Its upper
            # half is absent: Exec must not allocate from this unbacked gap.
            c.immediate(0x227c, base + 0x810000)  # movea.l #hole,a1
            c.immediate(0x203c, BLOCK_SIZE)
            c.words(0x4eae, -204, 0x4a80)        # AllocAbs; tst.l d0
            c.relative(0x6600, "failed_hole")
            c.words(0x52ad, 48)                 # report rejected-hole count
    c.relative(0x6000, "success")
    for name, status in [("success", 0x600d600d), ("failed_alloc", 0xbad00001),
                         ("failed_data", 0xbad00002), ("failed_free", 0xbad00003),
                         ("failed_hole", 0xbad00004)]:
        c.label(name)
        c.words(0x2b7c); c.long(status); c.words(20)
        c.relative(0x6000, "halt")
    c.label("halt")
    c.words(0x60fe)                     # bra.s * (interrupts still serviceable)
    c.label("report")
    assert len(MAGIC) == 16
    c.data += MAGIC
    fields = [2, 0, 0, 0, 0, len(addresses), BLOCK_SIZE, 0, 0, 0, 0, 0]
    for value in fields:
        c.long(value)
    c.label("table")
    for address in addresses:
        for value in (address, 0, 0, 0):
            c.long(value)
    c.resolve()
    if len(c.data) > 1024:
        raise ValueError("Bootblock exceeds 1024 bytes")
    c.data += bytes(1024 - len(c.data))
    total = 0
    for value in struct.unpack(">256I", c.data):
        total += value
        total = (total & 0xffffffff) + (total >> 32)
    struct.pack_into(">I", c.data, 4, total ^ 0xffffffff)
    image = bytes(c.data) + bytes(901120 - 1024)
    return image, addresses, c.labels


def generate(directory, corrupt_after_write=False):
    directory.mkdir(parents=True, exist_ok=False)
    cases, metadata = [], []
    for model, sizes, rom in [("A3000", [8, 16, 32, 8], "A3000"),
                              ("A4000Ec030", [8, 16], "A4000")]:
        image, addresses, labels = build(sizes, corrupt_after_write)
        disk = directory / (model + "-exec-memory.adf")
        disk.write_bytes(image)
        actions = [{"type": "wait", "cycles": 60000000, "label": "guest-probe-settle"}]
        # The boot task chooses its own buffer. Read-only snapshots locate the
        # guest-written report in Chip or motherboard Fast RAM, never I/O.
        for lower, upper in [(0, 0x200000), (0x07c00000, 0x08000000)]:
            for start in range(lower, upper, 65536):
                actions.append({"type": "memory-snapshot", "cycles": 0,
                                "memoryStart": start, "memoryLength": 65536,
                                "label": f"report-scan-{start:08x}"})
        cases.append({"name": model + "-ZorroIII-Exec-guest-memory", "model": model,
                      "ram": "Chip2MbFast4Mb", "mediaRoot": str(directory.resolve()),
                      "boot": disk.name, "kickstartRom": "%APPDATA%/AmigaEmulator/roms/KICK31-" + rom + ".ROM",
                      "strict": True, "traceExceptions": True, "maximumNonRomExceptions": 0,
                      "zorroIIIRamBoardsMiB": sizes, "actions": actions})
        metadata.append({"name": cases[-1]["name"], "sizesMiB": sizes,
                         "addresses": addresses, "labels": labels,
                         "imageSha256": hashlib.sha256(image).hexdigest(),
                         "deliberateCorruption": corrupt_after_write})
    (directory / "scenarios.json").write_text(json.dumps(cases, indent=2), encoding="utf-8")
    (directory / "probe-layout.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(directory / "scenarios.json")


def verify(generated, output):
    from verify_zorro_iii_boot import verify as verify_native_boot
    verify_native_boot(output)
    scenarios = {s["name"]: s for s in json.loads((generated / "scenarios.json").read_text(encoding="utf-8"))}
    summaries = []
    for layout in json.loads((generated / "probe-layout.json").read_text(encoding="utf-8")):
        scenario = scenarios[layout["name"]]
        disk = generated / scenario["boot"]
        image_hash = hashlib.sha256(disk.read_bytes()).hexdigest()
        if image_hash != layout["imageSha256"]:
            raise ValueError("Synthetic diagnostic ADF changed since generation")
        case = output / layout["name"]
        outcome = json.loads((case / "result.json").read_text(encoding="utf-8"))
        if (outcome.get("error") is not None or outcome.get("status") != "executed-unreviewed"
                or outcome.get("boot") != scenario["boot"]):
            raise ValueError("Runner did not complete the native case")
        rows = [json.loads(line) for line in (case / "steps.jsonl").read_text(encoding="utf-8").splitlines()]
        if any(row.get("exceptionObservations", {}).get("nonRomExceptions", 0) for row in rows):
            raise ValueError("Unexpected guest exception")
        found = []
        for path in case.glob("*-ram-*.bin"):
            raw = path.read_bytes()
            cursor = 0
            while (cursor := raw.find(MAGIC, cursor)) >= 0:
                if cursor + REPORT_SIZE + len(layout["addresses"]) * 16 <= len(raw):
                    report = struct.unpack_from(">12I", raw, cursor + 16)
                    if report[1] != 0:  # ignore unexecuted DMA/cache copies
                        found.append((path, cursor, report, raw))
                cursor += 1
        if len(found) != 1:
            raise ValueError(f"{layout['name']}: expected one executed report, found {len(found)}")
        path, cursor, report, raw = found[0]
        version, status, before, after, freed, count, block_size, sysbase, holes, *_ = report
        if version != 2:
            raise ValueError(f"Unsupported guest report version {version}")
        if status != 0x600d600d:
            failures = {0xbad00001: "AllocAbs did not return the requested address",
                        0xbad00002: "guest pattern comparison mismatch",
                        0xbad00003: "available Fast memory did not return to its initial total",
                        0xbad00004: "Exec allocated absent aperture-hole memory"}
            reason = failures.get(status, "guest test did not finish")
            raise ValueError(f"{layout['name']}: {status:08X} {reason}")
        if (freed, count, block_size) != (len(layout["addresses"]) * 2,
                                        len(layout["addresses"]), BLOCK_SIZE):
            raise ValueError(f"{layout['name']}: incomplete allocation/free counters {report}")
        if holes != layout["sizesMiB"].count(8):
            raise ValueError("Guest did not reject each unbacked 8 MiB-card aperture hole")
        if before != after or before == 0 or sysbase == 0:
            raise ValueError("Guest free-memory totals were not restored")
        entries = [struct.unpack_from(">4I", raw, cursor + REPORT_SIZE + i * 16) for i in range(count)]
        if entries != [(a, a, 2048, 2048) for a in layout["addresses"]]:
            raise ValueError(f"Guest allocation/pattern checks incomplete: {entries}")
        cards = rows[-1]["zorroIIICards"]
        if [c["size"] // 1048576 for c in cards] != layout["sizesMiB"]:
            raise ValueError("Native card layout differs")
        for address in layout["addresses"]:
            if not any(c["configured"] and c["address"] <= address and
                       address + BLOCK_SIZE <= c["address"] + c["size"] for c in cards):
                raise ValueError("Tested address outside native assigned RAM")
        summaries.append({"name": layout["name"], "imageSha256": image_hash,
                          "reportFile": str(path), "offset": cursor,
                          "reportSha256": hashlib.sha256(raw[cursor:cursor + REPORT_SIZE + count * 16]).hexdigest(),
                          "availFastBefore": before, "availFastAfter": after,
                          "freeCalls": freed, "rejectedHoleAllocations": holes, "entries": entries, "frame": rows[-1]["frame"],
                          "cpu": rows[-1]["cpu"], "cards": cards, "exceptions": 0})
        print(f"OK {layout['name']}: {count} simultaneous 4 KiB allocations, two patterns/reuse/free cycles")
    return summaries


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    create = sub.add_parser("generate")
    create.add_argument("directory", type=Path)
    create.add_argument("--corrupt-after-write", action="store_true",
                        help="negative-control disk: deliberately fail guest pattern comparison")
    check = sub.add_parser("verify")
    check.add_argument("generated", type=Path)
    check.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "generate":
            generate(args.directory, args.corrupt_after_write)
        else:
            result = verify(args.generated, args.output)
            (args.output / "guest-memory-summary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"FAIL {error}\n")
