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
using AmigaEmulator.Core.CustomChips;

namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// Lists the supported Custom Interrupt Source values.
    /// </summary>
    [Flags]
    public enum CustomInterruptSource : ushort
    {
        /// <summary>
        /// Represents None.
        /// </summary>
        None = 0x0000,
        /// <summary>
        /// Represents Tbe.
        /// </summary>
        Tbe = 0x0001,
        /// <summary>
        /// Represents Dsk Blk.
        /// </summary>
        DskBlk = 0x0002,
        /// <summary>
        /// Represents Soft.
        /// </summary>
        Soft = 0x0004,
        /// <summary>
        /// Represents Ports.
        /// </summary>
        Ports = 0x0008,
        /// <summary>
        /// Represents Copper.
        /// </summary>
        Copper = 0x0010,
        /// <summary>
        /// Represents Vertical Blank.
        /// </summary>
        VerticalBlank = 0x0020,
        /// <summary>
        /// Represents Blitter.
        /// </summary>
        Blitter = 0x0040,
        /// <summary>
        /// Represents Audio 0.
        /// </summary>
        Audio0 = 0x0080,
        /// <summary>
        /// Represents Audio 1.
        /// </summary>
        Audio1 = 0x0100,
        /// <summary>
        /// Represents Audio 2.
        /// </summary>
        Audio2 = 0x0200,
        /// <summary>
        /// Represents Audio 3.
        /// </summary>
        Audio3 = 0x0400,
        /// <summary>
        /// Represents Receive Buffer Full.
        /// </summary>
        ReceiveBufferFull = 0x0800,
        /// <summary>
        /// Represents Disk Sync.
        /// </summary>
        DiskSync = 0x1000,
        /// <summary>
        /// Represents External.
        /// </summary>
        External = 0x2000,
        /// <summary>
        /// Represents Master.
        /// </summary>
        Master = 0x4000
    }

    /// <summary>
    /// Stores custom-chip register state and implements set/clear register semantics.
    /// </summary>
    public sealed class CustomRegisterMap
    {
        /// <summary>
        /// Constant for Set Clear Bit.
        /// </summary>
        public const ushort SetClearBit = 0x8000;
        /// <summary>
        /// Constant for Custom Bit Mask.
        /// </summary>
        public const ushort CustomBitMask = 0x7FFF;
        /// <summary>
        /// Defines the DMA CONTROL BIT MASK custom register offset or bit mask.
        /// </summary>
        public const ushort DmaControlBitMask = 0x07FF;
        /// <summary>
        /// Defines the INTERRUPT REQUEST BIT MASK custom register offset or bit mask.
        /// </summary>
        public const ushort InterruptRequestBitMask = 0x3FFF;

        /// <summary>
        /// Defines the DMACONR custom register offset or bit mask.
        /// </summary>
        public const uint Dmaconr = 0x002;
        /// <summary>
        /// Defines the JOY0DAT custom register offset or bit mask.
        /// </summary>
        public const uint Joy0dat = 0x00A;
        /// <summary>
        /// Defines the JOY1DAT custom register offset or bit mask.
        /// </summary>
        public const uint Joy1dat = 0x00C;
        /// <summary>
        /// Defines the CLXDAT custom register offset or bit mask.
        /// </summary>
        public const uint Clxdat = 0x00E;
        /// <summary>
        /// Defines the ADKCONR custom register offset or bit mask.
        /// </summary>
        public const uint Adkconr = 0x010;
        /// <summary>
        /// Defines the POT0DAT custom register offset or bit mask.
        /// </summary>
        public const uint Pot0dat = 0x012;
        /// <summary>
        /// Defines the POT1DAT custom register offset or bit mask.
        /// </summary>
        public const uint Pot1dat = 0x014;
        /// <summary>
        /// Defines the POTINP custom register offset or bit mask.
        /// </summary>
        public const uint Potinp = 0x016;
        /// <summary>
        /// Defines the POTGO custom register offset or bit mask.
        /// </summary>
        public const uint Potgo = 0x034;
        /// <summary>
        /// Defines the INTENAR custom register offset or bit mask.
        /// </summary>
        public const uint Intenar = 0x01C;
        /// <summary>
        /// Defines the INTREQR custom register offset or bit mask.
        /// </summary>
        public const uint Intreqr = 0x01E;
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        public const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the CLXCON custom register offset or bit mask.
        /// </summary>
        public const uint Clxcon = 0x098;
        /// <summary>
        /// Defines the INTENA custom register offset or bit mask.
        /// </summary>
        public const uint Intena = 0x09A;
        /// <summary>
        /// Defines the INTREQ custom register offset or bit mask.
        /// </summary>
        public const uint Intreq = 0x09C;
        /// <summary>
        /// Defines the ADKCON custom register offset or bit mask.
        /// </summary>
        public const uint Adkcon = 0x09E;
        /// <summary>
        /// Defines the COPJMP1 custom register offset or bit mask.
        /// </summary>
        public const uint Copjmp1 = 0x088;
        /// <summary>
        /// Defines the COPJMP2 custom register offset or bit mask.
        /// </summary>
        public const uint Copjmp2 = 0x08A;

        /// <summary>
        /// Stores registers.
        /// </summary>
        private readonly ushort[] _registers = new ushort[0x1000 / 2];
        /// <summary>
        /// Stores dmacon.
        /// </summary>
        private ushort _dmacon;
        /// <summary>
        /// Stores adkcon.
        /// </summary>
        private ushort _adkcon;
        /// <summary>
        /// Stores intena.
        /// </summary>
        private ushort _intena;
        /// <summary>
        /// Stores intreq.
        /// </summary>
        private ushort _intreq;
        /// <summary>
        /// Stores potgo.
        /// </summary>
        private ushort _potgo;
        /// <summary>
        /// Stores live potentiometer counter values for POT0DAT and POT1DAT.
        /// </summary>
        private readonly byte[,] _potCounters = new byte[2, 2];
        /// <summary>
        /// Stores whether each potentiometer counter is running after a POTGO start strobe.
        /// </summary>
        private readonly bool[,] _potCounterActive = new bool[2, 2];
        /// <summary>
        /// Stores collision Unit.
        /// </summary>
        private CollisionUnit _collisionUnit;
        /// <summary>
        /// Stores is Blitter Busy.
        /// </summary>
        private Func<bool> _isBlitterBusy;
        /// <summary>
        /// Stores is Blitter Zero.
        /// </summary>
        private Func<bool> _isBlitterZero;
        /// <summary>
        /// Stores read Joy 0 Dat.
        /// </summary>
        private Func<ushort> _readJoy0Dat;
        /// <summary>
        /// Stores read Joy 1 Dat.
        /// </summary>
        private Func<ushort> _readJoy1Dat;
        /// <summary>
        /// Stores read Pot Inp.
        /// </summary>
        private Func<ushort, ushort> _readPotInp;

        /// <summary>
        /// Raised when custom register interrupt or Copper jump state changes.
        /// </summary>
        public event Action InterruptStateChanged;

        /// <summary>
        /// Raised when custom register interrupt or Copper jump state changes.
        /// </summary>
        public event Action<int> CopperJumpRequested;

        /// <summary>
        /// Gets DMA Control.
        /// </summary>
        public ushort DmaControl
        {
            get { return _dmacon; }
        }

        /// <summary>
        /// Gets Adk Control.
        /// </summary>
        public ushort AdkControl
        {
            get { return _adkcon; }
        }

        /// <summary>
        /// Gets Interrupt Enable.
        /// </summary>
        public ushort InterruptEnable
        {
            get { return _intena; }
        }

        /// <summary>
        /// Gets Interrupt Request.
        /// </summary>
        public ushort InterruptRequest
        {
            get { return _intreq; }
        }

        /// <summary>
        /// Gets Master Interrupt Enabled.
        /// </summary>
        public bool MasterInterruptEnabled
        {
            get { return (_intena & (ushort)CustomInterruptSource.Master) != 0; }
        }

        /// <summary>
        /// Gets Interrupt Pending.
        /// </summary>
        public bool InterruptPending
        {
            get { return ActiveInterruptLevel > 0; }
        }

        /// <summary>
        /// Gets Active Interrupt Level.
        /// </summary>
        public int ActiveInterruptLevel
        {
            get { return MasterInterruptEnabled ? GetInterruptLevel((ushort)(_intena & _intreq)) : 0; }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            for (int index = 0; index < _registers.Length; index++)
            {
                _registers[index] = 0;
            }

            _dmacon = 0;
            _adkcon = 0;
            _intena = 0;
            _intreq = 0;
            _potgo = 0;
            ResetPotCounters(false);
            RaiseInterruptStateChanged();
        }

        /// <summary>
        /// Attaches Blitter Status to this component.
        /// </summary>
        public void AttachBlitterStatus(Func<bool> isBusy, Func<bool> isZero)
        {
            _isBlitterBusy = isBusy;
            _isBlitterZero = isZero;
        }

        /// <summary>
        /// Attaches Input Readers to this component.
        /// </summary>
        public void AttachInputReaders(Func<ushort> readJoy0Dat, Func<ushort> readJoy1Dat, Func<ushort, ushort> readPotInp)
        {
            _readJoy0Dat = readJoy0Dat;
            _readJoy1Dat = readJoy1Dat;
            _readPotInp = readPotInp;
        }

        /// <summary>
        /// Attaches Denise collision state to CLXCON and CLXDAT.
        /// </summary>
        public void AttachCollisionUnit(CollisionUnit collisionUnit)
        {
            _collisionUnit = collisionUnit;
        }

        /// <summary>
        /// Reads a custom-chip register word, including readable mirrors and live input sources.
        /// </summary>
        public ushort ReadWord(uint address)
        {
            uint offset = GetOffset(address);
            if (offset == Dmaconr)
            {
                ushort status = _dmacon;
                if (_isBlitterBusy != null && _isBlitterBusy())
                {
                    status |= 0x4000;
                }

                if (_isBlitterZero == null || _isBlitterZero())
                {
                    status |= 0x2000;
                }

                return status;
            }

            if (offset == Copjmp1)
            {
                RaiseCopperJumpRequested(1);
                return _registers[GetIndex(address)];
            }

            if (offset == Copjmp2)
            {
                RaiseCopperJumpRequested(2);
                return _registers[GetIndex(address)];
            }

            if (offset == Dmacon)
            {
                return _dmacon;
            }

            if (offset == Adkconr || offset == Adkcon)
            {
                return _adkcon;
            }

            if (offset == Joy0dat)
            {
                return _readJoy0Dat != null ? _readJoy0Dat() : (ushort)0;
            }

            if (offset == Joy1dat)
            {
                return _readJoy1Dat != null ? _readJoy1Dat() : (ushort)0;
            }

            if (offset == Clxdat)
            {
                return _collisionUnit != null ? _collisionUnit.ReadAndClear() : (ushort)0;
            }

            if (offset == Pot0dat || offset == Pot1dat)
            {
                int port = offset == Pot0dat ? 0 : 1;
                return (ushort)((_potCounters[port, 1] << 8) | _potCounters[port, 0]);
            }

            if (offset == Potinp)
            {
                return _readPotInp != null ? _readPotInp(_potgo) : (ushort)0x5500;
            }

            if (offset == Potgo)
            {
                return _potgo;
            }

            if (offset == Intenar || offset == Intena)
            {
                return _intena;
            }

            if (offset == Intreqr || offset == Intreq)
            {
                return _intreq;
            }

            return _registers[GetIndex(address)];
        }

        /// <summary>
        /// Reads a custom-chip register word without read-clear side effects.
        /// </summary>
        public ushort PeekWord(uint address)
        {
            uint offset = GetOffset(address);
            if (offset == Clxdat)
            {
                return _collisionUnit != null ? _collisionUnit.CollisionFlags : (ushort)0;
            }

            if (offset == Copjmp1 || offset == Copjmp2)
            {
                return _registers[GetIndex(address)];
            }

            return ReadWord(address);
        }

        /// <summary>
        /// Writes a custom-chip register word and applies Amiga set/clear side effects.
        /// </summary>
        public void WriteWord(uint address, ushort value)
        {
            uint offset = GetOffset(address);
            if (offset == Copjmp1)
            {
                RaiseCopperJumpRequested(1);
                return;
            }

            if (offset == Copjmp2)
            {
                RaiseCopperJumpRequested(2);
                return;
            }

            if (offset == Dmacon)
            {
                _dmacon = ApplySetClear(_dmacon, value, DmaControlBitMask);
                _registers[GetIndex(address)] = _dmacon;
                _registers[GetIndex(Dmaconr)] = _dmacon;
                return;
            }

            if (offset == Clxcon)
            {
                if (_collisionUnit != null)
                {
                    _collisionUnit.Control = value;
                }

                _registers[GetIndex(address)] = value;
                return;
            }

            if (offset == Adkcon)
            {
                _adkcon = ApplySetClear(_adkcon, value);
                _registers[GetIndex(address)] = _adkcon;
                _registers[GetIndex(Adkconr)] = _adkcon;
                return;
            }

            if (offset == Potgo)
            {
                _potgo = value;
                if ((value & 0x0001) != 0)
                {
                    ResetPotCounters(true);
                }

                _registers[GetIndex(address)] = _potgo;
                return;
            }

            if (offset == Intena)
            {
                ushort oldLevelBits = GetActiveLevelBits();
                _intena = ApplySetClear(_intena, value);
                _registers[GetIndex(address)] = _intena;
                _registers[GetIndex(Intenar)] = _intena;
                RaiseInterruptStateChangedIfNeeded(oldLevelBits);
                return;
            }

            if (offset == Intreq)
            {
                ushort oldLevelBits = GetActiveLevelBits();
                _intreq = ApplySetClear(_intreq, value, InterruptRequestBitMask);
                _registers[GetIndex(address)] = _intreq;
                _registers[GetIndex(Intreqr)] = _intreq;
                RaiseInterruptStateChangedIfNeeded(oldLevelBits);
                return;
            }

            _registers[GetIndex(address)] = value;
        }

        /// <summary>
        /// Stores a custom-register word without applying hardware side effects.
        /// </summary>
        public void WriteRawWord(uint address, ushort value)
        {
            _registers[GetIndex(address)] = value;
        }

        /// <summary>
        /// Advances live potentiometer counters once per scanline.
        /// </summary>
        public void TickPotCounters()
        {
            for (int port = 0; port < 2; port++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    if (_potCounterActive[port, axis])
                    {
                        _potCounters[port, axis]++;
                    }
                }
            }
        }

        /// <summary>
        /// Clears potentiometer counters and optionally starts them.
        /// </summary>
        private void ResetPotCounters(bool active)
        {
            for (int port = 0; port < 2; port++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    _potCounters[port, axis] = 0;
                    _potCounterActive[port, axis] = active;
                }
            }
        }

        /// <summary>
        /// Reads one byte lane from a custom-chip register word.
        /// </summary>
        public byte ReadByte(uint address)
        {
            ushort word = ReadWord(address & 0xFFFFFE);
            return (address & 1) == 0 ? (byte)(word >> 8) : (byte)(word & 0xFF);
        }

        /// <summary>
        /// Reads one byte lane from a custom-chip register word without read-clear side effects.
        /// </summary>
        public byte PeekByte(uint address)
        {
            ushort word = PeekWord(address & 0xFFFFFE);
            return (address & 1) == 0 ? (byte)(word >> 8) : (byte)(word & 0xFF);
        }

        /// <summary>
        /// Writes one byte lane to a custom-chip register while preserving set/clear behavior.
        /// </summary>
        public void WriteByte(uint address, byte value)
        {
            uint wordAddress = address & 0xFFFFFE;
            if (IsStrobeRegister(wordAddress))
            {
                WriteWord(wordAddress, 0);
                return;
            }

            if (IsSetClearRegister(wordAddress))
            {
                ushort byteLaneValue = (address & 1) == 0
                    ? (ushort)(value << 8)
                    : value;
                WriteWord(wordAddress, byteLaneValue);
                return;
            }

            ushort current = _registers[GetIndex(wordAddress)];
            ushort updated = (address & 1) == 0
                ? (ushort)((current & 0x00FF) | (value << 8))
                : (ushort)((current & 0xFF00) | value);
            WriteWord(wordAddress, updated);
        }

        /// <summary>
        /// Runs Request Interrupt.
        /// </summary>
        public void RequestInterrupt(CustomInterruptSource source)
        {
            ushort requested = (ushort)((ushort)source & InterruptRequestBitMask);
            if (requested == 0)
            {
                return;
            }

            ushort oldLevelBits = GetActiveLevelBits();
            _intreq = (ushort)((_intreq | requested) & InterruptRequestBitMask);
            _registers[GetIndex(Intreq)] = _intreq;
            _registers[GetIndex(Intreqr)] = _intreq;
            RaiseInterruptStateChangedIfNeeded(oldLevelBits);
        }

        /// <summary>
        /// Runs Clear Interrupt.
        /// </summary>
        public void ClearInterrupt(CustomInterruptSource source)
        {
            ushort requested = (ushort)((ushort)source & InterruptRequestBitMask);
            if (requested == 0)
            {
                return;
            }

            ushort oldLevelBits = GetActiveLevelBits();
            _intreq = (ushort)(_intreq & ~requested);
            _registers[GetIndex(Intreq)] = _intreq;
            _registers[GetIndex(Intreqr)] = _intreq;
            RaiseInterruptStateChangedIfNeeded(oldLevelBits);
        }

        /// <summary>
        /// Returns the Word Write Mask.
        /// </summary>
        public static ushort GetWordWriteMask(uint address)
        {
            uint offset = GetOffset(address);
            if (offset == Dmacon || offset == Adkcon || offset == Intena || offset == Intreq)
            {
                return 0xFFFF;
            }

            return 0xFFFF;
        }

        /// <summary>
        /// Returns the Byte Write Mask.
        /// </summary>
        public static byte GetByteWriteMask(uint address)
        {
            ushort wordMask = GetWordWriteMask(address & 0xFFFFFE);
            return (address & 1) == 0 ? (byte)(wordMask >> 8) : (byte)(wordMask & 0xFF);
        }

        /// <summary>
        /// Returns the Active Level Bits.
        /// </summary>
        private ushort GetActiveLevelBits()
        {
            if (!MasterInterruptEnabled)
            {
                return 0;
            }

            return (ushort)(_intena & _intreq & InterruptRequestBitMask);
        }

        /// <summary>
        /// Applies Set Clear to the current state.
        /// </summary>
        private static ushort ApplySetClear(ushort current, ushort value)
        {
            return ApplySetClear(current, value, CustomBitMask);
        }

        /// <summary>
        /// Returns whether Set Clear Register is true.
        /// </summary>
        private static bool IsSetClearRegister(uint address)
        {
            uint offset = GetOffset(address);
            return offset == Dmacon || offset == Adkcon || offset == Intena || offset == Intreq;
        }

        /// <summary>
        /// Returns whether strobe Register is true.
        /// </summary>
        private static bool IsStrobeRegister(uint address)
        {
            uint offset = GetOffset(address);
            return offset == Copjmp1 || offset == Copjmp2;
        }

        /// <summary>
        /// Applies Set Clear to the current state.
        /// </summary>
        private static ushort ApplySetClear(ushort current, ushort value, ushort allowedMask)
        {
            ushort mask = (ushort)(value & allowedMask);
            if ((value & SetClearBit) != 0)
            {
                return (ushort)((current | mask) & allowedMask);
            }

            return (ushort)(current & ~mask & allowedMask);
        }

        /// <summary>
        /// Returns the Interrupt Level.
        /// </summary>
        private static int GetInterruptLevel(ushort activeBits)
        {
            ushort requests = (ushort)(activeBits & InterruptRequestBitMask);
            if ((requests & (ushort)CustomInterruptSource.External) != 0)
            {
                return 6;
            }

            if ((requests & ((ushort)CustomInterruptSource.DiskSync | (ushort)CustomInterruptSource.ReceiveBufferFull)) != 0)
            {
                return 5;
            }

            ushort level4 = (ushort)((ushort)CustomInterruptSource.Audio0 | (ushort)CustomInterruptSource.Audio1 | (ushort)CustomInterruptSource.Audio2 | (ushort)CustomInterruptSource.Audio3);
            if ((requests & level4) != 0)
            {
                return 4;
            }

            ushort level3 = (ushort)((ushort)CustomInterruptSource.Blitter | (ushort)CustomInterruptSource.VerticalBlank | (ushort)CustomInterruptSource.Copper);
            if ((requests & level3) != 0)
            {
                return 3;
            }

            if ((requests & (ushort)CustomInterruptSource.Ports) != 0)
            {
                return 2;
            }

            ushort level1 = (ushort)((ushort)CustomInterruptSource.Tbe | (ushort)CustomInterruptSource.DskBlk | (ushort)CustomInterruptSource.Soft);
            return (requests & level1) != 0 ? 1 : 0;
        }

        /// <summary>
        /// Raises the Interrupt State Changed If Needed notification.
        /// </summary>
        private void RaiseInterruptStateChangedIfNeeded(ushort oldLevelBits)
        {
            if (oldLevelBits != GetActiveLevelBits())
            {
                RaiseInterruptStateChanged();
            }
        }

        /// <summary>
        /// Raises the Interrupt State Changed notification.
        /// </summary>
        private void RaiseInterruptStateChanged()
        {
            Action handler = InterruptStateChanged;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Raises the Copper Jump Requested notification.
        /// </summary>
        private void RaiseCopperJumpRequested(int list)
        {
            Action<int> handler = CopperJumpRequested;
            if (handler != null)
            {
                handler(list);
            }
        }

        /// <summary>
        /// Returns the Offset.
        /// </summary>
        private static uint GetOffset(uint address)
        {
            return address & 0x01FE;
        }

        /// <summary>
        /// Returns the Index.
        /// </summary>
        private static int GetIndex(uint address)
        {
            return (int)(GetOffset(address) >> 1);
        }
    }
}
