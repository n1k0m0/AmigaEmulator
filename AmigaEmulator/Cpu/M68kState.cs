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
    /// Lists the supported 68000 Execution State values.
    /// </summary>
    public enum M68kExecutionState
    {
        /// <summary>
        /// Represents Reset.
        /// </summary>
        Reset,
        /// <summary>
        /// Represents Fetch Opcode.
        /// </summary>
        FetchOpcode,
        /// <summary>
        /// Represents Execute Instruction.
        /// </summary>
        ExecuteInstruction,
        /// <summary>
        /// Represents Stopped.
        /// </summary>
        Stopped,
        /// <summary>
        /// Represents Illegal Instruction.
        /// </summary>
        IllegalInstruction
    }

    public sealed class M68kState
    {
        /// <summary>
        /// Stores D.
        /// </summary>
        public readonly uint[] D = new uint[8];
        /// <summary>
        /// Stores A.
        /// </summary>
        public readonly uint[] A = new uint[8];

        /// <summary>
        /// Stores PC.
        /// </summary>
        public uint PC;
        /// <summary>
        /// Stores SR.
        /// </summary>
        public ushort SR;
        /// <summary>
        /// Stores USP.
        /// </summary>
        public uint USP;
        /// <summary>
        /// Stores SSP.
        /// </summary>
        public uint SSP;
        /// <summary>
        /// Stores Current Opcode.
        /// </summary>
        public ushort CurrentOpcode;
        /// <summary>
        /// Stores Cycles.
        /// </summary>
        public long Cycles;
        /// <summary>
        /// Stores Execution State.
        /// </summary>
        public M68kExecutionState ExecutionState;

        public M68kState Clone()
        {
            var clone = new M68kState();
            for (int index = 0; index < 8; index++)
            {
                clone.D[index] = D[index];
                clone.A[index] = A[index];
            }

            clone.PC = PC;
            clone.SR = SR;
            clone.USP = USP;
            clone.SSP = SSP;
            clone.CurrentOpcode = CurrentOpcode;
            clone.Cycles = Cycles;
            clone.ExecutionState = ExecutionState;
            return clone;
        }
    }
}
