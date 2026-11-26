namespace KingsValley.Core.Gameplay;

/// <summary>Original campaign transitions measured in native logic calls on MSX1 RC-760/FRS.</summary>
public static class NativeDeathTiming
{
    /// <summary>The 100-call spin/final pose animation is followed by mode 6's decision.
    /// With spare lives, one further call initializes mode 4 and decrements REST.</summary>
    public static int DecisionTicks(int spareLives) => spareLives > 0 ? 2 : 1;

    /// <summary>The retry banner waits 0x78 calls; one further call resumes mode 5.
    /// Including animation/decision, native gameplay resumes on call 223.</summary>
    public const int RetryBannerTicks = 0x78 + 1;
}
