#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.SaveSystem;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Input;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.Scenes;
using Wagenheimer.MsxAudio.Mixing;

namespace KingsValley.Engine.UI;

/// <summary>
/// **System menu** (opened/closed with <c>ESC</c> during gameplay): SaveState / LoadState (3 slots
/// with screenshot preview), audio profile, music, sound effects, graphics variant, fullscreen,
/// retry stage, resume and back to title.
/// </summary>
/// <remarks>
/// <para>Drawn with the game's own font (<see cref="MsxScreen"/>) on the
/// <see cref="RenderLayers.SystemMenu"/> layer, above the world — authentic MSX2 Konami pause menu.</para>
/// <para>While it is open the game scene **freezes** (<see cref="Scenes.GameScene"/> returns before
/// processing a tick).</para>
/// </remarks>
public static class SystemMenu
{
    private static Entity? _entity;
    private static View? _view;
    private static byte[]? _capturedThumbnail;

    // Key repeat timing
    private static float _upHoldTimer;
    private static float _downHoldTimer;
    private static float _leftHoldTimer;
    private static float _rightHoldTimer;

    // Pointer hover isolation: mouse only affects selection when it actually moves
    private static Vector2 _lastPointerPosition = new(-1000, -1000);
    private static bool _pointerMovedSinceKeyboard;

    /// <summary>Is the menu open?</summary>
    public static bool IsOpen => _view != null;

    /// <summary>Frame the menu was opened on (to ignore the same ESC that opened it).</summary>
    public static uint OpenedFrame { get; private set; }

    /// <summary>Opens the menu if closed, closes it if open.</summary>
    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>Opens the menu in the middle of the current scene.</summary>
    public static void Open()
    {
        if (IsOpen) return;

        var scene = Nez.Core.Scene;
        if (scene == null) return;

        // Capture current screen for SaveState before drawing the menu box
        _capturedThumbnail = ScreenshotHelper.CaptureThumbnail(Nez.Core.GraphicsDevice);

        _view = new View();
        _entity = scene.CreateEntity("system-menu");
        _entity.AddComponent(_view);
        OpenedFrame = Time.FrameCount;

        _upHoldTimer = 0f;
        _downHoldTimer = 0f;
        _leftHoldTimer = 0f;
        _rightHoldTimer = 0f;
        _lastPointerPosition = UnifiedPointer.Position;
        _pointerMovedSinceKeyboard = false;

        GameAudio.PlayConfirm();
        Console.WriteLine($"[SystemMenu] opened (frame {OpenedFrame})");
    }

    /// <summary>Closes the menu (idempotent).</summary>
    public static void Close()
    {
        if (_view == null) return;

        _capturedThumbnail = null;
        _view = null;
        _entity?.Destroy();
        _entity = null;
        GameAudio.PlayCursor();
        Console.WriteLine($"[SystemMenu] closed (frame {Time.FrameCount})");
    }

