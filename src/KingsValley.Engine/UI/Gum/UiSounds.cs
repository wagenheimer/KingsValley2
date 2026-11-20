#nullable enable
using KingsValley.Engine.Assets;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// <summary>Default sounds of the game UI.</summary>
/// </summary>
/// <remarks>
/// Replicates Unity's <c>EventSounds</c>: every button/toggle plays **one click on click** and **nothing** on
/// hover (<c>useUnityPointerEnterSound: 0</c> in ButtonMed/ButtonBig/btYes/btNo/btBook...).
///
/// The sound is the **original game cursor** (id 50, <c>password_key</c>), played by the same backend as the
/// effects (<see cref="GameAudio.PlayCursor"/>): no MP3, no parallel mixer.
/// </remarks>
public static class UiSounds
{
    /// <summary>Plays the UI click sound (original cursor).</summary>
    public static void PlayClick() => GameAudio.PlayCursor();
}
