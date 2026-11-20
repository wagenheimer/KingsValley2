#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Nez;
using Wagenheimer.MonoGameHelper.Common;
using Wagenheimer.MonoGameHelper.Input;
using Wagenheimer.MonoGameHelper.Layout;

namespace KingsValley.Engine.Input;

/// <summary>
/// Unifies pointer reading for Mouse, Touch (touch screens/mobile) and Virtual Cursor (GamePad).
/// Provides automatic coordinates in design space (256x212) and click/touch states.
/// </summary>
public static class UnifiedPointer
{
    private static Vector2 _lastDesignPosition;
    private static bool _hasMoved;

    /// <summary>Current pointer or touch position in design units (256x212).</summary>
    public static Vector2 Position
    {
        get
        {
            var touches = TouchPanel.GetState();
            foreach (var t in touches)
            {
                if (t.State is TouchLocationState.Pressed or TouchLocationState.Moved)
                    return ScreenToDesign(t.Position);
            }

            var helper = InputHelperManager.Instance;
            if (helper != null && helper.ActiveDevice is InputDeviceType.GamePad)
                return helper.VirtualCursor.Position;

            return ScreenToDesign(Nez.Input.MousePosition);
        }
    }

    /// <summary>Converts back buffer pixels to centered 256x212 design space.</summary>
    public static Vector2 ScreenToDesign(Vector2 screenPoint)
    {
        float scale = DesignViewport.Scale;
        if (scale <= 0) return screenPoint;
        float offsetX = (DesignViewport.PixelWidth - KingsValley.Core.Common.GameConstants.DesignWidth * scale) * 0.5f;
        float offsetY = (DesignViewport.PixelHeight - KingsValley.Core.Common.GameConstants.DesignHeight * scale) * 0.5f;
        return new Vector2((screenPoint.X - offsetX) / scale, (screenPoint.Y - offsetY) / scale);
    }

    /// <summary>True if the pointer moved since the last check.</summary>
    public static bool HasMoved
    {
        get
        {
            var current = Position;
            if (current != _lastDesignPosition)
            {
                _lastDesignPosition = current;
                _hasMoved = true;
            }
            else
            {
                _hasMoved = false;
            }
            return _hasMoved;
        }
    }

    /// <summary>First frame in which the mouse button or the finger touches the screen (or the virtual cursor's action button).</summary>
    public static bool IsJustPressed
    {
        get
        {
            if (Nez.Input.LeftMouseButtonPressed)
                return true;

            var touches = TouchPanel.GetState();
            foreach (var t in touches)
            {
                if (t.State == TouchLocationState.Pressed)
                    return true;
            }

            var helper = InputHelperManager.Instance;
            if (helper != null && helper.ActiveDevice is InputDeviceType.GamePad)
                return helper.VirtualCursor.IsLeftJustPressed;

            return false;
        }
    }

    /// <summary>True while the mouse button is held or the finger is on the screen.</summary>
    public static bool IsDown
    {
        get
        {
            if (Nez.Input.LeftMouseButtonDown)
                return true;

            var touches = TouchPanel.GetState();
            foreach (var t in touches)
            {
                if (t.State is TouchLocationState.Pressed or TouchLocationState.Moved)
                    return true;
            }

            var helper = InputHelperManager.Instance;
            if (helper != null && helper.ActiveDevice is InputDeviceType.GamePad)
                return helper.VirtualCursor.IsLeftDown;

            return false;
        }
    }

    /// <summary>First frame in which the mouse button is released or the finger leaves the screen.</summary>
    public static bool IsJustReleased
    {
        get
        {
            if (Nez.Input.LeftMouseButtonReleased)
                return true;

            var touches = TouchPanel.GetState();
            foreach (var t in touches)
            {
                if (t.State == TouchLocationState.Released)
                    return true;
            }

            var helper = InputHelperManager.Instance;
            if (helper != null && helper.ActiveDevice is InputDeviceType.GamePad)
                return helper.VirtualCursor.IsLeftJustReleased;

            return false;
        }
    }

    /// <summary>Secondary click (right mouse button or 2-finger touch).</summary>
    public static bool IsSecondaryJustPressed
    {
        get
        {
            if (Nez.Input.RightMouseButtonPressed)
                return true;

            var touches = TouchPanel.GetState();
            if (touches.Count >= 2)
            {
                foreach (var t in touches)
                {
                    if (t.State == TouchLocationState.Pressed)
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>True while the right button is held or there are 2 fingers on the screen.</summary>
    public static bool IsSecondaryDown
    {
        get
        {
            if (Nez.Input.RightMouseButtonDown)
                return true;

            var touches = TouchPanel.GetState();
            if (touches.Count >= 2)
            {
                foreach (var t in touches)
                {
                    if (t.State is TouchLocationState.Pressed or TouchLocationState.Moved)
                        return true;
                }
            }

            return false;
        }
    }
}
