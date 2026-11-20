#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using KingsValley.Core.Common;
using KingsValley.Core.SaveSystem;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Nez;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// ROM setup and extraction scene: detects MSX1 and MSX2 cartridges in the local ROMs folder,
/// guides the user to place their cartridges, shows extraction status, and allows launching
/// the game once at least one flavor is ready.
/// </summary>
public class FirstRunScene : SceneBase
{
    private readonly string? _initialNotice;

    public FirstRunScene(string? initialNotice = null)
    {
        _initialNotice = initialNotice;
    }

    protected override void OnInitialize()
    {
        AddEntity(new Entity("first-run")).AddComponent(new FirstRunView(_initialNotice));
    }

    private sealed class FirstRunView : RenderableComponent, IUpdatable
    {
        private static readonly Color BgColor = new(0, 0, 72);
        private static readonly Color BorderColor = new(255, 255, 255);
        private static readonly Color SubBorderColor = new(60, 90, 160);
        private static readonly Color TitleColor = new(255, 224, 96);
        private static readonly Color HighlightColor = new(0, 70, 180);
        private static readonly Color ButtonNormalColor = new(16, 32, 96);
        private static readonly Color HintColor = new(150, 175, 255);
        private static readonly Color GreenOkColor = new(96, 255, 96);
        private static readonly Color YellowReadyColor = new(255, 220, 80);
        private static readonly Color RedMissingColor = new(255, 110, 110);
        private static readonly Color NoticeBgColor = new(100, 20, 20);
        private static readonly Color PrimaryFillColor = new(150, 112, 16);
        private static readonly Color PrimaryFocusFillColor = new(232, 184, 40);
        private static readonly Color PrimaryBorderColor = new(255, 224, 96);
        private static readonly Color PrimaryTextColor = new(24, 16, 0);
        private static readonly Color DisabledFillColor = new(10, 16, 44);
        private static readonly Color DisabledTextColor = new(104, 118, 152);

        private enum FocusArea { Buttons, RomList }

        private FocusArea _focus = FocusArea.Buttons;
        private int _selectedButton; // Stable index: 0 SELECT, 1 FOLDER, 2 REFRESH, 3 PLAY, 4 QUIT.
        private int _selectedRom;
        private int _firstVisibleRom;

        private readonly List<ScannedRomInfo> _scannedRoms = new();
        private string? _notice;
        private float _noticeTimer;
        private volatile bool _isExtracting;
        private volatile bool _lastExtractionSucceeded;
        private string? _targetRomPath;
        private float _autoPlayCountdown = -1f;

        public FirstRunView(string? initialNotice)
        {
            SetNotice(initialNotice ?? string.Empty, 12f);

            RomFolderManager.EnsureDirectories();
            RefreshRomList();

            // Once the game is playable, PLAY is the primary button and takes focus immediately.
            if (RomContent.CanPlayAny)
                FocusPlay();

            AudioBootstrap.Completed += OnExtractionCompleted;
        }

        public override RectangleF Bounds =>
            new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public override void OnAddedToEntity()
        {
            base.OnAddedToEntity();
            RenderLayer = RenderLayers.SystemMenu;
        }

        public override void OnRemovedFromEntity()
        {
            AudioBootstrap.Completed -= OnExtractionCompleted;
            base.OnRemovedFromEntity();
        }

        private void RefreshRomList()
        {
            _scannedRoms.Clear();
            _scannedRoms.AddRange(RomFolderManager.ScanRoms());

            if (_selectedRom >= _scannedRoms.Count)
                _selectedRom = Math.Max(0, _scannedRoms.Count - 1);

            if (_firstVisibleRom > _selectedRom)
                _firstVisibleRom = _selectedRom;
        }

        // ── update ─────────────────────────────────────────────────────────────────

