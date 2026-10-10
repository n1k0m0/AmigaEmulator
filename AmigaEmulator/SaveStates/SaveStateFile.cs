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
using System.IO;
using System.Text;
using AmigaEmulator.Core.Media;

namespace AmigaEmulator.Core.SaveStates
{
    /// <summary>
    /// Reads and writes complete, model-aware Amiga savestate files.
    /// </summary>
    public static class SaveStateFile
    {
        /// <summary>
        /// Retains the original magic so legacy A500 states remain readable.
        /// </summary>
        public const string Magic = "AMIGA500-STATE";

        private const int Version = 2;
        private const string Extension = ".amigasav";
        private const string UnknownMediaDirectory = "Unknown";

        public static string GetSlotPath(string saveDirectory, int slotIndex)
        {
            Directory.CreateDirectory(saveDirectory);
            return Path.Combine(saveDirectory, "slot-" + (slotIndex + 1).ToString("00") + Extension);
        }

        /// <summary>
        /// Creates a unique timestamped savestate path inside a directory.
        /// </summary>
        public static string CreateSavePath(string saveDirectory)
        {
            Directory.CreateDirectory(saveDirectory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string path = Path.Combine(saveDirectory, "save-" + timestamp + Extension);
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(saveDirectory, "save-" + timestamp + "-" + suffix + Extension);
                suffix++;
            }

            return path;
        }

        /// <summary>
        /// Gets the savestate directory for a mounted media item.
        /// </summary>
        public static string GetSaveDirectoryForMedia(string baseSaveDirectory, MountedMediaInfo mediaInfo)
        {
            return Path.Combine(baseSaveDirectory, GetMediaDirectoryName(mediaInfo));
        }

