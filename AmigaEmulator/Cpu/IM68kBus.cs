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
namespace AmigaEmulator.Core.Cpu
{
    /// <summary>
    /// Defines the IM68k Bus contract.
    /// </summary>
    public interface IM68kBus
    {
        byte ReadByte(uint address);

        ushort ReadWord(uint address);

        uint ReadLong(uint address);

        void WriteByte(uint address, byte value);

        void WriteWord(uint address, ushort value);

        void WriteLong(uint address, uint value);
    }

    /// <summary>
    /// Extends the 68000 bus with side-effect-free reads used while planning suspendable CPU bus micro-operations.
    /// </summary>
    public interface IM68kPeekableBus : IM68kBus
    {
        byte PeekByte(uint address);

        ushort PeekWord(uint address);

        uint PeekLong(uint address);
    }

    /// <summary>
    /// Allows the CPU to ask whether a pending bus operation is contended by custom-chip DMA.
    /// </summary>
    public interface IM68kBusArbitration
    {
        bool RequiresChipBus(uint address, int size);
    }

    /// <summary>
    /// Allows a machine bus to provide reset vectors after board-level overlay and ROM decode handling.
    /// </summary>
    public interface IM68kResetVectorProvider
    {
        bool TryReadResetVectors(out uint initialStackPointer, out uint initialProgramCounter);
    }
}
