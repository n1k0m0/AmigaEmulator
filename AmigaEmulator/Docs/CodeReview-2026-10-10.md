# Code review — 2026-10-10

Reviewed the repository-owned C#, Python, PowerShell, shell, Inno Setup, project
and workflow sources, with focused inspection of network framing, relay parsing,
disk-image handling, release packaging and the corresponding regressions. This
was a source review and automated test pass, not a new full game/model matrix.

## Findings and changes

- Added Apache-2.0 SPDX/copyright headers to 74 repository-owned source files.
  `Tools/audit_source_headers.py` now checks these files, and the release workflow
  runs it before packaging. JSON/Markdown cannot carry portable source comments.
- Kept the original FFmpeg-derived HQx notice and the BSD-2-Clause devtest
  license; these third-party sources must not be relabeled as Apache-2.0.
- Removed 343 generated XML summaries that only repeated identifiers such as
  “Runs Present” or “Gets or sets Width”. Hardware invariants, contracts and
  explanatory comments remain; the 2-GiB HDF cap now has an explicit rationale.
- AMIGAR session framing now distinguishes clean EOF from a truncated header or
  payload. Previously malformed partial frames returned `null` as if the peer
  had disconnected cleanly. Regression coverage includes all three cases.
- The Python relay now makes the same distinction for partial magic/header and
  payload. It raises a framing error for an incomplete packet while preserving
  `None` for a clean disconnect.

## Verification

- `dotnet build AmigaEmulator.sln -c Release -p:Platform=x64 --no-restore -warnaserror`:
  0 warnings, 0 errors.
- Full `AmigaEmulator.Tests` run: 298 passed, 0 failed, 0 skipped; this includes
  real LAN TLS and Python-relay integration tests.
- `python -m unittest discover -s relay_server`: 5 passed.
- `python Tools/audit_source_headers.py`: 0 repository-owned sources missing a
  license header.

## Siedler follow-up

At the end of the source review, the Siedler Intro/Disk-1 route with Kickstart
1.3 still reached an invalid filesystem state during the later Disk-2 handoff.
The subsequent investigation confirmed damaged allocation metadata in the
source ADF. A separately repaired copy now reaches gameplay and castle
construction; the original is preserved. The repair, Validator packet trace,
cold gameplay checks and remaining scope are documented in `MachineModels.md`.
