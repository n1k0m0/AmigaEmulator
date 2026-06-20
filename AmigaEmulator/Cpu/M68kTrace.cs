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
    using System.Globalization;

    /// <summary>
    /// Lists the supported 68000 Trace Access Type values.
    /// </summary>
    public enum M68kTraceAccessType
    {
        /// <summary>
        /// Represents None.
        /// </summary>
        None,
        /// <summary>
        /// Represents Opcode Fetch.
        /// </summary>
        OpcodeFetch,
        /// <summary>
        /// Represents Read.
        /// </summary>
        Read,
        /// <summary>
        /// Represents Write.
        /// </summary>
        Write,
        /// <summary>
        /// Represents Internal.
        /// </summary>
        Internal
    }

    /// <summary>
    /// Provides 68000 Trace Entry functionality.
    /// </summary>
    public sealed class M68kTraceEntry
    {
        /// <summary>
        /// Gets or sets CPU Cycle.
        /// </summary>
        public long CpuCycle { get; set; }

        /// <summary>
        /// Gets or sets PC Before.
        /// </summary>
        public uint PcBefore { get; set; }

        /// <summary>
        /// Gets or sets PC After.
        /// </summary>
        public uint PcAfter { get; set; }

        /// <summary>
        /// Gets or sets Opcode.
        /// </summary>
        public ushort Opcode { get; set; }

        /// <summary>
        /// Gets or sets Instruction.
        /// </summary>
        public string Instruction { get; set; }

        /// <summary>
        /// Gets or sets SR Before.
        /// </summary>
        public ushort SrBefore { get; set; }

        /// <summary>
        /// Gets or sets SR After.
        /// </summary>
        public ushort SrAfter { get; set; }

        /// <summary>
        /// Gets or sets Access Type.
        /// </summary>
        public M68kTraceAccessType AccessType { get; set; }

        /// <summary>
        /// Gets or sets Address.
        /// </summary>
        public uint Address { get; set; }

        /// <summary>
        /// Gets or sets Value.
        /// </summary>
        public uint Value { get; set; }

        /// <summary>
        /// Runs To String.
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0,8} PC {1:X6}->{2:X6} SR {3:X4}->{4:X4} OP {5:X4} {6} {7} @{8:X6}={9:X8}",
                CpuCycle,
                PcBefore,
                PcAfter,
                SrBefore,
                SrAfter,
                Opcode,
                Instruction ?? string.Empty,
                AccessType,
                Address,
                Value);
        }
    }
}
