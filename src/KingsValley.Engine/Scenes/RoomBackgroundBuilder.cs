#nullable enable
using System;
using System.IO;
using System.Text.Json;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Engine.Assets;
using KingsValley.Core.Rooms;
using KingsValley.Core.Stages;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Loads the room assembly data (<c>Content/Rom/Rooms/</c>) and assembles the 256x192 texture
/// from <see cref="KingsValley.Core.Rooms.RoomAssembler"/>. Animates the color (routine <c>5C14</c>)
/// every frame, rebuilding the texture when the palette changes.
/// </summary>
public sealed class RoomBackgroundBuilder
{
    private readonly GraphicsDevice _device;
    private static string RoomsDir => RomContent.RoomsDirectory;
    private byte[]? _blocks;
    private JsonDocument? _murals;
    private JsonDocument? _decorations;
    private JsonDocument? _palettes;
    private JsonDocument? _animations;
    private PaletteAnimator? _animator;
    private int _animatorPyramid;

    private Texture2D? _texture;
    private readonly Color[] _pixels = new Color[GameConstants.DesignWidth * GameConstants.PlayfieldHeight];

    private Texture2D? _msx1Texture;
    private int _msx1Stage = -1;
    private int _msx1Room = -1;
    private (int Stage, int Room) _msx1Warned = (-1, -1);

    /// <summary>
    /// Resets cached room textures and assembly assets so they are re-read for the active art set (F11 hot-reload).
    /// </summary>
    public void Reset()
    {
        _blocks = null;
        _murals?.Dispose();
        _murals = null;
        _decorations?.Dispose();
        _decorations = null;
        _palettes?.Dispose();
        _palettes = null;
        _animations?.Dispose();
        _animations = null;
        _animator = null;

        _texture?.Dispose();
        _texture = null;

        _msx1Texture?.Dispose();
        _msx1Texture = null;
        _msx1Stage = -1;
        _msx1Room = -1;
        _msx1BasePixels = null;
        _msx1Pixels = null;
        _msx1Palette = null;
        _msx1Murals = null;
        _msx1CleanDoor = null;
        _msx1Animator = null;
        _msx1Warned = (-1, -1);

        _mechanismPhaseTilesLoaded = false;
        _mechanismPhaseTiles = null;
        _mechanismTile4Loaded = false;
        _mechanismTile4 = null;
        _mechanism2PhaseTilesLoaded = false;
        _mechanism2PhaseTiles = null;
    }

    private static string SharedRoomsDir => RoomsDir;
    private Color[]? _msx1BasePixels;
    private Color[]? _msx1Pixels;
    private Color[]? _msx1Palette;
    private byte[][]? _msx1Murals;
    private Msx1PatternAnimator? _msx1Animator;
    private int _msx1MapVersion = -1;
    private bool _msx1OpaqueObjects=true;
    private DigCrumble? _msx1Crumble;
    private int _msx1MechanismPhaseSum = -1;
    private int _msx1SoulStoneCount = -1;
    private int _msx1ItemCount = -1;

    private void RestoreMsx1Cell(int col, int row, int pyr, StageSession s)
    {
        var origKind = s.Definition.Room(s.CurrentRoom).Cells[row * GameConstants.RoomColumns + col];
        if (origKind == TerrainKind.Soft && s.Map.At(col * 8, row * 8) == TerrainKind.Soft)
            RestoreMsx1SoftCell(col, row);
        else
            RestoreMsx1MuralCell(col, row, pyr);
    }

    private void RestoreMsx1SoftCell(int col, int row)
    {
        if (_msx1Pixels is null || _msx1Palette is null) return;
        for (int r = 0; r < 8; r++)
        {
            int py = row * 8 + r;
            if (py < 0 || py >= GameConstants.PlayfieldHeight) continue;
            byte palIdx = r == 7 ? (byte)1 : (r % 2 == 0 ? (byte)11 : (byte)10);
            Color colVal = palIdx < _msx1Palette.Length ? _msx1Palette[palIdx] : Color.Black;
            for (int c = 0; c < 8; c++)
            {
                int px = col * 8 + c;
                if (px < 0 || px >= GameConstants.DesignWidth) continue;
                _msx1Pixels[py * GameConstants.DesignWidth + px] = colVal;
            }
        }
    }

