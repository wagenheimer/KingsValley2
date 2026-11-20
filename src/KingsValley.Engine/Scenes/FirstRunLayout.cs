#nullable enable
using System;
using System.Collections.Generic;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Pure geometry and text-fitting rules for the first-run ROM setup screen.
/// No MonoGame/Nez types are used here so the layout can be unit tested directly.
/// The microfont constants must stay in sync with <see cref="Graphics.MsxScreen.MiniTextWidth"/>.
/// </summary>
public static class FirstRunLayout
{
    public const int DesignWidth = 256;
    public const int DesignHeight = 212;

    /// <summary>Advance per character of the 3x5 microfont (including spaces and unknown glyphs).</summary>
    public const int MicroCharWidth = 4;

    /// <summary>Line step of the 3x5 microfont.</summary>
    public const int MicroLineHeight = 5;

    /// <summary>Minimum clear space on each side of a text band.</summary>
    public const int TextPadding = 6;

    public const int PanelWidth = 252;
    public const int PanelHeight = 208;

    public static LayoutRect Panel { get; } = new(
        (DesignWidth - PanelWidth) / 2,
        (DesignHeight - PanelHeight) / 2,
        PanelWidth,
        PanelHeight);

    private const int BandInset = 4;
    private static int BandWidth => Panel.Width - 2 * BandInset;

    // Vertical bands. Absolute values are derived from the centered panel:
    // header 5..14, notice 16..27, status 30..54, buttons 59..72,
    // ROM list 76..192, footer 196..206 (panel ends at 210).
    public static LayoutRect Header { get; } = new(Panel.X + BandInset, Panel.Y + 3, BandWidth, 9);
    public static LayoutRect Notice { get; } = new(Panel.X + BandInset, Panel.Y + 14, BandWidth, 11);
    public static LayoutRect Status { get; } = new(Panel.X + BandInset, Panel.Y + 28, BandWidth, 24);
    public static LayoutRect Buttons { get; } = new(Panel.X + BandInset, Panel.Y + 57, BandWidth, 13);
    public static LayoutRect RomList { get; } = new(Panel.X + BandInset, Panel.Y + 74, BandWidth, 116);
    public static LayoutRect Footer { get; } = new(Panel.X + BandInset, Panel.Y + 194, BandWidth, 10);

    // ── Buttons ────────────────────────────────────────────────────────────────
    public const int ButtonCount = 6;
    public const int PlayButtonIndex = 4;
    public const int ButtonGap = 3;
    public const int ButtonPaddingX = 4;
    public const int ButtonHeight = 11;
    public const int ButtonBandPaddingY = 1;

    public static readonly string[] ButtonLabels = { "SELECT", "FOLDER", "REFRESH", "RESET", "PLAY", "QUIT" };

    /// <summary>
    /// Five stable rectangles, independent of game state. Extra horizontal space is shared
    /// equally so the row spans the whole button band without overlapping.
    /// </summary>
    public static IReadOnlyList<LayoutRect> GetButtonRects()
    {
        var widths = new int[ButtonCount];
        int used = 0;
        for (int i = 0; i < ButtonCount; i++)
        {
            widths[i] = MeasureWidth(ButtonLabels[i]) + 2 * ButtonPaddingX;
            used += widths[i];
        }

        used += (ButtonCount - 1) * ButtonGap;
        int extra = Math.Max(0, Buttons.Width - used);
        int perButton = extra / ButtonCount;
        int remainder = extra % ButtonCount;

        var rects = new List<LayoutRect>(ButtonCount);
        int x = Buttons.X;
        int y = Buttons.Y + ButtonBandPaddingY;
        for (int i = 0; i < ButtonCount; i++)
        {
            int width = widths[i] + perButton + (i < remainder ? 1 : 0);
            rects.Add(new LayoutRect(x, y, width, ButtonHeight));
            x += width + ButtonGap;
        }

        return rects;
    }

    // ── Status columns ─────────────────────────────────────────────────────────
    public const int StatusTextStep = 7;
    public const int StatusLabelColumnWidth = 108;
    public const int StatusTagColumnWidth = 120;

    public static LayoutRect StatusLabelColumn { get; } = new(
        Status.X + TextPadding, Status.Y, StatusLabelColumnWidth, Status.Height);

    public static LayoutRect StatusTagColumn { get; } = new(
        StatusLabelColumn.Right + 4, Status.Y, StatusTagColumnWidth, Status.Height);

    public static readonly string[] StatusLabels =
    {
        "MSX1 GRAPHICS (RC-760):",
        "MSX2 GRAPHICS (RC-761):",
        "AUDIO & 60 STAGES:",
    };

    // Longest expected values, used by layout tests to guard the two columns.
    public static readonly string[] StatusTags =
    {
        "[ INCLUDED / READY ]",
        "[ READY IN FOLDER ]",
        "[ NOT EXTRACTED ]",
        "[ READY TO EXTRACT ]",
        "[ AUDIO + 60 STAGES OK ]",
        "[ NEED MSX1 RC-760 ]",
    };

