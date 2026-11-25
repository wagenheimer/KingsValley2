namespace KingsValley.Engine;

/// <summary>
/// Render layers (Nez RenderLayer) centralized for the whole scene.
///
/// NEZ RULE: the LOWER the value, the MORE IN FRONT the element is drawn.
///
/// > ⚠️ This is the order INSIDE THE SCENE. The UI (Gum) is drawn after the whole scene and
/// > <c>PostUiOverlay</c> after Gum — no layer here puts anything above the UI.
/// </summary>
public static class RenderLayers
{
    /// <summary>Room background (sky/wall) — behind everything.</summary>
    public const int Background = 1000;

    /// <summary>Background decoration (not interactive).</summary>
    public const int BackDecor = 900;

    /// <summary>Floor/wall tile (the grid).</summary>
    public const int Tile = 100;

    /// <summary>Tile overlay (treasure/door/trap) on top of the floor.</summary>
    public const int TileOverlay = 90;

    /// <summary>Enemies (mummies/guards).</summary>
    public const int Enemy = 40;

    /// <summary>The player.</summary>
    public const int Player = 30;

    /// <summary>Projectiles/moving objects.</summary>
    public const int Projectile = 20;

    /// <summary>Episodic world effects (dust, sparks).</summary>
    public const int Effect = 10;

    /// <summary>Legacy user interface (Nez.UI) — DEPRECATED: the new UI is Gum.</summary>
    public const int Hud = 0;

    /// <summary>Effects above everything in the scene (death flash, screen).</summary>
    public const int ScreenFx = -50;

    /// <summary>
    /// System menu (ESC) — above the screen effects and everything in the world; below only the virtual
    /// cursor (<see cref="VirtualCursor"/>), which is drawn in the <c>PostUiOverlay</c>.
    /// </summary>
    public const int SystemMenu = -200;

    /// <summary>GamePad virtual pointer — above everything in the scene (but not the UI).</summary>
    public const int VirtualCursor = -9999;
}
