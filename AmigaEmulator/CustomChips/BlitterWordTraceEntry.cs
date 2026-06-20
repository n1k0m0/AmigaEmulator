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
namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Provides Blitter Word Trace Entry functionality.
    /// </summary>
    public sealed class BlitterWordTraceEntry
    {
        /// <summary>
        /// Gets or sets Line Mode.
        /// </summary>
        public bool LineMode { get; set; }

        /// <summary>
        /// Gets or sets Line.
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Gets or sets Word.
        /// </summary>
        public int Word { get; set; }

        /// <summary>
        /// Gets or sets A Address.
        /// </summary>
        public uint AAddress { get; set; }

        /// <summary>
        /// Gets or sets B Address.
        /// </summary>
        public uint BAddress { get; set; }

        /// <summary>
        /// Gets or sets C Address.
        /// </summary>
        public uint CAddress { get; set; }

        /// <summary>
        /// Gets or sets D Address.
        /// </summary>
        public uint DAddress { get; set; }

        /// <summary>
        /// Gets or sets Raw A.
        /// </summary>
        public ushort RawA { get; set; }

        /// <summary>
        /// Gets or sets Raw B.
        /// </summary>
        public ushort RawB { get; set; }

        /// <summary>
        /// Gets or sets Raw C.
        /// </summary>
        public ushort RawC { get; set; }

        /// <summary>
        /// Gets or sets Shifted A.
        /// </summary>
        public ushort ShiftedA { get; set; }

        /// <summary>
        /// Gets or sets Shifted B.
        /// </summary>
        public ushort ShiftedB { get; set; }

        /// <summary>
        /// Gets or sets Mask.
        /// </summary>
        public ushort Mask { get; set; }

        /// <summary>
        /// Gets or sets Result Before Fill.
        /// </summary>
        public ushort ResultBeforeFill { get; set; }

        /// <summary>
        /// Gets or sets Result.
        /// </summary>
        public ushort Result { get; set; }

        /// <summary>
        /// Gets or sets Minterm.
        /// </summary>
        public byte Minterm { get; set; }

        /// <summary>
        /// Gets or sets Use A.
        /// </summary>
        public bool UseA { get; set; }

        /// <summary>
        /// Gets or sets Use B.
        /// </summary>
        public bool UseB { get; set; }

        /// <summary>
        /// Gets or sets Use C.
        /// </summary>
        public bool UseC { get; set; }

        /// <summary>
        /// Gets or sets Use D.
        /// </summary>
        public bool UseD { get; set; }

        /// <summary>
        /// Gets or sets Fill Inclusive.
        /// </summary>
        public bool FillInclusive { get; set; }

        /// <summary>
        /// Gets or sets Fill Exclusive.
        /// </summary>
        public bool FillExclusive { get; set; }

        /// <summary>
        /// Gets or sets Fill Carry In.
        /// </summary>
        public bool FillCarryIn { get; set; }

        /// <summary>
        /// Gets or sets Fill Carry Out.
        /// </summary>
        public bool FillCarryOut { get; set; }

        /// <summary>
        /// Gets or sets Drawn.
        /// </summary>
        public bool Drawn { get; set; }

        /// <summary>
        /// Gets or sets Line X.
        /// </summary>
        public int LineX { get; set; }

        /// <summary>
        /// Gets or sets Line Y.
        /// </summary>
        public int LineY { get; set; }

        /// <summary>
        /// Gets or sets Line Error.
        /// </summary>
        public int LineError { get; set; }
    }
}
