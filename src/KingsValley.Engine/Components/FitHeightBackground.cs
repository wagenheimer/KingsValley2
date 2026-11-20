using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Sprites;
using Nez.Textures;
using Wagenheimer.MonoGameHelper.Layout;

namespace KingsValley.Engine.Components;

/// <summary>
/// Component that positions and scales a Background to cover the whole screen keeping a fixed height (Fit Height).
/// At any aspect ratio (16:9, 16:10, 21:9 ultrawide), the background covers 100% of the screen with no black bars.
/// </summary>
public class FitHeightBackground : Component, IUpdatable
{
    private readonly Texture2D _texture;
    private SpriteRenderer _renderer;
    private int _textureWidth;
    private int _textureHeight;

    public FitHeightBackground(Texture2D texture)
    {
        _texture = texture;
        _textureWidth = texture.Width;
        _textureHeight = texture.Height;
    }

    public override void OnAddedToEntity()
    {
        base.OnAddedToEntity();
        _renderer = Entity.AddComponent(new SpriteRenderer(new Sprite(_texture)));
        _renderer.RenderLayer = RenderLayers.Background; // Background draws first (behind everything)
        UpdatePositionAndScale();
    }

    public void Update()
    {
        UpdatePositionAndScale();
    }

    private void UpdatePositionAndScale()
    {
        if (Entity == null || Entity.Scene == null)
            return;

        // DESIGN space (height 768, dynamic width) — do NOT use SceneRenderTargetSize,
        // which with SceneResolutionPolicy.None returns the window's native size.
        var designSize = DesignViewport.DesignSize;
        float virtualWidth = designSize.X;
        float virtualHeight = designSize.Y;

        // Centraliza o background horizontal e verticalmente
        Entity.Position = DesignViewport.Center;

        // Default background height is 768. If the screen is wider than the texture (1820px), it scales to fill everything.
        float scaleY = virtualHeight / _textureHeight;
        float scaleX = scaleY;

        if (virtualWidth > _textureWidth * scaleX)
        {
            float fillScale = virtualWidth / _textureWidth;
            scaleX = fillScale;
            scaleY = fillScale;
        }

        Entity.Scale = new Vector2(scaleX, scaleY);
    }
}
