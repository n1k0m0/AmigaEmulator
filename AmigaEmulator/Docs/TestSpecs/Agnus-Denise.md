# Agnus and Denise Test Contracts

| ID | Area | Source | Contract | Status |
| --- | --- | --- | --- | --- |
| AGN-BEAM-001 | PAL beam | HRM video timing; A500 PAL constants | Beam wraps horizontally at 227 chip cycles and advances one PAL scanline. | implemented |
| AGN-VBL-001 | Interrupts | HRM interrupts/video timing | VBlank interrupt is requested at PAL frame wrap. | planned |
| AGN-DMA-001 | DMA arbitration | HRM DMA timing diagrams | Refresh, audio, disk, bitplane, copper, blitter, sprite, and CPU slots are selected with correct priority. | planned |
| AGN-BPL-001 | Bitplane DMA | HRM bitplane DMA; DDFSTRT/DDFSTOP | Bitplane fetch starts/stops according to DDF registers and display window. | implemented/basic |
| AGN-BPL-002 | Bitplane DMA | HRM Denise shift-register description | Agnus loads BPLxDAT latches before Denise shifts pixels. | implemented/basic |
| AGN-BPL-003 | Bitplane DMA | HRM DMA slot diagrams | One bitplane word is fetched per real DMA slot, with correct prefetch delay. | implemented/basic |
| DEN-BPL-001 | BPLxDAT | HRM playfield hardware | CPU/Copper writes to BPLxDAT feed visible Denise latches when DMA is off. | implemented |
| DEN-BPL-002 | Single playfield | HRM bitplane color selection | Plane bits combine into color index bits 0..5. | implemented/basic |
| DEN-BPL-003 | Dual playfield | HRM dual playfield mode/BPLCON2 | Odd planes map to playfield 2 color bank and priority rules. | implemented/basic |
| DEN-BPL-004 | EHB | HRM Extra Half-Brite | Plane 6 halves colors 0..31 in EHB mode. | planned |
| DEN-BPL-005 | HAM | HRM Hold-And-Modify | HAM modifies previous pixel RGB channels according to plane bits. | planned |
| DEN-SPR-001 | Direct sprites | HRM sprite registers | Direct SPRxPOS/CTL/DATA/DATB writes can produce sprite pixels. | implemented |
| DEN-SPR-002 | Sprite DMA | HRM sprite DMA | Sprite pointer lists load control/data words on the correct scanlines and DMA slots. | planned |
| DEN-SPR-003 | Priority | HRM BPLCON2 priority | Sprite/playfield priority matrix matches OCS. | planned |
| DEN-COLL-001 | Collision | HRM collision registers | Sprite/playfield and sprite/sprite collisions set CLXDAT bits. | planned |

`AGN-BPL-003` is now wired into the production frame scheduler. Bitplane DMA
slots fetch one enabled plane word at a time, commit a fetch group only after
all enabled planes arrive, and apply a prefetch-to-shifter delay before the
group becomes visible. North & South has a Golden ADF stripe regression check
because the previous grouped-to-split transition exposed vertical fetch bars.
