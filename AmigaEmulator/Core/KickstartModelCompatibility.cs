// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;

namespace AmigaEmulator.Core
{
    /// <summary>Conservative automatic selection: AGA ROM filenames alone do not identify the motherboard.
    /// Unknown/custom ROMs remain available through explicit selection.</summary>
    public static class KickstartModelCompatibility
    {
        public static bool IsKnownCompatible(AmigaMachineModel model, string sha1)
        {
            string[] hashes = model switch
            {
                AmigaMachineModel.A1200 => new[] { "70033828182FFFC7ED106E5373A8B89DDA76FAA5", "E21545723FE8374E91342617604F1B3D703094F1" },
                AmigaMachineModel.A3000 => new[] { "F8E210D72B4C4853E0C9B85D223BA20E3D1B36EE", "D82EBB59AFC53540DDF2D7187ECF239B7EA91590" },
                AmigaMachineModel.A4000Ec030 => new[] { "F0B4E9E29E12218C2D5BD7020E4E785297D91FD7", "81C631DD096BBB31D2AF90299C76B774DB74076C", "5FE04842D04A489720F0F4BB0E46948199406F49", "C3C481160866E60D085E436A24DB3617FF60B5F9" },
                _ => Array.Empty<string>()
            };
            foreach (string hash in hashes) if (string.Equals(hash, sha1, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
