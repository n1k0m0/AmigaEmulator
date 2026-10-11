#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
"""Regression checks for scenario preflight; no ROMs or guest boot required."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


RUNNER = Path(__file__).parent / "GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll"


class PreflightTests(unittest.TestCase):
    def run_scenario(self, scenarios, *, validate=True, raw=False):
        with tempfile.TemporaryDirectory(prefix="AmigaScenarioPreflight-") as temp:
            root = Path(temp)
            manifest = root / "scenario.json"
            output = root / "output"
            disk = root / "must-not-be-created.hdf"
            if not raw:
                for scenario in scenarios:
                    if scenario is not None:
                        scenario.setdefault("hardDisk", str(disk))
                        scenario.setdefault("createHardDiskMiB", 16)
            manifest.write_text(scenarios if raw else json.dumps(scenarios), encoding="utf-8")
            result = subprocess.run(["dotnet", str(RUNNER), str(manifest), "--validate" if validate else str(output)],
                                    capture_output=True, text=True, timeout=30)
            self.assertFalse(output.exists(), "Rejected preflight must not create output directories.")
            self.assertFalse(disk.exists(), "Preflight must not create or mount guest storage.")
            return result

    def assert_invalid(self, scenarios, **options):
        result = self.run_scenario(scenarios, **options)
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("INVALID", result.stderr)

    def test_misspelled_key_and_button_are_rejected(self):
        for action in ({"type": "key", "code": 68}, {"type": "joystick", "buttons": "Right"}):
            with self.subTest(action=action):
                self.assert_invalid([{"name": "typo", "actions": [action]}], validate=False)

    def test_key_action_requires_an_explicit_key(self):
        for kind in ("key", "key-down", "key-up"):
            with self.subTest(kind=kind):
                self.assert_invalid([{"name": "missing-key", "actions": [{"type": kind}]}])

    def test_held_keys_reject_invalid_codes_and_implicit_modifiers(self):
        for kind in ("key-down", "key-up"):
            for fields in ({"key": -1}, {"key": 128}, {"key": 25, "modifiers": [96]},
                           {"key": 25, "cycles": -1}):
                with self.subTest(kind=kind, fields=fields):
                    self.assert_invalid([{"name": "invalid-edge", "actions": [{"type": kind, **fields}]}])

    def test_explicit_held_key_chord_and_release_are_valid(self):
        result = self.run_scenario([{"name": "held-chord", "actions": [
            {"type": "key-down", "key": 25, "cycles": 400000},
            {"type": "wait", "cycles": 1000000},
            {"type": "key-down", "key": 69, "cycles": 400000},
            {"type": "key-up", "key": 69, "cycles": 400000},
            {"type": "key-up", "key": 25, "cycles": 400000}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("VALID 1", result.stdout)

    def test_write_protection_requires_an_explicit_tab_and_valid_drive(self):
        for action in ({"type": "write-protect"},
                       {"type": "write-protect", "drive": -1, "writeProtected": False},
                       {"type": "write-protect", "drive": 4, "writeProtected": True}):
            with self.subTest(action=action):
                self.assert_invalid([{"name": "invalid-tab", "actions": [action]}])
        result = self.run_scenario([{"name": "tab", "actions": [
            {"type": "write-protect", "drive": 0, "writeProtected": False},
            {"type": "write-protect", "drive": 3, "writeProtected": True}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_unknown_scenario_field_is_rejected(self):
        self.assert_invalid([{"name": "typo", "bootDisk": "missing.adf", "actions": []}])

    def test_exception_vector_guard_rejects_invalid_or_unobserved_vectors(self):
        for fields in ({"allowedNonRomExceptionVectors": [4, 5, 9]},
                       {"traceExceptions": True, "allowedNonRomExceptionVectors": [4, 4]},
                       {"traceExceptions": True, "allowedNonRomExceptionVectors": [1]},
                       {"traceExceptions": True, "allowedNonRomExceptionVectors": [24]}):
            with self.subTest(fields=fields):
                self.assert_invalid([{"name": "invalid-fault-guard", "actions": [], **fields}], validate=False)

    def test_exception_vector_guard_accepts_reviewed_probes_or_no_faults(self):
        for allowed in ([4, 5, 9], []):
            with self.subTest(allowed=allowed):
                result = self.run_scenario([{"name": "fault-guard", "traceExceptions": True,
                                            "allowedNonRomExceptionVectors": allowed, "actions": []}])
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn("VALID 1", result.stdout)

    def test_duplicate_names_cannot_replace_evidence(self):
        self.assert_invalid([{"name": "same", "actions": []}, {"name": "same", "actions": []}], validate=False)

    def test_null_scenario_has_a_controlled_error(self):
        self.assert_invalid([None])

    def test_invalid_json_has_a_controlled_error(self):
        self.assert_invalid("[{", raw=True)

    def test_valid_zero_key_and_modifiers_are_preserved(self):
        result = self.run_scenario([{"name": "valid", "actions": [
            {"type": "key", "key": 0, "modifiers": [96]},
            {"type": "joystick", "button": "Right, Fire"},
            {"type": "restore", "resetBeforeRestore": True, "cold": True}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("VALID 1", result.stdout)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runner", type=Path, default=RUNNER)
    args, rest = parser.parse_known_args()
    RUNNER = args.runner.resolve()
    unittest.main(argv=[__file__, *rest])