    /// <summary>
    /// Reads menu input (ESC to open/close, arrows/WASD, confirm/cancel, mouse/touch).
    /// Call once per frame, **after** <c>base.Update</c>.
    /// </summary>
    public static void Update()
    {
        if (Nez.Core.Instance is { IsActive: false })
            return;

        // Closed: ESC during gameplay opens the menu.
        if (_view == null)
        {
            if (CanOpen() && Pressed(Keys.Escape))
                Open();

            return;
        }

        // The ESC that opened the menu cannot close it on the same frame.
        if (Time.FrameCount == OpenedFrame)
            return;

        // 1. Check raw key states
        bool rawUp = IsKey(Keys.Up) || IsKey(Keys.W) || IsKey(Keys.NumPad8);
        bool rawDown = IsKey(Keys.Down) || IsKey(Keys.S) || IsKey(Keys.NumPad2);
        bool rawLeft = IsKey(Keys.Left) || IsKey(Keys.A) || IsKey(Keys.NumPad4);
        bool rawRight = IsKey(Keys.Right) || IsKey(Keys.D) || IsKey(Keys.NumPad6);

        bool rawUpPressed = Pressed(Keys.Up) || Pressed(Keys.W) || Pressed(Keys.NumPad8);
        bool rawDownPressed = Pressed(Keys.Down) || Pressed(Keys.S) || Pressed(Keys.NumPad2);
        bool rawLeftPressed = Pressed(Keys.Left) || Pressed(Keys.A) || Pressed(Keys.NumPad4);
        bool rawRightPressed = Pressed(Keys.Right) || Pressed(Keys.D) || Pressed(Keys.NumPad6);

        // Gamepad inputs
        bool padUpPressed = false, padDownPressed = false, padLeftPressed = false, padRightPressed = false;
        bool padUp = false, padDown = false, padLeft = false, padRight = false;
        bool padConfirm = false, padCancel = false;

        foreach (var pad in Nez.Input.GamePads)
        {
            if (!pad.IsConnected()) continue;

            padUpPressed |= pad.IsButtonPressed(Buttons.DPadUp) || pad.IsLeftStickUpPressed();
            padDownPressed |= pad.IsButtonPressed(Buttons.DPadDown) || pad.IsLeftStickDownPressed();
            padLeftPressed |= pad.IsButtonPressed(Buttons.DPadLeft) || pad.IsLeftStickLeftPressed();
            padRightPressed |= pad.IsButtonPressed(Buttons.DPadRight) || pad.IsLeftStickRightPressed();

            padUp |= pad.DpadUpDown || pad.IsLeftStickUp();
            padDown |= pad.DpadDownDown || pad.IsLeftStickDown();
            padLeft |= pad.DpadLeftDown || pad.IsLeftStickLeft();
            padRight |= pad.DpadRightDown || pad.IsLeftStickRight();

            padConfirm |= pad.IsButtonPressed(Buttons.A);
            padCancel |= pad.IsButtonPressed(Buttons.B) || pad.IsButtonPressed(Buttons.Back) || pad.IsButtonPressed(Buttons.Start);
        }

        // Key repeat processing (initial delay 0.28s, repeat every 0.08s)
        bool up = CheckRepeatedInput(rawUpPressed || padUpPressed, rawUp || padUp, ref _upHoldTimer);
        bool down = CheckRepeatedInput(rawDownPressed || padDownPressed, rawDown || padDown, ref _downHoldTimer);
        bool left = CheckRepeatedInput(rawLeftPressed || padLeftPressed, rawLeft || padLeft, ref _leftHoldTimer);
        bool right = CheckRepeatedInput(rawRightPressed || padRightPressed, rawRight || padRight, ref _rightHoldTimer);

        bool confirm = Pressed(Keys.Enter) || Pressed(Keys.Space) || Pressed(Keys.Z) || Pressed(Keys.J) || Pressed(Keys.K) || padConfirm;
        bool cancel = Pressed(Keys.Escape) || Pressed(Keys.Back) || padCancel;

        if (cancel)
        {
            if (_view.Page != MenuPage.Main)
            {
                _view.SwitchToMain();
                GameAudio.PlayCursor();
                return;
            }

            Close();
            return;
        }

        // If keyboard/gamepad navigated this frame, suppress mouse hover until mouse physically moves
        bool navInput = up || down || left || right || confirm;
        if (navInput)
        {
            _pointerMovedSinceKeyboard = false;
            _lastPointerPosition = UnifiedPointer.Position;
        }

        if (up) Move(-1);
        if (down) Move(+1);
        if (left) Change(-1);
        if (right) Change(+1);
        if (confirm) Activate();

        // Mouse pointer handling
        Pointer(confirm);
    }

    /// <summary>Index of the selected row (diagnostics).</summary>
    public static int SelectedIndex => _view?.Selected ?? -1;

    /// <summary>Moves the selection (diagnostics).</summary>
    public static void Select(int direction) => Move(direction);

    /// <summary>Cycles the selected value (diagnostics).</summary>
    public static void Cycle(int direction) => Change(direction);

    /// <summary>Activates the selected option (diagnostics).</summary>
    public static void Confirm() => Activate();

    private static bool Pressed(Keys key) => Nez.Input.IsKeyPressed(key);
    private static bool IsKey(Keys key) => Nez.Input.IsKeyDown(key);

