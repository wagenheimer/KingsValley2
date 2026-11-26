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

    public static LayoutRect Header { get; } = new(6,7,244,10);
    public static LayoutRect Notice { get; } = new(6,23,244,16);
    public static LayoutRect Status { get; } = new(6,43,244,29);
    public static LayoutRect RomList { get; } = new(6,76,244,80);
    public static LayoutRect Buttons { get; } = new(6,160,244,36);
    public static LayoutRect Footer { get; } = new(6,198,244,10);
    public static LayoutRect Dialog { get; } = new(12,38,232,140);
    public static LayoutRect DialogBody { get; } = new(24,66,208,72);
    public static LayoutRect DialogPrimary { get; } = new(24,150,98,16);
    public static LayoutRect DialogSecondary { get; } = new(134,150,98,16);

    // ── Buttons ────────────────────────────────────────────────────────────────
    public const int ButtonCount = 6;
    public const int PlayButtonIndex = 4;
    public const int ButtonGap = 3;
    public const int ButtonPaddingX = 4;
    public const int ButtonHeight = 16;
    public const int ButtonBandPaddingY = 1;

    public const int ExtractButtonIndex = 3;
    public static readonly string[] ButtonLabels = { "BROWSE", "FOLDER", "SCAN", "EXTRACT SELECTED", "PLAY", "QUIT" };

    /// <summary>
    /// Six stable rectangles: two primary actions above four utility actions.
    /// </summary>
    public static IReadOnlyList<LayoutRect> GetButtonRects() => new LayoutRect[]
    {
        new(6,183,58,12),new(67,183,57,12),new(127,183,48,12),
        new(6,161,146,16),new(156,161,94,16),new(178,183,72,12),
    };

    // ── Status columns ─────────────────────────────────────────────────────────
    public const int StatusTextStep = 9;
    public const int StatusLabelColumnWidth = 108;
    public const int StatusTagColumnWidth = 120;

    public static LayoutRect StatusLabelColumn { get; } = new(
        Status.X + TextPadding, Status.Y, StatusLabelColumnWidth, Status.Height);

    public static LayoutRect StatusTagColumn { get; } = new(
        StatusLabelColumn.Right + 4, Status.Y, StatusTagColumnWidth, Status.Height);

    public static readonly string[] StatusLabels = { "MSX1 CARTRIDGE", "MSX2 CARTRIDGE", "MSX2 CONTEST" };
    public static readonly string[] StatusTags = { "READY TO PLAY", "READY TO EXTRACT", "ADD A ROM", "AUDIO NOT EXTRACTED" };

    // ── ROM list ───────────────────────────────────────────────────────────────
    public const int VisibleRoms = 3;
    public const int RomHeaderHeight = 11;
    public const int RomItemHeight = 20;
    public const int RomItemGap = 2;

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
    public const string HeaderTitle = "CARTRIDGE SETUP";
    public const string DefaultNotice = "BROWSE FOR YOUR ORIGINAL CARTRIDGE TO BEGIN.";
    public const string NoticeRomReady = "CHOOSE A ROM, THEN SELECT EXTRACT SELECTED.";
    public const string NoticeGameReady = "READY TO PLAY. EXTRACT ANOTHER ROM OR SELECT PLAY.";
    public const string ExtractionTitle = "EXTRACTING DATA FROM YOUR CARTRIDGE...";
    public const string ExtractionCancelHint = "EXTRACTION RUNS ONCE - PRESS ESC TO CANCEL";

    public static readonly string[] FooterLines =
    {
        "ARROWS SELECT  ENTER OK  H ROM GUIDE  ESC BACK",
    };

    public static readonly string[] EmptyStateLines =
    {
        "NO CARTRIDGES FOUND.",
        "USE BROWSE, OR PLACE YOUR ROM BESIDE THE GAME",
        "OR IN ITS ROMS SUBFOLDER, THEN SELECT SCAN.",
        "MSX1 RC-760, MSX2 SCC AND EDIT CONTEST ARE SUPPORTED.",
        "YOUR ORIGINAL ROM FILES ARE NEVER CHANGED.",
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
