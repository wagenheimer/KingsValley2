#nullable enable
using System;
using Microsoft.Xna.Framework;
using Nez;
using Wagenheimer.MonoGameHelper.Common;
using Wagenheimer.MonoGameHelper.Input;
using Wagenheimer.MonoGameHelper.Layout;
using global::Gum.Wireframe;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Input bridge: makes **Gum** read the project's pointer (<see cref="InputHelperManager"/> —
/// physical mouse **or** the GamePad/touch virtual cursor) instead of the hardware mouse.
///
/// ## Why it exists
///
/// `MonoGameGum`'s default cursor is bound to the hardware: `X`/`Y` have `private set` and
/// `PrimaryDown/Push/Click` are read from `MouseState`/`TouchCollection` inside Gum itself.
/// Gum exposes <c>FormsUtilities.SetCursor(ICursor)</c>, so we swap the cursor for this one —
/// and from then on **the whole** Gum Forms system (hover, press, focus, `Click`) works with the
/// game's input, including the controller's virtual cursor.
///
/// ## Units (the deceptive detail)
///
/// The interface asks for `X`/`Y` in **screen pixels** (the framework converts on its own with
/// `XRespectingGumZoomAndBounds()`). Since the project thinks in **design units** (the Gum canvas
/// = `DesignViewport`), this cursor keeps the position in design and converts at both ends.
///
/// ## Order in the frame
///
/// `KingsValleyGame.Update` calls `InputHelperManager.Update(...)` **before** `GumUI.Update(gameTime)`,
/// so the state read here is already the current frame's.
/// </summary>
internal sealed class InputHelperCursor : ICursor
{
    /// <summary>Window (s) for two clicks to count as a double click (same value as Gum).</summary>
    private const float DoubleClickSeconds = 0.25f;

    private readonly InputHelperManager _input;

    private double _gameTime;
    private double _lastClickTime = -999d;
    private bool _doubleClick;

    public InputHelperCursor(InputHelperManager input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
    }

    /// <summary>True when the pointer is the virtual cursor (GamePad/touch), not the physical mouse.</summary>
    private bool VirtualPointer => _input.ActiveDevice is InputDeviceType.GamePad or InputDeviceType.Touch;

    /// <summary>Pointer position in **design units** (the Gum canvas).</summary>
    private Vector2 DesignPosition => KingsValley.Engine.Input.UnifiedPointer.Position;

    // ---------------------------------------------------------------------------------------
    // ICursor — position
    // ---------------------------------------------------------------------------------------

    /// <inheritdoc />
    public int X { get; private set; }

    /// <inheritdoc />
    public int Y { get; private set; }

    private int _lastX;
    private int _lastY;

    /// <inheritdoc />
    public int XChange => X - _lastX;

    /// <inheritdoc />
    public int YChange => Y - _lastY;

    /// <inheritdoc />
    public int ScrollWheelChange { get; private set; }

    /// <inheritdoc />
    public float ZVelocity => ScrollWheelChange;

    // ---------------------------------------------------------------------------------------
    // ICursor — buttons (read LIVE; they are stable within the frame)
    // ---------------------------------------------------------------------------------------

    /// <inheritdoc />
    public bool PrimaryPush => KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

    /// <inheritdoc />
    public bool PrimaryDown => KingsValley.Engine.Input.UnifiedPointer.IsDown;

    /// <inheritdoc />
    public bool PrimaryClick => KingsValley.Engine.Input.UnifiedPointer.IsJustReleased;

    /// <inheritdoc />
    public bool PrimaryClickNoSlide => PrimaryClick;

    /// <inheritdoc />
    public bool PrimaryDoubleClick => _doubleClick;

    /// <inheritdoc />
    public bool PrimaryDoublePush => false;

    /// <inheritdoc />
    public bool SecondaryPush => false;

    /// <inheritdoc />
    public bool SecondaryDown => false;

    /// <inheritdoc />
    public bool SecondaryClick => false;

    /// <inheritdoc />
    public bool SecondaryDoubleClick => false;

    /// <inheritdoc />
    public bool MiddlePush => false;

    /// <inheritdoc />
    public bool MiddleDown => false;

    /// <inheritdoc />
    public bool MiddleClick => false;

    /// <inheritdoc />
    public bool MiddleDoubleClick => false;

    // ---------------------------------------------------------------------------------------
    // ICursor — state written by the framework
    // ---------------------------------------------------------------------------------------

    /// <summary>The hardware cursor is managed by <see cref="HybridCursorManager"/> — not by Gum.</summary>
    public Cursors? CustomCursor
    {
        get => null;
        set { /* managed by HybridCursorManager (hardware / virtual cursor) */ }
    }

    /// <inheritdoc />
    public InputDevice LastInputDevice => _input.ActiveDevice == InputDeviceType.Touch
        ? InputDevice.TouchScreen
        : InputDevice.Mouse;

    /// <inheritdoc />
    public InteractiveGue? WindowPushed { get; set; }

    /// <inheritdoc />
    public InteractiveGue? VisualRightPushed { get; set; }

    /// <inheritdoc />
    [Obsolete("Use VisualOver")]
    public InteractiveGue? WindowOver
    {
        get => VisualOver;
        set => VisualOver = value;
    }

    /// <inheritdoc />
    public InteractiveGue? VisualOver { get; set; }

    // ---------------------------------------------------------------------------------------
    // ICursor — lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Pointer position in the **Gum canvas** space (design units). It is the value the
    /// framework uses for the hit-test — we return it directly, without redoing the zoom math.
    /// </summary>
    public float XRespectingGumZoomAndBounds() => DesignPosition.X;

    /// <inheritdoc cref="XRespectingGumZoomAndBounds" />
    public float YRespectingGumZoomAndBounds() => DesignPosition.Y;

    /// <summary>
    /// End of the input frame (the framework calls it on every update): consolidates position/scroll and the
    /// double-click timing. The buttons stay "live" in the getters.
    /// </summary>
    public void Activity(double currentGameTimeTotalSeconds)
    {
        var design = DesignPosition;

        X = (int)MathF.Round(DesignViewport.ToScreen(design).X);
        Y = (int)MathF.Round(DesignViewport.ToScreen(design).Y);
        ScrollWheelChange = Nez.Input.MouseWheelDelta;

        _doubleClick = PrimaryClick && (currentGameTimeTotalSeconds - _lastClickTime) <= DoubleClickSeconds;
        if (PrimaryClick)
            _lastClickTime = currentGameTimeTotalSeconds;

        _gameTime = currentGameTimeTotalSeconds;
        _lastX = X;
        _lastY = Y;
    }

    /// <summary>Instant of the last <see cref="Activity"/> (game seconds).</summary>
    public double CurrentTime => _gameTime;

    /// <inheritdoc />
    public double LastPrimaryClickTime => _lastClickTime;

    /// <inheritdoc />
    public double LastPrimaryPushTime => _lastClickTime;
}
