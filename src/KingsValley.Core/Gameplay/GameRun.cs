using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// A run (what the MSX2 HUD shows: HISCORE / SCORE / STAGE / REST). Immutable: each
/// transition returns a new run.
/// </summary>
public sealed record GameRun(int Stage, int Rest, int Score, int HiScore, bool Invincible = false, bool ContinueEnabled = false,string CampaignId=Campaigns.Original)
{
    /// <summary>Native Contest SCREEN SELECT choice. Original campaigns ignore this value.</summary>
    public int ContestPyramid {get;init;}=1;
    /// <summary>New run on stage 1 (or on the stage of a password).</summary>
    public static GameRun New(int hiScore, int stage = 1, int rest = GameConstants.StartingRest, bool invincible = false, bool continueEnabled = false,string campaignId=Campaigns.Original) =>
        new(Math.Clamp(stage, 1, Campaigns.StageCount(campaignId)), Math.Clamp(rest, 0, GameConstants.MaxRest), 0, hiScore, invincible, continueEnabled,campaignId);

    /// <summary>
    /// Cheat <c>TRYAGAIN</c> / <c>FESTIVAL</c> (<c>pwd_cheat</c> 0x6B24): the ROM calls <c>play_clear</c>,
    /// which copies <c>play_init</c> (<c>01 01 01 00 00 03</c>) into <c>E240</c> — <b>stage 1 with 1 life</b> —
    /// and turns on the continue (<c>E217 = 1</c>) or the invincibility (<c>E255 = 1</c>).
    /// </summary>
    public static GameRun Cheat(int hiScore, bool invincible = false, bool continueEnabled = false,string campaignId=Campaigns.Original) =>
        new(1, 1, 0, hiScore, invincible, continueEnabled,campaignId);

    /// <summary>The run is over (died with no extra lives)?</summary>
    public bool IsGameOver => Rest < 0;

    /// <summary>Was it the last stage?</summary>
    public bool IsLastStage => Stage >= Campaigns.StageCount(CampaignId);

    /// <summary>
    /// Password that resumes this run (MSX2 format, see <see cref="StagePassword"/>). The original
    /// does not represent REST 0 (<c>6AF1</c> rejects the zeroed byte), so the minimum used is 1.
    /// </summary>
    public string Password =>
        StagePassword.Encode(Stage, Math.Clamp(Rest, StagePassword.MinRest, GameConstants.MaxRest));

    /// <summary>
    /// Password drawn on the stage-end screen (<c>697C</c>). It comes out <b>after</b> the
    /// <c>INC (0xE240)</c> of <c>732C</c> — the "SPECIAL BONUS" added on top of the REST shown.
    /// Proof: the longplay shows "REST 01" and the password <c>EAEAODGD</c>, which gives back REST 2.
    /// </summary>
    public string ClearScreenPassword =>
        StagePassword.Encode(Stage, Math.Clamp(Rest + 1, StagePassword.MinRest, GameConstants.MaxRest));

    /// <summary>Adds points (and updates the record).</summary>
    public GameRun AddScore(int points)
    {
        int score = Score + points;
        return this with { Score = score, HiScore = Math.Max(HiScore, score) };
    }

    /// <summary>Stage cleared: next stage and +1 extra life.</summary>
    public GameRun StageCleared() =>
        this with
        {
            Stage = Math.Min(Stage + 1, Campaigns.StageCount(CampaignId)),
            Rest = Math.Min(Rest + GameConstants.RestPerStage, GameConstants.MaxRest),
        };

    /// <summary>Extra life by score (4C20 <c>add_score</c>: <c>E240 += 1</c>, cap 0x99).</summary>
    public GameRun GainLife() =>
        this with { Rest = Math.Min(Rest + 1, GameConstants.MaxRest) };

    /// <summary>Died: loses an extra life (below zero = game over).</summary>
    public GameRun Died() => this with { Rest = Rest - 1 };
}
