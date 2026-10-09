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
using AmigaEmulator.Core.Media;

namespace AmigaEmulator.Core.SaveStates
{
    /// <summary>
    /// Lists the state of a savestate slot in the overlay.
    /// </summary>
    public enum SaveStateSlotStatus
    {
        /// <summary>
        /// Represents Empty.
        /// </summary>
        Empty,
        /// <summary>
        /// Represents Occupied.
        /// </summary>
        Occupied,
        /// <summary>
        /// Represents Corrupt.
        /// </summary>
        Corrupt,
        /// <summary>
        /// Represents Incompatible.
        /// </summary>
        Incompatible
    }

    /// <summary>
    /// Contains the lightweight information shown by the Amiga savestate menu.
    /// </summary>
    public sealed class SaveStateSlotMetadata
    {
        /// <summary>
        /// Initializes a new instance of the SaveStateSlotMetadata class.
        /// </summary>
        public SaveStateSlotMetadata()
        {
            Path = string.Empty;
            DisplayName = string.Empty;
            MountedMedia = MountedMediaInfo.None;
            ScreenshotPixels = new uint[0];
        }

        /// <summary>
        /// Gets or sets Slot Index.
        /// </summary>
        public int SlotIndex { get; set; }

        /// <summary>
        /// Gets or sets Status.
        /// </summary>
        public SaveStateSlotStatus Status { get; set; }

        /// <summary>
        /// Gets or sets Path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets Display Name.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets Created Local Time.
        /// </summary>
        public DateTime CreatedLocalTime { get; set; }

        /// <summary>
        /// Gets or sets Mounted Media.
        /// </summary>
        public MountedMediaInfo MountedMedia { get; set; }

        /// <summary>
        /// Gets or sets Kickstart.
        /// </summary>
        public AmigaKickstart Kickstart { get; set; }

        /// <summary>
        /// Gets or sets RAM Profile.
        /// </summary>
        public AmigaRamProfile RamProfile { get; set; }

        public AmigaMachineModel MachineModel { get; set; } = AmigaMachineModel.A500;

        /// <summary>
        /// Gets or sets Accuracy Profile.
        /// </summary>
        public AmigaAccuracyProfile AccuracyProfile { get; set; }

        /// <summary>
        /// Gets or sets Screenshot Width.
        /// </summary>
        public int ScreenshotWidth { get; set; }

        /// <summary>
        /// Gets or sets Screenshot Height.
        /// </summary>
        public int ScreenshotHeight { get; set; }

        /// <summary>
        /// Gets or sets Screenshot Pixels.
        /// </summary>
        public uint[] ScreenshotPixels { get; set; }

        /// <summary>
        /// Returns whether Screenshot is present.
        /// </summary>
        public bool HasScreenshot
        {
            get { return ScreenshotWidth > 0 && ScreenshotHeight > 0 && ScreenshotPixels != null && ScreenshotPixels.Length > 0; }
        }

        /// <summary>
        /// Returns whether State is present.
        /// </summary>
        public bool HasState
        {
            get { return Status == SaveStateSlotStatus.Occupied; }
        }

        /// <summary>
        /// Runs Empty.
        /// </summary>
        public static SaveStateSlotMetadata Empty(int slotIndex, string path)
        {
            return new SaveStateSlotMetadata
            {
                SlotIndex = slotIndex,
                Status = SaveStateSlotStatus.Empty,
                Path = path ?? string.Empty,
                DisplayName = "EMPTY"
            };
        }
    }
}
