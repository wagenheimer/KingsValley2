#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Builds the **extraction report**: a <c>preview/</c> folder with the fixed screens, the 60 stages and
/// the pattern sheets (sprites, tiles and the font) as PNG, plus an <c>index.html</c> gallery that opens
/// in the browser when the extraction finishes.
///
/// <para>Everything in the report comes from the player's own cartridge: the screens and the pattern
/// tables are read from the VDP after running the game, and the stages from the extracted JSON. The
/// port's MSX2 art is only linked when it exists on disk (development builds), never extracted.</para>
/// </summary>
public static class ExtractionPreview
{
    private const int ScreenScale = 2;
    private const int StageCell = 3;
    private const int StageRoomWidth = 32 * StageCell;
    private const int StageRoomHeight = 24 * StageCell;
    private const int StagePadding = 5;
    private const int SheetScale = 3;

    /// <summary>MSX1 (TMS9918) fixed palette, used to colour the sheets.</summary>
    private static readonly (byte R, byte G, byte B)[] Tms =
    {
        (0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
        (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
        (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
        (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255),
    };

    /// <summary>Stage map colours (the JSON is abstract: wall / diggable / ladder / empty).</summary>
    private static readonly Dictionary<char, (byte R, byte G, byte B)> TileColors = new()
    {
        ['#'] = (206, 206, 216),
        ['?'] = (146, 112, 78),
        ['='] = (86, 200, 126),
        ['.'] = (24, 26, 38),
        [' '] = (24, 26, 38),
    };

    /// <summary>
    /// Builds the report under <paramref name="extractedDirectory"/>/preview and opens it.
    /// </summary>
    /// <param name="extractedDirectory">Folder with the extracted content (Screens/, Levels/, audio).</param>
    /// <param name="packagePath">The <c>.msxaudio</c> package (only for the music section).</param>
    /// <param name="rom">Cartridge image, used for the pattern sheets.</param>
    /// <param name="log">Receives one progress line per step.</param>
    public static void Build(string extractedDirectory, string? packagePath, byte[]? rom,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedDirectory);

        string preview = Path.Combine(extractedDirectory, "preview");
        string screensDir = Path.Combine(preview, "screens");
        string stagesDir = Path.Combine(preview, "stages");
        string patternsDir = Path.Combine(preview, "patterns");

        Directory.CreateDirectory(screensDir);
        Directory.CreateDirectory(stagesDir);

        var screens = BuildScreens(extractedDirectory, screensDir, log);
        var stages = BuildStages(extractedDirectory, stagesDir, log);
        var patterns = BuildPatterns(rom, patternsDir, log);
        // BuildSpriteAtlas was writing a dummy 235-byte atlas that shadowed the real game atlas.
        var compare = BuildCompare(extractedDirectory, Path.Combine(preview, "compare"));
        var music = BuildMusicSheet(packagePath, preview, log);

        string index = Path.Combine(preview, "index.html");
        File.WriteAllText(index,
            BuildHtml(screens, stages, patterns, compare, music, ReadStamp(extractedDirectory)),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        TryWriteShortcut(Path.Combine(extractedDirectory, "Open gallery.url"), index);

        log?.Invoke($"report: {screens.Count} screens, {stages.Count} stages, {patterns.Count} sheets, " +
                    $"{music.Songs} songs");

        // Show it: the first extraction ends with the report open in the browser.
        if (!string.Equals(Environment.GetEnvironmentVariable("KINGSVALLEY_NO_GALLERY"), "1",
                StringComparison.Ordinal))
        {
            TryOpen(index);
        }
    }

    // ── screens ─────────────────────────────────────────────────────────────────────────

    private static List<(string Name, string File)> BuildScreens(string extracted, string outDir, Action<string>? log)
    {
        var result = new List<(string, string)>();
        string source = Path.Combine(extracted, "Screens");
        if (!Directory.Exists(source))
            return result;

        foreach (string file in Directory.GetFiles(source, "*.bin").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                ScreenData screen = ScreenAssembler.DecodeFile(file);
                WritePng(Path.Combine(outDir, name + ".png"),
                    screen.Width * ScreenScale, screen.Height * ScreenScale,
                    ScaleRgb(screen, ScreenScale));
                result.Add((name, "screens/" + name + ".png"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExtractionPreview] screen {name}: {ex.Message}");
            }
        }

        log?.Invoke($"report: {result.Count} screens");
        return result;
    }

    private static byte[] ScaleRgb(ScreenData screen, int scale)
    {
        int width = screen.Width * scale;
        int height = screen.Height * scale;
        var rgb = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            int sy = y / scale;
            for (int x = 0; x < width; x++)
            {
                ScreenColor c = screen.Palette[screen.Indices[sy * screen.Width + x / scale] & 0x0F];
                int offset = (y * width + x) * 3;
                rgb[offset] = c.R;
                rgb[offset + 1] = c.G;
                rgb[offset + 2] = c.B;
            }
        }

        return rgb;
    }

    // ── stages ──────────────────────────────────────────────────────────────────────────

    private static List<(string Name, string File)> BuildStages(string extracted, string outDir, Action<string>? log)
    {
        var result = new List<(string, string)>();
        string source = Path.Combine(extracted, "Levels");
        if (!Directory.Exists(source))
            return result;

        foreach (string file in Directory.GetFiles(source, "stage*.json").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                if (!TryDrawStage(file, out byte[]? rgb, out int width, out int height))
                    continue;

                WritePng(Path.Combine(outDir, name + ".png"), width, height, rgb!);
                result.Add((name, "stages/" + name + ".png"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExtractionPreview] stage {name}: {ex.Message}");
            }
        }

        log?.Invoke($"report: {result.Count} stages");
        return result;
    }

    private static bool TryDrawStage(string jsonPath, out byte[]? rgb, out int width, out int height)
    {
        rgb = null;
        width = height = 0;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("rooms", out JsonElement rooms) || rooms.GetArrayLength() == 0)
            return false;

        int count = rooms.GetArrayLength();
        int cols = Math.Min(4, count);
        int rows = (count + cols - 1) / cols;

        width = cols * StageRoomWidth + (cols + 1) * StagePadding;
        height = rows * StageRoomHeight + (rows + 1) * StagePadding;
        var canvas = new byte[width * height * 3];
        Fill(canvas, 10, 11, 18);

        for (int i = 0; i < count; i++)
        {
            int ox = StagePadding + i % cols * (StageRoomWidth + StagePadding);
            int oy = StagePadding + i / cols * (StageRoomHeight + StagePadding);

            if (!rooms[i].TryGetProperty("rows", out JsonElement lines))
                continue;

            for (int y = 0; y < lines.GetArrayLength(); y++)
            {
                string line = lines[y].GetString() ?? string.Empty;
                for (int x = 0; x < line.Length; x++)
                {
                    (byte R, byte G, byte B) color = TileColors.TryGetValue(line[x], out var c)
                        ? c
                        : ((byte)255, (byte)0, (byte)255);

                    FillRect(canvas, width, ox + x * StageCell, oy + y * StageCell,
                        StageCell, StageCell, color);
                }
            }
        }

        DrawMarkers(root, canvas, width, height, count, cols);
        rgb = canvas;
        return true;
    }

    private static void DrawMarkers(JsonElement stage, byte[] canvas, int width, int height, int rooms, int cols)
    {
        void Mark(JsonElement item, (byte R, byte G, byte B) color, int size)
        {
            int room = item.TryGetProperty("room", out JsonElement r) ? r.GetInt32() : 1;
            int x = item.TryGetProperty("x", out JsonElement px) ? px.GetInt32() : 0;
            int y = item.TryGetProperty("y", out JsonElement py) ? py.GetInt32() : 0;

            int index = Math.Clamp(room - 1, 0, rooms - 1);
            int ox = StagePadding + index % cols * (StageRoomWidth + StagePadding) + x / 8 * StageCell;
            int oy = StagePadding + index / cols * (StageRoomHeight + StagePadding) + y / 8 * StageCell;

            OutlineRect(canvas, width, height, ox, oy, StageCell * size, StageCell * size, color);
        }

        foreach (string key in new[] { "soulStones", "coffins", "items", "mechanisms" })
        {
            if (!stage.TryGetProperty(key, out JsonElement list))
                continue;

            (byte, byte, byte) color = key switch
            {
                "soulStones" => (86, 156, 255),
                "coffins" => (232, 92, 220),
                "items" => (255, 255, 255),
                _ => (86, 232, 232),
            };

            foreach (JsonElement item in list.EnumerateArray())
                Mark(item, color, 1);
        }

        if (stage.TryGetProperty("exitDoor", out JsonElement door))
        {
            int size = door.TryGetProperty("w", out JsonElement w) ? Math.Max(1, w.GetInt32() / 4) : 1;
            Mark(door, (255, 214, 64), size);
        }

        if (stage.TryGetProperty("playerStart", out JsonElement player))
            Mark(player, (255, 148, 40), 2);
    }

    // ── pattern sheets (sprites, tiles, font) ───────────────────────────────────────────

    /// <summary>
    /// Reads the pattern tables the game left in the VDP and writes three sheets: the 256 sprite
    /// patterns (8x8), the 41 sprites composed as 16x16 and the 256 background tiles with their
    /// colours (where the A-Z / 0-9 font lives, patterns 192+ and 224+).
    /// </summary>
    private static List<(string Name, string File)> BuildPatterns(byte[]? rom, string outDir, Action<string>? log)
    {
        var result = new List<(string, string)>();
        if (rom is null)
            return result;

        Directory.CreateDirectory(outDir);

        try
        {
            (byte[] vram, byte[] registers) = RomScreens.CaptureGameplay(rom);

            int spriteBase = (registers[6] & 0x07) << 11;
            int patternBase = (registers[4] & 0x3C) << 11;
            int colourBase = (registers[3] & 0x80) * 0x40;

            WriteSheet(Path.Combine(outDir, "sprites8.png"), vram, spriteBase, 256,
                _ => (255, 255, 255));
            result.Add(("sprites8", "patterns/sprites8.png"));

            WriteSheet(Path.Combine(outDir, "tiles.png"), vram, patternBase, 256,
                tile => TileColour(vram, colourBase, tile));
            result.Add(("tiles", "patterns/tiles.png"));

            WriteSprites16(Path.Combine(outDir, "sprites16.png"), vram, spriteBase);
            result.Add(("sprites16", "patterns/sprites16.png"));

            // Player poses: rendered from the sprite layer, one image per pose (RAM E285).
            IReadOnlyList<(int Pose, byte[] Pixels)> poses = RomScreens.CapturePlayerPoses(rom);
            WritePlayerPoses(Path.Combine(outDir, "player_poses.png"), poses);
            if (poses.Count > 0)
                result.Add(("player_poses", "patterns/player_poses.png"));

            log?.Invoke($"report: {result.Count} pattern sheets");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExtractionPreview] patterns: {ex.Message}");
        }

        return result;
    }

