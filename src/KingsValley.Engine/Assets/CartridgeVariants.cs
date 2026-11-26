using KingsValley.Core.Common;

namespace KingsValley.Engine.Assets;

/// <summary>Extraction identity, hardware and campaign metadata for independently stored cartridges.</summary>
public static class CartridgeVariants
{
    public const string Contest="msx2-contest";
    public static readonly string[] All={"msx1","msx2",Contest};
    public static ArtFlavor Flavor(string variant)=>variant switch
    {
        "msx1"=>ArtFlavor.Msx1,"msx2" or Contest=>ArtFlavor.Msx2,
        _=>throw new ArgumentException("Unknown cartridge variant.",nameof(variant))
    };
    public static string Campaign(string variant)=>variant==Contest?Campaigns.Contest:Campaigns.Original;
    public static int StageCount(string variant){_ = Flavor(variant);return Campaigns.StageCount(Campaign(variant));}
    public static string Original(ArtFlavor flavor)=>flavor==ArtFlavor.Msx1?"msx1":"msx2";
    public static string ContentFolder(string variant)=>variant switch
    {"msx1"=>"RomMsx1","msx2"=>"Rom",Contest=>"RomContest",_=>throw new ArgumentException("Unknown cartridge variant.")};
}