    // ── ROM list ───────────────────────────────────────────────────────────────
    public const int VisibleRoms = 4;
    public const int RomHeaderHeight = 11;
    public const int RomItemHeight = 23;
    public const int RomItemGap = 3;

    /// <summary>Rectangles for up to <see cref="VisibleRoms"/> ROM rows inside the list band.</summary>
    public static IReadOnlyList<LayoutRect> GetRomItemRects(int count)
    {
        int total = Math.Max(0, Math.Min(count, VisibleRoms));
        var rects = new List<LayoutRect>(total);
        int y = RomList.Y + RomHeaderHeight;
        for (int i = 0; i < total; i++)
        {
            rects.Add(new LayoutRect(RomList.X, y, RomList.Width, RomItemHeight));
            y += RomItemHeight + RomItemGap;
        }

        return rects;
    }

    // ── Text content (kept here so widths can be verified in tests) ────────────
    public const string HeaderTitle = "KING'S VALLEY II - ROM SETUP";
    public const string DefaultNotice = "STEP 1: PLACE YOUR ORIGINAL ROMS IN THE FOLDER";
    public const string ExtractionTitle = "EXTRACTING DATA FROM YOUR CARTRIDGE...";
    public const string ExtractionCancelHint = "EXTRACTION RUNS ONCE - PRESS ESC TO CANCEL";

    public static readonly string[] FooterLines =
    {
        "S SELECT  O FOLDER  R SCAN  DEL RESET  P PLAY",
        "ARROWS MOVE  ENTER OK  MOUSE CLICK  ESC BACK",
    };

    public static readonly string[] EmptyStateLines =
    {
        "NO ROM FILES FOUND IN THE ROMS FOLDER.",
        "TO PLAY YOU NEED YOUR OWN KING'S VALLEY II CARTRIDGE:",
        "1. PRESS S TO PICK YOUR ROM.",
        "2. OR PRESS O AND COPY IT INTO THE ROMS FOLDER.",
        "   - MSX1 RC-760 (128 KB): 60 STAGES, AUDIO AND MSX1 ART",
        "   - MSX2 RC-761 (128 KB): SCC AUDIO AND MSX2 ART",
        "3. PRESS R TO RESCAN, THEN ENTER ON A ROM TO EXTRACT.",
    };

    public static readonly string[] ExtractionBullets =
    {
        "* EXTRACTING PSG & SCC AUDIO DRIVER TRACKS",
        "* EXTRACTING 60 STAGE LAYOUTS, ITEMS & DOORS",
        "* EXTRACTING FIXED SCREENS & PALETTES",
    };

    // ── Text fitting helpers ───────────────────────────────────────────────────

    /// <summary>Measured width of a microfont string. Matches MsxScreen.MiniTextWidth.</summary>
    public static int MeasureWidth(string? text) => (text?.Length ?? 0) * MicroCharWidth;

    /// <summary>Maximum text width that can be placed in a band with the required side padding.</summary>
    public static int TextWidth(LayoutRect band) => Math.Max(0, band.Width - 2 * TextPadding);

    public static int LeftX(LayoutRect band, int textWidth, int padding = TextPadding) =>
        ClampX(band.X + padding, band, textWidth, padding);

    public static int CenterX(LayoutRect band, int textWidth, int padding = TextPadding) =>
        ClampX(band.X + (band.Width - textWidth) / 2, band, textWidth, padding);

    public static int RightX(LayoutRect band, int textWidth, int padding = TextPadding) =>
        ClampX(band.Right - padding - textWidth, band, textWidth, padding);

    /// <summary>Truncates with an ASCII ellipsis so every glyph is part of the microfont.</summary>
    public static string FitLine(string text, int maxWidth)
    {
        if (MeasureWidth(text) <= maxWidth)
            return text;

        int maxChars = Math.Max(0, maxWidth / MicroCharWidth);
        if (maxChars <= 3)
            return new string('.', maxChars);

        return text[..(maxChars - 3)] + "...";
    }

    private static int ClampX(int preferred, LayoutRect band, int textWidth, int padding)
    {
        if (band.Width - textWidth < 2 * padding)
            padding = Math.Max(0, (band.Width - textWidth) / 2);

        int min = band.X + padding;
        int max = Math.Max(min, band.Right - padding - textWidth);
        return Math.Clamp(preferred, min, max);
    }
}

/// <summary>Minimal rectangle value used by the pure layout code.</summary>
public readonly record struct LayoutRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public bool Intersects(LayoutRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public LayoutRect Inset(int amount) => new(
        X + amount, Y + amount, Math.Max(0, Width - 2 * amount), Math.Max(0, Height - 2 * amount));
}
