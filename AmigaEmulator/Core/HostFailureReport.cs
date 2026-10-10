// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using System.Text;

namespace AmigaEmulator.Core
{
    /// <summary>Best-effort host exception diagnostics; never modifies guest media or saves.</summary>
    public static class HostFailureReport
    {
        public static string TryWrite(string directory, Exception error, Func<string> context)
        {
            try
            {
                string details;
                try { details = context(); }
                catch (Exception ex) { details = "Context unavailable: " + ex.GetType().Name; }
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "host-error-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine("UTC: " + DateTime.UtcNow.ToString("O"));
                writer.WriteLine("Build: " + typeof(HostFailureReport).Assembly.ManifestModule.ModuleVersionId);
                writer.WriteLine(error.ToString());
                writer.WriteLine(details);
                return path;
            }
            catch { return null; } // A reporting failure must not replace the original exception.
        }
    }
}
