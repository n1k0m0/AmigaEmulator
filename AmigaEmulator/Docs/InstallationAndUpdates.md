# Installation, ROM startup and GitHub updates

The Windows distribution follows the C64 frontend: Inno Setup 6, a self-contained
Windows x64 application, Start Menu entry, optional desktop shortcut and a
background GitHub Release check after the emulator window opens.

## User-owned Kickstart ROMs

Neither the installer nor the repository distribution includes Kickstart ROMs,
Workbench disks, games, saves or hard disk images. ROMs are never downloaded by
the startup wizard or updater.

Before creating the emulator, GUI startup checks the saved custom ROM,
`AMIGA_KICKSTART` and available ROM directories. If no usable ROM is available,
a dialog asks for an individual ROM file or an existing ROM folder. Folder
selection also searches subfolders. Validation checks supported 256 KB, 512 KB
and 1 MB sizes and the reset vector. Known motherboard hashes guide automatic
A1200/A3000/A4000 selection; an unknown user ROM can be selected explicitly.
This does not prove full machine/game compatibility.

A successful selection stores the external file and directory paths in
`%APPDATA%/AmigaEmulator/settings.json`; the ROM stays in its original location.
The directory is also available in `F10 → SETTINGS → ROMS`. Subsequent startup
uses the saved path. Cancel, an empty folder, an invalid ROM or an unavailable
path ends startup cleanly, before the emulator and updater are created.
Headless diagnostics and regression commands do not show the ROM wizard or
check for updates.

## Build an installer

The root `VERSION` file is the single default application/setup version. An
explicit build `-Version` overrides it, and publishing checks the executable's
file/product version before compiling Setup.

```powershell
./scripts/build-installer.ps1
```

Inno Setup 6 and the .NET 10 SDK must be installed on the build machine. The
result includes its runtime, so end users do not need a separate .NET SDK:

```text
artifacts/installer/AmigaEmulator-<version>-win-x64-setup.exe
artifacts/installer/AmigaEmulator-<version>-win-x64-setup.exe.sha256
```

The publisher guards its output path and refuses to clear a previous publish
folder containing ROMs or user media. The distribution audit rejects ordinary
ROM/media extensions, user data folders, encrypted Kickstart headers and raw
Kickstart images renamed to other extensions. Inno independently excludes ROMs
and media. Build artifacts are ignored by Git. The relay service is distributed
and deployed separately.

## Install, update and remove

The default installation uses Program Files and administrator privileges, as
the C64 installer does. `/CURRENTUSER` supports installation without elevation.
The stable Amiga installer ID is independent of C64's ID. Installing another
version offers cleanup of previous application binaries. Cleanup only touches
known program files; it never recursively deletes a user-chosen directory.
Silent updates skip this confirmation.

Updates and uninstall retain user-owned ROMs, settings, saves and hard disk
images, including files placed in the installation folder. Uninstall removes
installed application files and the installation registration. This deliberate
data retention differs from the C64 uninstaller's AppData cleanup.

## GitHub startup update

GUI startup starts a delayed background check against
`https://api.github.com/repos/n1k0m0/AmigaEmulator/releases/latest`. A newer stable
release needs an `AmigaEmulator-<version>-win-x64-setup.exe` asset. No release,
offline/rate-limited GitHub, absent Setup assets or an unchanged version continues
silently. Download URLs must point to this repository's HTTPS release assets.

Accepting the update opens the C64-style download window with progress, sizes,
cancel/retry and an Install button. Downloads use separate temporary directories
under `%TEMP%/AmigaEmulator/updates`. A supplied GitHub SHA-256 asset digest is
verified; malformed digests, mismatches, empty/truncated downloads, HTTP errors,
oversized transfers and cancellation cannot produce an installable update.
Closing the download window cancels; only the Install button authorizes launch.
A hidden PowerShell helper waits for the emulator process to exit before Setup
starts. Shutdown is marshalled to the emulator's UI thread so media can flush
and normal window cleanup runs first. Closing the emulator cancels its pending
startup check.

## GitHub release workflow

`.github/workflows/release.yml` builds/tests on a `v*` tag or manual dispatch.
Version tags must match `VERSION`. The job runs the .NET/Python regressions,
compiles and audits the installer, tests its real install/update/uninstall cycle,
and saves Setup plus checksum as workflow artifacts. A version tag additionally
creates or updates the GitHub Release assets; manual branch dispatch only builds.
To release, first update `VERSION`, commit the reviewed changes, then create and
push the matching tag. No commit, push, tag or public Release was performed when
implementing this system.

## Verification

```powershell
dotnet run --project AmigaEmulator.Tests -c Release -p:Platform=x64 -- --filter Distribution
./scripts/build-installer.ps1
./scripts/test-installer.ps1
```

The installer test uses a fresh external directory and refuses to run if an
existing Amiga installation is registered. It creates only synthetic ROM/HDF
fixtures, verifies package version/checksum, runs the installed self-contained
emulator, updates and uninstalls, and verifies user-owned fixtures survive.
It leaves logs and the retained fixtures in its external test directory.

Evidence on 2026-10-10: 274 .NET regressions pass (including nine distribution
tests), the actual GUI entry point shows and cancels the ROM dialog without
creating an emulator or settings, and actual setup install/update/uninstall
passes with byte-identical retained ROM/HDF fixtures. The GitHub workflow passes
`actionlint`; public-release download/launch awaits the first published release.
Fresh evidence: `%TEMP%/AmigaInstaller-20261010-101520`.
