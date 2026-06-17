# A500 Test Specification Catalog

This catalog maps emulator tests to hardware sources. It is intentionally close
to executable tests: every row should either already have a matching
`AmigaEmulator.Tests` case or be specific enough to implement next.

Primary local sources:

- `technical documentations/Amiga_Hardware_Reference_Manual_3rd_edition.pdf`
- `technical documentations/Commodore_Amiga_500_Technical_Manual.pdf`
- `technical documentations/Amiga_ROM_Kernel_Reference_Manual_v1.3.pdf`
- `technical documentations/M68000PRM.pdf`
- `technical documentations/Paula_SystemTheoretic.pdf`
- `technical documentations/denise_specs.pdf`

Supplementary public references:

- Amiga Hardware Reference Manual mirror: https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf
- HRM CIA Appendix mirror: https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_f.html
- Motorola M68000 PRM: https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf
- Paula disk/MFM notes: https://www.techtravels.org/2008/09/how-the-amiga-reads-floppies/
- DDFSTRT/DDFSTOP notes: https://amiga-dev.wikidot.com/hardware%3Addfstrt

Test status values:

- `implemented`: covered by `AmigaEmulator.Tests` or migrated self-test.
- `cli-existing`: covered by an existing emulator CLI self-test, not yet moved
  into a public test class.
- `planned`: test contract defined but not implemented yet.
- `blocked`: requires a core capability that does not exist yet.

Run quick suite:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll
```

Run optional ADF golden checks:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-adf
```
