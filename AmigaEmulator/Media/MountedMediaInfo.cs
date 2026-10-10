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
namespace AmigaEmulator.Core.Media
{
    /// <summary>
    /// Lists mounted media kinds shown by Amiga overlays.
    /// </summary>
    public enum MountedMediaKind
    {
        /// <summary>
        /// Represents None.
        /// </summary>
        None,
        /// <summary>
        /// Represents ADF.
        /// </summary>
        Adf,
        /// <summary>
        /// Represents Hdf.
        /// </summary>
        Hdf,
        /// <summary>
        /// Represents Directory.
        /// </summary>
        Directory,
        /// <summary>
        /// Represents Kickstart ROM.
        /// </summary>
        KickstartRom
    }

    /// <summary>
    /// Lists the host-side media status shown by Amiga overlays.
    /// </summary>
    public enum MountedMediaStatus
    {
        /// <summary>
        /// Represents Empty.
        /// </summary>
        Empty,
        /// <summary>
        /// Represents Mounted.
        /// </summary>
        Mounted,
        /// <summary>
        /// Represents Loading.
        /// </summary>
        Loading,
        /// <summary>
        /// Represents Ejected.
        /// </summary>
        Ejected,
        /// <summary>
        /// Represents Missing.
        /// </summary>
        Missing,
        /// <summary>
        /// Represents Error.
        /// </summary>
        Error
    }

    /// <summary>
    /// Represents one mounted media entry and its overlay status.
    /// </summary>
    public sealed class MountedMediaInfo
    {
        /// <summary>
        /// Initializes a new instance of the MountedMediaInfo class.
        /// </summary>
        public static readonly MountedMediaInfo None = new MountedMediaInfo(
            MountedMediaKind.None,
            MountedMediaStatus.Empty,
            "NONE",
            string.Empty,
            string.Empty,
            -1,
            false,
            string.Empty);

        /// <summary>
        /// Initializes a new instance of the MountedMediaInfo class.
        /// </summary>
        public MountedMediaInfo()
            : this(MountedMediaKind.None, MountedMediaStatus.Empty, "NONE", string.Empty, string.Empty, -1, false, string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MountedMediaInfo class.
        /// </summary>
        public MountedMediaInfo(
            MountedMediaKind kind,
            MountedMediaStatus status,
            string shortLabel,
            string displayName,
            string hostPath,
            int driveIndex,
            bool isWriteProtected,
            string error)
        {
            Kind = kind;
            Status = status;
            ShortLabel = shortLabel ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            HostPath = hostPath ?? string.Empty;
            DriveIndex = driveIndex;
            IsWriteProtected = isWriteProtected;
            Error = error ?? string.Empty;
        }

        public MountedMediaKind Kind { get; set; }

        public MountedMediaStatus Status { get; set; }

        public string ShortLabel { get; set; }

        public string DisplayName { get; set; }

        public string HostPath { get; set; }

        public int DriveIndex { get; set; }

        public bool IsWriteProtected { get; set; }

        public string Error { get; set; }

        /// <summary>
        /// Returns whether Media is present.
        /// </summary>
        public bool HasMedia
        {
            get { return Kind != MountedMediaKind.None; }
        }

        /// <summary>
        /// Returns whether Available is true.
        /// </summary>
        public bool IsAvailable
        {
            get { return HasMedia && Status == MountedMediaStatus.Mounted; }
        }

        /// <summary>
        /// Runs For Floppy.
        /// </summary>
        public static MountedMediaInfo ForFloppy(
            int driveIndex,
            string displayName,
            string hostPath,
            bool isWriteProtected)
        {
            return new MountedMediaInfo(
                MountedMediaKind.Adf,
                MountedMediaStatus.Mounted,
                "DF" + driveIndex,
                displayName,
                hostPath,
                driveIndex,
                isWriteProtected,
                string.Empty);
        }

        /// <summary>
        /// Runs With Status.
        /// </summary>
        public MountedMediaInfo WithStatus(MountedMediaStatus status, string error)
        {
            return new MountedMediaInfo(Kind, status, ShortLabel, DisplayName, HostPath, DriveIndex, IsWriteProtected, error);
        }
    }
}
