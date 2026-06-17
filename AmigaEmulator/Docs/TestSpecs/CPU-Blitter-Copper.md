# CPU, Blitter, and Copper Test Contracts

| ID | Area | Source | Contract | Status |
| --- | --- | --- | --- | --- |
| CPU-OP-001 | 68000 opcodes | Motorola M68000 PRM | Boot-critical addressing, arithmetic, exception, STOP/RTE, MOVEP, trap, privilege behavior. | implemented |
| CPU-OP-002 | 68000 opcode matrix | Motorola M68000 PRM opcode map | Every 16-bit opcode word can execute one isolated step without host exceptions. | implemented |
| CPU-OP-003 | 68000 implemented decode | Motorola M68000 PRM opcode map | Every opcode in a currently implemented family decodes as executable or architecturally intentional exception, not an accidental illegal fallback. | implemented/basic |
| CPU-OP-004 | 68000 semantic vectors | Motorola M68000 PRM condition code rules | Representative arithmetic, logic, branch, shift, bit-operation, and line exception vectors produce expected registers, PC, and CCR/SR bits. | implemented/basic |
| CPU-BUS-001 | Bus masking | M68000/A500 bus | Address registers remain 32-bit internally while A500 bus addresses are 24-bit. | implemented |
| BLT-MIN-001 | Minterms | HRM blitter chapter | Disabled/enabled sources feed correct minterm inputs. | implemented |
| BLT-FIL-001 | Fill | HRM blitter fill mode | Inclusive/exclusive fill scans right-to-left and carries fill state correctly. | implemented |
| BLT-DESC-001 | Descending | HRM blitter descending mode | Descending pointers, masks, shifts, and modulo are applied correctly. | implemented |
| BLT-LINE-001 | Line mode | HRM blitter line mode | Line mode writes according to mask and destination configuration. | implemented |
| BLT-BUS-001 | DMA timing | HRM DMA timing | Blitter busy visibility, BLTPRI, and bus stealing match Agnus arbitration. | planned |
| COP-MOV-001 | MOVE | HRM Copper chapter | Copper MOVE respects protected register range unless COPCON permits. | implemented |
| COP-WAI-001 | WAIT | HRM Copper chapter | Copper WAIT compares beam position and BFD blitter-done bit correctly. | implemented/basic |
| COP-SKP-001 | SKIP | HRM Copper chapter | Copper SKIP skips the following instruction when beam condition is reached. | planned |
| COP-JMP-001 | COPJMP | HRM Copper registers | COPJMP strobes load list pointer registers and restart list execution. | implemented/basic |

The existing self-tests are executable through `AmigaEmulator.Tests` and still
available via the emulator CLI for quick single-chip debugging.
