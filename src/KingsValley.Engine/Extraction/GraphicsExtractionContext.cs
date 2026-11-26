#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using KingsValley.Engine.Assets;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>Immutable cartridge identity and destination. A job never reads the active runtime flavor.</summary>
public sealed record GraphicsExtractionContext(byte[] Rom, ArtFlavor Flavor, string RomSha1, string Root, int StageLimit,string? CartridgeVariant=null)
{
    public string Variant => CartridgeVariant ?? CartridgeVariants.Original(Flavor);
    public static GraphicsExtractionContext Create(byte[] rom, string variant, string collectionRoot, int? stageLimit = null)
    {
        ArgumentNullException.ThrowIfNull(rom);
        int count=stageLimit ?? CartridgeVariants.StageCount(variant);
        if (count<1 || count>CartridgeVariants.StageCount(variant)) throw new ArgumentOutOfRangeException(nameof(stageLimit));
        var profile = RomExtractionProfiles.FindForRom(rom);
        if (profile?.GameId != "kings-valley-2" || profile.Variant != variant) throw new InvalidDataException("Graphics cartridge identity does not match the requested variant.");
        byte[] snapshot = (byte[])rom.Clone();
        return new(snapshot, CartridgeVariants.Flavor(variant),
            Convert.ToHexString(SHA1.HashData(snapshot)).ToLowerInvariant(), GraphicsLayout.GetRoot(collectionRoot, "kings-valley-2", variant), count,variant);
    }
}
