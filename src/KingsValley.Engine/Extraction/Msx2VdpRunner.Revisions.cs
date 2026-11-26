using System;
using System.Security.Cryptography;
using Konamiman.Z80dotNet;

namespace KingsValley.Engine.Extraction;

public sealed partial class Msx2VdpRunner
{
    /// <summary>Revision-specific entry adapter; the original fast Boot implementation is unchanged.</summary>
    public void BootVerified()
    {
        string sha1 = Convert.ToHexString(SHA1.HashData(_rom)).ToLowerInvariant();
        if (sha1 != "89d96d4b92d9a1c220cae0240cf0c00f310d14aa") { Boot(); return; }
        var registers = (Z80Registers)_cpu.Registers;
        registers.SP = unchecked((short)0xF380);
        // FRS moved the DI/IM1 entry to 40BF and waits at HALT 4109 before video init.
        // Execute its initialization up to that wait, then call its native video initializer.
        registers.PC = 0x40BF;
        RunUntil(0x4109);
        Invoke(0x53B6);
    }
}
