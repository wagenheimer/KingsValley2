#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Textures;
using Wagenheimer.MonoGameHelper.Input;
using Wagenheimer.MonoGameHelper.Layout;
using Wagenheimer.MonoGameHelper.UI;

namespace KingsValley.Engine.Components;

/// <summary>
/// Gamepad virtual pointer.
///
/// ## Resolution independence
///
/// The cursor lives in the **design space** (<see cref="DesignViewport"/>: fixed height, dynamic
/// width) — the very same coordinate system used by the scene and the UI. Consequences:
///
/// - **Position**: identical on every window size; no per-resolution conversion scattered around.
///   The cursor bounds track the design viewport, so it can reach the whole screen at any aspect
///   ratio (16:9, 16:10, ultrawide) instead of being stuck at a fixed 1430x768.
/// - **Speed**: measured in design units per second, so the feel is the same at any resolution.
/// - **Size**: drawn in design units (the overlay applies the scale matrix), so it keeps the same
///   proportion on screen at 720p, 1080p, 1440p or 4K. Its **sharpness** follows the art: with the
///   current 1x art it is upscaled like everything else, and when 2x/4x art exists it becomes
///   crisp without any code change (see the `scale:` atlas convention in `AGENTS.md`).
///
/// ## Above the UI
///
/// Drawn by <see cref="PostUiOverlay"/>, i.e. **after** the UI, making it the front-most element
/// on screen. Previously it was a scene `SpriteRenderer` and therefore sat **behind** the UI
/// (the UI is drawn after the whole scene).
/// </summary>
public class VirtualCursorViewComponent : Component, IUpdatable, IPostUiDrawable
{
    private readonly Sprite _cursorSprite;

    /// <summary>Current cursor position, in design units.</summary>
    private Vector2 _designPosition;

    public VirtualCursorViewComponent(Sprite cursorSprite)
    {
        _cursorSprite = cursorSprite;
    }

    /// <summary>Above every other overlay element, including the UI.</summary>
    public int PostUiLayer => PostUiLayers.Pointer;

    public bool IsPostUiVisible { get; private set; }

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

    public void Update()
    {
        var inputManager = InputHelperManager.Instance;
        var cursor = inputManager.VirtualCursor;

        // 1. Keep the cursor bounds equal to the visible design area. This is what makes the
        //    cursor comparable across resolutions: its reach follows the window instead of being
        //    pinned to a fixed 1430x768.
        int designWidth = (int)DesignViewport.DesignWidth;
        int designHeight = DesignViewport.DesignHeight;

        if (cursor.ScreenWidth != designWidth || cursor.ScreenHeight != designHeight)
        {
            cursor.ScreenWidth = designWidth;
            cursor.ScreenHeight = designHeight;

            // Re-clamp the current position in case the window shrank.
            cursor.SetPosition(cursor.Position);
        }

        // 2. The VirtualCursor position is already in design units — no camera or pixel conversion.
        _designPosition = cursor.Position;

        IsPostUiVisible = inputManager.CursorManager.IsVirtualCursorVisible;
    }

    /// <summary>
    /// Draws the cursor in design units. The origin is the top-left corner (the arrow tip), hence
    /// <see cref="Vector2.Zero"/> — using the sprite's own origin here would mutate the shared
    /// atlas sprite.
    /// </summary>
    public void DrawPostUi(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(
            _cursorSprite.Texture2D,
            _designPosition,
            _cursorSprite.SourceRect,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            SpriteEffects.None,
            0f);
    }
}
