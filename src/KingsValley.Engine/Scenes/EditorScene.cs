#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// **Stage editor** — the port's last item, mirroring the ROM editor (<c>cont_edit</c> 0x7B08,
/// banks 1-3) and the module's **real legend** in bank D (<c>print_legend</c> at <c>0xBC54</c>):
///
/// <code>floor1 · floor2 · ladder · player · enemy · trap · tool weapon · soul stone · exit door · save · end</code>
///
/// In the original the editor is a state machine driven by <c>E25B</c> (table <c>0x7B0E</c>:
/// <c>edit_hud</c> → <c>edit_kind</c> → <c>edit_sub</c> → <c>edit_scr</c> → <c>edit_put</c> →
/// <c>edit_yn</c>), with the legend in <c>E260</c> (0..10), the subtype in <c>E261</c>, the room in
/// <c>E262</c>/<c>E788</c> and the placement in <c>edit_put</c> (table <c>0x7E6F</c>:
/// <c>put_floor1</c>, <c>put_floor2</c>, <c>put_ladder</c>, <c>put_player</c>, <c>put_enemy</c>,
/// <c>put_trap</c>, <c>put_tool</c>, <c>put_gem</c>, <c>put_exit</c>).
///
/// The ROM help texts (bank D, table <c>0xBCBB</c>) are in <see cref="Help"/>.
/// </summary>
public class EditorScene : SceneBase
{
    /// <summary>Stage definition in memory, preserved during play-tests.</summary>
    public static StageDefinition? LastEditedStageDef { get; set; }
    public static int LastEditedStageNumber { get; set; } = 1;
    public static int LastEditedRoom { get; set; } = 1;
    public static int LastEditedCustomSlot { get; set; } = 1;

    protected override void OnInitialize()
    {
        if (LastEditedStageDef != null)
        {
            var stage = LastEditedStageDef;
            var num = LastEditedStageNumber;
            var rm = LastEditedRoom;
            var slot = LastEditedCustomSlot;
            LastEditedStageDef = null; // consumido
            AddEntity(new Entity("editor")).AddComponent(new EditorRenderer(stage, num, rm, slot));
            return;
        }

        var envStage = System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_STAGE") ??
                       System.Environment.GetEnvironmentVariable("KINGSVALLEY_LEVEL");
        var number = int.TryParse(envStage, out var n) ? Math.Clamp(n, 1, GameConstants.TotalStages) : LastEditedStageNumber;
        var def = StageLoader.Load(RomContent.LevelsDirectory, number);
        var autoSave = System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_SAVE") == "1";
        AddEntity(new Entity("editor")).AddComponent(new EditorRenderer(def, number, LastEditedRoom, LastEditedCustomSlot, autoSave));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("editor")?.GetComponent<EditorRenderer>();
        renderer?.ReloadGraphics();
    }

    private sealed class EditorRenderer : RenderableComponent, IUpdatable
    {
        private const int Columns = GameConstants.RoomColumns;
        private const int Rows = GameConstants.RoomRows;
        private const int Cell = 8;
        private const int HudY = Rows * Cell;

        /// <summary>Os 11 itens da legenda do ROM (“print_legend” 0xBC54, x=40, y=16+16*i).</summary>
        public static readonly string[] Legend =
        [
            "floor1", "floor2", "ladder", "player", "enemy", "trap",
            "tool weapon", "soul stone", "exit door", "save", "end",
        ];

        /// <summary>ROM help text per legend item (table 0xBCBB of bank D).</summary>
        public static readonly string[] Help =
        [
            "", "", "",
            "",
            "slouman / flouman / pyoncy / rock roll",
            "door1 lr / door1 rl / door2 lr / door2 rl / wall / floor / stone / ladder",
            "knife / boomerang / scoop / pick / hammer / drill",
            "", "", "", "",
        ];

        /// <summary>Subtypes (the ROM's <c>E261</c>) per legend item; empty = no subtype.</summary>
        private static readonly string[][] SubTypes =
        [
            [], [], [], [],
            ["slouman", "flouman", "pyoncy", "rock roll"],
            ["door1 lr", "door1 rl", "door2 lr", "door2 rl", "wall", "floor", "stone", "ladder"],
            ["knife", "boomerang", "scoop", "pick", "hammer", "drill"],
            [], [], [], [],
        ];

        private static readonly TerrainKind[] Palette =
            [TerrainKind.Empty, TerrainKind.Soft, TerrainKind.Wall, TerrainKind.Ladder];

        private StageDefinition _stage;
        private TerrainKind[][] _rooms;
        private int _currentStageNumber = 1;
        private int _currentMusicIndex = -1;

        private RoomBackgroundBuilder? _bgBuilder;
        private readonly Dictionary<int, Texture2D> _roomTextures = new();

        private void EnsureRoomTextures()
        {
            if (_bgBuilder == null)
                _bgBuilder = new RoomBackgroundBuilder(Nez.Core.GraphicsDevice);

            for (int i = 0; i < _rooms.Length; i++)
            {
                int roomId = i + 1;
                _roomTextures.TryGetValue(roomId, out var existing);
                _roomTextures[roomId] = _bgBuilder.BuildCustomRoomTexture(
                    Pyramid, _stage.Number, roomId, _rooms[i],
                    _stage.Ladders, _stage.GoldenLadders, _mechs, existing);
            }
        }

        private void UpdateRoomTexture(int roomId)
        {
            if (_bgBuilder == null)
                _bgBuilder = new RoomBackgroundBuilder(Nez.Core.GraphicsDevice);

            if (roomId >= 1 && roomId <= _rooms.Length)
            {
                _roomTextures.TryGetValue(roomId, out var existing);
                _roomTextures[roomId] = _bgBuilder.BuildCustomRoomTexture(
                    Pyramid, _stage.Number, roomId, _rooms[roomId - 1],
                    _stage.Ladders, _stage.GoldenLadders, _mechs, existing);
            }
        }

        public void ReloadGraphics()
        {
            _bgBuilder?.Reset();
            foreach (var tex in _roomTextures.Values)
                tex?.Dispose();
            _roomTextures.Clear();
            EnsureRoomTextures();
        }

        private int _room;
        private int _tool = 1;              // index in the legend (default: floor2, which the ROM paints first)
        private int _cx, _cy;
        private bool _legendOpen;
        private bool _mapOpen;
        private bool _helpOpen;
        private int _mapX, _mapY;
        private bool _autoSave;
        private bool _autoPlay;
        private bool _showGrid;
        private int _lastPaintedX = -1, _lastPaintedY = -1;
        private string? _statusMessage;
        private int _statusTimer;
        private bool _isEraser;

        // Stage World Preview (free camera with Zoom and Scroll)
        private bool _worldViewOpen;
        private float _worldCamX, _worldCamY;
        private float _worldZoom = 0.5f;
        private static readonly float[] ZoomLevels = [0.25f, 0.5f, 1.0f, 2.0f];
        private int _zoomIndex = 1; // 0.5f
        private Microsoft.Xna.Framework.Vector2 _dragStartPointer;
        private Microsoft.Xna.Framework.Vector2 _dragStartCam;
        private bool _isDraggingWorld;

        private void ZoomIn()
        {
            if (_zoomIndex < ZoomLevels.Length - 1)
            {
                _zoomIndex++;
                _worldZoom = ZoomLevels[_zoomIndex];
                GameAudio.PlayCursor();
            }
        }

        private void ZoomOut()
        {
            if (_zoomIndex > 0)
            {
                _zoomIndex--;
                _worldZoom = ZoomLevels[_zoomIndex];
                GameAudio.PlayCursor();
            }
        }

        private void OpenWorldView()
        {
            _worldViewOpen = true;
            _mapOpen = false;
            _legendOpen = false;
            _helpOpen = false;
            (int gx, int gy) = FindRoom(RoomNumber);
            _worldCamX = gx * 256 + 128;
            _worldCamY = gy * 192 + 96;
            _zoomIndex = 1;
            _worldZoom = ZoomLevels[_zoomIndex];
            GameAudio.PlayConfirm();
        }

        private void ShowStatus(string message)
        {
            _statusMessage = message;
            _statusTimer = 120;
        }

        private int CurrentToolbarIndex => _isEraser ? 3 : _tool switch
        {
            0 => 0, // soft
            1 => 1, // wall
            2 => 2, // ladder
            3 => 4, // player
            8 => 5, // exit
            7 => 6, // gem
            4 => 7, // enemy
            6 => 8, // item
            _ => -1,
        };

        private void SelectToolbar(int index)
        {
            switch (index)
            {
                case 0:
                    _tool = 0;
                    _isEraser = false;
                    ShowStatus("1: SOFT BRICK");
                    GameAudio.PlayCursor();
                    break;
                case 1:
                    _tool = 1;
                    _isEraser = false;
                    ShowStatus("2: SOLID WALL");
                    GameAudio.PlayCursor();
                    break;
                case 2:
                    _tool = 2;
                    _isEraser = false;
                    ShowStatus("3: LADDER");
                    GameAudio.PlayCursor();
                    break;
                case 3:
                    _isEraser = true;
                    ShowStatus("4: ERASER (CLICK/DRAG)");
                    GameAudio.PlayCursor();
                    break;
                case 4:
                    _tool = 3;
                    _isEraser = false;
                    ShowStatus("5: VICK START");
                    GameAudio.PlayCursor();
                    break;
                case 5:
                    _tool = 8;
                    _isEraser = false;
                    ShowStatus("6: EXIT DOOR");
                    GameAudio.PlayCursor();
                    break;
                case 6:
                    _tool = 7;
                    _isEraser = false;
                    ShowStatus("7: SOUL STONE");
                    GameAudio.PlayCursor();
                    break;
                case 7:
                    if (!_isEraser && _tool == 4)
                        _sub[4] = (_sub[4] + 1) % SubTypes[4].Length;
                    _tool = 4;
                    _isEraser = false;
                    ShowStatus($"8: FOE: {SubTypes[4][_sub[4]].ToUpperInvariant()}");
                    GameAudio.PlayCursor();
                    break;
                case 8:
                    if (!_isEraser && _tool == 6)
                        _sub[6] = (_sub[6] + 1) % SubTypes[6].Length;
                    _tool = 6;
                    _isEraser = false;
                    ShowStatus($"9: ITEM: {SubTypes[6][_sub[6]].ToUpperInvariant()}");
                    GameAudio.PlayCursor();
                    break;
            }
        }

        private static bool IsNum(Keys k1, Keys k2) => Nez.Input.IsKeyPressed(k1) || Nez.Input.IsKeyPressed(k2);

        private void AddRoomAt(int gridX, int gridY)
        {
            if (gridX < 0 || gridX >= 8 || gridY < 0 || gridY >= 8)
                return;

            if (_stage.RoomGrid[gridY, gridX] != 0)
                return;

            // Creates an empty room with walls on the edges and a default floor
            var newCells = new TerrainKind[Columns * Rows];
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (x == 0 || x == 1 || x == Columns - 1 || x == Columns - 2 ||
                        y == 0 || y == 1 || y == Rows - 1 || y == Rows - 2)
                    {
                        newCells[y * Columns + x] = TerrainKind.Wall;
                    }
                    else
                    {
                        newCells[y * Columns + x] = TerrainKind.Empty;
                    }
                }
            }

            int newRoomId = _rooms.Length + 1;
            var list = _rooms.ToList();
            list.Add(newCells);
            _rooms = list.ToArray();

            _stage.RoomGrid[gridY, gridX] = newRoomId;
            _room = newRoomId - 1;
            _mapOpen = false;
            UpdateRoomTexture(newRoomId);

