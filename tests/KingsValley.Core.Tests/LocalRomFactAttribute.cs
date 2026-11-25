namespace KingsValley.Core.Tests;

/// <summary>Local cartridge prerequisites are visible skips instead of successful no-op tests.</summary>
public sealed class LocalRomFactAttribute : FactAttribute
{
    public LocalRomFactAttribute(string variant)
    {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms",variant=="msx1"?"kv2_msx1.rom":"kv2_msx2_scc.rom");
        if(!File.Exists(path)) Skip="The local "+variant.ToUpperInvariant()+" cartridge is unavailable; no ROM is distributed with the tests.";
    }
}
