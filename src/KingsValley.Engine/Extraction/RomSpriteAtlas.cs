#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using KingsValley.Engine.Assets;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Builds the <b>generated MSX1 sprite atlas</b> (<c>Art/kv2.png</c> + <c>Art/kv2.atlas</c>) used by the free
/// build when the player only has the MSX1 cartridge.
/// </summary>
/// <remarks>
/// <para>
/// The 14 real player poses are captured from the cartridge's sprite layer by
/// <see cref="RomScreens.CapturePlayerPoses"/>. Every other name the renderer asks for is filled with a simple,
/// recognisable pixel shape in the fixed TMS9918 palette: this is deliberately a <b>fallback</b>, not a claim of
/// ROM fidelity. It exists so no actor, item, door, tile or glyph is invisible when the MSX2 art is absent.
/// </para>
/// <para>
/// The atlas uses the project's simplified Nez format (three lines per sprite: name, <c>x,y,w,h</c>,
/// <c>0,0</c>). It is only written when there is no real MSX2-derived atlas on disk, so a development build
/// never gets its original art shadowed by these fallback shapes.
/// </para>
/// </remarks>
public static class RomSpriteAtlas
{
    private const int SheetWidth = 512;
    private const int Padding = 1;

    private const int PoseBox = 32;
    private const int PoseWidth = 16;
    private const int PoseHeight = 32;
    private const int PoseCropX = 8;
    private const int PoseCropY = 7;