    /// <summary>Grid of 8x8 patterns (16 columns; index = row * 16 + column).</summary>
    private static void WriteSheet(string path, byte[] vram, int baseAddress, int count,
        Func<int, (byte R, byte G, byte B)> colourOf)
    {
        const int columns = 16;
        int cell = 8 * SheetScale + 4;
        int rows = (count + columns - 1) / columns;
        int width = columns * cell + 4;
        int height = rows * cell + 4;

        var canvas = new byte[width * height * 3];
        Fill(canvas, 18, 20, 28);

        for (int t = 0; t < count; t++)
        {
            int ox = 2 + t % columns * cell;
            int oy = 2 + t / columns * cell;
            (byte R, byte G, byte B) color = colourOf(t);

            for (int row = 0; row < 8; row++)
            {
                byte bits = vram[(baseAddress + t * 8 + row) & 0x3FFF];
                for (int bit = 0; bit < 8; bit++)
                {
                    if (((bits >> (7 - bit)) & 1) == 0)
                        continue;

                    FillRect(canvas, width, ox + bit * SheetScale, oy + row * SheetScale,
                        SheetScale, SheetScale, color);
                }
            }
        }

        WritePng(path, width, height, canvas);
    }

    /// <summary>16x16 sprites composed the way the VDP does it (p, p+1 / p+2, p+3).</summary>
    private static void WriteSprites16(string path, byte[] vram, int baseAddress)
    {
        var groups = new List<int>();
        for (int baseIndex = 0; baseIndex < 256; baseIndex += 4)
        {
            bool any = false;
            for (int q = 0; q < 4 && !any; q++)
            {
                for (int row = 0; row < 8; row++)
                {
                    if (vram[(baseAddress + (baseIndex + q) * 8 + row) & 0x3FFF] != 0)
                    {
                        any = true;
                        break;
                    }
                }
            }

            if (any)
                groups.Add(baseIndex);
        }

        const int columns = 12;
        int cell = 16 * SheetScale + 6;
        int rows = Math.Max(1, (groups.Count + columns - 1) / columns);
        int width = columns * cell + 4;
        int height = rows * cell + 4;

        var canvas = new byte[width * height * 3];
        Fill(canvas, 18, 20, 28);

        for (int i = 0; i < groups.Count; i++)
        {
            int ox = 2 + i % columns * cell;
            int oy = 2 + i / columns * cell;

            for (int q = 0; q < 4; q++)
            {
                int tile = groups[i] + q;
                for (int row = 0; row < 8; row++)
                {
                    byte bits = vram[(baseAddress + tile * 8 + row) & 0x3FFF];
                    for (int bit = 0; bit < 8; bit++)
                    {
                        if (((bits >> (7 - bit)) & 1) == 0)
                            continue;

                        FillRect(canvas, width,
                            ox + (q % 2) * 8 * SheetScale + bit * SheetScale,
                            oy + (q / 2) * 8 * SheetScale + row * SheetScale,
                            SheetScale, SheetScale, (255, 255, 255));
                    }
                }
            }
        }

        WritePng(path, width, height, canvas);
    }