    private static bool CheckRepeatedInput(bool isPressed, bool isDown, ref float holdTimer)
    {
        if (isPressed)
        {
            holdTimer = 0f;
            return true;
        }

        if (isDown)
        {
            holdTimer += Time.DeltaTime;
            const float initialDelay = 0.28f;
            const float repeatRate = 0.08f;
            if (holdTimer >= initialDelay)
            {
                holdTimer -= repeatRate;
                return true;
            }
        }
        else
        {
            holdTimer = 0f;
        }

        return false;
    }

    /// <summary>The menu opens during gameplay.</summary>
    private static bool CanOpen() => Nez.Core.Scene is Scenes.GameScene;

    private static void Move(int direction)
    {
        if (_view == null) return;
        _view.Move(direction);
        GameAudio.PlayCursor();
    }

    private static void Change(int direction)
    {
        if (_view == null) return;
        _view.Change(direction);
    }

    private static void Activate()
    {
        if (_view == null) return;
        _view.Activate();
    }

    /// <summary>Mouse/touch/virtual-cursor hover and click (design coordinates).</summary>
    private static void Pointer(bool confirmHandled)
    {
        if (_view == null) return;

        Vector2 currentPointer = UnifiedPointer.Position;

        // Detect actual mouse movement
        if (Vector2.DistanceSquared(currentPointer, _lastPointerPosition) > 4.0f)
        {
            _lastPointerPosition = currentPointer;
            _pointerMovedSinceKeyboard = true;
        }

        if (_pointerMovedSinceKeyboard)
        {
            if (_view.Hover(currentPointer))
                GameAudio.PlayCursor();
        }

        // Pointer click
        if (!confirmHandled && UnifiedPointer.IsJustPressed)
        {
            _pointerMovedSinceKeyboard = true;
            _lastPointerPosition = currentPointer;
            if (_view.Hover(currentPointer))
            {
                _view.HandleClickAt(currentPointer);
            }
        }
    }

    /// <summary>Submenu page.</summary>
    private enum MenuPage
    {
        Main,
        SaveState,
        LoadState,
    }

    /// <summary>One menu row.</summary>
    private sealed class Item
    {
        public string Label = string.Empty;
        public Func<string>? Value;
        public Action? Activate;
        public Action<int>? Change;
    }

    /// <summary>Menu drawing and layout.</summary>
    private sealed class View : RenderableComponent, IUpdatable
    {
        private const int PanelWidth = 216;
        private const int PanelHeight = 184;
        private const int RowHeightMain = 10;
        private const int FirstRowYMain = 18;
        private const int RowHeightSlot = 12;
        private const int FirstRowYSlot = 23;

        private static readonly Color BackdropDim = new(0, 0, 0, 160);
        private static readonly Color PanelBg = new(6, 18, 64);
        private static readonly Color OuterBorder = new(88, 172, 255);
        private static readonly Color InnerBorder = new(0, 56, 136);
        private static readonly Color DividerColor = new(0, 72, 160);
        private static readonly Color TitleColor = new(255, 224, 70);
        private static readonly Color HighlightBg = new(0, 70, 170);
        private static readonly Color AccentBar = new(255, 224, 70);
        private static readonly Color TextSelected = Color.White;
        private static readonly Color TextNormal = new(190, 215, 245);
        private static readonly Color ValueSelected = new(255, 224, 70);
        private static readonly Color ValueNormal = new(225, 225, 170);
        private static readonly Color ArrowSelected = new(255, 224, 70);
        private static readonly Color ArrowNormal = new(80, 120, 175);
        private static readonly Color HintColor = new(130, 180, 255);

        private readonly Item[] _mainItems;
        private int _selected;
        private MenuPage _page = MenuPage.Main;

        // Slot state cache
        private readonly SaveStateData?[] _slotData = new SaveStateData?[SaveStateService.MaxSlots];
        private readonly Texture2D?[] _slotTextures = new Texture2D?[SaveStateService.MaxSlots];
        private string _statusMessage = string.Empty;
        private float _statusTimer;

