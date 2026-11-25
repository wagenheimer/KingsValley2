#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Wagenheimer.MonoGameHelper.Graphics;
using Wagenheimer.MonoGameHelper.Layout;
using Wagenheimer.MonoGameHelper.UI;

namespace KingsValley.Engine.UI;

/// <summary>
/// Base for elements drawn on the layer **above the UI** (<see cref="PostUiOverlay"/>), in
/// **design units**.
///
/// Use it for what must stay above the Gum UI: virtual cursor, screen flash, floating texts,
/// debug bars. The "real" interface (HUD, menus, dialogs) goes in **Gum**.
/// </summary>
public abstract class ScreenOverlay : Component, IUpdatable, IPostUiDrawable
{
    /// <summary>Order in the post-UI layer (higher = more in front). See <see cref="PostUiLayers"/>.</summary>
    public virtual int PostUiLayer => PostUiLayers.WorldEffect;

    /// <summary>When false, nothing is drawn (but it stays registered).</summary>
    public virtual bool IsPostUiVisible => true;

    public override void OnAddedToEntity()
    {
        base.OnAddedToEntity();
        PostUiOverlay.Register(this);
    }

    public override void OnRemovedFromEntity()
    {
        PostUiOverlay.Unregister(this);
        base.OnRemovedFromEntity();
    }

    /// <summary>Logic (runs every frame). Override when needed.</summary>
    public virtual void Update()
    {
    }

    /// <summary>Drawing in design units (the scale matrix is already applied).</summary>
    public abstract void DrawPostUi(SpriteBatch spriteBatch);

    /// <summary>Visible width in design units (dynamic).</summary>
    protected static float DesignW => DesignViewport.DesignWidth;

    /// <summary>Visible height in design units (fixed).</summary>
    protected static float DesignH => DesignViewport.DesignHeight;

    /// <summary>Fills a rectangle (design units).</summary>
    protected static void Fill(SpriteBatch batch, float x, float y, float w, float h, Color color)
        => SpriteBatchDrawing.FillRectangle(batch, new Rectangle((int)x, (int)y, (int)w, (int)h), color);

    /// <summary>Draws the outline of a rectangle (design units).</summary>
    protected static void Outline(SpriteBatch batch, Rectangle rectangle, Color color, int thickness = 3)
        => SpriteBatchDrawing.DrawRectangleOutline(batch, rectangle, color, thickness);
}
