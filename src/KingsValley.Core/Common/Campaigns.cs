namespace KingsValley.Core.Common;

/// <summary>Campaign identity is independent of the MSX graphics pipeline.</summary>
public static class Campaigns
{
    public const string Original="original";
    public const string Contest="contest";
    public static int StageCount(string campaign)=>campaign switch
    {
        Original=>60,Contest=>20,_=>throw new ArgumentException("Unknown campaign.",nameof(campaign))
    };
}
