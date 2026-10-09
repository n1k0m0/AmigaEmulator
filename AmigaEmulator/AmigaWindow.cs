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
using System.Runtime.InteropServices;
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
    public sealed partial class AmigaWindow : SharpPixelsWindow
    {
        /// <summary>
        /// Lists the supported Overlay Mode values.
        /// </summary>
        private enum OverlayMode
        {
            /// <summary>
            /// Represents None.
            /// </summary>
            None,
            /// <summary>
            /// Represents Settings.
            /// </summary>
            Settings,
            /// <summary>
            /// Represents Main Menu.
            /// </summary>
            MainMenu,
            /// <summary>
            /// Represents Display.
            /// </summary>
            Display,
            /// <summary>
            /// Represents Media.
            /// </summary>
            Media,
            /// <summary>
            /// Represents Machine.
            /// </summary>
            Machine,
            /// <summary>
            /// Represents Roms.
            /// </summary>
            Roms,
            /// <summary>
            /// Represents Input.
            /// </summary>
            Input,
            /// <summary>
            /// Represents Reset.
            /// </summary>
            Reset,
            /// <summary>
            /// Represents Save States.
            /// </summary>
            SaveStates,
            HardDisk
        }

        /// <summary>
        /// Lists the supported Render Limit Mode values.
        /// </summary>
        private enum RenderLimitMode
        {
            /// <summary>
            /// Represents Native.
            /// </summary>
            Native,
            /// <summary>
            /// Represents 50 Hz.
            /// </summary>
            Hz50,
            /// <summary>
            /// Represents 60 Hz.
            /// </summary>
            Hz60,
            /// <summary>
            /// Represents 100 Hz.
            /// </summary>
            Hz100,
            /// <summary>
            /// Represents 120 Hz.
            /// </summary>
            Hz120,
            /// <summary>
            /// Represents Unlimited.
            /// </summary>
            Unlimited
        }

        /// <summary>
        /// Lists the supported Video Filter Mode values.
        /// </summary>
        private enum VideoFilterMode
        {
            /// <summary>
            /// Represents Sharp.
            /// </summary>
            Sharp,
            /// <summary>
            /// Represents CRT.
            /// </summary>
            Crt,
            /// <summary>
            /// Represents TV.
            /// </summary>
            Tv
        }

        /// <summary>
        /// Lists the supported Video Upscale Mode values.
        /// </summary>
        private enum VideoUpscaleMode
        {
            /// <summary>
            /// Represents None.
            /// </summary>
            None,
            /// <summary>
            /// Represents Scale2x.
            /// </summary>
            Scale2x,
            /// <summary>
            /// Represents Scale3x.
            /// </summary>
            Scale3x,
            /// <summary>
            /// Represents HQ2x.
            /// </summary>
            Hq2x,
            /// <summary>
            /// Represents HQ3x.
            /// </summary>
            Hq3x,
            /// <summary>
            /// Represents HQ4x.
            /// </summary>
            Hq4x,
            /// <summary>
            /// Represents xBRZ.
            /// </summary>
            Xbrz,
            /// <summary>
            /// Represents Clean 4.
            /// </summary>
            Clean4
        }

        /// <summary>
        /// Lists host monitor aperture modes.
        /// </summary>
        private enum MonitorApertureMode
        {
            /// <summary>
            /// Hides common PAL overscan border areas like a CRT monitor.
            /// </summary>
            Crt,
            /// <summary>
            /// Shows the complete PAL overscan presentation for diagnostics.
            /// </summary>
            Overscan
        }

        /// <summary>
        /// Lists the supported Paula Filter Mode values.
        /// </summary>
        private enum PaulaFilterMode
        {
            /// <summary>
            /// Represents Off.
            /// </summary>
            Off,
            /// <summary>
            /// Represents Led.
            /// </summary>
            Led,
            /// <summary>
            /// Represents Low Pass.
            /// </summary>
            LowPass
        }

        /// <summary>
        /// Lists the supported RAM Profile values.
        /// </summary>
        private enum RamProfile
        {
            /// <summary>
            /// Represents Chip 512 Slow 512.
            /// </summary>
            Chip512Slow512,
            /// <summary>
            /// Represents Chip 512.
            /// </summary>
            Chip512,
            /// <summary>
            /// Represents Chip 1 Mb.
            /// </summary>
            Chip1Mb,
            /// <summary>
            /// Represents Chip 2 Mb.
            /// </summary>
            Chip2Mb,
            /// <summary>
            /// Represents Chip 2 Mb Fast 8 Mb.
            /// </summary>
            Chip2MbFast8Mb,
            Chip1MbFast2Mb,
            Chip1MbFast4Mb,
            Chip2MbFast2Mb,
            Chip2MbFast4Mb,
            Chip512KSlow512KFast2Mb,
            Chip512KSlow512KFast4Mb,
            Chip512KSlow512KFast8Mb
        }

        /// <summary>
        /// Lists the supported Floppy Speed Mode values.
        /// </summary>
        private enum FloppySpeedMode
        {
            /// <summary>
            /// Represents Real.
            /// </summary>
            Real,
            /// <summary>
            /// Represents Turbo Loading.
            /// </summary>
            TurboLoading,
            /// <summary>
            /// Represents Turbo Always.
            /// </summary>
            TurboAlways
        }

        /// <summary>
        /// Lists the supported Input Port Mode values.
        /// </summary>
        private enum InputPortMode
        {
            /// <summary>
            /// Represents Mouse Port 1 Joystick Port 2.
            /// </summary>
            MousePort1JoystickPort2,
            /// <summary>
            /// Represents Joystick Port 1 Mouse Port 2.
            /// </summary>
            JoystickPort1MousePort2,
            /// <summary>
            /// Represents Dual Joystick.
            /// </summary>
            DualJoystick
        }

        /// <summary>
        /// Lists the supported Reset Mode values.
        /// </summary>
        private enum ResetMode
        {
            /// <summary>
            /// Represents Warm.
            /// </summary>
            Warm,
            /// <summary>
            /// Represents Remount.
            /// </summary>
            Remount,
            /// <summary>
            /// Represents Power.
            /// </summary>
            Power
        }

        /// <summary>
        /// Caps one host mouse injection below the signed 8-bit JOYxDAT wrap danger zone.
        /// </summary>
        private const int MaxMouseDeltaPerSlice = 32;

        /// <summary>
        /// Spreads host mouse motion through sub-frame chunks so it stays responsive without one-shot counter jumps.
        /// </summary>
        private const int MouseDeltaDrainSliceCycles = 4096;

        /// <summary>
        /// Holds a delayed mouse click only long enough to preserve down/up ordering after buffered motion.
        /// </summary>
        private const int DeferredMouseButtonHoldCycles = 30000;

        /// <summary>
        /// Keeps host edge-to-edge mouse motion inside the drawable width of a 16-lores-pixel Amiga sprite cursor.
        /// </summary>
        private const int MouseSpriteGuardHiresPixelsX = 32;

        /// <summary>
        /// Lets Kickstart finish early initialization before restoring DF0 from persistent settings.
        /// </summary>
        private const int DeferredMediaMountChipCycles = 16000000;
        /// <summary>
        /// Leaves horizontal aperture clipping to the core monitor viewport.
        /// </summary>
        private const int CrtApertureCropPixelsLeft = 0;

        /// <summary>
        /// Leaves horizontal aperture clipping to the core monitor viewport.
        /// </summary>
        private const int CrtApertureCropPixelsRight = 0;

        /// <summary>
        /// Keeps bottom overscan visible so disk prompts and game HUDs are not host-cropped.
        /// </summary>
        private const int PresentationCropPixelsBottom = 0;

        /// <summary>
        /// Treats sparse colored pixels in the bottom overscan as real content rather than a border fill.
        /// </summary>
        private const double BottomOverscanContentMinimumPercent = 0.2;

        /// <summary>
        /// Treats dense but mostly uniform bottom overscan as a border band that should stay hidden.
        /// </summary>
        private const double BottomOverscanContentMaximumPercent = 45.0;

        /// <summary>
        /// Keeps dense bottom artwork visible when it contains enough horizontal detail to be real content.
        /// </summary>
        private const double BottomOverscanHorizontalStructureMinimumPercent = 12.0;

        /// <summary>
        /// Keeps dense bottom artwork visible when it contains enough vertical detail to be real content.
        /// </summary>
        private const double BottomOverscanVerticalStructureMinimumPercent = 18.0;

        /// <summary>
        /// Keeps sparse bottom-overscan prompts visible while their text blinks.
        /// </summary>
        private const int BottomOverscanContentHoldFrames = 60;

        /// <summary>
        /// Tracks the bottom crop currently applied to the host presentation.
        /// </summary>
        private int _presentationCropBottom = PresentationCropPixelsBottom;

        /// <summary>
        /// Counts down while previously detected sparse bottom content may be blinking off.
        /// </summary>
        private int _bottomOverscanContentHoldFrames;

        /// <summary>
        /// Stores system.
        /// </summary>
        private AmigaSystem _system;
        /// <summary>
        /// Runs Paula Audio Output.
        /// </summary>
        private readonly PaulaAudioOutput _audioOutput = new PaulaAudioOutput();
        /// <summary>
        /// Runs Start New.
        /// </summary>
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        /// <summary>
        /// Runs Font.
        /// </summary>
        private readonly Font _smallFont = new Font(FontFamily.GenericMonospace, 11.0f, FontStyle.Regular);
        /// <summary>
        /// Runs Font.
        /// </summary>
        private readonly Font _titleFont = new Font(FontFamily.GenericMonospace, 18.0f, FontStyle.Bold);
        /// <summary>
        /// Stores main Menu Items.
        /// </summary>
        private readonly string[] _mainMenuItems = { "SETTINGS", "MEDIA", "MACHINE", "INPUT", "RESET", "DISPLAY", "HARD DISK" };
        /// <summary>
        /// Number of savestate rows visible in the browser overlay.
        /// </summary>
        private const int SaveOverlayVisibleRows = 12;
        /// <summary>
        /// Stores settings.
        /// </summary>
        private readonly AmigaEmulatorSettings _settings;
        /// <summary>
        /// Stores overlay Mode.
        /// </summary>
        private OverlayMode _overlayMode = OverlayMode.None;
        /// <summary>
        /// Stores previous Overlay Mode.
        /// </summary>
        private OverlayMode _previousOverlayMode = OverlayMode.None;
        /// <summary>
        /// Stores render Limit Mode.
        /// </summary>
        private RenderLimitMode _renderLimitMode = RenderLimitMode.Hz50;
        /// <summary>
        /// Stores video Filter Mode.
        /// </summary>
        private VideoFilterMode _videoFilterMode = VideoFilterMode.Sharp;
        /// <summary>
        /// Stores video Upscale Mode.
        /// </summary>
        private VideoUpscaleMode _videoUpscaleMode = VideoUpscaleMode.None;
        /// <summary>
        /// Stores monitor Aperture Mode.
        /// </summary>
        private MonitorApertureMode _monitorApertureMode = MonitorApertureMode.Crt;
        /// <summary>
        /// Stores paula Filter Mode.
        /// </summary>
        private PaulaFilterMode _paulaFilterMode = PaulaFilterMode.Led;
        /// <summary>
        /// Stores RAM Profile.
        /// </summary>
        private RamProfile _ramProfile = RamProfile.Chip1Mb;
        private AmigaMachineModel _machineModel = AmigaMachineModel.A500;
        /// <summary>
        /// Stores floppy Speed Mode.
        /// </summary>
        private FloppySpeedMode _floppySpeedMode = FloppySpeedMode.Real;
        /// <summary>
        /// Stores input Port Mode.
        /// </summary>
        private InputPortMode _inputPortMode = InputPortMode.MousePort1JoystickPort2;
        private readonly WindowsGamepadInput _gamepads = new WindowsGamepadInput();
        /// <summary>
        /// Stores reset Mode.
        /// </summary>
        private ResetMode _resetMode = ResetMode.Warm;
        /// <summary>
        /// Stores accuracy Profile.
        /// </summary>
        private AmigaAccuracyProfile _accuracyProfile = AmigaAccuracyProfile.Compatible;
        /// <summary>
        /// Stores mounted Media.
        /// </summary>
        private MountedMediaInfo _mountedMedia = MountedMediaInfo.None;
        /// <summary>
        /// Stores a DF0 image that should be mounted after Kickstart startup has settled.
        /// </summary>
        private MountedMediaInfo _deferredMediaMount = MountedMediaInfo.None;
        /// <summary>
        /// Stores the frame at which the deferred DF0 mount may run.
        /// </summary>
        private long _deferredMediaMountFrame = -1;
        /// <summary>
        /// Stores media Entries.
        /// </summary>
        private readonly List<string> _mediaEntries = new List<string>();
        /// <summary>
        /// Stores rom Entries.
        /// </summary>
        private readonly List<KickstartRomInfo> _romEntries = new List<KickstartRomInfo>();
        /// <summary>
        /// Stores media Directory.
        /// </summary>
        private string _mediaDirectory;
        /// <summary>
        /// Stores selected ROM Path.
        /// </summary>
        private string _selectedRomPath;
        /// <summary>
        /// Stores settings Selection.
        /// </summary>
        private int _settingsSelection;
        /// <summary>
        /// Stores main Selection.
        /// </summary>
        private int _mainSelection;
        /// <summary>
        /// Stores display Selection.
        /// </summary>
        private int _displaySelection;
        /// <summary>
        /// Stores media Selection.
        /// </summary>
        private int _mediaSelection;
        /// <summary>
        /// Stores ROM Selection.
        /// </summary>
        private int _romSelection;
        /// <summary>
        /// Stores machine Selection.
        /// </summary>
        private int _machineSelection;
        /// <summary>
        /// Stores input Selection.
        /// </summary>
        private int _inputSelection;
        /// <summary>
        /// Stores reset Selection.
        /// </summary>
        private int _resetSelection;
        /// <summary>
        /// Stores save Selection.
        /// </summary>
        private int _saveSelection;
        /// <summary>
        /// Stores save Overlay Scroll.
        /// </summary>
        private int _saveOverlayScroll;
        /// <summary>
        /// Stores save Delete Confirm Visible.
        /// </summary>
        private bool _saveDeleteConfirmVisible;
        /// <summary>
        /// Stores save Delete Confirm Yes Selected.
        /// </summary>
        private bool _saveDeleteConfirmYesSelected = true;
        /// <summary>
        /// Stores pending Delete Save Path.
        /// </summary>
        private string _pendingDeleteSavePath;
        /// <summary>
        /// Stores save Overlay Current Directory.
        /// </summary>
        private string _saveOverlayCurrentDirectory;
        /// <summary>
        /// Stores last Loaded Save State Path.
        /// </summary>
        private string _lastLoadedSaveStatePath;
        /// <summary>
        /// Stores save Overlay Entries.
        /// </summary>
        private List<SaveOverlayEntry> _saveOverlayEntries = new List<SaveOverlayEntry>();
        /// <summary>
        /// Stores debug Overlay Visible.
        /// </summary>
        private bool _debugOverlayVisible = false;
        /// <summary>
        /// Stores drive Overlay Visible.
        /// </summary>
        private bool _driveOverlayVisible = true;
        /// <summary>
        /// Stores audio Enabled.
        /// </summary>
        private bool _audioEnabled = true;
        /// <summary>
        /// Stores floppy Write Protected.
        /// </summary>
        private bool _floppyWriteProtected = true;
        /// <summary>
        /// Stores turbo Mode.
        /// </summary>
        private bool _turboMode;
        /// <summary>
        /// Stores fullscreen.
        /// </summary>
        private bool _fullscreen;
        /// <summary>
        /// Stores has Last Mouse Position.
        /// </summary>
        private bool _hasLastMousePosition;
        /// <summary>
        /// Stores last Mouse Position.
        /// </summary>
        private Point _lastMousePosition;
        /// <summary>
        /// Stores pending relative Mouse X motion.
        /// </summary>
        private int _pendingMouseDx;
        /// <summary>
        /// Stores pending relative Mouse Y motion.
        /// </summary>
        private int _pendingMouseDy;
        /// <summary>
        /// Stores the port receiving buffered Mouse motion.
        /// </summary>
        private AmigaControlPort _pendingMousePort;
        /// <summary>
        /// Stores queued mouse buttons waiting for buffered motion to drain.
        /// </summary>
        private AmigaJoystickButtons _queuedMouseDownButtons;
        /// <summary>
        /// Stores queued mouse button releases waiting for a deferred press.
        /// </summary>
        private AmigaJoystickButtons _queuedMouseUpButtons;
        /// <summary>
        /// Stores deferred mouse buttons currently held in the emulated port.
        /// </summary>
        private AmigaJoystickButtons _deferredHeldMouseButtons;
        /// <summary>
        /// Stores the remaining minimum hold time for deferred mouse buttons.
        /// </summary>
        private int _deferredMouseButtonHoldCycles;
        /// <summary>
        /// Hides the host cursor while the Amiga display owns mouse input.
        /// </summary>
        private readonly Cursor _hiddenHostCursor = CreateHiddenHostCursor();
        /// <summary>
        /// Tracks whether Windows currently clips host mouse motion to the emulator display.
        /// </summary>
        private bool _hostMouseCaptured;
        /// <summary>
        /// Stores the client rectangle currently used for host mouse clipping.
        /// </summary>
        private Rectangle _capturedHostMouseClientRectangle;
        /// <summary>
        /// Tracks a synthetic MouseMove generated by re-centering the host cursor.
        /// </summary>
        private bool _pendingHostMouseWarp;
        /// <summary>
        /// Stores the screen position targeted by the most recent host cursor warp.
        /// </summary>
        private Point _pendingHostMouseWarpScreenPoint;
        /// <summary>
        /// Stores master Volume.
        /// </summary>
        private float _masterVolume = 0.80f;
        /// <summary>
        /// Stores last Seconds.
        /// </summary>
        private double _lastSeconds;
        /// <summary>
        /// Stores last Update Seconds.
        /// </summary>
        private double _lastUpdateSeconds;
        /// <summary>
        /// Stores last Perf Seconds.
        /// </summary>
        private double _lastPerfSeconds;
        /// <summary>
        /// Stores last Title Update Seconds.
        /// </summary>
        private double _lastTitleUpdateSeconds;
        /// <summary>
        /// Stores pending Chip Cycles.
        /// </summary>
        private double _pendingChipCycles;
        /// <summary>
        /// Stores perf Chip Cycles.
        /// </summary>
        private long _perfChipCycles;
        /// <summary>
        /// Stores host Speed Percent.
        /// </summary>
        private double _hostSpeedPercent = 100.0;
        /// <summary>
        /// Stores frame-counter based presentation FPS for the window title.
        /// </summary>
        private double _presentedFps = 50.0;
        /// <summary>
        /// Stores frame counter at the previous performance sample.
        /// </summary>
        private long _perfFrameCounter;
        /// <summary>
        /// Stores smoothed CPU Mhz.
        /// </summary>
        private double _smoothedCpuMhz;
        /// <summary>
        /// Stores audio Filter Left.
        /// </summary>
        private float _audioFilterLeft;
        /// <summary>
        /// Stores audio Filter Right.
        /// </summary>
        private float _audioFilterRight;
        /// <summary>
        /// Stores last Frame.
        /// </summary>
        private long _lastFrame;
        /// <summary>
        /// Stores ROM Status Text.
        /// </summary>
        private string _romStatusText = "KICKSTART MISSING";
        /// <summary>
        /// Stores status Text.
        /// </summary>
        private string _statusText = "READY";
        /// <summary>
        /// Tracks whether the latest update produced a new presented frame for painting.
        /// </summary>
        private bool _paintFrameReady = true;

        /// <summary>
        /// Runs Amiga Window.
        /// </summary>
        public AmigaWindow(AmigaModel model, string title, bool applyStartupModel = false)
            : base(
                AmigaSystem.PresentationViewportWidth,
                AmigaSystem.PresentationViewportHeight,
                AmigaSystem.PresentationViewportWidth / 2,
                AmigaSystem.PresentationViewportHeight,
                title)
        {
            _settings = AmigaEmulatorSettingsStore.Load();
            if (applyStartupModel) _settings.ApplyStartupModel(model);
            _videoFilterMode = GetVideoFilterFromSettings(_settings.VideoFilter);
            _videoUpscaleMode = GetVideoUpscaleFromSettings(_settings.UpscaleMode);
            _monitorApertureMode = GetMonitorApertureFromSettings(_settings.MonitorAperture);
            _renderLimitMode = GetRenderLimitFromSettings(_settings.RenderFps);
            _paulaFilterMode = GetPaulaFilterFromSettings(_settings.PaulaFilter);
            _floppySpeedMode = GetFloppySpeedFromSettings(_settings.FloppySpeed);
            _resetMode = GetResetModeFromSettings(_settings.ResetMode);
            _driveOverlayVisible = _settings.DriveOverlayEnabled;
            _turboMode = _settings.TurboMode;
            _fullscreen = _settings.Fullscreen;
            _accuracyProfile = _settings.AccuracyProfile;
            _machineModel = _settings.MachineModel;
            _ramProfile = GetRamProfileFromSettings(_settings.RamProfile, GetRamProfileForModel(model));
            _system = new AmigaSystem(AmigaModel.Create(_machineModel, GetSettingsRamProfile(_ramProfile)), _settings.CreateAccuracyOptions());
            RestoreBatteryClock();
            MountConfiguredPcmcia();
            ApplyPresentationSourceCrop();
            SetPresentationStretchToClient(_settings.StretchToFullWidth);
            ApplyFloppyTiming();
            AttachAudioToSystem();
            _inputPortMode = GetInputPortModeFromSettings(_settings.Port1Mode, _settings.Port2Mode);
            ApplyInputPortMode();
            _mediaDirectory = !string.IsNullOrWhiteSpace(_settings.MediaBrowserDirectory) ? _settings.MediaBrowserDirectory : GetInitialMediaDirectory();
            _selectedRomPath = _settings.CustomKickstartPath ?? string.Empty;
            LoadConfiguredKickstart();
            MountConfiguredHardDisk();
            ScheduleConfiguredMediaRemount();
            RefreshMediaEntries();
            RefreshRomEntries();
            _saveOverlayCurrentDirectory = GetSaveDirectory();
            RefreshSaveStateSlots();
            ApplyVideoPresentation();
            UpdateFrequency = GetRenderUpdateFrequency(_renderLimitMode);
            ApplyStartupFullscreen();
            ResetEmulationClock();
        }

        /// <summary>
        /// Loads a savestate for this GUI session without persisting startup settings.
        /// </summary>
        public bool TryLoadStartupSaveState(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath) || !File.Exists(savePath))
            {
                _statusText = "START SAVE MISSING";
                return false;
            }

            try
            {
                SaveStateSlotMetadata slot = SaveStateFile.ReadMetadata(savePath);
                if (slot.Status != SaveStateSlotStatus.Occupied)
                {
                    _statusText = "START SAVE INVALID";
                    return false;
                }

                FlushMountedMedia();
                RamProfile targetRamProfile = GetRamProfileFromSettings(slot.RamProfile, _ramProfile);
                bool needsRecreate = _ramProfile != targetRamProfile || _accuracyProfile != slot.AccuracyProfile || _machineModel != slot.MachineModel;
                _machineModel = slot.MachineModel;
                _ramProfile = targetRamProfile;
                _accuracyProfile = slot.AccuracyProfile;
                if (needsRecreate)
                {
                    RecreateSystemForCurrentProfile(false);
                }
                else
                {
                    _system.SetAccuracyOptions(CreateAccuracyOptions(_accuracyProfile));
                }

                SaveStateFile.Load(slot.Path, _system);
                SynchronizeRestoredMachineSettings(false);
                ClearDeferredMediaMount();
                ApplyFloppyTiming();
                _system.PresentedFrameBuffer.CopyTo(Pixels);
                _mountedMedia = slot.MountedMedia ?? MountedMediaInfo.None;
                _lastLoadedSaveStatePath = slot.Path;
                ResetAudioOutput();
                ResetEmulationClock();
                _statusText = "SAVE LOADED";
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "START SAVE FAILED";
                return false;
            }
        }

        /// <summary>
        /// Handles the Update Frame callback.
        /// </summary>
        protected override void OnUpdateFrame()
        {
            PollGamepads();
            _paintFrameReady = false;
            int cycles = CalculateCyclesForUpdate();
            if (cycles > 0)
            {
                RunChipCyclesWithBufferedMouse(cycles);
                CompleteDeferredMediaMountIfReady();
                UpdatePerformanceCounters(cycles);
                UpdateTitleStatus();
            }

            if (_system.FrameCounter != _lastFrame)
            {
                _system.PresentedFrameBuffer.CopyTo(Pixels);
                _lastFrame = _system.FrameCounter;
                _paintFrameReady = true;
            }

            _lastSeconds = _stopwatch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Paints only when a fresh Amiga frame is available or an overlay needs live interaction.
        /// </summary>
        protected override bool ShouldPaintFrame()
        {
            return _paintFrameReady || _overlayMode != OverlayMode.None;
        }

        /// <summary>
        /// Runs Process Cmd Key.
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
            Rectangle presentation = GetPresentationRectangle();
            bool insidePresentation = presentation.Contains(e.Location);
            bool captureWasCentered = UpdateHostMouseCapture();
            if (_hostMouseCaptured)
            {
                Point center = GetHostMouseCaptureCenter(presentation);
                if (captureWasCentered || TryConsumeHostMouseWarp(e.Location))
                {
                    _lastMousePosition = center;
                    _hasLastMousePosition = true;
                    base.OnMouseMove(e);
                    return;
                }

                if (_overlayMode == OverlayMode.None &&
                    insidePresentation &&
                    TryGetMousePort(out AmigaControlPort capturedMousePort))
                {
                    int rawDx = e.X - center.X;
                    int rawDy = e.Y - center.Y;
                    if (rawDx != 0 || rawDy != 0)
                    {
                        int cursorSafeSourceWidth = Math.Max(1, PresentationSourceWidth - MouseSpriteGuardHiresPixelsX);
                        double scaleX = presentation.Width > 0 ? cursorSafeSourceWidth / (double)presentation.Width : 1.0;
                        double scaleY = presentation.Height > 0 ? PresentationSourceHeight / (double)presentation.Height : 1.0;
                        int dx = (int)Math.Round(rawDx * scaleX);
                        int dy = (int)Math.Round(rawDy * scaleY);
                        QueueBufferedMouseMotion(capturedMousePort, dx, dy);
                    }
                }

                CenterHostMouse(center);
                _lastMousePosition = center;
                _hasLastMousePosition = true;
                base.OnMouseMove(e);
                return;
            }

            if (_overlayMode == OverlayMode.None &&
                insidePresentation &&
                TryGetMousePort(out AmigaControlPort mousePort) &&
                _hasLastMousePosition &&
                presentation.Contains(_lastMousePosition))
            {
                int cursorSafeSourceWidth = Math.Max(1, PresentationSourceWidth - MouseSpriteGuardHiresPixelsX);
                double scaleX = presentation.Width > 0 ? cursorSafeSourceWidth / (double)presentation.Width : 1.0;
                double scaleY = presentation.Height > 0 ? PresentationSourceHeight / (double)presentation.Height : 1.0;
                int dx = (int)Math.Round((e.X - _lastMousePosition.X) * scaleX);
                int dy = (int)Math.Round((e.Y - _lastMousePosition.Y) * scaleY);
                QueueBufferedMouseMotion(mousePort, dx, dy);
            }

            _lastMousePosition = e.Location;
            _hasLastMousePosition = insidePresentation;
            base.OnMouseMove(e);
        }

        /// <summary>
        /// Handles the Mouse Down callback.
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_overlayMode == OverlayMode.None &&
                GetPresentationRectangle().Contains(e.Location) &&
                TryGetMousePort(out AmigaControlPort mousePort))
            {
                AmigaJoystickButtons button = MapMouseButton(e.Button);
                if (button != AmigaJoystickButtons.None)
                {
                    QueueOrApplyMouseButton(mousePort, button, true);
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
                    QueueOrApplyMouseButton(mousePort, button, false);
                }
            }

            base.OnMouseUp(e);
        }

        /// <summary>
        /// Handles the Mouse Leave callback.
        /// </summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            _hasLastMousePosition = false;
            UpdateHostMouseCapture();
            if (!_hostMouseCaptured)
            {
                ClearBufferedMouseMotion();
            }

            base.OnMouseLeave(e);
        }

        /// <summary>
        /// Captures or refreshes the host mouse clip while this window owns focus.
        /// </summary>
        private bool UpdateHostMouseCapture()
        {
            if (!IsHandleCreated || IsDisposed || WindowState == FormWindowState.Minimized)
            {
                ReleaseHostMouseCapture();
                return false;
            }

            if (!Focused && !ContainsFocus)
            {
                ReleaseHostMouseCapture();
                return false;
            }

            Rectangle presentation = GetPresentationRectangle();
            if (presentation.Width <= 0 || presentation.Height <= 0)
            {
                presentation = ClientRectangle;
            }

            if (presentation.Width <= 0 || presentation.Height <= 0)
            {
                ReleaseHostMouseCapture();
                return false;
            }

            bool centered = false;
            bool newlyCaptured = !_hostMouseCaptured;
            if (newlyCaptured || presentation != _capturedHostMouseClientRectangle)
            {
                NativeRect clip = ToScreenNativeRect(presentation);
                ClipCursor(ref clip);
                _capturedHostMouseClientRectangle = presentation;
                _hostMouseCaptured = true;
                CenterHostMouse(GetHostMouseCaptureCenter(presentation));
                centered = true;
            }

            if (!ReferenceEquals(Cursor, _hiddenHostCursor))
            {
                Cursor = _hiddenHostCursor;
            }

            return centered;
        }

        /// <summary>
        /// Releases the host mouse clip and restores the normal Windows cursor.
        /// </summary>
        private void ReleaseHostMouseCapture()
        {
            if (_hostMouseCaptured)
            {
                ClipCursor(IntPtr.Zero);
                _hostMouseCaptured = false;
                _capturedHostMouseClientRectangle = Rectangle.Empty;
            }

            _pendingHostMouseWarp = false;

            if (!ReferenceEquals(Cursor, Cursors.Default))
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Returns the client-space center used by relative mouse capture.
        /// </summary>
        private static Point GetHostMouseCaptureCenter(Rectangle rectangle)
        {
            return new Point(
                rectangle.Left + Math.Max(0, rectangle.Width / 2),
                rectangle.Top + Math.Max(0, rectangle.Height / 2));
        }

        /// <summary>
        /// Moves the host cursor back to the capture center.
        /// </summary>
        private void CenterHostMouse(Point clientPoint)
        {
            Point screenPoint = PointToScreen(clientPoint);
            if (Cursor.Position != screenPoint)
            {
                _pendingHostMouseWarp = true;
                _pendingHostMouseWarpScreenPoint = screenPoint;
                Cursor.Position = screenPoint;
            }
        }

        /// <summary>
        /// Consumes the MouseMove generated by Cursor.Position when it lands at the capture center.
        /// </summary>
        private bool TryConsumeHostMouseWarp(Point clientPoint)
        {
            if (!_pendingHostMouseWarp)
            {
                return false;
            }

            Point screenPoint = PointToScreen(clientPoint);
            int dx = Math.Abs(screenPoint.X - _pendingHostMouseWarpScreenPoint.X);
            int dy = Math.Abs(screenPoint.Y - _pendingHostMouseWarpScreenPoint.Y);
            _pendingHostMouseWarp = false;
            return dx <= 2 && dy <= 2;
        }

        /// <summary>
        /// Converts a client rectangle into the Win32 screen rectangle used by ClipCursor.
        /// </summary>
        private NativeRect ToScreenNativeRect(Rectangle clientRectangle)
        {
            Point topLeft = PointToScreen(clientRectangle.Location);
            Point bottomRight = PointToScreen(new Point(clientRectangle.Right, clientRectangle.Bottom));
            return new NativeRect
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = bottomRight.X,
                Bottom = bottomRight.Y
            };
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
                case OverlayMode.HardDisk: DrawHardDiskOverlay(graphics); break;
            }
        }

        /// <summary>
        /// Handles the Form Closed callback.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseHostMouseCapture();
            FlushMountedMedia();
            SaveBatteryClock();
            _smallFont.Dispose();
            _titleFont.Dispose();
            _audioOutput.Dispose();
            if (!ReferenceEquals(_hiddenHostCursor, Cursors.Default))
            {
                _hiddenHostCursor.Dispose();
            }

            if (_system != null)
            {
                DetachAudioFromSystem(_system);
                _system.Dispose();
            }
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Handles focus loss by releasing all emulated input buttons.
        /// </summary>
        protected override void OnDeactivate(EventArgs e)
        {
            ReleaseHostMouseCapture();
            ReleaseAllHostInput();
            base.OnDeactivate(e);
        }

        /// <summary>
        /// Captures the host mouse again when the emulator regains focus.
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Refreshes the host mouse clip after window moves.
        /// </summary>
        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Refreshes the host mouse clip after window resizes.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Handles Overlay Key from the host UI or emulator core.
        /// </summary>
        private bool HandleOverlayKey(Keys key)
        {
            if (_overlayMode == OverlayMode.SaveStates)
            {
                return HandleSaveOverlayKey(key);
            }

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

            return false;
        }

        /// <summary>
        /// Runs Toggle Overlay.
        /// </summary>
        private void ToggleOverlay(OverlayMode mode)
        {
            if (_overlayMode == mode)
            {
                if (_overlayMode == OverlayMode.SaveStates)
                {
                    _saveDeleteConfirmVisible = false;
                    _pendingDeleteSavePath = null;
                }

                _previousOverlayMode = OverlayMode.None;
                _overlayMode = OverlayMode.None;
                UpdateHostMouseCapture();
                return;
            }

            ShowOverlay(mode);
        }

        /// <summary>
        /// Runs Show Overlay.
        /// </summary>
        private void ShowOverlay(OverlayMode mode)
        {
            _previousOverlayMode = _overlayMode == OverlayMode.None ? OverlayMode.None : _overlayMode;
            _overlayMode = mode;
            if (mode == OverlayMode.SaveStates)
            {
                OpenSaveOverlay();
            }

            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Runs Close Or Back Overlay.
        /// </summary>
        private void CloseOrBackOverlay()
        {
            if (_overlayMode == OverlayMode.SaveStates)
            {
                CloseSaveOverlay();
                return;
            }

            if (_overlayMode == OverlayMode.Media ||
                _overlayMode == OverlayMode.Machine ||
                _overlayMode == OverlayMode.Roms ||
                _overlayMode == OverlayMode.Input ||
                _overlayMode == OverlayMode.Reset ||
                _overlayMode == OverlayMode.Display)
            {
                _overlayMode = _previousOverlayMode == OverlayMode.None ? OverlayMode.MainMenu : _previousOverlayMode;
                _previousOverlayMode = OverlayMode.None;
                UpdateHostMouseCapture();
                return;
            }

            _overlayMode = OverlayMode.None;
            _previousOverlayMode = OverlayMode.None;
            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Runs Move Selection.
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
                case OverlayMode.Settings: return 18;
                case OverlayMode.MainMenu: return _mainMenuItems.Length;
                case OverlayMode.Display: return 7;
                case OverlayMode.Media: return Math.Max(1, _mediaEntries.Count + 3);
                case OverlayMode.Machine: return 9;
                case OverlayMode.Roms: return Math.Max(1, _romEntries.Count + 3);
                case OverlayMode.Input: return 5;
                case OverlayMode.Reset: return 4;
                case OverlayMode.SaveStates: return Math.Max(1, _saveOverlayEntries.Count);
                case OverlayMode.HardDisk: return 8;
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
                case OverlayMode.HardDisk: return _hardDiskSelection;
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
                case OverlayMode.SaveStates: _saveSelection = value; ClampSaveOverlayScroll(); break;
                case OverlayMode.HardDisk: _hardDiskSelection = value; break;
            }
        }

        /// <summary>
        /// Runs Change Selected Item.
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
                case OverlayMode.HardDisk: ChangeHardDiskItem(direction); break;
            }
        }

        /// <summary>
        /// Runs Activate Selected Item.
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
                    ActivateSelectedSaveOverlayEntry();
                    break;
                case OverlayMode.HardDisk: ActivateHardDiskItem(); break;
            }
        }

        /// <summary>
        /// Runs Activate Main Menu Item.
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
                case 6: ShowOverlay(OverlayMode.HardDisk); break;
            }
        }

        /// <summary>
        /// Runs Change Settings Item.
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
                case 5: ApplyMonitorApertureMode(CycleEnum(_monitorApertureMode, direction)); break;
                case 6: CycleRenderLimit(); break;
                case 7: ToggleFullscreen(); break;
                case 8: ToggleTurboMode(); break;
                case 9: ApplyFloppySpeedMode(CycleEnum(_floppySpeedMode, direction)); break;
                case 10: _inputPortMode = CycleEnum(_inputPortMode, direction); ApplyInputPortMode(); break;
                case 11: ApplyRamProfile(CycleMachineRam(direction)); break;
                case 12: _debugOverlayVisible = !_debugOverlayVisible; break;
                case 13: ToggleDriveOverlay(); break;
                case 14: RefreshRomEntries(); ShowOverlay(OverlayMode.Roms); break;
                case 15: ApplyAccuracyProfile(CycleEnum(_accuracyProfile, direction)); break;
                case 16: ApplyResetMode(CycleEnum(_resetMode, direction)); break;
                case 17: ShowOverlay(OverlayMode.Reset); break;
            }
        }

        /// <summary>
        /// Runs Change Display Item.
        /// </summary>
        private void ChangeDisplayItem(int direction)
        {
            switch (_displaySelection)
            {
                case 0: ApplyVideoFilterMode(CycleEnum(_videoFilterMode, direction)); break;
                case 1: ApplyVideoUpscaleMode(CycleEnum(_videoUpscaleMode, direction)); break;
                case 2: ApplyMonitorApertureMode(CycleEnum(_monitorApertureMode, direction)); break;
                case 3: CycleRenderLimit(); break;
                case 4: ToggleFullscreen(); break;
                case 5: _debugOverlayVisible = !_debugOverlayVisible; break;
                case 6: TogglePresentationStretch(); break;
            }
        }

        /// <summary>
        /// Runs Change Machine Item.
        /// </summary>
        private void ChangeMachineItem(int direction)
        {
            switch (_machineSelection)
            {
                case 0: ApplyMachineModel(CycleEnum(_machineModel, direction)); break;
                case 1: ApplyRamProfile(CycleMachineRam(direction)); break;
                case 2: ApplyFloppySpeedMode(CycleEnum(_floppySpeedMode, direction)); break;
                case 3: ToggleDiskWriteProtection(); break;
                case 4: RefreshRomEntries(); ShowOverlay(OverlayMode.Roms); break;
                case 5: ApplyAccuracyProfile(CycleEnum(_accuracyProfile, direction)); break;
                case 6: ApplyResetMode(CycleEnum(_resetMode, direction)); break;
                case 7: ShowOverlay(OverlayMode.Reset); break;
                case 8: CyclePcmciaSram(direction); break;
            }
        }

        /// <summary>
        /// Runs Change Input Item.
        /// </summary>
        private void ChangeInputItem(int direction)
        {
            switch (_inputSelection)
            {
                case 0: _inputPortMode = CycleEnum(_inputPortMode, direction); ApplyInputPortMode(); break;
                case 1: _statusText = "KEYBOARD LAYOUT PC"; break;
                case 2: _statusText = "MOUSE SENSITIVITY NORMAL"; break;
                case 3: _gamepads.Poll(true); _statusText = "GAMEPAD " + FormatGamepadStatus(); break;
                case 4: _statusText = "INPUT DEFAULTS RESTORED"; _inputPortMode = InputPortMode.MousePort1JoystickPort2; ApplyInputPortMode(); break;
            }
        }

        /// <summary>
        /// Runs Change Reset Item.
        /// </summary>
        private void ChangeResetItem(int direction)
        {
            if (_resetSelection == 0)
            {
                ApplyResetMode(CycleEnum(_resetMode, direction));
            }
        }

        /// <summary>
        /// Runs Activate Reset Item.
        /// </summary>
        private void ActivateResetItem()
        {
            if (_resetSelection == 0)
            {
                ApplyResetMode(CycleEnum(_resetMode, 1));
                return;
            }

            if (_resetSelection == 3)
            {
                CloseOrBackOverlay();
                return;
            }

            bool powerCycle = _resetSelection == 2 || _resetMode == ResetMode.Power;
            MountedMediaInfo media = _mountedMedia;
            _system.Reset(powerCycle);
            ResetAudioOutput();
            if (powerCycle)
            {
                LoadConfiguredKickstart();
                ScheduleDeferredMediaMount(media);
            }
            else if (_resetMode == ResetMode.Remount)
            {
                RemountMedia(media);
            }

            ResetEmulationClock();
            _statusText = FormatResetMode(_resetMode) + " RESET COMPLETE";
            _overlayMode = OverlayMode.None;
        }

        /// <summary>
        /// Runs Activate Media Item.
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
                try
                {
                    ClearDeferredMediaMount();
                    _system.Bus.FloppyController.EjectDrive0();
                    _mountedMedia = MountedMediaInfo.None;
                    SaveMountedMediaSettings();
                    _statusText = "DF0 EJECTED";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    _statusText = "DF0 EJECT FAILED";
                }

                return;
            }

            if (_mediaSelection == 2)
            {
                try
                {
                    _mediaDirectory = Directory.GetParent(_mediaDirectory)?.FullName ?? _mediaDirectory;
                    SaveMediaDirectorySetting();
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
                SaveMediaDirectorySetting();
                RefreshMediaEntries();
                return;
            }

            try
            {
                ClearDeferredMediaMount();
                FlushMountedMedia();
                AdfImage image = AdfImage.Load(path);
                _system.Bus.FloppyController.InsertDrive0(image, _floppyWriteProtected);
                _mountedMedia = MountedMediaInfo.ForFloppy(0, Path.GetFileName(path), path, _floppyWriteProtected);
                SaveMountedMediaSettings();
                _statusText = "DF0 MOUNTED " + _mountedMedia.DisplayName;
            }
            catch
            {
                _statusText = "DF0 MOUNT FAILED";
            }
        }

        /// <summary>
        /// Runs Activate Rom Item.
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
        /// Opens the savestate overlay in browser mode.
        /// </summary>
        private void OpenSaveOverlay()
        {
            _saveDeleteConfirmVisible = false;
            _pendingDeleteSavePath = null;
            if (string.IsNullOrWhiteSpace(_saveOverlayCurrentDirectory))
            {
                _saveOverlayCurrentDirectory = GetSaveDirectory();
            }

            RefreshSaveStateSlots();
            if (!string.IsNullOrWhiteSpace(_lastLoadedSaveStatePath) && File.Exists(_lastLoadedSaveStatePath))
            {
                SelectSaveStatePath(_lastLoadedSaveStatePath);
            }
        }

        /// <summary>
        /// Closes the savestate overlay and clears pending popup state.
        /// </summary>
        private void CloseSaveOverlay()
        {
            _overlayMode = OverlayMode.None;
            _previousOverlayMode = OverlayMode.None;
            _saveDeleteConfirmVisible = false;
            _pendingDeleteSavePath = null;
            UpdateHostMouseCapture();
        }

        /// <summary>
        /// Handles savestate browser key input.
        /// </summary>
        private bool HandleSaveOverlayKey(Keys key)
        {
            if (_saveDeleteConfirmVisible)
            {
                return HandleSaveDeleteConfirmKey(key);
            }

            switch (key)
            {
                case Keys.Up:
                    MoveSaveOverlaySelection(-1);
                    return true;
                case Keys.Down:
                    MoveSaveOverlaySelection(1);
                    return true;
                case Keys.PageUp:
                    MoveSaveOverlaySelection(-SaveOverlayVisibleRows);
                    return true;
                case Keys.PageDown:
                    MoveSaveOverlaySelection(SaveOverlayVisibleRows);
                    return true;
                case Keys.Home:
                    _saveSelection = 0;
                    ClampSaveOverlayScroll();
                    return true;
                case Keys.End:
                    _saveSelection = Math.Max(0, _saveOverlayEntries.Count - 1);
                    ClampSaveOverlayScroll();
                    return true;
                case Keys.F5:
                case Keys.S:
                    SaveCurrentState();
                    return true;
                case Keys.Enter:
                case Keys.Space:
                case Keys.L:
                    ActivateSelectedSaveOverlayEntry();
                    return true;
                case Keys.Delete:
                    OpenSaveDeleteConfirmation();
                    return true;
                case Keys.Escape:
                    CloseSaveOverlay();
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Saves the current machine state into the mounted-media subdirectory.
        /// </summary>
        private void SaveCurrentState()
        {
            try
            {
                string saveDirectory = SaveStateFile.GetSaveDirectoryForMedia(GetSaveDirectory(), _mountedMedia);
                string savePath = SaveStateFile.CreateSavePath(saveDirectory);
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
                SelectSaveStatePath(savePath);
                _statusText = "SAVED " + Path.GetFileName(savePath).ToUpperInvariant();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE FAILED";
            }
        }

        /// <summary>
        /// Opens a directory or loads the selected savestate.
        /// </summary>
        private void ActivateSelectedSaveOverlayEntry()
        {
            if (_saveOverlayEntries.Count == 0)
            {
                _statusText = "NO SAVE SELECTED";
                return;
            }

            SaveOverlayEntry selectedEntry = _saveOverlayEntries[_saveSelection];
            if (selectedEntry.IsDirectory)
            {
                _saveOverlayCurrentDirectory = selectedEntry.Path;
                _saveSelection = 0;
                _saveOverlayScroll = 0;
                RefreshSaveStateSlots();
                _statusText = selectedEntry.IsParent ? "UP" : "OPEN " + TrimText(selectedEntry.DisplayName, 32);
                return;
            }

            LoadSelectedSaveState(selectedEntry.Metadata);
        }

        /// <summary>
        /// Loads the given savestate metadata entry.
        /// </summary>
        private void LoadSelectedSaveState(SaveStateSlotMetadata slot)
        {
            if (slot == null)
            {
                _statusText = "NO SAVE SELECTED";
                return;
            }

            if (slot.Status == SaveStateSlotStatus.Incompatible)
            {
                _statusText = "SAVE INCOMPATIBLE";
                return;
            }

            if (slot.Status == SaveStateSlotStatus.Corrupt)
            {
                _statusText = "SAVE CORRUPT";
                return;
            }

            try
            {
                FlushMountedMedia();
                RamProfile targetRamProfile = GetRamProfileFromSettings(slot.RamProfile, _ramProfile);
                bool needsRecreate = _ramProfile != targetRamProfile || _accuracyProfile != slot.AccuracyProfile || _machineModel != slot.MachineModel;
                _machineModel = slot.MachineModel;
                _ramProfile = targetRamProfile;
                _accuracyProfile = slot.AccuracyProfile;
                _settings.RamProfile = GetSettingsRamProfile(_ramProfile);
                _settings.MachineModel = _machineModel;
                _settings.AccuracyProfile = _accuracyProfile;
                AmigaEmulatorSettingsStore.Save(_settings);
                if (needsRecreate)
                {
                    RecreateSystemForCurrentProfile(false);
                }
                else
                {
                    _system.SetAccuracyOptions(CreateAccuracyOptions(_accuracyProfile));
                }

                SaveStateFile.Load(slot.Path, _system);
                SynchronizeRestoredMachineSettings(true);
                ApplyFloppyTiming();
                _system.PresentedFrameBuffer.CopyTo(Pixels);
                _mountedMedia = slot.MountedMedia ?? MountedMediaInfo.None;
                _lastLoadedSaveStatePath = slot.Path;
                SaveMountedMediaSettings();
                ResetAudioOutput();
                ResetEmulationClock();
                _statusText = "SAVE LOADED";
                CloseSaveOverlay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE LOAD FAILED";
                RefreshSaveStateSlots();
            }
        }

        /// <summary>
        /// Opens a yes/no confirmation before deleting a savestate.
        /// </summary>
        private void OpenSaveDeleteConfirmation()
        {
            if (_saveOverlayEntries.Count == 0)
            {
                _statusText = "NO SAVE SELECTED";
                return;
            }

            SaveOverlayEntry selectedEntry = _saveOverlayEntries[_saveSelection];
            if (selectedEntry.IsDirectory || selectedEntry.Metadata == null)
            {
                _statusText = "SELECT A SAVE";
                return;
            }

            _pendingDeleteSavePath = selectedEntry.Metadata.Path;
            _saveDeleteConfirmYesSelected = false;
            _saveDeleteConfirmVisible = true;
            _statusText = "CONFIRM DELETE";
        }

        /// <summary>
        /// Handles keys in the savestate delete confirmation popup.
        /// </summary>
        private bool HandleSaveDeleteConfirmKey(Keys key)
        {
            switch (key)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    _saveDeleteConfirmYesSelected = !_saveDeleteConfirmYesSelected;
                    return true;
                case Keys.Enter:
                    if (_saveDeleteConfirmYesSelected)
                    {
                        DeleteSelectedSaveStateConfirmed();
                    }
                    else
                    {
                        CancelSaveDeleteConfirmation();
                    }

                    return true;
                case Keys.Escape:
                    CancelSaveDeleteConfirmation();
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Cancels the pending savestate delete operation.
        /// </summary>
        private void CancelSaveDeleteConfirmation()
        {
            _saveDeleteConfirmVisible = false;
            _pendingDeleteSavePath = null;
            _statusText = "DELETE CANCELED";
        }

        /// <summary>
        /// Deletes the currently selected savestate file.
        /// </summary>
        private void DeleteSelectedSaveStateConfirmed()
        {
            if (string.IsNullOrWhiteSpace(_pendingDeleteSavePath))
            {
                _saveDeleteConfirmVisible = false;
                _statusText = "NO SAVE SELECTED";
                return;
            }

            try
            {
                string path = _pendingDeleteSavePath;
                string deletedSaveDirectory = Path.GetDirectoryName(path);
                File.Delete(path);
                if (string.Equals(_lastLoadedSaveStatePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _lastLoadedSaveStatePath = null;
                }

                bool deletedDirectory = TryDeleteEmptySaveSubdirectory(deletedSaveDirectory);
                _saveDeleteConfirmVisible = false;
                _pendingDeleteSavePath = null;
                _statusText = deletedDirectory ? "SAVE DIR DELETED" : "SAVE DELETED";
                RefreshSaveStateSlots();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _saveDeleteConfirmVisible = false;
                _pendingDeleteSavePath = null;
                _statusText = "SAVE DELETE FAILED";
            }
        }

        /// <summary>
        /// Runs Calculate Cycles For Update.
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
            _lastTitleUpdateSeconds = _lastUpdateSeconds;
            _pendingChipCycles = 0.0;
            _perfChipCycles = 0;
            _perfFrameCounter = _system.FrameCounter;
            _presentedFps = _system.Model.FrameRateHz;
            _smoothedCpuMhz = (_system.Model.ChipClockHz * 2.0) / 1000000.0;
            ClearBufferedMouseMotion();
        }

        /// <summary>
        /// Runs chip cycles while draining buffered host mouse motion in poll-safe slices.
        /// </summary>
        private void RunChipCyclesWithBufferedMouse(int cycles)
        {
            int remaining = cycles;
            while (remaining > 0)
            {
                DrainBufferedMouseMotionSlice();
                ApplyQueuedMouseButtonsIfReady();
                int slice = Math.Min(remaining, MouseDeltaDrainSliceCycles);
                _system.RunChipCycles(slice);
                AdvanceDeferredMouseButtons(slice);
                remaining -= slice;
            }
        }

        /// <summary>
        /// Queues relative host mouse motion for timed injection into JOYxDAT counters.
        /// </summary>
        private void QueueBufferedMouseMotion(AmigaControlPort port, int dx, int dy)
        {
            if (dx == 0 && dy == 0)
            {
                return;
            }

            _pendingMousePort = port;
            _pendingMouseDx += dx;
            _pendingMouseDy += dy;
        }

        /// <summary>
        /// Queues mouse buttons behind buffered motion or applies them immediately when safe.
        /// </summary>
        private void QueueOrApplyMouseButton(AmigaControlPort port, AmigaJoystickButtons button, bool pressed)
        {
            _pendingMousePort = port;
            if (pressed)
            {
                if (HasBufferedMouseMotion())
                {
                    _queuedMouseDownButtons |= button;
                    _queuedMouseUpButtons &= ~button;
                }
                else
                {
                    _system.Input.SetMouseButton(port, button, true);
                }

                return;
            }

            if (HasBufferedMouseMotion() ||
                (_queuedMouseDownButtons & button) != 0 ||
                (_deferredHeldMouseButtons & button) != 0)
            {
                _queuedMouseUpButtons |= button;
                return;
            }

            _system.Input.SetMouseButton(port, button, false);
        }

        /// <summary>
        /// Injects one bounded mouse-motion slice into the emulated input counters.
        /// </summary>
        private void DrainBufferedMouseMotionSlice()
        {
            if (_pendingMouseDx == 0 && _pendingMouseDy == 0)
            {
                return;
            }

            int dx = ClampMouseDelta(_pendingMouseDx);
            int dy = ClampMouseDelta(_pendingMouseDy);
            _pendingMouseDx -= dx;
            _pendingMouseDy -= dy;
            _system.Input.MoveMouse(_pendingMousePort, dx, dy);
        }

        /// <summary>
        /// Applies queued mouse-button presses after all buffered motion has reached JOYxDAT.
        /// </summary>
        private void ApplyQueuedMouseButtonsIfReady()
        {
            if (HasBufferedMouseMotion() || _queuedMouseDownButtons == AmigaJoystickButtons.None)
            {
                return;
            }

            SetMouseButtons(_pendingMousePort, _queuedMouseDownButtons, true);
            _deferredHeldMouseButtons |= _queuedMouseDownButtons;
            _queuedMouseDownButtons = AmigaJoystickButtons.None;
            _deferredMouseButtonHoldCycles = Math.Max(_deferredMouseButtonHoldCycles, DeferredMouseButtonHoldCycles);
        }

        /// <summary>
        /// Releases deferred mouse buttons after a minimum emulated hold time.
        /// </summary>
        private void AdvanceDeferredMouseButtons(int cycles)
        {
            if (_deferredHeldMouseButtons == AmigaJoystickButtons.None || _queuedMouseUpButtons == AmigaJoystickButtons.None)
            {
                return;
            }

            if (_deferredMouseButtonHoldCycles > 0)
            {
                _deferredMouseButtonHoldCycles -= cycles;
                if (_deferredMouseButtonHoldCycles > 0)
                {
                    return;
                }
            }

            AmigaJoystickButtons release = _queuedMouseUpButtons & _deferredHeldMouseButtons;
            if (release == AmigaJoystickButtons.None)
            {
                return;
            }

            SetMouseButtons(_pendingMousePort, release, false);
            _deferredHeldMouseButtons &= ~release;
            _queuedMouseUpButtons &= ~release;
        }

        /// <summary>
        /// Clears queued host mouse motion that should not survive focus or timing resets.
        /// </summary>
        private void ClearBufferedMouseMotion()
        {
            if (_system != null && _deferredHeldMouseButtons != AmigaJoystickButtons.None)
            {
                SetMouseButtons(_pendingMousePort, _deferredHeldMouseButtons, false);
            }

            _pendingMouseDx = 0;
            _pendingMouseDy = 0;
            _queuedMouseDownButtons = AmigaJoystickButtons.None;
            _queuedMouseUpButtons = AmigaJoystickButtons.None;
            _deferredHeldMouseButtons = AmigaJoystickButtons.None;
            _deferredMouseButtonHoldCycles = 0;
        }

        /// <summary>
        /// Returns whether host mouse motion is waiting to be injected.
        /// </summary>
        private bool HasBufferedMouseMotion()
        {
            return _pendingMouseDx != 0 || _pendingMouseDy != 0;
        }

        /// <summary>
        /// Applies a set of mouse button bits to the current emulated mouse port.
        /// </summary>
        private void SetMouseButtons(AmigaControlPort port, AmigaJoystickButtons buttons, bool pressed)
        {
            if ((buttons & AmigaJoystickButtons.Fire) != 0)
            {
                _system.Input.SetMouseButton(port, AmigaJoystickButtons.Fire, pressed);
            }

            if ((buttons & AmigaJoystickButtons.Second) != 0)
            {
                _system.Input.SetMouseButton(port, AmigaJoystickButtons.Second, pressed);
            }

            if ((buttons & AmigaJoystickButtons.Third) != 0)
            {
                _system.Input.SetMouseButton(port, AmigaJoystickButtons.Third, pressed);
            }
        }

        /// <summary>
        /// Clamps a pending mouse delta to one poll-safe injection slice.
        /// </summary>
        private static int ClampMouseDelta(int value)
        {
            if (value > MaxMouseDeltaPerSlice)
            {
                return MaxMouseDeltaPerSlice;
            }

            if (value < -MaxMouseDeltaPerSlice)
            {
                return -MaxMouseDeltaPerSlice;
            }

            return value;
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
            long frameCounter = _system.FrameCounter;
            long advancedFrames = Math.Max(0, frameCounter - _perfFrameCounter);
            _presentedFps = advancedFrames / elapsed;
            _perfFrameCounter = frameCounter;
            _perfChipCycles = 0;
            _lastPerfSeconds = now;
        }

        /// <summary>
        /// Updates Title Status from current emulator state.
        /// </summary>
        private void UpdateTitleStatus()
        {
            double now = _stopwatch.Elapsed.TotalSeconds;
            if (now - _lastTitleUpdateSeconds < 0.25)
            {
                return;
            }

            _lastTitleUpdateSeconds = now;
            Text = string.Format(
                "Amiga Emulator {0}{1:F2} MHz HOST:{2:F0}% - PC:{3:X6} OPC:{4:X4} - FPS:{5} - DF0:{6}",
                _turboMode ? "TURBO " : string.Empty,
                _smoothedCpuMhz,
                _hostSpeedPercent,
                _system.Cpu.PC,
                _system.Cpu.CurrentOpcode,
                (int)Math.Round(_presentedFps),
                FormatTitleDriveStatus());
        }

        /// <summary>
        /// Runs Format Title Drive Status.
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
        private RamProfile CycleMachineRam(int direction)
        {
            RamProfile candidate = _ramProfile;
            do { candidate = CycleEnum(candidate, direction); }
            while (AmigaModel.NormalizeRamProfile(_machineModel, GetSettingsRamProfile(candidate)) != GetSettingsRamProfile(candidate));
            return candidate;
        }

        private void ApplyMachineModel(AmigaMachineModel model)
        {
            if (_machineModel == model) return;
            _machineModel = model;
            _settings.MachineModel = model;
            _selectedRomPath = string.Empty;
            _settings.CustomKickstartPath = string.Empty;
            _settings.RamProfile = AmigaModel.NormalizeRamProfile(model, GetSettingsRamProfile(_ramProfile));
            _ramProfile = GetRamProfileFromSettings(_settings.RamProfile, _ramProfile);
            AmigaEmulatorSettingsStore.Save(_settings);
            RecreateSystemForCurrentProfile();
            _statusText = _system.Model.Name + " - RESET";
        }

        private void ApplyRamProfile(RamProfile profile)
        {
            profile = GetRamProfileFromSettings(AmigaModel.NormalizeRamProfile(_machineModel, GetSettingsRamProfile(profile)), profile);
            if (_ramProfile == profile && _system != null)
            {
                return;
            }

            _ramProfile = profile;
            _settings.RamProfile = GetSettingsRamProfile(profile);
            _settings.MachineModel = _machineModel;
            AmigaEmulatorSettingsStore.Save(_settings);
            RecreateSystemForCurrentProfile();
            _statusText = "RAM " + FormatRamProfile(_ramProfile);
        }

        /// <summary>
        /// Runs Recreate System For Current Profile.
        /// </summary>
        private void RecreateSystemForCurrentProfile(bool initializeDevices = true)
        {
            MountedMediaInfo media = _mountedMedia;
            AmigaSystem oldSystem = _system;
            FlushMountedMedia();
            SaveBatteryClock();
            DetachAudioFromSystem(oldSystem);
            _system = new AmigaSystem(AmigaModel.Create(_machineModel, GetSettingsRamProfile(_ramProfile)), CreateAccuracyOptions(_accuracyProfile));
            RestoreBatteryClock();
            if (initializeDevices) MountConfiguredPcmcia();
            ApplyFloppyTiming();
            AttachAudioToSystem();
            oldSystem?.Dispose();
            ApplyInputPortMode();

            if (initializeDevices)
            {
                LoadConfiguredKickstart();
                MountConfiguredHardDisk();
                ScheduleDeferredMediaMount(media);
            }
            UpdateFrequency = GetRenderUpdateFrequency(_renderLimitMode);
            ResetEmulationClock();
        }

        /// <summary>
        /// Runs Remount Media.
        /// </summary>
        private void RemountMedia(MountedMediaInfo media)
        {
            ClearDeferredMediaMount();
            if (media == null || !media.HasMedia || media.Kind != MountedMediaKind.Adf || string.IsNullOrWhiteSpace(media.HostPath) || !File.Exists(media.HostPath))
            {
                _mountedMedia = MountedMediaInfo.None;
                SaveMountedMediaSettings();
                return;
            }

            try
            {
                FlushMountedMedia();
                AdfImage image = AdfImage.Load(media.HostPath);
                _system.Bus.FloppyController.InsertDrive0(image, media.IsWriteProtected);
                _mountedMedia = MountedMediaInfo.ForFloppy(media.DriveIndex < 0 ? 0 : media.DriveIndex, media.DisplayName, media.HostPath, media.IsWriteProtected);
                SaveMountedMediaSettings();
            }
            catch
            {
                _mountedMedia = media.WithStatus(MountedMediaStatus.Error, "REMOUNT FAILED");
            }
        }

        /// <summary>
        /// Schedules the configured DF0 image from settings when present.
        /// </summary>
        private void ScheduleConfiguredMediaRemount()
        {
            if (_settings.HardDiskEnabled && _settings.BootFromHardDisk) return;
            string path = _settings.Df0Path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _mountedMedia = MountedMediaInfo.None;
                ClearDeferredMediaMount();
                return;
            }

            ScheduleDeferredMediaMount(MountedMediaInfo.ForFloppy(0, Path.GetFileName(path), path, _floppyWriteProtected));
        }

        /// <summary>
        /// Queues an automatic DF0 remount until Kickstart has run enough frames to accept media reliably.
        /// </summary>
        private void ScheduleDeferredMediaMount(MountedMediaInfo media)
        {
            if (media == null || !media.HasMedia || media.Kind != MountedMediaKind.Adf)
            {
                ClearDeferredMediaMount();
                return;
            }

            _deferredMediaMount = media;
            int cyclesPerFrame = _system.Model.ChipCyclesPerLine * _system.Model.LinesPerFrame;
            long delayFrames = Math.Max(1, (DeferredMediaMountChipCycles + cyclesPerFrame - 1) / cyclesPerFrame);
            _deferredMediaMountFrame = _system.FrameCounter + delayFrames;
            _mountedMedia = media.WithStatus(MountedMediaStatus.Loading, "WAITING FOR KICKSTART");
            _statusText = "DF0 QUEUED " + media.DisplayName;
        }

        /// <summary>
        /// Mounts queued DF0 media after the deferred startup window has elapsed.
        /// </summary>
        private void CompleteDeferredMediaMountIfReady()
        {
            if (_deferredMediaMount == null || !_deferredMediaMount.HasMedia || _deferredMediaMountFrame < 0)
            {
                return;
            }

            if (_system.FrameCounter < _deferredMediaMountFrame)
            {
                return;
            }

            MountedMediaInfo media = _deferredMediaMount;
            ClearDeferredMediaMount();
            RemountMedia(media);
            if (_mountedMedia.HasMedia && _mountedMedia.Status == MountedMediaStatus.Mounted)
            {
                _statusText = "DF0 MOUNTED " + _mountedMedia.DisplayName;
            }
        }

        /// <summary>
        /// Clears any pending automatic DF0 remount.
        /// </summary>
        private void ClearDeferredMediaMount()
        {
            _deferredMediaMount = MountedMediaInfo.None;
            _deferredMediaMountFrame = -1;
        }

        /// <summary>
        /// Flushes dirty writable media and reports failures in the overlay status.
        /// </summary>
        private void FlushMountedMedia()
        {
            try
            {
                if (_system != null)
                {
                    _system.Bus.FloppyController.FlushDrive0();
                    _system.Bus.HardDisk.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "DISK FLUSH FAILED";
            }
        }

        /// <summary>
        /// Stores current media browser directory.
        /// </summary>
        private void SaveMediaDirectorySetting()
        {
            _settings.MediaBrowserDirectory = _mediaDirectory ?? string.Empty;
            AmigaEmulatorSettingsStore.Save(_settings);
        }

        /// <summary>
        /// Stores mounted DF0 path settings.
        /// </summary>
        private void SaveMountedMediaSettings()
        {
            _settings.Df0Path = _mountedMedia != null && _mountedMedia.HasMedia && _mountedMedia.Kind == MountedMediaKind.Adf
                ? _mountedMedia.HostPath ?? string.Empty
                : string.Empty;
            _settings.Df1Path = string.Empty;
            _settings.Df2Path = string.Empty;
            _settings.Df3Path = string.Empty;
            AmigaEmulatorSettingsStore.Save(_settings);
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
                    return AmigaModel.A500Pal1MbChip;
                case RamProfile.Chip2Mb:
                    return AmigaModel.A500Pal2MbChip;
                case RamProfile.Chip2MbFast8Mb:
                    return AmigaModel.A500Pal2MbChipFast8Mb;
                case RamProfile.Chip1MbFast2Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip1MbFast2Mb);
                case RamProfile.Chip1MbFast4Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip1MbFast4Mb);
                case RamProfile.Chip2MbFast2Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip2MbFast2Mb);
                case RamProfile.Chip2MbFast4Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip2MbFast4Mb);
                case RamProfile.Chip512KSlow512KFast2Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip512KSlow512KFast2Mb);
                case RamProfile.Chip512KSlow512KFast4Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip512KSlow512KFast4Mb);
                case RamProfile.Chip512KSlow512KFast8Mb: return AmigaModel.ForRamProfile(AmigaRamProfile.Chip512KSlow512KFast8Mb);
                default:
                    return AmigaModel.A500Pal512Slow512;
            }
        }

        /// <summary>
        /// Returns the RAM Profile For Model.
        /// </summary>
        private static RamProfile GetRamProfileForModel(AmigaModel model)
        {
            foreach (AmigaRamProfile candidate in Enum.GetValues<AmigaRamProfile>())
            {
                var info = AmigaOverlaySettingCatalog.GetRamProfileInfo(candidate);
                if (info.ChipRamBytes == model.ChipRamBytes && info.SlowRamBytes == model.SlowRamBytes && info.FastRamBytes == model.FastRamBytes)
                    return GetRamProfileFromSettings(candidate, RamProfile.Chip512Slow512);
            }
            if (model == AmigaModel.A500Pal)
            {
                return RamProfile.Chip512;
            }

            if (model == AmigaModel.A500Pal1MbChip)
            {
                return RamProfile.Chip1Mb;
            }

            if (model == AmigaModel.A500Pal2MbChip)
            {
                return RamProfile.Chip2Mb;
            }

            if (model == AmigaModel.A500Pal2MbChipFast8Mb)
            {
                return RamProfile.Chip2MbFast8Mb;
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
                    return RamProfile.Chip1Mb;
                case AmigaRamProfile.Chip2Mb:
                    return RamProfile.Chip2Mb;
                case AmigaRamProfile.Chip2MbFast8Mb:
                    return RamProfile.Chip2MbFast8Mb;
                case AmigaRamProfile.Chip1MbFast2Mb: return RamProfile.Chip1MbFast2Mb;
                case AmigaRamProfile.Chip1MbFast4Mb: return RamProfile.Chip1MbFast4Mb;
                case AmigaRamProfile.Chip2MbFast2Mb: return RamProfile.Chip2MbFast2Mb;
                case AmigaRamProfile.Chip2MbFast4Mb: return RamProfile.Chip2MbFast4Mb;
                case AmigaRamProfile.Chip512KSlow512KFast2Mb: return RamProfile.Chip512KSlow512KFast2Mb;
                case AmigaRamProfile.Chip512KSlow512KFast4Mb: return RamProfile.Chip512KSlow512KFast4Mb;
                case AmigaRamProfile.Chip512KSlow512KFast8Mb: return RamProfile.Chip512KSlow512KFast8Mb;
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
                case RamProfile.Chip2Mb:
                    return AmigaRamProfile.Chip2Mb;
                case RamProfile.Chip2MbFast8Mb:
                    return AmigaRamProfile.Chip2MbFast8Mb;
                case RamProfile.Chip1MbFast2Mb: return AmigaRamProfile.Chip1MbFast2Mb;
                case RamProfile.Chip1MbFast4Mb: return AmigaRamProfile.Chip1MbFast4Mb;
                case RamProfile.Chip2MbFast2Mb: return AmigaRamProfile.Chip2MbFast2Mb;
                case RamProfile.Chip2MbFast4Mb: return AmigaRamProfile.Chip2MbFast4Mb;
                case RamProfile.Chip512KSlow512KFast2Mb: return AmigaRamProfile.Chip512KSlow512KFast2Mb;
                case RamProfile.Chip512KSlow512KFast4Mb: return AmigaRamProfile.Chip512KSlow512KFast4Mb;
                case RamProfile.Chip512KSlow512KFast8Mb: return AmigaRamProfile.Chip512KSlow512KFast8Mb;
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
        /// Runs Refresh Media Entries.
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
        /// Runs Refresh Rom Entries.
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
        /// Reloads savestate metadata from the per-user save directory.
        /// </summary>
        private void RefreshSaveStateSlots()
        {
            var entries = new List<SaveStateSlotMetadata>();
            var overlayEntries = new List<SaveOverlayEntry>();
            try
            {
                string saveDirectory = GetSaveDirectory();
                SaveStateMigration.MigrateFlatSaves(saveDirectory, TextWriter.Null);
                if (string.IsNullOrWhiteSpace(_saveOverlayCurrentDirectory) || !Directory.Exists(_saveOverlayCurrentDirectory))
                {
                    _saveOverlayCurrentDirectory = saveDirectory;
                }

                string currentDirectory = Path.GetFullPath(_saveOverlayCurrentDirectory);
                string rootDirectory = Path.GetFullPath(saveDirectory);
                if (!IsPathInsideOrEqual(currentDirectory, rootDirectory))
                {
                    currentDirectory = rootDirectory;
                    _saveOverlayCurrentDirectory = rootDirectory;
                }

                DirectoryInfo parent = Directory.GetParent(currentDirectory);
                if (parent != null && !string.Equals(
                    currentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    overlayEntries.Add(new SaveOverlayEntry(parent.FullName, "UP", true, true, null));
                }

                string[] directories = Directory.GetDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                foreach (string directory in directories)
                {
                    overlayEntries.Add(new SaveOverlayEntry(directory, "DIR " + Path.GetFileName(directory).ToUpperInvariant(), true, false, null));
                }

                string[] files = Directory.GetFiles(currentDirectory, SaveStateFile.SearchPattern, SearchOption.TopDirectoryOnly);
                foreach (string file in files)
                {
                    try
                    {
                        entries.Add(SaveStateFile.ReadMetadata(file));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        entries.Add(new SaveStateSlotMetadata
                        {
                            SlotIndex = -1,
                            Status = SaveStateSlotStatus.Corrupt,
                            Path = file,
                            DisplayName = "CORRUPT"
                        });
                    }
                }

                entries.Sort((left, right) => right.CreatedLocalTime.CompareTo(left.CreatedLocalTime));
                foreach (SaveStateSlotMetadata entry in entries)
                {
                    overlayEntries.Add(new SaveOverlayEntry(entry.Path, FormatSaveListEntry(entry, 80), false, false, entry));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _statusText = "SAVE DIR FAILED";
            }

            _saveOverlayEntries = overlayEntries;
            if (_saveSelection >= _saveOverlayEntries.Count)
            {
                _saveSelection = Math.Max(0, _saveOverlayEntries.Count - 1);
            }

            if (_saveSelection < 0)
            {
                _saveSelection = 0;
            }

            ClampSaveOverlayScroll();
        }

        /// <summary>
        /// Selects a savestate path in the overlay list.
        /// </summary>
        private void SelectSaveStatePath(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                _saveOverlayCurrentDirectory = directory;
                RefreshSaveStateSlots();
            }

            for (int index = 0; index < _saveOverlayEntries.Count; index++)
            {
                if (!_saveOverlayEntries[index].IsDirectory &&
                    string.Equals(_saveOverlayEntries[index].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    _saveSelection = index;
                    ClampSaveOverlayScroll();
                    return;
                }
            }
        }

        /// <summary>
        /// Moves the savestate overlay list selection.
        /// </summary>
        private void MoveSaveOverlaySelection(int delta)
        {
            if (_saveOverlayEntries.Count == 0)
            {
                return;
            }

            _saveSelection += delta;
            if (_saveSelection < 0)
            {
                _saveSelection = 0;
            }

            if (_saveSelection >= _saveOverlayEntries.Count)
            {
                _saveSelection = _saveOverlayEntries.Count - 1;
            }

            ClampSaveOverlayScroll();
        }

        /// <summary>
        /// Keeps the savestate overlay scroll position around the current selection.
        /// </summary>
        private void ClampSaveOverlayScroll()
        {
            if (_saveSelection < _saveOverlayScroll)
            {
                _saveOverlayScroll = _saveSelection;
            }

            int maxVisibleStart = Math.Max(0, _saveOverlayEntries.Count - SaveOverlayVisibleRows);
            int visibleEnd = _saveOverlayScroll + SaveOverlayVisibleRows - 1;
            if (_saveSelection > visibleEnd)
            {
                _saveOverlayScroll = _saveSelection - (SaveOverlayVisibleRows - 1);
            }

            if (_saveOverlayScroll > maxVisibleStart)
            {
                _saveOverlayScroll = maxVisibleStart;
            }

            if (_saveOverlayScroll < 0)
            {
                _saveOverlayScroll = 0;
            }
        }

        /// <summary>
        /// Gets the per-user savestate directory.
        /// </summary>
        private static string GetSaveDirectory()
        {
            string saveDirectory = UserDataPaths.GetSaveDirectory();
            Directory.CreateDirectory(saveDirectory);
            return saveDirectory;
        }

        /// <summary>
        /// Deletes an empty savestate subdirectory while preserving the savestate root.
        /// </summary>
        private bool TryDeleteEmptySaveSubdirectory(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    return false;
                }

                string rootDirectory = Path.GetFullPath(GetSaveDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidateDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(candidateDirectory, rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !IsPathInsideDirectory(candidateDirectory, rootDirectory) ||
                    Directory.GetFileSystemEntries(candidateDirectory).Length != 0)
                {
                    return false;
                }

                string parentDirectory = Path.GetDirectoryName(candidateDirectory);
                Directory.Delete(candidateDirectory);
                string currentDirectory = string.IsNullOrWhiteSpace(_saveOverlayCurrentDirectory)
                    ? string.Empty
                    : Path.GetFullPath(_saveOverlayCurrentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(currentDirectory, candidateDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    _saveOverlayCurrentDirectory = !string.IsNullOrWhiteSpace(parentDirectory) && IsPathInsideOrEqual(parentDirectory, rootDirectory)
                        ? parentDirectory
                        : rootDirectory;
                    _saveSelection = 0;
                    _saveOverlayScroll = 0;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
        }

        /// <summary>
        /// Checks whether a path is strictly inside a root directory.
        /// </summary>
        private static bool IsPathInsideDirectory(string path, string rootDirectory)
        {
            return path.StartsWith(rootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(rootDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether a path is the root directory or inside it.
        /// </summary>
        private static bool IsPathInsideOrEqual(string path, string rootDirectory)
        {
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                IsPathInsideDirectory(normalizedPath, normalizedRoot);
        }

        /// <summary>
        /// Runs Open Rom Directory.
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
                MenuLine("MONITOR", FormatMonitorAperture(_monitorApertureMode)),
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
                MenuLine("RESET NOW", "OPEN CONFIRM"),
                MenuLine("PCMCIA", FormatPcmciaSram())
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
                MenuLine("MONITOR", FormatMonitorAperture(_monitorApertureMode)),
                MenuLine("RENDER FPS", FormatRenderLimit(_renderLimitMode)),
                MenuLine("WINDOW", _fullscreen ? "FULLSCREEN" : "WINDOW"),
                MenuLine("DEBUG OVERLAY", _debugOverlayVisible ? "ON" : "OFF"),
                MenuLine("IMAGE WIDTH", _settings.StretchToFullWidth ? "FULL WIDTH" : "ORIGINAL")
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
            /// Constant for max Visible Entries.
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
            /// Constant for max Visible Entries.
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
                MenuLine("MODEL", _system.Model.Name + (_machineModel == AmigaMachineModel.A500 ? "" : " (PREVIEW)")),
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
                MenuLine("GAMEPAD", FormatGamepadStatus()),
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
            int width = Math.Min(760, Math.Max(520, ClientSize.Width - 32));
            int height = Math.Min(ClientSize.Height - 32, 388);
            int x = (ClientSize.Width - width) / 2;
            int y = Math.Max(18, (ClientSize.Height - height) / 2);
            int listX = x + 20;
            int listY = y + 82;
            int thumbnailWidth = 184;
            int thumbnailHeight = 112;
            int thumbnailX = x + width - thumbnailWidth - 24;
            int thumbnailY = listY;
            int listWidth = Math.Max(190, thumbnailX - listX - 22);
            int rowHeight = 20;

            using (var brush = new SolidBrush(Color.FromArgb(224, 10, 13, 24)))
            using (var border = new Pen(Color.FromArgb(255, 255, 220, 130)))
            using (var dimBorder = new Pen(Color.FromArgb(255, 94, 126, 168)))
            using (var titleBrush = new SolidBrush(Color.FromArgb(255, 255, 230, 150)))
            using (var helpBrush = new SolidBrush(Color.FromArgb(255, 176, 198, 218)))
            using (var textBrush = new SolidBrush(Color.FromArgb(255, 235, 242, 250)))
            using (var valueBrush = new SolidBrush(Color.FromArgb(255, 182, 214, 108)))
            using (var selectedBrush = new SolidBrush(Color.FromArgb(255, 255, 243, 168)))
            using (var rowBrush = new SolidBrush(Color.FromArgb(95, 75, 96, 128)))
            using (var panelBrush = new SolidBrush(Color.FromArgb(210, 20, 24, 38)))
            {
                graphics.FillRectangle(brush, x, y, width, height);
                graphics.DrawRectangle(border, x, y, width, height);
                graphics.DrawLine(dimBorder, x + 14, y + 52, x + width - 14, y + 52);
                graphics.DrawString("SAVE STATES", _titleFont, titleBrush, x + 18, y + 14);
                graphics.DrawString("ENTER OPEN/LOAD  BACK UP  S/F5 SAVE  DEL DELETE  ESC CLOSE", _smallFont, helpBrush, x + 20, y + 54);
                graphics.DrawString("STATUS " + TrimText(_statusText, 58), _smallFont, valueBrush, x + 20, y + height - 30);

                graphics.FillRectangle(panelBrush, listX - 4, listY - 4, listWidth + 8, (SaveOverlayVisibleRows * rowHeight) + 8);
                if (_saveOverlayEntries.Count == 0)
                {
                    graphics.DrawString("NO SAVES HERE", _titleFont, selectedBrush, listX, listY + 24);
                    graphics.DrawString("PRESS S TO CREATE ONE", _smallFont, textBrush, listX, listY + 56);
                }
                else
                {
                    for (int row = 0; row < SaveOverlayVisibleRows; row++)
                    {
                        int index = _saveOverlayScroll + row;
                        if (index >= _saveOverlayEntries.Count)
                        {
                            break;
                        }

                        SaveOverlayEntry entry = _saveOverlayEntries[index];
                        bool selected = index == _saveSelection;
                        int rowY = listY + (row * rowHeight);
                        if (selected)
                        {
                            graphics.FillRectangle(rowBrush, listX - 2, rowY - 1, listWidth + 4, rowHeight);
                        }

                        graphics.DrawString(
                            (selected ? "> " : "  ") + TrimText(entry.DisplayName, Math.Max(10, listWidth / 8)),
                            _smallFont,
                            selected ? selectedBrush : textBrush,
                            listX,
                            rowY);
                    }
                }

                graphics.DrawRectangle(dimBorder, thumbnailX - 1, thumbnailY - 1, thumbnailWidth + 1, thumbnailHeight + 1);
                SaveOverlayEntry selectedEntry = _saveOverlayEntries.Count > 0 ? _saveOverlayEntries[_saveSelection] : null;
                if (selectedEntry != null && !selectedEntry.IsDirectory && selectedEntry.Metadata != null)
                {
                    SaveStateSlotMetadata metadata = selectedEntry.Metadata;
                    DrawSavePreview(graphics, metadata, thumbnailX, thumbnailY, thumbnailWidth, thumbnailHeight);
                    graphics.DrawString(metadata.CreatedLocalTime.ToString("yyyy-MM-dd"), _smallFont, textBrush, thumbnailX, thumbnailY + thumbnailHeight + 10);
                    graphics.DrawString(metadata.CreatedLocalTime.ToString("HH:mm:ss"), _smallFont, textBrush, thumbnailX, thumbnailY + thumbnailHeight + 28);
                    graphics.DrawString(TrimText(GetSaveStateDisplayMediaName(metadata), 22), _smallFont, valueBrush, thumbnailX, thumbnailY + thumbnailHeight + 46);
                }
                else
                {
                    graphics.FillRectangle(panelBrush, thumbnailX, thumbnailY, thumbnailWidth, thumbnailHeight);
                    graphics.DrawString(selectedEntry != null && selectedEntry.IsDirectory ? "FOLDER" : "NO IMAGE", _smallFont, dimBorder.Brush, thumbnailX + 54, thumbnailY + 45);
                }

                if (_saveDeleteConfirmVisible)
                {
                    DrawSaveDeleteConfirmOverlay(graphics, x + 120, y + 130, Math.Min(360, width - 240), 120);
                }
            }
        }

        /// <summary>
        /// Draws the savestate delete confirmation popup.
        /// </summary>
        private void DrawSaveDeleteConfirmOverlay(Graphics graphics, int x, int y, int width, int height)
        {
            using (var brush = new SolidBrush(Color.FromArgb(244, 20, 24, 38)))
            using (var border = new Pen(Color.FromArgb(255, 255, 243, 168)))
            using (var titleBrush = new SolidBrush(Color.FromArgb(255, 255, 243, 168)))
            using (var textBrush = new SolidBrush(Color.FromArgb(255, 232, 238, 244)))
            {
                graphics.FillRectangle(brush, x, y, width, height);
                graphics.DrawRectangle(border, x, y, width, height);
                graphics.DrawString("DELETE SAVE?", _titleFont, titleBrush, x + 16, y + 12);
                graphics.DrawString(TrimText(Path.GetFileName(_pendingDeleteSavePath), 34), _smallFont, textBrush, x + 18, y + 48);
                graphics.DrawString(_saveDeleteConfirmYesSelected ? "> YES     NO" : "  YES   > NO", _titleFont, titleBrush, x + 18, y + 70);
                graphics.DrawString("ENTER ACCEPT  ESC CANCEL", _smallFont, textBrush, x + 18, y + 100);
            }
        }

        /// <summary>
        /// Draws a savestate preview thumbnail.
        /// </summary>
        private static void DrawSavePreview(Graphics graphics, SaveStateSlotMetadata metadata, int x, int y, int width, int height)
        {
            if (metadata == null ||
                !metadata.HasScreenshot ||
                metadata.ScreenshotPixels.Length < metadata.ScreenshotWidth * metadata.ScreenshotHeight)
            {
                using (var brush = new SolidBrush(Color.FromArgb(255, 24, 24, 32)))
                {
                    graphics.FillRectangle(brush, x, y, width, height);
                }

                return;
            }

            GetPreviewCropRectangle(metadata.ScreenshotPixels, metadata.ScreenshotWidth, metadata.ScreenshotHeight, out int cropX, out int cropY, out int cropWidth, out int cropHeight);
            using (var bitmap = new Bitmap(metadata.ScreenshotWidth, metadata.ScreenshotHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, metadata.ScreenshotWidth, metadata.ScreenshotHeight),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int[] signedPixels = new int[metadata.ScreenshotWidth * metadata.ScreenshotHeight];
                    Buffer.BlockCopy(metadata.ScreenshotPixels, 0, signedPixels, 0, signedPixels.Length * sizeof(int));
                    System.Runtime.InteropServices.Marshal.Copy(signedPixels, 0, bitmapData.Scan0, signedPixels.Length);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                graphics.DrawImage(bitmap, new Rectangle(x, y, width, height), new Rectangle(cropX, cropY, cropWidth, cropHeight), GraphicsUnit.Pixel);
            }
        }

        /// <summary>
        /// Finds the non-border area of a savestate preview frame.
        /// </summary>
        private static void GetPreviewCropRectangle(uint[] sourcePixels, int sourceWidth, int sourceHeight, out int cropX, out int cropY, out int cropWidth, out int cropHeight)
        {
            uint borderColor = sourcePixels[0];
            if (!IsPreviewCropBorderColor(borderColor))
            {
                cropX = 0;
                cropY = 0;
                cropWidth = sourceWidth;
                cropHeight = sourceHeight;
                return;
            }

            int left = sourceWidth;
            int top = sourceHeight;
            int right = -1;
            int bottom = -1;

            for (int y = 0; y < sourceHeight; y++)
            {
                int row = y * sourceWidth;
                for (int x = 0; x < sourceWidth; x++)
                {
                    if (IsPreviewContentPixel(sourcePixels[row + x], borderColor))
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            if (right < left || bottom < top)
            {
                cropX = 0;
                cropY = 0;
                cropWidth = sourceWidth;
                cropHeight = sourceHeight;
                return;
            }

            int detectedWidth = right - left + 1;
            int detectedHeight = bottom - top + 1;
            int minimumUsefulArea = (sourceWidth * sourceHeight) / 4;
            if (detectedWidth * detectedHeight < minimumUsefulArea)
            {
                cropX = 0;
                cropY = 0;
                cropWidth = sourceWidth;
                cropHeight = sourceHeight;
                return;
            }

            cropX = left;
            cropY = top;
            cropWidth = detectedWidth;
            cropHeight = detectedHeight;
        }

        /// <summary>
        /// Returns true when the preview border color is a synthetic empty border rather than game artwork.
        /// </summary>
        private static bool IsPreviewCropBorderColor(uint argb)
        {
            int red = (int)((argb >> 16) & 0xFF);
            int green = (int)((argb >> 8) & 0xFF);
            int blue = (int)(argb & 0xFF);
            return red <= 8 && green <= 8 && blue <= 8;
        }

        /// <summary>
        /// Returns whether the pixel differs from the surrounding border color.
        /// </summary>
        private static bool IsPreviewContentPixel(uint argb, uint borderColor)
        {
            int redDifference = Math.Abs((int)((argb >> 16) & 0xFF) - (int)((borderColor >> 16) & 0xFF));
            int greenDifference = Math.Abs((int)((argb >> 8) & 0xFF) - (int)((borderColor >> 8) & 0xFF));
            int blueDifference = Math.Abs((int)(argb & 0xFF) - (int)(borderColor & 0xFF));
            return redDifference > 2 || greenDifference > 2 || blueDifference > 2;
        }

        /// <summary>
        /// Draws Menu into the presentation surface.
        /// </summary>
        private void DrawMenu(Graphics graphics, string title, string help, string[] lines, int selection)
        {
            int width = Math.Min(760, Math.Max(420, ClientSize.Width - 32));
            int height = 116 + (lines.Length * 24);
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
        /// Runs Adjust Volume.
        /// </summary>
        private void AdjustVolume(int direction)
        {
            _masterVolume = Math.Max(0.0f, Math.Min(1.5f, _masterVolume + (direction * 0.05f)));
        }

        /// <summary>
        /// Runs Toggle Audio.
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
        /// Runs Detach Audio From System.
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
        /// Releases keyboard, joystick, and mouse state derived from host input.
        /// </summary>
        private void ReleaseAllHostInput()
        {
            _hasLastMousePosition = false;
            ClearBufferedMouseMotion();
            if (_system != null)
            {
                _system.Input.ReleaseAll();
            }
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
        /// Runs Cycle Render Limit.
        /// </summary>
        private void CycleRenderLimit()
        {
            _renderLimitMode = CycleEnum(_renderLimitMode, 1);

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
                case RenderLimitMode.Native: return _system != null ? _system.Model.FrameRateHz : 50.0;
                case RenderLimitMode.Hz60: return 60.0;
                case RenderLimitMode.Hz100: return 100.0;
                case RenderLimitMode.Hz120:
                    return 120.0;
                case RenderLimitMode.Unlimited:
                    return _system != null ? _system.Model.FrameRateHz : 50.0;
                default:
                    return 50.0;
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
        /// Applies Monitor Aperture Mode to the current presentation.
        /// </summary>
        private void ApplyMonitorApertureMode(MonitorApertureMode mode)
        {
            _monitorApertureMode = mode;
            _settings.MonitorAperture = GetSettingsMonitorAperture(mode);
            AmigaEmulatorSettingsStore.Save(_settings);
            ApplyPresentationSourceCrop();
            _statusText = "MONITOR " + FormatMonitorAperture(mode);
        }

        /// <summary>
        /// Applies the current source crop to the host presentation.
        /// </summary>
        private void ApplyPresentationSourceCrop()
        {
            int left = 0;
            int right = 0;
            if (_monitorApertureMode == MonitorApertureMode.Crt)
            {
                left = CrtApertureCropPixelsLeft;
                right = CrtApertureCropPixelsRight;
            }

            SetPresentationSourceCrop(left, 0, right, _presentationCropBottom);
        }

        /// <summary>
        /// Reveals sparse bottom-overscan content such as disk prompts while still hiding solid border bands.
        /// </summary>
        private void UpdateAdaptivePresentationCrop()
        {
            int targetBottomCrop = CalculateAdaptivePresentationBottomCrop();
            if (targetBottomCrop == _presentationCropBottom)
            {
                return;
            }

            _presentationCropBottom = targetBottomCrop;
            ApplyPresentationSourceCrop();
        }

        /// <summary>
        /// Returns the bottom crop that best fits the current frame.
        /// </summary>
        private int CalculateAdaptivePresentationBottomCrop()
        {
            uint[] pixels = _system.PresentedFrameBuffer.Pixels;
            int width = _system.PresentedFrameBuffer.Width;
            int height = _system.PresentedFrameBuffer.Height;
            int cropRows = Math.Min(PresentationCropPixelsBottom, height);
            if (pixels == null || pixels.Length < width * height || width <= 0 || cropRows <= 0)
            {
                return PresentationCropPixelsBottom;
            }

            int startY = height - cropRows;
            int sampledPixels = width * cropRows;
            int visiblePixels = 0;
            for (int y = startY; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (IsPresentationVisiblePixel(pixels[rowOffset + x]))
                    {
                        visiblePixels++;
                    }
                }
            }

            double visiblePercent = sampledPixels > 0 ? (visiblePixels * 100.0) / sampledPixels : 0.0;
            if (visiblePercent >= BottomOverscanContentMinimumPercent &&
                visiblePercent <= BottomOverscanContentMaximumPercent)
            {
                _bottomOverscanContentHoldFrames = BottomOverscanContentHoldFrames;
                return 0;
            }

            if (visiblePercent > BottomOverscanContentMaximumPercent)
            {
                if (HasStructuredBottomOverscanContent(pixels, width, height, cropRows))
                {
                    _bottomOverscanContentHoldFrames = BottomOverscanContentHoldFrames;
                    return 0;
                }

                if (IsBrightNeutralBottomBackground(pixels, width, height, cropRows))
                {
                    _bottomOverscanContentHoldFrames = BottomOverscanContentHoldFrames;
                    return 0;
                }

                _bottomOverscanContentHoldFrames = 0;
                return PresentationCropPixelsBottom;
            }

            if (_bottomOverscanContentHoldFrames > 0)
            {
                _bottomOverscanContentHoldFrames--;
                return 0;
            }

            return PresentationCropPixelsBottom;
        }

        /// <summary>
        /// Returns true when a dense bottom band contains real artwork instead of a flat border color.
        /// </summary>
        private static bool HasStructuredBottomOverscanContent(uint[] pixels, int width, int height, int cropRows)
        {
            int startY = height - cropRows;
            int horizontalPairs = 0;
            int horizontalTransitions = 0;
            int verticalPairs = 0;
            int verticalTransitions = 0;

            for (int y = startY; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    uint color = pixels[rowOffset + x];
                    if (x > 0)
                    {
                        horizontalPairs++;
                        if (color != pixels[rowOffset + x - 1])
                        {
                            horizontalTransitions++;
                        }
                    }

                    if (y > startY)
                    {
                        verticalPairs++;
                        if (color != pixels[rowOffset + x - width])
                        {
                            verticalTransitions++;
                        }
                    }
                }
            }

            double horizontalPercent = horizontalPairs > 0 ? (horizontalTransitions * 100.0) / horizontalPairs : 0.0;
            double verticalPercent = verticalPairs > 0 ? (verticalTransitions * 100.0) / verticalPairs : 0.0;
            return horizontalPercent >= BottomOverscanHorizontalStructureMinimumPercent ||
                   verticalPercent >= BottomOverscanVerticalStructureMinimumPercent;
        }

        /// <summary>
        /// Keeps Kickstart-style light backgrounds from being mistaken for removable bottom overscan.
        /// </summary>
        private static bool IsBrightNeutralBottomBackground(uint[] pixels, int width, int height, int cropRows)
        {
            int startY = height - cropRows;
            long redSum = 0;
            long greenSum = 0;
            long blueSum = 0;
            int count = 0;
            for (int y = startY; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    uint color = pixels[rowOffset + x];
                    redSum += (color >> 16) & 0xFFu;
                    greenSum += (color >> 8) & 0xFFu;
                    blueSum += color & 0xFFu;
                    count++;
                }
            }

            if (count <= 0)
            {
                return false;
            }

            int red = (int)(redSum / count);
            int green = (int)(greenSum / count);
            int blue = (int)(blueSum / count);
            int brightest = Math.Max(red, Math.Max(green, blue));
            int darkest = Math.Min(red, Math.Min(green, blue));
            return red >= 200 && green >= 200 && blue >= 200 && brightest - darkest <= 40;
        }

        /// <summary>
        /// Returns true when a source pixel is bright enough to count as visible overscan content.
        /// </summary>
        private static bool IsPresentationVisiblePixel(uint color)
        {
            int red = (int)((color >> 16) & 0xFFu);
            int green = (int)((color >> 8) & 0xFFu);
            int blue = (int)(color & 0xFFu);
            return red > 24 || green > 24 || blue > 24;
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
        /// Applies Floppy Speed Mode to the current state.
        /// </summary>
        private void ApplyFloppySpeedMode(FloppySpeedMode mode)
        {
            _floppySpeedMode = mode;
            _settings.FloppySpeed = GetSettingsFloppySpeed(mode);
            ApplyFloppyTiming();
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "FLOPPY SPEED " + FormatFloppySpeed(mode);
        }

        /// <summary>
        /// Applies Reset Mode to persistent settings.
        /// </summary>
        private void ApplyResetMode(ResetMode mode)
        {
            _resetMode = mode;
            _settings.ResetMode = GetSettingsResetMode(mode);
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "RESET MODE " + FormatResetMode(mode);
        }

        /// <summary>
        /// Toggles DF0 write protection and remounts the current image when needed.
        /// </summary>
        private void ToggleDiskWriteProtection()
        {
            _floppyWriteProtected = !_floppyWriteProtected;
            MountedMediaInfo media = _mountedMedia;
            if (media != null && media.HasMedia && media.Kind == MountedMediaKind.Adf)
            {
                RemountMedia(MountedMediaInfo.ForFloppy(0, media.DisplayName, media.HostPath, _floppyWriteProtected));
            }

            _statusText = "DISK WRITE " + (_floppyWriteProtected ? "PROTECTED" : "ENABLED");
        }

        /// <summary>
        /// Applies the selected floppy timing to Paula.
        /// </summary>
        private void ApplyFloppyTiming()
        {
            if (_system == null)
            {
                return;
            }

            _system.Paula.DiskWordPeriodChipCycles = GetDiskWordPeriodChipCycles(_floppySpeedMode);
        }

        /// <summary>
        /// Runs Toggle Drive Overlay.
        /// </summary>
        private void ToggleDriveOverlay()
        {
            _driveOverlayVisible = !_driveOverlayVisible;
            _settings.DriveOverlayEnabled = _driveOverlayVisible;
            AmigaEmulatorSettingsStore.Save(_settings);
        }

        /// <summary>
        /// Toggles the persisted image aspect ratio setting.
        /// </summary>
        private void TogglePresentationStretch()
        {
            _settings.StretchToFullWidth = !_settings.StretchToFullWidth;
            SetPresentationStretchToClient(_settings.StretchToFullWidth);
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = _settings.StretchToFullWidth ? "IMAGE FULL WIDTH" : "IMAGE ORIGINAL";
        }

        /// <summary>Toggles fullscreen while preserving the selected image width.</summary>
        private void ToggleFullscreen()
        {
            if (_fullscreen)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                _fullscreen = false;
                SetPresentationStretchToClient(_settings.StretchToFullWidth);
                _settings.Fullscreen = false;
                AmigaEmulatorSettingsStore.Save(_settings);
                _statusText = "WINDOW MODE";
                return;
            }

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _fullscreen = true;
            SetPresentationStretchToClient(_settings.StretchToFullWidth);
            _settings.Fullscreen = true;
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = "FULLSCREEN";
        }

        /// <summary>
        /// Applies persisted fullscreen state without resaving settings during startup.
        /// </summary>
        private void ApplyStartupFullscreen()
        {
            if (!_fullscreen)
            {
                return;
            }

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            SetPresentationStretchToClient(_settings.StretchToFullWidth);
        }

        /// <summary>
        /// Runs Toggle Turbo Mode.
        /// </summary>
        private void ToggleTurboMode()
        {
            _turboMode = !_turboMode;
            _settings.TurboMode = _turboMode;
            AmigaEmulatorSettingsStore.Save(_settings);
            _statusText = _turboMode ? "TURBO ON" : "TURBO OFF";
        }

        /// <summary>
        /// Runs Reload Kickstart.
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

            if (_machineModel != AmigaMachineModel.A500)
            {
                if (_system.Model.Chipset == AmigaChipset.Aga || _machineModel == AmigaMachineModel.A3000)
                {
                    foreach (var rom in KickstartRomResolver.FindAvailableKickstartRoms())
                        if (rom.IsUsable && KickstartModelCompatibility.IsKnownCompatible(_machineModel, rom.Sha1))
                        { LoadSelectedKickstart(rom.Path); return; }
                    _romStatusText = _machineModel == AmigaMachineModel.A1200 ? "SELECT A1200 KICKSTART 3.x" : _machineModel == AmigaMachineModel.A3000 ? "SELECT A3000 KICKSTART 3.1" : "SELECT A4000 KICKSTART 3.x";
                    _statusText = _romStatusText;
                    return;
                }
                string[] candidates = _machineModel switch
                {
                    AmigaMachineModel.A600 => new[] { "KICK31.ROM", "KICK20.ROM" },
                    AmigaMachineModel.A2000Ocs => new[] { "KICK13.ROM", "KICK12.ROM" },
                    _ => new[] { "KICK20.ROM", "KICK31.ROM" }
                };
                string preferred = KickstartRomResolver.FindFirstExisting(candidates);
                if (preferred != null) { LoadSelectedKickstart(preferred); return; }
                _romStatusText = _machineModel == AmigaMachineModel.A2000Ocs
                    ? "SELECT A2000 KICKSTART 1.2/1.3" : "SELECT ECS KICKSTART 2.x/3.1";
                _statusText = _romStatusText;
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
                ApplyFloppyTiming();
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
                ApplyFloppyTiming();
                ResetEmulationClock();
                _romStatusText = FormatRomStatus(info);
                return;
            }

            _romStatusText = info != null && !string.IsNullOrWhiteSpace(info.Error)
                ? "MISSING - " + info.Error
                : "MISSING";
        }

        /// <summary>
        /// Runs Format Rom Status.
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
        /// Runs Format Rom List Value.
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
        /// Gets struct.
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
        /// Runs Menu Line.
        /// </summary>
        private static string MenuLine(string label, string value)
        {
            return label + "|" + (value ?? string.Empty);
        }

        /// <summary>
        /// Runs Format Percent.
        /// </summary>
        private static string FormatPercent(float value)
        {
            return ((int)Math.Round(value * 100.0f)).ToString() + "%";
        }

        /// <summary>
        /// Runs Format Render Limit.
        /// </summary>
        private static string FormatRenderLimit(RenderLimitMode mode)
        {
            switch (mode)
            {
                case RenderLimitMode.Native: return "NATIVE";
                case RenderLimitMode.Hz120: return "120 HZ";
                case RenderLimitMode.Hz100: return "100 HZ";
                case RenderLimitMode.Hz60: return "60 HZ";
                case RenderLimitMode.Unlimited: return "UNLIMITED";
                default: return "50 HZ";
            }
        }

        /// <summary>
        /// Runs Format Video Filter.
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
        /// Runs Format Video Upscale.
        /// </summary>
        private static string FormatVideoUpscale(VideoUpscaleMode mode)
        {
            switch (mode)
            {
                case VideoUpscaleMode.Scale2x: return "2X SHARP";
                case VideoUpscaleMode.Scale3x: return "3X SHARP";
                case VideoUpscaleMode.Hq2x: return "2X SMOOTH";
                case VideoUpscaleMode.Hq3x: return "3X SMOOTH";
                case VideoUpscaleMode.Hq4x: return "4X SMOOTH";
                case VideoUpscaleMode.Xbrz: return "4X SOFT";
                case VideoUpscaleMode.Clean4: return "4X SHARP";
                default: return "NONE";
            }
        }

        /// <summary>
        /// Formats the host monitor aperture mode.
        /// </summary>
        private static string FormatMonitorAperture(MonitorApertureMode mode)
        {
            switch (mode)
            {
                case MonitorApertureMode.Overscan: return "OVERSCAN";
                default: return "CRT";
            }
        }

        /// <summary>
        /// Runs Format Paula Filter.
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
        /// Runs To Sharp Pixels Filter.
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
        /// Runs To Sharp Pixels Upscale.
        /// </summary>
        private static SharpPixelsGpuUpscaleMode ToSharpPixelsUpscale(VideoUpscaleMode mode)
        {
            switch (mode)
            {
                case VideoUpscaleMode.Scale2x: return SharpPixelsGpuUpscaleMode.Scale2x;
                case VideoUpscaleMode.Scale3x: return SharpPixelsGpuUpscaleMode.Scale3x;
                case VideoUpscaleMode.Hq2x: return SharpPixelsGpuUpscaleMode.Hq2x;
                case VideoUpscaleMode.Hq3x: return SharpPixelsGpuUpscaleMode.Hq3x;
                case VideoUpscaleMode.Hq4x: return SharpPixelsGpuUpscaleMode.Hq4x;
                case VideoUpscaleMode.Xbrz: return SharpPixelsGpuUpscaleMode.Xbrz;
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
                case AmigaUpscaleMode.Hq4x: return VideoUpscaleMode.Hq4x;
                case AmigaUpscaleMode.Xbrz: return VideoUpscaleMode.Xbrz;
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
                case VideoUpscaleMode.Hq4x: return AmigaUpscaleMode.Hq4x;
                case VideoUpscaleMode.Xbrz: return AmigaUpscaleMode.Xbrz;
                case VideoUpscaleMode.Clean4: return AmigaUpscaleMode.Clean4;
                default: return AmigaUpscaleMode.None;
            }
        }

        /// <summary>
        /// Returns the monitor aperture mode from settings.
        /// </summary>
        private static MonitorApertureMode GetMonitorApertureFromSettings(AmigaMonitorAperture mode)
        {
            switch (mode)
            {
                case AmigaMonitorAperture.Overscan: return MonitorApertureMode.Overscan;
                default: return MonitorApertureMode.Crt;
            }
        }

        /// <summary>
        /// Returns the settings monitor aperture value.
        /// </summary>
        private static AmigaMonitorAperture GetSettingsMonitorAperture(MonitorApertureMode mode)
        {
            switch (mode)
            {
                case MonitorApertureMode.Overscan: return AmigaMonitorAperture.Overscan;
                default: return AmigaMonitorAperture.Crt;
            }
        }

        /// <summary>
        /// Returns the Render Limit From Settings.
        /// </summary>
        private static RenderLimitMode GetRenderLimitFromSettings(AmigaRenderFps mode)
        {
            switch (mode)
            {
                case AmigaRenderFps.Native: return RenderLimitMode.Native;
                case AmigaRenderFps.Hz50: return RenderLimitMode.Hz50;
                case AmigaRenderFps.Hz60: return RenderLimitMode.Hz60;
                case AmigaRenderFps.Hz100: return RenderLimitMode.Hz100;
                case AmigaRenderFps.Hz120: return RenderLimitMode.Hz120;
                case AmigaRenderFps.Unlimited: return RenderLimitMode.Unlimited;
                default: return RenderLimitMode.Native;
            }
        }

        /// <summary>
        /// Returns the Settings Render Fps.
        /// </summary>
        private static AmigaRenderFps GetSettingsRenderFps(RenderLimitMode mode)
        {
            switch (mode)
            {
                case RenderLimitMode.Native: return AmigaRenderFps.Native;
                case RenderLimitMode.Hz60: return AmigaRenderFps.Hz60;
                case RenderLimitMode.Hz100: return AmigaRenderFps.Hz100;
                case RenderLimitMode.Hz120: return AmigaRenderFps.Hz120;
                case RenderLimitMode.Unlimited: return AmigaRenderFps.Unlimited;
                default: return AmigaRenderFps.Hz50;
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
        /// Returns the Floppy Speed From Settings.
        /// </summary>
        private static FloppySpeedMode GetFloppySpeedFromSettings(AmigaFloppySpeed mode)
        {
            switch (mode)
            {
                case AmigaFloppySpeed.Fast:
                    return FloppySpeedMode.TurboLoading;
                case AmigaFloppySpeed.Turbo:
                case AmigaFloppySpeed.Instant:
                    return FloppySpeedMode.TurboAlways;
                default:
                    return FloppySpeedMode.Real;
            }
        }

        /// <summary>
        /// Returns the Settings Floppy Speed.
        /// </summary>
        private static AmigaFloppySpeed GetSettingsFloppySpeed(FloppySpeedMode mode)
        {
            switch (mode)
            {
                case FloppySpeedMode.TurboLoading:
                    return AmigaFloppySpeed.Fast;
                case FloppySpeedMode.TurboAlways:
                    return AmigaFloppySpeed.Turbo;
                default:
                    return AmigaFloppySpeed.Accurate;
            }
        }

        /// <summary>
        /// Returns the configured disk DMA word period.
        /// </summary>
        private static int GetDiskWordPeriodChipCycles(FloppySpeedMode mode)
        {
            switch (mode)
            {
                case FloppySpeedMode.TurboLoading:
                    return 27;
                case FloppySpeedMode.TurboAlways:
                    return 8;
                default:
                    return 108;
            }
        }

        /// <summary>
        /// Returns the Reset Mode From Settings.
        /// </summary>
        private static ResetMode GetResetModeFromSettings(AmigaResetMode mode)
        {
            switch (mode)
            {
                case AmigaResetMode.Reload:
                    return ResetMode.Remount;
                case AmigaResetMode.Power:
                    return ResetMode.Power;
                default:
                    return ResetMode.Warm;
            }
        }

        /// <summary>
        /// Returns the Settings Reset Mode.
        /// </summary>
        private static AmigaResetMode GetSettingsResetMode(ResetMode mode)
        {
            switch (mode)
            {
                case ResetMode.Remount:
                    return AmigaResetMode.Reload;
                case ResetMode.Power:
                    return AmigaResetMode.Power;
                default:
                    return AmigaResetMode.Warm;
            }
        }

        /// <summary>
        /// Runs Format Ram Profile.
        /// </summary>
        private static string FormatRamProfile(RamProfile profile)
        {
            switch (profile)
            {
                case RamProfile.Chip512: return "512K CHIP";
                case RamProfile.Chip1Mb: return "1MB CHIP";
                case RamProfile.Chip2Mb: return "2MB CHIP";
                case RamProfile.Chip2MbFast8Mb: return "2MB CHIP + 8MB FAST";
                case RamProfile.Chip1MbFast2Mb: return "1MB CHIP + 2MB FAST";
                case RamProfile.Chip1MbFast4Mb: return "1MB CHIP + 4MB FAST";
                case RamProfile.Chip2MbFast2Mb: return "2MB CHIP + 2MB FAST";
                case RamProfile.Chip2MbFast4Mb: return "2MB CHIP + 4MB FAST";
                case RamProfile.Chip512KSlow512KFast2Mb: return "512K CHIP + 512K SLOW + 2MB FAST";
                case RamProfile.Chip512KSlow512KFast4Mb: return "512K CHIP + 512K SLOW + 4MB FAST";
                case RamProfile.Chip512KSlow512KFast8Mb: return "512K CHIP + 512K SLOW + 8MB FAST";
                default: return "512K CHIP + 512K SLOW";
            }
        }

        /// <summary>
        /// Runs Format Floppy Speed.
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
        /// Runs Format Input Ports.
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

            _system.Input.SetGamepadButtons(AmigaControlPort.Port0, AmigaJoystickButtons.None);
            _system.Input.SetGamepadButtons(AmigaControlPort.Port1, AmigaJoystickButtons.None);
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
        /// Runs Format Reset Mode.
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
        /// Runs Format Accuracy Profile.
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
        /// Runs Format Overlay Path.
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
        /// Formats a savestate row for the overlay list.
        /// </summary>
        private static string FormatSaveListEntry(SaveStateSlotMetadata entry, int maxLength)
        {
            string mediaName = GetSaveStateDisplayMediaName(entry);
            string text = entry.CreatedLocalTime.ToString("MM-dd HH:mm") + " " + mediaName;
            return TrimText(text, maxLength);
        }

        /// <summary>
        /// Gets the best available media name for a savestate row.
        /// </summary>
        private static string GetSaveStateDisplayMediaName(SaveStateSlotMetadata entry)
        {
            if (entry == null)
            {
                return "UNKNOWN";
            }

            MountedMediaInfo media = entry.MountedMedia;
            if (media != null && !string.IsNullOrWhiteSpace(media.HostPath))
            {
                return Path.GetFileNameWithoutExtension(media.HostPath).ToUpperInvariant();
            }

            if (media != null && !string.IsNullOrWhiteSpace(media.DisplayName))
            {
                return media.DisplayName.ToUpperInvariant();
            }

            try
            {
                string directoryName = Path.GetFileName(Path.GetDirectoryName(entry.Path));
                if (!string.IsNullOrWhiteSpace(directoryName) &&
                    !string.Equals(directoryName, "saves", StringComparison.OrdinalIgnoreCase))
                {
                    return directoryName.ToUpperInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            return Path.GetFileNameWithoutExtension(entry.Path).ToUpperInvariant();
        }

        /// <summary>
        /// Runs Trim Text.
        /// </summary>
        private static string TrimText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return "..." + text.Substring(text.Length - Math.Max(0, maxLength - 3));
        }

        /// <summary>
        /// Creates a transparent cursor used while the Amiga mouse sprite owns the display.
        /// </summary>
        private static Cursor CreateHiddenHostCursor()
        {
            byte[] andMask = new byte[128];
            byte[] xorMask = new byte[128];
            for (int index = 0; index < andMask.Length; index++)
            {
                andMask[index] = 0xFF;
            }

            IntPtr handle = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andMask, xorMask);
            return handle != IntPtr.Zero ? new Cursor(handle) : Cursors.Default;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateCursor(
            IntPtr hInst,
            int xHotSpot,
            int yHotSpot,
            int nWidth,
            int nHeight,
            byte[] pvANDPlane,
            byte[] pvXORPlane);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClipCursor(ref NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClipCursor(IntPtr lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Represents a savestate browser entry component.
        /// </summary>
        private sealed class SaveOverlayEntry
        {
            /// <summary>
            /// Initializes a new instance of the SaveOverlayEntry class.
            /// </summary>
            public SaveOverlayEntry(string path, string displayName, bool isDirectory, bool isParent, SaveStateSlotMetadata metadata)
            {
                Path = path;
                DisplayName = displayName ?? string.Empty;
                IsDirectory = isDirectory;
                IsParent = isParent;
                Metadata = metadata;
            }

            /// <summary>
            /// Gets host filesystem path.
            /// </summary>
            public string Path { get; private set; }

            /// <summary>
            /// Gets user-facing display name.
            /// </summary>
            public string DisplayName { get; private set; }

            /// <summary>
            /// Gets whether this entry represents a directory.
            /// </summary>
            public bool IsDirectory { get; private set; }

            /// <summary>
            /// Gets whether this entry navigates to the parent directory.
            /// </summary>
            public bool IsParent { get; private set; }

            /// <summary>
            /// Gets savestate metadata when this entry represents a save.
            /// </summary>
            public SaveStateSlotMetadata Metadata { get; private set; }
        }
    }
}
