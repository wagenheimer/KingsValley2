#nullable enable
using KingsValley.Core.Rooms;
using KingsValley.Core.Screens;
using KingsValley.Core.Stages;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Scenes;

/// <summary>Adapts native MSX1 murals and atlas tiles to the shared room compositor.</summary>
public sealed class Msx1EditorRoomAssembler
{
    private readonly byte[][] _blocks = new byte[6][];
    private readonly string _graphicsDirectory;
    private readonly bool[] _goldenReady = new bool[6];
    private static readonly int[] MuralCodes = Enumerable.Range(128, 32).ToArray();
    private static readonly ScreenColor[] Palette = Tms9918.Palette.Select(c => new ScreenColor(c.R, c.G, c.B)).ToArray();

    public Msx1EditorRoomAssembler(string graphicsDirectory)
        : this(Path.Combine(graphicsDirectory, "Art", "kv2.atlas"), Path.Combine(graphicsDirectory, "Rooms", "murals.bin")) { }

    public Msx1EditorRoomAssembler(string atlas, string muralsPath)
    {
        _graphicsDirectory=Path.GetDirectoryName(Path.GetDirectoryName(muralsPath)!)!;
        byte[] murals = File.ReadAllBytes(muralsPath);
        if (murals.Length != 6144) throw new InvalidDataException("MSX1 murals must contain six native 64 by 32 images.");
        var image = PngRgba.Decode(File.ReadAllBytes(Path.ChangeExtension(atlas, ".png")));
        var regions = AtlasInventory.Read(File.ReadAllText(atlas), image).ToDictionary(r => r.Name);
        for (int pyramid = 1; pyramid <= 6; pyramid++)
        {
            byte[] blocks = _blocks[pyramid - 1] = new byte[8192];
            // These are compositor slots, populated only with decoded cartridge pixels.
            foreach (var tile in new[] { (Name: "soft", Code: 5), (Name: "wall", Code: 0x5E), (Name: "ladder", Code: 3), (Name: "ladder_alt", Code: 4) })
            {
                string name = $"{tile.Name}_p{pyramid}" + (tile.Name is "soft" or "wall" ? "_0" : "");
                if (!regions.TryGetValue(name, out var region) || region.Width != 8 || region.Height != 8)
                    throw new InvalidDataException("Missing native MSX1 editor tile: " + name);
                byte[] rgba = new byte[64 * 4];
                for (int y = 0; y < 8; y++)
                    image.Pixels.AsSpan(((region.Y + y) * image.Width + region.X) * 4, 32).CopyTo(rgba.AsSpan(y * 32));
                byte[] indices = PaletteIndexing.FromRgba(rgba, Tms9918.Palette, transparentZero: false);
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i] == 0) indices[i] = 1; // Native opaque black must cover the mural.
                Pack(blocks, tile.Code, indices);
            }
            for (int cell = 0; cell < 32; cell++)
            {
                byte[] indices = new byte[64];
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int pixel = (cell / 8 * 8 + y) * 64 + cell % 8 * 8 + x;
                    byte packed = murals[(pyramid - 1) * 1024 + pixel / 2];
                    indices[y * 8 + x] = (byte)(pixel % 2 == 0 ? packed >> 4 : packed & 15);
                }
                Pack(blocks, MuralCodes[cell], indices);
            }
        }
    }

    private static void Pack(byte[] blocks, int code, byte[] pixels)
    {
        for (int i = 0; i < 32; i++) blocks[code * 32 + i] = (byte)(pixels[i * 2] << 4 | pixels[i * 2 + 1]);
    }

    public ScreenData Assemble(int pyramid, int room, TerrainKind[] map, IReadOnlyList<MechanismDefinition>? mechanisms = null, IReadOnlyList<LadderDefinition>? goldenLadders = null, byte[]? background = null)
    {
        if (pyramid is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(pyramid));
        if (map.Length != 32 * 24) throw new ArgumentException("A room requires 32 by 24 terrain cells.", nameof(map));
        var terrain = (TerrainKind[])map.Clone();
        foreach (var mechanism in mechanisms ?? Array.Empty<MechanismDefinition>())
            if (mechanism.Position.Room == room)
                for (int y = 0; y < mechanism.HeightCells; y++) for (int x = 0; x < mechanism.WidthCells; x++)
                {
                    int col = (mechanism.Position.X >> 3) + x, row = (mechanism.Position.Y >> 3) + y;
                    if (col is >= 0 and < 32 && row is >= 0 and < 24) terrain[row * 32 + col] = TerrainKind.Empty;
                }
        // Native MSX1 editor mode (E200=11, E254=1) omits stage decorations
        // and separate temporary-ladder records; editable ladder cells use tiles 3/4.
        if(goldenLadders?.Any(l=>l.Room==room)==true) EnsureGolden(pyramid);
        byte[] pixels = RoomAssembler.Assemble(pyramid, 1, room, _blocks[pyramid - 1], MuralCodes,
            Array.Empty<DecorationEntry>(), terrain, Array.Empty<LadderDefinition>(), goldenLadders ?? Array.Empty<LadderDefinition>(), background);
        return new ScreenData(256, 192, false, Palette, pixels);
    }

    private readonly Dictionary<string, byte[]> _nativeTables = new();

    public byte[] Background(int pyramid, int stage, int room, Msx1PatternAnimator? animator)
    {
        string root = Path.Combine(_graphicsDirectory, "Rooms", "Backgrounds");
        byte[] Read(string path)
        {
            if (!_nativeTables.TryGetValue(path, out var data)) _nativeTables[path] = data = File.ReadAllBytes(path);
            return data;
        }
        byte[] snapshot = Read(Path.Combine(root, $"p{pyramid}.vdp"));
        byte[] regs = snapshot.AsSpan(0, 8).ToArray(), vram = snapshot.AsSpan(8).ToArray();
        string names = Path.Combine(root, $"p{pyramid}", $"s{stage:00}_r{room:00}.names");
        if (!File.Exists(names)) names = Path.Combine(root, $"p{pyramid}", stage % 2 == 0 ? "even.names" : "odd.names");
        Read(names).CopyTo(vram, (regs[2] & 15) << 10);
        animator?.ApplyToVram(pyramid, vram, regs);
        return Tms9918.DecodeScreen2(vram, regs);
    }

    private void EnsureGolden(int pyramid)
    {
        if(_goldenReady[pyramid-1])return;
        string levels=Path.Combine(_graphicsDirectory,"Levels");
        foreach(string path in Directory.EnumerateFiles(levels,"stage*.json").OrderBy(p=>p,StringComparer.Ordinal))
        {
            var stage=StageLoader.LoadFile(path);if(stage.Pyramid!=pyramid)continue;
            foreach(var ladder in stage.GoldenLadders)
            {
                string dump=Path.Combine(_graphicsDirectory,"Rooms","dumps",$"s{stage.Number:00}_r{ladder.Room:00}.bin");
                if(!File.Exists(dump))continue;
                var screen=ScreenAssembler.DecodeFile(dump);
                for(int side=0;side<2;side++)
                {
                    byte[] pixels=new byte[64];
                    for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                    {
                        byte index=screen.Indices[(ladder.Row*8+y)*256+(ladder.Column+side)*8+x];
                        pixels[y*8+x]=index==0?(byte)1:index;
                    }
                    Pack(_blocks[pyramid-1],side+1,pixels);
                }
                _goldenReady[pyramid-1]=true;return;
            }
        }
        throw new InvalidDataException("The extracted MSX1 generation has no native temporary-ladder tile source for this pyramid.");
    }
}
