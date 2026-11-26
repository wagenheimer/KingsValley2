using System;
using System.IO;
using System.Security.Cryptography;

namespace KingsValley.Engine.Extraction;

/// <summary>Adapts exact mapper-write operands for the Konami/slot-2 dump to the SCC recipe's bus.
/// Graphics and source identities remain unchanged; no cartridge file is modified.</summary>
internal static class Msx2ExecutionImage
{
    public static byte[] For(byte[] source)
    {
        if (Convert.ToHexString(SHA1.HashData(source)) != "F25628E474035E6BF0440B274C5DF04F94C49FB8") return source;
        byte[] image = (byte[])source.Clone();
        int[] writes = {0x55,0x59,0x5D,0x6B,0x71,0x77,0x100,0x105,0x10B,0x193,0x199,0x19F,0x1C6,0x1D3,0x1DA,0x32F,0x333,0x337,0x343,0x348,0x34D,0x13B8,0x13BC,0x13C0,0x13C9,0x13CF,0x13D5,0x7F28};
        foreach (int offset in writes)
        {
            int address = image[offset + 1] | image[offset + 2] << 8;
            int target = address switch { 0x68FF => 0x7000, 0x70FF => 0x9000, 0x78FF => 0xB000, _ => throw new InvalidDataException("Unexpected mapper instruction.") };
            if (image[offset] != 0x32) throw new InvalidDataException("Unexpected mapper opcode.");
            image[offset + 1] = (byte)target; image[offset + 2] = (byte)(target >> 8);
        }
        return image;
    }
}
