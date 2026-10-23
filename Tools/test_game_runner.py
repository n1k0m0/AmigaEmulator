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
    def run_scenario(self, scenarios, *, validate=True, raw=False, extra_args=()):
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
            result = subprocess.run(["dotnet", str(RUNNER), str(manifest),
                                     "--validate" if validate else str(output), *extra_args],
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

    def test_held_joystick_edges_require_real_buttons_and_accept_chords(self):
        for kind in ("joystick-down", "joystick-up"):
            for button in (None, "None", "Forward", "Up,Forward"):
                with self.subTest(kind=kind, button=button):
                    action = {"type": kind}
                    if button is not None:
                        action["button"] = button
                    self.assert_invalid([{"name": "invalid-joystick-edge", "actions": [action]}])
        result = self.run_scenario([{"name": "held-joystick", "actions": [
            {"type": "joystick-down", "button": "Up,Right", "cycles": 250000},
            {"type": "wait", "cycles": 500000},
            {"type": "joystick-up", "button": "Up", "cycles": 0},
            {"type": "joystick-down", "button": "Fire", "cycles": 0},
            {"type": "joystick-up", "button": "Right,Fire", "cycles": 250000}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("VALID 1", result.stdout)

    def test_joy1_guard_requires_four_hex_digits(self):
        for value in ("", "123", "12345", "GGGG"):
            with self.subTest(value=value):
                self.assert_invalid([{"name": "bad-joy1", "actions": [
                    {"type": "wait", "expectedJoy1": value}]}])
        result = self.run_scenario([{"name": "joy1", "actions": [
            {"type": "joystick-down", "button": "Up,Right", "expectedJoy1": "0000"}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_memory_watch_accepts_bounded_distinct_ram_addresses(self):
        for addresses in ([0] * 33, [0, 0], [0xA00000], [0xFFFFFFFF]):
            with self.subTest(addresses=addresses):
                self.assert_invalid([{"name": "bad-watch", "memoryWatchAddresses": addresses,
                                      "actions": [{"type": "wait"}]}])
        result = self.run_scenario([{"name": "memory-watch", "memoryWatchAddresses":
                                    [0, 0x1FFFFF, 0x200000], "actions": [{"type": "wait"}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_zorro_iii_development_ram_requires_a_supported_desktop_board(self):
        for model, size in (("A500", 8), ("A1200", 8), ("A3000", 7)):
            with self.subTest(model=model, size=size):
                self.assert_invalid([{"name": "invalid-z3", "model": model,
                                     "zorroIIIRamMiB": size, "actions": [{"type": "wait"}]}])
        result = self.run_scenario([{"name": "valid-z3", "model": "A3000",
                                    "ram": "Chip2MbFast4Mb", "zorroIIIRamMiB": 8,
                                    "actions": [{"type": "wait"}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

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

    def test_native_shres_hash_guard_requires_sha256_hex(self):
        for value in ("", "0" * 63, "x" * 64):
            with self.subTest(value=value):
                self.assert_invalid([{"name": "bad-native-hash", "actions": [
                    {"type": "wait", "expectedNativeFrameHash": value}]}])
        result = self.run_scenario([{"name": "native-hash", "actions": [
            {"type": "wait", "expectedNativeFrameHash": "A" * 64}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_interlaced_hash_guard_requires_sha256_hex(self):
        for value in ("", "0" * 63, "x" * 64):
            with self.subTest(value=value):
                self.assert_invalid([{"name": "bad-interlaced-hash", "actions": [
                    {"type": "wait", "expectedInterlacedFrameHash": value}]}])
        result = self.run_scenario([{"name": "interlaced-hash", "actions": [
            {"type": "wait", "expectedInterlacedFrameHash": "A" * 64}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_bplcon0_guard_requires_four_hex_digits(self):
        for value in ("", "A30", "A3060", "XX06"):
            with self.subTest(value=value):
                self.assert_invalid([{"name": "bad-bplcon0", "actions": [
                    {"type": "wait", "expectedBplcon0": value}]}])
        result = self.run_scenario([{"name": "bplcon0", "actions": [
            {"type": "wait", "expectedBplcon0": "A306"}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_native_interlaced_hash_guard_requires_sha256_hex(self):
        for value in ("", "0" * 63, "x" * 64):
            with self.subTest(value=value):
                self.assert_invalid([{"name": "bad-native-interlaced-hash", "actions": [
                    {"type": "wait", "expectedNativeInterlacedFrameHash": value}]}])
        result = self.run_scenario([{"name": "native-interlaced-hash", "actions": [
            {"type": "wait", "expectedNativeInterlacedFrameHash": "A" * 64}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_native_interlaced_presence_guard_rejects_conflicting_hash(self):
        self.assert_invalid([{"name": "conflicting-native-field-guard", "actions": [
            {"type": "wait", "expectNativeInterlacedFrame": False,
             "expectedNativeInterlacedFrameHash": "A" * 64}]}])
        for present in (True, False):
            with self.subTest(present=present):
                result = self.run_scenario([{"name": "native-field-presence", "actions": [
                    {"type": "wait", "expectNativeInterlacedFrame": present}]}])
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_logical_interlaced_presence_guard_rejects_conflicting_hash(self):
        self.assert_invalid([{"name": "conflicting-field-guard", "actions": [
            {"type": "wait", "expectInterlacedFrame": False,
             "expectedInterlacedFrameHash": "A" * 64}]}])
        for present in (True, False):
            with self.subTest(present=present):
                result = self.run_scenario([{"name": "field-presence", "actions": [
                    {"type": "wait", "expectInterlacedFrame": present}]}])
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_compact_run_option_is_validated_before_outputs(self):
        case = [{"name": "compact", "actions": []}]
        good = self.run_scenario(case, extra_args=("--no-final-checkpoint",))
        self.assertEqual(0, good.returncode, good.stdout + good.stderr)
        self.assertIn("VALID 1", good.stdout)
        selected = self.run_scenario(case, extra_args=("--image-label=loaded",))
        self.assertEqual(0, selected.returncode, selected.stdout + selected.stderr)
        raw = self.run_scenario(case, extra_args=("--image-label=loaded", "--raw-frame"))
        self.assertEqual(0, raw.returncode, raw.stdout + raw.stderr)
        for extras in (("--no-final-checkpont",), ("compact", "another"),
                       ("--image-label=",), ("--image-label=one", "--image-label=two"),
                       ("--no-images", "--image-label=one"), ("--raw-frame",),
                       ("--no-images", "--raw-frame")):
            with self.subTest(extras=extras):
                bad = self.run_scenario(case, validate=False, extra_args=extras)
                self.assertEqual(2, bad.returncode, bad.stdout + bad.stderr)
                expected = "INVALID image label(s)" if extras == ("--image-label=one", "--image-label=two") else "INVALID command line"
                self.assertIn(expected, bad.stderr)

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

    def test_visible_pixel_guard_bounds_are_checked_before_outputs(self):
        for count in (-1, 704 * 288 + 1):
            self.assert_invalid([{"name": "invalid", "actions": [
                {"type": "wait", "minimumNonBlackPixels": count}]}], validate=False)
        for count in (0, 704 * 288):
            result = self.run_scenario([{"name": "valid", "actions": [
                {"type": "wait", "minimumNonBlackPixels": count}]}])
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_forbidden_frame_hashes_require_complete_sha256_before_outputs(self):
        for hashes in (["0" * 63], ["G" * 64], [None]):
            with self.subTest(hashes=hashes):
                self.assert_invalid([{"name": "invalid", "actions": [
                    {"type": "wait", "forbiddenFrameHashes": hashes}]}], validate=False)
        result = self.run_scenario([{"name": "valid", "actions": [
            {"type": "wait", "forbiddenFrameHashes": ["a" * 64]}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_invalid_json_has_a_controlled_error(self):
        self.assert_invalid("[{", raw=True)

    def test_valid_zero_key_and_modifiers_are_preserved(self):
        result = self.run_scenario([{"name": "valid", "actions": [
            {"type": "key", "key": 0, "modifiers": [96]},
            {"type": "joystick", "button": "Right, Fire"},
            {"type": "restore", "resetBeforeRestore": True, "cold": True}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("VALID 1", result.stdout)

    def test_mouse_button_edges_accept_a_two_button_chord(self):
        result = self.run_scenario([{"name": "mouse-chord", "actions": [
            {"type": "mouse-button-down", "button": "Second", "cycles": 1000,
             "expectedCiaAPortA": "C0", "expectedPotInp": "5100"},
            {"type": "mouse-button-down", "button": "Fire", "cycles": 1000,
             "expectedCiaAPortA": "80", "expectedPotInp": "5100"},
            {"type": "mouse-button-up", "button": "Fire", "cycles": 1000,
             "expectedCiaAPortA": "C0", "expectedPotInp": "5100"},
            {"type": "mouse-button-up", "button": "Second", "cycles": 1000,
             "expectedCiaAPortA": "C0", "expectedPotInp": "5500"}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("VALID 1", result.stdout)
        for button in ("Right", "Second, Fire", "None", "Invalid"):
            with self.subTest(button=button):
                self.assert_invalid([{"name": "bad-mouse-edge", "actions": [
                    {"type": "mouse-button-down", "button": button}]}], validate=False)

    def test_expected_input_registers_reject_bad_hex(self):
        for field, bad in (("expectedCiaAPortA", "C"), ("expectedCiaAPortA", "GG"),
                           ("expectedPotInp", "510"), ("expectedPotInp", "ZZZZ")):
            with self.subTest(field=field, bad=bad):
                self.assert_invalid([{"name": "bad-input-guard", "actions": [
                    {"type": "wait", field: bad}]}], validate=False)

    def test_mouse_step_pacing_only_applies_to_mouse_actions(self):
        result = self.run_scenario([{"name": "fast-aim", "actions": [
            {"type": "mouse", "x": 240, "y": 30, "mouseStepCycles": 20000}]}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        for action in ({"type": "mouse", "mouseStepCycles": -1},
                       {"type": "wait", "mouseStepCycles": 20000}):
            with self.subTest(action=action):
                self.assert_invalid([{"name": "bad-pacing", "actions": [action]}], validate=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runner", type=Path, default=RUNNER)
    args, rest = parser.parse_known_args()
    RUNNER = args.runner.resolve()
    unittest.main(argv=[__file__, *rest])
