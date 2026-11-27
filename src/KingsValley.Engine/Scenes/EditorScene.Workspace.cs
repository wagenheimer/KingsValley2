#nullable enable
using Perf = KingsValley.Engine.Diagnostics.EditorPerformanceLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gum.DataTypes;
using Gum.GueDeriving;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.UI.Gum;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;

namespace KingsValley.Engine.Scenes;

public partial class EditorScene
{
    private static WorkspaceState? _workspaceState;
    private sealed class WorkspaceState
    {
        public string? Path;
        public string Name = "Untitled";
        public string SavedJson = "";
        public EditorHistory History = new();
        public bool FullStage;
        public int RoomZoom;
        public EditorWorldCameraState? WorldCamera;
    }

    private sealed partial class EditorRenderer
    {
        private Workspace? _workspace;
        private bool _renderWorkspaceRoom;
        public override void OnAddedToEntity()
        {
            base.OnAddedToEntity();
            _workspace = new Workspace(this, ((EditorScene)Entity.Scene)._resumeWorkspace ? _workspaceState : null);
        }
        public override void OnRemovedFromEntity()
        {
            _workspace?.Dispose(); _workspace = null;
            foreach (var texture in _roomTextures.Values) texture.Dispose();
            _roomTextures.Clear(); _bgBuilder?.Reset();
            base.OnRemovedFromEntity();
        }

        private sealed partial class Workspace : IDisposable
        {
            private readonly EditorRenderer _editor;
            private readonly WorkspaceState _state;
            private readonly RenderTarget2D _preview;
            private readonly Batcher _batcher;
            private ContainerRuntime? _root;
            private readonly List<(Rectangle Bounds, Action Action, RectangleRuntime Background, bool Selected, bool Enabled, Color Normal, string Name)> _buttons = new();
            private readonly Dictionary<string, string> _toolNames = new();
            private EditorWorkspaceLayout _layout = null!;
            private Point _size;
            private int _uiScale = 1;
            private bool _overview;
            private bool _worldDirty = true;
            private readonly Dictionary<int, RenderTarget2D> _worldPreviews = new();
            private readonly Dictionary<int, Rectangle> _worldBounds = new();
            private Point Pointer
            {
                get
                {
                    var bounds=Nez.Core.Instance.Window.ClientBounds;
                    var buffer=Nez.Core.GraphicsDevice.PresentationParameters;
                    return EditorWorkspaceLayout.ToUiPoint(_mouse.Position,new(bounds.Width,bounds.Height),new(buffer.BackBufferWidth,buffer.BackBufferHeight));
                }
            }
            private MouseState _mouse, _previousMouse;
            private KeyboardState _keys, _previousKeys;
            private Point? _hoverCell;
            private int _category, _mode = 1, _brush = 1, _zoom, _enemyLevel = 1, _mechanismHeight = 3;
            private bool _rebuild = true;
            private string? _stroke;
            private readonly HashSet<(int Room,Point Cell)> _painted = new();
            private Rectangle? _selection;
            private Point? _selectionStart;
            private Point _pasteAt;
            private ClipboardRegion? _clipboard;
            private float _recoveryTimer;
            private string _status = "Choose a tool, then paint in the room.";
            private string? _modal;
            private string _message = "";
            private Action? _pending;
            private int _templateSource;
            private bool _slotSave;
            private int _ignoreFrames = 2;
            private static readonly Color Background = new(10, 16, 28), Panel = new(20, 29, 45), Border = new(58, 78, 108), TextColor = new(231, 237, 246), Muted = new(166, 185, 210), Gold = new(255, 211, 78);