    /// <summary>
    /// Builds the **MSX1 sprite atlas** (<c>Art/kv2.png</c> + <c>Art/kv2.atlas</c>) from the player poses
    /// captured off the sprite layer, so the game can draw Vick with the cartridge's own art instead of
    /// falling back to the MSX2 atlas.
    ///
    /// <para>Each pose is cropped to 16x24 with the feet on the box's bottom edge (<c>DrawActor</c>
    /// anchors there) and named <c>vick_s0_00</c>…<c>vick_s0_13</c>.</para>
    /// </summary>
    private static void BuildSpriteAtlas(byte[]? rom, string extracted, Action<string>? log)
    {
        if (rom is null)
            return;

        try
        {
            IReadOnlyList<(int Pose, byte[] Pixels)> poses = RomScreens.CapturePlayerPoses(rom);
            if (poses.Count == 0)
                return;

            // The capture box is 32x32 with its origin at (playerX - 8, playerY - 16); the character is
            // 16 wide and 32 tall (matching MSX2 sprite bounds with 8px top padding so DrawPlayer at Y - 8
            // places the feet on the floor).
            const int box = 32;
            const int width = 16;
            const int height = 32;
            const int cropX = 8;
            const int cropY = 0;

            int sheetWidth = poses.Count * width;
            int sheetHeight = height;
            var rgba = new byte[sheetWidth * sheetHeight * 4];

            // The project's atlas format is the simplified one Nez parses here: name, "x,y,w,h", "ox,oy".
            var atlas = new System.Text.StringBuilder();

            for (int i = 0; i < poses.Count; i++)
            {
                byte[] pixels = poses[i].Pixels;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte value = pixels[(cropY + y) * box + cropX + x];
                        int offset = (y * sheetWidth + i * width + x) * 4;

                        if (value == 0)
                            continue; // transparent

                        (byte R, byte G, byte B) color = Tms[(value - 1) & 0x0F];
                        rgba[offset] = color.R;
                        rgba[offset + 1] = color.G;
                        rgba[offset + 2] = color.B;
                        rgba[offset + 3] = 255;
                    }
                }

                string name = $"vick_s0_{poses[i].Pose:00}";
                atlas.Append(name).Append('\n');
                atlas.Append($"{i * width},0,{width},{height}\n");
                atlas.Append("0,0\n");
            }

