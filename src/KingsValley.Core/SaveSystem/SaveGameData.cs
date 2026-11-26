namespace KingsValley.Core.SaveSystem;

/// <summary>
/// Persisted game data (settings + progress). Versioned: <see cref="Version"/> allows
/// migrating old saves (see <c>SaveMigrator</c> when there is more than one version).
/// </summary>
public sealed class SaveGameData : CampaignProgress
{
    /// <summary>Save format version (for migration).</summary>
    public int Version { get; set; } = 1;

    // ── Settings ───────────────────────────────────────────────────────────────────

    /// <summary>Saved language code (e.g.: <c>"en"</c>, <c>"pt"</c>). Empty = use the OS one.</summary>
    public string GameLanguage { get; set; } = string.Empty;

    /// <summary>Uses the custom cursor (game art) instead of the system arrow?</summary>
    public bool CustomCursor { get; set; } = true;

    /// <summary>Window in full screen?</summary>
    public bool FullScreen { get; set; }

    /// <summary>
    /// Graphics variant: <c>"msx2"</c> (default, SCREEN 5) or <c>"msx1"</c> (SCREEN 2, RC-760).
    /// Switchable in the options menu and through <c>KINGSVALLEY_GFX</c>.
    /// </summary>
    public string GfxRom { get; set; } = "msx2";

    /// <summary>
    /// In MSX1 mode: items, soulstones, coffins and doors have a black 16x16 backdrop
    /// (authentic to MSX1 SCREEN 2 attribute limits). Default true for pure MSX1 authenticity.
    /// Can be toggled to false for modern transparent blending.
    /// </summary>
    public bool Msx1OpaqueBackdrop { get; set; } = true;

    /// <summary>
    /// Title art language: <c>"jp"</c> (cartridge original) or <c>"en"</c> (KING'S VALLEY II).
    /// The cartridge carries both screens; the system menu switches and saves here.
    /// </summary>
    public string TitleLanguage { get; set; } = "jp";

    /// <summary>Audio profile: <c>"original"</c>, <c>"stereo"</c> or <c>"remaster"</c>.</summary>
    public string AudioProfile { get; set; } = "original";

    /// <summary>Music on?</summary>
    public bool MusicEnabled { get; set; } = true;

    /// <summary>Are sound effects enabled?</summary>
    public bool SfxEnabled { get; set; } = true;

    // ── Progresso ───────────────────────────────────────────────────────────────────

    public string OriginalGfxRom {get;set;}="msx2";
    public CampaignProgress ContestProgress {get;set;}=new();
    public CampaignProgress ProgressFor(string campaign)=>campaign switch
    {
        Common.Campaigns.Original=>this,Common.Campaigns.Contest=>ContestProgress,
        _=>throw new ArgumentException("Unknown campaign.",nameof(campaign))
    };
}

public class CampaignProgress
{
    /// <summary>Highest room reached (1-based).</summary>
    public int HighestLevel { get; set; } = 1;

    /// <summary>Rooms already completed.</summary>
    public List<int> CompletedLevels { get; set; } = new();

    /// <summary>Total accumulated score.</summary>
    public int TotalScore { get; set; }

    /// <summary>Record (HISCORE of the HUD).</summary>
    public int HiScore { get; set; }
}
