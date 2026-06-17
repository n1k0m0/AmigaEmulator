/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Audio;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;
using SharpPixels;

namespace AmigaEmulator
{
    /// <summary>
    /// Hosts the emulator UI, SharpPixels presentation, overlay menus, input mapping, media loading, and audio output.
    /// </summary>
    public sealed class AmigaWindow : SharpPixelsWindow
    {
        /// <summary>
        /// Lists the supported Overlay Mode values.
        /// </summary>
        private enum OverlayMode
        {
            /// <summary>
            /// Represents the N on e option.
            /// </summary>
            None,
            /// <summary>
            /// Represents the S et ti ng s option.
            /// </summary>
            Settings,
            /// <summary>
            /// Represents the M ai nM en u option.
            /// </summary>
            MainMenu,
            /// <summary>
            /// Represents the D is pl ay option.
            /// </summary>
            Display,
            /// <summary>
            /// Represents the M ed ia option.
            /// </summary>
            Media,
            /// <summary>
            /// Represents the M ac hi ne option.
            /// </summary>
            Machine,
            /// <summary>
            /// Represents the R om s option.
            /// </summary>
            Roms,
            /// <summary>
            /// Represents the I np ut option.
            /// </summary>
            Input,
            /// <summary>
            /// Represents the R es et option.
            /// </summary>
            Reset,
            /// <summary>
            /// Represents the S av eS ta te s option.
            /// </summary>
            SaveStates
        }

        /// <summary>
        /// Lists the supported Render Limit Mode values.
        /// </summary>
        private enum RenderLimitMode
        {
            /// <summary>
            /// Represents the H z50 option.
            /// </summary>
            Hz50,
            /// <summary>
            /// Represents the H z120 option.
            /// </summary>
            Hz120,
            /// <summary>
            /// Represents the U nl im it ed option.
            /// </summary>
            Unlimited
        }

        /// <summary>
        /// Lists the supported Video Filter Mode values.
        /// </summary>
        private enum VideoFilterMode
        {
            /// <summary>
            /// Represents the S ha rp option.
            /// </summary>
            Sharp,
            /// <summary>
            /// Represents the C rt option.
            /// </summary>
            Crt,
            /// <summary>
            /// Represents the T v option.
            /// </summary>
            Tv
        }

        /// <summary>
        /// Lists the supported Video Upscale Mode values.
        /// </summary>
        private enum VideoUpscaleMode
        {
            /// <summary>
            /// Represents the N on e option.
            /// </summary>
            None,
            /// <summary>
            /// Represents the S ca le2 x option.
            /// </summary>
            Scale2x,
            /// <summary>
            /// Represents the S ca le3 x option.
            /// </summary>
            Scale3x,
            /// <summary>
            /// Represents the H q2 x option.
            /// </summary>
            Hq2x,
            /// <summary>
            /// Represents the H q3 x option.
            /// </summary>
            Hq3x,
            /// <summary>
            /// Represents the C le an4 option.
            /// </summary>
            Clean4
        }

        /// <summary>
        /// Lists the supported Paula Filter Mode values.
        /// </summary>
        private enum PaulaFilterMode
        {
            /// <summary>
            /// Represents the O ff option.
            /// </summary>
            Off,
            /// <summary>
            /// Represents the L ed option.
            /// </summary>
            Led,
            /// <summary>
            /// Represents the L ow Pa ss option.
            /// </summary>
            LowPass
        }

        /// <summary>
        /// Lists the supported RAM Profile values.
        /// </summary>
        private enum RamProfile
        {
            /// <summary>
            /// Represents the C hi p512 Sl ow512 option.
            /// </summary>
            Chip512Slow512,
            /// <summary>
            /// Represents the C hi p512 option.
            /// </summary>
            Chip512,
            /// <summary>
            /// Represents the C hi p1 Mb option.
            /// </summary>
            Chip1Mb,
            /// <summary>
            /// Represents the C hi p1 Mb Fa st4 Mb option.
            /// </summary>
            Chip1MbFast4Mb
        }

        /// <summary>
        /// Lists the supported Floppy Speed Mode values.
        /// </summary>
        private enum FloppySpeedMode
        {
            /// <summary>
            /// Represents the R ea l option.
            /// </summary>
            Real,
            /// <summary>
            /// Represents the T ur bo Lo ad in g option.
            /// </summary>
            TurboLoading,
            /// <summary>
            /// Represents the T ur bo Al wa ys option.
            /// </summary>
            TurboAlways
        }

        /// <summary>
        /// Lists the supported Input Port Mode values.
        /// </summary>
        private enum InputPortMode
        {
            /// <summary>
            /// Represents the M ou se Po rt1 Jo ys ti ck Po rt2 option.
            /// </summary>
            MousePort1JoystickPort2,
            /// <summary>
            /// Represents the J oy st ic kP or t1 Mo us eP or t2 option.
            /// </summary>
            JoystickPort1MousePort2,
            /// <summary>
            /// Represents the D ua lJ oy st ic k option.
            /// </summary>
            DualJoystick
        }

        /// <summary>
        /// Lists the supported Reset Mode values.
        /// </summary>
        private enum ResetMode
        {
            /// <summary>
            /// Represents the W ar m option.
            /// </summary>
            Warm,
            /// <summary>
            /// Represents the R em ou nt option.
            /// </summary>
            Remount,
            /// <summary>
            /// Represents the P ow er option.
            /// </summary>
            Power
        }

        /// <summary>
        /// Tracks the system.
        /// </summary>
        private AmigaSystem _system;
        /// <summary>
        /// Executes the Paula Audio Output logic.
        /// </summary>
        private readonly PaulaAudioOutput _audioOutput = new PaulaAudioOutput();
        /// <summary>
        /// Executes the Start New logic.
        /// </summary>
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        /// <summary>
        /// Executes the Font logic.
        /// </summary>
        private readonly Font _smallFont = new Font(FontFamily.GenericMonospace, 11.0f, FontStyle.Regular);
        /// <summary>
        /// Executes the Font logic.
        /// </summary>
        private readonly Font _titleFont = new Font(FontFamily.GenericMonospace, 18.0f, FontStyle.Bold);
        /// <summary>
        /// Tracks the main Menu Items.
        /// </summary>
        private readonly string[] _mainMenuItems = { "SETTINGS", "MEDIA", "MACHINE", "INPUT", "RESET", "DISPLAY" };
        /// <summary>
        /// Tracks the save Slots.
        /// </summary>
        private readonly SaveStateSlotMetadata[] _saveSlots = new SaveStateSlotMetadata[10];
        /// <summary>
        /// Tracks the settings.
        /// </summary>
        private readonly AmigaEmulatorSettings _settings;
        /// <summary>
        /// Tracks the overlay Mode.
        /// </summary>
        private OverlayMode _overlayMode = OverlayMode.None;
        /// <summary>
        /// Tracks the previous Overlay Mode.
        /// </summary>
        private OverlayMode _previousOverlayMode = OverlayMode.None;
        /// <summary>
        /// Tracks the render Limit Mode.
        /// </summary>
        private RenderLimitMode _renderLimitMode = RenderLimitMode.Hz50;
        /// <summary>
        /// Tracks the video Filter Mode.
        /// </summary>
        private VideoFilterMode _videoFilterMode = VideoFilterMode.Sharp;
        /// <summary>
        /// Tracks the video Upscale Mode.
        /// </summary>
        private VideoUpscaleMode _videoUpscaleMode = VideoUpscaleMode.None;
        /// <summary>
        /// Tracks the paula Filter Mode.
        /// </summary>
        private PaulaFilterMode _paulaFilterMode = PaulaFilterMode.Led;
        /// <summary>
        /// Tracks the RAM Profile.
        /// </summary>
        private RamProfile _ramProfile = RamProfile.Chip512;
        /// <summary>
        /// Tracks the floppy Speed Mode.
        /// </summary>
        private FloppySpeedMode _floppySpeedMode = FloppySpeedMode.Real;
        /// <summary>
        /// Tracks the input Port Mode.
        /// </summary>
        private InputPortMode _inputPortMode = InputPortMode.MousePort1JoystickPort2;
        /// <summary>
        /// Tracks the reset Mode.
        /// </summary>
        private ResetMode _resetMode = ResetMode.Warm;
        /// <summary>
        /// Tracks the accuracy Profile.
        /// </summary>
        private AmigaAccuracyProfile _accuracyProfile = AmigaAccuracyProfile.Fast;
        /// <summary>
        /// Tracks the mounted Media.
        /// </summary>
        private MountedMediaInfo _mountedMedia = MountedMediaInfo.None;
        /// <summary>
        /// Executes the media Entries logic.
        /// </summary>
        private readonly List<string> _mediaEntries = new List<string>();
        /// <summary>
        /// Executes the ROM Entries logic.
        /// </summary>
        private readonly List<KickstartRomInfo> _romEntries = new List<KickstartRomInfo>();
        /// <summary>
        /// Tracks the media Directory.
        /// </summary>
        private string _mediaDirectory;
        /// <summary>
        /// Tracks the selected ROM Path.
        /// </summary>
        private string _selectedRomPath;
        /// <summary>
        /// Tracks the settings Selection.
        /// </summary>
        private int _settingsSelection;
        /// <summary>
        /// Tracks the main Selection.
        /// </summary>
        private int _mainSelection;
        /// <summary>
        /// Tracks the display Selection.
        /// </summary>
        private int _displaySelection;
        /// <summary>
        /// Tracks the media Selection.
        /// </summary>
        private int _mediaSelection;
        /// <summary>
        /// Tracks the ROM Selection.
        /// </summary>
        private int _romSelection;
        /// <summary>
        /// Tracks the machine Selection.
        /// </summary>
        private int _machineSelection;
        /// <summary>
        /// Tracks the input Selection.
        /// </summary>
        private int _inputSelection;
        /// <summary>
        /// Tracks the reset Selection.
        /// </summary>
        private int _resetSelection;
        /// <summary>
        /// Tracks the save Selection.
        /// </summary>
        private int _saveSelection;
        /// <summary>
        /// Tracks the debug Overlay Visible.
        /// </summary>
        private bool _debugOverlayVisible = false;
        /// <summary>
        /// Tracks the drive Overlay Visible.
        /// </summary>
        private bool _driveOverlayVisible = true;
        /// <summary>
        /// Tracks the audio Enabled.
        /// </summary>
        private bool _audioEnabled = true;
        /// <summary>
        /// Tracks the floppy Write Protected.
        /// </summary>
        private bool _floppyWriteProtected = true;
        /// <summary>
        /// Tracks the turbo Mode.
        /// </summary>
        private bool _turboMode;
        /// <summary>
        /// Tracks the fullscreen.
        /// </summary>
        private bool _fullscreen;
        /// <summary>
        /// Tracks the has Last Mouse Position.
        /// </summary>
        private bool _hasLastMousePosition;
        /// <summary>
        /// Tracks the last Mouse Position.
        /// </summary>
        private Point _lastMousePosition;
        /// <summary>
        /// Tracks the master Volume.
        /// </summary>
        private float _masterVolume = 0.80f;
        /// <summary>
        /// Tracks the last Seconds.
        /// </summary>
        private double _lastSeconds;
        /// <summary>
        /// Tracks the last Update Seconds.
        /// </summary>
        private double _lastUpdateSeconds;
        /// <summary>
        /// Tracks the last Perf Seconds.
        /// </summary>
        private double _lastPerfSeconds;
        /// <summary>
        /// Tracks the pending Chip Cycles.
        /// </summary>
        private double _pendingChipCycles;
        /// <summary>
        /// Tracks the perf Chip Cycles.
        /// </summary>
        private long _perfChipCycles;
        /// <summary>
        /// Tracks the host Speed Percent.
        /// </summary>
        private double _hostSpeedPercent = 100.0;
        /// <summary>
        /// Tracks the smoothed CPU Mhz.
        /// </summary>
        private double _smoothedCpuMhz;
        /// <summary>
        /// Tracks the audio Filter Left.
        /// </summary>
        private float _audioFilterLeft;
        /// <summary>
        /// Tracks the audio Filter Right.
        /// </summary>
        private float _audioFilterRight;
        /// <summary>
        /// Tracks the last Frame.
        /// </summary>
        private long _lastFrame;
        /// <summary>
        /// Tracks the ROM Status Text.
        /// </summary>
        private string _romStatusText = "KICKSTART MISSING";
        /// <summary>
        /// Tracks the status Text.
        /// </summary>
        private string _statusText = "READY";

