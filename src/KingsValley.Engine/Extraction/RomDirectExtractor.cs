#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using KingsValley.Core.Screens;
using KingsValley.Engine.Assets;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Direct ROM extractor for MSX2 (RC-761) and MSX1 (RC-760) King's Valley II cartridges.
/// Reads sprites, tiles, murals and screens directly from the ROM bytes using reverse-engineered
/// Konami RLE decompression and pattern tables — 100% deterministic, exact pixel-art, sub-second execution.
/// </summary>
public static class RomDirectExtractor
{
    private static void RequireMsx2(byte[] rom)
    {
        var profile=Wagenheimer.MsxAudio.Extraction.RomExtractionProfiles.FindForRom(rom);
        if(profile?.GameId!="kings-valley-2" || profile.Variant is not ("msx2" or CartridgeVariants.Contest)) throw new InvalidDataException("This recipe requires the verified MSX2 cartridge.");
    }

    private static readonly (int R, int G, int B)[] PlayerColors = { (255, 255, 255), (105, 55, 20) };
    private static readonly (byte R, byte G, byte B)[] Tms =
    {
        (0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
        (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
        (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
        (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255),
    };

    private static readonly Dictionary<int, (int A, int B)> EnemyColors = new()
    {
        { 1, (11, 12) }, // White / Normal mummy
        { 2, (10, 7) },  // Gold mummy
        { 3, (11, 12) }, // Red mummy
        { 4, (11, 12) }, // Blue mummy
        { 5, (10, 11) }, // Vanish effect
        { 6, (7, 9) }    // Rock / Statue
    };

    private static readonly byte[] Rev = BuildRevTable();

    private static byte[] BuildRevTable()
    {
        var t = new byte[256];
        for (int b = 0; b < 256; b++)
        {
            int r = 0;
            for (int i = 0; i < 8; i++)
                r |= ((b >> i) & 1) << (7 - i);
            t[b] = (byte)r;
        }
        return t;
    }

    public sealed class RomReader
    {
        public byte[] Data { get; }
        public RomReader(byte[] data) => Data = data ?? throw new ArgumentNullException(nameof(data));

        public int Off(int addr, int b8 = 14, int ba = 15)
        {
            if (addr >= 0x4000 && addr < 0x6000)
                return addr - 0x4000;
            if (addr >= 0x8000 && addr < 0xA000)
                return b8 * 0x2000 + addr - 0x8000;
            if (addr >= 0xA000 && addr < 0xC000)
                return ba * 0x2000 + addr - 0xA000;
            if (addr < 0x2000)
                return addr;
            return addr & 0x1FFFF;
        }

        public byte Byte(int addr, int b8 = 14, int ba = 15) => Data[Off(addr, b8, ba)];
        public ushort Word(int addr, int b8 = 14, int ba = 15)
        {
            int o = Off(addr, b8, ba);
            return (ushort)(Data[o] | (Data[o + 1] << 8));
        }
    }

    public sealed class DirectSprite
    {
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] Rgba { get; }
        public int X { get; set; }
        public int Y { get; set; }

        public DirectSprite(string name, int width, int height, byte[] rgba)
        {
            Name = name;
            Width = width;
            Height = height;
            Rgba = rgba;
        }
    }

    /// <summary>
    /// Konami RLE decompressor (routine 4E21/4E27 in bank 0).
    /// </summary>
    public static void Decompress(RomReader rom, byte[] vram, int src, int dest)
    {
        int p = rom.Off(src);
        byte[] d = rom.Data;
        while (p < d.Length)
        {
            byte n = d[p++];
            if (n == 0)
                return;

            if ((n & 0x80) != 0)
            {
                int cnt = n & 0x7F;
                if (cnt == 0)
                {
                    dest = d[p] | (d[p + 1] << 8);
                    p += 2;
                    continue;
                }
                if (dest + cnt <= vram.Length && p + cnt <= d.Length)
                {
                    Array.Copy(d, p, vram, dest, cnt);
                }
                p += cnt;
                dest += cnt;
            }
            else
            {
                if (p >= d.Length) break;
                byte val = d[p++];
                for (int i = 0; i < n && dest + i < vram.Length; i++)
                    vram[dest + i] = val;
                dest += n;
            }
        }
    }

    /// <summary>
    /// 4E60: Copies 16x16 patterns VRAM->VRAM with horizontal bit reversal and column swap.
    /// </summary>
    public static void MirrorCopy(byte[] vram, int dest, int src, int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int half = 0; half < 2; half++)
            {
                for (int k = 0; k < 16; k++)
                {
                    int dIdx = dest - 16 * half + k;
                    int sIdx = src + half * 16 + k;
                    if (dIdx >= 0 && dIdx < vram.Length && sIdx >= 0 && sIdx < vram.Length)
                        vram[dIdx] = Rev[vram[sIdx]];
                }
            }
            src += 32;
            dest += 32;
        }
    }

    public static void PlayerSet(RomReader rom, byte[] vram, int s)
    {
        int p = rom.Off(rom.Word(0x858B + 2 * s));
        byte[] d = rom.Data;
        while (p + 3 < d.Length && d[p] != 0xFF)
        {
            byte b0 = d[p];
            byte b1 = d[p + 1];
            byte b2 = d[p + 2];
            byte b3 = d[p + 3];
            p += 4;
            if ((b0 & 0x0F) == 0)
            {
                Decompress(rom, vram, b2 | (b3 << 8), b0 | (b1 << 8));
            }
            else
            {
                MirrorCopy(vram, (b0 & 0xF0) | (b1 << 8), b2 | (b3 << 8), b0 & 0x0F);
            }
        }
    }