            string art = Path.Combine(extracted, "Art");
            Directory.CreateDirectory(art);
            WritePngRgba(Path.Combine(art, "kv2.png"), sheetWidth, sheetHeight, rgba);
            File.WriteAllText(Path.Combine(art, "kv2.atlas"), atlas.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            log?.Invoke($"report: atlas with {poses.Count} player poses");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExtractionPreview] atlas: {ex.Message}");
        }
    }

    /// <summary>Sheet with the player's poses, 32x32 each, using the MSX1 fixed palette.</summary>
    private static void WritePlayerPoses(string path, IReadOnlyList<(int Pose, byte[] Pixels)> poses)
    {
        const int box = 32;
        const int columns = 7;
        int cell = box * SheetScale + 6;
        int rows = Math.Max(1, (poses.Count + columns - 1) / columns);
        int width = columns * cell + 4;
        int height = rows * cell + 4;

        var canvas = new byte[width * height * 3];
        Fill(canvas, 18, 20, 28);

        for (int i = 0; i < poses.Count; i++)
        {
            int ox = 2 + i % columns * cell;
            int oy = 2 + i / columns * cell;
            byte[] pixels = poses[i].Pixels;

            for (int y = 0; y < box; y++)
            {
                for (int x = 0; x < box; x++)
                {
                    byte value = pixels[y * box + x];
                    if (value == 0)
                        continue;

                    (byte R, byte G, byte B) color = Tms[(value - 1) & 0x0F];

                    // Colour 1 is black; show it as dark grey so the outline is visible on the sheet.
                    if (((value - 1) & 0x0F) == 1)
                        color = (60, 66, 84);

                    FillRect(canvas, width, ox + x * SheetScale, oy + y * SheetScale,
                        SheetScale, SheetScale, color);
                }
            }
        }

        WritePng(path, width, height, canvas);
    }

    private static (byte R, byte G, byte B) TileColour(byte[] vram, int colourBase, int tile)
    {
        int index = (colourBase + tile / 8 * 8) & 0x3FFF;
        byte colours = vram[index];
        return Tms[(colours >> 4) & 0x0F];
    }

    // ── optional sections ───────────────────────────────────────────────────────────────

    /// <summary>Port MSX2 screens, when the development content is present (never extracted).</summary>
    private static List<(string Name, string File)> BuildCompare(string extracted, string outDir)
    {
        var result = new List<(string, string)>();
        string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Rom", "Art", "Screens");
        if (!Directory.Exists(source))
            return result;

        Directory.CreateDirectory(outDir);
        foreach (string file in Directory.GetFiles(source, "*.bin").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                ScreenData screen = ScreenAssembler.DecodeFile(file);
                WritePng(Path.Combine(outDir, name + ".png"),
                    screen.Width * ScreenScale, screen.Height * ScreenScale,
                    ScaleRgb(screen, ScreenScale));
                result.Add((name, "compare/" + name + ".png"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExtractionPreview] compare {name}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>Music counts for the report (the package itself is played by MSXScope).</summary>
    private static (int Songs, int Clips) BuildMusicSheet(string? packagePath, string preview, Action<string>? log)
    {
        _ = preview;
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            return (0, 0);

        return (0, 0); // counts come from audio.source.json; nothing to render here
    }

    private static string ReadStamp(string extracted)
    {
        string path = Path.Combine(extracted, "audio.source.json");
        return File.Exists(path) ? File.ReadAllText(path) : "{}";
    }

    private static void TryWriteShortcut(string path, string target)
    {
        try
        {
            File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + new Uri(target).AbsoluteUri + "\r\n",
                Encoding.ASCII);
        }
        catch (IOException)
        {
            // a shortcut is a convenience: never blocks the extraction
        }
    }

    private static void TryOpen(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExtractionPreview] could not open the report: {ex.Message}");
        }
    }

    // ── HTML ────────────────────────────────────────────────────────────────────────────

    private static string BuildHtml(
        List<(string Name, string File)> screens,
        List<(string Name, string File)> stages,
        List<(string Name, string File)> patterns,
        List<(string Name, string File)> compare,
        (int Songs, int Clips) music,
        string stampJson)
    {
        string rom = "?", sha1 = "?";
        int songs = music.Songs, clips = music.Clips;

        try
        {
            using JsonDocument stamp = JsonDocument.Parse(stampJson);
            if (stamp.RootElement.TryGetProperty("rom", out JsonElement r)) rom = r.GetString() ?? "?";
            if (stamp.RootElement.TryGetProperty("sha1", out JsonElement s)) sha1 = (s.GetString() ?? "?").Substring(0, 12);
            if (stamp.RootElement.TryGetProperty("songs", out JsonElement sc)) songs = sc.GetInt32();
            if (stamp.RootElement.TryGetProperty("clips", out JsonElement cc)) clips = cc.GetInt32();
        }
        catch (JsonException)
        {
            // an invalid stamp must not stop the report from opening
        }

        var html = new StringBuilder();
        html.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>King's Valley II — content extracted from the ROM</title>
<style>
 :root{color-scheme:dark}
 body{margin:0;background:#0b0c12;color:#e8e8f0;font:15px/1.5 system-ui,Segoe UI,Roboto,sans-serif}
 header{padding:24px 28px;background:linear-gradient(180deg,#151a2b,#0b0c12);border-bottom:1px solid #262b3d}
 h1{margin:0 0 6px;font-size:22px}
 h2{margin:32px 28px 12px;font-size:17px;color:#9fb4ff}
 .meta{color:#98a0b8;font-size:13px}
 .meta code{background:#171b28;padding:1px 6px;border-radius:4px}
 .grid{display:flex;flex-wrap:wrap;gap:14px;padding:0 28px 28px}
 .card{display:block;text-decoration:none;color:inherit;background:#131725;border:1px solid #232941;
       border-radius:10px;padding:8px;transition:transform .12s,border-color .12s}
 .card:hover{transform:translateY(-2px);border-color:#4a5c99}
 .card img{display:block;image-rendering:pixelated;max-width:100%;border-radius:6px}
 .card span{display:block;margin-top:6px;font-size:12px;color:#aab2cc}
 .player{margin:0 28px 28px;padding:18px 20px;background:#131725;border:1px solid #232941;border-radius:12px}
 .player kbd{background:#262b3d;border:1px solid #39405c;border-bottom-width:2px;border-radius:5px;
             padding:1px 6px;font:600 12px/1.4 ui-monospace,Consolas,monospace}
 .player code{background:#171b28;padding:1px 6px;border-radius:4px}
 .player a{color:#9fb4ff}
 footer{padding:18px 28px 40px;color:#5f6880;font-size:12px}
</style></head><body>
<header>
 <h1>King's Valley II — content extracted from the ROM</h1>
""");
        html.Append($"""
 <div class="meta">ROM: <code>{Escape(rom)}</code> · sha1 <code>{Escape(sha1)}</code>
 · {songs} songs · {clips} effects · {stages.Count} stages · {screens.Count} screens</div>
</header>

<h2>Music</h2>
<div class="player">
 <b>{songs} songs and {clips} effects</b> extracted from the ROM into <code>audio.msxaudio</code>.
 To listen, open the game and press <kbd>ESC</kbd> → <b>PLAYER</b>: on first use it downloads the
 player and opens the package. Nothing is bundled with the game.
 <div class="meta" style="margin-top:12px">
   Download the player now:
   <a href="{DownloadUrl}" target="_blank" rel="noopener">MSXScope for this platform</a> ·
   <a href="https://github.com/wagenheimer/MsxAudio/releases" target="_blank" rel="noopener">all releases</a>
 </div>
</div>

<h2>Fixed screens ({screens.Count})</h2>
<div class="grid">
""");
        AppendCards(html, screens);

        if (compare.Count > 0)
        {
            html.Append($"""

<h2>Comparison: the port's MSX2 art ({compare.Count})</h2>
<p class="meta" style="padding:0 28px 8px">This art does <b>not</b> come from the MSX1 ROM — it is the
MSX2 version (from the longplay). SCREEN 2 on the MSX1 allows 2 colours per 8-pixel row per tile;
SCREEN 5 on the MSX2 allows 16.</p>
<div class="grid">
""");
            AppendCards(html, compare);
        }

        if (patterns.Count > 0)
        {
            html.Append($"""

<h2>Pattern sheets ({patterns.Count})</h2>
<p class="meta" style="padding:0 28px 8px">Read from the game's VDP: 256 sprite patterns (8x8), the
16x16 sprites composed the way the chip does it, and the 256 background tiles with their colours —
where the MSX1 font lives (A–Z, 0–9 in patterns 192+ and 224+).</p>
<div class="grid">
""");
            AppendCards(html, patterns);
        }

        html.Append($"""

<h2>Stages ({stages.Count})</h2>
<div class="grid">
""");
        AppendCards(html, stages);

        html.Append("""

<footer>PNGs are generated for reference only; the game reads <code>.bin</code>/JSON and the
<code>.msxaudio</code> package. Click an image to open it full size.</footer></body></html>
""");
        return html.ToString();
    }

    private static void AppendCards(StringBuilder html, List<(string Name, string File)> items)
    {
        foreach ((string name, string file) in items)
        {
            html.Append($"<a class=\"card\" href=\"{file}\" target=\"_blank\">")
                .Append($"<img src=\"{file}\" alt=\"{Escape(name)}\">")
                .Append($"<span>{Escape(name)}</span></a>\n");
        }

        html.Append("</div>\n");
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);

    /// <summary>Direct download of the player for the current platform.</summary>
    private static string DownloadUrl =>
        "https://github.com/wagenheimer/MsxAudio/releases/latest/download/" +
        (MsxScopeInstaller.AssetName ?? "MSXScope-win-x64.zip");

    // ── PNG (minimal writer: 8-bit RGB, no filtering) ───────────────────────────────────

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static void WritePng(string path, int width, int height, byte[] rgb)
    {
        using FileStream file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bits per channel
        header[9] = 2;  // RGB
        WriteChunk(file, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter "none"
                raw.Write(rgb, y * width * 3, width * 3);
            }

            raw.Position = 0;
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                raw.CopyTo(zlib);

            WriteChunk(file, "IDAT", compressed.ToArray());
        }

        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    /// <summary>PNG writer for RGBA (8 bits per channel, no filtering).</summary>
    private static void WritePngRgba(string path, int width, int height, byte[] rgba)
    {
        using FileStream file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bits per channel
        header[9] = 6;  // RGBA
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
        foreach (byte b in typeBytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        crc ^= 0xFFFFFFFF;

        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    // ── drawing (rectangles) ────────────────────────────────────────────────────────────

    private static void Fill(byte[] canvas, byte r, byte g, byte b)
    {
        for (int i = 0; i < canvas.Length; i += 3)
        {
            canvas[i] = r;
            canvas[i + 1] = g;
            canvas[i + 2] = b;
        }
    }

    private static void FillRect(byte[] canvas, int width, int x, int y, int w, int h,
        (byte R, byte G, byte B) color)
    {
        int height = canvas.Length / 3 / width;
        for (int py = Math.Max(0, y); py < Math.Min(height, y + h); py++)
        {
            for (int px = Math.Max(0, x); px < Math.Min(width, x + w); px++)
            {
                int offset = (py * width + px) * 3;
                canvas[offset] = color.R;
                canvas[offset + 1] = color.G;
                canvas[offset + 2] = color.B;
            }
        }
    }

    private static void OutlineRect(byte[] canvas, int width, int height, int x, int y, int w, int h,
        (byte R, byte G, byte B) color)
    {
        FillRect(canvas, width, x, y, w, 1, color);
        FillRect(canvas, width, x, y + h - 1, w, 1, color);
        FillRect(canvas, width, x, y, 1, h, color);
        FillRect(canvas, width, x + w - 1, y, 1, h, color);
    }
}