        /// <summary>
        /// Writes a complete savestate with menu metadata and screenshot pixels.
        /// </summary>
        public static void Write(
            string path,
            AmigaSystem system,
            MountedMediaInfo mountedMedia,
            AmigaKickstart kickstart,
            AmigaRamProfile ramProfile,
            AmigaAccuracyProfile accuracyProfile,
            uint[] screenshotPixels,
            int screenshotWidth,
            int screenshotHeight)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            var declaredModel = AmigaModel.Create(system.Model.Machine, AmigaModel.GetDefaultRamProfile(system.Model.Machine));
            if (system.Model.Cpu != declaredModel.Cpu || system.Model.CpuClockHz != declaredModel.CpuClockHz ||
                system.Model.Chipset != declaredModel.Chipset || system.Model.IdeModel != declaredModel.IdeModel ||
                system.Model.HasDesktopResources != declaredModel.HasDesktopResources)
                throw new InvalidOperationException("Development processor/chipset states require the diagnostic core-state API.");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(DateTime.UtcNow.Ticks);
                WriteMountedMediaInfo(writer, mountedMedia);
                writer.Write((int)kickstart);
                writer.Write((int)ramProfile);
                writer.Write((int)accuracyProfile);
                writer.Write((int)system.Model.Machine);
                writer.Write(screenshotWidth);
                writer.Write(screenshotHeight);
                StateSerializer.WriteUIntArray(writer, screenshotPixels);
                system.SaveState(writer);
            }
        }

        /// <summary>
        /// Reads only the metadata and screenshot needed by the overlay.
        /// </summary>
        public static SaveStateSlotMetadata ReadMetadata(string path, int slotIndex)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                int fileVersion = ValidateHeader(reader);
                long ticksUtc = reader.ReadInt64();
                MountedMediaInfo mediaInfo = ReadMountedMediaInfo(reader);
                var kickstart = (AmigaKickstart)reader.ReadInt32();
                var ramProfile = (AmigaRamProfile)reader.ReadInt32();
                var accuracyProfile = (AmigaAccuracyProfile)reader.ReadInt32();
                var machineModel = fileVersion >= 2 ? (AmigaMachineModel)reader.ReadInt32() : AmigaMachineModel.A500;
                int screenshotWidth = reader.ReadInt32();
                int screenshotHeight = reader.ReadInt32();
                uint[] pixels = StateSerializer.ReadUIntArray(reader);

                bool compatible =
                    Enum.IsDefined(typeof(AmigaKickstart), kickstart) &&
                    Enum.IsDefined(typeof(AmigaRamProfile), ramProfile) &&
                    Enum.IsDefined(typeof(AmigaAccuracyProfile), accuracyProfile) &&
                    Enum.IsDefined(machineModel) && AmigaModel.NormalizeRamProfile(machineModel, ramProfile) == ramProfile;

                return new SaveStateSlotMetadata
                {
                    SlotIndex = slotIndex,
                    Status = compatible ? SaveStateSlotStatus.Occupied : SaveStateSlotStatus.Incompatible,
                    Path = path,
                    DisplayName = compatible ? BuildDisplayName(ticksUtc, mediaInfo) : "INCOMPATIBLE",
                    CreatedLocalTime = new DateTime(ticksUtc, DateTimeKind.Utc).ToLocalTime(),
                    MountedMedia = mediaInfo,
                    Kickstart = kickstart,
                    RamProfile = ramProfile,
                    MachineModel = machineModel,
                    AccuracyProfile = accuracyProfile,
                    ScreenshotWidth = screenshotWidth,
                    ScreenshotHeight = screenshotHeight,
                    ScreenshotPixels = pixels ?? Array.Empty<uint>()
                };
            }
        }

        /// <summary>
        /// Reads only metadata for browser-style savestate lists.
        /// </summary>
        public static SaveStateSlotMetadata ReadMetadata(string path)
        {
            return ReadMetadata(path, -1);
        }

        /// <summary>
        /// Loads a complete savestate into an existing Amiga system.
        /// </summary>
        public static void Load(string path, AmigaSystem system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                int fileVersion = ValidateHeader(reader);
                reader.ReadInt64();
                MountedMediaInfo mediaInfo = ReadMountedMediaInfo(reader);
                var kickstart = (AmigaKickstart)reader.ReadInt32();
                var ram = (AmigaRamProfile)reader.ReadInt32();
                var accuracy = (AmigaAccuracyProfile)reader.ReadInt32();
                var machine = fileVersion >= 2 ? (AmigaMachineModel)reader.ReadInt32() : AmigaMachineModel.A500;
                if (machine != system.Model.Machine) throw new InvalidDataException("Savestate machine model does not match the current machine.");
                if (!Enum.IsDefined(kickstart) || !Enum.IsDefined(ram) || !Enum.IsDefined(accuracy) || !Enum.IsDefined(machine))
                    throw new InvalidDataException("Savestate contains unsupported machine settings.");
                AmigaModel required = AmigaModel.Create(machine, ram);
                if (required.ChipRamBytes != system.Model.ChipRamBytes || required.SlowRamBytes != system.Model.SlowRamBytes || required.FastRamBytes != system.Model.FastRamBytes)
                    throw new InvalidDataException("Savestate RAM configuration does not match the current machine.");
                reader.ReadInt32();
                reader.ReadInt32();
                StateSerializer.ReadUIntArray(reader);
                system.LoadState(reader);
                RestoreMountedMedia(system, mediaInfo);
            }
        }

        /// <summary>
        /// Reattaches host-side media referenced by savestate metadata after the machine state is restored.
        /// </summary>
        private static void RestoreMountedMedia(AmigaSystem system, MountedMediaInfo mediaInfo)
        {
            if (mediaInfo == null ||
                mediaInfo.Kind != MountedMediaKind.Adf ||
                mediaInfo.Status != MountedMediaStatus.Mounted ||
                mediaInfo.DriveIndex < 0 ||
                mediaInfo.DriveIndex > 3 ||
                string.IsNullOrWhiteSpace(mediaInfo.HostPath) ||
                !File.Exists(mediaInfo.HostPath))
            {
                return;
            }

            system.Bus.FloppyController.RestoreDriveMedia(
                mediaInfo.DriveIndex,
                AdfImage.Load(mediaInfo.HostPath),
                mediaInfo.IsWriteProtected);
            system.Bus.RefreshFloppyControlLines();
        }

        /// <summary>
        /// Gets the savestate search pattern.
        /// </summary>
        public static string SearchPattern
        {
            get { return "*" + Extension; }
        }

        /// <summary>
        /// Gets a filesystem-safe directory name for a mounted media item.
        /// </summary>
        public static string GetMediaDirectoryName(MountedMediaInfo mediaInfo)
        {
            string name = null;
            if (mediaInfo != null && mediaInfo.HasMedia)
            {
                if (!string.IsNullOrWhiteSpace(mediaInfo.HostPath))
                {
                    name = Path.GetFileNameWithoutExtension(mediaInfo.HostPath);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = mediaInfo.DisplayName;
                }
            }

            return SanitizeDirectoryName(name);
        }

        /// <summary>
        /// Validates the file header.
        /// </summary>
        private static int ValidateHeader(BinaryReader reader)
        {
            string magic = reader.ReadString();
            int version = reader.ReadInt32();
            if (!string.Equals(magic, Magic, StringComparison.Ordinal) || version < 1 || version > Version)
            {
                throw new InvalidDataException("Unsupported Amiga savestate format.");
            }
            return version;
        }

        /// <summary>
        /// Writes mounted media metadata.
        /// </summary>
        private static void WriteMountedMediaInfo(BinaryWriter writer, MountedMediaInfo mediaInfo)
        {
            mediaInfo = mediaInfo ?? MountedMediaInfo.None;
            writer.Write((int)mediaInfo.Kind);
            writer.Write((int)mediaInfo.Status);
            StateSerializer.WriteString(writer, mediaInfo.ShortLabel);
            StateSerializer.WriteString(writer, mediaInfo.DisplayName);
            StateSerializer.WriteString(writer, mediaInfo.HostPath);
            writer.Write(mediaInfo.DriveIndex);
            writer.Write(mediaInfo.IsWriteProtected);
            StateSerializer.WriteString(writer, mediaInfo.Error);
        }

        /// <summary>
        /// Reads mounted media metadata.
        /// </summary>
        private static MountedMediaInfo ReadMountedMediaInfo(BinaryReader reader)
        {
            var kind = (MountedMediaKind)reader.ReadInt32();
            var status = (MountedMediaStatus)reader.ReadInt32();
            string shortLabel = StateSerializer.ReadString(reader) ?? "NONE";
            string displayName = StateSerializer.ReadString(reader) ?? string.Empty;
            string hostPath = StateSerializer.ReadString(reader) ?? string.Empty;
            int driveIndex = reader.ReadInt32();
            bool writeProtected = reader.ReadBoolean();
            string error = StateSerializer.ReadString(reader) ?? string.Empty;
            return new MountedMediaInfo(kind, status, shortLabel, displayName, hostPath, driveIndex, writeProtected, error);
        }

        private static string BuildDisplayName(long ticksUtc, MountedMediaInfo mediaInfo)
        {
            DateTime local = new DateTime(ticksUtc, DateTimeKind.Utc).ToLocalTime();
            string media = mediaInfo != null && mediaInfo.HasMedia
                ? mediaInfo.DisplayName
                : "NO MEDIA";
            return local.ToString("yyyy-MM-dd HH:mm:ss") + " " + media;
        }

        /// <summary>
        /// Sanitizes a mounted media display name for use as a directory name.
        /// </summary>
        private static string SanitizeDirectoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return UnknownMediaDirectory;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int index = 0; index < name.Length; index++)
            {
                char value = name[index];
                bool invalid = false;
                for (int invalidIndex = 0; invalidIndex < invalidChars.Length; invalidIndex++)
                {
                    if (value == invalidChars[invalidIndex])
                    {
                        invalid = true;
                        break;
                    }
                }

                builder.Append(invalid || char.IsControl(value) ? '_' : value);
            }

            string sanitized = builder.ToString().Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return UnknownMediaDirectory;
            }

            const int maximumLength = 80;
            if (sanitized.Length > maximumLength)
            {
                sanitized = sanitized.Substring(0, maximumLength).Trim().Trim('.');
            }

            return string.IsNullOrWhiteSpace(sanitized) ? UnknownMediaDirectory : sanitized;
        }
    }
}
