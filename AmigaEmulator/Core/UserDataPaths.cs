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
using System.IO;

namespace AmigaEmulator.Core
{
    /// <summary>
    /// Builds per-user application data, ROM, save-state, and media directories.
    /// </summary>
    public static class UserDataPaths
    {
        /// <summary>
        /// Returns the Base Directory.
        /// </summary>
        public static string GetBaseDirectory()
        {
            string isolatedDirectory = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(isolatedDirectory)) return Path.GetFullPath(isolatedDirectory);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(appData, "AmigaEmulator");
        }

        /// <summary>
        /// Returns the ROM Directory.
        /// </summary>
        public static string GetRomDirectory()
        {
            return Path.Combine(GetBaseDirectory(), "roms");
        }

        /// <summary>
        /// Returns the Save Directory.
        /// </summary>
        public static string GetSaveDirectory()
        {
            return Path.Combine(GetBaseDirectory(), "saves");
        }

        /// <summary>
        /// Returns the Settings Path.
        /// </summary>
        public static string GetSettingsPath()
        {
            return Path.Combine(GetBaseDirectory(), "settings.json");
        }
    }
}