    private void RestoreMsx1MuralCell(int col, int row, int pyr, Color[]? targetPixels = null)
    {
        var pixels = targetPixels ?? _msx1Pixels;
        if (pixels is null || _msx1Palette is null || _msx1Murals is null || pyr < 1 || pyr > _msx1Murals.Length)
            return;

        int muralCol = col % 8;
        int muralRow = row % 4;
        byte[] mural = _msx1Murals[pyr - 1];

        for (int r = 0; r < 8; r++)
        {
            int py = row * 8 + r;
            if (py < 0 || py >= GameConstants.PlayfieldHeight) continue;

            int my = muralRow * 8 + r;
            for (int c = 0; c < 8; c++)
            {
                int px = col * 8 + c;
                if (px < 0 || px >= GameConstants.DesignWidth) continue;

                int mx = muralCol * 8 + c;
                byte palIdx = mural[my * 64 + mx];
                Color colVal = palIdx < _msx1Palette.Length ? _msx1Palette[palIdx] : Color.Black;
                pixels[py * GameConstants.DesignWidth + px] = colVal;
            }
        }
    }

    private KingsValley.Core.Screens.ScreenData? _msx1CleanDoor;

    private void RestoreMsx1CleanDoorBlock(int doorX, int doorY, int b)
    {
        if (_msx1Pixels is null || _msx1Palette is null) return;
        if (_msx1CleanDoor == null)
        {
            string path=RomContent.RoomSupportPath("door_clean.bin");
            if(!File.Exists(path)) throw new InvalidDataException("The native MSX1 clean door table is missing.");
            _msx1CleanDoor=KingsValley.Core.Screens.ScreenAssembler.DecodeFile(path);
            if(_msx1CleanDoor.Width!=32 || _msx1CleanDoor.Height!=32) throw new InvalidDataException("The native clean door table must be 32 by 32 pixels.");
        }
        int col=(b&3)*8,row=((b>>2)&3)*8;
        for(int y=0;y<8;y++) for(int x=0;x<8;x++)
        {
            int px=doorX+col+x,py=doorY+row+y;
            if(px<0 || px>=256 || py<0 || py>=192) continue;
            byte index=_msx1CleanDoor.Indices[(row+y)*32+col+x];
            var color=_msx1CleanDoor.Palette[index];
            _msx1Pixels[py*256+px]=new Color(color.R,color.G,color.B,color.A);
        }
    }

    private static void BlitMsx1Tile(Color[] pixels, byte[] tile, int x, int y, Color[]? palette) =>
        BlitMsx1TileWide(pixels, tile, x, y, 16, palette);

    private static void BlitMsx1TileWide(Color[] pixels, byte[] tile, int x, int y, int width, Color[]? palette)
    {
        if (palette is null) return;
        int height = tile.Length * 2 / width;
        for (int r = 0; r < height; r++)
        {
            int py = y + r;
            if (py < 0 || py >= GameConstants.PlayfieldHeight) continue;

            for (int c = 0; c < width; c++)
            {
                int px = x + c;
                if (px < 0 || px >= GameConstants.DesignWidth) continue;

                byte b = tile[r * (width / 2) + c / 2];
                byte p = (byte)(c % 2 == 0 ? b >> 4 : b & 0x0F);
                if (p != 0 && p < palette.Length)
                    pixels[py * GameConstants.DesignWidth + px] = palette[p];
            }
        }
    }

    /// <summary>
    /// Room in MSX1 mode: loads the converted VRAM dump (<c>Rooms/dumps/sNN_rM.bin</c>),
    /// animates background patterns (eyes, torches), restores dug/crumbling tiles to mural,
    /// and renders active mechanism phases.
    /// Returns <c>null</c> when that room's dump does not exist yet.
    /// </summary>
    private Texture2D? UpdateMsx1(StageSession s, int stage, int room)
    {
        EnsureLoaded();

        _msx1Animator ??= new Msx1PatternAnimator();
        _msx1Animator.EnsureLoaded(Path.GetDirectoryName(RomContent.RoomSupportPath("animations.json")) ?? RoomsDir);
        int pyr = (stage - 1) / 10 + 1;
        float dt = Nez.Time.DeltaTime;
        bool animChanged = _msx1Animator.Update(dt, pyr);
        bool roomChanged = _msx1Texture is null || _msx1Stage != stage || _msx1Room != room;

        if (_msx1Murals is null)
        {
            var muralsPath = RomContent.RoomSupportPath("murals.bin");
            if (!File.Exists(muralsPath))
                muralsPath = Path.Combine(AudioBootstrap.ExtractedDirectory, "Rooms", "murals.bin");
            if (!File.Exists(muralsPath))
                muralsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "RomMsx1", "Rooms", "murals.bin");

            if (File.Exists(muralsPath))
            {
                var rawMurals = File.ReadAllBytes(muralsPath);
                _msx1Murals = new byte[6][];
                for (int p = 0; p < 6; p++)
                {
                    _msx1Murals[p] = new byte[64 * 32];
                    for (int i = 0; i < 1024; i++)
                    {
                        int bIdx = p * 1024 + i;
                        if (bIdx < rawMurals.Length)
                        {
                            byte b = rawMurals[bIdx];
                            _msx1Murals[p][i * 2] = (byte)(b >> 4);
                            _msx1Murals[p][i * 2 + 1] = (byte)(b & 0x0F);
                        }
                    }
                }
            }
        }

