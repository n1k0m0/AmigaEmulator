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
        /// Defines the CIA A Status Mask constant.
        /// </summary>
        public const byte CiaAStatusMask = 0x3C;
        /// <summary>
        /// Defines the CIA A Disk Change Bit constant.
        /// </summary>
        public const byte CiaADiskChangeBit = 0x04;
        /// <summary>
        /// Defines the CIA A Write Protect Bit constant.
        /// </summary>
        public const byte CiaAWriteProtectBit = 0x08;
        /// <summary>
        /// Defines the CIA A Track0 Bit constant.
        /// </summary>
        public const byte CiaATrack0Bit = 0x10;
        /// <summary>
        /// Defines the CIA A Ready Bit constant.
        /// </summary>
        public const byte CiaAReadyBit = 0x20;

        /// <summary>
        /// Defines the CIA B Step Bit constant.
        /// </summary>
        private const byte CiaBStepBit = 0x01;
        /// <summary>
        /// Defines the CIA B Direction Bit constant.
        /// </summary>
        private const byte CiaBDirectionBit = 0x02;
        /// <summary>
        /// Defines the CIA B Side Bit constant.
        /// </summary>
        private const byte CiaBSideBit = 0x04;
        /// <summary>
        /// Defines the CIA B Select0 Bit constant.
        /// </summary>
        private const byte CiaBSelect0Bit = 0x08;
        /// <summary>
        /// Defines the CIA B Motor Bit constant.
        /// </summary>
        private const byte CiaBMotorBit = 0x80;

        /// <summary>
        /// Initializes a new instance of the FloppyController class.
        /// </summary>
        public FloppyController()
        {
            Drive0 = new FloppyDrive();
        }

        /// <summary>
        /// Raised when mounted floppy media changes.
        /// </summary>
        public event System.Action MediaChanged;

        /// <summary>
        /// Gets or updates the Drive0.
        /// </summary>
        public FloppyDrive Drive0 { get; private set; }

        /// <summary>
        /// Executes the Index Pulse Pending logic.
        /// </summary>
        public bool IndexPulsePending
        {
            get { return Drive0.IndexPulsePending; }
        }

        /// <summary>
        /// Executes the Insert Drive0 logic.
        /// </summary>
        public void InsertDrive0(AdfImage image)
        {
            Drive0.Insert(image);
            RaiseMediaChanged();
        }

        /// <summary>
        /// Executes the Insert Drive0 logic.
        /// </summary>
        public void InsertDrive0(AdfImage image, bool writeProtected)
        {
            Drive0.Insert(image, writeProtected);
            RaiseMediaChanged();
        }

        /// <summary>
        /// Executes the Eject Drive0 logic.
        /// </summary>
        public void EjectDrive0()
        {
            Drive0.Eject();
            RaiseMediaChanged();
        }

        /// <summary>
        /// Applies CIA B Port to the current state.
        /// </summary>
        public void ApplyCiaBPort(byte prb, byte ddrb)
        {
            bool selected = IsActiveLowOutput(prb, ddrb, CiaBSelect0Bit);
            bool motorLineLow = IsActiveLowOutput(prb, ddrb, CiaBMotorBit);
            bool stepLineHigh = !IsActiveLowOutput(prb, ddrb, CiaBStepBit);
            bool directionIn = !IsHighOutput(prb, ddrb, CiaBDirectionBit);
            bool sideOne = IsActiveLowOutput(prb, ddrb, CiaBSideBit);

            Drive0.ApplySelection(selected, motorLineLow);
            Drive0.SetSide(sideOne ? 1 : 0);
            Drive0.SetStepLine(stepLineHigh, directionIn);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick()
        {
            Drive0.Tick();
        }

        /// <summary>
        /// Builds the CIA A Status Bits.
        /// </summary>
        public byte BuildCiaAStatusBits()
        {
            byte value = CiaAStatusMask;
            if (Drive0.Selected)
            {
                if (!Drive0.HasDisk || Drive0.DiskChanged)
                {
                    value = (byte)(value & ~CiaADiskChangeBit);
                }

                if (Drive0.HasDisk && Drive0.WriteProtected)
                {
                    value = (byte)(value & ~CiaAWriteProtectBit);
                }

                if (Drive0.Track0)
                {
                    value = (byte)(value & ~CiaATrack0Bit);
                }

                if (Drive0.Ready)
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
            return string.Format(
                "selected:{0} motor:{1} disk:{2} track:{3} side:{4} ready:{5} wp:{6} changed:{7} writes:{8} decoded:{9} status:${10:X2}",
                Drive0.Selected,
                Drive0.MotorOn,
                Drive0.HasDisk,
                Drive0.Track,
                Drive0.Side,
                Drive0.Ready,
                Drive0.WriteProtected,
                Drive0.DiskChanged,
                Drive0.CapturedWriteWordCount,
                Drive0.DecodedWriteSectorCount,
                BuildCiaAStatusBits());
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
