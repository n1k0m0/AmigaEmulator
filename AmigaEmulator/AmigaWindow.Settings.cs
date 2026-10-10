// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Drawing;

namespace AmigaEmulator;

public sealed partial class AmigaWindow
{
    private void ChangeRamItem(int direction)
    {
        if (_ramSelection == 0) ApplyRamProfile(CycleMachineRam(direction));
        else CyclePcmciaSram(direction);
    }

    private void ChangeSoundItem(int direction)
    {
        switch (_soundSelection)
        {
            case 0: ToggleAudio(); break;
            case 1: AdjustVolume(direction); break;
            case 2: ApplyPaulaFilterMode(CycleEnum(_paulaFilterMode, direction)); break;
        }
        _settings.AudioEnabled = _audioEnabled;
        _settings.MasterVolume = _masterVolume;
        AmigaEmulator.Core.AmigaEmulatorSettingsStore.Save(_settings);
    }

    private void DrawRamOverlay(Graphics graphics) => DrawMenu(graphics, "SETTINGS / RAM", "LEFT/RIGHT ADJUST  ESC BACK", new[]
    {
        MenuLine("MEMORY LAYOUT", FormatRamProfile(_ramProfile)),
        MenuLine("PCMCIA SRAM", FormatPcmciaSram())
    }, _ramSelection);

    private void DrawSoundOverlay(Graphics graphics) => DrawMenu(graphics, "SETTINGS / SOUND", "LEFT/RIGHT ADJUST  ESC BACK", new[]
    {
        MenuLine("AUDIO", _audioEnabled ? "ON" : "OFF"),
        MenuLine("MASTER VOLUME", FormatPercent(_masterVolume)),
        MenuLine("PAULA FILTER", FormatPaulaFilter(_paulaFilterMode))
    }, _soundSelection);
}
