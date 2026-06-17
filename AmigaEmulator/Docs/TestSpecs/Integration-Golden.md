# Integration and Golden Test Contracts

| ID | Area | Source | Contract | Status |
| --- | --- | --- | --- | --- |
| INT-BUS-001 | Custom writes | A500 memory map; HRM custom registers | CPU writes to custom registers trigger chip side effects such as BPLxDAT latches. | implemented |
| INT-COP-001 | Copper/display | HRM Copper + playfield chapters | Copper mid-frame register writes affect the visible frame at the correct beam position. | planned |
| INT-BLT-001 | Blitter/display | HRM blitter + DMA timing | Blitter writes to chip RAM during display DMA interact with bitplane fetch timing correctly. | planned |
| INT-DSK-001 | Disk boot | Kickstart ROM 1.3; HRM disk/CIA | Kickstart reads bootblock through CIA/Paula/floppy path and leaves ROM overlay correctly. | planned |
| GOLD-KS-001 | Kickstart | Kickstart 1.3 ROM | No-disk boot reaches real animated disk screen without fake drawing. | planned |
| GOLD-ADF-001 | Game checkpoint | Local ADF corpus | Turrican III reaches a nonblank trainer/boot frame. | implemented/optional |
| GOLD-ADF-002 | Game checkpoint | Local ADF corpus | North & South reaches a nonblank intro/language frame. | implemented/optional |
| GOLD-ADF-003 | Matrix | Local ADF corpus | ADF matrix emits category, PC, visual class, and screenshot per model. | cli-existing |
| GOLD-ADF-004 | External diagnostics | keirf/amiga-stuff Amiga Test Kit 1.21 | AmigaTestKit.adf boots to a visible diagnostic frame with multiple colors. | implemented/optional |

Golden ADF tests are optional because they are slow and require local ROM/ADF
files. Use `--include-adf` when validating gameplay-facing changes.