        /// <summary>
        /// Executes the Amiga Window logic.
        /// </summary>
        public AmigaWindow(AmigaModel model, string title)
            : base(model.VisibleWidth, model.VisibleHeight, model.VisibleWidth / 2, model.VisibleHeight, title)
        {
            _settings = AmigaEmulatorSettingsStore.Load();
            _videoFilterMode = GetVideoFilterFromSettings(_settings.VideoFilter);
            _videoUpscaleMode = GetVideoUpscaleFromSettings(_settings.UpscaleMode);
            _renderLimitMode = GetRenderLimitFromSettings(_settings.RenderFps);
            _paulaFilterMode = GetPaulaFilterFromSettings(_settings.PaulaFilter);
            _driveOverlayVisible = _settings.DriveOverlayEnabled;
            _turboMode = _settings.TurboMode;
            _accuracyProfile = _settings.AccuracyProfile;
            _ramProfile = GetRamProfileFromSettings(_settings.RamProfile, GetRamProfileForModel(model));
            _system = new AmigaSystem(GetModelForRamProfile(_ramProfile), _settings.CreateAccuracyOptions());
            AttachAudioToSystem();
            _inputPortMode = GetInputPortModeFromSettings(_settings.Port1Mode, _settings.Port2Mode);
            ApplyInputPortMode();
            _mediaDirectory = !string.IsNullOrWhiteSpace(_settings.MediaBrowserDirectory) ? _settings.MediaBrowserDirectory : GetInitialMediaDirectory();
            _selectedRomPath = _settings.CustomKickstartPath ?? string.Empty;
            LoadConfiguredKickstart();
            RefreshMediaEntries();
            RefreshRomEntries();
            RefreshSaveStateSlots();
            ApplyVideoPresentation();
            UpdateFrequency = GetRenderUpdateFrequency(_renderLimitMode);
            ResetEmulationClock();
        }

        /// <summary>
        /// Handles the Update Frame callback.
        /// </summary>
        protected override void OnUpdateFrame()
        {
            int cycles = CalculateCyclesForUpdate();
            if (cycles > 0)
            {
                _system.RunChipCycles(cycles);
                UpdatePerformanceCounters(cycles);
                UpdateTitleStatus(cycles);
            }

            _system.PresentedFrameBuffer.CopyTo(Pixels);
            _lastSeconds = _stopwatch.Elapsed.TotalSeconds;
            _lastFrame = _system.FrameCounter;
        }

        /// <summary>
        /// Executes the Process Cmd Key logic.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;

            if (key == Keys.F9)
            {
                ToggleTurboMode();
                return true;
            }

            if (key == Keys.F10)
            {
                ToggleOverlay(OverlayMode.MainMenu);
                return true;
            }

            if (key == Keys.F11)
            {
                ToggleFullscreen();
                return true;
            }

            if (key == Keys.F12)
            {
                RefreshSaveStateSlots();
                ToggleOverlay(OverlayMode.SaveStates);
                return true;
            }

            if (_overlayMode != OverlayMode.None)
            {
                if (HandleOverlayKey(key))
                {
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Handles the Mouse Wheel callback.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_overlayMode != OverlayMode.None && e.Delta != 0)
            {
                MoveSelection(e.Delta > 0 ? -1 : 1);
                return;
            }

            base.OnMouseWheel(e);
        }

        /// <summary>
        /// Handles the User Key Down callback.
        /// </summary>
        public override void OnUserKeyDown(SharpPixels.KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs == null)
            {
                return;
            }

            if (_overlayMode != OverlayMode.None)
            {
                return;
            }

            if (IsEmulatorHotkey(keyEventArgs.Key))
            {
                return;
            }

            if (HandleJoystickKey(keyEventArgs.Key, true))
            {
                keyEventArgs.Handled = true;
                return;
            }

            if (TryMapAmigaRawKey(keyEventArgs.Key, out byte rawKey))
            {
                _system.Input.KeyboardKeyDown(rawKey);
                keyEventArgs.Handled = true;
            }
        }

        /// <summary>
        /// Handles the User Key Up callback.
        /// </summary>
        public override void OnUserKeyUp(SharpPixels.KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs == null)
            {
                return;
            }

            if (IsEmulatorHotkey(keyEventArgs.Key))
            {
                return;
            }

            if (HandleJoystickKey(keyEventArgs.Key, false))
            {
                keyEventArgs.Handled = true;
            }

