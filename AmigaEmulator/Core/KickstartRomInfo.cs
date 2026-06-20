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
namespace AmigaEmulator.Core
{
    /// <summary>
    /// Provides Kickstart ROM Info functionality.
    /// </summary>
    public sealed class KickstartRomInfo
    {
        /// <summary>
        /// Gets or sets Path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets Exists.
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// Gets or sets Is Usable.
        /// </summary>
        public bool IsUsable { get; set; }

        /// <summary>
        /// Gets or sets Kind.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Gets or sets Size.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// Gets or sets Sha 1.
        /// </summary>
        public string Sha1 { get; set; }

        /// <summary>
        /// Gets or sets Initial Stack Pointer.
        /// </summary>
        public uint InitialStackPointer { get; set; }

        /// <summary>
        /// Gets or sets Initial Program Counter.
        /// </summary>
        public uint InitialProgramCounter { get; set; }

        /// <summary>
        /// Gets or sets Error.
        /// </summary>
        public string Error { get; set; }
    }
}