    public static void StaticPatterns(RomReader rom, byte[] vram)
    {
        Decompress(rom, vram, 0x97A1, 0xF880);
        byte[] d = rom.Data;
        int p = rom.Off(0x98C9);
        while (p + 3 < d.Length && d[p] != 0)
        {
            int n = d[p];
            int dst = d[p + 1];
            int lo = d[p + 2];
            int hi = d[p + 3];
            p += 4;
            int s = rom.Off(lo | (hi << 8));
            int destOff = 0xF800 + dst * 8;
            int len = n * 32;
            if (destOff + len <= vram.Length && s + len <= d.Length)
                Array.Copy(d, s, vram, destOff, len);
        }

        p = rom.Off(0x98EA);
        while (p + 3 < d.Length && d[p] != 0)
        {
            int op = d[p];
            int n = d[p + 1];
            int a = d[p + 2];
            int b = d[p + 3];
            p += 4;
            int src = 0xF800 + a * 8;
            int dst = 0xF800 + b * 8;
            if (op == 2)
            {
                MirrorCopy(vram, dst + 16, src, n);
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    for (int k = 0; k < 16; k++)
                    {
                        if (dst + 32 * i + 16 + k < vram.Length && src + 32 * i + 31 - k < vram.Length)
                        {
                            vram[dst + 32 * i + k] = vram[src + 32 * i + 15 - k];
                            vram[dst + 32 * i + 16 + k] = vram[src + 32 * i + 31 - k];
                        }
                    }
                }
            }
        }
    }

    public static (byte R, byte G, byte B)[] ExtractPalette(RomReader rom)
    {
        var pal = new (byte R, byte G, byte B)[16]
        {
            (0, 0, 0),       // 0: Transparent
            (0, 0, 0),       // 1: Black
            (36, 218, 36),   // 2: Medium Green
            (109, 255, 109), // 3: Light Green
            (36, 36, 255),   // 4: Dark Blue
            (72, 109, 255),  // 5: Light Blue
            (182, 36, 36),   // 6: Dark Red
            (72, 218, 255),  // 7: Cyan
            (255, 36, 36),   // 8: Medium Red
            (255, 109, 109), // 9: Light Red
            (218, 218, 36),  // 10: Dark Yellow
            (255, 255, 109), // 11: Light Yellow
            (36, 145, 36),   // 12: Dark Green
            (218, 72, 182),  // 13: Magenta
            (182, 182, 182), // 14: Gray
            (255, 255, 255), // 15: White
        };
        int p = rom.Off(ContestCartridge.Is(rom.Data)?0xB460:0xB95D, b8: 14, ba: 15);
        byte[] d = rom.Data;
        while (p + 2 < d.Length && d[p] != 0xFF)
        {
            byte i = d[p];
            byte rb = d[p + 1];
            byte g = d[p + 2];
            int r = (rb >> 4) & 7;
            int b = rb & 7;
            int gg = g & 7;
            if (i < 16)
                pal[i] = ((byte)(r * 255 / 7), (byte)(gg * 255 / 7), (byte)(b * 255 / 7));
            p += 3;
        }
        return pal;
    }

    private static bool[,] Pat16(byte[] vram, int offset)
    {
        var m = new bool[16, 16];
        for (int row = 0; row < 16; row++)
        {
            for (int half = 0; half < 2; half++)
            {
                int idx = offset + half * 16 + row;
                byte bits = (idx >= 0 && idx < vram.Length) ? vram[idx] : (byte)0;
                for (int k = 0; k < 8; k++)
                {
                    if (((bits >> (7 - k)) & 1) != 0)
                        m[row, half * 8 + k] = true;
                }
            }
        }
        return m;
    }

    private static byte[] Compose(bool[,] a, bool[,] b, int ca, int cb, (byte R, byte G, byte B)[] pal)
    {
        var rgba = new byte[16 * 16 * 4];
        for (int r = 0; r < 16; r++)
        {
            for (int c = 0; c < 16; c++)
            {
                int val = 0;
                if (a[r, c]) val = ca;
                if (b[r, c]) val |= cb;
                if (val > 0 && val < pal.Length)
                {
                    int o = (r * 16 + c) * 4;
                    rgba[o] = pal[val].R;
                    rgba[o + 1] = pal[val].G;
                    rgba[o + 2] = pal[val].B;
                    rgba[o + 3] = 255;
                }
            }
        }
        return rgba;
    }

    private static byte[] PoseFromLine(byte[] line, (byte R, byte G, byte B)[] pal)
    {
        // 128 bytes = 4 patterns of 32 bytes (16x16): topA, topB, botA, botB
        var topA = Pat16(line, 0);
        var topB = Pat16(line, 32);
        var botA = Pat16(line, 64);
        var botB = Pat16(line, 96);

        byte[] topRgba = Compose(topA, topB, 13, 14, pal);
        byte[] botRgba = Compose(botA, botB, 13, 14, pal);

        var full = new byte[16 * 32 * 4];
        Array.Copy(topRgba, 0, full, 0, 16 * 16 * 4);
        Array.Copy(botRgba, 0, full, 16 * 16 * 4, 16 * 16 * 4);
        return full;
    }

    private static byte[] Page1Line(byte[] vram, int y, int x = 0, int nbytes = 128)
    {
        int a = 0x8000 + y * 128 + x / 2;
        var res = new byte[nbytes];
        if (a + nbytes <= vram.Length)
            Array.Copy(vram, a, res, 0, nbytes);
        return res;
    }

    /// <summary>
    /// Extracts all MSX2 sprites and writes the official kv2.atlas and kv2.png in milliseconds.
    /// </summary>
    public static void ExtractMsx2Atlas(byte[] romBytes, string extractedDirectory, Action<string>? log = null)
    {
        RequireMsx2(romBytes);
        if(!ContestCartridge.Is(romBytes) && (!File.Exists(Path.Combine(extractedDirectory,"Art","Native","arrow.bin")) ||
           !File.Exists(Path.Combine(extractedDirectory,"Art","Native","title_cursor.bin"))))
        {
            if(ContestCartridge.Is(romBytes))ContestCartridge.WriteScreens(romBytes,extractedDirectory);
            else Msx2Screens.WriteSupported(romBytes,extractedDirectory,log);
        }
        log?.Invoke("msx2 atlas: reading exact sprites from ROM...");
        var rom = new RomReader(romBytes);
        var pal = ExtractPalette(rom);
        var sprites = new List<DirectSprite>();

        // 1. Vick poses (7 sets of 14 poses = 98 poses of 16x32)
        var tops = new (byte Y, byte X)[14];
        var bots = new (byte Y, byte X)[14];
        for (int i = 0; i < 14; i++)
        {
            tops[i] = (rom.Byte(0x869C + 2 * i), rom.Byte(0x869D + 2 * i));
            bots[i] = (rom.Byte(0x86B8 + 2 * i), rom.Byte(0x86B9 + 2 * i));
        }

        for (int s = 0; s < 7; s++)
        {
            var vram = new byte[0x20000];
            PlayerSet(rom, vram, s);
            for (int i = 0; i < (s == 0 ? 10 : 14); i++)
            {
                byte[] lineTop = Page1Line(vram, tops[i].Y, tops[i].X, 64);
                byte[] lineBot = Page1Line(vram, bots[i].Y, bots[i].X, 64);
                var fullLine = new byte[128];
                Array.Copy(lineTop, 0, fullLine, 0, 64);
                Array.Copy(lineBot, 0, fullLine, 64, 64);

                sprites.Add(new DirectSprite($"vick_s{s}_{i:D2}", 16, 32, PoseFromLine(fullLine, pal)));
            }
        }

        // 2. Vick fall/death (0x953D -> 0xE000)
        {
            var vram = new byte[0x20000];
            Decompress(rom, vram, 0x953D, 0xE000);
            for (int i = 0; i < 7; i++)
            {
                byte[] line = Page1Line(vram, 0xC0 + i);
                sprites.Add(new DirectSprite($"vick_x_{i:D2}", 16, 32, PoseFromLine(line, pal)));
            }
        }

        // 3. Vick door entrance (0xABB9 -> 0xF800, 8 poses of 128 bytes)
        {
            var vram = new byte[0x20000];
            Decompress(rom, vram, ContestCartridge.Is(romBytes)?0x9EF0:0xABB9, 0xF800);
            for (int i = 0; i < 8; i++)
            {
                var line = new byte[128];
                Array.Copy(vram, 0xF800 + i * 128, line, 0, 128);
                sprites.Add(new DirectSprite($"vick_d_{i:D2}", 16, 32, PoseFromLine(line, pal)));
            }
        }

        // 4. Static patterns (Enemies and Projectiles)
        {
            var vram = new byte[0x20000];
            StaticPatterns(rom, vram);

            // Enemies (26 frames)
            for (int f = 0; f < 26; f++)
            {
                int pa = rom.Byte((ContestCartridge.Is(romBytes)?0xB2A2:0xBEED) + 2 * f, ba: 13) / 4;
                int pb = rom.Byte((ContestCartridge.Is(romBytes)?0xB2A3:0xBEEE) + 2 * f, ba: 13) / 4;
                if (pa == 0 && pb == 0) continue;

                var a = Pat16(vram, 0xF800 + pa * 32);
                var b = Pat16(vram, 0xF800 + pb * 32);

                foreach (var (t, cols) in EnemyColors)
                {
                    sprites.Add(new DirectSprite($"enemy_t{t}_{f:D2}", 16, 16,
                        Compose(a, b, cols.A, cols.B, pal)));
                }
            }

            // Projectiles (pairs from 4 to 14, step 2)
            for (int n = 4; n < 16; n += 2)
            {
                var a = Pat16(vram, 0xF800 + n * 32);
                var b = Pat16(vram, 0xF800 + (n + 1) * 32);
                sprites.Add(new DirectSprite($"proj_{n:D2}", 16, 16,
                    Compose(a, b, 11, 7, pal)));
            }
        }

        // 5. Items, Objects, and Cutscene Sprites
        AddObjectsAndTiles(sprites, pal, extractedDirectory, romBytes);

        // Build authentic intro and world Vick cutscene sprites (80x32) from the 5 Konami walking poses
        string screensDir = Path.Combine(extractedDirectory, "Screens");
        BuildIntroVickScreen(sprites, screensDir, pal.Select(c=>new ScreenColor(c.R,c.G,c.B)).ToArray());

        // Pack and write atlas
        string artDir = Path.Combine(extractedDirectory, "Art");
        Directory.CreateDirectory(artDir);
        PackSprites(sprites, artDir, "kv2", log);
        log?.Invoke($"msx2 atlas: {sprites.Count} source-derived sprite regions saved; validation determines completeness.");
    }

    /// <summary>
    /// Extracts MSX2 room background metadata: blocks_pN.bin, murals.json, decorations.json, palettes.json, animations.json.
    /// </summary>
    public static void ExtractMsx2Rooms(byte[] romBytes, string extractedDirectory, Action<string>? log = null)
    {
        RequireMsx2(romBytes);
        string roomsDir = Path.Combine(extractedDirectory, "Rooms");
        Directory.CreateDirectory(roomsDir);

        var rom = new RomReader(romBytes);

        // 1. murals.json (both hierarchical and flat keys for complete compatibility)
        var murals = new Dictionary<string, object>();
        for (int p = 1; p <= 6; p++)
        {
            var odd = MuralCodes(rom, p, "odd");
            var even = MuralCodes(rom, p, "even");
            murals[p.ToString()] = new Dictionary<string, List<int>>
            {
                ["odd"] = odd,
                ["even"] = even
            };
            murals[$"p{p}_odd"] = odd;
            murals[$"p{p}_even"] = even;
        }
        File.WriteAllText(Path.Combine(roomsDir, "murals.json"),
            JsonSerializer.Serialize(murals, new JsonSerializerOptions { WriteIndented = true }));

        // 2. decorations.json
        var decs = new Dictionary<string, List<object>>();
        for (int s = 1; s <= 60; s++)
        {
            int pyr = (s - 1) / 10 + 1;
            decs[s.ToString(CultureInfo.InvariantCulture)] = DecorationsOf(rom, s, pyr);
        }
        File.WriteAllText(Path.Combine(roomsDir, "decorations.json"),
            JsonSerializer.Serialize(decs, new JsonSerializerOptions { WriteIndented = true }));

        // 3. palettes.json and animations.json
        ExtractMsx2Palettes(rom, roomsDir);

        // 4. blocks_p1.bin .. blocks_p6.bin
        EnsureBlocks(roomsDir, romBytes);

        log?.Invoke("msx2 rooms: murals, decorations, palettes and room blocks ready.");
    }

    public static List<int> MuralCodes(RomReader rom, int pyramid, string stageParity)
    {
        int baseAddr = rom.Word(0x7FFE + 2 * pyramid) + (stageParity == "odd" ? 0x20 : 0);
        int p = rom.Off(baseAddr);
        var list = new List<int>(32);
        for (int i = 0; i < 32; i++)
            list.Add(rom.Data[p + i]);
        return list;
    }

    public static List<object> DecorationsOf(RomReader rom, int stage, int pyramid)
    {
        int lst = rom.Off(rom.Word(0x806A + 2 * stage));
        byte[] d = rom.Data;
        var outList = new List<object>();
        while (lst + 2 < d.Length && d[lst] != 0xFF)
        {
            byte b0 = d[lst];
            byte b1 = d[lst + 1];
            byte nxt = d[lst + 2];
            lst += 3;

            int kind = b0 & 7;
            int y = b0 & 0xF8;
            int x = b1 & 0xF8;
            int room = (b1 & 7) + 1;
            int gfx = rom.Off(rom.Word(0x800C + 2 * ((pyramid - 1) * 8 + kind)));
            int shape = kind >> 1;

            int rows, cols, height;
            string layout;
            var codes = new List<int>();

            if (shape == 3)
            {
                rows = 1; cols = 2; height = nxt; layout = "pillar";
                codes.Add(d[gfx]); codes.Add(d[gfx + 1]); codes.Add(d[gfx + 2]); codes.Add(d[gfx + 3]);
            }
            else if (shape == 0)
            {
                rows = 4; cols = 4; height = 4; layout = "grid";
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        codes.Add(d[gfx + r * 4 + c]);
            }
            else if (shape == 1)
            {
                rows = 4; cols = 2; height = 4; layout = "grid";
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 2; c++)
                        codes.Add(d[gfx + r * 2 + c]);
            }
            else
            {
                rows = 2; cols = 4; height = 2; layout = "grid";
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 4; c++)
                        codes.Add(d[gfx + r * 4 + c]);
            }

            outList.Add(new
            {
                kind,
                shape = layout,
                room,
                y,
                x,
                extra = nxt,
                rows,
                cols,
                height,
                blocks = codes
            });
        }
        return outList;
    }

    public static void ExtractMsx2Palettes(RomReader rom, string roomsDir)
    {
        var palettes = new Dictionary<string, List<int[]>>();
        var animations = new Dictionary<string, List<object>>();

        int baseAddr = rom.Off(0xA000, b8: 14, ba: 15);
        byte[] data = rom.Data;
        var fixedPal = ExtractPalette(rom);

        for (int pyr = 1; pyr <= 6; pyr++)
        {
            var colors = new List<int[]>(16);
            for (int k = 0; k < 16; k++)
                colors.Add(new int[] { fixedPal[k].R, fixedPal[k].G, fixedPal[k].B });

            // Read pyramid specific colors from pointer table at 0xB97A in Bank 15
            int tableOff = baseAddr + (0xB97A + pyr * 2 - 0xA000);
            if (tableOff + 1 < data.Length)
            {
                int ptr = data[tableOff] | (data[tableOff + 1] << 8);
                int ptrOff = baseAddr + (ptr - 0xA000);
                while (ptrOff + 2 < data.Length && data[ptrOff] != 0xFF)
                {
                    int colIdx = data[ptrOff];
                    byte rb = data[ptrOff + 1];
                    byte g = data[ptrOff + 2];
                    int r = (rb >> 4) & 7;
                    int b = rb & 7;
                    int gg = g & 7;
                    if (colIdx < 16)
                    {
                        colors[colIdx] = new int[] { r * 255 / 7, gg * 255 / 7, b * 255 / 7 };
                    }
                    ptrOff += 3;
                }
            }

            palettes[pyr.ToString(CultureInfo.InvariantCulture)] = colors;

            // Color 6 animation cycle
            int off = baseAddr + (0xBD52 + 5 * pyr - 0xA000);
            byte colour = data[off];
            byte limit = data[off + 1];
            byte period = data[off + 2];
            int rampPtr = data[off + 3] | (data[off + 4] << 8);
            int rampOff = baseAddr + (rampPtr - 0xA000);

            var ramp = new List<int[]>();
            for (int k = 0; k < limit; k++)
            {
                int w = data[rampOff + 2 * k] | (data[rampOff + 2 * k + 1] << 8);
                int rr = (w >> 4) & 7;
                int bb = w & 7;
                int gg = (w >> 8) & 7;
                ramp.Add(new int[] { (int)(rr * 255 / 7), (int)(gg * 255 / 7), (int)(bb * 255 / 7) });
            }

            var slots = new List<object>
            {
                new { colour, limit, period, ramp }
            };

            if (pyr == 4)
            {
                int extraRampOff = baseAddr + (0xBDAF - 0xA000);
                var extraRamp = new List<int[]>();
                for (int k = 0; k < 2; k++)
                {
                    int w = data[extraRampOff + 2 * k] | (data[extraRampOff + 2 * k + 1] << 8);
                    int rr = (w >> 4) & 7;
                    int bb = w & 7;
                    int gg = (w >> 8) & 7;
                    extraRamp.Add(new int[] { (int)(rr * 255 / 7), (int)(gg * 255 / 7), (int)(bb * 255 / 7) });
                }
                slots.Add(new { colour = 6, limit = 2, period = 5, ramp = extraRamp });
            }

            animations[pyr.ToString(CultureInfo.InvariantCulture)] = slots;
        }

        File.WriteAllText(Path.Combine(roomsDir, "palettes.json"),
            JsonSerializer.Serialize(palettes, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(roomsDir, "animations.json"),
            JsonSerializer.Serialize(animations, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] Extract4bppBlockGrid(byte[] blocks, int[] codes, int cols, int rows, (byte R, byte G, byte B)[] pal)
    {
        int width = cols * 8;
        int height = rows * 8;
        var rgba = new byte[width * height * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int code = codes[r * cols + c];
                if (code <= 0 || code >= 256) continue;
                int blockOffset = code * 32;
                if (blockOffset + 32 > blocks.Length) continue;

                for (int y = 0; y < 8; y++)
                {
                    int py = r * 8 + y;
                    for (int xByte = 0; xByte < 4; xByte++)
                    {
                        byte b = blocks[blockOffset + y * 4 + xByte];
                        int p0 = (b >> 4) & 0x0F;
                        int p1 = b & 0x0F;

                        if (p0 > 0 && p0 < pal.Length)
                        {
                            int px = c * 8 + xByte * 2;
                            int o = (py * width + px) * 4;
                            rgba[o] = pal[p0].R;
                            rgba[o + 1] = pal[p0].G;
                            rgba[o + 2] = pal[p0].B;
                            rgba[o + 3] = 255;
                        }
                        if (p1 > 0 && p1 < pal.Length)
                        {
                            int px = c * 8 + xByte * 2 + 1;
                            int o = (py * width + px) * 4;
                            rgba[o] = pal[p1].R;
                            rgba[o + 1] = pal[p1].G;
                            rgba[o + 2] = pal[p1].B;
                            rgba[o + 3] = 255;
                        }
                    }
                }
            }
        }
        return rgba;
    }

    private static void AddObjectsAndTiles(List<DirectSprite> sprites, (byte R, byte G, byte B)[] pal, string extractedDir, byte[] romBytesForFont)
    {
        string roomsDir = Path.Combine(extractedDir, "Rooms");

        // 1. Load authentic pyramid palettes from palettes.json
        var pyramidPalettes = new Dictionary<int, (byte R, byte G, byte B)[]>();
        string palJsonPath = Path.Combine(roomsDir, "palettes.json");
        if (File.Exists(palJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(palJsonPath));
                for (int p = 1; p <= 6; p++)
                {
                    if (doc.RootElement.TryGetProperty(p.ToString(), out var el))
                    {
                        var pList = new (byte R, byte G, byte B)[16];
                        int idx = 0;
                        foreach (var cEl in el.EnumerateArray())
                        {
                            if (idx < 16)
                            {
                                byte r = (byte)cEl[0].GetInt32();
                                byte g = (byte)cEl[1].GetInt32();
                                byte b = (byte)cEl[2].GetInt32();
                                pList[idx++] = (r, g, b);
                            }
                        }
                        pyramidPalettes[p] = pList;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RomDirectExtractor] palettes.json read: {ex.Message}");
            }
        }

        // 2. Load authentic 4bpp tileblocks from blocks_p1.bin .. blocks_p6.bin
        var pyramidBlocks = new Dictionary<int, byte[]>();
        for (int p = 1; p <= 6; p++)
        {
            string pPath = Path.Combine(roomsDir, $"blocks_p{p}.bin");
            if (File.Exists(pPath))
                pyramidBlocks[p] = File.ReadAllBytes(pPath);
            else
                throw new InvalidDataException($"Pyramid {p} block table is missing.");
        }

        byte[] blocksP1 = pyramidBlocks[1];
        var palP1 = pyramidPalettes.TryGetValue(1, out var pp1) ? pp1 : pal;

        // 3. Authentic MSX2 Items (2x2 8x8 blocks = 16x16, Table 0x9328 in Bank 2)
        var itemTiles = new (string Name, int[] Codes)[]
        {
            ("knife",     new[] { 129, 130, 188, 189 }),
            ("boomerang", new[] { 0,   0,   137, 138 }),
            ("shovel",    new[] { 134, 135, 184, 136 }),
            ("pickaxe",   new[] { 185, 187, 186, 131 }),
            ("hammer",    new[] { 180, 181, 182, 132 }),
            ("drill",     new[] { 178, 179, 183, 133 }),
        };
        foreach (var (name, codes) in itemTiles)
        {
            byte[] rgba = Extract4bppBlockGrid(blocksP1, codes, 2, 2, palP1);
            sprites.Add(new DirectSprite(name, 16, 16, rgba));
            sprites.Add(new DirectSprite("item_" + name, 16, 16, rgba));
        }

        // 4. Objects per pyramid (1..6)
        int[] soulstoneCodes = { 175, 176, 177, 128 }; // 2x2 = 16x16 (Table 0x9324)
        int[] coffinClosed = { 155, 156, 157, 158, 159, 160 }; // 2 cols, 3 rows = 16x24 (Table 0x93AC)
        int[] coffinAjar   = { 161, 162, 163, 164, 165, 166 }; // Table 0x93B2
        int[] coffinOpen   = { 167, 168, 169, 170, 171, 172 }; // Table 0x93B8
        int[] doorF0 = { 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114 }; // 4x4 = 32x32 (Table 0x9340)
        int[] doorF1 = { 100, 88, 89, 101, 104, 90, 91, 105, 108, 92, 93, 109, 112, 120, 120, 113 };
        int[] doorF2 = { 115, 88, 89, 116, 117, 90, 91, 118, 117, 92, 93, 118, 119, 120, 120, 121 };

        for (int p = 1; p <= 6; p++)
        {
            byte[] blocks = pyramidBlocks.TryGetValue(p, out var b) ? b : blocksP1;
            var pyrPal = pyramidPalettes.TryGetValue(p, out var pp) ? pp : pal;

            // Soulstone 16x16
            sprites.Add(new DirectSprite($"soulstone_p{p}", 16, 16,
                Extract4bppBlockGrid(blocks, soulstoneCodes, 2, 2, pyrPal)));

            // Sarcophagus 16x24
            sprites.Add(new DirectSprite($"coffin_p{p}", 16, 24,
                Extract4bppBlockGrid(blocks, coffinClosed, 2, 3, pyrPal)));
            sprites.Add(new DirectSprite($"coffin_ajar_p{p}", 16, 24,
                Extract4bppBlockGrid(blocks, coffinAjar, 2, 3, pyrPal)));
            sprites.Add(new DirectSprite($"coffin_open_p{p}", 16, 24,
                Extract4bppBlockGrid(blocks, coffinOpen, 2, 3, pyrPal)));

            // Exit Doors 32x32
            sprites.Add(new DirectSprite($"door_f0_p{p}", 32, 32,
                Extract4bppBlockGrid(blocks, doorF0, 4, 4, pyrPal)));
            sprites.Add(new DirectSprite($"door_f1_p{p}", 32, 32,
                Extract4bppBlockGrid(blocks, doorF1, 4, 4, pyrPal)));
            sprites.Add(new DirectSprite($"door_f2_p{p}", 32, 32,
                Extract4bppBlockGrid(blocks, doorF2, 4, 4, pyrPal)));

            // Door Stud (Gold) 8x8 (tile 174 = 0xAE)
            sprites.Add(new DirectSprite($"door_stud_p{p}", 8, 8,
                Extract4bppBlockGrid(blocks, new[] { 174 }, 1, 1, pyrPal)));

            // Clean door blocks per slot (0..15)
            for (int bi = 0; bi < 16; bi++)
            {
                sprites.Add(new DirectSprite($"door_clean_{bi}_p{p}", 8, 8,
                    Extract4bppBlockGrid(blocks, new[] { doorF0[bi] }, 1, 1, pyrPal)));

            }
            sprites.Add(new DirectSprite($"door_empty_stud_p{p}", 8, 8,
                Extract4bppBlockGrid(blocks, new[] { doorF0[0] }, 1, 1, pyrPal)));

            // Ladder 8x8 (block 3 from blocks_p{p}.bin)
            sprites.Add(new DirectSprite($"ladder_p{p}", 8, 8,
                Extract4bppBlockGrid(blocks, new[] { 3 }, 1, 1, pyrPal)));

            // Walls (block 5) and Soft Sand (block 0x5E = 94)
            byte[] wall = Extract4bppBlockGrid(blocks, new[] { 5 }, 1, 1, pyrPal);
            for (int v = 0; v < 4; v++)
                sprites.Add(new DirectSprite($"wall_p{p}_{v}", 8, 8, wall));

            byte[] soft = Extract4bppBlockGrid(blocks, new[] { 0x5E }, 1, 1, pyrPal);
            for (int v = 0; v < 4; v++)
                sprites.Add(new DirectSprite($"soft_p{p}_{v}", 8, 8, soft));

            // Crumble stages 6..23
            for (int code = 6; code < 24; code++)
            {
                sprites.Add(new DirectSprite($"crumble_{code}_p{p}", 8, 8,
                    Extract4bppBlockGrid(blocks, new[] { code }, 1, 1, pyrPal)));
            }
        }

        // SCREEN 5 world-map font: bank 0C AA29, codes D0..FA (routine 5B16).
        // Digits D0..D9, exclamation DB, alphabet DD..F6. No embedded bitmap substitutes.
        byte[] Glyph(int code, int color)
        {
            int source = (ContestCartridge.Is(romBytesForFont)?0x1673C:12 * 0x2000 + 0xAA29 - 0xA000) + (code - 0xD0) * 8;
            var rgba = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if ((romBytesForFont[source + y] & (128 >> x)) != 0)
                    {
                        int offset = (y * 8 + x) * 4;
                        rgba[offset] = pal[color].R; rgba[offset + 1] = pal[color].G; rgba[offset + 2] = pal[color].B; rgba[offset + 3] = 255;
                    }
            return rgba;
        }
        for (int i = 0; i < 26; i++) sprites.Add(new DirectSprite("font_" + (char)('A' + i), 8, 8, Glyph(0xDD + i, 11)));
        for (int i = 0; i < 10; i++) sprites.Add(new DirectSprite("digit_" + i, 8, 8, Glyph(0xD0 + i, 10)));
        sprites.Add(new DirectSprite("font_excl", 8, 8, Glyph(0xDB, 11)));
        // Draw only the native static HUD script (5EF2) after the original stage setup.
        // The number fields remain untouched; the port supplies the current score/lives later.
        var hudMachine=new Msx2VdpRunner(romBytesForFont);
        bool contest=ContestCartridge.Is(romBytesForFont);
        if(contest){hudMachine.BootContest();ContestCartridge.Prepare(hudMachine,1);}
        else {hudMachine.Boot();hudMachine.PrepareAndPaint(1);}
        hudMachine.Invoke(contest?(ushort)0x5679:(ushort)0x5BEB); // Native normal-font tiles, required before the HUD text stream.
        hudMachine.Invoke(contest?(ushort)0x4D99:(ushort)0x51D0,hl:contest?(ushort)0x5C1A:(ushort)0x5EF2);
        var hud=hudMachine.SnapshotScreen5();
        byte[] strip=new byte[256*20*4];
        for(int i=0;i<256*20;i++)
        {
            var color=hud.Palette[hud.Indices[192*256+i]];
            strip[i*4]=color.R; strip[i*4+1]=color.G; strip[i*4+2]=color.B; strip[i*4+3]=255;
        }
        sprites.Add(new DirectSprite("hud_strip",256,20,strip));
        if(contest)
        {
            // Use the native normal-font writer for score digits. The generic world-map
            // digit regions have another shape/color and must not cover the Contest HUD.
            for(int digit=0;digit<10;digit++)
            {
                hudMachine.SetMemory(0xF100,80);hudMachine.SetMemory(0xF101,198);
                hudMachine.SetMemory(0xF102,(byte)(0xD0+digit));hudMachine.SetMemory(0xF103,255);
                hudMachine.Invoke(0x4D99,hl:0xF100);
                var image=hudMachine.SnapshotScreen5();var rgba=new byte[8*8*4];
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                {
                    var color=image.Palette[image.Indices[(198+y)*256+80+x]];int offset=(y*8+x)*4;
                    rgba[offset]=color.R;rgba[offset+1]=color.G;rgba[offset+2]=color.B;rgba[offset+3]=255;
                }
                sprites.Add(new DirectSprite($"hud_digit_{digit}",8,8,rgba));
            }
        }
        // Native UI colors belong to each capture, not the gameplay palette.
        foreach(string name in contest?Array.Empty<string>():new[]{"arrow","title_cursor"})
        {
            var image=ScreenAssembler.DecodeFile(Path.Combine(extractedDir,"Art","Native",name+".bin"));
            var rgba=new byte[image.Width*image.Height*4];
            for(int pixel=0;pixel<image.Indices.Length;pixel++)
            {
                byte index=image.Indices[pixel];var color=image.Palette[index];
                rgba[pixel*4]=color.R;rgba[pixel*4+1]=color.G;rgba[pixel*4+2]=color.B;
                rgba[pixel*4+3]=image.HasAlpha && index==0?(byte)0:(byte)255;
            }
            sprites.Add(new DirectSprite(name,image.Width,image.Height,rgba));
        }

    }

    private static byte[] ExtractVramSprite16Rgba(byte[] vram, int spriteBase, int pattern, int color, (byte R, byte G, byte B)[] pal)
    {
        var rgba = new byte[16 * 16 * 4];
        int offset = spriteBase + pattern * 8;
        if (offset + 32 > vram.Length) return rgba;

        for (int r = 0; r < 16; r++)
        {
            byte left = vram[offset + r];
            byte right = vram[offset + 16 + r];
            for (int c = 0; c < 8; c++)
            {
                if (((left >> (7 - c)) & 1) != 0 && color < pal.Length)
                {
                    int o = (r * 16 + c) * 4;
                    rgba[o] = pal[color].R; rgba[o + 1] = pal[color].G; rgba[o + 2] = pal[color].B; rgba[o + 3] = 255;
                }
                if (((right >> (7 - c)) & 1) != 0 && color < pal.Length)
                {
                    int o = (r * 16 + 8 + c) * 4;
                    rgba[o] = pal[color].R; rgba[o + 1] = pal[color].G; rgba[o + 2] = pal[color].B; rgba[o + 3] = 255;
                }
            }
        }
        return rgba;
    }

    private static byte[] ExtractTileBlockRgba(byte[] vram, int tableBase, int[] tileCodes, int cols, int rows, int color, (byte R, byte G, byte B)[] pal)
    {
        int width = cols * 8;
        int height = rows * 8;
        var rgba = new byte[width * height * 4];
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
                        if (((bits >> (7 - x)) & 1) != 0 && color < pal.Length)
                        {
                            int o = ((r * 8 + y) * width + (c * 8 + x)) * 4;
                            rgba[o] = pal[color].R; rgba[o + 1] = pal[color].G; rgba[o + 2] = pal[color].B; rgba[o + 3] = 255;
                        }
                    }
                }
            }
        }
        return rgba;
    }

    private static byte[] ExtractTile8Rgba(byte[] vram, int tableBase, int code, int color, (byte R, byte G, byte B)[] pal)
    {
        var rgba = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            byte bits = vram[(tableBase + code * 8 + y) & 0x3FFF];
            for (int x = 0; x < 8; x++)
            {
                if (((bits >> (7 - x)) & 1) != 0 && color < pal.Length)
                {
                    int o = (y * 8 + x) * 4;
                    rgba[o] = pal[color].R; rgba[o + 1] = pal[color].G; rgba[o + 2] = pal[color].B; rgba[o + 3] = 255;
                }
            }
        }
        return rgba;
    }

    private static byte[] ExtractDumpTileRgba(byte[] dump, int row, int col, int width, int height, (byte R, byte G, byte B)[] pal)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int dumpX = col * 8 + x;
                int dumpY = row * 8 + y;
                if (dumpX >= 256 || dumpY >= 192) continue;
                int byteOff = 38 + dumpY * 128 + dumpX / 2;
                if (byteOff >= dump.Length) continue;

                byte b = dump[byteOff];
                byte colorIdx = (dumpX % 2 == 0) ? (byte)(b >> 4) : (byte)(b & 0x0F);
                if (colorIdx > 0 && colorIdx < pal.Length)
                {
                    int o = (y * width + x) * 4;
                    rgba[o] = pal[colorIdx].R;
                    rgba[o + 1] = pal[colorIdx].G;
                    rgba[o + 2] = pal[colorIdx].B;
                    rgba[o + 3] = 255;
                }
            }
        }
        return rgba;
    }

    internal static void PackSprites(List<DirectSprite> sprites, string artDir, string baseName, Action<string>? log)
    {
        const int sheetWidth = 512;
        const int padding = 1;
        int x = padding;
        int y = padding;
        int rowHeight = 0;

        foreach (var s in sprites)
        {
            if (x + s.Width + padding > sheetWidth)
            {
                x = padding;
                y += rowHeight + padding;
                rowHeight = 0;
            }
            s.X = x;
            s.Y = y;
            x += s.Width + padding;
            rowHeight = Math.Max(rowHeight, s.Height);
        }

        int width = sheetWidth;
        int height = y + rowHeight + padding;
        var fullRgba = new byte[width * height * 4];
        var atlas = new StringBuilder(sprites.Count * 28);

        foreach (var s in sprites)
        {
            for (int sy = 0; sy < s.Height; sy++)
            {
                for (int sx = 0; sx < s.Width; sx++)
                {
                    int srcOff = (sy * s.Width + sx) * 4;
                    if (s.Rgba[srcOff + 3] == 0) continue;

                    int dstOff = ((s.Y + sy) * width + s.X + sx) * 4;
                    fullRgba[dstOff] = s.Rgba[srcOff];
                    fullRgba[dstOff + 1] = s.Rgba[srcOff + 1];
                    fullRgba[dstOff + 2] = s.Rgba[srcOff + 2];
                    fullRgba[dstOff + 3] = s.Rgba[srcOff + 3];
                }
            }

            atlas.Append(s.Name).Append('\n');
            atlas.Append(s.X).Append(',').Append(s.Y).Append(',')
                 .Append(s.Width).Append(',').Append(s.Height).Append('\n');
            atlas.Append("0,0\n");
        }

        WritePngRgba(Path.Combine(artDir, baseName + ".png"), width, height, fullRgba);
        File.WriteAllText(Path.Combine(artDir, baseName + ".atlas"), atlas.ToString(),
            new UTF8Encoding(false));
    }

    private static void WritePngRgba(string path, int width, int height, byte[] rgba)
    {
        using var stream = File.Create(path);
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        ihdr[0] = (byte)(width >> 24); ihdr[1] = (byte)(width >> 16);
        ihdr[2] = (byte)(width >> 8); ihdr[3] = (byte)width;
        ihdr[4] = (byte)(height >> 24); ihdr[5] = (byte)(height >> 16);
        ihdr[6] = (byte)(height >> 8); ihdr[7] = (byte)height;
        ihdr[8] = 8;
        ihdr[9] = 6; // RGBA
        WriteChunk(stream, "IHDR", ihdr);

        using (var raw = new MemoryStream())
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter: None
                raw.Write(rgba, y * rowBytes, rowBytes);
            }

            raw.Position = 0;
            using var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.CopyTo(deflate);
            }

            var idat = new byte[compressed.Length + 6];
            idat[0] = 0x78; idat[1] = 0x9C;
            compressed.Position = 0;
            compressed.Read(idat, 2, (int)compressed.Length);

            uint a = 1, b = 0;
            raw.Position = 0;
            int current;
            while ((current = raw.ReadByte()) >= 0)
            {
                a = (a + (uint)current) % 65521;
                b = (b + a) % 65521;
            }
            uint adler = (b << 16) | a;
            int end = idat.Length - 4;
            idat[end] = (byte)(adler >> 24);
            idat[end + 1] = (byte)(adler >> 16);
            idat[end + 2] = (byte)(adler >> 8);
            idat[end + 3] = (byte)adler;

            WriteChunk(stream, "IDAT", idat);
        }

        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] len =
        {
            (byte)(data.Length >> 24), (byte)(data.Length >> 16),
            (byte)(data.Length >> 8), (byte)data.Length
        };
        stream.Write(len);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeBytes) crc = UpdateCrc(crc, b);
        foreach (byte b in data) crc = UpdateCrc(crc, b);
        crc ^= 0xFFFFFFFF;

        byte[] crcBytes = { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
        stream.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    // ── Authentic Konami Intro Vick and Block Extraction ──

    public static void BuildIntroVickScreen(IReadOnlyList<DirectSprite> sprites,string screensDir,IReadOnlyList<ScreenColor> palette,ScreenPaletteModel model=ScreenPaletteModel.V9938)
    {
        Directory.CreateDirectory(screensDir);
        string[] names={"vick_s0_03","vick_s0_04","vick_s0_05","vick_d_00","vick_d_01"};
        byte[] rgba=new byte[80*32*4];
        for(int pose=0;pose<names.Length;pose++)
        {
            var sprite=sprites.FirstOrDefault(s=>s.Name==names[pose]);
            if(sprite==null || sprite.Width!=16 || sprite.Height!=32) throw new InvalidDataException("Missing native cutscene pose: "+names[pose]);
            for(int row=0;row<32;row++) Array.Copy(sprite.Rgba,row*16*4,rgba,(row*80+pose*16)*4,16*4);
        }
        var colors=palette.Select(c=>(c.R,c.G,c.B)).ToArray();
        byte[] indices=Wagenheimer.MsxGraphics.Formats.PaletteIndexing.FromRgba(rgba,colors,transparentZero:true);
        byte[] encoded=ScreenAssembler.Encode(80,32,palette,indices,hasAlpha:true,paletteModel:model);
        File.WriteAllBytes(Path.Combine(screensDir,"intro_vick.bin"),encoded);
        File.WriteAllBytes(Path.Combine(screensDir,"world_vick.bin"),encoded);
    }

    /// <summary>
    /// Extracts authentic MSX2 tileblocks (blocks_p1..6.bin, 8192 bytes each) using the authentic
    /// Konami MSX2 cartridge V9938 routines via <see cref="Msx2VdpRunner"/>.
    /// </summary>
    public static void EnsureBlocks(string roomsDir, byte[]? msx2Rom = null)
    {
        ArgumentNullException.ThrowIfNull(msx2Rom);
        var identity = Wagenheimer.MsxAudio.Extraction.RomExtractionProfiles.FindForRom(msx2Rom);
        if (identity?.GameId != "kings-valley-2" || identity.Variant != "msx2")
            throw new InvalidDataException("MSX2 block extraction requires a verified MSX2 cartridge.");
        Directory.CreateDirectory(roomsDir);
        var runner = new Msx2VdpRunner(msx2Rom);
        runner.Boot();
        var palettes = new Dictionary<string, List<int[]>>();
        for (int pyramid = 1; pyramid <= 6; pyramid++)
        {
            byte[] blocks = runner.ExtractBlocks(pyramid);
            if (blocks.Length != 8192 || blocks.All(value => value == 0))
                throw new InvalidDataException($"Pyramid {pyramid} produced an invalid block table.");
            File.WriteAllBytes(Path.Combine(roomsDir, $"blocks_p{pyramid}.bin"), blocks);
            palettes[pyramid.ToString(CultureInfo.InvariantCulture)] = runner.Palette
                .Select(c => new[] { c.R * 255 / 7, c.G * 255 / 7, c.B * 255 / 7 }).ToList();
        }
        File.WriteAllText(Path.Combine(roomsDir, "palettes.json"), JsonSerializer.Serialize(palettes, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Ensures MSX2 screens are present without using any programmatic drawing.
    /// Screens are decoded authentically from the ROM via RomScreens.
    /// </summary>
    public static void ExtractMsx2Screens(byte[] romBytes, string extractedDirectory, Action<string>? log = null)
    {
        RequireMsx2(romBytes);
        Msx2Screens.WriteSupported(romBytes, extractedDirectory, log);
    }
}
