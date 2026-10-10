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
    public sealed class BlitterWordTraceEntry
    {
        public bool LineMode { get; set; }

        public int Line { get; set; }

        public int Word { get; set; }

        public uint AAddress { get; set; }

        public uint BAddress { get; set; }

        public uint CAddress { get; set; }

        public uint DAddress { get; set; }

        public ushort RawA { get; set; }

        public ushort RawB { get; set; }

        public ushort RawC { get; set; }

        public ushort ShiftedA { get; set; }

        public ushort ShiftedB { get; set; }

        public ushort Mask { get; set; }

        public ushort ResultBeforeFill { get; set; }

        public ushort Result { get; set; }

        public byte Minterm { get; set; }

        public bool UseA { get; set; }

        public bool UseB { get; set; }

        public bool UseC { get; set; }

        public bool UseD { get; set; }

        public bool FillInclusive { get; set; }

        public bool FillExclusive { get; set; }

        public bool FillCarryIn { get; set; }

        public bool FillCarryOut { get; set; }

        public bool Drawn { get; set; }

        public int LineX { get; set; }

        public int LineY { get; set; }

        public int LineError { get; set; }
    }
}
