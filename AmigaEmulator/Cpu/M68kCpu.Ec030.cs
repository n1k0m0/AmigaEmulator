using System;

namespace AmigaEmulator.Core.Cpu
{
    public sealed partial class M68kCpu
    {
        private string ExecuteEc030AccessControl(ushort opcode)
        {
            ushort extension = FetchWord();
            int kind = extension & 0xFDFF;
            bool test = (extension & 0xFDE0) == 0x8000;
            bool move = kind == 0x0800 || kind == 0x0C00 || kind == 0x6000;
            if (!test && !move)
            { EnterException(11, 0, _currentInstructionAddress); return "LINE-F"; }
            if (TryEnterPrivilegeViolation(out string privilege)) return privilege;
            int mode = (opcode >> 3) & 7, register = opcode & 7;
            if (mode != 2 && mode != 5 && mode != 6 && (mode != 7 || register > 1))
            { EnterException(11, 0, _currentInstructionAddress); return "LINE-F"; }
            uint address = GetEffectiveAddress(mode, register, 4, false);
            if (test)
            {
                int fcField = extension & 31;
                int functionCode;
                if ((fcField & 24) == 16) functionCode = fcField & 7;
                else if ((fcField & 24) == 8) functionCode = (int)(_state.D[fcField & 7] & 7);
                else if (fcField == 0) functionCode = (int)_sourceFunctionCode;
                else if (fcField == 1) functionCode = (int)_destinationFunctionCode;
                else { EnterException(11, 0, _currentInstructionAddress); return "LINE-F"; }
                bool read = (extension & 0x200) != 0;
                _accessControlStatus = (ushort)(MatchesAccessControl(_accessControl0, address, functionCode, read) ||
                    MatchesAccessControl(_accessControl1, address, functionCode, read) ? 0x40 : 0);
                return read ? "PTESTR" : "PTESTW";
            }
            bool toMemory = (extension & 0x200) != 0;
            if (kind == 0x6000)
            {
                if (toMemory) WriteWord(address, _accessControlStatus);
                else _accessControlStatus = (ushort)(ReadWord(address) & 0x40);
            }
            else if (toMemory) WriteLong(address, kind == 0x0800 ? _accessControl0 : _accessControl1);
            else
            {
                uint value = ReadLong(address) & 0xFFFF8777u;
                if (kind == 0x0800) _accessControl0 = value; else _accessControl1 = value;
            }
            return "PMOVE ACU";
        }

        private static bool MatchesAccessControl(uint control, uint address, int functionCode, bool read)
        {
            if ((control & 0x8000) == 0) return false;
            uint addressMask = ~(control >> 16) & 255;
            if ((((address >> 24) ^ (control >> 24)) & addressMask) != 0) return false;
            int functionMask = (int)(~control & 7);
            if (((functionCode ^ (int)(control >> 4)) & functionMask) != 0) return false;
            return (control & 0x100) != 0 || read == ((control & 0x200) != 0);
        }
    }
}
