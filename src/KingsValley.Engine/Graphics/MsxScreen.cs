#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Textures;
using KingsValley.Engine.Assets;

namespace KingsValley.Engine.Graphics;

/// <summary>
/// Drawing of the "MSX2 screen" in the design space (256x212): sprites from the <c>kv2</c> atlas, the game's own
/// font (<c>font_A..Z</c>, extracted from the ROM) and the HUD digits (<c>digit_0..9</c>).
/// </summary>
public static class MsxScreen
{
    private static readonly Dictionary<string, Sprite?> Cache = new();
    private static Texture2D? _pixel;
    private static Texture2D? _applicationGlyphs;

    /// <summary>Width of a character (VDP).</summary>
    public const int CharSize = 8;

    /// <summary>Advance per microfont character; spaces and unknown glyphs advance too.</summary>
    public const int MiniCharWidth = 4;

    /// <summary>Line step of the 3x5 microfont.</summary>
    public const int MiniLineHeight = 5;

    /// <summary>Atlas sprite by name (null if it does not exist).</summary>
    public static Sprite? Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
            return cached;

        Sprite? sprite = null;
        var atlas = GameAssets.AtlasForSprite(name);
        if (atlas != null && System.Array.IndexOf(atlas.Names, name) >= 0)
            sprite = atlas.GetSprite(name);

