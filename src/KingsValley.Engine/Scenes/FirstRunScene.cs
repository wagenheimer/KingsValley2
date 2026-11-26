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
    private readonly bool _requestReset;
    private FirstRunView? _view;

    public bool IsExtracting => _view?.IsExtracting ?? false;

    public FirstRunScene(string? initialNotice = null, bool requestReset = false)
    {
        _initialNotice = initialNotice;
        _requestReset=requestReset;
    }

    protected override void OnInitialize()
    {
        GameAudio.StopMusic();
        _view = new FirstRunView(_initialNotice,_requestReset);
        AddEntity(new Entity("first-run")).AddComponent(_view);
    }

    public bool HasDialog => _view?.HasDialog == true;
    public void SelectCartridge(string path)=>_view?.SelectCartridge(path);

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
        private bool _canPlay;
        private readonly bool[] _graphicsReady=new bool[3];
        private readonly bool[] _audioReady=new bool[3];
        private SetupDialog? _dialog;
        private bool _dialogPrimary;
        private bool _cancelRequested;
        private Vector2 _lastMouse=new(-10000,-10000);
        private string? _pickedRom;
        public bool HasDialog=>_dialog!=null;
        private sealed record SetupDialog(string Title,string Message,string Primary,Action? Action,string? Secondary);
        private bool CanExtract=>_selectedRom>=0 && _selectedRom<_scannedRoms.Count && _scannedRoms[_selectedRom].IsSupported;
        private float _displayProgress;


        public bool IsExtracting => _isExtracting;

        public FirstRunView(string? initialNotice,bool requestReset)
        {
            GameAudio.StopMusic();


            RomFolderManager.EnsureDirectories();
            RefreshRomList();

            // Once the game is playable, PLAY is the primary button and takes focus immediately.
            if (_canPlay)
                FocusPlay();

            AudioBootstrap.Completed += OnExtractionCompleted;
            // Automatic boot extraction uses the same progress/cancellation UI as a manual start.
            _isExtracting = AudioBootstrap.IsRunning;
            if(requestReset)ActionPurge();
            else if(!string.IsNullOrWhiteSpace(initialNotice))ShowDialog("ROM SETUP",initialNotice);
            else if(!_canPlay && _scannedRoms.Any(r=>r.IsSupported)){_focus=FocusArea.RomList;}
            if (!_isExtracting && int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_ROM_GUIDE"), out int guidePage))
                ShowRomGuide(Math.Clamp(guidePage, 0, SupportedCartridges.GuidePageCount - 1));
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
            string? selected=_scannedRoms.ElementAtOrDefault(_selectedRom)?.FilePath;
            _scannedRoms.Clear();_scannedRoms.AddRange(RomFolderManager.ScanRoms());
            if(_pickedRom!=null && !_scannedRoms.Any(r=>r.FilePath==_pickedRom) &&
                RomFolderManager.InspectRomFile(_pickedRom) is {} picked)_scannedRoms.Add(picked);
            var distinct=_scannedRoms.GroupBy(r=>r.IsSupported?r.Sha1:r.FilePath,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.OrderByDescending(r=>r.FilePath==_pickedRom)
                    .ThenByDescending(r=>Path.GetDirectoryName(r.FilePath)?.TrimEnd(Path.DirectorySeparatorChar)==AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).First()).ToArray();
            _scannedRoms.Clear();_scannedRoms.AddRange(distinct);
            int previous=_scannedRoms.FindIndex(r=>r.FilePath==selected);
            _selectedRom=previous>=0?previous:Math.Max(0,_scannedRoms.FindIndex(r=>r.IsSupported && r.Flavor==ScannedRomFlavor.Msx2));
            _firstVisibleRom=0;EnsureVisibleRom();
            for(int i=0;i<CartridgeVariants.All.Length;i++)
            {
                string variant=CartridgeVariants.All[i];_graphicsReady[i]=RomContent.IsVariantAvailable(variant);
                _audioReady[i]=AudioBootstrap.HasPackageForVariant(variant);
            }
            _canPlay=RomContent.CanPlayAny;
        }

        // ── update ─────────────────────────────────────────────────────────────────

        void IUpdatable.Update()
        {
            if(UI.SystemMenu.IsOpen)return;
            if(!_isExtracting && AudioBootstrap.IsRunning){_isExtracting=true;_lastExtractionSucceeded=false;}
            if(_noticeTimer>0)_noticeTimer-=Nez.Time.DeltaTime;
            if(_isExtracting)
            {
                _displayProgress=MathHelper.Lerp(_displayProgress,(float)AudioBootstrap.Progress,0.15f);
                if(!AudioBootstrap.IsRunning){FinishExtraction(_lastExtractionSucceeded,_cancelRequested?"EXTRACTION CANCELLED.":null);return;}
            }
            if(_dialog!=null){UpdateDialog();return;}
            if(_isExtracting)
            {
                if(Nez.Input.IsKeyPressed(Keys.Escape))ShowDialog("STOP EXTRACTION?","The current attempt will stop. Any previous complete extraction is kept.","STOP",()=>{_cancelRequested=true;AudioBootstrap.Cancel();},"KEEP GOING",false);
                return;
            }
            if(Nez.Input.IsKeyPressed(Keys.S)){ActionSelectRom();return;}
            if(Nez.Input.IsKeyPressed(Keys.H)){ShowRomGuide(0);return;}
            if(Nez.Input.IsKeyPressed(Keys.O)){ActionOpenFolder();return;}
            if(Nez.Input.IsKeyPressed(Keys.R)){ActionRefresh();return;}
            if(Nez.Input.IsKeyPressed(Keys.Delete) || Nez.Input.IsKeyPressed(Keys.Back)){ActionPurge();return;}
            if(Nez.Input.IsKeyPressed(Keys.P) && _canPlay){LaunchGame();return;}
            bool up=Nez.Input.IsKeyPressed(Keys.Up),down=Nez.Input.IsKeyPressed(Keys.Down);
            bool left=Nez.Input.IsKeyPressed(Keys.Left),right=Nez.Input.IsKeyPressed(Keys.Right);
            bool confirm=Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.Space);
            foreach(var pad in Nez.Input.GamePads)
            {
                if(!pad.IsConnected())continue;
                up|=pad.IsButtonPressed(Buttons.DPadUp) || pad.IsLeftStickUp();
                down|=pad.IsButtonPressed(Buttons.DPadDown) || pad.IsLeftStickDown();
                left|=pad.IsButtonPressed(Buttons.DPadLeft) || pad.IsLeftStickLeft();
                right|=pad.IsButtonPressed(Buttons.DPadRight) || pad.IsLeftStickRight();
                confirm|=pad.IsButtonPressed(Buttons.A);
                if(pad.IsButtonPressed(Buttons.X)){ActionSelectRom();return;}
                if(pad.IsButtonPressed(Buttons.Y)){ActionRefresh();return;}
                if(pad.IsButtonPressed(Buttons.Start)){ShowRomGuide(0);return;}
                if(pad.IsButtonPressed(Buttons.B)){ActivateButton(5);return;}
            }
            if(up)NavigateVertical(-1);if(down)NavigateVertical(1);
            if(left)NavigateHorizontal(-1);if(right)NavigateHorizontal(1);
            var mouse=Wagenheimer.MonoGameHelper.Layout.DesignViewport.ToDesign(Nez.Input.MousePosition);
            bool moved=Vector2.DistanceSquared(mouse,_lastMouse)>1;_lastMouse=mouse;
            if(moved || Nez.Input.LeftMouseButtonPressed)HandleMouse(mouse);
            if(confirm)ActivateSelection();
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
            _dialog=null;
            RomContent.Initialize();
            RefreshRomList();

            if (cancelledNotice != null)
            {
                ShowDialog("EXTRACTION STOPPED",cancelledNotice+" Previous complete data is kept.");
            }
            else if (succeeded && _canPlay)
            {
                ShowDialog("EXTRACTION COMPLETE","The selected cartridge has been extracted. Its data is stored separately from the other version. Choose PLAY to start, or SETUP to extract another cartridge.","PLAY",LaunchGame,"SETUP");
                FocusPlay();
            }
            else if (succeeded)
            {
                // GetPlayableBlocker() is not part of the current API; derive a short reason
                // from the public readiness flags instead of showing a false success message.
                ShowDialog("EXTRACTION INCOMPLETE",PlayableBlocker());
            }
            else
            {
                var attempt=AudioBootstrap.LastGraphicsAttemptDirectory is { } directory
                    ? Extraction.GraphicsExtractionManifest.Read(directory) : null;
                ShowDialog("EXTRACTION FAILED",attempt?.Outcome=="partial"
                    ? "Graphics are incomplete. Open the extraction report for details, then try again."
                    : "The extraction did not complete. Check that your cartridge is supported and try again.");
            }

            // Do not let a stale success flag leak into the next extraction.
            _lastExtractionSucceeded = false;
        }

        private static string PlayableBlocker()
        {
            if (!RomContent.AreStagesAvailable || !RomContent.IsAudioAvailable)
                return "CHOOSE A SUPPORTED ROM, THEN SELECT EXTRACT SELECTED.";
            if (!RomContent.IsMsx1Available && !RomContent.IsMsx2Available)
                return "SELECT AN MSX1 OR MSX2 ROM TO PLAY.";
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
            if(_focus==FocusArea.RomList)
            {
                if(dir>0 && _selectedRom>=_scannedRoms.Count-1){_focus=FocusArea.Buttons;_selectedButton=3;}
                else {_selectedRom=Math.Clamp(_selectedRom+dir,0,Math.Max(0,_scannedRoms.Count-1));EnsureVisibleRom();}
                return;
            }
            bool primary=_selectedButton is 3 or 4;
            if(dir<0 && primary && _scannedRoms.Count>0){_focus=FocusArea.RomList;return;}
            if(dir>0 && primary)_selectedButton=_selectedButton==3?0:5;
            else if(dir<0 && !primary)_selectedButton=_selectedButton<2?3:4;
        }

        private void NavigateHorizontal(int dir)
        {
            if(_focus!=FocusArea.Buttons)return;
            if(_selectedButton is 3 or 4){_selectedButton=_selectedButton==3?4:3;return;}
            int[] order={0,1,2,5};int index=Array.IndexOf(order,_selectedButton);
            _selectedButton=order[Math.Clamp(index+dir,0,order.Length-1)];
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
            if (_isExtracting)
                return;

            int px = (int)mouse.X;
            int py = (int)mouse.Y;
            if (FirstRunLayout.Footer.Contains(px, py) && Nez.Input.LeftMouseButtonPressed)
            {
                ShowRomGuide(0);
                return;
            }
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
                    SetNotice("SELECTED. CHOOSE EXTRACT SELECTED TO CONTINUE.",4f);
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
                _focus=FocusArea.Buttons;_selectedButton=FirstRunLayout.ExtractButtonIndex;
            }
        }

        private void ActivateButton(int index)
        {
            switch (index)
            {
                case 0: ActionSelectRom(); break;
                case 1: ActionOpenFolder(); break;
                case 2: ActionRefresh(); break;
                case 3: if(CanExtract)StartRomExtraction(_scannedRoms[_selectedRom].FilePath); break;
                case FirstRunLayout.PlayButtonIndex:
                    if (_canPlay)
                        LaunchGame();
                    else
                        SetNotice("PLAY LOCKED. " + PlayableBlocker(), 5f);
                    break;
                default: if(_canPlay)LaunchGame();else Nez.Core.Exit(); break;
            }
        }

        /// <summary>Opens the native file dialog and selects a cartridge without starting extraction.</summary>
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

            SelectCartridge(path);
        }

        public void SelectCartridge(string path)
        {
            if(_isExtracting || _dialog!=null)return;
            var info=RomFolderManager.InspectRomFile(path);
            if(info==null){ShowDialog("CANNOT READ CARTRIDGE","Choose a readable .rom, .bin or .zip file.");return;}
            _pickedRom=path;RefreshRomList();_selectedRom=_scannedRoms.FindIndex(r=>r.FilePath==path);EnsureVisibleRom();
            _focus=FocusArea.Buttons;_selectedButton=FirstRunLayout.ExtractButtonIndex;
            if(!info.IsSupported)ShowDialog("UNSUPPORTED CARTRIDGE","This revision has not been verified yet. SHA-1: " + info.Sha1 + ". Choose a supported cartridge. No data has been extracted.");
            else SetNotice("CARTRIDGE SELECTED. CHOOSE EXTRACT SELECTED.",5f);
        }

        private void ActionOpenFolder()
        {
            RomFolderManager.OpenRomsFolder();
            SetNotice("OPENED ROMS FOLDER. DROP YOUR .ROM FILES THERE.", 6f);
        }

        private void ActionRefresh()
        {
            RomContent.Initialize();
            RefreshRomList();
            SetNotice($"REFRESHED: {_scannedRoms.Count} ROM(S) DETECTED.", 4f);
        }

        private void ActionPurge()
        {
            if(_isExtracting)return;
            ShowDialog("RESET EXTRACTED DATA?","This removes extracted graphics and audio for all cartridges. Your original ROM files are kept. You will need to extract again.","RESET DATA",PurgeData,"CANCEL",false);
        }

        private void PurgeData()
        {
            if (_isExtracting) return;

            GameAudio.StopMusic();
            GameAudio.Unload();
            bool purged;
            try { purged = RomFolderManager.PurgeExtractedContent(); }
            catch (Exception ex)
            {
                RomContent.Initialize();RefreshRomList();GameAssets.Unload();
                ShowDialog("RESET INCOMPLETE","Some extracted files could not be removed. Close any application using them and try again. " + ex.Message);
                return;
            }
            RomContent.Initialize();
            RefreshRomList();
            GameAssets.Unload();
            if (!RomContent.CanPlayAny)
            {
                _selectedButton = 0; // BROWSE
                _focus = FocusArea.Buttons;
            }
            if (purged)
                SetNotice("EXTRACTED DATA RESET. SELECT A ROM TO EXTRACT AGAIN.", 5f);
            else
                SetNotice("NO EXTRACTED DATA TO RESET. ROM FILES ARE KEPT.", 4f);
        }

        private void StartRomExtraction(string filePath)
        {
            if (_isExtracting) return;

            _targetRomPath = filePath;
            _cancelRequested=false;
            _displayProgress=0;
            if (!AudioBootstrap.Start(filePath, out string? error))
            {
                _targetRomPath = null;
                ShowDialog("CANNOT START EXTRACTION",error ?? "Choose another supported cartridge and try again.");
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
            if (!RomContent.IsVariantAvailable(RomContent.Variant))
            {
                var playableFlavor = RomContent.GetFirstPlayableFlavor();
                var targetSet = ArtSets.Find(RomContent.Available,
                    playableFlavor == ArtFlavor.Msx2 ? ArtSets.Msx2Id : ArtSets.Msx1Id);

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

            MsxScreen.Text(batcher,FirstRunLayout.HeaderTitle,(256-FirstRunLayout.HeaderTitle.Length*8)/2,FirstRunLayout.Header.Y,TitleColor);

            RenderNotice(batcher);
            RenderStatusBox(batcher);
            RenderButtons(batcher);

            if (_isExtracting)
                RenderExtractionProgress(batcher);
            else
                RenderRomsList(batcher);

            if (_isExtracting)
            {
                DrawCentered(batcher, "[ EXTRACTION IN PROGRESS - SYSTEM LOCKED ]", FirstRunLayout.Footer,
                    FirstRunLayout.Footer.Y + 2, YellowReadyColor);
            }
            else
            {
                for (int i = 0; i < FirstRunLayout.FooterLines.Length; i++)
                {
                    DrawCentered(batcher, FirstRunLayout.FooterLines[i], FirstRunLayout.Footer,
                        FirstRunLayout.Footer.Y + 2 + i * (MsxScreen.MiniLineHeight + 2), HintColor);
                }
            }
            if(_dialog!=null)RenderDialog(batcher);
        }

        private void RenderNotice(Batcher batcher)
        {
            var band=FirstRunLayout.Notice;
            string text=_noticeTimer>0 && _notice!=null?_notice:
                _canPlay?FirstRunLayout.NoticeGameReady:_scannedRoms.Count>0?FirstRunLayout.NoticeRomReady:FirstRunLayout.DefaultNotice;
            DrawCentered(batcher,text,band,band.Y+1,_canPlay?GreenOkColor:Color.White);
            DrawCentered(batcher,string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINGSVALLEY_ROM_DIR"))?"SEARCH: GAME FOLDER / ROMS / USER ROMS":"SEARCH: CUSTOM ROM FOLDER",band,band.Y+10,HintColor);
        }

        private void RenderStatusBox(Batcher batcher)
        {
            var band=FirstRunLayout.Status;
            for(int i=0;i<CartridgeVariants.All.Length;i++)
            {
                bool found=_scannedRoms.Any(r=>r.IsSupported && r.Variant==CartridgeVariants.All[i]);
                string tag=_graphicsReady[i]?(_audioReady[i]?"READY TO PLAY":"AUDIO NOT EXTRACTED"):found?"READY TO EXTRACT":"ADD A ROM";
                int y=band.Y+2+i*FirstRunLayout.StatusTextStep;
                DrawLeft(batcher,FirstRunLayout.StatusLabels[i],FirstRunLayout.StatusLabelColumn,y,Color.White);
                DrawRight(batcher,tag,FirstRunLayout.StatusTagColumn,y,_graphicsReady[i]&&_audioReady[i]?GreenOkColor:found?YellowReadyColor:HintColor);
            }
        }

        private void RenderButtons(Batcher batcher)
        {
            bool playable = _canPlay;
            var rects = FirstRunLayout.GetButtonRects();

            for (int i = 0; i < rects.Count; i++)
            {
                LayoutRect r = rects[i];
                bool isPlay = i == FirstRunLayout.PlayButtonIndex;
                bool isFocused = _focus == FocusArea.Buttons && _selectedButton == i;
                bool enabled = !_isExtracting && (!isPlay || playable) && (i!=3 || CanExtract);
                bool needsExtraction=CanExtract && (!_graphicsReady[Math.Max(0,Array.IndexOf(CartridgeVariants.All,_scannedRoms[_selectedRom].Variant))] || !_audioReady[Math.Max(0,Array.IndexOf(CartridgeVariants.All,_scannedRoms[_selectedRom].Variant))]);
                bool primary=!_isExtracting && (i==0 && !CanExtract && !playable || i==3 && CanExtract && (needsExtraction || !playable) || isPlay && playable && !needsExtraction);

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

                DrawCentered(batcher, i==5 && _canPlay?"BACK":FirstRunLayout.ButtonLabels[i], r, r.Y+(r.Height-5)/2, textColor,
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
                bool isSelected = _selectedRom == index;
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

                string tag = rom.Variant==CartridgeVariants.Contest?"CONTEST":rom.Flavor==ScannedRomFlavor.Msx1?"MSX1":rom.Flavor==ScannedRomFlavor.Msx2?"MSX2":"OTHER";
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

                string source=Path.GetDirectoryName(Path.GetFullPath(rom.FilePath))==Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)?"GAME FOLDER":"ROM FOLDER";
                var revision = SupportedCartridges.All.FirstOrDefault(entry => entry.Sha1.Equals(rom.Sha1, StringComparison.OrdinalIgnoreCase));
                string line2=rom.IsSupported?revision?.Recommendation ?? $"VERIFIED CARTRIDGE - {source}":"NOT SUPPORTED - H: ACCEPTED ROM GUIDE";
                DrawLeft(batcher, line2, item, item.Y + 12,
                    isSelected ? Color.White : HintColor, 8);
            }
        }

        private void RenderEmptyState(Batcher batcher, LayoutRect band)
        {
            var lines = new List<string>();
            foreach (string source in _canPlay?new[]{"YOUR EXTRACTED DATA IS READY.","SELECT PLAY TO START THE GAME.","TO ADD ANOTHER CARTRIDGE, USE BROWSE.","YOUR ROM IS NOT NEEDED AGAIN UNLESS YOU RESET DATA."}:FirstRunLayout.EmptyStateLines)
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
            var band=FirstRunLayout.RomList;
            DrawBox(batcher,band,new Color(0,0,48),SubBorderColor);
            DrawLeft(batcher,_cancelRequested?"STOPPING EXTRACTION...":"EXTRACTING CARTRIDGE",band,band.Y+8,TitleColor,10);
            float progress=Math.Clamp(_displayProgress,0f,1f);
            DrawRight(batcher,$"{(int)(progress*100)}%",band,band.Y+8,GreenOkColor,10);
            var bar=new Rectangle(band.X+10,band.Y+23,band.Width-20,9);
            MsxScreen.Fill(batcher,bar,ButtonNormalColor);
            MsxScreen.Fill(batcher,new Rectangle(bar.X,bar.Y,(int)(bar.Width*progress),bar.Height),TitleColor);
            DrawCentered(batcher,Path.GetFileName(_targetRomPath??"YOUR CARTRIDGE"),band,band.Y+40,Color.White,10);
            DrawWrapped(batcher,AudioBootstrap.Status.ToUpperInvariant(),band,band.Y+51,HintColor,8,3,true);
            DrawCentered(batcher,"ESC: CANCEL EXTRACTION",band,band.Bottom-9,YellowReadyColor);
        }

        // ── text/fill helpers (every string is measured before it is drawn) ────────

        private static Rectangle Rect(LayoutRect r) => new(r.X, r.Y, r.Width, r.Height);

        private void ShowRomGuide(int page)
        {
            bool last = page == SupportedCartridges.GuidePageCount - 1;
            ShowDialog($"ACCEPTED ROMS {page + 1}/{SupportedCartridges.GuidePageCount}",
                SupportedCartridges.GuidePage(page), last ? "START OVER" : "NEXT ROM",
                () => ShowRomGuide(last ? 0 : page + 1), "CLOSE");
        }

        private void ShowDialog(string title,string message,string primary="OK",Action? action=null,string? secondary=null,bool primarySelected=true)
        {
            _dialog=new(title.ToUpperInvariant(),message.ToUpperInvariant(),primary,action,secondary);
            _dialogPrimary=primarySelected;
        }

        private void UpdateDialog()
        {
            if(_dialog==null)return;
            bool confirm=Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.Space);
            bool cancel=Nez.Input.IsKeyPressed(Keys.Escape);
            bool move=Nez.Input.IsKeyPressed(Keys.Left) || Nez.Input.IsKeyPressed(Keys.Right) || Nez.Input.IsKeyPressed(Keys.Tab);
            foreach(var pad in Nez.Input.GamePads)
            {
                if(!pad.IsConnected())continue;
                confirm|=pad.IsButtonPressed(Buttons.A);cancel|=pad.IsButtonPressed(Buttons.B);
                move|=pad.IsButtonPressed(Buttons.DPadLeft) || pad.IsButtonPressed(Buttons.DPadRight);
            }
            if(cancel){_dialog=null;return;}
            if(move && _dialog.Secondary!=null)_dialogPrimary=!_dialogPrimary;
            if(Nez.Input.LeftMouseButtonPressed)
            {
                var mouse=Wagenheimer.MonoGameHelper.Layout.DesignViewport.ToDesign(Nez.Input.MousePosition);
                if(FirstRunLayout.DialogPrimary.Contains((int)mouse.X,(int)mouse.Y)){_dialogPrimary=true;confirm=true;}
                else if(_dialog.Secondary!=null && FirstRunLayout.DialogSecondary.Contains((int)mouse.X,(int)mouse.Y)){_dialogPrimary=false;confirm=true;}
            }
            if(!confirm)return;
            var action=_dialogPrimary?_dialog.Action:null;
            _dialog=null;action?.Invoke();
        }

        private void RenderDialog(Batcher batcher)
        {
            if(_dialog==null)return;
            MsxScreen.Fill(batcher,new Rectangle(0,0,256,212),new Color(0,0,0,210));
            var box=FirstRunLayout.Dialog;
            DrawBox(batcher,box,new Color(12,24,72),TitleColor);
            MsxScreen.Text(batcher,_dialog.Title,128-_dialog.Title.Length*4,box.Y+10,TitleColor);
            DrawWrapped(batcher,_dialog.Message,FirstRunLayout.DialogBody,FirstRunLayout.DialogBody.Y,Color.White,9,8,false);
            void Button(LayoutRect rectangle,string label,bool focused)
            {
                DrawBox(batcher,rectangle,focused?PrimaryFocusFillColor:ButtonNormalColor,focused?TitleColor:SubBorderColor);
                DrawCentered(batcher,label,rectangle,rectangle.Y+5,focused?PrimaryTextColor:Color.White,4);
            }
            Button(FirstRunLayout.DialogPrimary,_dialog.Primary,_dialogPrimary);
            if(_dialog.Secondary!=null)Button(FirstRunLayout.DialogSecondary,_dialog.Secondary,!_dialogPrimary);
        }

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