        if (roomChanged)
        {
            var path = Path.Combine(RoomsDir, "dumps", $"s{stage:D2}_r{room:D2}.bin");
            if (!File.Exists(path))
                return null;

            var raw = File.ReadAllBytes(path);
            var data = KingsValley.Core.Screens.ScreenAssembler.Decode(raw);
            _msx1BasePixels ??= new Color[data.Width * data.Height];
            _msx1Pixels ??= new Color[data.Width * data.Height];
            _msx1Palette = new Color[data.Palette.Length];
            for (int i = 0; i < data.Palette.Length; i++)
            {
                var sc = data.Palette[i];
                _msx1Palette[i] = new Color(sc.R, sc.G, sc.B, sc.A);
            }

            for (int i = 0; i < data.Width * data.Height; i++)
            {
                var sc = data.Palette[data.Indices[i] & 0x0F];
                _msx1BasePixels[i] = new Color(sc.R, sc.G, sc.B, sc.A);
            }

            _msx1Texture?.Dispose();
            _msx1Texture = new Texture2D(_device, data.Width, data.Height);
            _msx1Stage = stage;
            _msx1Room = room;
            _msx1MapVersion = -1;
            _msx1Crumble = null;
            _msx1MechanismPhaseSum = -1;
            _msx1SoulStoneCount = -1;
            _msx1ItemCount = -1;
        }

        int mechPhaseSum = 0;
        foreach (var m in s.RoomMechanisms)
            mechPhaseSum = (mechPhaseSum * 7) + s.MechanismPhase(m) + (s.IsMechanismSpent(m) ? 100 : 0);

        bool mapChanged = _msx1MapVersion != s.MapVersion;
        bool crumbleChanged = _msx1Crumble != s.Crumble;
        bool mechChanged = _msx1MechanismPhaseSum != mechPhaseSum;
        bool entitiesChanged = _msx1SoulStoneCount != s.SoulStones.Count || _msx1ItemCount != s.Items.Count;
        bool backdropChanged=_msx1OpaqueObjects!=RomContent.Msx1OpaqueBackdrop;

