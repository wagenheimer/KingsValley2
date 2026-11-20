namespace KingsValley.Core.Common;

/// <summary>
/// Global constants (pure C# — no graphical dependency).
///
/// ## Source of truth
///
/// The original **King's Valley II** (Konami, 1988). The numbers come from the ROM and from measurements in the emulator
/// (openMSX + C-BIOS) — see <c>docs/ROM_EXTRACTION.md</c> and <c>docs/GAME_DESIGN.md</c>:
/// - MSX2 screen **256x212**: play field of **256x192** (32x24 cells of 8 px) + a 20 px HUD strip.
/// - Logic runs at **~30 Hz**: the VBL ISR is held by <c>E206</c> and ~half of the frames are
///   skipped. **1 logic call = 1 port tick**, and every ROM counter counts 1 per call
///   (do not divide by 2) — walking = 2 px/tick.
/// - Jump: displacement table per tick measured in RAM (see <c>Gameplay.Physics</c>).
/// </summary>
public static class GameConstants
{
    /// <summary>Window title.</summary>
    public const string GameTitle = "King's Valley II";

    /// <summary>Studio (save folder).</summary>
    public const string CompanyName = "Green Sauce Games";

    /// <summary>Folder (inside AppData) used for saves/config.</summary>
    public const string CompanyFolder = "GreenSauceGames";

    // ---------------------------------------------------------------- Screen (MSX2)

    /// <summary>Side of a cell (VDP character), in px.</summary>
    public const int CellSize = 8;

    /// <summary>Columns of a room (256 / 8).</summary>
    public const int RoomColumns = 32;

    /// <summary>Rows of a room (192 / 8).</summary>
    public const int RoomRows = 24;

    /// <summary>Width of the play field, in px.</summary>
    public const int PlayfieldWidth = RoomColumns * CellSize;

    /// <summary>Height of the play field, in px.</summary>
    public const int PlayfieldHeight = RoomRows * CellSize;

    /// <summary>Height of the HUD strip (HISCORE / SCORE / STAGE / REST) on the MSX2.</summary>
    public const int HudHeight = 20;

    /// <summary>Width of the design space (the whole MSX2 screen).</summary>
    public const int DesignWidth = PlayfieldWidth;

    /// <summary>Height of the design space (192 of game + 20 of HUD = 212 MSX2 rows).</summary>
    public const int DesignHeight = PlayfieldHeight + HudHeight;

    /// <summary>Default window width (4x the MSX).</summary>
    public const int DefaultWindowWidth = DesignWidth * 4;

    /// <summary>Default window height (4x the MSX).</summary>
    public const int DefaultWindowHeight = DesignHeight * 4;

    // ---------------------------------------------------------------- Time

    /// <summary>Frames per second of the MSX (NTSC).</summary>
    public const int FramesPerSecond = 60;

    /// <summary>
    /// Video frames (60 Hz) per logic tick — only for the tick time (2/60 = 1/30 s).
    /// <b>Do not</b> use for counters read from the ROM: they count <b>1 per logic call</b> = 1 tick
    /// (the H.TIMI ISR is held at <c>E206</c> and the effective logic runs at ~30 Hz) — see <c>Physics</c>.
    /// </summary>
    public const int FramesPerTick = 2;

    // ---------------------------------------------------------------- Campanha

    /// <summary>Total stages (6 pyramids × 10).</summary>
    public const int TotalStages = 60;

    /// <summary>Stages per pyramid.</summary>
    public const int StagesPerPyramid = 10;

    /// <summary>Extra lives ("REST") at the start — the original shows <c>REST 00</c> on stage 1.</summary>
    public const int StartingRest = 0;

    /// <summary>Extra lives gained when clearing a stage (measured on the longplay: REST goes up by 1 per stage).</summary>
    public const int RestPerStage = 1;

    /// <summary>Largest displayable number of extra lives (2 digits in the HUD / password).</summary>
    public const int MaxRest = 99;

    /// <summary>
    /// Extra life by score (4C20 <c>add_score</c>): the original keeps in <c>E245</c> the threshold in
    /// units of 10,000 points — <c>play_init</c> (4C1A) sets <c>3</c> (30,000) and each extra life
    /// adds 3 in BCD (60,000, 90,000…), saturating at <c>0xFF</c> past 0x99.
    /// </summary>
    public const int ExtraLifeFirstThreshold = 3;

    /// <summary>4C20: each extra life raises the <c>E245</c> threshold by 3 (BCD) — 30,000 points.</summary>
    public const int ExtraLifeThresholdStep = 3;
}