        Cache[name] = sprite;
        return sprite;
    }

    /// <summary>Forgets the sprites (when the atlas is reloaded).</summary>
    public static void Reset() => Cache.Clear();

    /// <summary>Draws a sprite with the top-left corner at (x, y).</summary>
    public static void Draw(Batcher batcher, string name, float x, float y, bool flipX = false, Color? color = null) =>
        Draw(batcher, name, x, y, 1f, flipX, color);

    /// <summary>Draws a sprite with the top-left corner at (x, y) and a custom scale.</summary>
    public static void Draw(Batcher batcher, string name, float x, float y, float scale, bool flipX = false, Color? color = null)
    {
        var sprite = Get(name);
        if (sprite == null)
        {
            // Startup/diagnostic UI must remain readable before a cartridge is available.
            // These application glyphs are never exported or accepted as extracted ROM assets.
            char? character = name.StartsWith("font_",StringComparison.Ordinal) && name.Length==6 ? name[5] :
                name.StartsWith("digit_",StringComparison.Ordinal) && name.Length==7 ? name[6] : name=="font_excl" ? '!' : null;
            if(character.HasValue) DrawFallbackChar(batcher,character.Value,x,y,color ?? Color.White,scale);
            return;
        }


        batcher.Draw(sprite.Texture2D, new Vector2(x, y), sprite.SourceRect, color ?? Color.White, 0f, Vector2.Zero,
            new Vector2(scale, scale), flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    /// <summary>Draws a sprite aligned by the base, centered in a 16 px box (characters).</summary>
    public static void DrawActor(Batcher batcher, string name, int x, int y, bool flipX, Color? color = null)
    {
        var sprite = Get(name);
        if (sprite == null)
            return;

        float dx = x + 8 - sprite.SourceRect.Width / 2f;
        float dy = y + 16 - sprite.SourceRect.Height;
        Draw(batcher, name, dx, dy, 1f, flipX, color);
    }

    /// <summary>Measured width of <see cref="MiniText"/>, including spaces and unknown glyphs.</summary>
    public static int MiniTextWidth(string text) => (text?.Length ?? 0) * MiniCharWidth;

    /// <summary>
    /// Wraps text for <see cref="MiniText"/> on word boundaries; words longer than a line are
    /// hard-split. No returned line is wider than <paramref name="maxWidth"/>.
    /// </summary>
    public static IReadOnlyList<string> MiniTextWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth < MiniCharWidth)
            return lines;

        if (text.Contains('\n'))
        {
            foreach (string paragraph in text.Replace("\r", "").Split('\n'))
                lines.AddRange(MiniTextWrap(paragraph, maxWidth));
            return lines;
        }

        int charsPerLine = maxWidth / MiniCharWidth;
        var current = new System.Text.StringBuilder();

        void Flush()
        {
            if (current.Length == 0)
                return;

            lines.Add(current.ToString());
            current.Clear();
        }

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (MiniTextWidth(word) <= maxWidth)
            {
                if (current.Length > 0 && current.Length * MiniCharWidth + MiniCharWidth + MiniTextWidth(word) > maxWidth)
                    Flush();

                if (current.Length > 0)
                    current.Append(' ');
                current.Append(word);
                continue;
            }

            // Overlong word: emit full chunks and keep the tail for the next line.
            Flush();
            int offset = 0;
            while (word.Length - offset > charsPerLine)
            {
                lines.Add(word[offset..(offset + charsPerLine)]);
                offset += charsPerLine;
            }

            if (offset < word.Length)
                current.Append(word, offset, word.Length - offset);
        }

        Flush();
        return lines;
    }

    /// <summary>
    /// Renders compact text in the 3x5 pixel microfont (4 px step per character).
    /// Allows up to 64 readable characters per line across the 256px width without truncation.
    /// </summary>
    public static void MiniText(Batcher batcher, string text, float x, float y, Color? color = null)
    {
        var tint = color ?? Color.White;
        float curX = x;
        foreach (char raw in text.ToUpperInvariant())
        {
            if (raw == ' ')
            {
                curX += MiniCharWidth;
                continue;
            }

            ushort glyph = GetMiniGlyph(raw);
            if (glyph != 0 && raw < 128)
                batcher.Draw(ApplicationGlyphs, new Vector2(curX, y),
                    new Rectangle((raw % 16) * 8, (raw / 16) * 8, 3, 5), tint);
            else if (glyph != 0)
                for (int row=0;row<5;row++)
                for (int col=0;col<3;col++)
                    if ((glyph & (1 << (14-row*3-col))) != 0)
                        Fill(batcher,new Rectangle((int)curX+col,(int)y+row,1,1),tint);
            curX += MiniCharWidth;
        }
    }

    internal static ushort GetMiniGlyph(char ch) => ch switch
    {
        '0' => 0b111_101_101_101_111,
        '1' => 0b010_110_010_010_111,
        '2' => 0b111_001_111_100_111,
        '3' => 0b111_001_111_001_111,
        '4' => 0b101_101_111_001_001,
        '5' => 0b111_100_111_001_111,
        '6' => 0b111_100_111_101_111,
        '7' => 0b111_001_010_010_010,
        '8' => 0b111_101_111_101_111,
        '9' => 0b111_101_111_001_111,

        'A' => 0b010_101_111_101_101,
        'B' => 0b110_101_110_101_110,
        'C' => 0b011_100_100_100_011,
        'D' => 0b110_101_101_101_110,
        'E' => 0b111_100_110_100_111,
        'F' => 0b111_100_110_100_100,
        'G' => 0b011_100_101_101_011,
        'H' => 0b101_101_111_101_101,
        'I' => 0b111_010_010_010_111,
        'J' => 0b001_001_001_101_010,
        'K' => 0b101_110_100_110_101,
        'L' => 0b100_100_100_100_111,
        'M' => 0b101_111_101_101_101,
        'N' => 0b110_101_101_101_101,
        'O' => 0b010_101_101_101_010,
        'P' => 0b110_101_110_100_100,
        'Q' => 0b010_101_101_110_011,
        'R' => 0b110_101_110_101_101,
        'S' => 0b011_100_010_001_110,
        'T' => 0b111_010_010_010_010,
        'U' => 0b101_101_101_101_111,
        'V' => 0b101_101_101_101_010,
        'W' => 0b101_101_101_111_101,
        'X' => 0b101_101_010_101_101,
        'Y' => 0b101_101_010_010_010,
        'Z' => 0b111_001_010_100_111,

        '[' => 0b110_100_100_100_110,
        ']' => 0b011_001_001_001_011,
        '(' => 0b010_100_100_100_010,
        ')' => 0b010_001_001_001_010,
        '<' => 0b001_010_100_010_001,
        '>' => 0b100_010_001_010_100,
        '/' => 0b001_010_010_010_100,
        ':' => 0b000_010_000_010_000,
        '-' => 0b000_000_111_000_000,
        '_' => 0b000_000_000_000_111,
        '+' => 0b000_010_111_010_000,
        '!' => 0b010_010_010_000_010,
        '?' => 0b110_001_010_000_010,
        '.' => 0b000_000_000_000_010,
        ',' => 0b000_000_000_010_100,
        '▲' or '^' => 0b010_111_111_010_010,
        '▼' or 'v' => 0b010_010_111_111_010,
        '◄' => 0b001_011_111_011_001,
        '►' => 0b100_110_111_110_100,
        '|' => 0b010_010_010_010_010,
        '*' => 0b101_010_111_010_101,
        '%' => 0b101_001_010_100_101,
        '\'' => 0b010_010_000_000_000,
        '&' => 0b010_101_010_101_011,
        '"' => 0b101_101_000_000_000,
        '•' => 0b000_000_010_000_000,
        _ => 0,
    };

    /// <summary>5x7 glyph patterns for fallback rendering (A-Z, 0-9).</summary>
    private static readonly byte[][] FallbackGlyphsAlphaNum =
    {
        new byte[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 }, // A
        new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110 }, // B
        new byte[] { 0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111 }, // C
        new byte[] { 0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110 }, // D
        new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111 }, // E
        new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000 }, // F
        new byte[] { 0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111 }, // G
        new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 }, // H
        new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111 }, // I
        new byte[] { 0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100 }, // J
        new byte[] { 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001 }, // K
        new byte[] { 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111 }, // L
        new byte[] { 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001 }, // M
        new byte[] { 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001 }, // N
        new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 }, // O
        new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000 }, // P
        new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101 }, // Q
        new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001 }, // R
        new byte[] { 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110 }, // S
        new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 }, // T
        new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 }, // U
        new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 }, // V
        new byte[] { 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010 }, // W
        new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001 }, // X
        new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100 }, // Y
        new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111 }, // Z
        new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 }, // 0
        new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 }, // 1
        new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 }, // 2
        new byte[] { 0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110 }, // 3
        new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 }, // 4
        new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110 }, // 5
        new byte[] { 0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 }, // 6
        new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 }, // 7
        new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 }, // 8
        new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110 }, // 9
    };

    private static readonly byte[] GlyphExcl = { 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100 };
    private static readonly byte[] GlyphQuest = { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100 };
    private static readonly byte[] GlyphColon = { 0b00000, 0b00100, 0b00000, 0b00000, 0b00100, 0b00000, 0b00000 };
    private static readonly byte[] GlyphSemi = { 0b00000, 0b00100, 0b00000, 0b00000, 0b00100, 0b00100, 0b01000 };
    private static readonly byte[] GlyphDash = { 0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000 };
    private static readonly byte[] GlyphUnder = { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b11111 };
    private static readonly byte[] GlyphPlus = { 0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000 };
    private static readonly byte[] GlyphEqual = { 0b00000, 0b11111, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000 };
    private static readonly byte[] GlyphDot = { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100 };
    private static readonly byte[] GlyphComma = { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100, 0b01000 };
    private static readonly byte[] GlyphSlash = { 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b00000, 0b00000 };
    private static readonly byte[] GlyphBackslash = { 0b10000, 0b01000, 0b00100, 0b00010, 0b00001, 0b00000, 0b00000 };
    private static readonly byte[] GlyphPipe = { 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 };
    private static readonly byte[] GlyphLess = { 0b00010, 0b00100, 0b01000, 0b10000, 0b01000, 0b00100, 0b00010 };
    private static readonly byte[] GlyphGreater = { 0b01000, 0b00100, 0b00010, 0b00001, 0b00010, 0b00100, 0b01000 };
    private static readonly byte[] GlyphOpenParen = { 0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010 };
    private static readonly byte[] GlyphCloseParen = { 0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000 };
    private static readonly byte[] GlyphOpenBracket = { 0b01110, 0b01000, 0b01000, 0b01000, 0b01000, 0b01000, 0b01110 };
    private static readonly byte[] GlyphCloseBracket = { 0b01110, 0b00010, 0b00010, 0b00010, 0b00010, 0b00010, 0b01110 };
    private static readonly byte[] GlyphQuote = { 0b00100, 0b00100, 0b01000, 0b00000, 0b00000, 0b00000, 0b00000 };
    private static readonly byte[] GlyphDblQuote = { 0b01010, 0b01010, 0b10100, 0b00000, 0b00000, 0b00000, 0b00000 };
    private static readonly byte[] GlyphAsterisk = { 0b00000, 0b10101, 0b01110, 0b11111, 0b01110, 0b10101, 0b00000 };
    private static readonly byte[] GlyphPercent = { 0b11001, 0b11010, 0b00100, 0b01000, 0b01011, 0b10011, 0b00000 };
    private static readonly byte[] GlyphAmpersand = { 0b01100, 0b10010, 0b10100, 0b01000, 0b10101, 0b10010, 0b01101 };
    private static readonly byte[] GlyphHash = { 0b01010, 0b11111, 0b01010, 0b01010, 0b11111, 0b01010, 0b00000 };
    private static readonly byte[] GlyphUp = { 0b00100, 0b01110, 0b11111, 0b00100, 0b00100, 0b00000, 0b00000 };
    private static readonly byte[] GlyphDown = { 0b00100, 0b00100, 0b11111, 0b01110, 0b00100, 0b00000, 0b00000 };
    private static readonly byte[] GlyphLeft = { 0b00010, 0b00110, 0b01110, 0b11110, 0b01110, 0b00110, 0b00010 };
    private static readonly byte[] GlyphRight = { 0b01000, 0b01100, 0b01110, 0b01111, 0b01110, 0b01100, 0b01000 };
    private static readonly byte[] GlyphBullet = { 0b00000, 0b00000, 0b01110, 0b01110, 0b01110, 0b00000, 0b00000 };

    internal static byte[]? GetFallbackGlyph(char c) => c switch
    {
        >= 'A' and <= 'Z' => FallbackGlyphsAlphaNum[c - 'A'],
        >= '0' and <= '9' => FallbackGlyphsAlphaNum[26 + (c - '0')],
        '!' => GlyphExcl,
        '?' => GlyphQuest,
        ':' => GlyphColon,
        ';' => GlyphSemi,
        '-' => GlyphDash,
        '_' => GlyphUnder,
        '+' => GlyphPlus,
        '=' => GlyphEqual,
        '.' => GlyphDot,
        ',' => GlyphComma,
        '/' => GlyphSlash,
        '\\' => GlyphBackslash,
        '|' => GlyphPipe,
        '<' => GlyphLess,
        '>' => GlyphGreater,
        '(' => GlyphOpenParen,
        ')' => GlyphCloseParen,
        '[' => GlyphOpenBracket,
        ']' => GlyphCloseBracket,
        '\'' => GlyphQuote,
        '"' => GlyphDblQuote,
        '*' => GlyphAsterisk,
        '%' => GlyphPercent,
        '&' => GlyphAmpersand,
        '#' => GlyphHash,
        '▲' or '^' => GlyphUp,
        '▼' or 'v' => GlyphDown,
        '◄' => GlyphLeft,
        '►' => GlyphRight,
        '•' => GlyphBullet,
        _ => null,
    };

    /// <summary>
    /// Renders an 8x8 character using the built-in fallback font.
    /// Used when the sprite atlas is missing, unextracted or lacks a glyph.
    /// </summary>
    public static void DrawFallbackChar(Batcher batcher, char c, float x, float y, Color tint, float scale = 1f)
    {
        byte[]? pattern = GetFallbackGlyph(c);
        if (pattern == null)
            return;

        if (c >= 128)
        {
            for (int row = 0; row < 7; row++)
            for (int col = 0; col < 5; col++)
                if ((pattern[row] & (1 << (4-col))) != 0)
                    Fill(batcher,new Rectangle((int)(x+(col+1)*scale),(int)(y+row*scale),
                        (int)scale,(int)scale),tint);
            return;
        }
        int index = c;
        batcher.Draw(ApplicationGlyphs, new Vector2(x,y),
            new Rectangle((index % 16) * 8, 64 + (index / 16) * 8, 8, 8),
            tint, 0f, Vector2.Zero, new Vector2(scale), SpriteEffects.None, 0f);
    }

    // Application-only fonts remain available before extraction. One quad per glyph also
    // avoids thousands of pixel draws and buffer flushes on the first-run screen.
    private static Texture2D ApplicationGlyphs
    {
        get
        {
            if (_applicationGlyphs is { IsDisposed: false }) return _applicationGlyphs;
            var pixels = new Color[128 * 128];
            for (int code = 0; code < 128; code++)
            {
                int x = (code % 16) * 8, y = (code / 16) * 8;
                ushort mini = GetMiniGlyph((char)code);
                for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if ((mini & (1 << (14-row*3-col))) != 0)
                        pixels[(y+row)*128+x+col] = Color.White;
                var fallback = GetFallbackGlyph((char)code);
                if (fallback == null) continue;
                for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                    if ((fallback[row] & (1 << (4-col))) != 0)
                        pixels[(64+y+row)*128+x+col+1] = Color.White;
            }
            _applicationGlyphs = new Texture2D(Nez.Core.GraphicsDevice,128,128);
            _applicationGlyphs.SetData(pixels);
            return _applicationGlyphs;
        }
    }

    /// <summary>Text in the game font (A-Z, 0-9 and space). Falls back to procedural glyphs if the atlas is absent.</summary>
    public static void Text(Batcher batcher, string text, float x, float y, Color? color = null)
    {
        var tint = color ?? Color.White;
        foreach (char raw in text.ToUpperInvariant())
        {
            if (raw == ' ')
            {
                x += CharSize;
                continue;
            }

            string? glyph = raw switch
            {
                >= 'A' and <= 'Z' => "font_" + raw,
                >= '0' and <= '9' => "digit_" + raw,
                '!' => "font_excl",
                _ => null,
            };

            Sprite? sprite = glyph != null ? Get(glyph) : null;
            if (sprite != null)
                Draw(batcher, glyph!, x, y, false, tint);
            else
                DrawFallbackChar(batcher, raw, x, y, tint);

            x += CharSize;
        }
    }

    /// <summary>Number with leading zeros (like the HUD: <c>001500</c>).</summary>
    public static void Number(Batcher batcher, int value, int digits, float x, float y)
    {
        var s = System.Math.Max(0, value).ToString().PadLeft(digits, '0');
        if (s.Length > digits)
            s = s[^digits..];
        Text(batcher, s, x, y);
    }

    /// <summary>Solid rectangle.</summary>
    public static void Fill(Batcher batcher, Rectangle rect, Color color)
    {
        _pixel ??= CreatePixel();
        batcher.Draw(_pixel, new Vector2(rect.X, rect.Y), null, color, 0f, Vector2.Zero,
            new Vector2(rect.Width, rect.Height), SpriteEffects.None, 0f);
    }

    private static Texture2D CreatePixel()
    {
        var texture = new Texture2D(Nez.Core.GraphicsDevice, 1, 1);
        texture.SetData(new[] { Color.White });
        return texture;
    }
}
