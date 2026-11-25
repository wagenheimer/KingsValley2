#nullable enable
namespace KingsValley.Engine.Graphics;

/// <summary>Presentation choices for the verified MSX1 intro sprite-strip inventory.</summary>
public static class Msx1IntroPresentation
{
    // Native 531F/8CE4 door captures have seven transparent rows above their visible actor.
    // The intro records a trimmed top-left origin, so remove that padding when reusing them.
    public const int DoorTopPadding = 7;
    // SAT 74A1 anchors both arrival poses at EDCC. The second recorded bitmap trims
    // one empty left column; the level-door bitmap does not have that empty column.
    public static int DoorX(int recordedPose,int trimmedX) => trimmedX-(recordedPose==4?1:0);

    // The verified profile's first three deduplicated poses are walking. Its final two are
    // front-facing arrival poses. The requested presentation uses the level's back-facing
    // pair instead; keep the raw timeline/strip unchanged as extraction evidence.
    public static string? DoorSprite(int recordedPose) => recordedPose switch
    {
        3 => "vick_d_00",
        4 => "vick_d_01",
        _ => null,
    };
}
