#nullable enable
using Microsoft.Xna.Framework;
using Nez;
using Wagenheimer.MonoGameHelper.Layout;

namespace KingsValley.Engine.Graphics;

/// <summary>
/// Keeps the Nez camera in the **native resolution + design coordinates** model.
///
/// What it does:
/// - `Camera.RawZoom = DesignViewport.Scale` — the *raw* zoom (the real scale applied to the
///   matrix). Note that `Camera.Zoom` is a normalized -1..1 value mapped between
///   `MinimumZoom`/`MaximumZoom`, so it must **not** be used for this.
/// - Positions the camera at the **design centre** (<see cref="DesignViewport.Center"/>).
///   With native resolution the render target equals the back buffer, so `Camera.Origin` is the
///   centre of the *native* area and the position has to compensate for that.
/// - Reapplies whenever the window is resized (Nez adjusts the camera origin on size changes).
///
/// IMPORTANT: this component does **not** reposition the camera every frame — only when the
/// back buffer size changes. That is deliberate: a camera-shake component drives the position
/// during a shake and captures its rest anchor. Repositioning every frame would erase the shake.
/// Add this component and let it apply **before** adding any shake component, so the rest anchor
/// is the correct design centre.
/// </summary>
public class DesignViewportCamera : Component, IUpdatable
{
    private Point _lastPixelSize;
    private bool _applied;

    public override void OnAddedToEntity()
    {
        base.OnAddedToEntity();
        Apply();
    }

    /// <summary>Applies the design zoom and position to the camera. Idempotent.</summary>
    public void Apply()
    {
        var cameraEntity = Entity;
        if (cameraEntity?.Scene?.Camera == null)
            return;

        cameraEntity.Scene.Camera.RawZoom = DesignViewport.Scale;
        cameraEntity.Transform.Position = new Vector2(KingsValley.Core.Common.GameConstants.DesignWidth * 0.5f, KingsValley.Core.Common.GameConstants.DesignHeight * 0.5f);

        _lastPixelSize = new Point(DesignViewport.PixelWidth, DesignViewport.PixelHeight);
        _applied = true;
    }

    public void Update()
    {
        // DesignViewport is refreshed by the game loop; here we only react to a size change.
        var pixelSize = new Point(DesignViewport.PixelWidth, DesignViewport.PixelHeight);

        if (!_applied || pixelSize != _lastPixelSize)
        {
            Apply();
        }
    }
}
