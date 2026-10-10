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
using System.IO;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Core.Cia
{
    /// <summary>
    /// Emulates an MOS 8520 CIA including ports, timers, TOD, serial input, and interrupt state.
    /// </summary>
    public sealed class Cia8520
    {
        /// <summary>
        /// Constant for Register PRA.
        /// </summary>
        private const int RegisterPra = 0;
        /// <summary>
        /// Constant for Register PRB.
        /// </summary>
        private const int RegisterPrb = 1;
        /// <summary>
        /// Constant for Register DDRA.
        /// </summary>
        private const int RegisterDdra = 2;
        /// <summary>
        /// Constant for Register DDRB.
        /// </summary>
        private const int RegisterDdrb = 3;
        /// <summary>
        /// Constant for Register Timer A Low.
        /// </summary>
        private const int RegisterTimerALow = 4;
        /// <summary>
        /// Constant for Register Timer A High.
        /// </summary>
        private const int RegisterTimerAHigh = 5;
        /// <summary>
        /// Constant for Register Timer B Low.
        /// </summary>
        private const int RegisterTimerBLow = 6;
        /// <summary>
        /// Constant for Register Timer B High.
        /// </summary>
        private const int RegisterTimerBHigh = 7;
        /// <summary>
        /// Constant for Register TOD Low.
        /// </summary>
        private const int RegisterTodLow = 8;
        /// <summary>
        /// Constant for Register TOD Mid.
        /// </summary>
        private const int RegisterTodMid = 9;
        /// <summary>
        /// Constant for Register TOD High.
        /// </summary>
        private const int RegisterTodHigh = 10;
        /// <summary>
        /// Constant for Register Sdr.
        /// </summary>
        private const int RegisterSdr = 12;
        /// <summary>
        /// Constant for Register Icr.
        /// </summary>
        private const int RegisterIcr = 13;
        /// <summary>
        /// Constant for Register Cra.
        /// </summary>
        private const int RegisterCra = 14;
        /// <summary>
        /// Constant for Register Crb.
        /// </summary>
        private const int RegisterCrb = 15;

        /// <summary>
        /// Defines the INTERRUPT TIMER A custom register offset or bit mask.
        /// </summary>
        private const byte InterruptTimerA = 0x01;
        /// <summary>
        /// Defines the INTERRUPT TIMER B custom register offset or bit mask.
        /// </summary>
        private const byte InterruptTimerB = 0x02;
        /// <summary>
        /// Defines the INTERRUPT ALARM custom register offset or bit mask.
        /// </summary>
        private const byte InterruptAlarm = 0x04;
        /// <summary>
        /// Defines the INTERRUPT SERIAL custom register offset or bit mask.
        /// </summary>
        private const byte InterruptSerial = 0x08;
        /// <summary>
        /// Defines the INTERRUPT FLAG custom register offset or bit mask.
        /// </summary>
        private const byte InterruptFlag = 0x10;
        /// <summary>
        /// Defines the INTERRUPT MASTER custom register offset or bit mask.
        /// </summary>
        private const byte InterruptMaster = 0x80;

        /// <summary>
        /// Stores name.
        /// </summary>
        private readonly string _name;
        /// <summary>
        /// Stores registers.
        /// </summary>
        private readonly byte[] _registers = new byte[16];
        /// <summary>
        /// Stores port A Input Mask.
        /// </summary>
        private byte _portAInputMask;
        /// <summary>
        /// Stores port A Input Value.
        /// </summary>
        private byte _portAInputValue;
        /// <summary>
        /// Stores port B Input Mask.
        /// </summary>
        private byte _portBInputMask;
        /// <summary>
        /// Stores port B Input Value.
        /// </summary>
        private byte _portBInputValue;
        /// <summary>
        /// Stores timer A Counter.
        /// </summary>
        private ushort _timerACounter;
        /// <summary>
        /// Stores timer B Counter.
        /// </summary>
        private ushort _timerBCounter;
        /// <summary>
        /// Stores timer A Latch.
        /// </summary>
        private ushort _timerALatch;
        /// <summary>
        /// Stores timer B Latch.
        /// </summary>
        private ushort _timerBLatch;
        /// <summary>
        /// Stores TOD Alarm.
        /// </summary>
        private int _todAlarm;
        /// <summary>
        /// Stores TOD Read Latch.
        /// </summary>
        private int _todReadLatch;
        /// <summary>
        /// Stores TOD Read Latched.
        /// </summary>
        private bool _todReadLatched;
        /// <summary>
        /// Stores TOD Stopped.
        /// </summary>
        private bool _todStopped;
        /// <summary>
        /// Stores TOD Alarm Write Mask.
        /// </summary>
        private byte _todAlarmWriteMask;
        /// <summary>
        /// Stores interrupt Mask.
        /// </summary>
        private byte _interruptMask;
        /// <summary>
        /// Stores interrupt Request.
        /// </summary>
        private byte _interruptRequest;
        /// <summary>
        /// Stores last Overlay At Zero.
        /// </summary>
        private bool _lastOverlayAtZero;
        /// <summary>
        /// Tracks keyboard/serial bytes waiting for the emulated CNT/SP shift.
        /// </summary>
        private readonly Queue<byte> _serialInputQueue = new Queue<byte>();
        private readonly Queue<int> _serialInputPeriods = new Queue<int>();
        private int _serialInputBitPeriodEClocks = 1;
        private int _serialInputClockCountdown;
        /// <summary>
        /// Stores serial Shift Byte.
        /// </summary>
        private byte _serialShiftByte;
        /// <summary>
        /// Tracks remaining serial bits before SDR is full.
        /// </summary>
        private int _serialBitsRemaining;
        // Timer A supplies two underflows per outgoing CNT pulse, eight pulses per byte.
        private int _serialOutputHalfClocks;
        private bool _serialOutputBufferPending;
        // Retain the host's serial-output-to-input transition even when it falls
        // between peripheral ticks. CIA-A's keyboard uses it as KDAT handshake.
        private uint _serialInputHandshakeSequence;
        internal uint SerialInputHandshakeSequence => _serialInputHandshakeSequence;

        /// <summary>
        /// Initializes a new instance of the Cia8520 class.
        /// </summary>
        public Cia8520(string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "CIA" : name;
            Reset();
        }

        /// <summary>
        /// Raised when CIA overlay or interrupt output state changes.
        /// </summary>
        public event Action<bool> OverlayChanged;

        /// <summary>
        /// Raised when CIA overlay or interrupt output state changes.
        /// </summary>
        public event Action<Cia8520> InterruptChanged;

        /// <summary>
        /// Gets Name.
        /// </summary>
        public string Name
        {
            get { return _name; }
        }

        /// <summary>
        /// Writes CIA port, timer, TOD, serial, and interrupt state to a savestate.
        /// </summary>
        public void SaveState(BinaryWriter writer)
        {
            StateSerializer.WriteObjectFields(writer, this);
        }

        /// <summary>
        /// Restores CIA port, timer, TOD, serial, and interrupt state from a savestate.
        /// </summary>
        public void LoadState(BinaryReader reader)
        {
            // Old snapshots had no serial-output bookkeeping. Recover an in-flight
            // output-mode transfer from their saved SDR/timer state.
            _serialOutputHalfClocks = -1;
            _serialOutputBufferPending = false;
            _serialInputHandshakeSequence = 0;
            _serialInputPeriods.Clear();
            _serialInputBitPeriodEClocks = 1;
            _serialInputClockCountdown = 0;
            StateSerializer.ReadObjectFields(reader, this);
            if (_serialOutputHalfClocks < 0)
                _serialOutputHalfClocks = SerialOutputMode && TimerStarted(RegisterCra) && (_interruptRequest & InterruptSerial) == 0 ? 16 : 0;
        }

        /// <summary>
        /// Gets PRA.
        /// </summary>
        public byte Pra
        {
            get { return _registers[RegisterPra]; }
        }

        /// <summary>Active-low CIA-A /LED output selects the audio low-pass circuit.</summary>
        public bool AudioFilterLedEnabled => IsCiaA && !GetEffectivePortBit(RegisterPra, 1);

        /// <summary>
        /// Gets PRB.
        /// </summary>
        public byte Prb
        {
            get { return _registers[RegisterPrb]; }
        }

        /// <summary>
        /// Gets DDRA.
        /// </summary>
        public byte Ddra
        {
            get { return _registers[RegisterDdra]; }
        }

        /// <summary>
        /// Gets DDRB.
        /// </summary>
        public byte Ddrb
        {
            get { return _registers[RegisterDdrb]; }
        }

        /// <summary>
        /// Gets Timer A.
        /// </summary>
        public ushort TimerA
        {
            get { return _timerACounter; }
        }

        /// <summary>
        /// Gets Timer B.
        /// </summary>
        public ushort TimerB
        {
            get { return _timerBCounter; }
        }

        /// <summary>
        /// Gets Timer A Latch.
        /// </summary>
        public ushort TimerALatch
        {
            get { return _timerALatch; }
        }

        /// <summary>
        /// Gets Timer B Latch.
        /// </summary>
        public ushort TimerBLatch
        {
            get { return _timerBLatch; }
        }

        /// <summary>
        /// Gets Interrupt Control.
        /// </summary>
        public byte InterruptControl
        {
            get { return BuildIcrValue(); }
        }

        /// <summary>
        /// Gets Interrupt Mask.
        /// </summary>
        public byte InterruptMask
        {
            get { return _interruptMask; }
        }

        /// <summary>
        /// Gets Interrupt Request.
        /// </summary>
        public byte InterruptRequest
        {
            get { return _interruptRequest; }
        }

        /// <summary>
        /// Gets Interrupt Pending.
        /// </summary>
        public bool InterruptPending
        {
            get { return (_interruptRequest & _interruptMask) != 0; }
        }

        /// <summary>
        /// Gets Serial Data.
        /// </summary>
        public byte SerialData
        {
            get { return _registers[RegisterSdr]; }
        }

        /// <summary>
        /// Returns whether Serial Input Pending is true.
        /// </summary>
        public bool IsSerialInputPending
        {
            get { return SerialOutputMode || (_interruptRequest & InterruptSerial) != 0 || _serialBitsRemaining > 0 || _serialInputQueue.Count > 0; }
        }

        /// <summary>
        /// Returns the Debug Info.
        /// </summary>
        public string GetDebugInfo()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "PRA=${0:X2} PRB=${1:X2} DDRA=${2:X2} DDRB=${3:X2} TA=${4:X4}/{5:X4} TB=${6:X4}/{7:X4} TOD=${8:X6} ALARM=${9:X6} CRA=${10:X2} CRB=${11:X2} ICR=${12:X2} MASK=${13:X2} REQ=${14:X2} IRQ:{15}",
                _registers[RegisterPra],
                _registers[RegisterPrb],
                _registers[RegisterDdra],
                _registers[RegisterDdrb],
                _timerACounter,
                _timerALatch,
                _timerBCounter,
                _timerBLatch,
                ((_registers[RegisterTodHigh] & 0xFF) << 16) | ((_registers[RegisterTodMid] & 0xFF) << 8) | _registers[RegisterTodLow],
                _todAlarm,
                _registers[RegisterCra],
                _registers[RegisterCrb],
                BuildIcrValue(),
                _interruptMask,
                _interruptRequest,
                InterruptPending);
        }

        /// <summary>
        /// Gets Overlay At Zero.
        /// </summary>
        public bool OverlayAtZero
        {
            get
            {
                if (!IsCiaA)
                {
                    return false;
                }

                if ((_registers[RegisterDdra] & 0x01) == 0)
                {
                    return true;
                }

                return GetEffectivePortBit(RegisterPra, 0);
            }
        }

        /// <summary>
        /// Resets the value state.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_registers, 0, _registers.Length);
            _registers[RegisterPra] = 0x03;
            _portAInputMask = 0;
            _portAInputValue = 0;
            _portBInputMask = 0;
            _portBInputValue = 0;
            _timerALatch = 0xFFFF;
            _timerBLatch = 0xFFFF;
            _timerACounter = _timerALatch;
            _timerBCounter = _timerBLatch;
            _todAlarm = 0;
            _todReadLatch = 0;
            _todReadLatched = false;
            _todStopped = false;
            _todAlarmWriteMask = 0;
            _interruptMask = 0;
            _interruptRequest = 0;
            _serialInputQueue.Clear();
            _serialInputPeriods.Clear();
            _serialInputBitPeriodEClocks = 1;
            _serialInputClockCountdown = 0;
            _serialShiftByte = 0;
            _serialBitsRemaining = 0;
            _serialOutputHalfClocks = 0;
            _serialOutputBufferPending = false;
            _serialInputHandshakeSequence = 0;
            _lastOverlayAtZero = OverlayAtZero;
            RaiseOverlayChanged(_lastOverlayAtZero);
            RaiseInterruptChanged();
        }

        /// <summary>
        /// Updates the Port A Input Bits.
        /// </summary>
        public void SetPortAInputBits(byte mask, byte value)
        {
            _portAInputMask = mask;
            _portAInputValue = (byte)(value & mask);
            NotifyOverlayIfChanged();
        }

        /// <summary>
        /// Updates the Port B Input Bits.
        /// </summary>
        public void SetPortBInputBits(byte mask, byte value)
        {
            _portBInputMask = mask;
            _portBInputValue = (byte)(value & mask);
        }

        /// <summary>
        /// Advances this component by one emulator tick.
        /// </summary>
        public void Tick()
        {
            bool timerAUnderflow = TickTimer(RegisterCra, ref _timerACounter, _timerALatch, InterruptTimerA);
            if (TimerBCountsEClock() || (timerAUnderflow && TimerBCountsTimerAUnderflow()))
            {
                TickTimer(RegisterCrb, ref _timerBCounter, _timerBLatch, InterruptTimerB);
            }

            if (SerialOutputMode)
            {
                if (timerAUnderflow) TickSerialOutput();
            }
            else TickSerialInput();
        }

        /// <summary>
        /// Advances the TOD Event state by one emulator tick.
        /// </summary>
        public void TickTodEvent()
        {
            IncrementTod();
        }

        /// <summary>
        /// Runs Request Interrupt.
        /// </summary>
        public void RequestInterrupt(byte mask)
        {
            byte requested = (byte)(mask & 0x1F);
            if (requested == 0)
            {
                return;
            }

            byte oldIcr = BuildIcrValue();
            _interruptRequest = (byte)(_interruptRequest | requested);
            if (BuildIcrValue() != oldIcr)
            {
                RaiseInterruptChanged();
            }
        }

        /// <summary>
        /// Clears selected latched ICR request bits after a delayed CPU read retires.
        /// </summary>
        public void ClearInterruptRequest(byte mask)
        {
            byte cleared = (byte)(mask & 0x1F);
            if (cleared == 0)
            {
                return;
            }

            byte oldIcr = BuildIcrValue();
            _interruptRequest = (byte)(_interruptRequest & ~cleared);
            if (BuildIcrValue() != oldIcr)
            {
                RaiseInterruptChanged();
            }
        }

        /// <summary>
        /// Loads Serial Input Byte into emulator state.
        /// </summary>
        public void LoadSerialInputByte(byte value)
        {
            _registers[RegisterSdr] = value;
            _serialBitsRemaining = 0;
            _serialInputClockCountdown = 0;
            RequestInterrupt(InterruptSerial);
        }

        /// <summary>
        /// Queues one external serial byte and shifts it into SDR over CIA ticks.
        /// </summary>
        public void QueueSerialInputByte(byte value)
        {
            QueueSerialInputByte(value, 1);
        }

        public void QueueSerialInputByte(byte value, int eClocksPerBit)
        {
            if (eClocksPerBit < 1) throw new ArgumentOutOfRangeException(nameof(eClocksPerBit));
            // Old snapshots can contain queued bytes without period metadata.
            while (_serialInputPeriods.Count < _serialInputQueue.Count) _serialInputPeriods.Enqueue(1);
            _serialInputQueue.Enqueue(value);
            _serialInputPeriods.Enqueue(eClocksPerBit);
        }

        /// <summary>
        /// Runs Pulse Flag.
        /// </summary>
        public void PulseFlag()
        {
            RequestInterrupt(InterruptFlag);
        }

        /// <summary>
        /// Reads a register from this CIA through the Amiga odd/even byte lane mapping.
        /// </summary>
        public byte ReadByte(uint address)
        {
            int index = GetRegisterIndex(address);
            if (index == RegisterTimerALow)
            {
                return (byte)(_timerACounter & 0xFF);
            }

            if (index == RegisterTimerAHigh)
            {
                return (byte)(_timerACounter >> 8);
            }

            if (index == RegisterTimerBLow)
            {
                return (byte)(_timerBCounter & 0xFF);
            }

            if (index == RegisterTimerBHigh)
            {
                return (byte)(_timerBCounter >> 8);
            }

            if (index == RegisterTodLow || index == RegisterTodMid || index == RegisterTodHigh)
            {
                return ReadTodByte(index);
            }

            if (index == RegisterSdr)
            {
                return _registers[RegisterSdr];
            }

            if (index == RegisterIcr)
            {
                byte value = BuildIcrValue();
                if (_interruptRequest != 0)
                {
                    _interruptRequest = 0;
                    RaiseInterruptChanged();
                }

                return value;
            }

            if (index == RegisterPra || index == RegisterPrb)
            {
                return ReadPort(index);
            }

            return _registers[index];
        }

        /// <summary>
        /// Reads a CIA register without applying read side effects such as ICR clearing or TOD latching.
        /// </summary>
        public byte PeekByte(uint address)
        {
            int index = GetRegisterIndex(address);
            if (index == RegisterTimerALow)
            {
                return (byte)(_timerACounter & 0xFF);
            }

            if (index == RegisterTimerAHigh)
            {
                return (byte)(_timerACounter >> 8);
            }

            if (index == RegisterTimerBLow)
            {
                return (byte)(_timerBCounter & 0xFF);
            }

            if (index == RegisterTimerBHigh)
            {
                return (byte)(_timerBCounter >> 8);
            }

            if (index == RegisterTodLow || index == RegisterTodMid || index == RegisterTodHigh)
            {
                int value = _todReadLatched ? _todReadLatch : GetTodValue();
                if (index == RegisterTodHigh)
                {
                    return (byte)((value >> 16) & 0xFF);
                }

                return index == RegisterTodMid
                    ? (byte)((value >> 8) & 0xFF)
                    : (byte)(value & 0xFF);
            }

            if (index == RegisterSdr)
            {
                return _registers[RegisterSdr];
            }

            if (index == RegisterIcr)
            {
                return BuildIcrValue();
            }

            if (index == RegisterPra || index == RegisterPrb)
            {
                return ReadPort(index);
            }

            return _registers[index];
        }

        /// <summary>
        /// Writes a register through the Amiga CIA byte lane mapping and updates side effects.
        /// </summary>
        public void WriteByte(uint address, byte value)
        {
            int index = GetRegisterIndex(address);
            byte previousValue = _registers[index];
            _registers[index] = value;

            if (index == RegisterPra || index == RegisterDdra)
            {
                NotifyOverlayIfChanged();
            }

            if (index == RegisterTimerALow)
            {
                _timerALatch = (ushort)((_timerALatch & 0xFF00) | value);
            }
            else if (index == RegisterTimerAHigh)
            {
                _timerALatch = (ushort)((_timerALatch & 0x00FF) | (value << 8));
                if (!TimerStarted(RegisterCra))
                {
                    _timerACounter = ReloadValue(_timerALatch);
                    StartOneShotTimerOnHighByteWrite(RegisterCra);
                }
            }
            else if (index == RegisterTimerBLow)
            {
                _timerBLatch = (ushort)((_timerBLatch & 0xFF00) | value);
            }
            else if (index == RegisterTimerBHigh)
            {
                _timerBLatch = (ushort)((_timerBLatch & 0x00FF) | (value << 8));
                if (!TimerStarted(RegisterCrb))
                {
                    _timerBCounter = ReloadValue(_timerBLatch);
                    StartOneShotTimerOnHighByteWrite(RegisterCrb);
                }
            }
            else if (index == RegisterTodLow || index == RegisterTodMid || index == RegisterTodHigh)
            {
                if (TodAlarmWriteSelected())
                {
                    _registers[index] = previousValue;
                    WriteTodAlarmByte(index, value);
                }
                else
                {
                    WriteTodByte(index, value);
                }
            }
            else if (index == RegisterIcr)
            {
                WriteIcr(value);
            }
            else if (index == RegisterSdr && SerialOutputMode)
            {
                // SDR is the one-byte holding buffer; writing while busy queues
                // the following byte instead of restarting the current transfer.
                _serialOutputBufferPending = true;
            }
            else if (index == RegisterCra)
            {
                if (((previousValue ^ value) & 0x40) != 0)
                {
                    if ((previousValue & 0x40) != 0) _serialInputHandshakeSequence++;
                    _serialBitsRemaining = 0;
                    _serialInputClockCountdown = 0;
                    _serialOutputHalfClocks = 0;
                    _serialOutputBufferPending = false;
                }
                WriteControl(RegisterCra, ref _timerACounter, _timerALatch, value);
            }
            else if (index == RegisterCrb)
            {
                WriteControl(RegisterCrb, ref _timerBCounter, _timerBLatch, value);
            }
        }

        /// <summary>
        /// Returns whether CIA A is true.
        /// </summary>
        private bool IsCiaA
        {
            get { return string.Equals(_name, "CIA-A", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Builds the Icr Value.
        /// </summary>
        private byte BuildIcrValue()
        {
            byte value = (byte)(_interruptRequest & 0x1F);
            if ((_interruptRequest & _interruptMask) != 0)
            {
                value = (byte)(value | InterruptMaster);
            }

            return value;
        }

        /// <summary>
        /// Returns the Effective Port Bit.
        /// </summary>
        private bool GetEffectivePortBit(int registerIndex, int bit)
        {
            byte mask = (byte)(1 << bit);
            byte direction = registerIndex == RegisterPra ? _registers[RegisterDdra] : _registers[RegisterDdrb];
            if ((direction & mask) == 0)
            {
                byte inputMask = registerIndex == RegisterPra ? _portAInputMask : _portBInputMask;
                byte inputValue = registerIndex == RegisterPra ? _portAInputValue : _portBInputValue;
                if ((inputMask & mask) != 0)
                {
                    return (inputValue & mask) != 0;
                }

                return true;
            }

            return (_registers[registerIndex] & mask) != 0;
        }

        /// <summary>
        /// Combines CIA port output latches, direction bits, and external pins for a register read.
        /// </summary>
        private byte ReadPort(int registerIndex)
        {
            byte direction = registerIndex == RegisterPra ? _registers[RegisterDdra] : _registers[RegisterDdrb];
            byte inputMask = registerIndex == RegisterPra ? _portAInputMask : _portBInputMask;
            byte inputValue = registerIndex == RegisterPra ? _portAInputValue : _portBInputValue;
            byte inputs = (byte)((byte)~direction | (inputValue & inputMask));
            inputs = (byte)((inputs & (byte)~inputMask) | (inputValue & inputMask));
            return (byte)((_registers[registerIndex] & direction) | (inputs & (byte)~direction));
        }

        /// <summary>
        /// Notifies this component about Overlay If Changed.
        /// </summary>
        private void NotifyOverlayIfChanged()
        {
            if (!IsCiaA)
            {
                return;
            }

            bool overlayAtZero = OverlayAtZero;
            if (overlayAtZero != _lastOverlayAtZero)
            {
                _lastOverlayAtZero = overlayAtZero;
                RaiseOverlayChanged(overlayAtZero);
            }
        }

        /// <summary>
        /// Raises the Overlay Changed notification.
        /// </summary>
        private void RaiseOverlayChanged(bool overlayAtZero)
        {
            Action<bool> handler = OverlayChanged;
            if (handler != null)
            {
                handler(overlayAtZero);
            }
        }

        /// <summary>
        /// Raises the Interrupt Changed notification.
        /// </summary>
        private void RaiseInterruptChanged()
        {
            Action<Cia8520> handler = InterruptChanged;
            if (handler != null)
            {
                handler(this);
            }
        }

        /// <summary>
        /// Runs Timer Started.
        /// </summary>
        private bool TimerStarted(int controlRegister)
        {
            return (_registers[controlRegister] & 0x01) != 0;
        }

        /// <summary>
        /// Handles the e Shot Mode callback.
        /// </summary>
        private bool OneShotMode(int controlRegister)
        {
            return (_registers[controlRegister] & 0x08) != 0;
        }

        /// <summary>
        /// Runs Start One Shot Timer On High Byte Write.
        /// </summary>
        private void StartOneShotTimerOnHighByteWrite(int controlRegister)
        {
            if (OneShotMode(controlRegister))
            {
                _registers[controlRegister] = (byte)(_registers[controlRegister] | 0x01);
            }
        }

        /// <summary>
        /// Runs Timer B Counts E Clock.
        /// </summary>
        private bool TimerBCountsEClock()
        {
            return (_registers[RegisterCrb] & 0x60) == 0;
        }

        /// <summary>
        /// Runs Timer B Counts Timer A Underflow.
        /// </summary>
        private bool TimerBCountsTimerAUnderflow()
        {
            return (_registers[RegisterCrb] & 0x40) != 0;
        }

        /// <summary>
        /// Selects the direction of the CIA serial shift register.
        /// </summary>
        private bool SerialOutputMode => (_registers[RegisterCra] & 0x40) != 0;

        private void TickSerialOutput()
        {
            if (_serialOutputHalfClocks == 0)
            {
                if (!_serialOutputBufferPending) return;
                _serialOutputBufferPending = false;
                _serialOutputHalfClocks = 16;
            }
            if (--_serialOutputHalfClocks == 0) RequestInterrupt(InterruptSerial);
        }

        /// <summary>
        /// Advances the externally clocked input byte while CRA selects input mode.
        /// </summary>
        private void TickSerialInput()
        {
            if ((_interruptRequest & InterruptSerial) != 0)
            {
                return;
            }

            if (_serialBitsRemaining == 0)
            {
                if (_serialInputQueue.Count == 0)
                {
                    return;
                }

                _serialShiftByte = _serialInputQueue.Dequeue();
                _serialBitsRemaining = 8;
                // Legacy snapshots retain their one-edge-per-tick queued bytes.
                _serialInputBitPeriodEClocks = _serialInputPeriods.Count > 0 ? _serialInputPeriods.Dequeue() : 1;
                _serialInputClockCountdown = _serialInputBitPeriodEClocks;
            }

            if (_serialInputClockCountdown > 0 && --_serialInputClockCountdown > 0) return;
            _serialBitsRemaining--;
            _serialInputClockCountdown = _serialInputBitPeriodEClocks;
            if (_serialBitsRemaining == 0)
            {
                _registers[RegisterSdr] = _serialShiftByte;
                RequestInterrupt(InterruptSerial);
            }
        }

        /// <summary>
        /// Advances the Timer state by one emulator tick.
        /// </summary>
        private bool TickTimer(int controlRegister, ref ushort counter, ushort latch, byte interruptBit)
        {
            if (!TimerStarted(controlRegister))
            {
                return false;
            }

            if (counter == 0)
            {
                counter = ReloadValue(latch);
            }

            counter--;
            if (counter != 0)
            {
                return false;
            }

            RequestInterrupt(interruptBit);
            counter = ReloadValue(latch);
            if (OneShotMode(controlRegister))
            {
                _registers[controlRegister] = (byte)(_registers[controlRegister] & 0xFE);
            }

            return true;
        }

        /// <summary>
        /// Applies timer control-register changes, including start, load, and one-shot behavior.
        /// </summary>
        private void WriteControl(int controlRegister, ref ushort counter, ushort latch, byte value)
        {
            bool wasStarted = TimerStarted(controlRegister);
            bool alarmWriteWasSelected = controlRegister == RegisterCrb && TodAlarmWriteSelected();
            _registers[controlRegister] = (byte)(value & 0xEF);
            if (controlRegister == RegisterCrb && !alarmWriteWasSelected && TodAlarmWriteSelected())
            {
                _todAlarmWriteMask = 0;
            }

            bool startedNow = !wasStarted && (value & 0x01) != 0;
            if (startedNow || (value & 0x10) != 0)
            {
                counter = ReloadValue(latch);
                _registers[controlRegister] = (byte)(_registers[controlRegister] & 0xEF);
            }
        }

        /// <summary>
        /// Runs Increment Tod.
        /// </summary>
        private void IncrementTod()
        {
            if (_todStopped)
            {
                return;
            }

            int value = GetTodValue();
            value = (value + 1) & 0x00FFFFFF;
            _registers[RegisterTodLow] = (byte)(value & 0xFF);
            _registers[RegisterTodMid] = (byte)((value >> 8) & 0xFF);
            _registers[RegisterTodHigh] = (byte)((value >> 16) & 0xFF);
            CheckTodAlarm(value);
        }

        /// <summary>
        /// Reads one byte from the latched or live CIA time-of-day counter.
        /// </summary>
        private byte ReadTodByte(int index)
        {
            if (index == RegisterTodHigh)
            {
                _todReadLatch = GetTodValue();
                _todReadLatched = true;
                return (byte)((_todReadLatch >> 16) & 0xFF);
            }

            int value = _todReadLatched ? _todReadLatch : GetTodValue();
            byte result = index == RegisterTodMid
                ? (byte)((value >> 8) & 0xFF)
                : (byte)(value & 0xFF);

            if (index == RegisterTodLow)
            {
                _todReadLatched = false;
            }

            return result;
        }

        /// <summary>
        /// Writes one byte of the CIA time-of-day counter or alarm latch.
        /// </summary>
        private void WriteTodByte(int index, byte value)
        {
            if (index == RegisterTodLow)
            {
                _registers[RegisterTodLow] = value;
                _todStopped = false;
                _todReadLatched = false;
                return;
            }

            _todStopped = true;
            _registers[index] = value;
            _todReadLatched = false;
        }

        /// <summary>
        /// Returns the TOD Value.
        /// </summary>
        private int GetTodValue()
        {
            return ((_registers[RegisterTodHigh] & 0xFF) << 16) |
                ((_registers[RegisterTodMid] & 0xFF) << 8) |
                _registers[RegisterTodLow];
        }

        /// <summary>
        /// Checks TOD Alarm for test validation.
        /// </summary>
        private void CheckTodAlarm(int value)
        {
            if ((value & 0x00FFFFFF) == _todAlarm)
            {
                RequestInterrupt(InterruptAlarm);
            }
        }

        /// <summary>
        /// Runs Reload Value.
        /// </summary>
        private static ushort ReloadValue(ushort latch)
        {
            return latch == 0 ? (ushort)0xFFFF : latch;
        }

        /// <summary>
        /// Updates the CIA interrupt mask according to the set/clear control bit.
        /// </summary>
        private void WriteIcr(byte value)
        {
            byte mask = (byte)(value & 0x1F);
            byte oldIcr = BuildIcrValue();
            if ((value & InterruptMaster) != 0)
            {
                _interruptMask = (byte)(_interruptMask | mask);
            }
            else
            {
                _interruptMask = (byte)(_interruptMask & ~mask);
            }

            if (BuildIcrValue() != oldIcr)
            {
                RaiseInterruptChanged();
            }
        }

        /// <summary>
        /// Runs Tod Alarm Write Selected.
        /// </summary>
        private bool TodAlarmWriteSelected()
        {
            return (_registers[RegisterCrb] & 0x80) != 0;
        }

        /// <summary>
        /// Writes one byte of the CIA time-of-day alarm register.
        /// </summary>
        private void WriteTodAlarmByte(int index, byte value)
        {
            if (index == RegisterTodLow)
            {
                _todAlarm = (_todAlarm & 0xFFFF00) | value;
                _todAlarmWriteMask = (byte)(_todAlarmWriteMask | 0x01);
            }
            else if (index == RegisterTodMid)
            {
                _todAlarm = (_todAlarm & 0xFF00FF) | (value << 8);
                _todAlarmWriteMask = (byte)(_todAlarmWriteMask | 0x02);
            }
            else if (index == RegisterTodHigh)
            {
                _todAlarm = (_todAlarm & 0x00FFFF) | (value << 16);
                _todAlarmWriteMask = (byte)(_todAlarmWriteMask | 0x04);
            }

            _todAlarm &= 0x00FFFFFF;
            if (index == RegisterTodLow || _todAlarmWriteMask == 0x07)
            {
                CheckTodAlarm(GetTodValue());
            }
        }

        /// <summary>
        /// Returns the Register Index.
        /// </summary>
        private static int GetRegisterIndex(uint address)
        {
            return (int)((address >> 8) & 0x0F);
        }
    }
}