        public View()
        {
            _mainItems = new[]
            {
                new Item
                {
                    Label = "CONTINUE",
                    Activate = Close,
                },
                new Item
                {
                    Label = "SAVE STATE",
                    Activate = () => SwitchToPage(MenuPage.SaveState),
                },
                new Item
                {
                    Label = "LOAD STATE",
                    Activate = () => SwitchToPage(MenuPage.LoadState),
                },
                new Item
                {
                    Label = "RETRY STAGE",
                    Activate = RestartStage,
                },
                new Item
                {
                    Label = "MUSIC",
                    Value = () => GameAudio.MusicEnabled ? "ON" : "OFF",
                    Change = _ => ToggleMusic(),
                    Activate = ToggleMusic,
                },
                new Item
                {
                    Label = "SOUND",
                    Value = () => GameAudio.SoundEnabled ? "ON" : "OFF",
                    Change = _ => ToggleSound(),
                    Activate = ToggleSound,
                },
                new Item
                {
                    Label = "AUDIO",
                    Value = () => GameAudio.Profile.Name.ToUpperInvariant(),
                    Change = CycleProfile,
                    Activate = () => CycleProfile(+1),
                },
                new Item
                {
                    Label = "GRAPHICS",
                    Value = () => GfxVariant.Current.Name,
                    Change = CycleGraphics,
                    Activate = () => CycleGraphics(+1),
                },
                new Item
                {
                    Label = "BACKDROP",
                    Value = () => RomContent.Msx1OpaqueBackdrop ? "BLACK" : "BORDER",
                    Change = _ => ToggleMsx1Backdrop(),
                    Activate = ToggleMsx1Backdrop,
                },
                new Item
                {
                    Label = "SCREEN",
                    Value = () => IsFullScreen ? "FULL" : "WINDOW",
                    Change = _ => ToggleFullScreen(),
                    Activate = ToggleFullScreen,
                },
                new Item
                {
                    Label = "TITLE LOGO",
                    Value = () => RomContent.TitleArtLanguage == TitleLanguage.Japanese ? "JP" : "EN",
                    Change = _ => ToggleTitleLanguage(),
                    Activate = ToggleTitleLanguage,
                },
                new Item
                {
                    Label = "ROM SETUP",
                    Activate = () =>
                    {
                        Close();
                        Nez.Core.Scene = new Scenes.FirstRunScene();
                    },
                },
                new Item
                {
                    Label = "RESET DATA",
                    Activate = () =>
                    {
                        Close();
                        Extraction.RomFolderManager.PurgeExtractedContent();
                        RomContent.Initialize();
                        GameAssets.Unload();
                        Nez.Core.Scene = new Scenes.FirstRunScene("EXTRACTED DATA RESET. SELECT A ROM TO EXTRACT.");
                    },
                },
                new Item
                {
                    Label = "MAIN MENU",
                    Activate = QuitToTitle,
                },
            };
        }

        public MenuPage Page => _page;
        public int Selected => _selected;

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public override void OnAddedToEntity()
        {
            base.OnAddedToEntity();
            RenderLayer = RenderLayers.SystemMenu;
        }

        public override void OnRemovedFromEntity()
        {
            base.OnRemovedFromEntity();
            DisposeSlotTextures();
        }

        public void Update()
        {
            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.DeltaTime;
                if (_statusTimer <= 0f)
                    _statusMessage = string.Empty;
            }
        }

        public void SwitchToMain()
        {
            _page = MenuPage.Main;
            _selected = 1; // Highlight SAVE STATE when returning
            _statusMessage = string.Empty;
        }

        private void SwitchToPage(MenuPage page)
        {
            _page = page;
            _selected = 0;
            _statusMessage = string.Empty;
            ReloadSlots();
            GameAudio.PlayConfirm();
        }