    /// <summary>Fixed TMS9918 colours (index 0 is transparent in a sprite atlas).</summary>
    private static readonly (byte R, byte G, byte B)[] Tms =
    {
        (0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
        (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
        (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
        (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255),
    };

    /// <summary>5x7 glyphs, in the order A..Z, 0..9, '!'.</summary>
    private static readonly string[] Glyphs =
    {
        "01110/10001/10001/11111/10001/10001/10001", // A
        "11110/10001/10001/11110/10001/10001/11110", // B
        "01111/10000/10000/10000/10000/10000/01111", // C
        "11110/10001/10001/10001/10001/10001/11110", // D
        "11111/10000/10000/11110/10000/10000/11111", // E
        "11111/10000/10000/11110/10000/10000/10000", // F
        "01111/10000/10000/10111/10001/10001/01111", // G
        "10001/10001/10001/11111/10001/10001/10001", // H
        "11111/00100/00100/00100/00100/00100/11111", // I
        "00111/00010/00010/00010/00010/10010/01100", // J
        "10001/10010/10100/11000/10100/10010/10001", // K
        "10000/10000/10000/10000/10000/10000/11111", // L
        "10001/11011/10101/10101/10001/10001/10001", // M
        "10001/11001/10101/10011/10001/10001/10001", // N
        "01110/10001/10001/10001/10001/10001/01110", // O
        "11110/10001/10001/11110/10000/10000/10000", // P
        "01110/10001/10001/10001/10101/10010/01101", // Q
        "11110/10001/10001/11110/10100/10010/10001", // R
        "01111/10000/10000/01110/00001/00001/11110", // S
        "11111/00100/00100/00100/00100/00100/00100", // T
        "10001/10001/10001/10001/10001/10001/01110", // U
        "10001/10001/10001/10001/10001/01010/00100", // V
        "10001/10001/10001/10101/10101/10101/01010", // W
        "10001/10001/01010/00100/01010/10001/10001", // X
        "10001/10001/01010/00100/00100/00100/00100", // Y
        "11111/00001/00010/00100/01000/10000/11111", // Z
        "01110/10001/10011/10101/11001/10001/01110", // 0
        "00100/01100/00100/00100/00100/00100/01110", // 1
        "01110/10001/00001/00010/00100/01000/11111", // 2
        "11110/00001/00001/01110/00001/00001/11110", // 3
        "00010/00110/01010/10010/11111/00010/00010", // 4
        "11111/10000/10000/11110/00001/00001/11110", // 5
        "01110/10000/10000/11110/10001/10001/01110", // 6
        "11111/00001/00010/00100/01000/01000/01000", // 7
        "01110/10001/10001/01110/10001/10001/01110", // 8
        "01110/10001/10001/01111/00001/00001/01110", // 9
        "00100/00100/00100/00100/00100/00000/00100", // !
    };

    private sealed class Sprite
    {
        public Sprite(string name, int width, int height, byte[] pixels)
        {
            Name = name;
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    /// Writes the atlas into <paramref name="extractedDirectory"/>/Art. A real MSX2 atlas on disk wins: the
    /// generated fallback must never replace art extracted from the player's MSX2 cartridge.
    /// </summary>
    /// <param name="rom">MSX1 (RC-760) cartridge image.</param>
    /// <param name="extractedDirectory">Root of the extracted content.</param>
    /// <param name="log">Receives progress lines.</param>
    /// <param name="force">
    /// Ignore the "real MSX2 atlas present" guard. Only tests use this, with a temporary content directory.
    /// </param>
    /// <param name="poseFrames">Emulation frames spent looking for real player poses (tests use a shorter run).</param>
    /// <param name="token">Checked before and after the capture.</param>
    public static void Build(byte[] rom, string extractedDirectory, Action<string>? log = null,
        bool force = false, int poseFrames = 120 * 60, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedDirectory);
        token.ThrowIfCancellationRequested();

        if (!force && HasRealMsx2Atlas())
        {
            log?.Invoke("atlas: skipped (real MSX2 atlas present)");
            return;
        }

        var sprites = new List<Sprite>();
        var poses = new Dictionary<int, byte[]>();
        byte[]? vram = null;
        byte[]? registers = null;

        try
        {
            var (capturedPoses, capturedVram, capturedRegs) = RomScreens.CaptureGameplayAssets(rom, poseFrames);
            foreach ((int pose, byte[] pixels) in capturedPoses)
                poses[pose] = pixels;

            vram = capturedVram;
            registers = capturedRegs;
            log?.Invoke($"atlas: {poses.Count} real player poses and VRAM tables captured");
        }
        catch (Exception ex)
        {
            // A ROM that refuses the gameplay capture still gets a complete fallback atlas.
            Console.WriteLine($"[RomSpriteAtlas] gameplay capture: {ex.Message}");
            log?.Invoke("atlas: gameplay capture unavailable, using fallback shapes");
        }

        token.ThrowIfCancellationRequested();

        AddPlayerSprites(sprites, poses, vram, registers);
        if (vram != null && registers != null)
            AddRomSprites(sprites, vram, registers);
        else
            AddFallbackSprites(sprites);

        Pack(sprites, out int width, out int height, out byte[] rgba, out string atlas);

        string art = Path.Combine(extractedDirectory, "Art");
        Directory.CreateDirectory(art);
        WritePngRgba(Path.Combine(art, "kv2.png"), width, height, rgba);
        File.WriteAllText(Path.Combine(art, "kv2.atlas"), atlas,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        // Marks this atlas as a generated approximation. RomContent uses it to prefer the real
        // MSX2-derived art bundled with the application whenever that art is available.
        File.WriteAllText(Path.Combine(art, "kv2.generated"), "generated-msx1-fallback\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        log?.Invoke($"atlas: {sprites.Count} sprites ({width}x{height})");
    }

    /// <summary>Is there an original (or custom) MSX2 atlas that must not be shadowed?</summary>
    private static bool HasRealMsx2Atlas()
    {
        if (File.Exists(Path.Combine(RomContent.ContentDirectory, "Rom", "Art", "kv2.atlas")))
            return true;

        string sets = Path.Combine(RomContent.ContentDirectory, "ArtSets");
        if (!Directory.Exists(sets))
            return false;

        foreach (string directory in Directory.GetDirectories(sets))
        {
            // MSX1 packs may legitimately be generated; only an MSX2-derived pack is "real" art here.
            if (Path.GetFileName(directory).Equals("msx1", StringComparison.OrdinalIgnoreCase))
                continue;

            if (File.Exists(Path.Combine(directory, "Art", "kv2.atlas")))
                return true;
        }

        return false;
    }

    // ── sprite list ───────────────────────────────────────────────────────────────────────

    private static void AddPlayerSprites(List<Sprite> sprites, IReadOnlyDictionary<int, byte[]> poses,
        byte[]? vram, byte[]? registers)
    {
        int spriteBase = registers != null ? (registers[6] & 0x07) << 11 : 0x1800;

        byte[]? Pose(int index)
        {
            if (poses.Count == 0)
                return null;
            if (poses.TryGetValue(index, out byte[]? pixels))
                return pixels;
            return poses.Values.ElementAt(index % poses.Count);
        }

        byte[] Crop(byte[]? pixels, int variant, int colorSet)
        {
            if (pixels is null)
                return DrawVick(variant, colorSet);

            var canvas = new Canvas(PoseWidth, PoseHeight);
            for (int y = 0; y < PoseHeight; y++)
            {
                int srcY = y + PoseCropY;
                if (srcY < 0 || srcY >= PoseBox)
                    continue;

                for (int x = 0; x < PoseWidth; x++)
                {
                    byte value = pixels[srcY * PoseBox + PoseCropX + x];
                    canvas.Put(x, y, value == 0 ? 0 : (value - 1) & 0x0F);
                }
            }
            return canvas.Data;
        }

        for (int set = 0; set < 6; set++)
        {
            for (int pose = 0; pose < 16; pose++)
            {
                byte[]? source = Pose(pose);
                sprites.Add(new Sprite($"vick_s{set}_{Num(pose)}", PoseWidth, PoseHeight,
                    Crop(source, pose, set)));
            }
        }

        for (int pose = 0; pose < 16; pose++)
        {
            byte[] xPixels;
            if (vram != null)
            {
                int basePattern = 152 + (pose % 4) * 4;
                var c = new Canvas(PoseWidth, PoseHeight);
                byte[] exp = ExtractSprite16(vram, spriteBase, basePattern, 15);
                for (int r = 0; r < 16; r++)
                    for (int col = 0; col < 16; col++)
                        c.Put(col, r + 8, exp[r * 16 + col]);
                xPixels = c.Data;
            }
            else
            {
                xPixels = Crop(Pose(pose), pose, 1);
            }
            sprites.Add(new Sprite($"vick_x_{Num(pose)}", PoseWidth, PoseHeight, xPixels));

            byte[]? stand = Pose(0);
            byte[] dPixels;
            if (stand != null)
            {
                var c = new Canvas(PoseWidth, PoseHeight);
                byte[] cropped = Crop(stand, 0, 0);
                int clipRows = Math.Min(24, 8 + pose * 2);
                for (int y = 0; y < PoseHeight - clipRows; y++)
                    for (int x = 0; x < PoseWidth; x++)
                        c.Put(x, y, cropped[y * PoseWidth + x]);
                dPixels = c.Data;
            }
            else
            {
                dPixels = Crop(Pose(pose), pose, 2);
            }
            sprites.Add(new Sprite($"vick_d_{Num(pose)}", PoseWidth, PoseHeight, dPixels));
        }
    }

    private static void AddRomSprites(List<Sprite> sprites, byte[] vram, byte[] registers)
    {
        int spriteBase = (registers[6] & 0x07) << 11;
        int patternBase = (registers[4] & 0x3C) << 11;
        int middleBank = patternBase + 0x800;

        // Enemies (16x16)
        for (int type = 0; type <= 6; type++)
        {
            int color = type switch
            {
                1 => 11, // Mummy: light yellow
                2 => 10, // Gold Mummy: dark yellow
                3 => 8,  // Red Mummy: medium red
                4 => 7,  // Blue Mummy: cyan
                5 => 15, // Vanish: white
                6 => 14, // Rock/Statue: gray
                _ => 11,
            };

            for (int frame = 0; frame < 16; frame++)
            {
                int basePattern = type == 5
                    ? 152 + (frame % 4) * 4
                    : (frame % 4) switch { 0 => 88, 1 => 72, 2 => 76, _ => 80 };

                byte[] enemy = ExtractSprite16(vram, spriteBase, basePattern, color);
                sprites.Add(new Sprite($"enemy_t{type}_{Num(frame)}", 16, 16, enemy));
            }
        }

        // Items (16x16)
        string[] items = { "knife", "boomerang", "shovel", "pickaxe", "hammer", "drill" };
        int[] itemBases = { 44, 16, 48, 56, 60, 64 };
        int[] itemColors = { 15, 10, 7, 14, 14, 7 };

        for (int i = 0; i < items.Length; i++)
        {
            byte[] item = ExtractSprite16(vram, spriteBase, itemBases[i], itemColors[i]);
            sprites.Add(new Sprite(items[i], 16, 16, item));
            sprites.Add(new Sprite("item_" + items[i], 16, 16, item));
        }

        // Projectiles (16x16)
        int[] projFrames = { 4, 6, 8, 10, 12, 14 };
        int[] projBases = { 28, 32, 36, 16, 20, 24 };
        int[] projColors = { 15, 15, 15, 10, 10, 10 };

        for (int i = 0; i < projFrames.Length; i++)
        {
            byte[] proj = ExtractSprite16(vram, spriteBase, projBases[i], projColors[i]);
            sprites.Add(new Sprite($"proj_{Num(projFrames[i])}", 16, 16, proj));
        }

        // Coffins (16x24), Doors (32x32), Studs (8x8), Soulstones (16x16), Terrain (8x8)
        int[] woodColors = { 10, 12, 6, 8, 13, 11 };
        int[] soulstoneColors = { 15, 10, 3, 9, 13, 14 };

        int[] coffinClosed = { 155, 156, 157, 158, 159, 160 };
        int[] coffinAjar = { 161, 162, 163, 164, 165, 166 };
        int[] coffinOpen = { 167, 168, 169, 170, 171, 172 };

        int[] doorF0 = { 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114 };
        int[] doorF1 = { 100, 88, 89, 101, 104, 90, 91, 105, 108, 92, 93, 109, 112, 120, 120, 113 };
        int[] doorF2 = { 115, 88, 89, 116, 117, 90, 91, 118, 117, 92, 93, 118, 119, 120, 120, 121 };

        for (int p = 1; p <= 6; p++)
        {
            int wood = woodColors[(p - 1) % 6];
            int stone = soulstoneColors[(p - 1) % 6];

            sprites.Add(new Sprite($"soulstone_p{p}", 16, 16,
                ExtractSprite16(vram, spriteBase, 40, stone)));

            sprites.Add(new Sprite($"coffin_p{p}", 16, 24,
                ExtractTileBlock(vram, middleBank, coffinClosed, 2, 3, wood)));
            sprites.Add(new Sprite($"coffin_ajar_p{p}", 16, 24,
                ExtractTileBlock(vram, middleBank, coffinAjar, 2, 3, wood)));
            sprites.Add(new Sprite($"coffin_open_p{p}", 16, 24,
                ExtractTileBlock(vram, middleBank, coffinOpen, 2, 3, wood)));

            sprites.Add(new Sprite($"door_f0_p{p}", 32, 32,
                ExtractTileBlock(vram, middleBank, doorF0, 4, 4, wood)));
            sprites.Add(new Sprite($"door_f1_p{p}", 32, 32,
                ExtractTileBlock(vram, middleBank, doorF1, 4, 4, wood)));
            sprites.Add(new Sprite($"door_f2_p{p}", 32, 32,
                ExtractTileBlock(vram, middleBank, doorF2, 4, 4, wood)));
            sprites.Add(new Sprite($"door_stud_p{p}", 8, 8,
                ExtractTile8(vram, middleBank, 0xAE, wood)));

            sprites.Add(new Sprite($"ladder_p{p}", 8, 8, DrawLadder(p)));
            for (int v = 0; v < 4; v++)
            {
                sprites.Add(new Sprite($"wall_p{p}_{v}", 8, 8, DrawWall(p, v)));
                sprites.Add(new Sprite($"soft_p{p}_{v}", 8, 8, DrawSoft(p, v)));
            }
            for (int code = 0; code < 24; code++)
            {
                sprites.Add(new Sprite($"crumble_{code}_p{p}", 8, 8, DrawCrumble(code, p)));
            }
        }

        // Font (A..Z)
        for (int i = 0; i < 26; i++)
        {
            byte[] glyph = ExtractTile8(vram, middleBank, 0xE1 + i, 15);
            if (glyph.All(b => b == 0))
                glyph = DrawGlyph(Glyphs[i], 15);
            sprites.Add(new Sprite("font_" + (char)('A' + i), 8, 8, glyph));
        }

        // Digits (0..9)
        for (int i = 0; i < 10; i++)
        {
            byte[] digit = ExtractTile8(vram, middleBank, 0xD0 + i, 15);
            if (digit.All(b => b == 0))
                digit = DrawGlyph(Glyphs[26 + i], 10);
            sprites.Add(new Sprite("digit_" + i, 8, 8, digit));
        }

        // Exclamation, arrow, cursor, and authentic HUD strip
        byte[] excl = ExtractTile8(vram, middleBank, 0xDB, 15);
        if (excl.All(b => b == 0))
            excl = DrawGlyph(Glyphs[36], 8);
        sprites.Add(new Sprite("font_excl", 8, 8, excl));
        sprites.Add(new Sprite("arrow", 8, 8, DrawArrow()));
        sprites.Add(new Sprite("title_cursor", 16, 16, DrawTitleCursor()));
        sprites.Add(new Sprite("hud_strip", 256, 20, DrawRealHudStrip()));
    }

    private static byte[] ExtractTile8(byte[] vram, int tableBase, int code, int color)
    {
        var canvas = new Canvas(8, 8);
        for (int y = 0; y < 8; y++)
        {
            byte bits = vram[(tableBase + code * 8 + y) & 0x3FFF];
            for (int x = 0; x < 8; x++)
            {
                if (((bits >> (7 - x)) & 1) != 0)
                    canvas.Put(x, y, color);
            }
        }
        return canvas.Data;
    }

    private static byte[] ExtractTileBlock(byte[] vram, int tableBase, int[] tileCodes, int cols, int rows, int color)
    {
        int width = cols * 8;
        int height = rows * 8;
        var canvas = new Canvas(width, height);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int code = tileCodes[r * cols + c];
                for (int y = 0; y < 8; y++)
                {
                    byte bits = vram[(tableBase + code * 8 + y) & 0x3FFF];
                    for (int x = 0; x < 8; x++)
                    {
                        if (((bits >> (7 - x)) & 1) != 0)
                            canvas.Put(c * 8 + x, r * 8 + y, color);
                    }
                }
            }
        }
        return canvas.Data;
    }

    private static byte[] ExtractSprite16(byte[] vram, int spriteBase, int basePattern, int color)
    {
        var canvas = new Canvas(16, 16);
        for (int r = 0; r < 16; r++)
        {
            for (int c = 0; c < 16; c++)
            {
                int quad = (c / 8) * 2 + (r / 8);
                int tile = basePattern + quad;
                int tileRow = r % 8;
                int tileCol = c % 8;
                byte bits = vram[(spriteBase + tile * 8 + tileRow) & 0x3FFF];
                if (((bits >> (7 - tileCol)) & 1) != 0)
                    canvas.Put(c, r, color);
            }
        }
        return canvas.Data;
    }

    private static byte[] DrawRealHudStrip()
    {
        const string b64 = "eNrtVEEKxCAMDFvoaSEfCuRrfcK+w1fsk/oAwZqkYuI1p0L14owZM6NgKbnxg9TYj5z+I3pCPLHK5MrNNiKW1dZrDAkrzNRvf8J73gdHLBVIE5EyQQ8EQR8wgXYAp4ep59ZPr1r/VVfq2GOsqmdDwgrj+4N64nP0X3Cv4DbcKLv6t7S05KeZ36WhNf+4/94x3P/A5h+bIWG9/+z7v/rn6rP/R/L7Kheqb7as";
        byte[] compressed = Convert.FromBase64String(b64);
        using var ms = new MemoryStream(compressed);
        using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        zlib.CopyTo(outMs);
        byte[] decomp = outMs.ToArray();

        var c = new Canvas(256, 20);
        int[] paletteMap = { 0, 4, 5, 15 };

        for (int i = 0; i < decomp.Length; i++)
        {
            byte b = decomp[i];
            int p0 = paletteMap[(b >> 6) & 3];
            int p1 = paletteMap[(b >> 4) & 3];
            int p2 = paletteMap[(b >> 2) & 3];
            int p3 = paletteMap[b & 3];

            int baseIdx = i * 4;
            int x0 = baseIdx % 256;
            int y0 = baseIdx / 256;
            if (y0 < 20)
            {
                c.Put(x0, y0, p0);
                if (x0 + 1 < 256) c.Put(x0 + 1, y0, p1);
                if (x0 + 2 < 256) c.Put(x0 + 2, y0, p2);
                if (x0 + 3 < 256) c.Put(x0 + 3, y0, p3);
            }
        }
        return c.Data;
    }

    private static void AddFallbackSprites(List<Sprite> sprites)
    {
        // Enemies. Type 5 is the death effect, type 6 the stone after appearing (see EnemyFrames).
        for (int type = 0; type <= 6; type++)
            for (int frame = 0; frame < 16; frame++)
                sprites.Add(new Sprite($"enemy_t{type}_{Num(frame)}", 16, 24, DrawEnemy(type, frame)));

        for (int pyramid = 1; pyramid <= 6; pyramid++)
        {
            sprites.Add(new Sprite($"coffin_p{pyramid}", 16, 24, DrawCoffin(0, pyramid)));
            sprites.Add(new Sprite($"coffin_ajar_p{pyramid}", 16, 24, DrawCoffin(1, pyramid)));
            sprites.Add(new Sprite($"coffin_open_p{pyramid}", 16, 24, DrawCoffin(2, pyramid)));
            sprites.Add(new Sprite($"soulstone_p{pyramid}", 16, 16, DrawSoulstone(pyramid)));
            sprites.Add(new Sprite($"door_stud_p{pyramid}", 8, 8, DrawStud(pyramid)));
            sprites.Add(new Sprite($"ladder_p{pyramid}", 8, 8, DrawLadder(pyramid)));

            for (int frame = 0; frame < 3; frame++)
                sprites.Add(new Sprite($"door_f{frame}_p{pyramid}", 32, 32, DrawDoor(frame, pyramid)));

            for (int variant = 0; variant < 4; variant++)
            {
                sprites.Add(new Sprite($"wall_p{pyramid}_{variant}", 8, 8, DrawWall(pyramid, variant)));
                sprites.Add(new Sprite($"soft_p{pyramid}_{variant}", 8, 8, DrawSoft(pyramid, variant)));
            }

            for (int code = 0; code < 24; code++)
                sprites.Add(new Sprite($"crumble_{code}_p{pyramid}", 8, 8, DrawCrumble(code, pyramid)));
        }

        foreach (int frame in new[] { 4, 6, 8, 10, 12, 14 })
            sprites.Add(new Sprite($"proj_{Num(frame)}", 16, 16, DrawProjectile(frame)));

        string[] items = { "knife", "boomerang", "shovel", "pickaxe", "hammer", "drill" };
        foreach (string item in items)
        {
            sprites.Add(new Sprite(item, 16, 16, DrawItem(item)));
            sprites.Add(new Sprite("item_" + item, 16, 16, DrawItem(item)));
        }

        for (int i = 0; i < 26; i++)
            sprites.Add(new Sprite("font_" + (char)('A' + i), 8, 8, DrawGlyph(Glyphs[i], 15)));
        for (int i = 0; i < 10; i++)
            sprites.Add(new Sprite("digit_" + i, 8, 8, DrawGlyph(Glyphs[26 + i], 10)));
        sprites.Add(new Sprite("font_excl", 8, 8, DrawGlyph(Glyphs[36], 8)));

        sprites.Add(new Sprite("arrow", 8, 8, DrawArrow()));
        sprites.Add(new Sprite("title_cursor", 16, 16, DrawTitleCursor()));
        sprites.Add(new Sprite("hud_strip", 256, 20, DrawHudStrip()));
    }

    // ── simple fallback shapes ────────────────────────────────────────────────────────────

    private static byte[] DrawVick(int variant, int colorSet)
    {
        var c = new Canvas(PoseWidth, PoseHeight);
        int hat = colorSet % 2 == 0 ? 8 : 6;
        int suit = colorSet % 3 switch { 0 => 4, 1 => 12, _ => 5 };
        int swing = (variant % 4 - 1) * 2;

        c.Rect(3, 2, 10, 3, hat);
        c.Rect(1 + swing, 4, 4, 2, hat);
        c.Rect(4, 5, 8, 6, 9);
        c.Put(6, 7, 1);
        c.Put(10, 7, 1);
        c.Rect(3, 11, 10, 10, suit);
        c.Rect(1 + swing, 12, 2, 7, 9);
        c.Rect(13 - swing, 12, 2, 7, 9);
        c.Rect(4, 21, 3, 8, 4);
        c.Rect(9, 21, 3, 8, 4);
        c.Rect(3, 29, 5, 2, 1);
        c.Rect(9, 29, 5, 2, 1);
        if (variant % 3 == 0)
            c.Rect(7, 15, 2, 3, 10); // belt
        return c.Data;
    }

    private static byte[] DrawEnemy(int type, int frame)
    {
        var c = new Canvas(16, 24);
        int body = type switch { 0 => 14, 1 => 10, 2 => 3, 3 => 6, 4 => 13, 5 => 15, _ => 11 };
        int swing = frame % 4;

        if (type == 5)
        {
            c.Line(2, 2, 14, 22, 13);
            c.Line(14, 2, 2, 22, 13);
            c.Disc(8, 12, Math.Max(2, 5 - swing), 15);
            return c.Data;
        }

        c.Rect(3, 1, 10, 6, body);
        c.Rect(2, 7, 12, 12, body);
        c.Rect(0, 9 + swing / 2, 2, 7, body);
        c.Rect(14, 9 - swing / 2, 2, 7, body);
        c.Rect(3, 19, 4, 5 - swing / 2, body);
        c.Rect(9, 19, 4, 5 - swing / 2, body);
        c.Put(6, 3, 1);
        c.Put(10, 3, 1);
        for (int row = 4; row < 19; row += 4)
            c.Rect(2, row, 12, 1, 15);
        return c.Data;
    }

    private static byte[] DrawCoffin(int look, int pyramid)
    {
        var c = new Canvas(16, 24);
        int wood = new[] { 10, 12, 6, 8, 13, 11 }[(pyramid - 1) % 6];

        c.Rect(1, 3, 14, 20, wood);
        c.Rect(3, 1, 10, 3, wood);
        c.Frame(1, 3, 14, 20, 1);
        c.Rect(2, 10, 12, 2, 10);
        c.Rect(7, 5, 2, 5, 15);

        if (look == 1)
        {
            c.Rect(4, 5, 8, 6, 0);
            c.Line(4, 11, 12, 5, 15);
        }
        else if (look == 2)
        {
            c.Rect(3, 4, 10, 15, 0);
            c.Rect(2, 1, 12, 3, wood);
            c.Rect(3, 19, 10, 3, 1);
        }

        return c.Data;
    }

    private static byte[] DrawSoulstone(int pyramid)
    {
        var c = new Canvas(16, 16);
        int gem = new[] { 4, 12, 3, 6, 13, 7 }[(pyramid - 1) % 6];
        c.Disc(8, 8, 6, gem);
        c.Line(8, 2, 8, 14, 15);
        c.Line(2, 8, 14, 8, 15);
        c.Put(6, 5, 15);
        c.Put(5, 6, 15);
        return c.Data;
    }

    private static byte[] DrawDoor(int frame, int pyramid)
    {
        var c = new Canvas(32, 32);
        int stone = new[] { 14, 10, 11, 12, 6, 13 }[(pyramid - 1) % 6];

        c.Rect(0, 0, 32, 32, stone);
        c.Frame(0, 0, 32, 32, 1);
        c.Rect(4, 6, 24, 26, 1);
        c.Rect(8, 2, 16, 4, stone);
        for (int x = 4; x < 32; x += 8)
            c.Rect(x, 0, 1, 6, 1);

        // frame 0 = closed, 1 = half open, 2 = open.
        if (frame >= 1)
            c.Rect(8, 8, 16, 24 - frame * 6, 1);
        if (frame >= 2)
            c.Rect(11, 8, 10, 20, 0);

        return c.Data;
    }

    private static byte[] DrawStud(int pyramid)
    {
        var c = new Canvas(8, 8);
        int color = new[] { 10, 15, 11, 10, 15, 11 }[(pyramid - 1) % 6];
        c.Put(3, 1, color);
        c.Rect(2, 2, 4, 2, color);
        c.Rect(1, 4, 6, 2, color);
        c.Put(3, 6, color);
        c.Put(4, 6, color);
        return c.Data;
    }

    private static byte[] DrawProjectile(int frame)
    {
        var c = new Canvas(16, 16);
        bool boomerang = frame >= 10;
        int color = boomerang ? 10 : 14;

        if (boomerang)
        {
            c.Line(2, 3, 8, 13, color);
            c.Line(14, 3, 8, 13, color);
            c.Rect(7, 12, 2, 2, 15);
        }
        else
        {
            c.Line(3, 13, 13, 3, color);
            c.Line(2, 12, 12, 2, 15);
            c.Rect(2, 12, 3, 3, 10);
        }

        if (frame % 4 == 0)
            c.Disc(8, 8, 6, 7);
        return c.Data;
    }

    private static byte[] DrawItem(string name)
    {
        var c = new Canvas(16, 16);
        switch (name)
        {
            case "knife":
                c.Line(3, 13, 12, 3, 14);
                c.Line(4, 12, 11, 4, 15);
                c.Rect(2, 12, 3, 3, 10);
                break;
            case "boomerang":
                c.Line(2, 4, 8, 13, 10);
                c.Line(14, 4, 8, 13, 10);
                c.Rect(7, 12, 2, 2, 15);
                break;
            case "shovel":
                c.Line(8, 1, 8, 11, 11);
                c.Rect(5, 10, 6, 4, 14);
                c.Rect(7, 14, 2, 2, 14);
                break;
            case "pickaxe":
                c.Line(8, 2, 8, 14, 11);
                c.Line(2, 5, 14, 4, 14);
                c.Line(4, 3, 12, 6, 15);
                break;
            case "hammer":
                c.Line(8, 6, 8, 14, 11);
                c.Rect(3, 2, 10, 5, 14);
                c.Rect(5, 3, 6, 3, 15);
                break;
            default: // drill
                c.Line(8, 1, 3, 9, 14);
                c.Line(8, 1, 13, 9, 14);
                c.Rect(6, 9, 4, 6, 7);
                c.Rect(7, 14, 2, 2, 10);
                break;
        }
        return c.Data;
    }

    private static byte[] DrawGlyph(string bitmap, int color)
    {
        var c = new Canvas(8, 8);
        string[] rows = bitmap.Split('/');
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '1')
                    c.Put(x + 1, y + 1, color);
        return c.Data;
    }

    private static byte[] DrawArrow()
    {
        var c = new Canvas(8, 8);
        c.Rect(1, 3, 5, 2, 10);
        c.Line(5, 1, 7, 4, 10);
        c.Line(7, 4, 5, 7, 10);
        return c.Data;
    }

    private static byte[] DrawTitleCursor()
    {
        var c = new Canvas(16, 16);
        c.Rect(2, 1, 9, 11, 15);
        c.Frame(2, 1, 9, 11, 1);
        c.Rect(4, 11, 3, 4, 15);
        c.Rect(7, 10, 3, 4, 15);
        c.Rect(10, 8, 3, 4, 15);
        return c.Data;
    }

    private static byte[] DrawHudStrip() => DrawRealHudStrip();

    private static byte[] DrawWall(int pyramid, int variant)
    {
        var c = new Canvas(8, 8);
        int stone = new[] { 14, 10, 11, 12, 6, 13 }[(pyramid - 1) % 6];
        c.Rect(0, 0, 8, 8, stone);
        c.Frame(0, 0, 8, 8, 1);
        c.Rect(variant % 2 == 0 ? 1 : 4, 3, 3, 1, 1);
        c.Rect(2, variant % 2 == 0 ? 5 : 2, 3, 1, 1);
        return c.Data;
    }

    private static byte[] DrawSoft(int pyramid, int variant)
    {
        var c = new Canvas(8, 8);
        int sand = new[] { 11, 10, 12, 10, 11, 12 }[(pyramid - 1) % 6];
        c.Rect(0, 0, 8, 8, sand);
        for (int x = 0; x < 8; x++)
            c.Put(x, (x + variant * 2) % 8, 15);
        c.Rect(0, 7, 8, 1, 1);
        return c.Data;
    }

    private static byte[] DrawLadder(int pyramid)
    {
        var c = new Canvas(8, 8);
        int wood = pyramid % 2 == 0 ? 10 : 11;
        c.Rect(1, 0, 2, 8, wood);
        c.Rect(5, 0, 2, 8, wood);
        for (int y = 1; y < 8; y += 3)
            c.Rect(1, y, 6, 1, 15);
        return c.Data;
    }

    private static byte[] DrawCrumble(int code, int pyramid)
    {
        var c = new Canvas(8, 8);
        int color = new[] { 14, 10, 11, 12, 6, 13 }[(pyramid - 1) % 6];
        int step = code % 4;
        c.Rect(0, 0, 8, 8, color);
        c.Frame(0, 0, 8, 8, 1);
        c.Line(1, 1 + step, 6, 6 - step, 1);
        c.Line(6, 1 + step, 1, 6 - step, 1);
        return c.Data;
    }

    // ── packing and files ─────────────────────────────────────────────────────────────────

    private static void Pack(List<Sprite> sprites, out int width, out int height, out byte[] rgba, out string atlas)
    {
        int x = Padding;
        int y = Padding;
        int rowHeight = 0;

        foreach (Sprite sprite in sprites)
        {
            if (x + sprite.Width + Padding > SheetWidth)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
            }

            sprite.X = x;
            sprite.Y = y;
            x += sprite.Width + Padding;
            rowHeight = Math.Max(rowHeight, sprite.Height);
        }

        width = SheetWidth;
        height = y + rowHeight + Padding;
        rgba = new byte[width * height * 4];
        var text = new StringBuilder(sprites.Count * 28);

        foreach (Sprite sprite in sprites)
        {
            for (int sy = 0; sy < sprite.Height; sy++)
            {
                for (int sx = 0; sx < sprite.Width; sx++)
                {
                    int value = sprite.Pixels[sy * sprite.Width + sx];
                    if (value == 0)
                        continue;

                    (byte R, byte G, byte B) = Tms[value & 0x0F];
                    int offset = ((sprite.Y + sy) * width + sprite.X + sx) * 4;
                    rgba[offset] = R;
                    rgba[offset + 1] = G;
                    rgba[offset + 2] = B;
                    rgba[offset + 3] = 255;
                }
            }

            text.Append(sprite.Name).Append('\n');
            text.Append(sprite.X).Append(',').Append(sprite.Y).Append(',')
                .Append(sprite.Width).Append(',').Append(sprite.Height).Append('\n');
            text.Append("0,0\n");
        }

        atlas = text.ToString();
    }

    private static string Num(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    // ── tiny canvas ───────────────────────────────────────────────────────────────────────

    private sealed class Canvas
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public Canvas(int width, int height)
        {
            _width = width;
            _height = height;
            _pixels = new byte[width * height];
        }

        public byte[] Data => _pixels;

        public void Put(int x, int y, int color)
        {
            if (color <= 0 || x < 0 || x >= _width || y < 0 || y >= _height)
                return;
            _pixels[y * _width + x] = (byte)color;
        }

        public void Rect(int x, int y, int width, int height, int color)
        {
            for (int row = y; row < y + height; row++)
                for (int col = x; col < x + width; col++)
                    Put(col, row, color);
        }

        public void Frame(int x, int y, int width, int height, int color)
        {
            Rect(x, y, width, 1, color);
            Rect(x, y + height - 1, width, 1, color);
            Rect(x, y, 1, height, color);
            Rect(x + width - 1, y, 1, height, color);
        }

        public void Line(int x0, int y0, int x1, int y1, int color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                Put(x0, y0, color);
                if (x0 == x1 && y0 == y1)
                    break;

                int twice = 2 * error;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }

        public void Disc(int cx, int cy, int radius, int color)
        {
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    if (x * x + y * y <= radius * radius)
                        Put(cx + x, cy + y, color);
        }
    }

    // ── PNG writer (8-bit RGBA, no filtering) ─────────────────────────────────────────────

    private static void WritePngRgba(string path, int width, int height, byte[] rgba)
    {
        using FileStream file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        WriteChunk(file, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgba, y * width * 4, width * 4);
            }

            raw.Position = 0;
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                raw.CopyTo(zlib);

            WriteChunk(file, "IDAT", compressed.ToArray());
        }

        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes)
            crc = CrcStep(crc, value);
        foreach (byte value in data)
            crc = CrcStep(crc, value);

        var checksum = new byte[4];
        WriteBigEndian(checksum, 0, crc ^ 0xFFFFFFFF);
        stream.Write(checksum);
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
