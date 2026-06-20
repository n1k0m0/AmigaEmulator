# Amiga Devtest Integration

`cdhooper/amiga_devtest` is used as an optional AmigaOS block-device
integration test for the floppy path.

The test is intentionally above the raw Paula/CIA level. It exercises the
Kickstart/AmigaOS `trackdisk.device` path through CIA drive control, Paula
disk DMA, and the mounted ADF image.

## Artifact

Place a bootable AmigaDOS ADF at one of these paths:

- `external-tests/amiga_devtest/amiga_devtest_trackdisk.adf`
- `external-tests/amiga_devtest/devtest_trackdisk.adf`
- `external-tests/amiga_devtest/devtest.adf`

Alternatively pass a path explicitly:

```powershell
.\Tools\run-amiga-devtest.ps1 -AdfPath C:\path\to\amiga_devtest_trackdisk.adf
```

The ADF should contain `devtest` and run it from `S:startup-sequence`, for
example with read-only trackdisk checks:

```text
devtest -g trackdisk.device 0
devtest -c CHANGESTATE -c PROTSTATUS -c READ(512,0) -c MOTORON -c MOTOROFF trackdisk.device 0
devtest -i 512 -l 20 trackdisk.device 0
```

Destructive write/integrity checks must only be run on disposable ADF copies.

## Run

```powershell
.\Tools\run-amiga-devtest.ps1
```

The test reports `SKIP` when the prepared ADF or Kickstart ROM is missing.

## Current Assertion

The current automated assertion verifies that the prepared ADF boots, advances
frames, performs and completes disk DMA, and produces visible output.

Full pass/fail parsing of `devtest` console output is not yet implemented.
That requires either serial/console capture or a dedicated result marker inside
the test ADF.