        if (_msx1Texture is not null && _msx1BasePixels is not null && _msx1Pixels is not null &&
            (roomChanged || animChanged || mapChanged || crumbleChanged || mechChanged || entitiesChanged || backdropChanged))
        {
            Array.Copy(_msx1BasePixels, _msx1Pixels, _msx1BasePixels.Length);
            string roomKey = $"s{stage:D2}_r{room:D2}";
            _msx1Animator.Apply(roomKey, pyr, _msx1Pixels, _msx1Palette);

            if (_msx1Murals is not null)
            {
                if(!RomContent.Msx1OpaqueBackdrop)
                {
                    // Optional presentation: remove baked native object backgrounds before drawing
                    // their transparent atlas copies. Terrain underneath is restored from ROM tiles.
                    foreach(var coffin in s.Coffins.Where(c=>c.Position.Room==room &&
                        c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy))
                        for(int y=coffin.Position.Y-8;y<coffin.Position.Y+16;y+=8)
                            for(int x=coffin.Position.X;x<coffin.Position.X+16;x+=8)
                                RestoreMsx1MuralCell(x/8,y/8,pyr);
                    var exit=s.Definition.ExitDoor.Position;
                    if(exit.Room==room)
                        for(int y=exit.Y;y<exit.Y+32;y+=8)for(int x=exit.X;x<exit.X+32;x+=8)
                            RestoreMsx1MuralCell(x/8,y/8,pyr);
                }
                // Collectibles are absent from native room dumps. Dynamic sprites handle them;
                // replacing their cells with a generic mural here would erase real decorations.

                // Restore clean door blocks for collected soulstones
                var door = s.Definition.ExitDoor.Position;
                if (door.Room == room && _msx1Palette is not null && RomContent.Msx1OpaqueBackdrop)
                {
                    var layout = DoorStudLayouts.For(s.SoulStoneTotal);
                    for (int i = s.DoorStuds; i < s.SoulStoneTotal && i < layout.Length; i++)
                    {
                        int b = layout[i];
                        RestoreMsx1CleanDoorBlock(door.X, door.Y, b);
                    }
                }

                // Restore mural for dug tiles:
                var roomDef = s.Definition.Room(room);
                for (int r = 0; r < GameConstants.RoomRows; r++)
                {
                    for (int c = 0; c < GameConstants.RoomColumns; c++)
                    {
                        var origKind = roomDef.Cells[r * GameConstants.RoomColumns + c];
                        if (origKind is TerrainKind.Soft or TerrainKind.Wall && s.Map.At(c * 8, r * 8) == TerrainKind.Empty)
                        {
                            RestoreMsx1MuralCell(c, r, pyr);
                        }
                    }
                }

                // Restore mural for crumbling tiles:
                if (s.Crumble is { Stage: >= 0 } cr)
                {
                    int c0 = cr.X >> 3, r0 = cr.Y >> 3;
                    RestoreMsx1MuralCell(c0, r0, pyr);
                    int c1 = (cr.Floor ? cr.X + 8 : cr.X) >> 3;
                    int r1 = (cr.Floor ? cr.Y : cr.Y + 8) >> 3;
                    RestoreMsx1MuralCell(c1, r1, pyr);
                }

                // Swivel doors (MechanismKind.RotatingDoor, MechanismKind.Unknown2) and trap bars (Unknown4):
                var tiles = MechanismPhaseTiles();
                var tile4 = MechanismTile4();
                foreach (var m in s.RoomMechanisms)
                {
                    if (s.IsMechanismSpent(m)) continue;
                    if (m.Kind == MechanismKind.RotatingDoor && tiles is not null)
                    {
                        int phase = s.MechanismPhase(m);
                        int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
                        for (int r = r0; r < r0 + m.HeightCells; r++)
                            for (int c = c0; c < c0 + m.WidthCells; c++)
                                RestoreMsx1MuralCell(c, r, pyr);

                        BlitMsx1Tile(_msx1Pixels, tiles[phase], m.Position.X, m.Position.Y, _msx1Palette);
                    }
                    else if (m.Kind == MechanismKind.Unknown2)
                    {
                        var tiles2 = Mechanism2PhaseTiles(m.Direction);
                        if (tiles2 is not null)
                        {
                            int phase = s.MechanismPhase(m);
                            int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
                            for (int r = r0; r < r0 + m.HeightCells; r++)
                                for (int c = c0; c < c0 + m.WidthCells; c++)
                                    RestoreMsx1MuralCell(c, r, pyr);

                            BlitMsx1Tile(_msx1Pixels, tiles2[phase], m.Position.X, m.Position.Y, _msx1Palette);
                        }
                    }
                    else if (m.Kind == MechanismKind.Unknown4 && tile4 is not null)
                    {
                        BlitMsx1TileWide(_msx1Pixels, tile4, m.Position.X, m.Position.Y, 32, _msx1Palette);
                    }
                }
            }

            _msx1Texture.SetData(_msx1Pixels);
            _msx1MapVersion = s.MapVersion;
            _msx1OpaqueObjects=RomContent.Msx1OpaqueBackdrop;
            _msx1Crumble = s.Crumble;
            _msx1MechanismPhaseSum = mechPhaseSum;
            _msx1SoulStoneCount = s.SoulStones.Count;
            _msx1ItemCount = s.Items.Count;
        }