        private void ReloadSlots()
        {
            DisposeSlotTextures();
            var gd = Nez.Core.GraphicsDevice;

            for (int i = 0; i < SaveStateService.MaxSlots; i++)
            {
                int slot = i + 1;
                _slotData[i] = SaveStateService.Load(slot);

                var pngPath = SaveStateService.GetPngPath(slot);
                if (File.Exists(pngPath) && gd != null)
                {
                    try
                    {
                        using var stream = File.OpenRead(pngPath);
                        _slotTextures[i] = Texture2D.FromStream(gd, stream);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SystemMenu] Failed to load slot {slot} preview: {ex.Message}");
                    }
                }
            }
        }

        private void DisposeSlotTextures()
        {
            for (int i = 0; i < _slotTextures.Length; i++)
            {
                _slotTextures[i]?.Dispose();
                _slotTextures[i] = null;
            }
        }

        private int TotalRows => _page == MenuPage.Main ? _mainItems.Length : 4; // 3 slots + Back

        public void Move(int direction) =>
            _selected = (_selected + direction + TotalRows) % TotalRows;

        public void Change(int direction)
        {
            if (_page == MenuPage.Main)
            {
                Item item = _mainItems[_selected];
                if (item.Change == null) return;
                GameAudio.PlayCursor();
                item.Change(direction);
            }
            else
            {
                Move(direction);
            }
        }

        public void Activate()
        {
            if (_page == MenuPage.Main)
            {
                Item item = _mainItems[_selected];
                if (item.Activate != null)
                {
                    item.Activate();
                    return;
                }

                Change(+1);
            }
            else
            {
                // Slot page
                if (_selected == 3) // Back
                {
                    SwitchToMain();
                    GameAudio.PlayCursor();
                    return;
                }

                int slot = _selected + 1;
                if (Nez.Core.Scene is GameScene gameScene)
                {
                    if (_page == MenuPage.SaveState)
                    {
                        var data = gameScene.CreateSaveState(slot);
                        if (data != null)
                        {
                            SaveStateService.Save(slot, data, _capturedThumbnail);
                            _slotData[_selected] = data;

                            // Refresh thumbnail in UI
                            _slotTextures[_selected]?.Dispose();
                            _slotTextures[_selected] = null;
                            if (_capturedThumbnail != null && _capturedThumbnail.Length > 0 && Nez.Core.GraphicsDevice != null)
                            {
                                using var ms = new MemoryStream(_capturedThumbnail);
                                _slotTextures[_selected] = Texture2D.FromStream(Nez.Core.GraphicsDevice, ms);
                            }

                            GameAudio.PlayConfirm();
                            _statusMessage = $"SAVED TO SLOT {slot}!";
                            _statusTimer = 2.0f;
                        }
                    }
                    else if (_page == MenuPage.LoadState)
                    {
                        var data = _slotData[_selected];
                        if (data == null)
                        {
                            GameAudio.PlayCursor();
                            _statusMessage = "SLOT IS EMPTY!";
                            _statusTimer = 1.5f;
                            return;
                        }

                        if (gameScene.LoadSaveState(data))
                        {
                            GameAudio.PlayConfirm();
                            Close();
                        }
                    }
                }
            }
        }

        public bool Hover(Vector2 pointer)
        {
            int x = (GameConstants.DesignWidth - PanelWidth) / 2;
            int y = (GameConstants.DesignHeight - PanelHeight) / 2;

            if (_page == MenuPage.Main)
            {
                int firstRow = y + FirstRowYMain;
                if (pointer.X < x + 4 || pointer.X > x + PanelWidth - 4 ||
                    pointer.Y < firstRow - 2 || pointer.Y >= firstRow + _mainItems.Length * RowHeightMain)
                {
                    return false;
                }

                int row = (int)((pointer.Y - (firstRow - 2)) / RowHeightMain);
                if (row < 0 || row >= _mainItems.Length || row == _selected)
                    return false;

                _selected = row;
                return true;
            }
            else
            {
                int firstRow = y + FirstRowYSlot;
                if (pointer.X < x + 4 || pointer.X > x + PanelWidth - 4 ||
                    pointer.Y < firstRow - 2 || pointer.Y >= firstRow + 4 * RowHeightSlot)
                {
                    return false;
                }

                int row = (int)((pointer.Y - (firstRow - 2)) / RowHeightSlot);
                if (row < 0 || row >= 4 || row == _selected)
                    return false;

                _selected = row;
                return true;
            }
        }

        public void HandleClickAt(Vector2 pointer)
        {
            if (_page != MenuPage.Main)
            {
                Activate();
                return;
            }

            int x = (GameConstants.DesignWidth - PanelWidth) / 2;
            Item item = _mainItems[_selected];

            if (item.Activate != null && item.Change == null)
            {
                item.Activate();
                return;
            }

            // Clicked left side of value -> change(-1), right side -> change(+1)
            int midX = x + PanelWidth - 40;
            if (pointer.X < midX)
                Change(-1);
            else
                Change(+1);
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            // 1. Semi-transparent dark overlay dimming the game behind
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), BackdropDim);

            int x = (GameConstants.DesignWidth - PanelWidth) / 2;
            int y = (GameConstants.DesignHeight - PanelHeight) / 2;

            // 2. Panel box
            MsxScreen.Fill(batcher, new Rectangle(x, y, PanelWidth, PanelHeight), PanelBg);

            // Double border
            MsxScreen.Fill(batcher, new Rectangle(x, y, PanelWidth, 1), OuterBorder);
            MsxScreen.Fill(batcher, new Rectangle(x, y + PanelHeight - 1, PanelWidth, 1), OuterBorder);
            MsxScreen.Fill(batcher, new Rectangle(x, y, 1, PanelHeight), OuterBorder);
            MsxScreen.Fill(batcher, new Rectangle(x + PanelWidth - 1, y, 1, PanelHeight), OuterBorder);

            MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, PanelWidth - 2, 1), InnerBorder);
            MsxScreen.Fill(batcher, new Rectangle(x + 1, y + PanelHeight - 2, PanelWidth - 2, 1), InnerBorder);
            MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, 1, PanelHeight - 2), InnerBorder);
            MsxScreen.Fill(batcher, new Rectangle(x + PanelWidth - 2, y + 1, 1, PanelHeight - 2), InnerBorder);

            if (_page == MenuPage.Main)
            {
                RenderMainMenu(batcher, x, y);
            }
            else
            {
                RenderSlotMenu(batcher, x, y);
            }
        }

        private void RenderMainMenu(Batcher batcher, int x, int y)
        {
            // 3. Header title
            const string title = "* SYSTEM MENU *";
            float titleX = x + (PanelWidth - title.Length * MsxScreen.CharSize) / 2f;
            MsxScreen.Text(batcher, title, titleX, y + 6, TitleColor);

            // Header separator line
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 16, PanelWidth - 12, 1), DividerColor);

            // 4. Menu rows
            for (int i = 0; i < _mainItems.Length; i++)
            {
                int rowY = y + FirstRowYMain + i * RowHeightMain;
                bool isSelected = i == _selected;

                if (isSelected)
                {
                    // Highlight row background
                    MsxScreen.Fill(batcher, new Rectangle(x + 4, rowY - 1, PanelWidth - 8, 10), HighlightBg);
                    // Golden accent bar on left edge
                    MsxScreen.Fill(batcher, new Rectangle(x + 4, rowY - 1, 2, 10), AccentBar);

                    // Selection cursor
                    if (MsxScreen.Get("arrow") != null)
                        MsxScreen.Draw(batcher, "arrow", x + 8, rowY);
                    else
                        MsxScreen.MiniText(batcher, "►", x + 9, rowY + 1, TitleColor);
                }

                // Label
                Color labelColor = isSelected ? TextSelected : TextNormal;
                MsxScreen.Text(batcher, _mainItems[i].Label, x + 22, rowY, labelColor);

                // Value with arrows (if row has value)
                string value = _mainItems[i].Value?.Invoke() ?? string.Empty;
                if (value.Length > 0)
                {
                    Color valColor = isSelected ? ValueSelected : ValueNormal;
                    Color arrColor = isSelected ? ArrowSelected : ArrowNormal;

                    int valWidth = value.Length * MsxScreen.CharSize;
                    int valRightX = x + PanelWidth - 14;
                    int valX = valRightX - valWidth;

                    // Left arrow ◄
                    MsxScreen.MiniText(batcher, "◄", valX - 7, rowY + 1, arrColor);

                    // Value text
                    MsxScreen.Text(batcher, value, valX, rowY, valColor);

                    // Right arrow ►
                    MsxScreen.MiniText(batcher, "►", valRightX + 3, rowY + 1, arrColor);
                }
            }

            // Footer separator line
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + PanelHeight - 16, PanelWidth - 12, 1), DividerColor);

            // 5. Footer help text
            MsxScreen.MiniText(batcher, "▲▼ SELECT  ◄► CHANGE  ENTER OK  ESC RESUME", x + 10, y + PanelHeight - 11, HintColor);
        }

        private void RenderSlotMenu(Batcher batcher, int x, int y)
        {
            string title = _page == MenuPage.SaveState ? "* SAVE STATE *" : "* LOAD STATE *";
            float titleX = x + (PanelWidth - title.Length * MsxScreen.CharSize) / 2f;
            MsxScreen.Text(batcher, title, titleX, y + 6, TitleColor);

            // Header separator line
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 16, PanelWidth - 12, 1), DividerColor);

            // 4 Slot rows
            for (int i = 0; i < 4; i++)
            {
                int rowY = y + FirstRowYSlot + i * RowHeightSlot;
                bool isSelected = i == _selected;

                if (isSelected)
                {
                    MsxScreen.Fill(batcher, new Rectangle(x + 4, rowY - 2, PanelWidth - 8, 11), HighlightBg);
                    MsxScreen.Fill(batcher, new Rectangle(x + 4, rowY - 2, 2, 11), AccentBar);

                    if (MsxScreen.Get("arrow") != null)
                        MsxScreen.Draw(batcher, "arrow", x + 8, rowY + 1);
                    else
                        MsxScreen.MiniText(batcher, "►", x + 9, rowY + 2, TitleColor);
                }

                Color labelColor = isSelected ? TextSelected : TextNormal;

                if (i < 3)
                {
                    var data = _slotData[i];
                    string label = data != null
                        ? $"SLOT {i + 1}: STAGE {data.Stage:D2}  {data.Timestamp:HH:mm}"
                        : $"SLOT {i + 1}: -- EMPTY --";

                    Color col = isSelected ? TextSelected : (data != null ? TextNormal : HintColor);
                    MsxScreen.Text(batcher, label, x + 22, rowY, col);
                }
                else
                {
                    MsxScreen.Text(batcher, "◄ BACK TO MENU", x + 22, rowY, labelColor);
                }
            }

            // Divider line below slots
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 73, PanelWidth - 12, 1), DividerColor);

            // Thumbnail and Metadata Preview
            if (_selected < 3)
            {
                int slotIdx = _selected;
                var data = _slotData[slotIdx];
                var tex = _slotTextures[slotIdx];

                int thumbX = x + 8;
                int thumbY = y + 77;
                int thumbW = 84;
                int thumbH = 70;

                // Outer border around thumbnail
                MsxScreen.Fill(batcher, new Rectangle(thumbX - 1, thumbY - 1, thumbW + 2, thumbH + 2), OuterBorder);

                if (tex != null)
                {
                    batcher.Draw(tex, new Rectangle(thumbX, thumbY, thumbW, thumbH), Color.White);
                }
                else
                {
                    MsxScreen.Fill(batcher, new Rectangle(thumbX, thumbY, thumbW, thumbH), Color.Black);
                    MsxScreen.MiniText(batcher, "NO PREVIEW", thumbX + 16, thumbY + 31, HintColor);
                }

                int infoX = thumbX + thumbW + 8;
                if (data != null)
                {
                    MsxScreen.MiniText(batcher, $"DATE: {data.Timestamp:yyyy-MM-dd}", infoX, thumbY + 2, TextNormal);
                    MsxScreen.MiniText(batcher, $"TIME: {data.Timestamp:HH:mm:ss}", infoX, thumbY + 13, TextNormal);
                    MsxScreen.MiniText(batcher, $"STAGE: {data.Stage:D2} (PYR {data.Pyramid})", infoX, thumbY + 24, ValueSelected);
                    MsxScreen.MiniText(batcher, $"ROOM:  {data.Room:D2}", infoX, thumbY + 35, TextNormal);
                    MsxScreen.MiniText(batcher, $"SCORE: {data.Score}", infoX, thumbY + 46, TitleColor);
                    int collected = Math.Max(0, data.TotalSoulStones - data.SoulStonesRemaining);
                    MsxScreen.MiniText(batcher, $"REST: {data.Lives}   SOULS: {collected}/{data.TotalSoulStones}", infoX, thumbY + 57, TextNormal);
                }
                else
                {
                    MsxScreen.Text(batcher, "EMPTY SLOT", infoX + 4, thumbY + 20, HintColor);
                    string actionHint = _page == MenuPage.SaveState ? "ENTER TO SAVE" : "NO SAVE DATA";
                    MsxScreen.MiniText(batcher, actionHint, infoX + 4, thumbY + 36, ArrowNormal);
                }
            }
            else
            {
                MsxScreen.Text(batcher, "RETURN TO MAIN MENU", x + 34, y + 105, TextNormal);
            }

            // Status message (saved / empty feedback)
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                int msgX = (int)(x + (PanelWidth - _statusMessage.Length * MsxScreen.CharSize) / 2f);
                MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 149, PanelWidth - 12, 10), HighlightBg);
                MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 149, PanelWidth - 12, 1), AccentBar);
                MsxScreen.Text(batcher, _statusMessage, msgX, y + 150, TitleColor);
            }

            // Footer separator line
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + PanelHeight - 16, PanelWidth - 12, 1), DividerColor);

            // Footer help text
            string footerHint = _page == MenuPage.SaveState
                ? "▲▼ SELECT   ENTER SAVE   ESC BACK"
                : "▲▼ SELECT   ENTER LOAD   ESC BACK";
            MsxScreen.MiniText(batcher, footerHint, x + 16, y + PanelHeight - 11, HintColor);
        }

        private static void ToggleMusic()
        {
            GameAudio.SetMusicEnabled(!GameAudio.MusicEnabled);
            SaveSettings();
            GameAudio.PlayConfirm();
        }

        private static void ToggleSound()
        {
            GameAudio.SetSoundEnabled(!GameAudio.SoundEnabled);
            SaveSettings();
            GameAudio.PlayConfirm();
        }

        private static void CycleProfile(int direction)
        {
            GameAudio.SetProfile(AudioProfile.Next(GameAudio.Profile.Name, direction).Name);
            SaveSettings();
        }

        private static void ToggleTitleLanguage()
        {
            var save = ProfileSaveService.Load();
            bool toEnglish = RomContent.TitleArtLanguage == TitleLanguage.Japanese;

            save.TitleLanguage = toEnglish ? "en" : "jp";
            ProfileSaveService.Save(save);
            RomContent.UseTitleLanguage(toEnglish ? TitleLanguage.English : TitleLanguage.Japanese);
            GameAudio.PlayConfirm();
            Console.WriteLine($"[SystemMenu] titulo: {RomContent.TitleArtLanguage}");
        }

        private static void CycleGraphics(int direction)
        {
            GfxVariant.Cycle(direction, reloadScene: false);
        }

        private static void ToggleMsx1Backdrop()
        {
            RomContent.Msx1OpaqueBackdrop = !RomContent.Msx1OpaqueBackdrop;
            var save = ProfileSaveService.Load();
            save.Msx1OpaqueBackdrop = RomContent.Msx1OpaqueBackdrop;
            ProfileSaveService.Save(save);
            GameAudio.PlayConfirm();
            Console.WriteLine($"[SystemMenu] msx1 backdrop: {(RomContent.Msx1OpaqueBackdrop ? "BLACK" : "BORDER")}");
        }

        private static bool IsFullScreen => Nez.Screen.GraphicsDeviceManager?.IsFullScreen == true;

        private static void ToggleFullScreen()
        {
            var game = Nez.Core.Instance as KingsValleyGame;
            game?.SetFullScreen(!IsFullScreen);

            var save = ProfileSaveService.Load();
            save.FullScreen = IsFullScreen;
            ProfileSaveService.Save(save);
            GameAudio.PlayConfirm();
        }

        private static void RestartStage()
        {
            Close();
            if (Nez.Core.Scene is Scenes.GameScene gameScene)
            {
                gameScene.RestartStage();
            }
        }

        private static void QuitToTitle()
        {
            Close();

            if (GameNavigation.StageOverride != null)
            {
                GameNavigation.StageOverride = null;
                GameNavigation.GoTo(GameSceneId.Editor);
                return;
            }

            GameNavigation.GoTo(GameSceneId.MainMenu);
        }

        private static void SaveSettings()
        {
            var save = ProfileSaveService.Load();
            save.AudioProfile = GameAudio.Profile.Name;
            save.MusicEnabled = GameAudio.MusicEnabled;
            save.SfxEnabled = GameAudio.SoundEnabled;
            ProfileSaveService.Save(save);
        }
    }
}