            if (TryMapAmigaRawKey(keyEventArgs.Key, out byte rawKey))
            {
                _system.Input.KeyboardKeyUp(rawKey);
                keyEventArgs.Handled = true;
            }
        }

        /// <summary>
        /// Handles the Mouse Move callback.
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_overlayMode == OverlayMode.None &&
                TryGetMousePort(out AmigaControlPort mousePort) &&
                _hasLastMousePosition)
            {
                Rectangle presentation = GetPresentationRectangle();
                double scaleX = presentation.Width > 0 ? PixelsWidth / (double)presentation.Width : 1.0;
                double scaleY = presentation.Height > 0 ? PixelsHeight / (double)presentation.Height : 1.0;
                int dx = (int)Math.Round((e.X - _lastMousePosition.X) * scaleX);
                int dy = (int)Math.Round((e.Y - _lastMousePosition.Y) * scaleY);
                _system.Input.MoveMouse(mousePort, dx, dy);
            }

            _lastMousePosition = e.Location;
            _hasLastMousePosition = true;
            base.OnMouseMove(e);
        }

        /// <summary>
        /// Handles the Mouse Down callback.
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_overlayMode == OverlayMode.None && TryGetMousePort(out AmigaControlPort mousePort))
            {
                AmigaJoystickButtons button = MapMouseButton(e.Button);
                if (button != AmigaJoystickButtons.None)
                {
                    _system.Input.SetMouseButton(mousePort, button, true);
                    Capture = true;
                }
            }

            base.OnMouseDown(e);
        }

        /// <summary>
        /// Handles the Mouse Up callback.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (TryGetMousePort(out AmigaControlPort mousePort))
            {
                AmigaJoystickButtons button = MapMouseButton(e.Button);
                if (button != AmigaJoystickButtons.None)
                {
                    _system.Input.SetMouseButton(mousePort, button, false);
                }
            }

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                Capture = false;
            }

            base.OnMouseUp(e);
        }

        /// <summary>
        /// Handles the Mouse Leave callback.
        /// </summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            _hasLastMousePosition = false;
            base.OnMouseLeave(e);
        }

        /// <summary>
        /// Handles the Render Overlay callback.
        /// </summary>
        protected override void OnRenderOverlay(Graphics graphics, Rectangle target)
        {
            if (_debugOverlayVisible)
            {
                DrawDebugOverlay(graphics);
            }

            if (_driveOverlayVisible)
            {
                DrawDriveFooter(graphics);
            }

            switch (_overlayMode)
            {
                case OverlayMode.Settings:
                    DrawSettingsOverlay(graphics);
                    break;
                case OverlayMode.MainMenu:
                    DrawMainMenuOverlay(graphics);
                    break;
                case OverlayMode.Display:
                    DrawDisplayOverlay(graphics);
                    break;
                case OverlayMode.Media:
                    DrawMediaOverlay(graphics);
                    break;
                case OverlayMode.Machine:
                    DrawMachineOverlay(graphics);
                    break;
                case OverlayMode.Roms:
                    DrawRomOverlay(graphics);
                    break;
                case OverlayMode.Input:
                    DrawInputOverlay(graphics);
                    break;
                case OverlayMode.Reset:
                    DrawResetOverlay(graphics);
                    break;
                case OverlayMode.SaveStates:
                    DrawSaveStatesOverlay(graphics);
                    break;
            }
        }

        /// <summary>
        /// Handles the Form Closed callback.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _smallFont.Dispose();
            _titleFont.Dispose();
            _audioOutput.Dispose();
            if (_system != null)
            {
                DetachAudioFromSystem(_system);
                _system.Dispose();
            }
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Handles Overlay Key from the host UI or emulator core.
        /// </summary>
        private bool HandleOverlayKey(Keys key)
        {
            if (key == Keys.Escape)
            {
                CloseOrBackOverlay();
                return true;
            }

            if (key == Keys.Up)
            {
                MoveSelection(-1);
                return true;
            }

            if (key == Keys.Down)
            {
                MoveSelection(1);
                return true;
            }

            if (key == Keys.PageUp)
            {
                MoveSelection(-10);
                return true;
            }

            if (key == Keys.PageDown)
            {
                MoveSelection(10);
                return true;
            }

            if (key == Keys.Home)
            {
                SetSelection(0);
                return true;
            }

            if (key == Keys.End)
            {
                int count = GetSelectionCount();
                if (count > 0)
                {
                    SetSelection(count - 1);
                }

                return true;
            }

            if (key == Keys.Left)
            {
                ChangeSelectedItem(-1);
                return true;
            }

            if (key == Keys.Right)
            {
                ChangeSelectedItem(1);
                return true;
            }

            if (key == Keys.Enter || key == Keys.Space)
            {
                ActivateSelectedItem();
                return true;
            }

            if (key == Keys.Delete && _overlayMode == OverlayMode.SaveStates)
            {
                DeleteCurrentSaveSlot();
                return true;
            }

            if (key == Keys.S && _overlayMode == OverlayMode.SaveStates)
            {
                SaveCurrentSlot();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes the Toggle Overlay logic.
        /// </summary>
        private void ToggleOverlay(OverlayMode mode)
        {
            if (_overlayMode == mode)
            {
                _previousOverlayMode = OverlayMode.None;
                _overlayMode = OverlayMode.None;
                return;
            }

            ShowOverlay(mode);
        }

        /// <summary>
        /// Executes the Show Overlay logic.
        /// </summary>
        private void ShowOverlay(OverlayMode mode)
        {
            _previousOverlayMode = _overlayMode == OverlayMode.None ? OverlayMode.None : _overlayMode;
            _overlayMode = mode;
        }

        /// <summary>
        /// Executes the Close Or Back Overlay logic.
        /// </summary>
        private void CloseOrBackOverlay()
        {
            if (_overlayMode == OverlayMode.Media ||
                _overlayMode == OverlayMode.Machine ||
                _overlayMode == OverlayMode.Roms ||
                _overlayMode == OverlayMode.Input ||
                _overlayMode == OverlayMode.Reset ||
                _overlayMode == OverlayMode.Display)
            {
                _overlayMode = _previousOverlayMode == OverlayMode.None ? OverlayMode.MainMenu : _previousOverlayMode;
                _previousOverlayMode = OverlayMode.None;
                return;
            }

            _overlayMode = OverlayMode.None;
            _previousOverlayMode = OverlayMode.None;
        }

        /// <summary>
        /// Executes the Move Selection logic.
        /// </summary>
        private void MoveSelection(int delta)
        {
            int count = GetSelectionCount();
            if (count <= 0)
            {
                return;
            }

            int value = GetSelection();
            value = (value + delta + count) % count;
            SetSelection(value);
        }

        /// <summary>
        /// Returns the Selection Count.
        /// </summary>
        private int GetSelectionCount()
        {
            switch (_overlayMode)
            {
                case OverlayMode.Settings: return 17;
                case OverlayMode.MainMenu: return _mainMenuItems.Length;
                case OverlayMode.Display: return 5;
                case OverlayMode.Media: return Math.Max(1, _mediaEntries.Count + 3);
                case OverlayMode.Machine: return 8;
                case OverlayMode.Roms: return Math.Max(1, _romEntries.Count + 3);
                case OverlayMode.Input: return 5;
                case OverlayMode.Reset: return 4;
                case OverlayMode.SaveStates: return _saveSlots.Length;
                default: return 0;
            }
        }

        /// <summary>
        /// Returns the Selection.
        /// </summary>
        private int GetSelection()
        {
            switch (_overlayMode)
            {
                case OverlayMode.Settings: return _settingsSelection;
                case OverlayMode.MainMenu: return _mainSelection;
                case OverlayMode.Display: return _displaySelection;
                case OverlayMode.Media: return _mediaSelection;
                case OverlayMode.Machine: return _machineSelection;
                case OverlayMode.Roms: return _romSelection;
                case OverlayMode.Input: return _inputSelection;
                case OverlayMode.Reset: return _resetSelection;
                case OverlayMode.SaveStates: return _saveSelection;
                default: return 0;
            }
        }

        /// <summary>
        /// Updates the Selection.
        /// </summary>
        private void SetSelection(int value)
        {
            switch (_overlayMode)
            {
                case OverlayMode.Settings: _settingsSelection = value; break;
                case OverlayMode.MainMenu: _mainSelection = value; break;
                case OverlayMode.Display: _displaySelection = value; break;
                case OverlayMode.Media: _mediaSelection = value; break;
                case OverlayMode.Machine: _machineSelection = value; break;
                case OverlayMode.Roms: _romSelection = value; break;
                case OverlayMode.Input: _inputSelection = value; break;
                case OverlayMode.Reset: _resetSelection = value; break;
                case OverlayMode.SaveStates: _saveSelection = value; break;
            }
        }

        /// <summary>
        /// Executes the Change Selected Item logic.
        /// </summary>
        private void ChangeSelectedItem(int direction)
        {
            switch (_overlayMode)
            {
                case OverlayMode.Settings:
                    ChangeSettingsItem(direction);
                    break;
                case OverlayMode.Display:
                    ChangeDisplayItem(direction);
                    break;
                case OverlayMode.Machine:
                    ChangeMachineItem(direction);
                    break;
                case OverlayMode.Input:
                    ChangeInputItem(direction);
                    break;
                case OverlayMode.Reset:
                    ChangeResetItem(direction);
                    break;
            }
        }

        /// <summary>
        /// Executes the Activate Selected Item logic.
        /// </summary>
        private void ActivateSelectedItem()
        {
            switch (_overlayMode)
            {
                case OverlayMode.MainMenu:
                    ActivateMainMenuItem();
                    break;
                case OverlayMode.Settings:
                    ChangeSettingsItem(1);
                    break;
                case OverlayMode.Display:
                    ChangeDisplayItem(1);
                    break;
                case OverlayMode.Media:
                    ActivateMediaItem();
                    break;
                case OverlayMode.Machine:
                    ChangeMachineItem(1);
                    break;
                case OverlayMode.Roms:
                    ActivateRomItem();
                    break;
                case OverlayMode.Input:
                    ChangeInputItem(1);
                    break;
                case OverlayMode.Reset:
                    ActivateResetItem();
                    break;
                case OverlayMode.SaveStates:
                    LoadOrCreateSaveSlot();
                    break;
            }
        }

        /// <summary>
        /// Executes the Activate Main Menu Item logic.
        /// </summary>
        private void ActivateMainMenuItem()
        {
            switch (_mainSelection)
            {
                case 0: ShowOverlay(OverlayMode.Settings); break;
                case 1: RefreshMediaEntries(); ShowOverlay(OverlayMode.Media); break;
                case 2: ShowOverlay(OverlayMode.Machine); break;
                case 3: ShowOverlay(OverlayMode.Input); break;
                case 4: ShowOverlay(OverlayMode.Reset); break;
                case 5: ShowOverlay(OverlayMode.Display); break;
            }
        }

        /// <summary>
        /// Executes the Change Settings Item logic.
        /// </summary>
        private void ChangeSettingsItem(int direction)
        {
            switch (_settingsSelection)
            {
                case 0: AdjustVolume(direction); break;
                case 1: ToggleAudio(); break;
                case 2: ApplyPaulaFilterMode(CycleEnum(_paulaFilterMode, direction)); break;
                case 3: ApplyVideoFilterMode(CycleEnum(_videoFilterMode, direction)); break;
                case 4: ApplyVideoUpscaleMode(CycleEnum(_videoUpscaleMode, direction)); break;
                case 5: CycleRenderLimit(); break;
                case 6: ToggleFullscreen(); break;
                case 7: ToggleTurboMode(); break;
                case 8: _floppySpeedMode = CycleEnum(_floppySpeedMode, direction); break;
                case 9: _inputPortMode = CycleEnum(_inputPortMode, direction); ApplyInputPortMode(); break;
                case 10: ApplyRamProfile(CycleEnum(_ramProfile, direction)); break;
                case 11: _debugOverlayVisible = !_debugOverlayVisible; break;
                case 12: ToggleDriveOverlay(); break;
                case 13: RefreshRomEntries(); ShowOverlay(OverlayMode.Roms); break;
                case 14: ApplyAccuracyProfile(CycleEnum(_accuracyProfile, direction)); break;
                case 15: _resetMode = CycleEnum(_resetMode, direction); break;
                case 16: ShowOverlay(OverlayMode.Reset); break;
            }
        }

        /// <summary>
        /// Executes the Change Display Item logic.
        /// </summary>
        private void ChangeDisplayItem(int direction)
        {
            switch (_displaySelection)
            {
                case 0: ApplyVideoFilterMode(CycleEnum(_videoFilterMode, direction)); break;
                case 1: ApplyVideoUpscaleMode(CycleEnum(_videoUpscaleMode, direction)); break;
                case 2: CycleRenderLimit(); break;
                case 3: ToggleFullscreen(); break;
                case 4: _debugOverlayVisible = !_debugOverlayVisible; break;
            }
        }

        /// <summary>
        /// Executes the Change Machine Item logic.
        /// </summary>
        private void ChangeMachineItem(int direction)
        {
            switch (_machineSelection)
            {
                case 0: _statusText = _system.Model.Name; break;
                case 1: ApplyRamProfile(CycleEnum(_ramProfile, direction)); break;
                case 2: _floppySpeedMode = CycleEnum(_floppySpeedMode, direction); break;
                case 3: _floppyWriteProtected = !_floppyWriteProtected; break;
                case 4: RefreshRomEntries(); ShowOverlay(OverlayMode.Roms); break;
                case 5: ApplyAccuracyProfile(CycleEnum(_accuracyProfile, direction)); break;
                case 6: _resetMode = CycleEnum(_resetMode, direction); break;
                case 7: ShowOverlay(OverlayMode.Reset); break;
            }
        }

        /// <summary>
        /// Executes the Change Input Item logic.
        /// </summary>
        private void ChangeInputItem(int direction)
        {
            switch (_inputSelection)
            {
                case 0: _inputPortMode = CycleEnum(_inputPortMode, direction); ApplyInputPortMode(); break;
                case 1: _statusText = "KEYBOARD LAYOUT PC"; break;
                case 2: _statusText = "MOUSE SENSITIVITY NORMAL"; break;
                case 3: _statusText = "GAMEPAD AUTODETECT"; break;
                case 4: _statusText = "INPUT DEFAULTS RESTORED"; _inputPortMode = InputPortMode.MousePort1JoystickPort2; ApplyInputPortMode(); break;
            }
        }

        /// <summary>
        /// Executes the Change Reset Item logic.
        /// </summary>
        private void ChangeResetItem(int direction)
        {
            if (_resetSelection == 0)
            {
                _resetMode = CycleEnum(_resetMode, direction);
            }
        }

        /// <summary>
        /// Executes the Activate Reset Item logic.
        /// </summary>
        private void ActivateResetItem()
        {
            if (_resetSelection == 0)
            {
                _resetMode = CycleEnum(_resetMode, 1);
                return;
            }

            if (_resetSelection == 3)
            {
                CloseOrBackOverlay();
                return;
            }

            bool powerCycle = _resetSelection == 2 || _resetMode == ResetMode.Power;
            _system.Reset(powerCycle);
            ResetAudioOutput();
            if (powerCycle)
            {
                LoadDefaultKickstart();
            }

            ResetEmulationClock();
            _statusText = FormatResetMode(_resetMode) + " RESET COMPLETE";
            _overlayMode = OverlayMode.None;
        }

        /// <summary>
        /// Executes the Activate Media Item logic.
        /// </summary>
        private void ActivateMediaItem()
        {
            if (_mediaSelection == 0)
            {
                RefreshMediaEntries();
                _statusText = "MEDIA LIST REFRESHED";
                return;
            }

            if (_mediaSelection == 1)
            {
                _mountedMedia = MountedMediaInfo.None;
                _system.Bus.FloppyController.EjectDrive0();
                _statusText = "DF0 EJECTED";
                return;
            }

            if (_mediaSelection == 2)
            {
                try
                {
                    _mediaDirectory = Directory.GetParent(_mediaDirectory)?.FullName ?? _mediaDirectory;
                    RefreshMediaEntries();
                }
                catch
                {
                    _statusText = "CANNOT OPEN PARENT";
                }

                return;
            }

            int entryIndex = _mediaSelection - 3;
            if (entryIndex < 0 || entryIndex >= _mediaEntries.Count)
            {
                return;
            }

            string path = _mediaEntries[entryIndex];
            if (Directory.Exists(path))
            {
                _mediaDirectory = path;
                RefreshMediaEntries();
                return;
            }

            try
            {
                AdfImage image = AdfImage.Load(path);
                _system.Bus.FloppyController.InsertDrive0(image, _floppyWriteProtected);
                _mountedMedia = MountedMediaInfo.ForFloppy(0, Path.GetFileName(path), path, _floppyWriteProtected);
                _statusText = "DF0 MOUNTED " + _mountedMedia.DisplayName;
            }
            catch
            {
                _statusText = "DF0 MOUNT FAILED";
            }
        }

        /// <summary>
        /// Executes the Activate ROM Item logic.
        /// </summary>
        private void ActivateRomItem()
        {
            if (_romSelection == 0)
            {
                RefreshRomEntries();
                _statusText = "ROM LIST REFRESHED";
                return;
            }

            if (_romSelection == 1)
            {
                OpenRomDirectory();
                return;
            }

            if (_romSelection == 2)
            {
                _selectedRomPath = string.Empty;
                _settings.CustomKickstartPath = string.Empty;
                AmigaEmulatorSettingsStore.Save(_settings);
                LoadConfiguredKickstart();
                _statusText = "AUTO ROM SELECTED";
                return;
            }

            int entryIndex = _romSelection - 3;
            if (entryIndex < 0 || entryIndex >= _romEntries.Count)
            {
                return;
            }

            KickstartRomInfo info = _romEntries[entryIndex];
            if (info == null || !info.IsUsable)
            {
                _statusText = "ROM NOT USABLE";
                return;
            }

            LoadSelectedKickstart(info.Path);
        }

        /// <summary>
        /// Loads Or Create Save Slot into emulator state.
        /// </summary>
        private void LoadOrCreateSaveSlot()
        {
            SaveStateSlotMetadata slot = _saveSlots[_saveSelection];
            if (slot == null || !slot.HasState)
            {
                SaveCurrentSlot();
                return;
            }

            try
            {
                SaveStateFile.Load(slot.Path, _system);
                _system.PresentedFrameBuffer.CopyTo(Pixels);
                _mountedMedia = slot.MountedMedia ?? MountedMediaInfo.None;
                _statusText = "SAVE SLOT " + (_saveSelection + 1) + " LOADED";
                _overlayMode = OverlayMode.None;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE LOAD FAILED";
                RefreshSaveStateSlots();
            }
        }

        /// <summary>
        /// Saves Current Slot from emulator state.
        /// </summary>
        private void SaveCurrentSlot()
        {
            try
            {
                string savePath = SaveStateFile.GetSlotPath(UserDataPaths.GetSaveDirectory(), _saveSelection);
                uint[] screenshotPixels = new uint[Pixels.Length];
                Array.Copy(Pixels, screenshotPixels, screenshotPixels.Length);
                SaveStateFile.Write(
                    savePath,
                    _system,
                    _mountedMedia,
                    GetCurrentKickstartProfile(),
                    GetSettingsRamProfile(_ramProfile),
                    _accuracyProfile,
                    screenshotPixels,
                    _system.Model.VisibleWidth,
                    _system.Model.VisibleHeight);
                RefreshSaveStateSlots();
                _statusText = "SAVE SLOT " + (_saveSelection + 1) + " WRITTEN";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE FAILED";
            }
        }

        /// <summary>
        /// Deletes the selected savestate slot file.
        /// </summary>
        private void DeleteCurrentSaveSlot()
        {
            SaveStateSlotMetadata slot = _saveSlots[_saveSelection];
            if (slot == null || string.IsNullOrWhiteSpace(slot.Path) || !File.Exists(slot.Path))
            {
                _statusText = "SAVE SLOT EMPTY";
                return;
            }

            try
            {
                File.Delete(slot.Path);
                RefreshSaveStateSlots();
                _statusText = "SAVE SLOT CLEARED";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE DELETE FAILED";
            }
        }

        /// <summary>
        /// Executes the Calculate Cycles For Update logic.
        /// </summary>
        private int CalculateCyclesForUpdate()
        {
            double now = _stopwatch.Elapsed.TotalSeconds;
            double elapsed = now - _lastUpdateSeconds;
            _lastUpdateSeconds = now;

            if (elapsed < 0.0)
            {
                elapsed = 0.0;
            }

            double cyclesPerSecond = GetPalChipCyclesPerSecond();
            double speedMultiplier = _turboMode ? 8.0 : 1.0;
            _pendingChipCycles += elapsed * cyclesPerSecond * speedMultiplier;

            int maxFrameBurst = _turboMode ? 48 : 12;
            int maxCycles = _system.Model.ChipCyclesPerLine * _system.Model.LinesPerFrame * maxFrameBurst;
            int cycles = (int)Math.Min(_pendingChipCycles, maxCycles);
            _pendingChipCycles -= cycles;

            if (!_turboMode && _pendingChipCycles > maxCycles)
            {
                _pendingChipCycles = maxCycles;
            }

            return cycles;
        }

        /// <summary>
        /// Resets the Emulation Clock state.
        /// </summary>
        private void ResetEmulationClock()
        {
            _lastUpdateSeconds = _stopwatch.Elapsed.TotalSeconds;
            _lastPerfSeconds = _lastUpdateSeconds;
            _pendingChipCycles = 0.0;
            _perfChipCycles = 0;
            _smoothedCpuMhz = (_system.Model.ChipClockHz * 2.0) / 1000000.0;
        }

        /// <summary>
        /// Returns the Pal Chip Cycles period Second.
        /// </summary>
        private double GetPalChipCyclesPerSecond()
        {
            return _system.Model.ChipClockHz;
        }

        /// <summary>
        /// Updates Performance Counters from current emulator state.
        /// </summary>
        private void UpdatePerformanceCounters(int chipCycles)
        {
            _perfChipCycles += chipCycles;
            double now = _stopwatch.Elapsed.TotalSeconds;
            double elapsed = now - _lastPerfSeconds;
            if (elapsed < 1.0)
            {
                return;
            }

            _hostSpeedPercent = (_perfChipCycles / (GetPalChipCyclesPerSecond() * elapsed)) * 100.0;
            _smoothedCpuMhz = ((_perfChipCycles * 2.0) / elapsed) / 1000000.0;
            _perfChipCycles = 0;
            _lastPerfSeconds = now;
        }

        /// <summary>
        /// Updates Title Status from current emulator state.
        /// </summary>
        private void UpdateTitleStatus(int chipCycles)
        {
            Text = string.Format(
                "Amiga Emulator {0}{1:F2} MHz - PC:{2:X6} OPC:{3:X4} - FPS:{4} - DF0:{5}",
                _turboMode ? "TURBO " : string.Empty,
                _smoothedCpuMhz,
                _system.Cpu.PC,
                _system.Cpu.CurrentOpcode,
                FPS,
                FormatTitleDriveStatus());
        }

        /// <summary>
        /// Executes the Format Title Drive Status logic.
        /// </summary>
        private string FormatTitleDriveStatus()
        {
            return _mountedMedia.HasMedia ? TrimText(_mountedMedia.DisplayName, 18) : "EMPTY";
        }

        /// <summary>
        /// Applies Accuracy Profile to the current state.
        /// </summary>
        private void ApplyAccuracyProfile(AmigaAccuracyProfile profile)
        {
            _accuracyProfile = profile;
            _settings.AccuracyProfile = profile;
            _system.SetAccuracyOptions(CreateAccuracyOptions(profile));
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "ACCURACY " + FormatAccuracyProfile(profile);
        }

        /// <summary>
        /// Applies RAM Profile to the current state.
        /// </summary>
        private void ApplyRamProfile(RamProfile profile)
        {
            if (_ramProfile == profile && _system != null && _system.Model == GetModelForRamProfile(profile))
            {
                return;
            }

            _ramProfile = profile;
            _settings.RamProfile = GetSettingsRamProfile(profile);
            AmigaEmulatorSettingsStore.Save(_settings);
            RecreateSystemForCurrentProfile();
            _statusText = "RAM " + FormatRamProfile(_ramProfile);
        }

        /// <summary>
        /// Executes the Recreate System For Current Profile logic.
        /// </summary>
        private void RecreateSystemForCurrentProfile()
        {
            MountedMediaInfo media = _mountedMedia;
            AmigaSystem oldSystem = _system;
            DetachAudioFromSystem(oldSystem);
            _system = new AmigaSystem(GetModelForRamProfile(_ramProfile), CreateAccuracyOptions(_accuracyProfile));
            AttachAudioToSystem();
            oldSystem?.Dispose();
            ApplyInputPortMode();

            LoadConfiguredKickstart();
            RemountMedia(media);
            UpdateFrequency = _system.Model.FrameRateHz;
            ResetEmulationClock();
        }

        /// <summary>
        /// Executes the Remount Media logic.
        /// </summary>
        private void RemountMedia(MountedMediaInfo media)
        {
            if (media == null || !media.HasMedia || media.Kind != MountedMediaKind.Adf || string.IsNullOrWhiteSpace(media.HostPath) || !File.Exists(media.HostPath))
            {
                _mountedMedia = MountedMediaInfo.None;
                return;
            }

            try
            {
                AdfImage image = AdfImage.Load(media.HostPath);
                _system.Bus.FloppyController.InsertDrive0(image, media.IsWriteProtected);
                _mountedMedia = MountedMediaInfo.ForFloppy(media.DriveIndex < 0 ? 0 : media.DriveIndex, media.DisplayName, media.HostPath, media.IsWriteProtected);
            }
            catch
            {
                _mountedMedia = media.WithStatus(MountedMediaStatus.Error, "REMOUNT FAILED");
            }
        }

        /// <summary>
        /// Returns the Model For RAM Profile.
        /// </summary>
        private static AmigaModel GetModelForRamProfile(RamProfile profile)
        {
            switch (profile)
            {
                case RamProfile.Chip512:
                    return AmigaModel.A500Pal;
                case RamProfile.Chip1Mb:
                case RamProfile.Chip1MbFast4Mb:
                    return AmigaModel.A500Pal1MbChip;
                default:
                    return AmigaModel.A500Pal512Slow512;
            }
        }

        /// <summary>
        /// Returns the RAM Profile For Model.
        /// </summary>
        private static RamProfile GetRamProfileForModel(AmigaModel model)
        {
            if (model == AmigaModel.A500Pal)
            {
                return RamProfile.Chip512;
            }

            if (model == AmigaModel.A500Pal1MbChip)
            {
                return RamProfile.Chip1Mb;
            }

            return RamProfile.Chip512Slow512;
        }

        /// <summary>
        /// Returns the RAM Profile From Settings.
        /// </summary>
        private static RamProfile GetRamProfileFromSettings(AmigaRamProfile profile, RamProfile fallback)
        {
            switch (profile)
            {
                case AmigaRamProfile.Chip512K:
                    return RamProfile.Chip512;
                case AmigaRamProfile.Chip512KSlow512K:
                    return RamProfile.Chip512Slow512;
                case AmigaRamProfile.Chip1Mb:
                case AmigaRamProfile.Chip2Mb:
                    return RamProfile.Chip1Mb;
                case AmigaRamProfile.Chip2MbFast8Mb:
                    return RamProfile.Chip1MbFast4Mb;
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Returns the Settings RAM Profile.
        /// </summary>
        private static AmigaRamProfile GetSettingsRamProfile(RamProfile profile)
        {
            switch (profile)
            {
                case RamProfile.Chip512Slow512:
                    return AmigaRamProfile.Chip512KSlow512K;
                case RamProfile.Chip1Mb:
                    return AmigaRamProfile.Chip1Mb;
                case RamProfile.Chip1MbFast4Mb:
                    return AmigaRamProfile.Chip2MbFast8Mb;
                default:
                    return AmigaRamProfile.Chip512K;
            }
        }

        /// <summary>
        /// Creates the Accuracy Options.
        /// </summary>
        private static AmigaAccuracyOptions CreateAccuracyOptions(AmigaAccuracyProfile profile)
        {
            switch (profile)
            {
                case AmigaAccuracyProfile.Strict:
                    return AmigaAccuracyOptions.Strict.Clone();
                case AmigaAccuracyProfile.Compatible:
                    return AmigaAccuracyOptions.Compatible.Clone();
                default:
                    return AmigaAccuracyOptions.Fast.Clone();
            }
        }

        /// <summary>
        /// Executes the Refresh Media Entries logic.
        /// </summary>
        private void RefreshMediaEntries()
        {
            _mediaEntries.Clear();
            if (string.IsNullOrWhiteSpace(_mediaDirectory) || !Directory.Exists(_mediaDirectory))
            {
                _mediaDirectory = GetInitialMediaDirectory();
            }

            try
            {
                foreach (string directory in Directory.GetDirectories(_mediaDirectory))
                {
                    _mediaEntries.Add(directory);
                }

                foreach (string file in Directory.GetFiles(_mediaDirectory, "*.adf"))
                {
                    _mediaEntries.Add(file);
                }
            }
            catch
            {
                _statusText = "MEDIA DIRECTORY UNAVAILABLE";
            }

            _mediaEntries.Sort(StringComparer.OrdinalIgnoreCase);
            _mediaSelection = Math.Min(_mediaSelection, Math.Max(0, _mediaEntries.Count + 2));
        }

        /// <summary>
        /// Executes the Refresh ROM Entries logic.
        /// </summary>
        private void RefreshRomEntries()
        {
            _romEntries.Clear();
            Directory.CreateDirectory(UserDataPaths.GetRomDirectory());
            foreach (KickstartRomInfo info in KickstartRomResolver.FindAvailableKickstartRoms())
            {
                _romEntries.Add(info);
            }

            _romSelection = Math.Min(_romSelection, Math.Max(0, _romEntries.Count + 2));
        }

        /// <summary>
        /// Reloads savestate slot metadata from the per-user save directory.
        /// </summary>
        private void RefreshSaveStateSlots()
        {
            string saveDirectory = UserDataPaths.GetSaveDirectory();
            Directory.CreateDirectory(saveDirectory);
            for (int index = 0; index < _saveSlots.Length; index++)
            {
                string path = SaveStateFile.GetSlotPath(saveDirectory, index);
                if (!File.Exists(path))
                {
                    _saveSlots[index] = SaveStateSlotMetadata.Empty(index, path);
                    continue;
                }

                try
                {
                    _saveSlots[index] = SaveStateFile.ReadMetadata(path, index);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    _saveSlots[index] = new SaveStateSlotMetadata
                    {
                        SlotIndex = index,
                        Status = SaveStateSlotStatus.Corrupt,
                        Path = path,
                        DisplayName = "CORRUPT"
                    };
                }
            }
        }

        /// <summary>
        /// Executes the Open ROM Directory logic.
        /// </summary>
        private void OpenRomDirectory()
        {
            try
            {
                string directory = UserDataPaths.GetRomDirectory();
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
                _statusText = "ROM FOLDER OPENED";
            }
            catch
            {
                _statusText = "CANNOT OPEN ROM FOLDER";
            }
        }

        /// <summary>
        /// Draws Settings Overlay into the presentation surface.
        /// </summary>
        private void DrawSettingsOverlay(Graphics graphics)
        {
            DrawMenu(graphics, "SETTINGS", "ENTER CHANGE  LEFT/RIGHT ADJUST  ESC BACK", new[]
            {
                MenuLine("MASTER VOLUME", FormatPercent(_masterVolume)),
                MenuLine("AUDIO", _audioEnabled ? "ON" : "OFF"),
                MenuLine("PAULA FILTER", FormatPaulaFilter(_paulaFilterMode)),
                MenuLine("VIDEO FILTER", FormatVideoFilter(_videoFilterMode)),
                MenuLine("VIDEO UPSCALE", FormatVideoUpscale(_videoUpscaleMode)),
                MenuLine("RENDER FPS", FormatRenderLimit(_renderLimitMode)),
                MenuLine("DISPLAY", _fullscreen ? "FULLSCREEN" : "WINDOW"),
                MenuLine("TURBO", _turboMode ? "ON" : "OFF"),
                MenuLine("FLOPPY SPEED", FormatFloppySpeed(_floppySpeedMode)),
                MenuLine("INPUT PORTS", FormatInputPorts(_inputPortMode)),
                MenuLine("RAM", FormatRamProfile(_ramProfile)),
                MenuLine("DEBUG OVERLAY", _debugOverlayVisible ? "ON" : "OFF"),
                MenuLine("DRIVE OVERLAY", _driveOverlayVisible ? "ON" : "OFF"),
                MenuLine("KICKSTART", _romStatusText),
                MenuLine("ACCURACY", FormatAccuracyProfile(_accuracyProfile)),
                MenuLine("RESET MODE", FormatResetMode(_resetMode)),
                MenuLine("RESET NOW", "OPEN CONFIRM")
            }, _settingsSelection);
        }

        /// <summary>
        /// Draws Main Menu Overlay into the presentation surface.
        /// </summary>
        private void DrawMainMenuOverlay(Graphics graphics)
        {
            var lines = new string[_mainMenuItems.Length];
            for (int index = 0; index < lines.Length; index++)
            {
                string value = string.Empty;
                if (index == 1)
                {
                    value = _mountedMedia.HasMedia ? _mountedMedia.DisplayName : "DF0 EMPTY";
                }
                else if (index == 2)
                {
                    value = FormatRamProfile(_ramProfile);
                }

                lines[index] = MenuLine(_mainMenuItems[index], value);
            }

            DrawMenu(graphics, "MAIN MENU", "ENTER OPEN  F9 TURBO  F11 FULLSCREEN  F12 SAVES  ESC CLOSE", lines, _mainSelection);
        }

        /// <summary>
        /// Draws Display Overlay into the presentation surface.
        /// </summary>
        private void DrawDisplayOverlay(Graphics graphics)
        {
            DrawMenu(graphics, "DISPLAY", "ENTER CHANGE  LEFT/RIGHT ADJUST  ESC BACK", new[]
            {
                MenuLine("VIDEO FILTER", FormatVideoFilter(_videoFilterMode)),
                MenuLine("VIDEO UPSCALE", FormatVideoUpscale(_videoUpscaleMode)),
                MenuLine("RENDER FPS", FormatRenderLimit(_renderLimitMode)),
                MenuLine("WINDOW", _fullscreen ? "FULLSCREEN" : "WINDOW"),
                MenuLine("DEBUG OVERLAY", _debugOverlayVisible ? "ON" : "OFF")
            }, _displaySelection);
        }

        /// <summary>
        /// Draws Media Overlay into the presentation surface.
        /// </summary>
        private void DrawMediaOverlay(Graphics graphics)
        {
            var lines = new List<string>
            {
                MenuLine("REFRESH", FormatOverlayPath(_mediaDirectory, 34)),
                MenuLine("EJECT DF0", _mountedMedia.HasMedia ? _mountedMedia.DisplayName : "EMPTY"),
                MenuLine("PARENT DIR", "..")
            };

            /// <summary>
            /// Defines the max Visible Entries constant.
            /// </summary>
            const int maxVisibleEntries = 10;
            int firstEntry = GetScrolledEntryStart(_mediaSelection - 3, _mediaEntries.Count, maxVisibleEntries);
            int visibleEntries = Math.Min(maxVisibleEntries, Math.Max(0, _mediaEntries.Count - firstEntry));
            for (int index = 0; index < visibleEntries; index++)
            {
                string path = _mediaEntries[firstEntry + index];
                string label = Directory.Exists(path) ? "[DIR]" : "ADF";
                lines.Add(MenuLine(label, Path.GetFileName(path)));
            }

            int visibleSelection = _mediaSelection;
            if (_mediaSelection >= 3)
            {
                visibleSelection = 3 + (_mediaSelection - 3 - firstEntry);
            }

            if (_mediaEntries.Count == 0)
            {
                lines.Add(MenuLine("NO ADF FILES", "PLACE DISKS IN " + FormatOverlayPath(_mediaDirectory, 24)));
            }
            else if (_mediaEntries.Count > maxVisibleEntries)
            {
                lines.Add(MenuLine("SCROLL", (firstEntry + 1) + "-" + (firstEntry + visibleEntries) + " / " + _mediaEntries.Count));
            }

            DrawMenu(graphics, "MEDIA", "ENTER MOUNT/OPEN  UP/DOWN SCROLL  ESC BACK", lines.ToArray(), visibleSelection);
        }

        /// <summary>
        /// Draws ROM Overlay into the presentation surface.
        /// </summary>
        private void DrawRomOverlay(Graphics graphics)
        {
            var lines = new List<string>
            {
                MenuLine("REFRESH", _romEntries.Count + " ROMS"),
                MenuLine("ROM FOLDER", FormatOverlayPath(UserDataPaths.GetRomDirectory(), 34)),
                MenuLine("AUTO KICKSTART", string.IsNullOrWhiteSpace(_selectedRomPath) ? "ACTIVE" : "USE DEFAULT")
            };

            /// <summary>
            /// Defines the max Visible Entries constant.
            /// </summary>
            const int maxVisibleEntries = 12;
            int firstEntry = GetScrolledEntryStart(_romSelection - 3, _romEntries.Count, maxVisibleEntries);
            int visibleEntries = Math.Min(maxVisibleEntries, Math.Max(0, _romEntries.Count - firstEntry));
            for (int index = 0; index < visibleEntries; index++)
            {
                KickstartRomInfo info = _romEntries[firstEntry + index];
                string label = IsSelectedRom(info.Path) ? "* ROM" : "ROM";
                string value = FormatRomListValue(info);
                lines.Add(MenuLine(label, value));
            }

            int visibleSelection = _romSelection;
            if (_romSelection >= 3)
            {
                visibleSelection = 3 + (_romSelection - 3 - firstEntry);
            }

            if (_romEntries.Count == 0)
            {
                lines.Add(MenuLine("NO ROMS", "PLACE ROMS IN " + FormatOverlayPath(UserDataPaths.GetRomDirectory(), 24)));
            }
            else if (_romEntries.Count > maxVisibleEntries)
            {
                lines.Add(MenuLine("SCROLL", (firstEntry + 1) + "-" + (firstEntry + visibleEntries) + " / " + _romEntries.Count));
            }

            DrawMenu(graphics, "KICKSTART ROM", "ENTER SELECT/OPEN  UP/DOWN SCROLL  ESC BACK", lines.ToArray(), visibleSelection);
        }

        /// <summary>
        /// Draws Machine Overlay into the presentation surface.
        /// </summary>
        private void DrawMachineOverlay(Graphics graphics)
        {
            DrawMenu(graphics, "MACHINE", "ENTER CHANGE  ESC BACK", new[]
            {
                MenuLine("MODEL", _system.Model.Name),
                MenuLine("RAM", FormatRamProfile(_ramProfile)),
                MenuLine("FLOPPY SPEED", FormatFloppySpeed(_floppySpeedMode)),
                MenuLine("DISK WRITE", _floppyWriteProtected ? "PROTECTED" : "ENABLED"),
                MenuLine("KICKSTART", _romStatusText),
                MenuLine("ACCURACY", FormatAccuracyProfile(_accuracyProfile)),
                MenuLine("RESET MODE", FormatResetMode(_resetMode)),
                MenuLine("RESET NOW", "OPEN CONFIRM")
            }, _machineSelection);
        }

        /// <summary>
        /// Draws Input Overlay into the presentation surface.
        /// </summary>
        private void DrawInputOverlay(Graphics graphics)
        {
            DrawMenu(graphics, "INPUT", "ENTER CHANGE  ESC BACK", new[]
            {
                MenuLine("PORT MODE", FormatInputPorts(_inputPortMode)),
                MenuLine("KEYBOARD", "PC LAYOUT"),
                MenuLine("MOUSE", "NORMAL"),
                MenuLine("GAMEPAD", "AUTO"),
                MenuLine("DEFAULTS", "RESTORE")
            }, _inputSelection);
        }

        /// <summary>
        /// Draws Reset Overlay into the presentation surface.
        /// </summary>
        private void DrawResetOverlay(Graphics graphics)
        {
            DrawMenu(graphics, "RESET", "ENTER ACCEPT  ESC BACK", new[]
            {
                MenuLine("RESET MODE", FormatResetMode(_resetMode)),
                MenuLine("APPLY RESET", "RAM PRESERVED WHERE POSSIBLE"),
                MenuLine("POWER CYCLE", "CLEAR WRITABLE MEMORY"),
                MenuLine("CANCEL", "BACK")
            }, _resetSelection);
        }

        /// <summary>
        /// Draws Save States Overlay into the presentation surface.
        /// </summary>
        private void DrawSaveStatesOverlay(Graphics graphics)
        {
            var lines = new string[_saveSlots.Length];
            for (int index = 0; index < _saveSlots.Length; index++)
            {
                SaveStateSlotMetadata slot = _saveSlots[index];
                string value = slot == null ? "EMPTY" : slot.DisplayName;
                if (slot != null && slot.Status == SaveStateSlotStatus.Corrupt)
                {
                    value = "CORRUPT";
                }

                lines[index] = MenuLine("SLOT " + (index + 1), string.IsNullOrWhiteSpace(value) ? "EMPTY" : value);
            }

            DrawMenu(graphics, "SAVE STATES", "ENTER LOAD/CREATE  S SAVE  DEL CLEAR  ESC CLOSE", lines, _saveSelection);
        }

        /// <summary>
        /// Draws Menu into the presentation surface.
        /// </summary>
        private void DrawMenu(Graphics graphics, string title, string help, string[] lines, int selection)
        {
            int width = Math.Min(760, Math.Max(420, ClientSize.Width - 32));
            int height = 92 + (lines.Length * 24);
            int x = (ClientSize.Width - width) / 2;
            int y = Math.Max(18, (ClientSize.Height - height) / 2);

            using (var brush = new SolidBrush(Color.FromArgb(204, 10, 13, 24)))
            using (var border = new Pen(Color.FromArgb(255, 255, 220, 130)))
            using (var dimBorder = new Pen(Color.FromArgb(255, 94, 126, 168)))
            using (var titleBrush = new SolidBrush(Color.FromArgb(255, 255, 230, 150)))
            using (var helpBrush = new SolidBrush(Color.FromArgb(255, 176, 198, 218)))
            using (var textBrush = new SolidBrush(Color.FromArgb(255, 235, 242, 250)))
            using (var valueBrush = new SolidBrush(Color.FromArgb(255, 182, 214, 108)))
            using (var selectedBrush = new SolidBrush(Color.FromArgb(255, 255, 243, 168)))
            using (var rowBrush = new SolidBrush(Color.FromArgb(95, 75, 96, 128)))
            {
                graphics.FillRectangle(brush, x, y, width, height);
                graphics.DrawRectangle(border, x, y, width, height);
                graphics.DrawLine(dimBorder, x + 14, y + 46, x + width - 14, y + 46);
                graphics.DrawString(title, _titleFont, titleBrush, x + 18, y + 14);
                graphics.DrawString(help, _smallFont, helpBrush, x + 20, y + height - 28);

                int rowY = y + 58;
                for (int index = 0; index < lines.Length; index++)
                {
                    bool selected = index == selection;
                    if (selected)
                    {
                        graphics.FillRectangle(rowBrush, x + 12, rowY + (index * 24) - 2, width - 24, 23);
                    }

                    string line = lines[index] ?? string.Empty;
                    string left = line;
                    string right = string.Empty;
                    int separator = line.IndexOf('|');
                    if (separator >= 0)
                    {
                        left = line.Substring(0, separator);
                        right = line.Substring(separator + 1);
                    }

                    graphics.DrawString((selected ? "> " : "  ") + left, _smallFont, selected ? selectedBrush : textBrush, x + 22, rowY + (index * 24));
                    if (!string.IsNullOrWhiteSpace(right))
                    {
                        graphics.DrawString(TrimText(right, 40), _smallFont, selected ? selectedBrush : valueBrush, x + 235, rowY + (index * 24));
                    }
                }

                graphics.DrawString("STATUS " + TrimText(_statusText, 58), _smallFont, valueBrush, x + 20, y + height - 48);
            }
        }

        /// <summary>
        /// Draws Debug Overlay into the presentation surface.
        /// </summary>
        private void DrawDebugOverlay(Graphics graphics)
        {
            string[] lines =
            {
                "AMIGA DEBUG",
                _system.GetCpuDebugInfo(),
                _system.GetBeamDebugInfo(),
                _system.GetDmaDebugInfo(),
                "AUDIO " + (_audioEnabled ? "ON" : "OFF") + " HOST " + (_audioOutput.Available ? "OK" : "FAIL") + " FRAMES " + _audioOutput.SubmittedFrames + " DROP " + _audioOutput.DroppedFrames,
                "SECONDS " + _lastSeconds.ToString("F1") + " SPEED " + _hostSpeedPercent.ToString("F0") + "% " + FormatAccuracyProfile(_accuracyProfile) + " TURBO " + (_turboMode ? "ON" : "OFF")
            };

            using (var brush = new SolidBrush(Color.FromArgb(204, 5, 8, 14)))
            using (var pen = new Pen(Color.FromArgb(210, 165, 210, 255)))
            using (var text = new SolidBrush(Color.FromArgb(240, 245, 250)))
            {
                var rect = new Rectangle(10, 10, 620, 116);
                graphics.FillRectangle(brush, rect);
                graphics.DrawRectangle(pen, rect);
                for (int index = 0; index < lines.Length; index++)
                {
                    graphics.DrawString(lines[index], _smallFont, text, rect.X + 10, rect.Y + 8 + (index * 16));
                }
            }
        }

        /// <summary>
        /// Draws Drive Footer into the presentation surface.
        /// </summary>
        private void DrawDriveFooter(Graphics graphics)
        {
            string text = _mountedMedia.HasMedia ? "DF0 " + _mountedMedia.DisplayName : "DF0 EMPTY";
            bool ledOn = _system != null && _system.Bus.FloppyController.Drive0.ActivityLedOn;
            using (var brush = new SolidBrush(Color.FromArgb(204, 5, 8, 14)))
            using (var pen = new Pen(Color.FromArgb(220, 108, 214, 182)))
            using (var textBrush = new SolidBrush(Color.FromArgb(255, 210, 245, 230)))
            using (var ledOuterPen = new Pen(Color.FromArgb(230, 22, 28, 24)))
            using (var ledOffBrush = new SolidBrush(Color.FromArgb(255, 42, 26, 22)))
            using (var ledOnBrush = new SolidBrush(Color.FromArgb(255, 244, 64, 34)))
            using (var ledGlowBrush = new SolidBrush(Color.FromArgb(92, 255, 92, 48)))
            {
                int width = 280;
                int height = 28;
                int x = ClientSize.Width - width - 12;
                int y = ClientSize.Height - height - 12;
                graphics.FillRectangle(brush, x, y, width, height);
                graphics.DrawRectangle(pen, x, y, width, height);
                var ledRect = new Rectangle(x + 10, y + 8, 12, 12);
                if (ledOn)
                {
                    graphics.FillEllipse(ledGlowBrush, ledRect.X - 4, ledRect.Y - 4, ledRect.Width + 8, ledRect.Height + 8);
                }

                graphics.FillEllipse(ledOn ? ledOnBrush : ledOffBrush, ledRect);
                graphics.DrawEllipse(ledOuterPen, ledRect);
                graphics.DrawString(text, _smallFont, textBrush, x + 30, y + 7);
            }
        }

        /// <summary>
        /// Executes the Adjust Volume logic.
        /// </summary>
        private void AdjustVolume(int direction)
        {
            _masterVolume = Math.Max(0.0f, Math.Min(1.5f, _masterVolume + (direction * 0.05f)));
        }

        /// <summary>
        /// Executes the Toggle Audio logic.
        /// </summary>
        private void ToggleAudio()
        {
            _audioEnabled = !_audioEnabled;
            if (!_audioEnabled)
            {
                ResetAudioOutput();
            }
        }

        /// <summary>
        /// Attaches Audio To System to this component.
        /// </summary>
        private void AttachAudioToSystem()
        {
            if (_system != null)
            {
                _system.Paula.AudioSampleGenerated += HandlePaulaSampleGenerated;
            }

            ResetAudioOutput();
        }

        /// <summary>
        /// Executes the Detach Audio From System logic.
        /// </summary>
        private void DetachAudioFromSystem(AmigaSystem system)
        {
            if (system != null)
            {
                system.Paula.AudioSampleGenerated -= HandlePaulaSampleGenerated;
            }
        }

        /// <summary>
        /// Resets the Audio Output state.
        /// </summary>
        private void ResetAudioOutput()
        {
            _audioFilterLeft = 0.0f;
            _audioFilterRight = 0.0f;
            _audioOutput.Clear();
        }

        /// <summary>
        /// Handles Paula Sample Generated from the host UI or emulator core.
        /// </summary>
        private void HandlePaulaSampleGenerated(float left, float right)
        {
            if (!_audioEnabled)
            {
                return;
            }

            ApplyPaulaFilter(ref left, ref right);
            _audioOutput.Submit(left, right, _masterVolume);
        }

        /// <summary>
        /// Applies Paula Filter to the current state.
        /// </summary>
        private void ApplyPaulaFilter(ref float left, ref float right)
        {
            float alpha;
            switch (_paulaFilterMode)
            {
                case PaulaFilterMode.Led:
                    alpha = 0.06f;
                    break;
                case PaulaFilterMode.LowPass:
                    alpha = 0.12f;
                    break;
                default:
                    _audioFilterLeft = left;
                    _audioFilterRight = right;
                    return;
            }

            _audioFilterLeft += (left - _audioFilterLeft) * alpha;
            _audioFilterRight += (right - _audioFilterRight) * alpha;
            left = _audioFilterLeft;
            right = _audioFilterRight;
        }

        /// <summary>
        /// Executes the Cycle Render Limit logic.
        /// </summary>
        private void CycleRenderLimit()
        {
            switch (_renderLimitMode)
            {
                case RenderLimitMode.Hz50:
                    _renderLimitMode = RenderLimitMode.Hz120;
                    break;
                case RenderLimitMode.Hz120:
                    _renderLimitMode = RenderLimitMode.Unlimited;
                    break;
                default:
                    _renderLimitMode = RenderLimitMode.Hz50;
                    break;
            }

            UpdateFrequency = GetRenderUpdateFrequency(_renderLimitMode);
            _settings.RenderFps = GetSettingsRenderFps(_renderLimitMode);
            AmigaEmulatorSettingsStore.Save(_settings);
            ResetEmulationClock();

            _statusText = "RENDER FPS " + FormatRenderLimit(_renderLimitMode);
        }

        /// <summary>
        /// Returns the Render Update Frequency.
        /// </summary>
        private double GetRenderUpdateFrequency(RenderLimitMode mode)
        {
            switch (mode)
            {
                case RenderLimitMode.Hz120:
                    return 120.0;
                case RenderLimitMode.Unlimited:
                    return 0.0;
                default:
                    return _system != null ? _system.Model.FrameRateHz : 50.0;
            }
        }

        /// <summary>
        /// Applies Video Filter Mode to the current state.
        /// </summary>
        private void ApplyVideoFilterMode(VideoFilterMode mode)
        {
            _videoFilterMode = mode;
            _settings.VideoFilter = GetSettingsVideoFilter(mode);
            AmigaEmulatorSettingsStore.Save(_settings);
            ApplyVideoPresentation();
            _statusText = "VIDEO FILTER " + FormatVideoFilter(mode);
        }

        /// <summary>
        /// Applies Video Upscale Mode to the current state.
        /// </summary>
        private void ApplyVideoUpscaleMode(VideoUpscaleMode mode)
        {
            _videoUpscaleMode = mode;
            _settings.UpscaleMode = GetSettingsVideoUpscale(mode);
            AmigaEmulatorSettingsStore.Save(_settings);
            ApplyVideoPresentation();
            _statusText = "VIDEO UPSCALE " + FormatVideoUpscale(mode);
        }

        /// <summary>
        /// Applies Video Presentation to the current state.
        /// </summary>
        private void ApplyVideoPresentation()
        {
            SetPresentationEffects(ToSharpPixelsFilter(_videoFilterMode), ToSharpPixelsUpscale(_videoUpscaleMode));
        }

        /// <summary>
        /// Applies Paula Filter Mode to the current state.
        /// </summary>
        private void ApplyPaulaFilterMode(PaulaFilterMode mode)
        {
            _paulaFilterMode = mode;
            _settings.PaulaFilter = GetSettingsPaulaFilter(mode);
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "PAULA FILTER " + FormatPaulaFilter(mode);
        }

        /// <summary>
        /// Executes the Toggle Drive Overlay logic.
        /// </summary>
        private void ToggleDriveOverlay()
        {
            _driveOverlayVisible = !_driveOverlayVisible;
            _settings.DriveOverlayEnabled = _driveOverlayVisible;
            AmigaEmulatorSettingsStore.Save(_settings);
        }

        /// <summary>
        /// Executes the Toggle Fullscreen logic.
        /// </summary>
        private void ToggleFullscreen()
        {
            if (_fullscreen)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                _fullscreen = false;
                _settings.Fullscreen = false;
                AmigaEmulatorSettingsStore.Save(_settings);
                _statusText = "WINDOW MODE";
                return;
            }

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _fullscreen = true;
            _settings.Fullscreen = true;
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "FULLSCREEN";
        }

        /// <summary>
        /// Executes the Toggle Turbo Mode logic.
        /// </summary>
        private void ToggleTurboMode()
        {
            _turboMode = !_turboMode;
            _settings.TurboMode = _turboMode;
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = _turboMode ? "TURBO ON" : "TURBO OFF";
        }

        /// <summary>
        /// Executes the Reload Kickstart logic.
        /// </summary>
        private void ReloadKickstart()
        {
            LoadConfiguredKickstart();
            _statusText = _romStatusText;
        }

        /// <summary>
        /// Loads Configured Kickstart into emulator state.
        /// </summary>
        private void LoadConfiguredKickstart()
        {
            if (!string.IsNullOrWhiteSpace(_selectedRomPath) && File.Exists(_selectedRomPath))
            {
                LoadSelectedKickstart(_selectedRomPath);
                return;
            }

            LoadDefaultKickstart();
        }

        /// <summary>
        /// Loads Selected Kickstart into emulator state.
        /// </summary>
        private void LoadSelectedKickstart(string path)
        {
            if (KickstartRomResolver.TryLoadKickstart(path, out byte[] romBytes, out KickstartRomInfo info))
            {
                _system.LoadKickstart(romBytes);
                ResetEmulationClock();
                _selectedRomPath = info.Path;
                _settings.CustomKickstartPath = info.Path;
                AmigaEmulatorSettingsStore.Save(_settings);
                _romStatusText = FormatRomStatus(info);
                _statusText = "ROM LOADED " + Path.GetFileName(info.Path);
                return;
            }

            _romStatusText = info != null && !string.IsNullOrWhiteSpace(info.Error)
                ? "ROM ERROR - " + info.Error
                : "ROM ERROR";
            _statusText = _romStatusText;
        }

        /// <summary>
        /// Loads Default Kickstart into emulator state.
        /// </summary>
        private void LoadDefaultKickstart()
        {
            if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo info))
            {
                _system.LoadKickstart(romBytes);
                ResetEmulationClock();
                _romStatusText = FormatRomStatus(info);
                return;
            }

            _romStatusText = info != null && !string.IsNullOrWhiteSpace(info.Error)
                ? "MISSING - " + info.Error
                : "MISSING";
        }

        /// <summary>
        /// Executes the Format ROM Status logic.
        /// </summary>
        private static string FormatRomStatus(KickstartRomInfo info)
        {
            if (info == null)
            {
                return "MISSING";
            }

            string kind = string.IsNullOrWhiteSpace(info.Kind) ? "Kickstart" : info.Kind;
            return kind + " " + Path.GetFileName(info.Path);
        }

        /// <summary>
        /// Returns whether Selected ROM is true.
        /// </summary>
        private bool IsSelectedRom(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(_selectedRomPath))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(_selectedRomPath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the Kickstart profile currently represented by the overlay settings.
        /// </summary>
        private AmigaKickstart GetCurrentKickstartProfile()
        {
            return string.IsNullOrWhiteSpace(_selectedRomPath) ? AmigaKickstart.Auto : AmigaKickstart.Custom;
        }

        /// <summary>
        /// Executes the Format ROM List Value logic.
        /// </summary>
        private static string FormatRomListValue(KickstartRomInfo info)
        {
            if (info == null)
            {
                return "UNKNOWN";
            }

            string name = Path.GetFileName(info.Path);
            if (!info.IsUsable)
            {
                return name + "  INVALID";
            }

            string kind = string.IsNullOrWhiteSpace(info.Kind) ? "Kickstart" : info.Kind;
            return name + "  " + kind;
        }

        /// <summary>
        /// Returns the Scrolled Entry Start.
        /// </summary>
        private static int GetScrolledEntryStart(int selectedEntry, int totalEntries, int maxVisibleEntries)
        {
            if (totalEntries <= maxVisibleEntries || maxVisibleEntries <= 0)
            {
                return 0;
            }

            int selected = Math.Max(0, Math.Min(totalEntries - 1, selectedEntry));
            int start = selected - (maxVisibleEntries / 2);
            int maxStart = Math.Max(0, totalEntries - maxVisibleEntries);
            if (start < 0)
            {
                return 0;
            }

            return start > maxStart ? maxStart : start;
        }

        /// <summary>
        /// Executes the struct logic.
        /// </summary>
        private static T CycleEnum<T>(T value, int direction) where T : struct
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            int index = Array.IndexOf(values, value);
            if (index < 0)
            {
                index = 0;
            }

            index = (index + direction + values.Length) % values.Length;
            return values[index];
        }

        /// <summary>
        /// Executes the Menu Line logic.
        /// </summary>
        private static string MenuLine(string label, string value)
        {
            return label + "|" + (value ?? string.Empty);
        }

        /// <summary>
        /// Executes the Format Percent logic.
        /// </summary>
        private static string FormatPercent(float value)
        {
            return ((int)Math.Round(value * 100.0f)).ToString() + "%";
        }

        /// <summary>
        /// Executes the Format Render Limit logic.
        /// </summary>
        private static string FormatRenderLimit(RenderLimitMode mode)
        {
            switch (mode)
            {
                case RenderLimitMode.Hz120: return "120 HZ";
                case RenderLimitMode.Unlimited: return "UNLIMITED";
                default: return "50 HZ";
            }
        }

        /// <summary>
        /// Executes the Format Video Filter logic.
        /// </summary>
        private static string FormatVideoFilter(VideoFilterMode mode)
        {
            switch (mode)
            {
                case VideoFilterMode.Crt: return "CRT";
                case VideoFilterMode.Tv: return "TV";
                default: return "SHARP";
            }
        }

        /// <summary>
        /// Executes the Format Video Upscale logic.
        /// </summary>
        private static string FormatVideoUpscale(VideoUpscaleMode mode)
        {
            switch (mode)
            {
                case VideoUpscaleMode.Scale2x: return "SCALE2X";
                case VideoUpscaleMode.Scale3x: return "SCALE3X";
                case VideoUpscaleMode.Hq2x: return "HQ2X";
                case VideoUpscaleMode.Hq3x: return "HQ3X";
                case VideoUpscaleMode.Clean4: return "CLEAN4";
                default: return "NONE";
            }
        }

        /// <summary>
        /// Executes the Format Paula Filter logic.
        /// </summary>
        private static string FormatPaulaFilter(PaulaFilterMode mode)
        {
            switch (mode)
            {
                case PaulaFilterMode.Led: return "LED";
                case PaulaFilterMode.LowPass: return "LOWPASS";
                default: return "OFF";
            }
        }

        /// <summary>
        /// Executes the To Sharp Pixels Filter logic.
        /// </summary>
        private static SharpPixelsGpuFilterMode ToSharpPixelsFilter(VideoFilterMode mode)
        {
            switch (mode)
            {
                case VideoFilterMode.Crt: return SharpPixelsGpuFilterMode.Crt;
                case VideoFilterMode.Tv: return SharpPixelsGpuFilterMode.Tv;
                default: return SharpPixelsGpuFilterMode.Sharp;
            }
        }

        /// <summary>
        /// Executes the To Sharp Pixels Upscale logic.
        /// </summary>
        private static SharpPixelsGpuUpscaleMode ToSharpPixelsUpscale(VideoUpscaleMode mode)
        {
            switch (mode)
            {
                case VideoUpscaleMode.Scale2x: return SharpPixelsGpuUpscaleMode.Scale2x;
                case VideoUpscaleMode.Scale3x: return SharpPixelsGpuUpscaleMode.Scale3x;
                case VideoUpscaleMode.Hq2x: return SharpPixelsGpuUpscaleMode.Hq2x;
                case VideoUpscaleMode.Hq3x: return SharpPixelsGpuUpscaleMode.Hq3x;
                case VideoUpscaleMode.Clean4: return SharpPixelsGpuUpscaleMode.Clean4;
                default: return SharpPixelsGpuUpscaleMode.None;
            }
        }

        /// <summary>
        /// Returns the Video Filter From Settings.
        /// </summary>
        private static VideoFilterMode GetVideoFilterFromSettings(AmigaVideoFilter mode)
        {
            switch (mode)
            {
                case AmigaVideoFilter.Crt: return VideoFilterMode.Crt;
                case AmigaVideoFilter.Tv: return VideoFilterMode.Tv;
                default: return VideoFilterMode.Sharp;
            }
        }

        /// <summary>
        /// Returns the Settings Video Filter.
        /// </summary>
        private static AmigaVideoFilter GetSettingsVideoFilter(VideoFilterMode mode)
        {
            switch (mode)
            {
                case VideoFilterMode.Crt: return AmigaVideoFilter.Crt;
                case VideoFilterMode.Tv: return AmigaVideoFilter.Tv;
                default: return AmigaVideoFilter.Sharp;
            }
        }

        /// <summary>
        /// Returns the Video Upscale From Settings.
        /// </summary>
        private static VideoUpscaleMode GetVideoUpscaleFromSettings(AmigaUpscaleMode mode)
        {
            switch (mode)
            {
                case AmigaUpscaleMode.Scale2x: return VideoUpscaleMode.Scale2x;
                case AmigaUpscaleMode.Scale3x: return VideoUpscaleMode.Scale3x;
                case AmigaUpscaleMode.Hq2x: return VideoUpscaleMode.Hq2x;
                case AmigaUpscaleMode.Hq3x: return VideoUpscaleMode.Hq3x;
                case AmigaUpscaleMode.Clean4: return VideoUpscaleMode.Clean4;
                default: return VideoUpscaleMode.None;
            }
        }

        /// <summary>
        /// Returns the Settings Video Upscale.
        /// </summary>
        private static AmigaUpscaleMode GetSettingsVideoUpscale(VideoUpscaleMode mode)
        {
            switch (mode)
            {
                case VideoUpscaleMode.Scale2x: return AmigaUpscaleMode.Scale2x;
                case VideoUpscaleMode.Scale3x: return AmigaUpscaleMode.Scale3x;
                case VideoUpscaleMode.Hq2x: return AmigaUpscaleMode.Hq2x;
                case VideoUpscaleMode.Hq3x: return AmigaUpscaleMode.Hq3x;
                case VideoUpscaleMode.Clean4: return AmigaUpscaleMode.Clean4;
                default: return AmigaUpscaleMode.None;
            }
        }

        /// <summary>
        /// Returns the Render Limit From Settings.
        /// </summary>
        private static RenderLimitMode GetRenderLimitFromSettings(AmigaRenderFps mode)
        {
            switch (mode)
            {
                case AmigaRenderFps.Hz120: return RenderLimitMode.Hz120;
                case AmigaRenderFps.Unlimited: return RenderLimitMode.Unlimited;
                default: return RenderLimitMode.Hz50;
            }
        }

        /// <summary>
        /// Returns the Settings Render Fps.
        /// </summary>
        private static AmigaRenderFps GetSettingsRenderFps(RenderLimitMode mode)
        {
            switch (mode)
            {
                case RenderLimitMode.Hz120: return AmigaRenderFps.Hz120;
                case RenderLimitMode.Unlimited: return AmigaRenderFps.Unlimited;
                default: return AmigaRenderFps.Native;
            }
        }

        /// <summary>
        /// Returns the Paula Filter From Settings.
        /// </summary>
        private static PaulaFilterMode GetPaulaFilterFromSettings(AmigaPaulaFilter mode)
        {
            switch (mode)
            {
                case AmigaPaulaFilter.Off: return PaulaFilterMode.Off;
                case AmigaPaulaFilter.A1200: return PaulaFilterMode.LowPass;
                case AmigaPaulaFilter.Led:
                case AmigaPaulaFilter.A500:
                default:
                    return PaulaFilterMode.Led;
            }
        }

        /// <summary>
        /// Returns the Settings Paula Filter.
        /// </summary>
        private static AmigaPaulaFilter GetSettingsPaulaFilter(PaulaFilterMode mode)
        {
            switch (mode)
            {
                case PaulaFilterMode.Off: return AmigaPaulaFilter.Off;
                case PaulaFilterMode.LowPass: return AmigaPaulaFilter.A1200;
                default: return AmigaPaulaFilter.A500;
            }
        }

        /// <summary>
        /// Executes the Format RAM Profile logic.
        /// </summary>
        private static string FormatRamProfile(RamProfile profile)
        {
            switch (profile)
            {
                case RamProfile.Chip512: return "512K CHIP";
                case RamProfile.Chip1Mb: return "1MB CHIP";
                case RamProfile.Chip1MbFast4Mb: return "1MB CHIP + 4MB FAST";
                default: return "512K CHIP + 512K SLOW";
            }
        }

        /// <summary>
        /// Executes the Format Floppy Speed logic.
        /// </summary>
        private static string FormatFloppySpeed(FloppySpeedMode mode)
        {
            switch (mode)
            {
                case FloppySpeedMode.TurboLoading: return "TURBO LOAD";
                case FloppySpeedMode.TurboAlways: return "TURBO ALWAYS";
                default: return "REAL";
            }
        }

        /// <summary>
        /// Executes the Format Input Ports logic.
        /// </summary>
        private static string FormatInputPorts(InputPortMode mode)
        {
            switch (mode)
            {
                case InputPortMode.JoystickPort1MousePort2: return "JOY P1 / MOUSE P2";
                case InputPortMode.DualJoystick: return "DUAL JOYSTICK";
                default: return "MOUSE P1 / JOY P2";
            }
        }

        /// <summary>
        /// Applies Input Port Mode to the current state.
        /// </summary>
        private void ApplyInputPortMode()
        {
            if (_system == null)
            {
                return;
            }

            switch (_inputPortMode)
            {
                case InputPortMode.JoystickPort1MousePort2:
                    _system.Input.SetPortModes(AmigaInputPortMode.KeyboardJoystick, AmigaInputPortMode.Mouse);
                    _settings.Port1Mode = AmigaInputPortMode.KeyboardJoystick;
                    _settings.Port2Mode = AmigaInputPortMode.Mouse;
                    break;
                case InputPortMode.DualJoystick:
                    _system.Input.SetPortModes(AmigaInputPortMode.KeyboardJoystick, AmigaInputPortMode.KeyboardJoystick);
                    _settings.Port1Mode = AmigaInputPortMode.KeyboardJoystick;
                    _settings.Port2Mode = AmigaInputPortMode.KeyboardJoystick;
                    break;
                default:
                    _system.Input.SetPortModes(AmigaInputPortMode.Mouse, AmigaInputPortMode.KeyboardJoystick);
                    _settings.Port1Mode = AmigaInputPortMode.Mouse;
                    _settings.Port2Mode = AmigaInputPortMode.KeyboardJoystick;
                    break;
            }

            _statusText = "INPUT " + FormatInputPorts(_inputPortMode);
            AmigaEmulatorSettingsStore.Save(_settings);
        }

        /// <summary>
        /// Returns the Input Port Mode From Settings.
        /// </summary>
        private static InputPortMode GetInputPortModeFromSettings(AmigaInputPortMode port0, AmigaInputPortMode port1)
        {
            if (IsJoystickMode(port0) && port1 == AmigaInputPortMode.Mouse)
            {
                return InputPortMode.JoystickPort1MousePort2;
            }

            if (IsJoystickMode(port0) && IsJoystickMode(port1))
            {
                return InputPortMode.DualJoystick;
            }

            return InputPortMode.MousePort1JoystickPort2;
        }

        /// <summary>
        /// Returns whether Joystick Mode is true.
        /// </summary>
        private static bool IsJoystickMode(AmigaInputPortMode mode)
        {
            return mode == AmigaInputPortMode.Joystick ||
                mode == AmigaInputPortMode.KeyboardJoystick ||
                mode == AmigaInputPortMode.Gamepad;
        }

        /// <summary>
        /// Attempts to Get Mouse Port and reports whether it succeeded.
        /// </summary>
        private bool TryGetMousePort(out AmigaControlPort port)
        {
            switch (_inputPortMode)
            {
                case InputPortMode.JoystickPort1MousePort2:
                    port = AmigaControlPort.Port1;
                    return true;
                case InputPortMode.DualJoystick:
                    port = AmigaControlPort.Port0;
                    return false;
                default:
                    port = AmigaControlPort.Port0;
                    return true;
            }
        }

        /// <summary>
        /// Handles Joystick Key from the host UI or emulator core.
        /// </summary>
        private bool HandleJoystickKey(Key key, bool pressed)
        {
            if (!TryMapJoystickKey(key, out AmigaControlPort port, out AmigaJoystickButtons button))
            {
                return false;
            }

            _system.Input.SetJoystickButton(port, button, pressed);
            return true;
        }

        /// <summary>
        /// Returns whether Emulator Hotkey is true.
        /// </summary>
        private static bool IsEmulatorHotkey(Key key)
        {
            return key == Key.F9 || key == Key.F10 || key == Key.F11 || key == Key.F12;
        }

        /// <summary>
        /// Attempts to Map Joystick Key and reports whether it succeeded.
        /// </summary>
        private bool TryMapJoystickKey(Key key, out AmigaControlPort port, out AmigaJoystickButtons button)
        {
            port = _inputPortMode == InputPortMode.JoystickPort1MousePort2 ? AmigaControlPort.Port0 : AmigaControlPort.Port1;
            button = AmigaJoystickButtons.None;

            bool wasd = key == Key.W || key == Key.A || key == Key.S || key == Key.D || key == Key.AltLeft;
            bool arrows = key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right ||
                key == Key.NumPad0 || key == Key.NumPad2 || key == Key.NumPad4 || key == Key.NumPad6 || key == Key.NumPad8 ||
                key == Key.ControlLeft || key == Key.ControlRight;

            if (_inputPortMode == InputPortMode.DualJoystick)
            {
                if (wasd)
                {
                    port = AmigaControlPort.Port0;
                }
                else if (arrows)
                {
                    port = AmigaControlPort.Port1;
                }
                else
                {
                    return false;
                }
            }
            else if (!arrows)
            {
                return false;
            }

            switch (key)
            {
                case Key.W:
                case Key.Up:
                case Key.NumPad8:
                    button = AmigaJoystickButtons.Up;
                    return true;
                case Key.S:
                case Key.Down:
                case Key.NumPad2:
                    button = AmigaJoystickButtons.Down;
                    return true;
                case Key.A:
                case Key.Left:
                case Key.NumPad4:
                    button = AmigaJoystickButtons.Left;
                    return true;
                case Key.D:
                case Key.Right:
                case Key.NumPad6:
                    button = AmigaJoystickButtons.Right;
                    return true;
                case Key.ControlLeft:
                case Key.ControlRight:
                case Key.NumPad0:
                case Key.AltLeft:
                    button = AmigaJoystickButtons.Fire;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Maps Mouse Button between host and Amiga representations.
        /// </summary>
        private static AmigaJoystickButtons MapMouseButton(MouseButtons button)
        {
            switch (button)
            {
                case MouseButtons.Left: return AmigaJoystickButtons.Fire;
                case MouseButtons.Right: return AmigaJoystickButtons.Second;
                case MouseButtons.Middle: return AmigaJoystickButtons.Third;
                default: return AmigaJoystickButtons.None;
            }
        }

        /// <summary>
        /// Attempts to Map Amiga Raw Key and reports whether it succeeded.
        /// </summary>
        private static bool TryMapAmigaRawKey(Key key, out byte rawKey)
        {
            switch (key)
            {
                case Key.Grave: rawKey = 0x00; return true;
                case Key.D1: rawKey = 0x01; return true;
                case Key.D2: rawKey = 0x02; return true;
                case Key.D3: rawKey = 0x03; return true;
                case Key.D4: rawKey = 0x04; return true;
                case Key.D5: rawKey = 0x05; return true;
                case Key.D6: rawKey = 0x06; return true;
                case Key.D7: rawKey = 0x07; return true;
                case Key.D8: rawKey = 0x08; return true;
                case Key.D9: rawKey = 0x09; return true;
                case Key.D0: rawKey = 0x0A; return true;
                case Key.Minus: rawKey = 0x0B; return true;
                case Key.Equals: rawKey = 0x0C; return true;
                case Key.Backslash: rawKey = 0x0D; return true;
                case Key.Q: rawKey = 0x10; return true;
                case Key.W: rawKey = 0x11; return true;
                case Key.E: rawKey = 0x12; return true;
                case Key.R: rawKey = 0x13; return true;
                case Key.T: rawKey = 0x14; return true;
                case Key.Y: rawKey = 0x15; return true;
                case Key.U: rawKey = 0x16; return true;
                case Key.I: rawKey = 0x17; return true;
                case Key.O: rawKey = 0x18; return true;
                case Key.P: rawKey = 0x19; return true;
                case Key.OpenBracket: rawKey = 0x1A; return true;
                case Key.CloseBracket: rawKey = 0x1B; return true;
                case Key.A: rawKey = 0x20; return true;
                case Key.S: rawKey = 0x21; return true;
                case Key.D: rawKey = 0x22; return true;
                case Key.F: rawKey = 0x23; return true;
                case Key.G: rawKey = 0x24; return true;
                case Key.H: rawKey = 0x25; return true;
                case Key.J: rawKey = 0x26; return true;
                case Key.K: rawKey = 0x27; return true;
                case Key.L: rawKey = 0x28; return true;
                case Key.Semicolon: rawKey = 0x29; return true;
                case Key.Apostrophe: rawKey = 0x2A; return true;
                case Key.Z: rawKey = 0x31; return true;
                case Key.X: rawKey = 0x32; return true;
                case Key.C: rawKey = 0x33; return true;
                case Key.V: rawKey = 0x34; return true;
                case Key.B: rawKey = 0x35; return true;
                case Key.N: rawKey = 0x36; return true;
                case Key.M: rawKey = 0x37; return true;
                case Key.Comma: rawKey = 0x38; return true;
                case Key.Period: rawKey = 0x39; return true;
                case Key.Slash: rawKey = 0x3A; return true;
                case Key.Space: rawKey = 0x40; return true;
                case Key.Backspace: rawKey = 0x41; return true;
                case Key.Tab: rawKey = 0x42; return true;
                case Key.Enter: rawKey = 0x44; return true;
                case Key.Escape: rawKey = 0x45; return true;
                case Key.Delete: rawKey = 0x46; return true;
                case Key.Up: rawKey = 0x4C; return true;
                case Key.Down: rawKey = 0x4D; return true;
                case Key.Right: rawKey = 0x4E; return true;
                case Key.Left: rawKey = 0x4F; return true;
                case Key.F1: rawKey = 0x50; return true;
                case Key.F2: rawKey = 0x51; return true;
                case Key.F3: rawKey = 0x52; return true;
                case Key.F4: rawKey = 0x53; return true;
                case Key.F5: rawKey = 0x54; return true;
                case Key.F6: rawKey = 0x55; return true;
                case Key.F7: rawKey = 0x56; return true;
                case Key.F8: rawKey = 0x57; return true;
                case Key.F9: rawKey = 0x58; return true;
                case Key.F10: rawKey = 0x59; return true;
                case Key.ShiftLeft: rawKey = 0x60; return true;
                case Key.ShiftRight: rawKey = 0x61; return true;
                case Key.CapsLock: rawKey = 0x62; return true;
                case Key.ControlLeft:
                case Key.ControlRight:
                    rawKey = 0x63;
                    return true;
                case Key.AltLeft: rawKey = 0x64; return true;
                case Key.AltRight: rawKey = 0x65; return true;
                default:
                    rawKey = 0;
                    return false;
            }
        }

        /// <summary>
        /// Executes the Format Reset Mode logic.
        /// </summary>
        private static string FormatResetMode(ResetMode mode)
        {
            switch (mode)
            {
                case ResetMode.Remount: return "RESET + REMOUNT";
                case ResetMode.Power: return "POWER";
                default: return "WARM";
            }
        }

        /// <summary>
        /// Executes the Format Accuracy Profile logic.
        /// </summary>
        private static string FormatAccuracyProfile(AmigaAccuracyProfile profile)
        {
            switch (profile)
            {
                case AmigaAccuracyProfile.Strict: return "STRICT";
                case AmigaAccuracyProfile.Compatible: return "COMPAT";
                default: return "FAST";
            }
        }

        /// <summary>
        /// Returns the Initial Media Directory.
        /// </summary>
        private static string GetInitialMediaDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
            {
                string directory = Path.Combine(documents, "AmigaEmulator");
                Directory.CreateDirectory(directory);
                return directory;
            }

            string fallback = AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        /// <summary>
        /// Executes the Format Overlay Path logic.
        /// </summary>
        private static string FormatOverlayPath(string path, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "NONE";
            }

            string normalized = path.Replace('\\', '/');
            return TrimText(normalized, maxLength);
        }

        /// <summary>
        /// Executes the Trim Text logic.
        /// </summary>
        private static string TrimText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return "..." + text.Substring(text.Length - Math.Max(0, maxLength - 3));
        }
    }
}
