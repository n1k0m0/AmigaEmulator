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
    /// Maps CIA-B floppy control lines to drive state and exposes CIA-A disk status bits.
    /// </summary>
    public sealed class FloppyController
    {
        /// <summary>
        /// Constant for Number Of Drives.
        /// </summary>
        private const int DriveCount = 4;
        /// <summary>
        /// Constant for CIA A Status Mask.
        /// </summary>
        public const byte CiaAStatusMask = 0x3C;
        /// <summary>
        /// Constant for CIA A Disk Change Bit.
        /// </summary>
        public const byte CiaADiskChangeBit = 0x04;
        /// <summary>
        /// Constant for CIA A Write Protect Bit.
        /// </summary>
        public const byte CiaAWriteProtectBit = 0x08;
        /// <summary>
        /// Constant for CIA A Track 0 Bit.
        /// </summary>
        public const byte CiaATrack0Bit = 0x10;
        /// <summary>
        /// Constant for CIA A Ready Bit.
        /// </summary>
        public const byte CiaAReadyBit = 0x20;

        /// <summary>
        /// Constant for CIA B Step Bit.
        /// </summary>
        private const byte CiaBStepBit = 0x01;
        /// <summary>
        /// Constant for CIA B Direction Bit.
        /// </summary>
        private const byte CiaBDirectionBit = 0x02;
        /// <summary>
        /// Constant for CIA B Side Bit.
        /// </summary>
        private const byte CiaBSideBit = 0x04;
        /// <summary>
        /// Constant for CIA B First Select Bit.
        /// </summary>
        private const byte CiaBFirstSelectBit = 0x08;
        /// <summary>
        /// Constant for CIA B Motor Bit.
        /// </summary>
        private const byte CiaBMotorBit = 0x80;
        /// <summary>
        /// Initializes a new instance of the FloppyController class.
        /// </summary>
        public FloppyController()
        {
            Drive0 = new FloppyDrive();
            Drive1 = new FloppyDrive();
            Drive2 = new FloppyDrive();
            Drive3 = new FloppyDrive();
        }

        /// <summary>
        /// Raised when mounted floppy media changes.
        /// </summary>
        public event System.Action MediaChanged;

        /// <summary>
        /// Gets or sets Drive 0.
        /// </summary>
        public FloppyDrive Drive0 { get; private set; }

        /// <summary>
        /// Gets or sets Drive 1.
        /// </summary>
        public FloppyDrive Drive1 { get; private set; }

        /// <summary>
        /// Gets or sets Drive 2.
        /// </summary>
        public FloppyDrive Drive2 { get; private set; }

        /// <summary>
        /// Gets or sets Drive 3.
        /// </summary>
        public FloppyDrive Drive3 { get; private set; }

        /// <summary>
        /// Gets the first currently selected drive, matching the shared Amiga floppy bus.
        /// </summary>
        public FloppyDrive SelectedDrive
        {
            get
            {
                FloppyDrive[] drives = GetDrives();
                for (int index = 0; index < drives.Length; index++)
                {
                    if (drives[index].Selected)
                    {
                        return drives[index];
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets Index Pulse Pending.
        /// </summary>
        public bool IndexPulsePending
        {
            get
            {
                FloppyDrive[] drives = GetDrives();
                for (int index = 0; index < drives.Length; index++)
                {
                    if (drives[index].Selected && drives[index].IndexPulsePending)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns a drive by Amiga drive index.
        /// </summary>
        public FloppyDrive GetDrive(int driveIndex)
        {
            if (driveIndex < 0 || driveIndex >= DriveCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(driveIndex));
            }

            return GetDrives()[driveIndex];
        }

        /// <summary>
        /// Runs Insert Drive 0.
        /// </summary>
        public void InsertDrive0(AdfImage image)
        {
            InsertDrive(0, image);
        }

        /// <summary>
        /// Runs Insert Drive 0.
        /// </summary>
        public void InsertDrive0(AdfImage image, bool writeProtected)
        {
            InsertDrive(0, image, writeProtected);
        }

        /// <summary>
        /// Inserts media into the requested drive.
        /// </summary>
        public void InsertDrive(int driveIndex, AdfImage image)
        {
            InsertDrive(driveIndex, image, true);
        }

        /// <summary>
        /// Inserts media into the requested drive.
        /// </summary>
        public void InsertDrive(int driveIndex, AdfImage image, bool writeProtected)
        {
            GetDrive(driveIndex).Insert(image, writeProtected);
            RaiseMediaChanged();
        }

        /// <summary>
        /// Restores DF0 media after loading a savestate without asserting a new disk-change event.
        /// </summary>
        public void RestoreDrive0Media(AdfImage image, bool writeProtected)
        {
            RestoreDriveMedia(0, image, writeProtected);
        }

        /// <summary>
        /// Restores media after loading a savestate without asserting a new disk-change event.
        /// </summary>
        public void RestoreDriveMedia(int driveIndex, AdfImage image, bool writeProtected)
        {
            GetDrive(driveIndex).RestoreMedia(image, writeProtected);
        }

        /// <summary>
        /// Runs Eject Drive 0.
        /// </summary>
        public void EjectDrive0()
        {
            EjectDrive(0);
        }

        /// <summary>
        /// Ejects media from the requested drive.
        /// </summary>
        public void EjectDrive(int driveIndex)
        {
            GetDrive(driveIndex).Eject();
            RaiseMediaChanged();
        }

        /// <summary>
        /// Flushes dirty writable floppy media.
        /// </summary>
        public void FlushDrive0()
        {
            FlushDrive(0);
        }

        /// <summary>
        /// Flushes dirty writable floppy media in the requested drive.
        /// </summary>
        public void FlushDrive(int driveIndex)
        {
            GetDrive(driveIndex).FlushImage();
        }

        /// <summary>
        /// Persists pending writes on every mounted floppy drive.
        /// </summary>
        public void FlushAllDrives()
        {
            foreach (FloppyDrive drive in GetDrives()) drive.FlushImage();
        }

        /// <summary>
        /// Applies CIA B Port to the current state.
        /// </summary>
        public void ApplyCiaBPort(byte prb, byte ddrb)
        {
            bool motorLineLow = IsActiveLowOutput(prb, ddrb, CiaBMotorBit);
            bool stepLineHigh = !IsActiveLowOutput(prb, ddrb, CiaBStepBit);
            bool directionIn = !IsHighOutput(prb, ddrb, CiaBDirectionBit);
            bool sideOne = IsActiveLowOutput(prb, ddrb, CiaBSideBit);

            FloppyDrive[] drives = GetDrives();
            for (int index = 0; index < drives.Length; index++)
            {
                bool selected = IsActiveLowOutput(prb, ddrb, (byte)(CiaBFirstSelectBit << index));
                drives[index].SetSide(sideOne ? 1 : 0);
                drives[index].ApplyControlLines(selected, motorLineLow, stepLineHigh, directionIn);
            }
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick()
        {
            FloppyDrive[] drives = GetDrives();
            for (int index = 0; index < drives.Length; index++)
            {
                drives[index].Tick();
            }
        }

        /// <summary>
        /// Builds the CIA A Status Bits.
        /// </summary>
        public byte BuildCiaAStatusBits()
        {
            byte value = CiaAStatusMask;
            FloppyDrive drive = SelectedDrive;
            if (drive != null)
            {
                int selectedIndex = GetSelectedDriveIndex();
                bool physicalDrivePresent = selectedIndex == 0 || drive.HasPhysicalDrive;
                if (!physicalDrivePresent)
                {
                    return value;
                }

                if (!drive.HasDisk || drive.DiskChanged)
                {
                    value = (byte)(value & ~CiaADiskChangeBit);
                }

                if (drive.HasDisk && drive.WriteProtected)
                {
                    value = (byte)(value & ~CiaAWriteProtectBit);
                }

                if (drive.Track0)
                {
                    value = (byte)(value & ~CiaATrack0Bit);
                }

                bool readyPin = selectedIndex > 0 && !drive.MotorOn
                    ? drive.IdentificationReadySignal : drive.ReadySignal;
                if (readyPin)
                {
                    value = (byte)(value & ~CiaAReadyBit);
                }
            }

            return value;
        }

        /// <summary>
        /// Returns the Debug Info.
        /// </summary>
        public string GetDebugInfo()
        {
            FloppyDrive selectedDrive = SelectedDrive;
            int selectedIndex = GetSelectedDriveIndex();
            return string.Format(
                "selected:{0} selectedDrive:{1} motor:{2} disk:{3} track:{4} side:{5} ready:{6} ciaReady:{7} wp:{8} changed:{9} writes:{10} decoded:{11} status:${12:X2} stream:{13}/{14} index:{15} df0:{16} df1:{17} df2:{18} df3:{19}",
                selectedDrive != null,
                selectedIndex >= 0 ? "DF" + selectedIndex.ToString() : "none",
                selectedDrive != null && selectedDrive.MotorOn,
                selectedDrive != null && selectedDrive.HasDisk,
                selectedDrive != null ? selectedDrive.Track : Drive0.Track,
                selectedDrive != null ? selectedDrive.Side : Drive0.Side,
                selectedDrive != null && selectedDrive.Ready,
                selectedDrive != null && selectedDrive.ReadySignal,
                selectedDrive != null && selectedDrive.WriteProtected,
                selectedDrive != null && selectedDrive.DiskChanged,
                selectedDrive != null ? selectedDrive.CapturedWriteWordCount : 0,
                selectedDrive != null ? selectedDrive.DecodedWriteSectorCount : 0,
                BuildCiaAStatusBits(),
                selectedDrive != null ? selectedDrive.StreamWordPosition : Drive0.StreamWordPosition,
                selectedDrive != null ? selectedDrive.StreamWordCount : Drive0.StreamWordCount,
                selectedDrive != null ? selectedDrive.IndexPulseCountdown : Drive0.IndexPulseCountdown,
                FormatDriveDebug(Drive0),
                FormatDriveDebug(Drive1),
                FormatDriveDebug(Drive2),
                FormatDriveDebug(Drive3));
        }

        /// <summary>
        /// Formats a drive's physical state even when a different drive is currently selected.
        /// </summary>
        private static string FormatDriveDebug(FloppyDrive drive)
        {
            return string.Format(
                "sel={0},motor={1},disk={2},track={3},side={4},ready={5},wp={6},chg={7},stream={8}/{9},idx={10}",
                drive.Selected,
                drive.MotorOn,
                drive.HasDisk,
                drive.Track,
                drive.Side,
                drive.ReadySignal,
                drive.WriteProtected,
                drive.DiskChanged,
                drive.StreamWordPosition,
                drive.StreamWordCount,
                drive.IndexPulseCountdown);
        }

        /// <summary>
        /// Returns the currently selected drive index, or -1 if no drive is selected.
        /// </summary>
        private int GetSelectedDriveIndex()
        {
            FloppyDrive[] drives = GetDrives();
            for (int index = 0; index < drives.Length; index++)
            {
                if (drives[index].Selected)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns the drive array in DF0-DF3 bus order.
        /// </summary>
        private FloppyDrive[] GetDrives()
        {
            if (Drive1 == null)
            {
                Drive1 = new FloppyDrive();
            }

            if (Drive2 == null)
            {
                Drive2 = new FloppyDrive();
            }

            if (Drive3 == null)
            {
                Drive3 = new FloppyDrive();
            }

            return new[] { Drive0, Drive1, Drive2, Drive3 };
        }

        /// <summary>
        /// Returns whether Active Low Output is true.
        /// </summary>
        private static bool IsActiveLowOutput(byte value, byte direction, byte mask)
        {
            return (direction & mask) != 0 && (value & mask) == 0;
        }

        /// <summary>
        /// Returns whether High Output is true.
        /// </summary>
        private static bool IsHighOutput(byte value, byte direction, byte mask)
        {
            return (direction & mask) != 0 && (value & mask) != 0;
        }

        /// <summary>
        /// Raises the Media Changed notification.
        /// </summary>
        private void RaiseMediaChanged()
        {
            System.Action handler = MediaChanged;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
