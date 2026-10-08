# Storage and memory expansion — 2026-10-08

The optional controller exposes one ATA master via A600 Gayle registers, CHS
and LBA28 sector addressing, 16-bit PIO, IDENTIFY, READ/WRITE SECTORS, seek,
recalibrate, initialize parameters, software reset, and level-2 interrupts.
DMA, ATAPI, multiple-mode transfers, PCMCIA and a complete A600 chipset model
are not advertised. HDF images are raw whole disks, capped at 2 GiB for this
initial driver-compatible implementation. New image sizes are rounded down to
complete 16-head/63-sector cylinders. Newly created images have a valid RDB,
one bootable DH0 partition and FFS DOS type; format/install inside the guest.

Gayle register decoding and ID/IRQ behavior were checked against the
[WinUAE Gayle hardware implementation](https://github.com/tonioni/WinUAE/blob/master/gayle.cpp).
ATA register/data behavior follows the
[ATA command/register definitions](https://github.com/torvalds/linux/blob/master/include/linux/ata.h).
RDB/partition fields and Zorro memory-board registers follow the
[Amiga-compatible hardblocks definitions](https://github.com/aros-development-team/AROS/blob/master/compiler/include/devices/hardblocks.h)
and [Autoconfig register definitions](https://github.com/aros-development-team/AROS/blob/master/compiler/include/libraries/configregs.h).
No source from these projects or proprietary ROM/software was copied into the repository.

## Verification

- Isolated Release build: zero warnings/errors. Standard suite: 125 passed;
  complete local ADF/Siedler/vAmigaTS smoke suite with contention: 139 passed,
  zero failures/skips. Final executable: `tmp/storage-final/AmigaEmulator.exe`.
  The existing GUI locks its normal build directory; it was left running.
- Seven new storage/expansion regressions verify RDB/PART checksums and boot
  flags, non-overwriting creation, identify endianness/capacity, CHS/LBA
  persistence across a track boundary, count-zero 256-sector transfer, read-only
  errors, invalid ranges, absent slave, status/alternate-status IRQ semantics,
  software reset preserving the Gayle interrupt mask, partial-transfer
  savestates and 2/4/8 MiB Autoconfig assignment/memory bounds.
- A 68000 deferred MOVE.L reads distinct consecutive FIFO words, without
  debugger peeks consuming data. FIFO preview cursors also support multiple
  reads planned in a single instruction and preserve real retirement effects.
- Independent amitools `rdbtool` accepted the generated 16-MiB image geometry,
  RDB/PART checksums, DH0 boot priority/FFS parameters. `xdftool` formatted that
  test partition. These tools were installed only under the temporary evidence
  directory; the application needs neither Python nor amitools.
- The local Kickstart 3.1 ROM cold-booted that FFS test image with **no ADF
  inserted**, through the emulated controller, into the AmigaDOS Initial CLI.
  Fourteen sectors were read. This is real ROM/device/filesystem execution.
  It does not certify an installed Workbench desktop or every HDD game.
- With 2 MiB Chip + 4 MiB Fast, `expansion.library` assigned the board at
  200000. Exec's memory list contains `expansion memory`, and Exec/tasks reside
  in that Fast RAM after boot. Thus Fast RAM is registered by the guest OS,
  not merely allocated on the host.
- The actual WinForms hard-disk menu was rendered and visually inspected.
  Its shared menu footer spacing was corrected to keep status below all rows.

Local evidence is under [tmp](../../../tmp): `storage-boot-ram/HD-Boot`
(screenshots, CPU/controller diagnostics, checkpoint and register trace),
`storage-menu-final.png`, `storage-final-build.log`, `storage-final-standard.log`,
and `storage-full.log`. ROMs, images and
temporary dependencies are outside the repository.

## Savestates and writes

System state version 3 appends controller/Autoconfig state; versions 1 and 2
retain their original layout. An old save disables IDE and retains the legacy
Fast-RAM mapping. Restoring a new HDD save requires the recorded image and its
original length. Image bytes are not embedded or rolled back. ATA pending
writes save the sector buffer; completed writes modify the external HDF.

Ordinary floppy games keep IDE disabled unless selected by the user. Media,
RAM, controller enablement, read-only mode, creation size and boot preference
are configurable in the overlay and saved in user settings.
