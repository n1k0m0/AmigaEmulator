# Paula and CIA Test Contracts

| ID | Area | Source | Contract | Status |
| --- | --- | --- | --- | --- |
| PAU-AUD-001 | Audio DMA | HRM audio chapter | Four channels fetch words, split signed 8-bit samples, apply 6-bit volume, and raise channel IRQs. | planned |
| PAU-AUD-002 | Audio timing | HRM audio period registers | Period counters determine sample stepping independently per channel. | planned |
| PAU-AUD-003 | Modulation | HRM ADKCON audio modulation | Channel pairs can modulate period/volume where enabled. | planned |
| PAU-DSK-001 | MFM stream | HRM disk controller; techtravels MFM notes | Standard ADF track stream contains gap words and paired sync words. | implemented |
| PAU-DSK-002 | DSKBYTR | HRM disk data registers | Byte-ready bit is set when a disk byte arrives and cleared by read. | cli-existing |
| PAU-DSK-003 | WORDSYNC | HRM ADKCON WORDSYNC | Disk DMA waits until sync word and starts after sync. | cli-existing |
| PAU-DSK-004 | DSKLEN | HRM disk DMA | Disk DMA arming, block complete IRQ, and write-mode behavior match Paula. | cli-existing/basic |
| CIA-TIM-001 | Timers | HRM Appendix F; 8520 docs | Timer A/B underflow, one-shot, continuous mode, and count sources match 8520. | implemented |
| CIA-TOD-001 | TOD | HRM Appendix F | 8520 binary TOD latch/read/alarm semantics are correct. | implemented |
| CIA-ICR-001 | Interrupts | HRM Appendix F | ICR mask/data semantics and IRQ reassertion match CIA. | implemented |
| CIA-FLP-001 | Floppy control | A500 schematics; HRM disk section | CIAB port B selects drive, motor latch, side, step, and direction. | cli-existing/basic |
| CIA-FLP-002 | Write protection | HRM disk sensing | Menu protection changes PROT without replacing media, altering CHNG, resetting rotation/head state or discarding dirty/partial writes; pending insertions retain their deadline and protection across restore on DF0-DF3. | implemented; three failing-before fixtures; title-specific score persistence remains open |
| CIA-KBD-001 | Keyboard | HRM Appendix G | Serial bits clock into CIA/interrupt path; missing acknowledgement recovers one bit per 143 ms, then F9 and replay; startup synchronizes before FD/FE; partial recovery restores exactly. | implemented; electrical handshake pulse-width coverage remains open |

Disk write support and non-AmigaDOS/custom track encodings are intentionally
marked incomplete until raw track preservation exists.