        return _msx1Texture;
    }

    public RoomBackgroundBuilder(GraphicsDevice device)
    {
        _device = device;
    }

    private void EnsureLoaded()
    {
        if (_blocks is not null)
            return;
        string dir = File.Exists(Path.Combine(RoomsDir, "blocks_p1.bin")) ? RoomsDir : SharedRoomsDir;
        var blocksPath = Path.Combine(dir, "blocks_p1.bin");
        if (!File.Exists(blocksPath))
            return;
        using var ms = new MemoryStream();
        for (int p = 1; p <= 6; p++)
        {
            var path = Path.Combine(dir, $"blocks_p{p}.bin");
            if (File.Exists(path))
                ms.Write(File.ReadAllBytes(path));
            else
                throw new InvalidDataException($"Missing block table for pyramid {p}.");
        }
        _blocks = ms.ToArray();
        string muralsFile = Path.Combine(dir, "murals.json");
        if (File.Exists(muralsFile))
            _murals = JsonDocument.Parse(File.ReadAllText(muralsFile));
        string decsFile = Path.Combine(dir, "decorations.json");
        if (File.Exists(decsFile))
            _decorations = JsonDocument.Parse(File.ReadAllText(decsFile));
        string palFile = Path.Combine(dir, "palettes.json");
        if (File.Exists(palFile))
            _palettes = JsonDocument.Parse(File.ReadAllText(palFile));
        var animFile = File.Exists(Path.Combine(RoomsDir, "animations.json")) ? Path.Combine(RoomsDir, "animations.json") : Path.Combine(dir, "animations.json");
        if (File.Exists(animFile))
            _animations = JsonDocument.Parse(File.ReadAllText(animFile));
        else
            _animations = JsonDocument.Parse("{}");
    }

    /// <summary>Updates the animation and rebuilds the texture if needed. Returns the current texture.</summary>
    public Texture2D? Update(StageSession? s)
    {
        if (s is null)
            return _texture;

        var def = s.Definition;
        int stage = def.Number;
        int room = s.CurrentRoom;

        // MSX1 variant: the room comes from an emulator dump (SCREEN 2), not from the MSX2 assembly.
        if (RomContent.Flavor == ArtFlavor.Msx1)
        {
            var msx1 = UpdateMsx1(s, stage, room);
            if (msx1 is not null)
                return msx1;

            if (_msx1Warned != (stage, room))
            {
                _msx1Warned = (stage, room);
                Console.WriteLine($"[RoomBackgroundBuilder] Missing MSX1 dump for stage {stage} room {room}; the opposite flavor is not a fallback.");
            }
            return null;
        }

        EnsureLoaded();
        if (_blocks is null || _murals is null || _decorations is null || _palettes is null || _animations is null)
            return _texture;

        int pyramid = def.Pyramid;

        // (re)creates the animator when the pyramid changes
        if (_animator is null || _animatorPyramid != pyramid)
        {
            _animator = new PaletteAnimator();
            _animator.Load(_device, pyramid, _animations, _palettes);
            _animatorPyramid = pyramid;
        }

        bool paletteChanged = _animator.Tick();

        // rebuilds if there is no texture, if the room changed, if the palette changed, if the terrain changed (dug)
        // or if the crumbling block changed stage (the original restores the background on each stage, 56DE)
        if (_texture is null || _currentStage != stage || _currentRoom != room || paletteChanged
            || _currentMapVersion != s.MapVersion || _currentCrumble != s.Crumble)
        {
            Rebuild(s, pyramid, stage, room);
            _currentStage = stage;
            _currentRoom = room;
            _currentMapVersion = s.MapVersion;
            _currentCrumble = s.Crumble;
        }

        return _texture;
    }

    /// <summary>Forces the background builder to rebuild on the next Update call.</summary>
    public void Invalidate()
    {
        _currentStage = -1;
        _currentRoom = -1;
        _currentMapVersion = -1;
        _msx1Stage = -1;
        _msx1Room = -1;
        _msx1MapVersion = -1;
    }

    /// <summary>
    /// Builds the 256x192 texture with the REAL ROM graphics (mural, decorations, 4bpp blocks, ladders and palette)
    /// for an arbitrary cell map (used in the editor and the world view at every zoom level).
    /// </summary>
    public Texture2D BuildCustomRoomTexture(
        int pyramid,
        int stage,
        int room,
        TerrainKind[] map,
        IReadOnlyList<LadderDefinition>? ladders = null,
        IReadOnlyList<LadderDefinition>? goldenLadders = null,
        IReadOnlyList<MechanismDefinition>? mechanisms = null,
        Texture2D? targetTexture = null)
    {
        EnsureLoaded();
        if (_blocks is null || _murals is null || _decorations is null || _palettes is null)
            throw new InvalidOperationException("Room assets not loaded.");

        int pyr = Math.Clamp(pyramid, 1, 6);
        var blocks = new byte[8192];
        Array.Copy(_blocks, (pyr - 1) * 8192, blocks, 0, 8192);

        var muralCodes = GetMuralCodes(pyr, stage);
        var decs = GetDecorations(stage);

        var mapClone = (TerrainKind[])map.Clone();
        if (mechanisms != null && mechanisms.Count > 0)
        {
            foreach (var m in mechanisms.Where(m => m.Position.Room == room))
            {
                int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
                for (int r = r0; r < r0 + m.HeightCells; r++)
                    for (int c = c0; c < c0 + m.WidthCells; c++)
                    {
                        if (c >= 0 && c < GameConstants.RoomColumns && r >= 0 && r < GameConstants.RoomRows)
                            mapClone[r * GameConstants.RoomColumns + c] = TerrainKind.Empty;
                    }
            }
        }

        var assembled = RoomAssembler.Assemble(
            pyramid: pyr,
            stage: stage,
            room: room,
            blocks: blocks,
            muralCodes: muralCodes,
            decorations: decs,
            map: mapClone,
            ladders: ladders ?? Array.Empty<LadderDefinition>(),
            goldenLadders: goldenLadders ?? Array.Empty<LadderDefinition>());

        if (mechanisms != null && mechanisms.Count > 0)
        {
            var tiles = MechanismPhaseTiles();
            var tile4 = MechanismTile4();
            foreach (var m in mechanisms.Where(m => m.Position.Room == room))
            {
                if (m.Kind == MechanismKind.RotatingDoor && tiles is not null)
                    BlitTile(assembled, tiles[0], m.Position.X, m.Position.Y);
                else if (m.Kind == MechanismKind.Unknown2)
                {
                    var tiles2 = Mechanism2PhaseTiles(m.Direction);
                    if (tiles2 is not null)
                        BlitTile(assembled, tiles2[0], m.Position.X, m.Position.Y);
                }
                else if (m.Kind == MechanismKind.Unknown4 && tile4 is not null)
                    BlitTileWide(assembled, tile4, m.Position.X, m.Position.Y, 32);
            }
        }

        var palEl = _palettes.RootElement.GetProperty(pyr.ToString());
        var palette = new Color[16];
        int i = 0;
        foreach (var c in palEl.EnumerateArray())
            palette[i++] = new Color(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());

        var texture = targetTexture;
        if (texture is null || texture.IsDisposed)
            texture = new Texture2D(_device, GameConstants.DesignWidth, GameConstants.PlayfieldHeight);

        var pixels = new Color[assembled.Length];
        for (int p = 0; p < assembled.Length; p++)
            pixels[p] = palette[assembled[p] & 0x0F];

        texture.SetData(pixels);
        return texture;
    }

    private int[] GetMuralCodes(int pyramid, int stage)
    {
        if (_murals == null) return new int[32];
        string parity = stage % 2 == 0 ? "even" : "odd";
        if (_murals.RootElement.TryGetProperty(pyramid.ToString(), out var pyrEl) && pyrEl.TryGetProperty(parity, out var codesEl))
            return codesEl.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (_murals.RootElement.TryGetProperty($"p{pyramid}_{parity}", out var flatEl))
            return flatEl.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        return new int[32];
    }

    private List<DecorationEntry> GetDecorations(int stage)
    {
        if (_decorations != null && _decorations.RootElement.TryGetProperty(stage.ToString(), out var decEl))
            return decEl.EnumerateArray().Select(ToDecoration).ToList();
        return new List<DecorationEntry>();
    }

    private int _currentStage = -1;
    private int _currentRoom = -1;
    private int _currentMapVersion = -1;
    private DigCrumble? _currentCrumble;

    private void Rebuild(StageSession s, int pyramid, int stage, int room)
    {
        if(_mechanismPyramid!=pyramid)
        {
            _mechanismPyramid=pyramid;
            _mechanismPhaseTilesLoaded=false;_mechanismPhaseTiles=null;
            _mechanism2PhaseTilesLoaded=false;_mechanism2PhaseTiles=null;
            _mechanismTile4Loaded=false;_mechanismTile4=null;
        }
        var def = s.Definition;
        var blocks = new byte[8192];
        Array.Copy(_blocks!, (pyramid - 1) * 8192, blocks, 0, 8192);

        var muralCodes = GetMuralCodes(pyramid, stage);
        var decs = GetDecorations(stage);

        // The terrain comes from the session's MUTABLE map (what the player digs), not from the immutable definition.
        // The cells that are crumbling show the BACKGROUND (the original restores the mural on each stage,
        // 56DE) - so the assembly receives them as empty; the crumbling frame is a sprite.
        var map = s.Map.Snapshot();
        if (s.Crumble is { Stage: >= 0 } cr)
        {
            ClearCell(map, cr.X, cr.Y);
            ClearCell(map, cr.Floor ? cr.X + 8 : cr.X, cr.Floor ? cr.Y : cr.Y + 8);
        }

        // The original writes wall into the mechanism's cells, but the mechanism itself is drawn on
        // top (with the MURAL around it): so the assembly sees those cells as empty and the mechanism
        // tile is blitted at the end.
        var mechanisms = s.RoomMechanisms.ToList();
        foreach (var m in mechanisms)
        {
            int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
            for (int r = r0; r < r0 + m.HeightCells; r++)
                for (int c = c0; c < c0 + m.WidthCells; c++)
                    ClearCellAt(map, c, r);
        }

        var assembled = RoomAssembler.Assemble(
            pyramid: pyramid, stage: stage, room: room,
            blocks: blocks, muralCodes: muralCodes, decorations: decs,
            map: map, ladders: def.Ladders, goldenLadders: def.GoldenLadders);

        // The mechanism (push door) on top of the mural: a 16x24 crop per phase.
        // The type 4 mechanism (Unknown4) is a bar 4 cells wide (32x8) of block 0xAD.
        if (mechanisms.Count > 0)
        {
            var tiles = MechanismPhaseTiles();
            var tile4 = MechanismTile4();
            foreach (var m in mechanisms)
            {
                if (s.IsMechanismSpent(m))
                    continue;

                if (m.Kind == MechanismKind.RotatingDoor && tiles is not null)
                    BlitTile(assembled, tiles[s.MechanismPhase(m)], m.Position.X, m.Position.Y);
                else if (m.Kind == MechanismKind.Unknown2)
                {
                    var tiles2 = Mechanism2PhaseTiles(m.Direction);
                    if (tiles2 is not null)
                        BlitTile(assembled, tiles2[s.MechanismPhase(m)], m.Position.X, m.Position.Y);
                }
                else if (m.Kind == MechanismKind.Unknown4 && tile4 is not null)
                    BlitTileWide(assembled, tile4, m.Position.X, m.Position.Y, 32);
            }
        }

        var palEl = _palettes!.RootElement.GetProperty(pyramid.ToString());
        var palette = new Color[16];
        int i = 0;
        foreach (var c in palEl.EnumerateArray())
            palette[i++] = new Color(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());

        // applies the animated colors
        if (_animator is not null && _animator.HasSlots)
        {
            for (int ci = 0; ci < 16; ci++)
                palette[ci] = _animator.GetColor(ci, palette[ci]);
        }

        if (_texture is null || _texture.IsDisposed)
            _texture = new Texture2D(_device, GameConstants.DesignWidth, GameConstants.PlayfieldHeight);

        for (int p = 0; p < assembled.Length; p++)
            _pixels[p] = palette[assembled[p] & 0x0F];
        _texture.SetData(_pixels);
    }

    private static void ClearCell(TerrainKind[] map, int x, int y) => ClearCellAt(map, x >> 3, y >> 3);

    private static void ClearCellAt(TerrainKind[] map, int col, int row)
    {
        if (col < 0 || col >= GameConstants.RoomColumns || row < 0 || row >= GameConstants.RoomRows)
            return;
        map[row * GameConstants.RoomColumns + col] = TerrainKind.Empty;
    }

    private byte[][]? _mechanismPhaseTiles;
    private bool _mechanismPhaseTilesLoaded;
    private int _mechanismPyramid=1;
    private string MechanismDirectory=>RomContent.Flavor==ArtFlavor.Msx2
        ?Path.Combine(RoomsDir,"Mechanisms",$"p{_mechanismPyramid}"):RoomsDir;

    /// <summary>16x24 crops of the type 1 mechanism per phase (0..3), 0 = transparent.</summary>
    private byte[][]? MechanismPhaseTiles()
    {
        if (!_mechanismPhaseTilesLoaded)
        {
            _mechanismPhaseTilesLoaded = true;
            string dir = MechanismDirectory;
            var list = new List<byte[]>();
            for (int p = 0; p < 4; p++)
            {
                var path = Path.Combine(dir, $"mechanism_t1_p{p}.bin");
                var bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
                list.Add(bytes.Length >= 192 ? bytes : Array.Empty<byte>());
            }
            _mechanismPhaseTiles = list.ToArray();
        }

        return _mechanismPhaseTiles;
    }

    private byte[][][]? _mechanism2PhaseTiles;
    private bool _mechanism2PhaseTilesLoaded;

    /// <summary>16x24 crops of the type 2 mechanism (one-way swivel door) per direction (0..1) and phase (0..3).</summary>
    private byte[][]? Mechanism2PhaseTiles(int dir)
    {
        if (!_mechanism2PhaseTilesLoaded)
        {
            _mechanism2PhaseTilesLoaded = true;
            string dirPath = MechanismDirectory;
            _mechanism2PhaseTiles = new byte[2][][];
            for (int d = 0; d < 2; d++)
            {
                var list = new List<byte[]>();
                for (int p = 0; p < 4; p++)
                {
                    var path = Path.Combine(dirPath, $"mechanism_t2_d{d}_p{p}.bin");
                    var bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
                    list.Add(bytes.Length >= 192 ? bytes : Array.Empty<byte>());
                }
                _mechanism2PhaseTiles[d] = list.ToArray();
            }
        }

        int clampedDir = Math.Clamp(dir, 0, 1);
        return _mechanism2PhaseTiles?[clampedDir];
    }

    /// <summary>Blits the mechanism tile (transparent where the value is 0) into the room index buffer.</summary>
    private byte[]? _mechanismTile4;
    private bool _mechanismTile4Loaded;

    /// <summary>32x8 tile of the type 4 mechanism (4 copies of block 0xAD), 0 = transparent.</summary>
    private byte[]? MechanismTile4()
    {
        if (!_mechanismTile4Loaded)
        {
            _mechanismTile4Loaded = true;
            string dir = MechanismDirectory;
            var path = Path.Combine(dir, "mechanism_t4.bin");
            _mechanismTile4 = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        return _mechanismTile4 is { Length: >= 128 } ? _mechanismTile4 : null;
    }

    private static void BlitTile(byte[] img, byte[] tile, int x, int y)
    {
        int height = tile.Length * 2 / 16;
        for (int r = 0; r < height; r++)
        {
            if (y + r < 0 || y + r >= GameConstants.PlayfieldHeight)
                continue;
            for (int c = 0; c < 16; c++)
            {
                if (x + c < 0 || x + c >= GameConstants.DesignWidth)
                    continue;
                byte b = tile[r * 8 + c / 2];
                byte p = (byte)(c % 2 == 0 ? b >> 4 : b & 0x0F);
                if (p != 0)
                    img[(y + r) * GameConstants.DesignWidth + x + c] = p;
            }
        }
    }

    private static void BlitTileWide(byte[] img, byte[] tile, int x, int y, int width)
    {
        int height = tile.Length * 2 / width;
        for (int r = 0; r < height; r++)
        {
            if (y + r < 0 || y + r >= GameConstants.PlayfieldHeight)
                continue;
            for (int c = 0; c < width; c++)
            {
                if (x + c < 0 || x + c >= GameConstants.DesignWidth)
                    continue;
                byte b = tile[r * (width / 2) + c / 2];
                byte p = (byte)(c % 2 == 0 ? b >> 4 : b & 0x0F);
                if (p != 0)
                    img[(y + r) * GameConstants.DesignWidth + x + c] = p;
            }
        }
    }

    private static DecorationEntry ToDecoration(JsonElement e) =>
        new(
            Kind: e.GetProperty("kind").GetInt32(),
            Shape: e.GetProperty("shape").GetString() ?? "grid",
            Room: e.GetProperty("room").GetInt32(),
            Y: e.GetProperty("y").GetInt32(),
            X: e.GetProperty("x").GetInt32(),
            Rows: e.GetProperty("rows").GetInt32(),
            Cols: e.GetProperty("cols").GetInt32(),
            Height: e.GetProperty("height").GetInt32(),
            Blocks: e.GetProperty("blocks").EnumerateArray().Select(b => b.GetInt32()).ToArray());
}
