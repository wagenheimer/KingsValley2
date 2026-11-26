namespace KingsValley.Core.Tests;

/// <summary>Local cartridge prerequisites are visible skips instead of successful no-op tests.</summary>
public sealed class LocalRomFactAttribute : FactAttribute
{
    public LocalRomFactAttribute(string variant)
    {
        if(variant=="msx1-frs")
        {
            string? frs=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_FRS_ROM");
            if(string.IsNullOrWhiteSpace(frs) || !File.Exists(frs))
                Skip="Set KINGSVALLEY_TEST_FRS_ROM to the locally owned verified MSX1 FRS cartridge.";
            return;
        }
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms",variant=="msx1"?"kv2_msx1.rom":"kv2_msx2_scc.rom");
        if(!File.Exists(path)) Skip="The local "+variant.ToUpperInvariant()+" cartridge is unavailable; no ROM is distributed with the tests.";
    }
}