        void IUpdatable.Update()
        {
            if (_noticeTimer > 0)
                _noticeTimer -= Nez.Time.DeltaTime;

            if (_autoPlayCountdown > 0)
            {
                _autoPlayCountdown -= Nez.Time.DeltaTime;
                if (_autoPlayCountdown <= 0)
                {
                    LaunchGame();
                    return;
                }
            }

            if (_isExtracting)
            {
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                {
                    AudioBootstrap.Cancel();
                    FinishExtraction(false, "EXTRACTION CANCELLED.");
                    return;
                }

                // Completed(success) is raised on the worker thread; IsRunning tells us when it is safe
                // to refresh the content. Never infer success from CanPlayAny alone.
                if (!AudioBootstrap.IsRunning)
                    FinishExtraction(_lastExtractionSucceeded, null);

                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.S))
            {
                ActionSelectRom();
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.O))
            {
                ActionOpenFolder();
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.R))
            {
                ActionRefresh();
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.Delete) || Nez.Input.IsKeyPressed(Keys.Back))
            {
                ActionPurge();
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.P) && RomContent.CanPlayAny)
            {
                LaunchGame();
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.Escape))
            {
                if (RomContent.CanPlayAny)
                {
                    LaunchGame();
                    return;
                }

                Nez.Core.Exit();
                return;
            }

            bool up = Nez.Input.IsKeyPressed(Keys.Up);
            bool down = Nez.Input.IsKeyPressed(Keys.Down);
            bool left = Nez.Input.IsKeyPressed(Keys.Left);
            bool right = Nez.Input.IsKeyPressed(Keys.Right);
            bool confirm = Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.Space);

            foreach (var pad in Nez.Input.GamePads)
            {
                if (!pad.IsConnected()) continue;

                if (pad.IsButtonPressed(Buttons.DPadUp) || pad.IsLeftStickUp()) up = true;
                if (pad.IsButtonPressed(Buttons.DPadDown) || pad.IsLeftStickDown()) down = true;
                if (pad.IsButtonPressed(Buttons.DPadLeft) || pad.IsLeftStickLeft()) left = true;
                if (pad.IsButtonPressed(Buttons.DPadRight) || pad.IsLeftStickRight()) right = true;
                if (pad.IsButtonPressed(Buttons.A)) confirm = true;
                if (pad.IsButtonPressed(Buttons.X)) { ActionOpenFolder(); return; }
                if (pad.IsButtonPressed(Buttons.Y)) { ActionRefresh(); return; }
                if (pad.IsButtonPressed(Buttons.B) || pad.IsButtonPressed(Buttons.Back)) { Nez.Core.Exit(); return; }
            }

            if (up) NavigateVertical(-1);
            if (down) NavigateVertical(+1);
            if (left) NavigateHorizontal(-1);
            if (right) NavigateHorizontal(+1);

            Vector2 mouse = Wagenheimer.MonoGameHelper.Layout.DesignViewport.ToDesign(Nez.Input.MousePosition);
            HandleMouse(mouse);

            if (confirm)
                ActivateSelection();
        }

        private void OnExtractionCompleted(bool success)
        {
            // Raised from the extraction worker; only the active extraction owns the result.
            if (_isExtracting)
                _lastExtractionSucceeded = success;
        }

        private void FinishExtraction(bool succeeded, string? cancelledNotice)
        {
            _isExtracting = false;
            RomContent.Initialize();
            RefreshRomList();

            if (cancelledNotice != null)
            {
                SetNotice(cancelledNotice, 5f);
            }
            else if (succeeded && RomContent.CanPlayAny)
            {
                SetNotice("EXTRACTION COMPLETE! READY TO PLAY.", 6f);
                FocusPlay();
            }
            else if (succeeded)
            {
                // GetPlayableBlocker() is not part of the current API; derive a short reason
                // from the public readiness flags instead of showing a false success message.
                SetNotice("EXTRACTION DONE. " + PlayableBlocker(), 6f);
            }
            else
            {
                SetNotice("EXTRACTION DID NOT COMPLETE. TRY AGAIN.", 6f);
            }

            // Do not let a stale success flag leak into the next extraction.
            _lastExtractionSucceeded = false;
        }

        private static string PlayableBlocker()
        {
            if (!RomContent.AreStagesAvailable)
                return "NEED 60 STAGES. EXTRACT WITH MSX1 RC-760.";
            if (!RomContent.IsMsx1Available && !RomContent.IsMsx2Available)
                return "NEED AN MSX1 OR MSX2 ROM TO PLAY.";
            if (!RomContent.IsAudioAvailable)
                return "NEED AUDIO. EXTRACT WITH MSX1 RC-760.";
            return "SETUP IS NOT COMPLETE YET.";
        }

        private void FocusPlay()
        {
            _focus = FocusArea.Buttons;
            _selectedButton = FirstRunLayout.PlayButtonIndex;
        }

        private void SetNotice(string? text, float seconds)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _notice = null;
                _noticeTimer = 0f;
                return;
            }

            _notice = text.Trim().ToUpperInvariant();
            _noticeTimer = seconds;
        }

        private void NavigateVertical(int dir)
        {
            if (_focus == FocusArea.Buttons)
            {
                if (dir > 0 && _scannedRoms.Count > 0)
                {
                    _focus = FocusArea.RomList;
                    _selectedRom = Math.Clamp(_selectedRom, 0, _scannedRoms.Count - 1);
                }
            }
            else
            {
                if (dir < 0 && _selectedRom == 0)
                {
                    _focus = FocusArea.Buttons;
                }
                else
                {
                    _selectedRom = Math.Clamp(_selectedRom + dir, 0, _scannedRoms.Count - 1);
                    EnsureVisibleRom();
                }
            }
        }

        private void NavigateHorizontal(int dir)
        {
            if (_focus == FocusArea.Buttons)
                _selectedButton = Math.Clamp(_selectedButton + dir, 0, FirstRunLayout.ButtonCount - 1);
        }

        private void EnsureVisibleRom()
        {
            if (_selectedRom < _firstVisibleRom)
                _firstVisibleRom = _selectedRom;
            else if (_selectedRom >= _firstVisibleRom + FirstRunLayout.VisibleRoms)
                _firstVisibleRom = _selectedRom - FirstRunLayout.VisibleRoms + 1;
        }

        private void HandleMouse(Vector2 mouse)
        {
            int px = (int)mouse.X;
            int py = (int)mouse.Y;
            var buttonRects = FirstRunLayout.GetButtonRects();

            for (int i = 0; i < buttonRects.Count; i++)
            {
                if (!buttonRects[i].Contains(px, py))
                    continue;

                _focus = FocusArea.Buttons;
                _selectedButton = i;
                if (Nez.Input.LeftMouseButtonPressed)
                    ActivateButton(i);
                return;
            }

            if (_scannedRoms.Count == 0)
                return;

            var itemRects = FirstRunLayout.GetRomItemRects(_scannedRoms.Count - _firstVisibleRom);
            for (int i = 0; i < itemRects.Count; i++)
            {
                if (!itemRects[i].Contains(px, py))
                    continue;

                _focus = FocusArea.RomList;
                _selectedRom = _firstVisibleRom + i;
                if (Nez.Input.LeftMouseButtonPressed)
                    StartRomExtraction(_scannedRoms[_selectedRom].FilePath);
                return;
            }
        }

        private void ActivateSelection()
        {
            if (_focus == FocusArea.Buttons)
            {
                ActivateButton(_selectedButton);
            }
            else if (_focus == FocusArea.RomList && _scannedRoms.Count > 0)
            {
                StartRomExtraction(_scannedRoms[_selectedRom].FilePath);
            }
        }

        private void ActivateButton(int index)
        {
            switch (index)
            {
                case 0: ActionSelectRom(); break;
                case 1: ActionOpenFolder(); break;
                case 2: ActionRefresh(); break;
                case 3: ActionPurge(); break;
                case FirstRunLayout.PlayButtonIndex:
                    if (RomContent.CanPlayAny)
                        LaunchGame();
                    else
                        SetNotice("PLAY LOCKED. " + PlayableBlocker(), 5f);
                    break;
                default: Nez.Core.Exit(); break;
            }
        }

        /// <summary>Opens the native "open file" dialog of the current OS and extracts the chosen cartridge.</summary>
        private void ActionSelectRom()
        {
            if (!Platform.RomPicker.IsAvailable)
            {
                SetNotice("NO FILE DIALOG. USE FOLDER (O) AND COPY THE ROM THERE.", 6f);
                return;
            }

            string? path = Platform.RomPicker.Pick();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetNotice("ROM SELECTION CANCELLED.", 4f);
                return;
            }

            StartRomExtraction(path);
        }

        private void ActionOpenFolder()
        {
            RomFolderManager.OpenRomsFolder();
            SetNotice("OPENED ROMS FOLDER. DROP YOUR .ROM FILES THERE.", 6f);
        }

        private void ActionRefresh()
        {
            RefreshRomList();
            RomContent.Initialize();
            SetNotice($"REFRESHED: {_scannedRoms.Count} ROM(S) DETECTED.", 4f);
        }

        private void ActionPurge()
        {
            if (_isExtracting) return;

            bool purged = RomFolderManager.PurgeExtractedContent();
            RomContent.Initialize();
            RefreshRomList();
            GameAssets.Unload();
            if (purged)
                SetNotice("EXTRACTED DATA RESET. SELECT A ROM TO EXTRACT AGAIN.", 5f);
            else
                SetNotice("NO EXTRACTED DATA TO RESET.", 4f);
        }

        private void StartRomExtraction(string filePath)
        {
            if (_isExtracting) return;

            var scanned = _scannedRoms.Find(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (scanned != null && scanned.Flavor == ScannedRomFlavor.Msx2)
            {
                SetNotice("MSX1 RC-760 IS NEEDED FOR AUDIO AND STAGES.", 6f);
                return;
            }

            _targetRomPath = filePath;
            if (!AudioBootstrap.Start(filePath, out string? error))
            {
                _targetRomPath = null;
                SetNotice(error ?? "CANNOT START EXTRACTION", 5f);
                return;
            }

            _lastExtractionSucceeded = false;
            _isExtracting = true;
            _notice = null;
            _noticeTimer = 0f;
        }

        private void LaunchGame()
        {
            if (!RomContent.CanPlayAny)
                return;

            // Ensure active art set matches what is playable
            if (!RomContent.IsFlavorAvailable(RomContent.Active.Flavor))
            {
                var playableFlavor = RomContent.GetFirstPlayableFlavor();
                var targetSet = ArtSets.Find(RomContent.Available,
                    playableFlavor == ArtFlavor.Msx1 ? ArtSets.Msx1Id : ArtSets.Msx2Id);

                if (targetSet != null)
                {
                    RomContent.Use(targetSet);
                    var save = ProfileSaveService.Load();
                    save.GfxRom = targetSet.Id;
                    ProfileSaveService.Save(save);
                }
            }

            Console.WriteLine($"[FirstRun] Launching game with {RomContent.Active.Name} ({RomContent.Active.Flavor})");
            GameNavigation.GoTo(GameSceneId.MainMenu);
        }

        // ── render ─────────────────────────────────────────────────────────────────

        public override void Render(Batcher batcher, Camera camera)
        {
            var panel = FirstRunLayout.Panel;
            MsxScreen.Fill(batcher, Rect(panel), BgColor);
            MsxScreen.Fill(batcher, new Rectangle(panel.X, panel.Y, panel.Width, 2), BorderColor);
            MsxScreen.Fill(batcher, new Rectangle(panel.X, panel.Bottom - 2, panel.Width, 2), BorderColor);
            MsxScreen.Fill(batcher, new Rectangle(panel.X, panel.Y, 2, panel.Height), BorderColor);
            MsxScreen.Fill(batcher, new Rectangle(panel.Right - 2, panel.Y, 2, panel.Height), BorderColor);

            DrawBox(batcher, FirstRunLayout.Header, BgColor, SubBorderColor);
            DrawCentered(batcher, FirstRunLayout.HeaderTitle, FirstRunLayout.Header,
                FirstRunLayout.Header.Y + 2, TitleColor);

            RenderNotice(batcher);
            RenderStatusBox(batcher);
            RenderButtons(batcher);

            if (_isExtracting)
                RenderExtractionProgress(batcher);
            else
                RenderRomsList(batcher);

            for (int i = 0; i < FirstRunLayout.FooterLines.Length; i++)
            {
                DrawCentered(batcher, FirstRunLayout.FooterLines[i], FirstRunLayout.Footer,
                    FirstRunLayout.Footer.Y + i * MsxScreen.MiniLineHeight, HintColor);
            }
        }

        private void RenderNotice(Batcher batcher)
        {
            var band = FirstRunLayout.Notice;
            if (_noticeTimer <= 0 || string.IsNullOrWhiteSpace(_notice))
            {
                DrawCentered(batcher, FirstRunLayout.DefaultNotice, band, band.Y + 3, HintColor);
                return;
            }

            MsxScreen.Fill(batcher, Rect(band), NoticeBgColor);
            DrawWrapped(batcher, "! " + _notice, band, band.Y, Color.Gold,
                MsxScreen.MiniLineHeight, 2, center: true);
        }

        private void RenderStatusBox(Batcher batcher)
        {
            var band = FirstRunLayout.Status;
            DrawBox(batcher, band, new Color(0, 0, 48), SubBorderColor);

            bool hasMsx1Rom = _scannedRoms.Exists(r => r.Flavor == ScannedRomFlavor.Msx1);
            bool hasMsx2Rom = _scannedRoms.Exists(r => r.Flavor == ScannedRomFlavor.Msx2);

            string msx1Tag = RomContent.IsMsx1Available
                ? "[ INCLUDED / READY ]"
                : hasMsx1Rom ? "[ READY IN FOLDER ]" : "[ NOT EXTRACTED ]";
            Color msx1Color = RomContent.IsMsx1Available
                ? GreenOkColor
                : hasMsx1Rom ? YellowReadyColor : RedMissingColor;

            string msx2Tag = RomContent.IsMsx2Available
                ? "[ INCLUDED / READY ]"
                : hasMsx2Rom ? "[ READY IN FOLDER ]" : "[ NOT EXTRACTED ]";
            Color msx2Color = RomContent.IsMsx2Available
                ? GreenOkColor
                : hasMsx2Rom ? YellowReadyColor : RedMissingColor;

            bool audioReady = RomContent.IsAudioAvailable && RomContent.AreStagesAvailable;
            string audioTag = audioReady
                ? "[ AUDIO + 60 STAGES OK ]"
                : hasMsx1Rom ? "[ READY TO EXTRACT ]" : "[ NEED MSX1 RC-760 ]";
            Color audioColor = audioReady ? GreenOkColor : hasMsx1Rom ? YellowReadyColor : RedMissingColor;

            string[] tags = { msx1Tag, msx2Tag, audioTag };
            Color[] colors = { msx1Color, msx2Color, audioColor };
            for (int i = 0; i < FirstRunLayout.StatusLabels.Length; i++)
            {
                int y = band.Y + 2 + i * FirstRunLayout.StatusTextStep;
                DrawLeft(batcher, FirstRunLayout.StatusLabels[i], FirstRunLayout.StatusLabelColumn, y, Color.White);
                DrawRight(batcher, tags[i], FirstRunLayout.StatusTagColumn, y, colors[i]);
            }
        }

        private void RenderButtons(Batcher batcher)
        {
            bool playable = RomContent.CanPlayAny;
            var rects = FirstRunLayout.GetButtonRects();

            for (int i = 0; i < rects.Count; i++)
            {
                LayoutRect r = rects[i];
                bool isPlay = i == FirstRunLayout.PlayButtonIndex;
                bool isFocused = _focus == FocusArea.Buttons && _selectedButton == i;
                bool enabled = !isPlay || playable;
                bool primary = isPlay && playable;

                Color fill = !enabled ? DisabledFillColor
                    : primary ? (isFocused ? PrimaryFocusFillColor : PrimaryFillColor)
                    : isFocused ? HighlightColor : ButtonNormalColor;
                Color border = !enabled ? SubBorderColor
                    : primary ? PrimaryBorderColor
                    : isFocused ? BorderColor : SubBorderColor;
                Color textColor = !enabled ? DisabledTextColor
                    : primary ? PrimaryTextColor
                    : isFocused ? TitleColor : Color.White;

                MsxScreen.Fill(batcher, Rect(r), fill);
                MsxScreen.Fill(batcher, new Rectangle(r.X, r.Y, r.Width, 1), border);
                MsxScreen.Fill(batcher, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), border);
                MsxScreen.Fill(batcher, new Rectangle(r.X, r.Y, 1, r.Height), border);
                MsxScreen.Fill(batcher, new Rectangle(r.Right - 1, r.Y, 1, r.Height), border);

                // Visible marker that stays on once PLAY becomes available.
                if (primary)
                {
                    MsxScreen.Fill(batcher, new Rectangle(r.X + 3, r.Y + 3, isFocused ? 8 : 5, 2), border);
                    MsxScreen.Fill(batcher, new Rectangle(r.X + 3, r.Bottom - 5, isFocused ? 8 : 5, 2), border);
                }

                DrawCentered(batcher, FirstRunLayout.ButtonLabels[i], r, r.Y + 3, textColor,
                    FirstRunLayout.ButtonPaddingX);
            }
        }

        private void RenderRomsList(Batcher batcher)
        {
            var band = FirstRunLayout.RomList;
            DrawBox(batcher, band, new Color(0, 0, 40), SubBorderColor);

            if (_scannedRoms.Count == 0)
            {
                RenderEmptyState(batcher, band);
                return;
            }

            var header = new LayoutRect(band.X, band.Y, band.Width, FirstRunLayout.RomHeaderHeight);
            DrawLeft(batcher, $"DETECTED CARTRIDGES ({_scannedRoms.Count}):", header,
                band.Y + 2, TitleColor);

            if (_scannedRoms.Count > FirstRunLayout.VisibleRoms)
            {
                int visibleCount = Math.Min(FirstRunLayout.VisibleRoms, _scannedRoms.Count - _firstVisibleRom);
                DrawRight(batcher,
                    $"SHOWING {_firstVisibleRom + 1}..{_firstVisibleRom + visibleCount} OF {_scannedRoms.Count}",
                    header, band.Y + 2, HintColor);
            }

            var itemRects = FirstRunLayout.GetRomItemRects(_scannedRoms.Count - _firstVisibleRom);
            for (int i = 0; i < itemRects.Count; i++)
            {
                int index = _firstVisibleRom + i;
                ScannedRomInfo rom = _scannedRoms[index];
                bool isSelected = _focus == FocusArea.RomList && _selectedRom == index;
                LayoutRect item = itemRects[i];

                MsxScreen.Fill(batcher, Rect(item), isSelected ? HighlightColor : new Color(0, 0, 56));
                MsxScreen.Fill(batcher, new Rectangle(item.X, item.Y, item.Width, 1),
                    isSelected ? TitleColor : SubBorderColor);
                MsxScreen.Fill(batcher, new Rectangle(item.X, item.Bottom - 1, item.Width, 1),
                    isSelected ? TitleColor : SubBorderColor);
                MsxScreen.Fill(batcher, new Rectangle(item.X, item.Y, 1, item.Height),
                    isSelected ? TitleColor : SubBorderColor);
                MsxScreen.Fill(batcher, new Rectangle(item.Right - 1, item.Y, 1, item.Height),
                    isSelected ? TitleColor : SubBorderColor);

                string tag = rom.DisplayTag;
                int tagWidth = MsxScreen.MiniTextWidth(tag);
                var nameBand = new LayoutRect(item.X + 2, item.Y,
                    Math.Max(0, item.Width - 10 - tagWidth), 12);
                string cursor = isSelected ? ">" : " ";
                string name = cursor + " " + Truncate(rom.FileName.ToUpperInvariant(), 22) +
                              $" ({rom.SizeBytes / 1024}K)";
                DrawLeft(batcher, name, nameBand, item.Y + 3,
                    isSelected ? TitleColor : Color.White);
                DrawRight(batcher, tag, item, item.Y + 3,
                    rom.Flavor == ScannedRomFlavor.Msx1 ? GreenOkColor : YellowReadyColor, 4);

                string line2 = rom.Flavor == ScannedRomFlavor.Msx1
                    ? Truncate(rom.Description, 26) + " -> [ENTER: EXTRACT AUDIO]"
                    : rom.Flavor == ScannedRomFlavor.Msx2
                        ? Truncate(rom.Description, 30) + " [MSX2 ART READY]"
                        : Truncate(rom.Description, 32);
                DrawLeft(batcher, line2, item, item.Y + 12,
                    isSelected ? Color.White : HintColor, 8);
            }
        }

        private void RenderEmptyState(Batcher batcher, LayoutRect band)
        {
            var lines = new List<string>();
            foreach (string source in FirstRunLayout.EmptyStateLines)
                lines.AddRange(MsxScreen.MiniTextWrap(source, FirstRunLayout.TextWidth(band)));

            const int step = 9;
            int blockHeight = Math.Max(0, lines.Count * step - (step - MsxScreen.MiniLineHeight));
            int y = band.Y + Math.Max(4, (band.Height - blockHeight) / 2);

            for (int i = 0; i < lines.Count; i++)
            {
                Color color = i == 0 ? TitleColor : i == 1 ? Color.White
                    : lines[i].StartsWith("   ", StringComparison.Ordinal) ? Color.LightGray : HintColor;
                DrawLeft(batcher, lines[i], band, y + i * step, color);
            }
        }

        private void RenderExtractionProgress(Batcher batcher)
        {
            var band = FirstRunLayout.RomList;
            DrawBox(batcher, band, new Color(0, 0, 48), TitleColor);

            DrawCentered(batcher, FirstRunLayout.ExtractionTitle, band, band.Y + 8, TitleColor);

            double progress = AudioBootstrap.Progress;
            var bar = new Rectangle(band.X + 10, band.Y + 22, band.Width - 20, 10);
            MsxScreen.Fill(batcher, bar, new Color(16, 16, 40));
            MsxScreen.Fill(batcher,
                new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(progress, 0, 1)), bar.Height),
                TitleColor);

            DrawRight(batcher, $"{(int)(progress * 100)}%", band, band.Y + 8, GreenOkColor, 10);

            DrawWrapped(batcher, AudioBootstrap.Status, band, band.Y + 36, Color.White,
                MsxScreen.MiniLineHeight, 2, center: true);

            for (int i = 0; i < FirstRunLayout.ExtractionBullets.Length; i++)
            {
                DrawLeft(batcher, FirstRunLayout.ExtractionBullets[i], band,
                    band.Y + 52 + i * 10, HintColor, 10);
            }

            DrawCentered(batcher, FirstRunLayout.ExtractionCancelHint, band, band.Y + 94, RedMissingColor);
        }

        // ── text/fill helpers (every string is measured before it is drawn) ────────

        private static Rectangle Rect(LayoutRect r) => new(r.X, r.Y, r.Width, r.Height);

        private static void DrawBox(Batcher batcher, LayoutRect rect, Color fill, Color border)
        {
            MsxScreen.Fill(batcher, Rect(rect), fill);
            MsxScreen.Fill(batcher, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
            MsxScreen.Fill(batcher, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
            MsxScreen.Fill(batcher, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
            MsxScreen.Fill(batcher, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
        }

        private static void DrawCentered(Batcher batcher, string text, LayoutRect band, int y,
            Color color, int padding = FirstRunLayout.TextPadding)
        {
            string fitted = FirstRunLayout.FitLine(text, Math.Max(0, band.Width - 2 * padding));
            int width = MsxScreen.MiniTextWidth(fitted);
            MsxScreen.MiniText(batcher, fitted, FirstRunLayout.CenterX(band, width, padding), y, color);
        }

        private static void DrawLeft(Batcher batcher, string text, LayoutRect band, int y,
            Color color, int padding = FirstRunLayout.TextPadding)
        {
            string fitted = FirstRunLayout.FitLine(text, Math.Max(0, band.Width - 2 * padding));
            int width = MsxScreen.MiniTextWidth(fitted);
            MsxScreen.MiniText(batcher, fitted, FirstRunLayout.LeftX(band, width, padding), y, color);
        }

        private static void DrawRight(Batcher batcher, string text, LayoutRect band, int y,
            Color color, int padding = FirstRunLayout.TextPadding)
        {
            string fitted = FirstRunLayout.FitLine(text, Math.Max(0, band.Width - 2 * padding));
            int width = MsxScreen.MiniTextWidth(fitted);
            MsxScreen.MiniText(batcher, fitted, FirstRunLayout.RightX(band, width, padding), y, color);
        }

        private static void DrawWrapped(Batcher batcher, string text, LayoutRect band, int startY,
            Color color, int lineStep, int maxLines, bool center)
        {
            var lines = MsxScreen.MiniTextWrap(text, FirstRunLayout.TextWidth(band));
            if (lines.Count > maxLines)
            {
                var trimmed = new List<string>(maxLines);
                for (int i = 0; i < maxLines - 1; i++)
                    trimmed.Add(lines[i]);
                trimmed.Add(FirstRunLayout.FitLine(lines[maxLines - 1],
                    Math.Max(0, FirstRunLayout.TextWidth(band) - 12)) + "...");
                lines = trimmed;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                int y = startY + i * lineStep;
                if (center) DrawCentered(batcher, lines[i], band, y, color);
                else DrawLeft(batcher, lines[i], band, y, color);
            }
        }

        private static string Truncate(string text, int length)
        {
            if (text.Length <= length)
                return text;
            return length <= 3 ? new string('.', length) : text[..(length - 3)] + "...";
        }
    }
}
