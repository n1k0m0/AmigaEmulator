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
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Finds, imports, identifies, and loads Kickstart ROM files from user data paths.
    /// </summary>
    public static class KickstartRomResolver
    {
        /// <summary>
        /// Stores Kickstart 13 Names.
        /// </summary>
        private static readonly string[] Kickstart13Names =
        {
            "KICK13.ROM",
            "kick13.rom",
            "kickstart13.rom",
            "kickstart-1.3.rom",
            "kick.rom"
        };

        /// <summary>
        /// Finds Default Kickstart ROM in the available emulator data.
        /// </summary>
        public static string FindDefaultKickstartRom()
        {
            string environmentPath = Environment.GetEnvironmentVariable("AMIGA_KICKSTART");
            if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
            {
                return environmentPath;
            }

            string path = FindFirstExisting(Kickstart13Names);
            if (path != null)
            {
                return path;
            }

            return Path.Combine(UserDataPaths.GetRomDirectory(), "KICK13.ROM");
        }

        /// <summary>
        /// Runs Ensure Kickstart 13 In User Rom Directory.
        /// </summary>
        public static string EnsureKickstart13InUserRomDirectory()
        {
            Directory.CreateDirectory(UserDataPaths.GetRomDirectory());

            string userPath = FindFirstExistingInDirectory(Kickstart13Names, UserDataPaths.GetRomDirectory());
            if (userPath != null)
            {
                return userPath;
            }

            string sourcePath = FindFirstExisting(Kickstart13Names, null, skipUserRomDirectory: true);
            if (sourcePath == null)
            {
                return Path.Combine(UserDataPaths.GetRomDirectory(), "KICK13.ROM");
            }

            string destinationPath = Path.Combine(UserDataPaths.GetRomDirectory(), "KICK13.ROM");
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        /// <summary>
        /// Attempts to Load Default Kickstart and reports whether it succeeded.
        /// </summary>
        public static bool TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo info)
        {
            string path = EnsureKickstart13InUserRomDirectory();
            return TryLoadKickstart(path, out romBytes, out info);
        }

        /// <summary>
        /// Attempts to Load Kickstart and reports whether it succeeded.
        /// </summary>
        public static bool TryLoadKickstart(string path, out byte[] romBytes, out KickstartRomInfo info)
        {
            info = Probe(path);
            if (!info.IsUsable)
            {
                romBytes = null;
                return false;
            }

            romBytes = File.ReadAllBytes(path);
            return true;
        }

        /// <summary>
        /// Finds Available Kickstart Roms in the available emulator data.
        /// </summary>
        public static IReadOnlyList<KickstartRomInfo> FindAvailableKickstartRoms()
        {
            return FindAvailableKickstartRoms(GetSearchDirectories());
        }

        public static IReadOnlyList<KickstartRomInfo> FindAvailableKickstartRoms(IEnumerable<string> directories)
        {
            var roms = new List<KickstartRomInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
            {
                AddRomCandidates(roms, seen, directory, "*.rom");
                AddRomCandidates(roms, seen, directory, "*.bin");
            }

            roms.Sort(delegate(KickstartRomInfo left, KickstartRomInfo right)
            {
                int usable = right.IsUsable.CompareTo(left.IsUsable);
                if (usable != 0)
                {
                    return usable;
                }

                return string.Compare(Path.GetFileName(left.Path), Path.GetFileName(right.Path), StringComparison.OrdinalIgnoreCase);
            });
            return roms;
        }

        /// <summary>
        /// Returns the Search Directories.
        /// </summary>
        public static IReadOnlyList<string> GetSearchDirectories()
        {
            return GetSearchDirectories(null, false);
        }

        /// <summary>
        /// Finds First Existing in the available emulator data.
        /// </summary>
        public static string FindFirstExisting(IEnumerable<string> fileNames, string explicitBasePath = null, bool skipUserRomDirectory = false)
        {
            foreach (string directory in GetSearchDirectories(explicitBasePath, skipUserRomDirectory))
            {
                foreach (string fileName in fileNames)
                {
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Runs Probe.
        /// </summary>
        public static KickstartRomInfo Probe(string path)
        {
            var info = new KickstartRomInfo { Path = path ?? string.Empty };
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    info.Exists = false;
                    info.IsUsable = false;
                    info.Error = "ROM file not found.";
                    return info;
                }

                long length = new FileInfo(path).Length;
                info.Exists = true;
                if (length != 256 * 1024 && length != 512 * 1024 && length != 1024 * 1024)
                {
                    info.Size = (int)Math.Min(length, int.MaxValue);
                    info.Error = "Kickstart ROM must be 256 KB, 512 KB, or 1 MB.";
                    return info;
                }
                byte[] bytes = File.ReadAllBytes(path);
                var rom = new KickstartRom(bytes);
                info.Exists = true;
                info.IsUsable = rom.IsPresent;
                info.Kind = rom.Kind.ToString();
                info.Size = rom.Size;
                info.Sha1 = rom.Sha1;
                ReadKickstartResetEntry(rom, out uint initialStackPointer, out uint initialProgramCounter);
                info.InitialStackPointer = initialStackPointer;
                info.InitialProgramCounter = initialProgramCounter;
                info.IsUsable = rom.IsPresent && IsLikelyKickstartVector(info.InitialStackPointer, info.InitialProgramCounter);
                if (!info.IsUsable)
                {
                    info.Error = "ROM has an unexpected reset vector.";
                }
                return info;
            }
            catch (Exception ex)
            {
                info.Exists = File.Exists(path ?? string.Empty);
                info.IsUsable = false;
                info.Error = ex.Message;
                return info;
            }
        }

        /// <summary>
        /// Builds the Status Report.
        /// </summary>
        public static string BuildStatusReport(string path, KickstartRomInfo info)
        {
            info = info ?? Probe(path);
            return "AMIGA KICKSTART ROM CHECK" + Environment.NewLine +
                "Path=" + info.Path + Environment.NewLine +
                "Exists=" + info.Exists + Environment.NewLine +
                "Usable=" + info.IsUsable + Environment.NewLine +
                "Kind=" + (info.Kind ?? "Missing") + Environment.NewLine +
                "Size=" + info.Size + Environment.NewLine +
                "SHA1=" + (info.Sha1 ?? string.Empty) + Environment.NewLine +
                "InitialSP=$" + info.InitialStackPointer.ToString("X8") + Environment.NewLine +
                "InitialPC=$" + info.InitialProgramCounter.ToString("X8") + Environment.NewLine +
                "Error=" + (info.Error ?? string.Empty) + Environment.NewLine +
                "Search paths:" + Environment.NewLine +
                BuildSearchPathReport();
        }

        /// <summary>
        /// Returns the Search Directories.
        /// </summary>
        private static IReadOnlyList<string> GetSearchDirectories(string explicitBasePath, bool skipUserRomDirectory)
        {
            var directories = new List<string>();
            AddDirectory(directories, explicitBasePath);
            AddDirectory(directories, AmigaEmulatorSettingsStore.Load().RomSearchDirectory);
            if (!skipUserRomDirectory)
            {
                AddDirectory(directories, UserDataPaths.GetRomDirectory(), false);
            }

            AddDirectory(directories, AppDomain.CurrentDomain.BaseDirectory);
            AddDirectory(directories, Directory.GetCurrentDirectory());
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "roms"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "ROMs"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "..", "roms"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "..", "ROMs"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "roms"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "ROMs"));
            return directories;
        }

        /// <summary>
        /// Runs Add Directory.
        /// </summary>
        private static void AddDirectory(List<string> directories, string directory, bool requireExistingDirectory = true)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(directory);
            }
            catch
            {
                return;
            }

            if (requireExistingDirectory && !Directory.Exists(fullPath))
            {
                return;
            }

            for (int index = 0; index < directories.Count; index++)
            {
                if (string.Equals(directories[index], fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            directories.Add(fullPath);
        }

        /// <summary>
        /// Builds the Search Path Report.
        /// </summary>
        private static string BuildSearchPathReport()
        {
            var builder = new System.Text.StringBuilder();
            foreach (string directory in GetSearchDirectories())
            {
                builder.Append("  ");
                builder.AppendLine(directory);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Runs Add Rom Candidates.
        /// </summary>
        private static void AddRomCandidates(List<KickstartRomInfo> roms, HashSet<string> seen, string directory, string pattern)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                foreach (string path in Directory.GetFiles(directory, pattern))
                {
                    string fullPath = Path.GetFullPath(path);
                    if (!seen.Add(fullPath))
                    {
                        continue;
                    }

                    roms.Add(Probe(fullPath));
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Finds First Existing In Directory in the available emulator data.
        /// </summary>
        private static string FindFirstExistingInDirectory(IEnumerable<string> fileNames, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            foreach (string fileName in fileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the initial stack pointer and program counter from the Kickstart reset vectors.
        /// </summary>
        private static void ReadKickstartResetEntry(KickstartRom rom, out uint initialStackPointer, out uint initialProgramCounter)
        {
            initialStackPointer = rom.ReadLong(0);
            initialProgramCounter = rom.ReadLong(4);

            ushort wordAtTwo = (ushort)((rom.Read(2) << 8) | rom.Read(3));
            uint jumpTarget = rom.ReadLong(4);
            if (wordAtTwo == 0x4EF9 && jumpTarget >= MemoryMap.KickstartStart && jumpTarget <= MemoryMap.KickstartEnd)
            {
                initialStackPointer = 0x00080000;
                initialProgramCounter = jumpTarget;
            }
        }

        /// <summary>
        /// Returns whether Likely Kickstart Vector is true.
        /// </summary>
        private static bool IsLikelyKickstartVector(uint initialStackPointer, uint initialProgramCounter)
        {
            bool stackLooksLikeRam = initialStackPointer <= 0x00200000 || (initialStackPointer >= 0x00C00000 && initialStackPointer <= 0x00C80000);
            bool pcLooksLikeRom = initialProgramCounter >= MemoryMap.KickstartStart && initialProgramCounter <= MemoryMap.KickstartEnd;
            return stackLooksLikeRam && pcLooksLikeRom;
        }
    }
}