            public Workspace(EditorRenderer editor, WorkspaceState? state)
            {
                _editor = editor;
                _category=Math.Max(0,Array.IndexOf(new[]{"terrain","objects","enemies","tools","mechanisms"},Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_CATEGORY")));
                Perf.Event("workspace","created",$"variant={RomContent.Variant} rooms={editor._rooms.Length}",new { width=Nez.Core.GraphicsDevice.PresentationParameters.BackBufferWidth,height=Nez.Core.GraphicsDevice.PresentationParameters.BackBufferHeight,adapter=Nez.Core.GraphicsDevice.Adapter.Description,presentation=Nez.Core.GraphicsDevice.PresentationParameters.PresentationInterval.ToString(),fixedTimeStep=Nez.Core.Instance.IsFixedTimeStep,targetFrameMs=Nez.Core.Instance.TargetElapsedTime.TotalMilliseconds });
                _overview=Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_OVERVIEW")=="1";
                _state = state ?? new WorkspaceState { SavedJson = editor.BuildJson() };
                if(state!=null)
                {
                    _overview=state.FullStage;_zoom=state.RoomZoom;
                    if(state.WorldCamera is {} pose){_worldCamera.Restore(pose);_worldFitPending=false;}
                }
                _preview = new RenderTarget2D(Nez.Core.GraphicsDevice, 256, 192, false, SurfaceFormat.Color, DepthFormat.None);
                _batcher = new Batcher(Nez.Core.GraphicsDevice);
                _mouse = _previousMouse = Mouse.GetState(); _keys = _previousKeys = Keyboard.GetState();
                if (Nez.Core.GraphicsDevice.PresentationParameters.BackBufferWidth < 800 || Nez.Core.GraphicsDevice.PresentationParameters.BackBufferHeight < 720)
                    Nez.Screen.SetSize(1024, 768);
                if (state == null && File.Exists(RecoveryPath)) _modal = "recovery";
                string? diagnostic = Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_DIALOG");
                if (diagnostic is "templates" or "slots" or "properties") _modal = diagnostic;
                string? source = Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_TEMPLATE_VARIANT") ?? RomContent.Variant;
                if (source != null)
                {
                    var sources = EditorTemplateCatalog.Available();
                    _templateSource = Math.Max(0, sources.ToList().FindIndex(s => s.Variant == source));
                    if (state==null && int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_TEMPLATE_STAGE"), out int number) && sources.Count > 0 && number >= 1 && number <= sources[_templateSource].StageCount)
                        LoadTemplate(sources[_templateSource], number);
                }
                if(state!=null)ValidateWorldProbeReturn();
            }
            private bool _dirty;
            private bool Dirty => _dirty;
            private static string RecoveryPath => Path.Combine(RomContent.CustomLevelsDirectory, "Recovery", "editor-recovery.kv2level");
            private static string[] ProtectedRoots => new[] { "msx1", "msx2", "msx2-contest" }
                .Select(variant => AudioBootstrap.GraphicsRootForVariant(variant))
                .Concat(new[] { Path.Combine(RomContent.ContentDirectory, "Rom"), Path.Combine(RomContent.ContentDirectory, "RomMsx1"), Path.Combine(RomContent.ContentDirectory, "RomContest") }).ToArray();

            public void Update()
            {
                using var timing=Perf.Measure("editor.update_preview");
                long started=System.Diagnostics.Stopwatch.GetTimestamp();
                try { UpdateCore(); }
                finally
                {
                    if (Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_PROFILE")=="1")
                    {
                        double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        _profileTotal+=elapsed;_profileMaximum=Math.Max(_profileMaximum,elapsed);
                        if(++_profileFrames==120){Console.WriteLine($"[Editor performance] update/render avg={_profileTotal/120:F2}ms max={_profileMaximum:F2}ms rooms={_editor._roomTextures.Count}");_profileTotal=0;_profileMaximum=0;_profileFrames=0;}
                    }
                }
            }
            private int _profileFrames;
            private double _profileTotal,_profileMaximum;
            private readonly bool _rebuildProbe=Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_REBUILD_PROBE")=="1";
            private int _probeFrame;
            private void UpdateCore()
            {
                if (_rebuildProbe && ++_probeFrame==180)
                {
                    Perf.Event("probe","ui.rebuild_same_document");
                    _rebuild=true;
                }
                var device = Nez.Core.GraphicsDevice;
                var size = new Point(device.PresentationParameters.BackBufferWidth, device.PresentationParameters.BackBufferHeight);
                int scale=EditorWorkspaceLayout.UiScale(size.X,size.Y);
                size=new Point(size.X/scale,size.Y/scale);
                if (_size != size || _uiScale != scale) { _size = size; _uiScale=scale; _rebuild = true; }
                if (_editor._bgBuilder?.TickCustomAnimations(_editor.Pyramid,Nez.Time.DeltaTime)==true)
                {
                    if (_overview) foreach(int room in _worldBounds.Keys) _editor.UpdateRoomTexture(room);
                    else _editor.UpdateRoomTexture(_editor.RoomNumber);
                    _worldDirty=true;
                }
                if (_rebuild) Rebuild();
                _previousMouse = _mouse; _mouse = Mouse.GetState(); _previousKeys = _keys; _keys = Keyboard.GetState();
                var oldHover=_hoverCell;int oldHoverRoom=_hoverRoom;
                if(_overview)
                {
                    var hit=_worldCamera.Hit(Pointer,_editor._stage.RoomGrid);_hoverCell=hit?.Cell;_hoverRoom=hit?.Room??0;
                    if(oldHover!=_hoverCell||oldHoverRoom!=_hoverRoom)_worldDirty=true;
                }
                else {_hoverCell=_layout.CellAt(Pointer);_hoverRoom=_editor.RoomNumber;}
                if(_mouse.LeftButton==ButtonState.Pressed && _previousMouse.LeftButton!=ButtonState.Pressed && new Rectangle(0,0,_size.X,_size.Y).Contains(Pointer))
                    Perf.Event("raw_input","left_button_down",$"pointer={Pointer} active={Nez.Core.Instance.IsActive} ignoreFrames={_ignoreFrames}");
                bool ignore = Environment.GetEnvironmentVariable("KINGSVALLEY_IGNORE_INPUT") == "1" || Nez.Core.Instance is { IsActive: false } || _ignoreFrames-- > 0;
                if (!ignore)
                {
                    bool click = _mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton != ButtonState.Pressed;
                    foreach (var button in _buttons)
                        button.Background.FillColor = !button.Enabled ? new Color(23, 30, 42) : button.Bounds.Contains(Pointer) ? Color.Lerp(button.Normal,Color.White,0.12f) : button.Normal;
                    if (click)
                    {
                        var button = _buttons.LastOrDefault(b => b.Enabled && b.Bounds.Contains(Pointer));
                        if (button.Action != null)
                        {
                            Perf.Input(button.Name,$"stage={_editor._currentStageNumber} room={_editor.RoomNumber} overview={_overview} pointer={Pointer}");
                            using var actionTiming=Perf.Measure("input.action",button.Name);
                            Try(button.Action); if(_rebuild)Rebuild(); RenderPreview(); return;
                        }
                    }
                    if (_modal != null)
                    {
                        if (Pressed(Keys.Escape)) { _modal = null; _pending = null; _rebuild = true; }
                    }
                    else if(_overview) Try(UpdateWorldInput);
                    else Try(UpdateCanvasInput);
                    if (_modal == null && _layout.Canvas.Contains(Pointer) && _mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue) Zoom(_mouse.ScrollWheelValue > _previousMouse.ScrollWheelValue);
                }
                _recoveryTimer += Nez.Time.DeltaTime;
                if (_recoveryTimer > 15)
                {
                    _recoveryTimer = 0;
                    if (Dirty) Try(() => Document().Save(RecoveryPath, ProtectedRoots));
                }
                if (_rebuild) Rebuild();
                Try(RunWorldEditProbe);
                RenderPreview();
            }

            private bool Pressed(Keys key) => _keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
            private bool Control => _keys.IsKeyDown(Keys.LeftControl) || _keys.IsKeyDown(Keys.RightControl) || _keys.IsKeyDown(Keys.LeftWindows) || _keys.IsKeyDown(Keys.RightWindows);
            private bool Shift => _keys.IsKeyDown(Keys.LeftShift) || _keys.IsKeyDown(Keys.RightShift);
            private void Try(Action action)
            {
                try { action(); }
                catch (Exception ex) when (IsRecoverableEditorAction(ex))
                { Perf.Event("error","editor.action",ex.Message,new {exception=ex.ToString()}); _message = ex.Message; _modal = "error"; _rebuild = true; Console.WriteLine("[Editor] " + ex.Message); }
            }
            private LevelDocument Document() => new(_state.Name, RomContent.Variant, _editor.BuildJson());
            private void Change(Action action)
            {
                string before = _editor.BuildJson(); action(); string after = _editor.BuildJson();
                _state.History.Record(before, after); InvalidateWorld(); _rebuild = true;
            }
            private void RenderPreview()
            {
                using var timing=Perf.Measure("preview.render",_overview?"overview":"room");
                var device = Nez.Core.GraphicsDevice; var previous = device.GetRenderTargets();
                try
                {
                    if (_overview)
                    {
                        if (!_worldDirty) return;
                        int previousRoom=_editor._room;
                        try { foreach (var entry in _worldBounds)
                        {
                            if(_worldPreviewValid.Contains(entry.Key))continue;
                            _editor._room=entry.Key-1;
                            if (!_worldPreviews.TryGetValue(entry.Key,out var target)) _worldPreviews[entry.Key]=target=new RenderTarget2D(device,256,192);
                            DrawPreview(target);_worldPreviewValid.Add(entry.Key);
                        } } finally { _editor._room=previousRoom; }
                        ComposeWorld();_worldDirty=false; return;
                    }
                    DrawPreview(_preview);
                }
                finally { _editor._renderWorkspaceRoom = false; device.SetRenderTargets(previous); }
            }
            private void DrawPreview(RenderTarget2D target)
            {
                    var device=Nez.Core.GraphicsDevice;
                    device.SetRenderTarget(target); device.Clear(Color.Black);
                    _batcher.Begin(BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                    _editor._renderWorkspaceRoom = true;
                    _editor.Render(_batcher, _editor.Entity.Scene.Camera);
                    _batcher.End();
            }
            public void DrawCanvasOverlay(Batcher batcher)
            {
                if (_overview) return;
                if (_selection is { } selection) Outline(batcher, new Rectangle(selection.X * 8, selection.Y * 8, selection.Width * 8, selection.Height * 8), Gold);
                if (_hoverCell is { } cell && _modal == null) Outline(batcher, new Rectangle(cell.X * 8, cell.Y * 8, 8, 8), Color.White);
            }
            public void RememberForTest()
            {
                FinishStroke();_state.FullStage=_overview;_state.RoomZoom=_zoom;
                _state.WorldCamera=_worldCamera.Initialized?_worldCamera.Capture():null;
                _workspaceState = _state; Try(() => Document().Save(RecoveryPath, ProtectedRoots));
            }
            public void Dispose() { _root?.RemoveFromRoot(); _preview.Dispose(); _worldCanvas?.Dispose(); foreach(var target in _worldPreviews.Values)target.Dispose(); _batcher.Dispose(); }

            private void Rebuild()
            {
                using var timing=Perf.Measure("ui.rebuild",$"size={_size} category={_category} overview={_overview}");
                _dirty = _state.SavedJson != _editor.BuildJson();
                _rebuild = false; _worldDirty=true;
                using(Perf.Measure("ui.detach_root")) _root?.RemoveFromRoot();
                _buttons.Clear();
                _layout = EditorWorkspaceLayout.Calculate(_size.X, _size.Y, _zoom);
                _root = new ContainerRuntime { Width = _size.X*_uiScale, Height = _size.Y*_uiScale, WidthUnits = DimensionUnitType.Absolute, HeightUnits = DimensionUnitType.Absolute };
                using(Perf.Measure("ui.attach_root")) _root.AddToRoot();
                Box(new(0, 0, _size.X, _size.Y), Background);
                Box(new(0,0,_size.X,32),new Color(22,32,49));
                Box(new(0,32,_size.X,46),new Color(16,24,38));
                Box(new(0,77,_size.X,1),Border);
                Label("KING'S VALLEY II", new(16, 4, 240, 28), 21, Gold);
                Label("LEVEL EDITOR", new(_size.X/2-120, 4, 240, 28), 17, centered:true);
                Label(RomContent.Active.Name, new(_size.X - 205, 5, 185, 24), 14, Muted);
                int x = 16;
                foreach (var entry in new (string Text, int Width, Action Action)[]
                {
                    ("New",66,()=>Guard(NewDocument)), ("Open",72,()=>Guard(OpenExternal)), ("Save",68,()=>SaveExternal(false)),
                    ("Save As...",94,()=>SaveExternal(true)), ("From Game...",112,()=>Guard(()=>{_modal="templates";_rebuild=true;})),
                    ("Slots",64,()=>{_modal="slots";_slotSave=false;_rebuild=true;})
                }) { if(entry.Text=="From Game...")x+=12; Button(entry.Text, new(x, 39, entry.Width, 32), entry.Action); x += entry.Width + 6; }
                Box(new(342,42,1,26),Border);
                Box(new(541,42,1,26),Border);
                Button("Undo", new(x + 8, 39, 64, 32), Undo, enabled: _state.History.CanUndo);
                Button("Redo", new(x + 78, 39, 64, 32), Redo, enabled: _state.History.CanRedo);
                Button("Test", new(_size.X - 112, 39, 96, 32), Test, color: new Color(25, 111, 57));
                Box(new(0, 80, _size.X, 24), Panel);
                Label((_state.Name + (Dirty ? "  *" : "")) + "  /  " + (Dirty ? "Unsaved changes" : "Ready"), new(16, 80, _size.X - 130, 24), 14, Dirty ? Gold : Muted);
                Button("Back", new(_size.X - 82, 79, 66, 24), ()=>Guard(()=>{DeleteRecovery();GameNavigation.GoTo(GameSceneId.MainMenu);}));
                int paletteRows=(Options().Length+1)/2;
                int modeY=292+(_category==2?Options().Length*48:paletteRows*68)+36;
                Frame(new(6,110,_layout.Left.Width-12,_size.Y-152),Panel);
                SectionHeader("TOOLBOX",new(7,111,_layout.Left.Width-14,28));
                Frame(new(_layout.Canvas.X-8,146,_layout.Canvas.Width+16,_size.Y-188),new Color(7,12,21));
                Box(new(_layout.Canvas.X-8,104,_layout.Canvas.Width+16,38),new Color(22,32,49));
                Box(new(_layout.Canvas.X-8,141,_layout.Canvas.Width+16,1),Border);
                if(_layout.Right.Width>0)Frame(new(_layout.Right.X+6,110,_layout.Right.Width-12,_size.Y-152),Panel);
                string[] categories = { "Terrain", "Objects", "Enemies", "Tools", "Mechanisms" };
                for (int i = 0; i < categories.Length; i++)
                {
                    int category = i, columnWidth=(_layout.Left.Width-34)/2;
                    var bounds=i==4?new Rectangle(14,220,_layout.Left.Width-28,30):new Rectangle(14+i%2*(columnWidth+6),148+i/2*36,columnWidth,30);
                    Button(categories[i],bounds,()=>{_category=category;_rebuild=true;},_category==i);
                }
                SectionHeader("TILE PALETTE",new(7,258,_layout.Left.Width-14,24));
                DrawPalette();
                SectionHeader("PAINT MODE",new(7,modeY-28,_layout.Left.Width-14,24));
                int modeW = (_layout.Left.Width - 36) / 3;
                for (int i = 0; i < 3; i++)
                {
                    int mode = i; Button(new[] { "Select", "Brush", "Fill" }[i], new(12 + i * (modeW + 6), modeY, modeW, 32), ()=>{_mode=mode;_rebuild=true;}, _mode == i);
                }
                if(_size.Y-modeY>180)
                {
                    Box(new(18,_size.Y-127,_layout.Left.Width-36,1),Border);
                    Label("B Brush   F Fill",new(18,_size.Y-116,_layout.Left.Width-36,20),11,Muted);
                    Label("V Select   E Erase",new(18,_size.Y-94,_layout.Left.Width-36,20),11,Muted);
                    Label("G Grid   F5 Test",new(18,_size.Y-72,_layout.Left.Width-36,20),11,Muted);
                }
                Label(_overview ? $"WORLD / {_editor._rooms.Length} ROOMS" : $"ROOM {_editor.RoomNumber:00}", new(_layout.Canvas.X + 5, 110, 160, 26), 16, Muted);
                if (_overview) DrawWorld();
                else
                {
                Box(new(_layout.Room.X-1,_layout.Room.Y-1,_layout.Room.Width+2,_layout.Room.Height+2),Border);
                for(int col=0;col<=32;col+=4) Label(col.ToString(),new(_layout.Room.X+col*8*_layout.Zoom-5,_layout.Room.Y-21,30,18),12,Muted);
                for(int row=0;row<=24;row+=4) Label(row.ToString(),new(_layout.Room.X-24,_layout.Room.Y+row*8*_layout.Zoom-6,24,18),12,Muted);
                var sprite = new SpriteRuntime { Texture = _preview, TextureWidth = 256, TextureHeight = 192 };
                Position(sprite, _layout.Room); _root.AddChild(sprite);
                }
                int zoomY = 108, zoomX = _layout.Canvas.Right - 274;
                Button("-", new(zoomX, zoomY, 42, 30), ()=>Zoom(false));
                if(_overview) Button($"{_worldCamera.Scale*100:0}% Fit",new(zoomX+45,zoomY,82,30),FitWorld,diagnosticName:"Fit stage");
                else Label($"{_layout.Zoom * 100}%", new(zoomX + 45, zoomY, 82, 30), 15, Muted, centered:true);
                Button("+", new(zoomX + 128, zoomY, 42, 30), ()=>Zoom(true));
                Button(_overview ? "Room view" : "Full stage", new(_layout.Canvas.X+164,108,100,30), ToggleWorld);
                Button(_editor._showGrid ? "Grid: On" : "Grid: Off", new(_layout.Canvas.Right - 92,108,90,30), ()=>{_editor._showGrid=!_editor._showGrid;InvalidateWorld();_rebuild=true;});
                if (_layout.Right.Width > 0) DrawProperties(_layout.Right);
                else Button("Properties", new(14, modeY + 40, _layout.Left.Width - 28, 32), ()=>{_modal="properties";_rebuild=true;});
                Box(new(0, _size.Y - 34, _size.X, 34), Panel);
                Label(_status, new(16, _size.Y - 28, _size.X - 135, 26), 15, Muted);
                Label("256 x 192", new(_size.X - 115, _size.Y - 28, 105, 26), 15, Muted);
                if (_modal != null) DrawModal();
            }

            private void DrawPalette()
            {
                var options = Options(); int width = (_layout.Left.Width - 40) / 2;
                if(_category==2)
                {
                    for(int i=0;i<options.Length;i++)
                    {
                        var option=options[i];var rect=new Rectangle(14,292+i*48,_layout.Left.Width-28,42);
                        bool selected=!_editor._isEraser&&_editor._tool==4&&_editor._sub[4]==option.Sub;
                        Button("",rect,()=>{_editor._isEraser=false;_editor._tool=4;_editor._sub[4]=option.Sub;_mode=1;_rebuild=true;},selected,diagnosticName:"tool."+option.Name);
                        Icon(option.Icon,new(rect.X+5,rect.Y+5,32,32));
                        Label(option.Name,new(rect.X+44,rect.Y+4,rect.Width-50,34),13,selected?Gold:TextColor);
                    }
                    return;
                }
                int step = 68;
                for (int i = 0; i < options.Length; i++)
                {
                    var option = options[i]; var rect = new Rectangle(14 + i % 2 * (width + 10), 292 + i / 2 * step, width, step-6);
                    bool selected = _editor._isEraser ? option.Tool == -1 : option.Tool == _editor._tool && (option.Sub < 0 || option.Sub == _editor._sub[option.Tool]);
                    Button("", rect, ()=>{_editor._isEraser=option.Tool<0;if(option.Tool>=0){_editor._tool=option.Tool;if(option.Sub>=0)_editor._sub[option.Tool]=option.Sub;}_mode=1;_rebuild=true;}, selected,diagnosticName:"tool."+option.Name);
                    Icon(option.Icon, new(rect.X + rect.Width / 2 - 13, rect.Y + 4, 26, Math.Min(28,step-30)));
                    Label(option.Name, new(rect.X + 5, rect.Bottom - 25, rect.Width - 10, 22), 12, selected ? Gold : TextColor, centered:true);
                }
            }
            private (string Name, int Tool, int Sub, string Icon)[] Options()
            {
                int p = _editor.Pyramid;
                string solid = RomContent.Flavor == ArtFlavor.Msx1 ? $"wall_p{p}_0" : $"soft_p{p}_0";
                string soft = RomContent.Flavor == ArtFlavor.Msx1 ? $"soft_p{p}_0" : $"wall_p{p}_0";
                return _category switch
                {
                    // The MSX2 legacy atlas names invert solid and diggable blocks.
                    0 => new[] { ("Solid wall",1,-1,solid), ("Soft block",0,-1,soft), ("Ladder",2,-1,$"ladder_p{p}"), ("Eraser",-1,-1,"") },
                    1 => new[] { ("Player",3,-1,"vick_s0_00"), ("Exit door",8,-1,$"door_f0_p{p}"), ("Soul stone",7,-1,$"soulstone_p{p}") },
                    2 => new[] { ("Mummy",4,0,EditorEnemySprite(EnemyKind.Mummy)), ("Gold mummy",4,1,EditorEnemySprite(EnemyKind.GoldMummy)), ("Guard",4,2,EditorEnemySprite(EnemyKind.Guard)), ("Rock",4,3,EditorEnemySprite(EnemyKind.Rock)) },
                    3 => Enumerable.Range(0,6).Select(i=>(new[]{"Knife","Boomerang","Shovel","Pickaxe","Hammer","Drill"}[i],6,i,ItemSprite((ItemKind)(i+1)))).ToArray(),
                    _ => new[] { ("Door L/R",5,0,$"door_stud_p{p}"), ("Door R/L",5,1,$"door_stud_p{p}"), ("Trap L/R",5,2,$"ladder_p{p}"), ("Trap R/L",5,3,$"ladder_p{p}"), ("Falling wall",5,4,$"wall_p{p}_0"), ("Floor trap",5,5,$"soft_p{p}_0"), ("Push block",5,6,$"movable_block_p{p}"), ("Temp. ladder",5,7,$"ladder_p{p}") }
                };
            }
            private void DrawProperties(Rectangle rect)
            {
                int x = rect.X + 18, width = rect.Width - 36;
                SectionHeader("INSPECTOR",new(rect.X+7,rect.Y+7,rect.Width-14,28));
                string toolName = _editor._isEraser ? "Eraser" : Options().FirstOrDefault(o => o.Tool == _editor._tool && (o.Sub < 0 || o.Sub == _editor._sub[o.Tool])).Name ?? Legend[_editor._tool];
                Frame(new(rect.X+14,rect.Y+43,rect.Width-28,34),new Color(42,39,29));
                Label(toolName,new(x+8,rect.Y+45,width-16,30),17,Gold,centered:true);
                SectionHeader("SETTINGS",new(rect.X+7,rect.Y+82,rect.Width-14,24));
                Button("<", new(x, rect.Y + 108, 34, 30), ()=>Change(()=>SetPyramid((_editor.Pyramid+4)%6+1)));
                Box(new(x+39,rect.Y+108,width-78,30),new Color(11,18,29));
                Label($"Pyramid {_editor.Pyramid}", new(x + 43, rect.Y + 110, width - 86, 24), 16, centered:true);
                Button(">", new(rect.Right - 52, rect.Y + 108, 34, 30), ()=>Change(()=>SetPyramid(_editor.Pyramid%6+1)));
                Label("Brush size", new(x, rect.Y + 151, width, 23), 16, Muted);
                Button($"{_brush} x {_brush}", new(x + 112, rect.Y + 146, width - 112, 30), ()=>{_brush=_brush==1?2:_brush==2?4:1;_rebuild=true;});
                Label("Music", new(x, rect.Y + 187, 84, 23), 16, Muted);
                Button($"Track {_editor._editorMusic+1}", new(x + 90, rect.Y + 182, width - 90, 30), ()=>Change(()=>_editor._editorMusic=(_editor._editorMusic+1)%5));
                if (_category == 2)
                    Button($"Enemy level: {_enemyLevel}", new(x,rect.Y+220,width,30),()=>{_enemyLevel=_enemyLevel%3+1;_rebuild=true;});
                else if (_category == 4)
                    Button($"Height: {_mechanismHeight} cells",new(x,rect.Y+220,width,30),()=>{_mechanismHeight=_mechanismHeight%12+1;_rebuild=true;});
                else Label("Snap to grid: 8 px", new(x, rect.Y + 220, width, 25), 15, Muted);
                SectionHeader("ROOM MAP",new(rect.X+7,rect.Y+258,rect.Width-14,24));
                int mapY = rect.Y + 288;
                int cell = Math.Min(26, Math.Min(width / 8,Math.Max(14,(rect.Bottom-mapY-92)/8)));
                for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
                {
                    int gx = col, gy = row, id = _editor._stage.RoomGrid[row, col];
                    Button("", new(x + col * cell, mapY + row * cell, cell - 2, cell - 2), ()=>
                    {
                        if (id > 0) { FinishStroke();SelectWorldRoom(id,center:true);_selection=null;_rebuild=true; }
                        else Change(()=>{_editor.AddRoomAt(gx,gy);if(_overview){_worldCamera.CenterRoom(_editor.RoomNumber,_editor._stage.RoomGrid);RefreshWorldBounds();}});
                    }, id == _editor.RoomNumber,diagnosticName:$"room_map.{gx},{gy}.room_{id}");
                    if (id > 0) Label(id.ToString("00"), new(x + col * cell + 3, mapY + row * cell + 3, cell - 5, cell - 4), cell < 22 ? 9 : 11, id == _editor.RoomNumber ? Gold : TextColor);
                }
                Label("Empty cell: add a room", new(x, mapY + cell * 8 + 8, width, 42), 13, Muted);
                Button("Copy", new(x, mapY + cell * 8 + 51, width / 2 - 4, 30), CopySelection, enabled: _selection != null);
                Button("Paste", new(x + width / 2 + 4, mapY + cell * 8 + 51, width / 2 - 4, 30), ()=>Change(Paste), enabled: _clipboard != null);
            }

            private void Frame(Rectangle rect,Color fill)
            {
                Box(rect,Border);Box(new(rect.X+1,rect.Y+1,rect.Width-2,rect.Height-2),fill);
            }
            private void SectionHeader(string title,Rectangle rect)
            {
                Box(rect,new Color(29,43,63));
                Box(new(rect.X,rect.Bottom-1,rect.Width,1),Border);
                Label(title,new(rect.X+10,rect.Y+2,rect.Width-20,rect.Height-4),13,new Color(197,215,239));
            }

            private void Box(Rectangle rect, Color color)
            {
                var box = new RectangleRuntime { IsFilled = true, StrokeWidth = 0, FillColor = color }; Position(box, rect); _root!.AddChild(box);
            }
            private void Label(string value, Rectangle rect, int size = 18, Color? color = null, bool centered = false)
            {
                using var timing=Perf.Measure("ui.create_text",$"fontSize={size} characters={value.Length}");
                var label = new TextRuntime { Text = value, FontFamily = "Fonts/NotoSansKR-Regular.ttf", FontSize = size*_uiScale, Color = color ?? TextColor, HorizontalAlignment = centered ? RenderingLibrary.Graphics.HorizontalAlignment.Center : RenderingLibrary.Graphics.HorizontalAlignment.Left, VerticalAlignment = RenderingLibrary.Graphics.VerticalAlignment.Center };
                Position(label, rect); _root!.AddChild(label);
            }
            private void Button(string title, Rectangle rect, Action action, bool selected = false, bool enabled = true, Color? color = null, string? diagnosticName = null)
            {
                Box(rect, selected ? Gold : Border);
                var background = new RectangleRuntime { IsFilled = true, StrokeWidth = 0, FillColor = color ?? (selected ? new Color(90,73,27) : new Color(29,42,61)) };
                Position(background,new(rect.X+1,rect.Y+1,rect.Width-2,rect.Height-2)); _root!.AddChild(background);
                if (title.Length > 0) Label(title,new(rect.X+6,rect.Y+2,rect.Width-12,rect.Height-4), rect.Width < 60 ? 11 : rect.Width < 80 ? 13 : 15, enabled ? selected ? Gold : TextColor : new Color(116,132,155), centered: true);
                _buttons.Add((rect,action,background,selected,enabled,background.FillColor,diagnosticName??(title.Length>0?title:"palette_or_room_cell")));
            }
            private void Icon(string name, Rectangle rect)
            {
                using var timing=Perf.Measure("ui.create_icon",name);
                if (string.IsNullOrEmpty(name)) { Label("X",rect,22,Gold); return; }
                var sprite = MsxScreen.Get(name); if (sprite == null) return;
                var visual = new SpriteRuntime(); UI.Gum.GumUiFactory.ApplySprite(visual, GameAssets.AtlasForSprite(name), name);
                int scale=Math.Max(1,Math.Min(rect.Width/sprite.SourceRect.Width,rect.Height/sprite.SourceRect.Height));
                visual.WidthUnits=DimensionUnitType.Absolute;visual.HeightUnits=DimensionUnitType.Absolute;
                visual.Width=sprite.SourceRect.Width*scale;visual.Height=sprite.SourceRect.Height*scale;
                visual.X=(rect.X+(rect.Width-visual.Width)/2)*_uiScale;visual.Y=rect.Y*_uiScale;
                visual.Width*=_uiScale;visual.Height*=_uiScale;_root!.AddChild(visual);
            }
            private void Position(Gum.Wireframe.GraphicalUiElement element, Rectangle rect)
            { element.X=rect.X*_uiScale;element.Y=rect.Y*_uiScale;element.WidthUnits=DimensionUnitType.Absolute;element.HeightUnits=DimensionUnitType.Absolute;element.Width=rect.Width*_uiScale;element.Height=rect.Height*_uiScale; }
        }
    }
}
