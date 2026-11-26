using System;
using System.IO;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>Uses the verified cartridge's hardware mapping for native recipe execution.</summary>
internal static class CartridgeMachines
{
    public static MiniMsx Create(byte[] rom)
    {
        var profile = RomExtractionProfiles.FindForRom(rom)
            ?? throw new InvalidDataException("No verified cartridge hardware profile.");
        var mapper = RomMapperNames.Parse(profile.RomType);
        var machine = new MiniMsx(rom, mapper) { ExternalSccSlot = profile.ExternalSccSlot };
        if (mapper == RomMapper.Ascii8)
        {
            // These conversions call bank-1 code before programming their mapper.
            // Supply the four initial windows expected by their loader.
            machine.SetBank(0x4000, 0); machine.SetBank(0x6000, 1);
            machine.SetBank(0x8000, 2); machine.SetBank(0xA000, 3);
        }
        return machine;
    }
}