            ShowStatus($"CREATED ROOM {newRoomId} AT ({gridX},{gridY})!");
            GameAudio.PlayConfirm();
            Console.WriteLine($"[Editor] New room {newRoomId} created at position ({gridX}, {gridY})");
        }

        private int Pyramid => Math.Clamp((_currentStageNumber - 1) / 10 + 1, 1, 6);

        /// <summary>Edited positions and rooms (written to the JSON along with the cells).</summary>
        private int _startRoom;
        private int _startX, _startY;
        private readonly List<StagePoint> _stones = [];
        private int _exitRoom;
        private int _exitX, _exitY;

        /// <summary>Edited subtypes, items and actors (the <c>E261</c> + the stage lists).</summary>
        private readonly int[] _sub = new int[Legend.Length];
        private readonly List<ItemDefinition> _items = [];
        private readonly List<CoffinDefinition> _coffins = [];
        private readonly List<MechanismDefinition> _mechs = [];

        // Custom Levels and Dialog Management
        private int _customSlot = 1; // 1..10
        private bool _saveDialogOpen;
        private bool _loadDialogOpen;
        private int _loadTab; // 0 = Custom Slots, 1 = ROM Templates
        private int _romTemplateStage = 1;

        public EditorRenderer(StageDefinition stage, int stageNumber = 1, int initialRoom = 1, int customSlot = 1, bool autoSave = false)
        {
            _stage = stage;
            _currentStageNumber = stageNumber;
            _autoSave = autoSave;
            _customSlot = Math.Clamp(customSlot, 1, 10);
            _romTemplateStage = stageNumber;
            _rooms = stage.Rooms.Select(r => (TerrainKind[])r.Cells.Clone()).ToArray();
            if (_rooms.Length == 0)
                _rooms = [new TerrainKind[Columns * Rows]];

            _startRoom = Math.Clamp(stage.PlayerStart.Room, 1, _rooms.Length);
            _startX = stage.PlayerStart.X;
            _startY = stage.PlayerStart.Y;

            _exitRoom = Math.Clamp(stage.ExitDoor.Position.Room, 1, _rooms.Length);
            _exitX = stage.ExitDoor.Position.X;
            _exitY = stage.ExitDoor.Position.Y;

            _room = Math.Clamp(initialRoom - 1, 0, _rooms.Length - 1);
            _stones.AddRange(stage.SoulStones);
            _items.AddRange(stage.Items);
            _coffins.AddRange(stage.Coffins);
            _mechs.AddRange(stage.Mechanisms);
            _legendOpen = System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_LEGEND") == "1";
            _autoPlay = System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_PLAY") == "1";
            if (System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_MAP") == "1")
            {
                _mapOpen = true;
                (_mapX, _mapY) = FindRoom(1);
            }
            if (System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_WORLDVIEW") == "1")
            {
                OpenWorldView();
                if (int.TryParse(System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_ZOOM"), out var zi))
                {
                    _zoomIndex = Math.Clamp(zi, 0, ZoomLevels.Length - 1);
                    _worldZoom = ZoomLevels[_zoomIndex];
                }
            }
            if (int.TryParse(System.Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_TOOL"), out var tool))
                _tool = Math.Clamp(tool, 0, Legend.Length - 1);

            EnsureRoomTextures();
        }

        /// <summary>Applies a loaded stage by updating all rooms, entities and textures.</summary>
        private void ApplyLoadedStage(StageDefinition stage, int stageNumber, int room = 1)
        {
            _stage = stage;
            _currentStageNumber = stageNumber;
            _rooms = stage.Rooms.Select(r => (TerrainKind[])r.Cells.Clone()).ToArray();
            if (_rooms.Length == 0)
                _rooms = [new TerrainKind[Columns * Rows]];

            _startRoom = Math.Clamp(stage.PlayerStart.Room, 1, _rooms.Length);
            _startX = stage.PlayerStart.X;
            _startY = stage.PlayerStart.Y;

            _exitRoom = Math.Clamp(stage.ExitDoor.Position.Room, 1, _rooms.Length);
            _exitX = stage.ExitDoor.Position.X;
            _exitY = stage.ExitDoor.Position.Y;

            _stones.Clear();
            _stones.AddRange(stage.SoulStones);

            _items.Clear();
            _items.AddRange(stage.Items);

            _coffins.Clear();
            _coffins.AddRange(stage.Coffins);

            _mechs.Clear();
            _mechs.AddRange(stage.Mechanisms);

            _room = Math.Clamp(room - 1, 0, _rooms.Length - 1);
            _mapOpen = false;
            _worldViewOpen = false;
            _legendOpen = false;
            _saveDialogOpen = false;
            _loadDialogOpen = false;

            EnsureRoomTextures();
        }

        /// <summary>Loads another ROM stage as a base/template in the editor.</summary>
        public void LoadStage(int number) => LoadRomTemplate(number);

        public void LoadRomTemplate(int number)
        {
            number = Math.Clamp(number, 1, GameConstants.TotalStages);
            var stage = StageLoader.Load(RomContent.LevelsDirectory, number);
            ApplyLoadedStage(stage, number, stage.PlayerStart.Room);
            _romTemplateStage = number;
            ShowStatus($"LOADED ROM {number:D2} (TEMPLATE)");
            GameAudio.PlayConfirm();
            Console.WriteLine($"[Editor] Loaded template from ROM stage {number} ({_rooms.Length} rooms)");
        }

        public void LoadFromCustomSlot(int slot)
        {
            slot = Math.Clamp(slot, 1, 10);
            var path = System.IO.Path.Combine(RomContent.CustomLevelsDirectory, $"custom_stage_{slot:D2}.json");
            if (!System.IO.File.Exists(path))
            {
                ShowStatus($"SLOT {slot:D2} IS EMPTY!");
                GameAudio.PlayCursor();
                return;
            }

            var json = System.IO.File.ReadAllText(path);
            var stage = StageLoader.Parse(json);
            _customSlot = slot;
            ApplyLoadedStage(stage, stage.Number > 0 ? stage.Number : slot, stage.PlayerStart.Room);
            ShowStatus($"LOADED CUSTOM SLOT {slot:D2}!");
            GameAudio.PlayConfirm();
            Console.WriteLine($"[Editor] Carregado custom level slot {slot} de {path}");
        }

        public void SaveToSlot(int slot)
        {
            slot = Math.Clamp(slot, 1, 10);
            _customSlot = slot;
            var json = BuildJson();
            var path = System.IO.Path.Combine(RomContent.CustomLevelsDirectory, $"custom_stage_{slot:D2}.json");

            // MAXIMUM SAFETY: Guarantees that the 60 original ROM stages are NEVER overwritten
            if (path.StartsWith(RomContent.LevelsDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Operation blocked: the 60 original ROM levels are protected from writing.");

            System.IO.File.WriteAllText(path, json);

            var reloaded = StageLoader.Parse(json);
            _saveDialogOpen = false;
            ShowStatus($"SAVED CUSTOM SLOT {slot:D2} OK!");
            GameAudio.PlayConfirm();
            Console.WriteLine($"[Editor] Salvo em {path} ({json.Length} bytes, slot {slot})");
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        private TerrainKind[] Cells => _rooms[Math.Clamp(_room, 0, _rooms.Length - 1)];

        private TerrainKind CellAt(int x, int y) => Cells[y * Columns + x];

        private int RoomNumber => _room + 1;

        public void Update()
        {
            if (_autoSave)
            {
                _autoSave = false;
                Save();
            }

            if (_autoPlay)
            {
                _autoPlay = false;
                PlayTest();
                return;
            }

            bool left = Nez.Input.IsKeyPressed(Keys.Left) || Nez.Input.IsKeyPressed(Keys.A);
            bool right = Nez.Input.IsKeyPressed(Keys.Right) || Nez.Input.IsKeyPressed(Keys.D);
            bool up = Nez.Input.IsKeyPressed(Keys.Up) || Nez.Input.IsKeyPressed(Keys.W);
            bool down = Nez.Input.IsKeyPressed(Keys.Down) || Nez.Input.IsKeyPressed(Keys.S);

            if (Nez.Input.IsKeyPressed(Keys.H))
            {
                _helpOpen = !_helpOpen;
                GameAudio.PlayCursor();
            }

            if (_helpOpen)
            {
                if (Nez.Input.IsKeyPressed(Keys.Escape) || Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.H))
                    _helpOpen = false;
                return;
            }

            if (_saveDialogOpen)
            {
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                {
                    _saveDialogOpen = false;
                    GameAudio.PlayCursor();
                    return;
                }

                // Keys 1 to 9 and 0 save to the corresponding slot
                for (int k = 0; k < 10; k++)
                {
                    var key1 = k == 9 ? Keys.D0 : (Keys.D1 + k);
                    var key2 = k == 9 ? Keys.NumPad0 : (Keys.NumPad1 + k);
                    if (Nez.Input.IsKeyPressed(key1) || Nez.Input.IsKeyPressed(key2))
                    {
                        SaveToSlot(k + 1);
                        return;
                    }
                }

                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                {
                    var pt = KingsValley.Engine.Input.UnifiedPointer.Position;
                    const int ox = 16, oy = 20;
                    for (int slot = 1; slot <= 10; slot++)
                    {
                        int col = (slot - 1) / 5;
                        int row = (slot - 1) % 5;
                        int sx = ox + 10 + col * 104;
                        int sy = oy + 36 + row * 20;
                        if (pt.X >= sx && pt.X <= sx + 98 && pt.Y >= sy && pt.Y <= sy + 16)
                        {
                            SaveToSlot(slot);
                            return;
                        }
                    }

                    if (pt.X < ox || pt.X > ox + 224 || pt.Y < oy || pt.Y > oy + 160)
                    {
                        _saveDialogOpen = false;
                        GameAudio.PlayCursor();
                        return;
                    }
                }
                return;
            }

            if (_loadDialogOpen)
            {
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                {
                    _loadDialogOpen = false;
                    GameAudio.PlayCursor();
                    return;
                }

                if (Nez.Input.IsKeyPressed(Keys.C)) { _loadTab = 0; GameAudio.PlayCursor(); }
                if (Nez.Input.IsKeyPressed(Keys.R)) { _loadTab = 1; GameAudio.PlayCursor(); }

                if (_loadTab == 0) // Custom slots
                {
                    for (int k = 0; k < 10; k++)
                    {
                        var key1 = k == 9 ? Keys.D0 : (Keys.D1 + k);
                        var key2 = k == 9 ? Keys.NumPad0 : (Keys.NumPad1 + k);
                        if (Nez.Input.IsKeyPressed(key1) || Nez.Input.IsKeyPressed(key2))
                        {
                            LoadFromCustomSlot(k + 1);
                            return;
                        }
                    }

                    if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                    {
                        var pt = KingsValley.Engine.Input.UnifiedPointer.Position;
                        const int ox = 16, oy = 20;

                        // Checa clique nas abas
                        if (pt.Y >= oy + 20 && pt.Y <= oy + 32)
                        {
                            if (pt.X >= ox + 10 && pt.X <= ox + 106) { _loadTab = 0; GameAudio.PlayCursor(); return; }
                            if (pt.X >= ox + 116 && pt.X <= ox + 212) { _loadTab = 1; GameAudio.PlayCursor(); return; }
                        }

                        for (int slot = 1; slot <= 10; slot++)
                        {
                            int col = (slot - 1) / 5;
                            int row = (slot - 1) % 5;
                            int sx = ox + 10 + col * 104;
                            int sy = oy + 38 + row * 18;
                            if (pt.X >= sx && pt.X <= sx + 98 && pt.Y >= sy && pt.Y <= sy + 15)
                            {
                                LoadFromCustomSlot(slot);
                                return;
                            }
                        }

                        if (pt.X < ox || pt.X > ox + 224 || pt.Y < oy || pt.Y > oy + 160)
                        {
                            _loadDialogOpen = false;
                            GameAudio.PlayCursor();
                            return;
                        }
                    }
                }
                else // ROM Templates
                {
                    if (left || Nez.Input.IsKeyPressed(Keys.Q) || Nez.Input.IsKeyPressed(Keys.OemOpenBrackets))
                    {
                        _romTemplateStage = _romTemplateStage <= 1 ? GameConstants.TotalStages : _romTemplateStage - 1;
                        GameAudio.PlayCursor();
                    }
                    if (right || Nez.Input.IsKeyPressed(Keys.E) || Nez.Input.IsKeyPressed(Keys.OemCloseBrackets))
                    {
                        _romTemplateStage = _romTemplateStage >= GameConstants.TotalStages ? 1 : _romTemplateStage + 1;
                        GameAudio.PlayCursor();
                    }

                    if (Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.Space))
                    {
                        LoadRomTemplate(_romTemplateStage);
                        return;
                    }

                    if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                    {
                        var pt = KingsValley.Engine.Input.UnifiedPointer.Position;
                        const int ox = 16, oy = 20;

                        if (pt.Y >= oy + 20 && pt.Y <= oy + 32)
                        {
                            if (pt.X >= ox + 10 && pt.X <= ox + 106) { _loadTab = 0; GameAudio.PlayCursor(); return; }
                            if (pt.X >= ox + 116 && pt.X <= ox + 212) { _loadTab = 1; GameAudio.PlayCursor(); return; }
                        }

                        // Arrows ◄ ►
                        if (pt.Y >= oy + 58 && pt.Y <= oy + 100)
                        {
                            if (pt.X >= ox + 24 && pt.X <= ox + 60)
                            {
                                _romTemplateStage = _romTemplateStage <= 1 ? GameConstants.TotalStages : _romTemplateStage - 1;
                                GameAudio.PlayCursor();
                                return;
                            }
                            if (pt.X >= ox + 160 && pt.X <= ox + 200)
                            {
                                _romTemplateStage = _romTemplateStage >= GameConstants.TotalStages ? 1 : _romTemplateStage + 1;
                                GameAudio.PlayCursor();
                                return;
                            }
                        }

                        // [ENTER] LOAD TEMPLATE button
                        if (pt.X >= ox + 48 && pt.X <= ox + 176 && pt.Y >= oy + 110 && pt.Y <= oy + 126)
                        {
                            LoadRomTemplate(_romTemplateStage);
                            return;
                        }

                        if (pt.X < ox || pt.X > ox + 224 || pt.Y < oy || pt.Y > oy + 160)
                        {
                            _loadDialogOpen = false;
                            GameAudio.PlayCursor();
                            return;
                        }
                    }
                }
                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.Tab))
            {
                _legendOpen = !_legendOpen;
                GameAudio.PlayCursor();
            }

            if (Nez.Input.IsKeyPressed(Keys.V))
            {
                if (_worldViewOpen)
                {
                    _worldViewOpen = false;
                    GameAudio.PlayCursor();
                }
                else
                {
                    OpenWorldView();
                }
            }

            if (_worldViewOpen)
            {
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                {
                    _worldViewOpen = false;
                    GameAudio.PlayCursor();
                    return;
                }

                // Keyboard navigation (WASD or arrows)
                float moveSpeed = 64f / _worldZoom;
                if (left) _worldCamX -= moveSpeed;
                if (right) _worldCamX += moveSpeed;
                if (up) _worldCamY -= moveSpeed;
                if (down) _worldCamY += moveSpeed;

                // Zoom por teclado (+ / - / Z / X)
                if (Nez.Input.IsKeyPressed(Keys.OemPlus) || Nez.Input.IsKeyPressed(Keys.Add) || Nez.Input.IsKeyPressed(Keys.Z))
                    ZoomIn();
                if (Nez.Input.IsKeyPressed(Keys.OemMinus) || Nez.Input.IsKeyPressed(Keys.Subtract) || Nez.Input.IsKeyPressed(Keys.X))
                    ZoomOut();

                // Zoom by the mouse wheel
                int wheel = Nez.Input.MouseWheelDelta;
                if (wheel > 0) ZoomIn();
                else if (wheel < 0) ZoomOut();

                // Scroll / Pan and Room Selection by mouse / touch
                var pointer = KingsValley.Engine.Input.UnifiedPointer.Position;
                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                {
                    // Checks the zoom buttons in the top right corner
                    if (pointer.Y >= 2 && pointer.Y <= 14)
                    {
                        if (pointer.X >= 204 && pointer.X <= 218) { ZoomOut(); return; }
                        if (pointer.X >= 220 && pointer.X <= 234) { ZoomIn(); return; }
                        if (pointer.X >= 236 && pointer.X <= 254) { _worldViewOpen = false; GameAudio.PlayCursor(); return; }
                    }

                    _dragStartPointer = pointer;
                    _dragStartCam = new Microsoft.Xna.Framework.Vector2(_worldCamX, _worldCamY);
                    _isDraggingWorld = true;
                }

                if (KingsValley.Engine.Input.UnifiedPointer.IsDown && _isDraggingWorld)
                {
                    _worldCamX = _dragStartCam.X - (pointer.X - _dragStartPointer.X) / _worldZoom;
                    _worldCamY = _dragStartCam.Y - (pointer.Y - _dragStartPointer.Y) / _worldZoom;
                }

                if (KingsValley.Engine.Input.UnifiedPointer.IsJustReleased && _isDraggingWorld)
                {
                    _isDraggingWorld = false;
                    if (Microsoft.Xna.Framework.Vector2.Distance(pointer, _dragStartPointer) < 4f)
                    {
                        float worldX = _worldCamX + (pointer.X - 128f) / _worldZoom;
                        float worldY = _worldCamY + (pointer.Y - 106f) / _worldZoom;
                        int gx = (int)Math.Floor(worldX / 256f);
                        int gy = (int)Math.Floor(worldY / 192f);
                        if (gx >= 0 && gx < 6 && gy >= 0 && gy < 6)
                        {
                            int id = _stage.RoomGrid[gy, gx];
                            if (id > 0 && id <= _rooms.Length)
                            {
                                _room = id - 1;
                                _worldViewOpen = false;
                                GameAudio.PlayConfirm();
                                ShowStatus($"EDITING ROOM {id}");
                                return;
                            }
                        }
                    }
                }

                return;
            }

            if (Nez.Input.IsKeyPressed(Keys.M) || Nez.Input.IsKeyPressed(Keys.R))
            {
                _mapOpen = !_mapOpen;
                (_mapX, _mapY) = FindRoom(_room + 1);
                GameAudio.PlayCursor();
            }

            if (_mapOpen)
            {
                if (Nez.Input.IsKeyPressed(Keys.V))
                {
                    OpenWorldView();
                    return;
                }

                // Stage Room Overview (real thumbnails of all rooms + add button)
                if (left && _mapX > 0) { _mapX--; GameAudio.PlayCursor(); }
                if (right && _mapX < 5) { _mapX++; GameAudio.PlayCursor(); }
                if (up && _mapY > 0) { _mapY--; GameAudio.PlayCursor(); }
                if (down && _mapY < 4) { _mapY++; GameAudio.PlayCursor(); }

                if (Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter))
                {
                    int id = _stage.RoomGrid[_mapY, _mapX];
                    if (id > 0 && id <= _rooms.Length)
                    {
                        _room = id - 1;
                        _mapOpen = false;
                        GameAudio.PlayConfirm();
                    }
                    else if (id == 0)
                    {
                        AddRoomAt(_mapX, _mapY);
                    }
                }

                // Direct mouse click on the room thumbnail or on the [+] button to add
                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                {
                    var pt = KingsValley.Engine.Input.UnifiedPointer.Position;

                    // Click on the World Preview option in the modal footer
                    if (pt.Y >= 162 && pt.Y <= 176 && pt.X >= 80 && pt.X <= 180)
                    {
                        OpenWorldView();
                        return;
                    }

                    const int mw = 32, mh = 24, gap = 4, ox = 20, oy = 26;
                    for (int y = 0; y < 5; y++)
                    {
                        for (int x = 0; x < 6; x++)
                        {
                            int rx = ox + x * (mw + gap);
                            int ry = oy + y * (mh + gap);
                            if (pt.X >= rx && pt.X < rx + mw && pt.Y >= ry && pt.Y < ry + mh)
                            {
                                int id = _stage.RoomGrid[y, x];
                                if (id > 0 && id <= _rooms.Length)
                                {
                                    _room = id - 1;
                                    _mapOpen = false;
                                    GameAudio.PlayConfirm();
                                    return;
                                }
                                else if (id == 0)
                                {
                                    AddRoomAt(x, y);
                                    return;
                                }
                            }
                        }
                    }

                    if (pt.X < 12 || pt.X > 244 || pt.Y < 10 || pt.Y > 182)
                    {
                        _mapOpen = false;
                        return;
                    }
                }

                if (Nez.Input.IsKeyPressed(Keys.Escape) || Nez.Input.IsKeyPressed(Keys.M) || Nez.Input.IsKeyPressed(Keys.R))
                    _mapOpen = false;
                return;
            }

            if (_legendOpen)
            {
                // UP / DOWN or W / S navigate the vertical tool list
                if (up)
                {
                    _tool = (_tool + Legend.Length - 1) % Legend.Length;
                    GameAudio.PlayCursor();
                }
                if (down)
                {
                    _tool = (_tool + 1) % Legend.Length;
                    GameAudio.PlayCursor();
                }

                // LEFT / RIGHT or A / D switch the subtypes of the current tool
                var subs = SubTypes[_tool];
                if (subs.Length > 0)
                {
                    if (left)
                    {
                        _sub[_tool] = (_sub[_tool] + subs.Length - 1) % subs.Length;
                        GameAudio.PlayCursor();
                    }
                    if (right)
                    {
                        _sub[_tool] = (_sub[_tool] + 1) % subs.Length;
                        GameAudio.PlayCursor();
                    }
                }

                // Mouse Wheel on the tool list
                int wheel = Nez.Input.MouseWheelDelta;
                if (wheel > 0)
                {
                    _tool = (_tool + Legend.Length - 1) % Legend.Length;
                    GameAudio.PlayCursor();
                }
                else if (wheel < 0)
                {
                    _tool = (_tool + 1) % Legend.Length;
                    GameAudio.PlayCursor();
                }

                // Clique do mouse direto no item da legenda
                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                {
                    var pt = KingsValley.Engine.Input.UnifiedPointer.Position;
                    if (pt.X >= 140 && pt.X <= 254 && pt.Y >= 20 && pt.Y < 20 + Legend.Length * 12)
                    {
                        int idx = (int)(pt.Y - 20) / 12;
                        if (idx >= 0 && idx < Legend.Length)
                        {
                            _tool = idx;
                            GameAudio.PlayCursor();
                            Activate();
                            return;
                        }
                    }
                }

                if (Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter))
                    Activate();
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                    _legendOpen = false;
                return;
            }

            if (_statusTimer > 0)
                _statusTimer--;

            // Numeric shortcuts 1 to 9 for the direct toolbar
            if (IsNum(Keys.D1, Keys.NumPad1)) SelectToolbar(0);
            if (IsNum(Keys.D2, Keys.NumPad2)) SelectToolbar(1);
            if (IsNum(Keys.D3, Keys.NumPad3)) SelectToolbar(2);
            if (IsNum(Keys.D4, Keys.NumPad4)) SelectToolbar(3);
            if (IsNum(Keys.D5, Keys.NumPad5)) SelectToolbar(4);
            if (IsNum(Keys.D6, Keys.NumPad6)) SelectToolbar(5);
            if (IsNum(Keys.D7, Keys.NumPad7)) SelectToolbar(6);
            if (IsNum(Keys.D8, Keys.NumPad8)) SelectToolbar(7);
            if (IsNum(Keys.D9, Keys.NumPad9)) SelectToolbar(8);

            // Mouse scroll to switch tools quickly
            int wheelDelta = Nez.Input.MouseWheelDelta;
            if (wheelDelta > 0)
            {
                int cur = CurrentToolbarIndex;
                int next = cur <= 0 ? 8 : cur - 1;
                SelectToolbar(next);
            }
            else if (wheelDelta < 0)
            {
                int cur = CurrentToolbarIndex;
                int next = (cur + 1) % 9;
                SelectToolbar(next);
            }

            if (left && _cx > 0) { _cx--; GameAudio.PlayCursor(); }
            if (right && _cx < Columns - 1) { _cx++; GameAudio.PlayCursor(); }
            if (up && _cy > 0) { _cy--; GameAudio.PlayCursor(); }
            if (down && _cy < Rows - 1) { _cy--; GameAudio.PlayCursor(); }

            if (Nez.Input.IsKeyPressed(Keys.Q) && _room > 0) { _room--; GameAudio.PlayCursor(); ShowStatus($"ROOM {_room + 1}/{_rooms.Length}"); }
            if (Nez.Input.IsKeyPressed(Keys.E) && _room < _rooms.Length - 1) { _room++; GameAudio.PlayCursor(); ShowStatus($"ROOM {_room + 1}/{_rooms.Length}"); }

            // Phase switch in the Editor: [ / ] or PageDown / PageUp
            if (Nez.Input.IsKeyPressed(Keys.OemOpenBrackets) || Nez.Input.IsKeyPressed(Keys.PageDown))
            {
                if (_currentStageNumber > 1)
                    LoadStage(_currentStageNumber - 1);
            }
            if (Nez.Input.IsKeyPressed(Keys.OemCloseBrackets) || Nez.Input.IsKeyPressed(Keys.PageUp))
            {
                if (_currentStageNumber < GameConstants.TotalStages)
                    LoadStage(_currentStageNumber + 1);
            }

            // T shortcut to toggle/test music in the Editor
            if (Nez.Input.IsKeyPressed(Keys.T))
            {
                _currentMusicIndex = (_currentMusicIndex + 1) % 6; // 0..4 = stage_a..e, 5 = mudo
                if (_currentMusicIndex < 5)
                {
                    var musics = new[] { "stage_a", "stage_b", "stage_c", "stage_d", "stage_e" };
                    GameAudio.PlayMusic(musics[_currentMusicIndex], loop: true);
                    ShowStatus($"BGM: {musics[_currentMusicIndex].ToUpperInvariant()}");
                    Console.WriteLine($"[Editor] Playing music: {musics[_currentMusicIndex]}");
                }
                else
                {
                    GameAudio.StopMusic();
                    ShowStatus("BGM: OFF");
                    Console.WriteLine("[Editor] Music stopped.");
                }
            }

            // Toggle Grid with the G key
            if (Nez.Input.IsKeyPressed(Keys.G))
            {
                _showGrid = !_showGrid;
                ShowStatus(_showGrid ? "GRID: ON" : "GRID: OFF");
                GameAudio.PlayCursor();
            }

            // Mouse / Touch interaction
            if (!_mapOpen && !_legendOpen && !_helpOpen)
            {
                var pointer = KingsValley.Engine.Input.UnifiedPointer.Position;

                // Clicks on the Edge Portals to navigate between neighboring rooms
                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
                {
                    (int curGx, int curGy) = FindRoom(RoomNumber);

                    // Top: Room above or open the panoramic view
                    if (pointer.Y <= 14 && pointer.X >= 80 && pointer.X <= 176)
                    {
                        if (curGy > 0 && _stage.RoomGrid[curGy - 1, curGx] > 0)
                        {
                            _room = _stage.RoomGrid[curGy - 1, curGx] - 1;
                            GameAudio.PlayCursor();
                            ShowStatus($"ROOM {_room + 1}/{_rooms.Length}");
                            return;
                        }
                        else if (_rooms.Length > 1)
                        {
                            _mapOpen = true;
                            (_mapX, _mapY) = (curGx, curGy);
                            GameAudio.PlayCursor();
                            return;
                        }
                    }

                    // Bottom of the room (above the HUD): Room below
                    if (pointer.Y >= 180 && pointer.Y < HudY && pointer.X >= 80 && pointer.X <= 176)
                    {
                        if (curGy < 5 && _stage.RoomGrid[curGy + 1, curGx] > 0)
                        {
                            _room = _stage.RoomGrid[curGy + 1, curGx] - 1;
                            GameAudio.PlayCursor();
                            ShowStatus($"ROOM {_room + 1}/{_rooms.Length}");
                            return;
                        }
                    }

                    // Left: Room on the left
                    if (pointer.X <= 32 && pointer.Y >= 80 && pointer.Y <= 112)
                    {
                        if (curGx > 0 && _stage.RoomGrid[curGy, curGx - 1] > 0)
                        {
                            _room = _stage.RoomGrid[curGy, curGx - 1] - 1;
                            GameAudio.PlayCursor();
                            ShowStatus($"ROOM {_room + 1}/{_rooms.Length}");
                            return;
                        }
                    }

                    // Right: Room on the right
                    if (pointer.X >= 224 && pointer.Y >= 80 && pointer.Y <= 112)
                    {
                        if (curGx < 5 && _stage.RoomGrid[curGy, curGx + 1] > 0)
                        {
                            _room = _stage.RoomGrid[curGy, curGx + 1] - 1;
                            GameAudio.PlayCursor();
                            ShowStatus($"ROOM {_room + 1}/{_rooms.Length}");
                            return;
                        }
                    }
                }

                // Clicks on the bottom HUD for quick actions
                if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed && pointer.Y >= HudY && pointer.Y < HudY + 20)
                {
                    if (pointer.Y < HudY + 11)
                    {
                        // HUD line 1: Toolbar with 9 buttons (28px each)
                        int btn = (int)(pointer.X - 2) / 28;
                        if (btn >= 0 && btn <= 8)
                        {
                            SelectToolbar(btn);
                            return;
                        }
                    }
                    else
                    {
                        // HUD line 2: Controls and actions
                        if (pointer.X < 22)
                        {
                            // Clicked on [S01] -> opens the ROM template selector
                            _loadDialogOpen = true;
                            _loadTab = 1;
                            GameAudio.PlayConfirm();
                        }
                        else if (pointer.X < 44)
                        {
                            // Clicked on [R1/3] -> opens the room map
                            _mapOpen = true;
                            (_mapX, _mapY) = FindRoom(RoomNumber);
                            GameAudio.PlayCursor();
                        }
                        else if (pointer.X < 116)
                        {
                            // Clicked on the tool description -> opens the legend/palette
                            _legendOpen = true;
                            GameAudio.PlayCursor();
                        }
                        else if (pointer.X < 144)
                        {
                            // [R]MAP
                            _mapOpen = true;
                            (_mapX, _mapY) = FindRoom(RoomNumber);
                            GameAudio.PlayCursor();
                        }
                        else if (pointer.X < 172)
                        {
                            // [V]VIEW
                            OpenWorldView();
                        }
                        else if (pointer.X < 200)
                        {
                            // [L]LOAD
                            _loadDialogOpen = true;
                            _saveDialogOpen = false;
                            GameAudio.PlayConfirm();
                        }
                        else if (pointer.X < 228)
                        {
                            // [S]SAVE
                            _saveDialogOpen = true;
                            _loadDialogOpen = false;
                            GameAudio.PlayConfirm();
                        }
                        else
                        {
                            // [P]PLAY
                            PlayTest();
                        }
                        return;
                    }
                }
                else if (pointer.X >= 0 && pointer.X < Columns * Cell && pointer.Y >= 0 && pointer.Y < Rows * Cell)
                {
                    int mx = (int)pointer.X / Cell;
                    int my = (int)pointer.Y / Cell;
                    _cx = mx;
                    _cy = my;

                    bool isContinuous = _isEraser || (_tool is 0 or 1 or 2); // borracha, soft, wall, ladder
                    bool isDown = KingsValley.Engine.Input.UnifiedPointer.IsDown;
                    bool isJustPressed = KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

                    if (isJustPressed || (isDown && isContinuous && (mx != _lastPaintedX || my != _lastPaintedY)))
                    {
                        _lastPaintedX = mx;
                        _lastPaintedY = my;
                        Apply();
                    }
                    else if (KingsValley.Engine.Input.UnifiedPointer.IsSecondaryJustPressed || KingsValley.Engine.Input.UnifiedPointer.IsSecondaryDown)
                    {
                        if (mx != _lastPaintedX || my != _lastPaintedY || KingsValley.Engine.Input.UnifiedPointer.IsSecondaryJustPressed)
                        {
                            _lastPaintedX = mx;
                            _lastPaintedY = my;
                            EraseAt(mx, my);
                        }
                    }
                }
                else
                {
                    _lastPaintedX = -1;
                    _lastPaintedY = -1;
                }
            }
            else
            {
                _lastPaintedX = -1;
                _lastPaintedY = -1;
            }

            if (!KingsValley.Engine.Input.UnifiedPointer.IsDown && !KingsValley.Engine.Input.UnifiedPointer.IsSecondaryDown)
            {
                _lastPaintedX = -1;
                _lastPaintedY = -1;
            }

            if (Nez.Input.IsKeyPressed(Keys.Space))
                Apply();

            if (Nez.Input.IsKeyPressed(Keys.X) || Nez.Input.IsKeyPressed(Keys.Delete))
            {
                EraseAt(_cx, _cy);
                GameAudio.PlayCursor();
            }

            if (Nez.Input.IsKeyPressed(Keys.F5) || Nez.Input.IsKeyPressed(Keys.S))
            {
                _saveDialogOpen = !_saveDialogOpen;
                _loadDialogOpen = false;
                GameAudio.PlayConfirm();
            }

            if (Nez.Input.IsKeyPressed(Keys.L))
            {
                _loadDialogOpen = !_loadDialogOpen;
                _saveDialogOpen = false;
                GameAudio.PlayConfirm();
            }

            if (Nez.Input.IsKeyPressed(Keys.P))
                PlayTest();

            if (Nez.Input.IsKeyPressed(Keys.Escape))
                GameNavigation.GoTo(GameSceneId.MainMenu);
        }

        /// <summary>Confirma o item da legenda (o <c>edit_kind</c> + <c>edit_put</c> do ROM).</summary>
        private void Activate()
        {
            if (_tool is 9) { _saveDialogOpen = true; _legendOpen = false; GameAudio.PlayConfirm(); return; } // "save"
            if (_tool is 10) { GameNavigation.GoTo(GameSceneId.MainMenu); return; }  // "end"

            _isEraser = false;
            Apply();
            _legendOpen = false;
        }

        /// <summary>Places the selected item in the cursor cell.</summary>
        private void Apply()
        {
            if (_isEraser)
            {
                EraseAt(_cx, _cy);
                GameAudio.PlayCursor();
                return;
            }

            int px = _cx * Cell, py = _cy * Cell;

            switch (_tool)
            {
                case 0: // put_floor1
                    Cells[_cy * Columns + _cx] = TerrainKind.Soft;
                    UpdateRoomTexture(RoomNumber);
                    break;
                case 1: // put_floor2
                    Cells[_cy * Columns + _cx] = TerrainKind.Wall;
                    UpdateRoomTexture(RoomNumber);
                    break;
                case 2: // put_ladder (ladders are 16px = 2 cells on the MSX2)
                    Cells[_cy * Columns + _cx] = TerrainKind.Ladder;
                    if (_cx + 1 < Columns)
                        Cells[_cy * Columns + _cx + 1] = TerrainKind.Ladder;
                    UpdateRoomTexture(RoomNumber);
                    break;
                case 3:                                                          // put_player
                    _startRoom = RoomNumber;
                    _startX = px;
                    _startY = py;
                    ShowStatus($"START SET: R{RoomNumber} ({px},{py})");
                    GameAudio.PlayConfirm();
                    break;
                case 7:                                                          // put_gem
                    var stone = new StagePoint(RoomNumber, px, py);
                    if (_stones.Contains(stone))
                    {
                        _stones.Remove(stone);
                        ShowStatus($"REMOVED GEM. TOTAL: {_stones.Count} (RM:{_stones.Count(s => s.Room == RoomNumber)})");
                    }
                    else
                    {
                        _stones.Add(stone);
                        ShowStatus($"ADDED GEM! TOTAL: {_stones.Count} (RM:{_stones.Count(s => s.Room == RoomNumber)})");
                    }
                    GameAudio.PlayCursor();
                    break;
                case 8:                                                          // put_exit
                    _exitRoom = RoomNumber;
                    _exitX = px;
                    _exitY = py;
                    ShowStatus($"EXIT SET: R{RoomNumber} ({px},{py})");
                    GameAudio.PlayConfirm();
                    break;
                case 4:                                                          // put_enemy (names_enemies)
                    switch (_sub[4])
                    {
                        case 0: Toggle(_coffins, RoomNumber, px, py,
                            () => new CoffinDefinition(new StagePoint(RoomNumber, px, py), EnemyKind.Mummy, 1)); break;
                        case 1: Toggle(_coffins, RoomNumber, px, py,
                            () => new CoffinDefinition(new StagePoint(RoomNumber, px, py), EnemyKind.GoldMummy, 2)); break;
                        case 2: Toggle(_mechs, RoomNumber, px, py,
                            () => new MechanismDefinition(MechanismKind.Unknown2,
                                new StagePoint(RoomNumber, px, py), 1, 1)); break;   // Pyoncy (E600 type 2)
                        default: Toggle(_mechs, RoomNumber, px, py,
                            () => new MechanismDefinition(MechanismKind.Unknown3,
                                new StagePoint(RoomNumber, px, py), 1, 1)); break;   // Rock Roll (type 3)
                    }
                    break;
                case 5:                                                          // put_trap
                    switch (_sub[5])
                    {
                        case 0: Door(0); break;      // door1 lr
                        case 1: Door(3); break;      // door1 rl
                        case 2: Door(0); break;      // door2 lr
                        case 3: Door(3); break;      // door2 rl
                        case 4: Cells[_cy * Columns + _cx] = TerrainKind.Wall; UpdateRoomTexture(RoomNumber); break;
                        case 5: Cells[_cy * Columns + _cx] = TerrainKind.Soft; UpdateRoomTexture(RoomNumber); break;
                        case 6: Toggle(_mechs, RoomNumber, px, py,
                            () => new MechanismDefinition(MechanismKind.MovableBlock,
                                new StagePoint(RoomNumber, px, py), 2, 1)); UpdateRoomTexture(RoomNumber); break;   // stone
                        default:
                            Cells[_cy * Columns + _cx] = TerrainKind.Ladder;
                            if (_cx + 1 < Columns)
                                Cells[_cy * Columns + _cx + 1] = TerrainKind.Ladder;
                            UpdateRoomTexture(RoomNumber);
                            break;
                    }
                    break;
                case 6:                                                          // put_tool (weapon)
                    var kind = (ItemKind)(_sub[6] + 1);        // knife..drill = 1..6
                    Toggle(_items, RoomNumber, px, py,
                        () => new ItemDefinition(kind, new StagePoint(RoomNumber, px, py)));
                    break;
                default:
                    GameAudio.PlayCursor();
                    return;
            }

            GameAudio.PlayConfirm();
        }

        /// <summary>Places/removes an entry at the cursor position (the same button undoes it).</summary>
        private void Toggle<T>(List<T> list, int room, int x, int y, Func<T> create)
        {
            int index = list.FindIndex(e => e switch
            {
                CoffinDefinition c => c.Position.Room == room && c.Position.X == x && c.Position.Y == y,
                ItemDefinition i => i.Position.Room == room && i.Position.X == x && i.Position.Y == y,
                MechanismDefinition m => m.Position.Room == room && m.Position.X == x && m.Position.Y == y,
                _ => false,
            });

            if (index >= 0)
                list.RemoveAt(index);
            else
                list.Add(create());
        }

        /// <summary>Erases the terrain cell and any entity at the same coordinate.</summary>
        private void EraseAt(int cx, int cy)
        {
            var old = Cells[cy * Columns + cx];
            Cells[cy * Columns + cx] = TerrainKind.Empty;
            if (old == TerrainKind.Ladder)
            {
                if (cx > 0 && Cells[cy * Columns + cx - 1] == TerrainKind.Ladder)
                    Cells[cy * Columns + cx - 1] = TerrainKind.Empty;
                else if (cx + 1 < Columns && Cells[cy * Columns + cx + 1] == TerrainKind.Ladder)
                    Cells[cy * Columns + cx + 1] = TerrainKind.Empty;
            }
            int px = cx * Cell, py = cy * Cell;
            _stones.RemoveAll(s => s.Room == RoomNumber && s.X / Cell == cx && s.Y / Cell == cy);
            _items.RemoveAll(i => i.Position.Room == RoomNumber && i.Position.X / Cell == cx && i.Position.Y / Cell == cy);
            _coffins.RemoveAll(c => c.Position.Room == RoomNumber && c.Position.X / Cell == cx && c.Position.Y / Cell == cy);
            _mechs.RemoveAll(m => m.Position.Room == RoomNumber && m.Position.X / Cell == cx && m.Position.Y / Cell == cy);
            UpdateRoomTexture(RoomNumber);
        }

        private static string ItemSprite(ItemKind kind) => kind switch
        {
            ItemKind.Knife => "knife",
            ItemKind.Boomerang => "boomerang",
            ItemKind.Shovel => "shovel",
            ItemKind.Pickaxe => "pickaxe",
            ItemKind.Hammer => "hammer",
            ItemKind.Drill => "drill",
            _ => "knife",
        };

        private static string CoffinSprite(int pyramid) => $"coffin_p{pyramid}";

        /// <summary>
        /// Draws the complete exit door with the matching golden studs (door studs)
        /// for the stage's total soul stones (gold / soul stones), identical to the original ROM.
        /// </summary>
        private void DrawExitDoor(Batcher batcher, int pyramid, float x, float y, float scale = 1.0f, Color? color = null)
        {
            MsxScreen.Draw(batcher, $"door_f0_p{pyramid}", x, y, scale: scale, color: color);
            var layout = DoorStudLayouts.For(_stones.Count);
            for (int i = 0; i < layout.Length; i++)
            {
                byte b = layout[i];
                float sx = x + (b & 3) * 8 * scale;
                float sy = y + ((b >> 2) & 3) * 8 * scale;
                MsxScreen.Draw(batcher, $"door_stud_p{pyramid}", sx, sy, scale: scale, color: color);
            }
        }

        /// <summary>Swivel door with the initial phase (3 = "rl", 0 = "lr").</summary>
        private void Door(int phase)
        {
            int px = _cx * Cell, py = _cy * Cell;
            Toggle(_mechs, RoomNumber, px, py,
                () => new MechanismDefinition(MechanismKind.RotatingDoor,
                    new StagePoint(RoomNumber, px, py), 2, 1, phase));
            UpdateRoomTexture(RoomNumber);
        }

        /// <summary>Builds the edited stage JSON in the **extractor schema** (the same the loader reads).</summary>
        private string BuildJson()
        {
            // The loader reads the extractor's raw schema (rooms[].rows = 24 strings of 32 chars).
            // We use the matching ROM stage as the metadata template if it exists, or stage01 as fallback.
            int templateNum = Math.Clamp(_currentStageNumber, 1, GameConstants.TotalStages);
            var sourcePath = System.IO.Path.Combine(RomContent.LevelsDirectory, $"stage{templateNum:D2}.json");
            if (!System.IO.File.Exists(sourcePath))
                sourcePath = System.IO.Path.Combine(RomContent.LevelsDirectory, "stage01.json");

            var root = JsonNode.Parse(System.IO.File.ReadAllText(sourcePath))!;
            root["stage"] = _currentStageNumber;
            root["pyramid"] = Pyramid;

            var rooms = root["rooms"]!.AsArray();

            // Saves the updated 8x8 roomGrid matrix
            var gridNode = new JsonArray();
            for (int r = 0; r < 8; r++)
            {
                var rowArr = new JsonArray();
                for (int c = 0; c < 8; c++)
                    rowArr.Add(_stage.RoomGrid[r, c]);
                gridNode.Add(rowArr);
            }
            root["roomGrid"] = gridNode;

            while (rooms.Count < _rooms.Length)
            {
                int newIdx = rooms.Count + 1;
                rooms.Add(new JsonObject
                {
                    ["index"] = newIdx,
                    ["rows"] = new JsonArray(),
                });
            }

            for (int i = 0; i < _rooms.Length && i < rooms.Count; i++)
            {
                var rows = new JsonArray();
                for (int y = 0; y < Rows; y++)
                {
                    var line = new System.Text.StringBuilder(Columns);
                    for (int x = 0; x < Columns; x++)
                        line.Append(RowChar(_rooms[i][y * Columns + x]));
                    rows.Add(line.ToString());
                }

                rooms[i]!["rows"] = rows;
            }

            var items = new JsonArray();
            foreach (var item in _items)
                items.Add(new JsonObject
                {
                    ["room"] = item.Position.Room, ["x"] = item.Position.X, ["y"] = item.Position.Y,
                    ["type"] = (int)item.Kind,
                });
            root["items"] = items;

            var coffins = new JsonArray();
            foreach (var coffin in _coffins)
                coffins.Add(new JsonObject
                {
                    ["room"] = coffin.Position.Room, ["x"] = coffin.Position.X, ["y"] = coffin.Position.Y,
                    ["type"] = (int)coffin.Kind, ["level"] = coffin.Level,
                });
            root["coffins"] = coffins;

            // raw = 18 hex chars; the loader reads the height at [16..17] and the door phase at [10..11].
            var mechs = new JsonArray();
            foreach (var mech in _mechs)
                mechs.Add(new JsonObject
                {
                    ["room"] = mech.Position.Room, ["x"] = mech.Position.X, ["y"] = mech.Position.Y,
                    ["type"] = (int)mech.Kind,
                    ["raw"] = $"0000000000{mech.InitialPhase:X2}0000{mech.HeightCells:X2}",
                });
            root["mechanisms"] = mechs;

            root["playerStart"] = new JsonObject
            {
                ["room"] = _startRoom, ["x"] = _startX, ["y"] = _startY,
            };

            var stones = new JsonArray();
            foreach (var s in _stones)
                stones.Add(new JsonObject { ["room"] = s.Room, ["x"] = s.X, ["y"] = s.Y });
            root["soulStones"] = stones;

            root["exitDoor"] = new JsonObject
            {
                ["room"] = _exitRoom, ["x"] = _exitX, ["y"] = _exitY,
                ["w"] = _stage.ExitDoor.WidthCells, ["h"] = _stage.ExitDoor.HeightCells,
            };

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Writes the edited stage into the active custom slot.</summary>
        private void Save() => SaveToSlot(_customSlot);

        /// <summary>
        /// **Play-test**: takes the in-memory stage to the <see cref="GameScene"/> (via
        /// <see cref="GameNavigation.StageOverride"/>), without going through disk.
        /// Preserves the editor data statically so it can be resumed if the player dies or exits.
        /// </summary>
        private void PlayTest()
        {
            var json = BuildJson();
            var def = StageLoader.Parse(json);
            LastEditedStageDef = def;
            LastEditedStageNumber = _currentStageNumber;
            LastEditedRoom = RoomNumber;
            LastEditedCustomSlot = _customSlot;
            GameNavigation.StageOverride = def;
            Console.WriteLine($"[Editor] play-test: stage {def.Number}, {def.Rooms.Count} rooms, " +
                              $"start R{_startRoom} ({def.PlayerStart.X},{def.PlayerStart.Y}), {def.SoulStones.Count} gems");
            GameNavigation.GoTo(GameSceneId.Level);
        }

        /// <summary>Terrain → JSON character: <c>'.'</c> empty, <c>'?'</c> ladder, <c>'='</c> diggable, <c>'#'</c> wall.</summary>
        private static char RowChar(TerrainKind kind) => kind switch
        {
            TerrainKind.Ladder => '?',
            TerrainKind.Soft => '=',
            TerrainKind.Wall => '#',
            _ => '.',
        };

        public override void Render(Batcher batcher, Camera camera)
        {
            int p = Pyramid;

            if (_worldViewOpen)
            {
                DrawWorldView(batcher, p);
                return;
            }

            // 1. Room background with the REAL ROM graphics assembled at runtime (mural, decorations, blocks, ladders)
            if (_roomTextures.TryGetValue(RoomNumber, out var currentRoomTex) && currentRoomTex != null && !currentRoomTex.IsDisposed)
            {
                batcher.Draw(currentRoomTex, Vector2.Zero, Color.White);
            }
            else
            {
                MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);
            }

            // 2. Subtle 8x8 grid (when G is on)
            if (_showGrid)
            {
                Color gridCol = new Color(28, 28, 42, 160);
                for (int x = 0; x <= Columns; x++)
                    MsxScreen.Fill(batcher, new Rectangle(x * Cell, 0, 1, Rows * Cell), gridCol);
                for (int y = 0; y <= Rows; y++)
                    MsxScreen.Fill(batcher, new Rectangle(0, y * Cell, Columns * Cell, 1), gridCol);
            }

            // 3. Entities in the current room with authentic ROM sprites
            // Exit Door (if it is in this room)
            if (_exitRoom == RoomNumber)
            {
                DrawExitDoor(batcher, p, _exitX, _exitY);
            }

            // Soul Stones
            foreach (var s in _stones.Where(s => s.Room == RoomNumber))
            {
                MsxScreen.Draw(batcher, $"soulstone_p{p}", s.X, s.Y);
            }

            // Sarcophagi / Enemies (Mummies use the 24px sarcophagus; Guard and Rock use their own 16x16 sprite)
            foreach (var c in _coffins.Where(c => c.Position.Room == RoomNumber))
            {
                if (c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy)
                {
                    MsxScreen.Draw(batcher, CoffinSprite(p), c.Position.X, c.Position.Y - 8);
                    string tag = c.Kind == EnemyKind.Mummy ? "M" : "G";
                    MsxScreen.Text(batcher, tag, c.Position.X + 4, c.Position.Y, Color.Yellow);
                }
                else if (c.Kind == EnemyKind.Guard)
                {
                    MsxScreen.Draw(batcher, "enemy_t3_00", c.Position.X, c.Position.Y);
                    MsxScreen.Text(batcher, "P", c.Position.X + 4, c.Position.Y, Color.Yellow);
                }
                else // Rock
                {
                    MsxScreen.Draw(batcher, "enemy_t4_00", c.Position.X, c.Position.Y);
                    MsxScreen.Text(batcher, "R", c.Position.X + 4, c.Position.Y, Color.Yellow);
                }
            }

            // Items (weapons and tools)
            foreach (var item in _items.Where(i => i.Position.Room == RoomNumber))
            {
                var sprite = ItemSprite(item.Kind);
                if (!string.IsNullOrEmpty(sprite))
                    MsxScreen.Draw(batcher, sprite, item.Position.X, item.Position.Y);
            }

            // Mecanismos e Armadilhas
            foreach (var m in _mechs.Where(m => m.Position.Room == RoomNumber))
            {
                if (m.Kind == MechanismKind.RotatingDoor)
                {
                    MsxScreen.Fill(batcher, new Rectangle(m.Position.X, m.Position.Y, 16, 16), new Color(40, 40, 80));
                    Outline(batcher, new Rectangle(m.Position.X, m.Position.Y, 16, 16), Color.Cyan);
                    string phase = m.InitialPhase == 3 ? "RL" : "LR";
                    MsxScreen.Text(batcher, phase, m.Position.X + 1, m.Position.Y + 4, Color.White);
                }
                else if (m.Kind == MechanismKind.MovableBlock)
                {
                    for (int i = 0; i < 4; i++)
                        MsxScreen.Draw(batcher, $"soft_p{p}_{i}", m.Position.X + (i % 2) * 8, m.Position.Y + (i / 2) * 8);
                }
            }

            // Player Start (Vick)
            if (_startRoom == RoomNumber)
            {
                MsxScreen.Draw(batcher, "vick_s0_00", _startX, _startY - 8);
                Outline(batcher, new Rectangle(_startX - 1, _startY - 9, 18, 26), Color.Cyan);
            }

            // 3.5 Visual indicators for multiple rooms and edge portals
            (int curGx, int curGy) = FindRoom(RoomNumber);
            int upRoom = (curGy > 0) ? _stage.RoomGrid[curGy - 1, curGx] : 0;
            int downRoom = (curGy < 5) ? _stage.RoomGrid[curGy + 1, curGx] : 0;
            int leftRoom = (curGx > 0) ? _stage.RoomGrid[curGy, curGx - 1] : 0;
            int rightRoom = (curGx < 5) ? _stage.RoomGrid[curGy, curGx + 1] : 0;

            // Top marker: level totals and room navigation
            if (upRoom > 0)
            {
                const int bx = 94, by = 2, bw = 68, bh = 9;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(14, 20, 36));
                Outline(batcher, new Rectangle(bx, by, bw, bh), Color.Cyan);
                MsxScreen.MiniText(batcher, $"▲ ROOM {upRoom}", bx + 10, by + 2, Color.Yellow);
            }
            else
            {
                // Level summary at the top: always visible
                string topInfo = _rooms.Length > 1
                    ? $"R{RoomNumber}/{_rooms.Length} | GEMS:{_stones.Count} | FOES:{_coffins.Count}"
                    : $"S{_currentStageNumber:D2} | GEMS:{_stones.Count} | FOES:{_coffins.Count} | ITEMS:{_items.Count}";
                int bw = topInfo.Length * 4 + 8;
                int bx = (GameConstants.DesignWidth - bw) / 2;
                const int by = 2, bh = 9;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(14, 14, 30));
                Outline(batcher, new Rectangle(bx, by, bw, bh), new Color(70, 70, 130));
                MsxScreen.MiniText(batcher, topInfo, bx + 4, by + 2, Color.Gold);
            }

            // Bottom marker (▼ ROOM {downRoom}) right above the HUD
            if (downRoom > 0)
            {
                const int bx = 94, by = 182, bw = 68, bh = 9;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(14, 20, 36));
                Outline(batcher, new Rectangle(bx, by, bw, bh), Color.Cyan);
                MsxScreen.MiniText(batcher, $"▼ ROOM {downRoom}", bx + 10, by + 2, Color.Yellow);
            }

            // Left marker (◄ R{leftRoom})
            if (leftRoom > 0)
            {
                const int bx = 2, by = 90, bw = 26, bh = 9;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(14, 20, 36));
                Outline(batcher, new Rectangle(bx, by, bw, bh), Color.Cyan);
                MsxScreen.MiniText(batcher, $"◄ R{leftRoom}", bx + 2, by + 2, Color.Yellow);
            }

            // Right marker (R{rightRoom} ►)
            if (rightRoom > 0)
            {
                const int bx = 228, by = 90, bw = 26, bh = 9;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(14, 20, 36));
                Outline(batcher, new Rectangle(bx, by, bw, bh), Color.Cyan);
                MsxScreen.MiniText(batcher, $"R{rightRoom} ►", bx + 2, by + 2, Color.Yellow);
            }

            if (_rooms.Length > 1)
            {
                const int by = 2, bh = 9;
                if (_startRoom != RoomNumber)
                {
                    MsxScreen.Fill(batcher, new Rectangle(2, by, 52, bh), new Color(14, 20, 36));
                    Outline(batcher, new Rectangle(2, by, 52, bh), new Color(40, 80, 120));
                    MsxScreen.MiniText(batcher, $"START:R{_startRoom}", 4, by + 2, Color.Cyan);
                }

                if (_exitRoom != RoomNumber)
                {
                    MsxScreen.Fill(batcher, new Rectangle(202, by, 52, bh), new Color(24, 14, 36));
                    Outline(batcher, new Rectangle(202, by, 52, bh), new Color(120, 60, 40));
                    MsxScreen.MiniText(batcher, $"EXIT:R{_exitRoom}", 204, by + 2, Color.Orange);
                }
            }

            // 4. Translucent ghost preview of the selected tool under the cursor
            int curX = _cx * Cell, curY = _cy * Cell;
            Color ghostColor = Color.White * 0.55f;
            if (_isEraser)
            {
                MsxScreen.Text(batcher, "X", curX + 1, curY, Color.Red * 0.85f);
            }
            else
            {
                switch (_tool)
                {
                    case 0: MsxScreen.Draw(batcher, $"soft_p{p}_0", curX, curY, color: ghostColor); break;
                    case 1: MsxScreen.Draw(batcher, $"wall_p{p}_0", curX, curY, color: ghostColor); break;
                    case 2: MsxScreen.Draw(batcher, $"ladder_p{p}", curX, curY, color: ghostColor); break;
                    case 3: MsxScreen.Draw(batcher, "vick_s0_00", curX, curY - 8, color: ghostColor); break;
                    case 7: MsxScreen.Draw(batcher, $"soulstone_p{p}", curX, curY, color: ghostColor); break;
                    case 8: DrawExitDoor(batcher, p, curX, curY, color: ghostColor); break;
                    case 4:
                        if (_sub[4] is 0 or 1)
                            MsxScreen.Draw(batcher, CoffinSprite(p), curX, curY - 8, color: ghostColor);
                        else if (_sub[4] == 2)
                            MsxScreen.Draw(batcher, "enemy_t3_00", curX, curY, color: ghostColor);
                        else
                            MsxScreen.Draw(batcher, "enemy_t4_00", curX, curY, color: ghostColor);
                        break;
                    case 6:
                        MsxScreen.Draw(batcher, ItemSprite((ItemKind)(_sub[6] + 1)), curX, curY, color: ghostColor);
                        break;
                }
            }

            var cursor = new Rectangle(curX, curY, Cell, Cell);
            Outline(batcher, cursor, Color.White);

            // 5. Rich, interactive HUD with validation
            DrawEditorHud(batcher, p);

            if (_legendOpen)
                DrawLegend(batcher);

            if (_mapOpen)
                DrawMap(batcher);

            if (_helpOpen)
                DrawHelp(batcher);

            if (_saveDialogOpen)
                DrawSaveDialog(batcher);

            if (_loadDialogOpen)
                DrawLoadDialog(batcher);
        }

        private void DrawEditorHud(Batcher batcher, int p)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, HudY, GameConstants.DesignWidth, 20), new Color(12, 12, 22));
            MsxScreen.Fill(batcher, new Rectangle(0, HudY, GameConstants.DesignWidth, 1), new Color(40, 40, 70));
            MsxScreen.Fill(batcher, new Rectangle(0, HudY + 10, GameConstants.DesignWidth, 1), new Color(24, 24, 42));

            // HUD line 1: Toolbar with 9 visual buttons (28px each)
            int activeIndex = CurrentToolbarIndex;
            for (int i = 0; i < 9; i++)
            {
                int bx = 2 + i * 28;
                int by = HudY + 1;
                bool isSel = activeIndex == i;

                var bg = isSel ? new Color(50, 70, 140) : new Color(18, 18, 30);
                var border = isSel ? Color.Gold : new Color(42, 42, 60);
                MsxScreen.Fill(batcher, new Rectangle(bx, by, 26, 8), bg);
                Outline(batcher, new Rectangle(bx, by, 26, 8), border);

                // Shortcut number in MiniText (3x5)
                MsxScreen.MiniText(batcher, (i + 1).ToString(), bx + 2, by + 2, isSel ? Color.Yellow : Color.LightGray);

                // Matching icon (with scale 0.5f to fit 8x8 when it is a 16x16 sprite)
                switch (i)
                {
                    case 0: MsxScreen.Draw(batcher, $"soft_p{p}_0", bx + 14, by); break;
                    case 1: MsxScreen.Draw(batcher, $"wall_p{p}_0", bx + 14, by); break;
                    case 2: MsxScreen.Draw(batcher, $"ladder_p{p}", bx + 14, by); break;
                    case 3: MsxScreen.MiniText(batcher, "X", bx + 16, by + 2, Color.Red); break;
                    case 4: MsxScreen.MiniText(batcher, "V", bx + 16, by + 2, Color.Cyan); break;
                    case 5: MsxScreen.MiniText(batcher, "D", bx + 16, by + 2, Color.Orange); break;
                    case 6: MsxScreen.Draw(batcher, $"soulstone_p{p}", bx + 13, by, scale: 0.5f); break;
                    case 7:
                        string eTag = _sub[4] switch { 0 => "M", 1 => "G", 2 => "P", _ => "R" };
                        MsxScreen.MiniText(batcher, eTag, bx + 16, by + 2, Color.Yellow);
                        break;
                    case 8:
                        var itemSpr = ItemSprite((ItemKind)(_sub[6] + 1));
                        if (!string.IsNullOrEmpty(itemSpr))
                            MsxScreen.Draw(batcher, itemSpr, bx + 13, by, scale: 0.5f);
                        break;
                }
            }

            // HUD line 2 (Y = 204): detailed status and quick shortcuts with crisp MiniText
            int y2 = HudY + 12;
            if (_statusTimer > 0 && !string.IsNullOrEmpty(_statusMessage))
            {
                MsxScreen.MiniText(batcher, _statusMessage, 4, y2, Color.Yellow);
            }
            else
            {
                // [S01]
                MsxScreen.MiniText(batcher, $"[S{_currentStageNumber:D2}]", 2, y2, Color.White);

                // [R1/3]
                string roomText = _rooms.Length > 1 ? $"[R{RoomNumber}/{_rooms.Length}]" : $"[R{RoomNumber}]";
                Color roomColor = _rooms.Length > 1 ? Color.Cyan : Color.Gray;
                MsxScreen.MiniText(batcher, roomText, 24, y2, roomColor);

                // Description of the active tool with the correct level totals
                string desc = _isEraser ? "[4]ERASER" : _tool switch
                {
                    0 => "[1]SOFT",
                    1 => "[2]SOLID WALL",
                    2 => "[3]LADDER",
                    3 => _startRoom == RoomNumber ? "[5]START" : $"[5]START:R{_startRoom}",
                    8 => _exitRoom == RoomNumber ? $"[6]EXIT(GEMS:{_stones.Count})" : $"[6]EXIT:R{_exitRoom}",
                    7 => $"[7]GEMS:{_stones.Count}(RM:{_stones.Count(s => s.Room == RoomNumber)})",
                    4 => $"[8]FOE:{SubTypes[4][_sub[4]][..3].ToUpperInvariant()} T:{_coffins.Count}",
                    6 => $"[9]ITEM:{SubTypes[6][_sub[6]][..3].ToUpperInvariant()} T:{_items.Count}",
                    _ => Legend[_tool].ToUpperInvariant(),
                };
                Color descCol = _isEraser ? Color.Salmon : _tool switch
                {
                    0 => Color.Gold,
                    1 => Color.White,
                    2 => Color.Yellow,
                    3 => Color.Cyan,
                    8 => Color.Orange,
                    7 => _stones.Count > 0 ? Color.LightGreen : Color.Red,
                    4 => Color.Yellow,
                    6 => Color.LightBlue,
                    _ => Color.White,
                };
                MsxScreen.MiniText(batcher, desc, 44, y2, descCol);

                // Action buttons on the right with a guaranteed 4px gap between each and zero overlap
                MsxScreen.MiniText(batcher, "[R]MAP", 118, y2, Color.Gold);
                MsxScreen.MiniText(batcher, "[V]VIEW", 146, y2, Color.Cyan);
                MsxScreen.MiniText(batcher, "[L]LOAD", 174, y2, Color.Yellow);
                MsxScreen.MiniText(batcher, "[S]SAVE", 202, y2, Color.LightGreen);
                MsxScreen.MiniText(batcher, "[P]PLAY", 230, y2, Color.LightBlue);
            }
        }

        private void DrawHelp(Batcher batcher)
        {
            const int ox = 18, oy = 14, w = 220, h = 164;
            MsxScreen.Fill(batcher, new Rectangle(ox, oy, w, h), new Color(12, 12, 32));
            Outline(batcher, new Rectangle(ox, oy, w, h), Color.Gold);
            MsxScreen.Text(batcher, "EDITOR SHORTCUTS", ox + 40, oy + 6, Color.Gold);

            int y = oy + 22;
            const int step = 10;
            MsxScreen.MiniText(batcher, "CLICK / DRAG : PAINT CELLS", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "R-CLICK / X  : ERASE AT CURSOR", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "1 - 9        : SELECT TOOLBAR ITEM", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "MOUSE WHEEL  : CYCLE TOOLBAR ITEMS", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "G            : TOGGLE 8x8 GRID", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "TAB          : TOOL PALETTE / SUBTYPES", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "R / M        : ROOM OVERVIEW (MAP)", ox + 8, y, Color.Gold); y += step;
            MsxScreen.MiniText(batcher, "V            : FULL WORLD PREVIEW (ZOOM/PAN)", ox + 8, y, Color.Cyan); y += step;
            MsxScreen.MiniText(batcher, "+ / - / WHL  : ZOOM IN / OUT (25% - 200%)", ox + 8, y, Color.Cyan); y += step;
            MsxScreen.MiniText(batcher, "DRAG MOUSE   : SCROLL / PAN WORLD PREVIEW", ox + 8, y, Color.Cyan); y += step;
            MsxScreen.MiniText(batcher, "EDGE PORTALS : CLICK SCREEN EDGES TO SWITCH ROOMS", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "Q / E        : PREV / NEXT ROOM", ox + 8, y, Color.White); y += step;
            MsxScreen.MiniText(batcher, "S / F5       : SAVE CUSTOM LEVEL (SLOTS 1-10)", ox + 8, y, Color.LightGreen); y += step;
            MsxScreen.MiniText(batcher, "L            : LOAD CUSTOM SLOT OR ROM TEMPLATE", ox + 8, y, Color.Yellow); y += step;
            MsxScreen.MiniText(batcher, "P            : PLAYTEST LEVEL (RETURN ON DEATH)", ox + 8, y, Color.LightBlue); y += step;
            MsxScreen.MiniText(batcher, "ESC / H      : CLOSE HELP", ox + 8, y, Color.Gold);
        }

        private void DrawSaveDialog(Batcher batcher)
        {
            const int ox = 16, oy = 20, w = 224, h = 160;
            MsxScreen.Fill(batcher, new Rectangle(ox, oy, w, h), new Color(12, 14, 28));
            Outline(batcher, new Rectangle(ox, oy, w, h), Color.Gold);
            MsxScreen.Text(batcher, "SAVE CUSTOM LEVEL", ox + 36, oy + 6, Color.Gold);

            MsxScreen.MiniText(batcher, "SELECT SLOT (1-9, 0=SLOT 10):", ox + 10, oy + 22, Color.White);

            for (int slot = 1; slot <= 10; slot++)
            {
                int col = (slot - 1) / 5;
                int row = (slot - 1) % 5;
                int sx = ox + 10 + col * 104;
                int sy = oy + 36 + row * 20;

                var path = System.IO.Path.Combine(RomContent.CustomLevelsDirectory, $"custom_stage_{slot:D2}.json");
                bool exists = System.IO.File.Exists(path);
                bool isCurrent = slot == _customSlot;

                var bgCol = isCurrent ? new Color(40, 60, 110) : new Color(20, 22, 38);
                var borderCol = isCurrent ? Color.Gold : (exists ? new Color(50, 80, 120) : new Color(34, 34, 50));
                MsxScreen.Fill(batcher, new Rectangle(sx, sy, 98, 16), bgCol);
                Outline(batcher, new Rectangle(sx, sy, 98, 16), borderCol);

                string keyChar = slot == 10 ? "0" : slot.ToString();
                MsxScreen.MiniText(batcher, $"[{keyChar}] SLOT {slot:D2}", sx + 4, sy + 3, isCurrent ? Color.Yellow : Color.White);

                if (exists)
                {
                    MsxScreen.MiniText(batcher, "*SAVED", sx + 62, sy + 3, Color.LightGreen);
                }
                else
                {
                    MsxScreen.MiniText(batcher, "[EMPTY]", sx + 58, sy + 3, Color.Gray);
                }
            }

            MsxScreen.MiniText(batcher, "* ROM STAGES (1-60) ARE NEVER OVERWRITTEN.", ox + 10, oy + 138, Color.Cyan);
            MsxScreen.MiniText(batcher, "CLICK / PRESS NUMBER TO SAVE | [ESC] CANCEL", ox + 10, oy + 148, Color.LightGray);
        }

        private void DrawLoadDialog(Batcher batcher)
        {
            const int ox = 16, oy = 20, w = 224, h = 160;
            MsxScreen.Fill(batcher, new Rectangle(ox, oy, w, h), new Color(12, 14, 28));
            Outline(batcher, new Rectangle(ox, oy, w, h), Color.Gold);
            MsxScreen.Text(batcher, "LOAD LEVEL", ox + 68, oy + 6, Color.Gold);

            // Abas: [C] CUSTOM SLOTS | [R] ROM TEMPLATES
            bool isCustom = _loadTab == 0;
            var tab1Bg = isCustom ? new Color(40, 60, 110) : new Color(20, 22, 38);
            var tab2Bg = !isCustom ? new Color(40, 60, 110) : new Color(20, 22, 38);
            MsxScreen.Fill(batcher, new Rectangle(ox + 10, oy + 20, 96, 12), tab1Bg);
            Outline(batcher, new Rectangle(ox + 10, oy + 20, 96, 12), isCustom ? Color.Gold : Color.Gray);
            MsxScreen.MiniText(batcher, "[C] CUSTOM SLOTS", ox + 14, oy + 22, isCustom ? Color.Yellow : Color.White);

            MsxScreen.Fill(batcher, new Rectangle(ox + 116, oy + 20, 96, 12), tab2Bg);
            Outline(batcher, new Rectangle(ox + 116, oy + 20, 96, 12), !isCustom ? Color.Gold : Color.Gray);
            MsxScreen.MiniText(batcher, "[R] ROM TEMPLATES", ox + 120, oy + 22, !isCustom ? Color.Yellow : Color.White);

            if (isCustom)
            {
                // Lists the 10 custom slots
                for (int slot = 1; slot <= 10; slot++)
                {
                    int col = (slot - 1) / 5;
                    int row = (slot - 1) % 5;
                    int sx = ox + 10 + col * 104;
                    int sy = oy + 38 + row * 18;

                    var path = System.IO.Path.Combine(RomContent.CustomLevelsDirectory, $"custom_stage_{slot:D2}.json");
                    bool exists = System.IO.File.Exists(path);
                    bool isCurrent = slot == _customSlot;

                    var bgCol = isCurrent ? new Color(40, 60, 110) : new Color(20, 22, 38);
                    var borderCol = isCurrent ? Color.Gold : (exists ? new Color(50, 80, 120) : new Color(34, 34, 50));
                    MsxScreen.Fill(batcher, new Rectangle(sx, sy, 98, 15), bgCol);
                    Outline(batcher, new Rectangle(sx, sy, 98, 15), borderCol);

                    string keyChar = slot == 10 ? "0" : slot.ToString();
                    MsxScreen.MiniText(batcher, $"[{keyChar}] SLOT {slot:D2}", sx + 4, sy + 3, exists ? (isCurrent ? Color.Yellow : Color.LightGreen) : Color.Gray);

                    if (exists)
                        MsxScreen.MiniText(batcher, "READY", sx + 64, sy + 3, Color.Cyan);
                    else
                        MsxScreen.MiniText(batcher, "EMPTY", sx + 64, sy + 3, Color.DarkGray);
                }

                MsxScreen.MiniText(batcher, "CLICK / PRESS 1-9, 0 TO LOAD SLOT | [ESC] CANCEL", ox + 10, oy + 144, Color.LightGray);
            }
            else
            {
                // Seletor de Templates da ROM (Stages 1 a 60)
                int st = _romTemplateStage;
                int py = Math.Clamp((st - 1) / 10 + 1, 1, 6);

                MsxScreen.MiniText(batcher, "USE A REAL ROM LEVEL AS A STARTING POINT:", ox + 10, oy + 42, Color.White);

                // Central selection box with arrows
                int bx = ox + 24, by = oy + 58, bw = 176, bh = 42;
                MsxScreen.Fill(batcher, new Rectangle(bx, by, bw, bh), new Color(18, 22, 40));
                Outline(batcher, new Rectangle(bx, by, bw, bh), Color.Gold);

                MsxScreen.MiniText(batcher, "◄ [Q]", bx + 8, by + 16, Color.Gold);
                MsxScreen.Text(batcher, $"STAGE {st:D2}", bx + 48, by + 14, Color.Yellow);
                MsxScreen.MiniText(batcher, "[E] ►", bx + 142, by + 16, Color.Gold);

                MsxScreen.MiniText(batcher, $"PYRAMID {py} | ◄ ► ARROWS TO BROWSE", bx + 24, by + 30, Color.LightBlue);

                MsxScreen.Fill(batcher, new Rectangle(ox + 48, oy + 110, 128, 16), new Color(30, 80, 50));
                Outline(batcher, new Rectangle(ox + 48, oy + 110, 128, 16), Color.LightGreen);
                MsxScreen.MiniText(batcher, "[ENTER] LOAD TEMPLATE", ox + 60, oy + 114, Color.White);

                MsxScreen.MiniText(batcher, "* ROM STAGES ARE PROTECTED AND NEVER OVERWRITTEN.", ox + 10, oy + 134, Color.Cyan);
                MsxScreen.MiniText(batcher, "[ESC] CANCEL", ox + 88, oy + 146, Color.Gray);
            }
        }

        /// <summary>Position (column, row) of a room in the stage's 8×8 grid.</summary>
        private (int X, int Y) FindRoom(int id)
        {
            // The ROM draws the 6x6 map of E788 (edit_legend 0x867D); the port's grid is 8x8,
            // but the official rooms sit in the first 6 rows/columns.
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    if (_stage.RoomGrid[y, x] == id)
                        return (x, y);
            return (0, 0);
        }

        /// <summary>Overview of all rooms of the pyramid with real thumbnails and a [+] button to create new rooms.</summary>
        private void DrawMap(Batcher batcher)
        {
            const int mw = 32, mh = 24, gap = 4, ox = 20, oy = 26;

            // Dark background with a golden frame
            MsxScreen.Fill(batcher, new Rectangle(12, 10, 232, 172), new Color(10, 10, 26));
            Outline(batcher, new Rectangle(12, 10, 232, 172), Color.Gold);

            // Header
            MsxScreen.Text(batcher, "STAGE ROOM OVERVIEW", 48, 14, Color.Gold);

            var wallCol = new Color(110, 130, 170);
            var softCol = new Color(190, 140, 50);
            var ladderCol = new Color(240, 220, 80);

            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    int rx = ox + x * (mw + gap);
                    int ry = oy + y * (mh + gap);
                    int id = _stage.RoomGrid[y, x];

                    if (id > 0 && id <= _rooms.Length)
                    {
                        // Room background
                        MsxScreen.Fill(batcher, new Rectangle(rx, ry, mw, mh), new Color(16, 16, 32));

                        // Renders a pixel-by-pixel thumbnail of the room
                        var rCells = _rooms[id - 1];
                        for (int py = 0; py < Rows; py++)
                        {
                            for (int px = 0; px < Columns; px++)
                            {
                                var terrain = rCells[py * Columns + px];
                                if (terrain == TerrainKind.Wall)
                                    MsxScreen.Fill(batcher, new Rectangle(rx + px, ry + py, 1, 1), wallCol);
                                else if (terrain == TerrainKind.Soft)
                                    MsxScreen.Fill(batcher, new Rectangle(rx + px, ry + py, 1, 1), softCol);
                                else if (terrain == TerrainKind.Ladder)
                                    MsxScreen.Fill(batcher, new Rectangle(rx + px, ry + py, 1, 1), ladderCol);
                            }
                        }

                        // Player Start if it is in this room
                        if (_startRoom == id)
                        {
                            int sx = Math.Clamp(_startX / 8, 0, mw - 2);
                            int sy = Math.Clamp(_startY / 8, 0, mh - 2);
                            MsxScreen.Fill(batcher, new Rectangle(rx + sx, ry + sy, 2, 2), Color.Cyan);
                        }

                        // Exit door if it is in this room
                        if (_exitRoom == id)
                        {
                            int ex = Math.Clamp(_exitX / 8, 0, mw - 2);
                            int ey = Math.Clamp(_exitY / 8, 0, mh - 2);
                            MsxScreen.Fill(batcher, new Rectangle(rx + ex, ry + ey, 2, 2), Color.Orange);
                        }

                        // Soulstones in this room
                        foreach (var s in _stones.Where(st => st.Room == id))
                        {
                            int stx = Math.Clamp(s.X / 8, 0, mw - 1);
                            int sty = Math.Clamp(s.Y / 8, 0, mh - 1);
                            MsxScreen.Fill(batcher, new Rectangle(rx + stx, ry + sty, 1, 1), Color.Gold);
                        }

                        // Enemies in this room
                        foreach (var c in _coffins.Where(cf => cf.Position.Room == id))
                        {
                            int cx = Math.Clamp(c.Position.X / 8, 0, mw - 1);
                            int cy = Math.Clamp(c.Position.Y / 8, 0, mh - 1);
                            MsxScreen.Fill(batcher, new Rectangle(rx + cx, ry + cy, 1, 1), Color.Red);
                        }

                        // Room frame: golden for the current room, blue for the others
                        bool isCurrent = id == RoomNumber;
                        Outline(batcher, new Rectangle(rx, ry, mw, mh), isCurrent ? Color.Gold : new Color(50, 70, 120));

                        // R{id} label in MiniText in the top corner
                        MsxScreen.MiniText(batcher, $"R{id}", rx + 2, ry + 2, isCurrent ? Color.Yellow : Color.White);
                    }
                    else
                    {
                        // Empty cell: draws an add [+] button if adjacent to an existing room
                        bool hasNeighbor = (y > 0 && _stage.RoomGrid[y - 1, x] > 0) ||
                                           (y < 4 && _stage.RoomGrid[y + 1, x] > 0) ||
                                           (x > 0 && _stage.RoomGrid[y, x - 1] > 0) ||
                                           (x < 5 && _stage.RoomGrid[y, x + 1] > 0) ||
                                           _rooms.Length == 1; // If there is only 1 room, any empty neighbor

                        if (hasNeighbor)
                        {
                            MsxScreen.Fill(batcher, new Rectangle(rx, ry, mw, mh), new Color(16, 20, 32));
                            Outline(batcher, new Rectangle(rx, ry, mw, mh), new Color(40, 60, 90));
                            MsxScreen.MiniText(batcher, "[+]", rx + 10, ry + 9, Color.LightGreen);
                        }
                    }
                }
            }

            // Cursor do teclado
            int curRx = ox + _mapX * (mw + gap);
            int curRy = oy + _mapY * (mh + gap);
            Outline(batcher, new Rectangle(curRx - 1, curRy - 1, mw + 2, mh + 2), Color.White);

            // Explanatory footer with a shortcut to the World Preview
            MsxScreen.MiniText(batcher, "[CLICK] ROOM | [+] ADD | [V] WORLD PREVIEW | [ESC] CLOSE", 6, 168, Color.LightGray);
        }

        private void DrawWorldView(Batcher batcher, int p)
        {
            // Dark background of the pyramid
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), new Color(8, 8, 16));

            var wallCol = new Color(110, 130, 170);
            var softCol = new Color(190, 140, 50);
            var ladderCol = new Color(240, 220, 80);

            // Renders each linked room at its exact position in the world
            for (int gy = 0; gy < 6; gy++)
            {
                for (int gx = 0; gx < 6; gx++)
                {
                    int id = _stage.RoomGrid[gy, gx];
                    if (id <= 0 || id > _rooms.Length)
                        continue;

                    float roomWorldX = gx * 256f;
                    float roomWorldY = gy * 192f;
                    float sx = 128f + (roomWorldX - _worldCamX) * _worldZoom;
                    float sy = 106f + (roomWorldY - _worldCamY) * _worldZoom;
                    float sw = 256f * _worldZoom;
                    float sh = 192f * _worldZoom;

                    // Frustum culling
                    if (sx + sw < 0 || sx > GameConstants.DesignWidth || sy + sh < 0 || sy > GameConstants.DesignHeight)
                        continue;

                    // Room background with the REAL ROM graphics assembled at runtime at any zoom level!
                    if (_roomTextures.TryGetValue(id, out var roomTex) && roomTex != null && !roomTex.IsDisposed)
                    {
                        batcher.Draw(roomTex, new Vector2(sx, sy), null, Color.White, 0f, Vector2.Zero, _worldZoom, SpriteEffects.None, 0f);
                    }
                    else
                    {
                        MsxScreen.Fill(batcher, new Rectangle((int)sx, (int)sy, (int)sw, (int)sh), new Color(14, 14, 28));
                    }

                    // Player Start if it is in this room
                    if (_startRoom == id)
                    {
                        float px = sx + _startX * _worldZoom;
                        float py = sy + (_startY - 8) * _worldZoom;
                        MsxScreen.Draw(batcher, "vick_s0_00", px, py, scale: _worldZoom);
                    }

                    // Exit door if it is in this room
                    if (_exitRoom == id)
                    {
                        float ex = sx + _exitX * _worldZoom;
                        float ey = sy + _exitY * _worldZoom;
                        DrawExitDoor(batcher, p, ex, ey, scale: _worldZoom);
                    }

                    // Soulstones in this room
                    foreach (var s in _stones.Where(st => st.Room == id))
                    {
                        float stx = sx + s.X * _worldZoom;
                        float sty = sy + s.Y * _worldZoom;
                        MsxScreen.Draw(batcher, $"soulstone_p{p}", stx, sty, scale: _worldZoom);
                    }

                    // Enemies in this room
                    foreach (var c in _coffins.Where(cf => cf.Position.Room == id))
                    {
                        string spr = c.Kind switch
                        {
                            EnemyKind.Guard => "enemy_t3_00",
                            EnemyKind.Rock => "enemy_t4_00",
                            _ => CoffinSprite(p)
                        };
                        float cOffsetY = (c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy) ? -8f : 0f;
                        float cx = sx + c.Position.X * _worldZoom;
                        float cy = sy + (c.Position.Y + cOffsetY) * _worldZoom;
                        MsxScreen.Draw(batcher, spr, cx, cy, scale: _worldZoom);
                    }

                    // Items in this room
                    foreach (var item in _items.Where(it => it.Position.Room == id))
                    {
                        var ispr = ItemSprite(item.Kind);
                        if (!string.IsNullOrEmpty(ispr))
                            MsxScreen.Draw(batcher, ispr, sx + item.Position.X * _worldZoom, sy + item.Position.Y * _worldZoom, scale: _worldZoom);
                    }

                    // Mechanisms in this room
                    foreach (var m in _mechs.Where(mc => mc.Position.Room == id))
                    {
                        if (m.Kind == MechanismKind.RotatingDoor)
                        {
                            int mw = (int)(16 * _worldZoom), mh = (int)(16 * _worldZoom);
                            MsxScreen.Fill(batcher, new Rectangle((int)(sx + m.Position.X * _worldZoom), (int)(sy + m.Position.Y * _worldZoom), mw, mh), new Color(40, 40, 80));
                            Outline(batcher, new Rectangle((int)(sx + m.Position.X * _worldZoom), (int)(sy + m.Position.Y * _worldZoom), mw, mh), Color.Cyan);
                        }
                        else if (m.Kind == MechanismKind.MovableBlock)
                        {
                            for (int bi = 0; bi < 4; bi++)
                                MsxScreen.Draw(batcher, $"soft_p{p}_{bi}", sx + (m.Position.X + (bi % 2) * 8) * _worldZoom, sy + (m.Position.Y + (bi / 2) * 8) * _worldZoom, scale: _worldZoom);
                        }
                    }

                    // Room frame
                    bool isCurrent = id == RoomNumber;
                    Outline(batcher, new Rectangle((int)sx, (int)sy, (int)sw, (int)sh), isCurrent ? Color.Gold : new Color(60, 90, 160));

                    // Room label
                    MsxScreen.MiniText(batcher, $"ROOM {id}", (int)sx + 2, (int)sy + 2, isCurrent ? Color.Yellow : Color.White);
                }
            }

            // Translucent top HUD of the World Preview (Y: 0..14)
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, 14), new Color(10, 14, 26));
            MsxScreen.Fill(batcher, new Rectangle(0, 14, GameConstants.DesignWidth, 1), new Color(50, 70, 120));

            string zoomText = $"{_worldZoom * 100:0}%";
            MsxScreen.MiniText(batcher, $"VIEW [{zoomText}]", 4, 4, Color.Gold);
            MsxScreen.MiniText(batcher, "DRAG:MOVE  CLICK ROOM:EDIT", 58, 4, Color.LightGray);

            // Quick control buttons at the top right: [-] [+] [ESC]
            var mousePos = KingsValley.Engine.Input.UnifiedPointer.Position;
            bool hoverMinus = mousePos.Y >= 2 && mousePos.Y <= 14 && mousePos.X >= 204 && mousePos.X <= 218;
            bool hoverPlus = mousePos.Y >= 2 && mousePos.Y <= 14 && mousePos.X >= 220 && mousePos.X <= 234;
            bool hoverClose = mousePos.Y >= 2 && mousePos.Y <= 14 && mousePos.X >= 236 && mousePos.X <= 254;

            MsxScreen.Fill(batcher, new Rectangle(204, 2, 14, 10), hoverMinus ? new Color(40, 60, 100) : new Color(20, 24, 40));
            Outline(batcher, new Rectangle(204, 2, 14, 10), hoverMinus ? Color.White : Color.Cyan);
            MsxScreen.MiniText(batcher, "-", 209, 4, Color.White);

            MsxScreen.Fill(batcher, new Rectangle(220, 2, 14, 10), hoverPlus ? new Color(40, 60, 100) : new Color(20, 24, 40));
            Outline(batcher, new Rectangle(220, 2, 14, 10), hoverPlus ? Color.White : Color.Cyan);
            MsxScreen.MiniText(batcher, "+", 225, 4, Color.White);

            MsxScreen.Fill(batcher, new Rectangle(236, 2, 18, 10), hoverClose ? new Color(80, 30, 40) : new Color(45, 18, 25));
            Outline(batcher, new Rectangle(236, 2, 18, 10), hoverClose ? Color.White : Color.Red);
            MsxScreen.MiniText(batcher, "ESC", 238, 4, hoverClose ? Color.White : Color.Salmon);
        }

        private void DrawLegend(Batcher batcher)
        {
            int p = Pyramid;
            MsxScreen.Fill(batcher, new Rectangle(140, 4, 114, 172), new Color(16, 16, 40));
            Outline(batcher, new Rectangle(140, 4, 114, 172), Color.Gold);
            MsxScreen.Text(batcher, "TOOL PALETTE", 146, 8, Color.Gold);

            for (int i = 0; i < Legend.Length; i++)
            {
                int y = 22 + i * 12;
                var color = i == _tool ? Color.Gold : Color.White;
                if (i == _tool)
                    MsxScreen.Fill(batcher, new Rectangle(142, y - 1, 110, 11), new Color(60, 60, 110));

                DrawToolIcon(batcher, 144, y, i, p);
                MsxScreen.Text(batcher, Legend[i], 156, y, color);
            }

            var legendSubs = SubTypes[_tool];
            if (legendSubs.Length > 0)
                MsxScreen.MiniText(batcher, $"{Legend[_tool]}: {legendSubs[_sub[_tool]]} (◄ ► SUB)", 4, 4, Color.Gold);
            else if (Help[_tool] is { Length: > 0 } help)
                MsxScreen.MiniText(batcher, help, 4, 4, new Color(180, 220, 255));

            MsxScreen.MiniText(batcher, "UP/DN:TOOL  ◄►:SUB  ESC:CLOSE", 144, 162, Color.Gray);
        }

        private void DrawToolIcon(Batcher batcher, int x, int y, int toolIndex, int p)
        {
            switch (toolIndex)
            {
                case 0: MsxScreen.Draw(batcher, $"soft_p{p}_0", x, y); break;
                case 1: MsxScreen.Draw(batcher, $"wall_p{p}_0", x, y); break;
                case 2: MsxScreen.Draw(batcher, $"ladder_p{p}", x, y); break;
                case 3: MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, 6, 6), Color.Cyan); break;
                case 4: MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, 6, 6), Color.Yellow); break;
                case 5: MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, 6, 6), Color.Magenta); break;
                case 6:
                    var itemSpr = ItemSprite((ItemKind)(_sub[6] + 1));
                    if (!string.IsNullOrEmpty(itemSpr))
                        MsxScreen.Draw(batcher, itemSpr, x, y, scale: 0.5f);
                    break;
                case 7: MsxScreen.Draw(batcher, $"soulstone_p{p}", x, y, scale: 0.5f); break;
                case 8: MsxScreen.Fill(batcher, new Rectangle(x + 1, y + 1, 6, 6), Color.Orange); break;
                case 9: MsxScreen.MiniText(batcher, "*", x + 2, y + 1, Color.LightGreen); break;
                case 10: MsxScreen.MiniText(batcher, "X", x + 2, y + 1, Color.Red); break;
            }
        }

        private static void Outline(Batcher batcher, Rectangle r, Color color)
        {
            MsxScreen.Fill(batcher, new Rectangle(r.X, r.Y, r.Width, 1), color);
            MsxScreen.Fill(batcher, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), color);
            MsxScreen.Fill(batcher, new Rectangle(r.X, r.Y, 1, r.Height), color);
            MsxScreen.Fill(batcher, new Rectangle(r.Right - 1, r.Y, 1, r.Height), color);
        }

        private static Color TerrainColor(TerrainKind kind) => kind switch
        {
            TerrainKind.Soft => new Color(196, 132, 66),     // diggable block (brick)
            TerrainKind.Wall => new Color(150, 150, 170),    // indestructible wall
            TerrainKind.Ladder => new Color(240, 210, 90),   // ladder
            _ => Color.Transparent,
        };
    }
}
