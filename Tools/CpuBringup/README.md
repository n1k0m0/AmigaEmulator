# CPU bring-up harness

Run from the solution directory:

```powershell
dotnet build Tools/CpuBringup/CpuBringup.csproj -c Release -p:Platform=x64
dotnet Tools/CpuBringup/bin/x64/Release/net10.0-windows7.0/CpuBringup.dll M68030 YOUR-ROM.ROM C:/Temp/new-cpu-result 40000000
```

Arguments: CPU (`M68EC020`, `M68020`, `M68EC030`, `M68030`), ROM path, fresh
output directory, optional Chip cycles (40 million), optional read-only HDF
(empty argument for none), optional chipset (`Ecs`/`Aga`), optional controller
(`Gayle`/`A4000`), optional CPU clock Hz (14,187,580), optional `desktop`.

The harness creates a synthetic A600-based development combination. It tests
CPU bring-up, not a complete named motherboard. For selectable A1200/A3000/
A4000 profiles, use GameRunner with `model` and a matching ROM instead.

Captures and diagnostics include cold boot, continued execution after core
save/restore, exception counts, CPU/vector state and framebuffer hashes.
Public savestate metadata deliberately refuses unreconstructible development
combinations. ROMs, HDFs and output captures belong outside the repository.
