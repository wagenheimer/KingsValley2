#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using Gum.GueDeriving;
using Gum.Wireframe;
using KernSmith.Gum;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Registers the game's **dynamic fonts** in Gum, through `KernSmith`, and picks the UI family/file
/// by the active culture.
///
/// ## Why dynamic
///
/// Out of the box, Gum uses a **fixed-size** bitmap font with limited coverage
/// (only the ranges declared in the `.gumx`). It blurs when scaled and locks positioning.
/// With `KernSmithFontCreator` the font is a **TTF rasterized on demand** at the requested size:
/// crisp at any size, without declaring ranges and ready for accents/CJK.
///
/// ## Original Unity fonts
///
/// Reused from the Unity project (`Assets/_Game/Art/fonts/base/`), where the UI used
/// **TMP SDF with borders** ("Black Border" materials). Here we use the same TTF and, when the
/// outline is needed, draw the text twice with an offset (see `AGENTS.md`).
///
/// | Constant | File | Original use |
/// | :--- | :--- | :--- |
/// | <see cref="Display"/> | `RaspoutineDemiBold_TB.otf` | titles/banners |
/// | <see cref="Ui"/> | `Berlin Sans FB Demi Bold.ttf` | UI/level texts (Latin) |
/// | <see cref="Comic"/> | `KOMIKAX_.ttf` | comic style |
/// | `UiJa` (private) | `KozGoPro-Bold.ttf` | UI in Japanese (CJK) |
/// | `UiKo` (private) | `NotoSansKR-Regular.ttf` | UI in Korean (CJK) |
/// | `UiZh` (private) | `SourceHanSansSC-Medium.ttf` | UI in simplified/traditional Chinese |
///
/// The UI font for the **active culture** is chosen by <see cref="ForCulture"/> — the Latin fonts
/// have no CJK glyphs, so `ja`/`ko`/`zh` get their own TTF to avoid coming out as "squares".
/// The CJK families exist **only for registration** (and for the missing-font warning): they
/// **must not** be assigned to a text, because the family path does not enable glyph
/// growth. The per-culture value is **always** obtained from <see cref="ForCulture"/>.
///
/// ## ⚠️ CJK uses the **file path**, not the family name
///
/// Gum only registers a "growable" model (`IGrowableFontCreator`) when the font is resolved by
/// **file** (`BmfcSave.FontFile`) — and the family path **only accepts `.ttf`**
/// (`BmfcSave.IsFontFilePath`). Since the atlas is generated with the initial ranges (ASCII) and the CJK
/// glyphs are added **on demand** (<see cref="TextRuntime.UseAutomaticFontGrowth"/>), the
/// CJK fonts are assigned as a relative path (`Fonts/<file>.ttf`). That is why the Unity
/// CJK fonts (`.otf` CFF) were converted to **TrueType** (`tools/otf_to_ttf.py`) — KernSmith's
/// FreeType rasterizes both, but Gum does not recognize `.otf` as a font file.
///
/// ## How to use it on a screen
///
/// Register once at startup (done in `KingsValleyGame.Initialize`) and then assign the return of
/// <see cref="ForCulture"/> to the text's **`FontFamily`** in Gum. The value is a **family name**
/// (Latin: <see cref="Ui"/>) or a **`.ttf` path** (CJK) — and it is that path that enables the
/// automatic glyph growth (<see cref="TextRuntime.UseAutomaticFontGrowth"/>).
///
/// <code>
/// label.FontFamily = GumFonts.ForCulture(Localization.Culture);
/// </code>
///
/// ⚠️ Registration **does not** create the visual elements: the font entries keep being
/// declared in the `.gumx` (through the **Gum Tool**/`gumcli`) — see the "never write .gumx by hand" rule.
/// </summary>
public static class GumFonts
{
    /// <summary>Display family (titles, banners).</summary>
    public const string Display = "Raspoutine DemiBold";

    /// <summary>UI family (running texts, HUD).</summary>
    public const string Ui = "Berlin Sans FB Demi";

    /// <summary>Comic style family.</summary>
    public const string Comic = "Komika Axis";

    /// <summary>Japanese UI family (KozGoPro) — only for registration/log; the resolution uses the `.ttf` path.</summary>
    private const string UiJa = "KozGoPro Bold";

    /// <summary>Korean UI family (NotoSansKR) — only for registration/log; the resolution uses the `.ttf` path.</summary>
    private const string UiKo = "NotoSansKR";

    /// <summary>Chinese UI family (SourceHanSans SC) — only for registration/log; the resolution uses the `.ttf` path.</summary>
    private const string UiZh = "SourceHanSans SC";

    /// <summary>Fonts folder, relative to the `Content` folder (the Gum FileManager root).</summary>
    private const string FontsFolder = "Fonts";

    /// <summary>Japanese font file (path relative to `Content/Gum`).</summary>
    private const string UiJaFile = "Fonts/KozGoPro-Bold.ttf";

    /// <summary>Korean font file (path relative to `Content/Gum`).</summary>
    private const string UiKoFile = "Fonts/NotoSansKR-Regular.ttf";

    /// <summary>Chinese font file (path relative to `Content/Gum`).</summary>
    private const string UiZhFile = "Fonts/SourceHanSansSC-Medium.ttf";

    private static bool _initialized;

    /// <summary>
    /// Creates the dynamic font creator, registers the game's TTFs and installs it in Gum.
    /// Call **after** `GumUI.Initialize`.
    /// </summary>
    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        if (_initialized)
            return;

        _initialized = true;

        var creator = new KernSmithFontCreator(graphicsDevice);

        Register(Display, "RaspoutineDemiBold_TB.otf");
        Register(Ui, "Berlin Sans FB Demi Bold.ttf");
        Register(Comic, "KOMIKAX_.ttf");

        // CJK fonts: without them the text in ja/ko/zh would come out as "squares" (the Latin ones lack the glyphs).
        Register(UiJa, "KozGoPro-Bold.ttf");
        Register(UiKo, "NotoSansKR-Regular.ttf");
        Register(UiZh, "SourceHanSansSC-Medium.ttf");

        // From here on Gum resolves the families registered at runtime (glyphs on demand).
        CustomSetPropertyOnRenderable.InMemoryFontCreator = creator;

        // The font materials (Unity gradients) need the GraphicsDevice to create the atlas.
        GumFontMaterials.SetGraphicsDevice(graphicsDevice);

        // Automatic glyph atlas growth: the atlas is generated with the initial ranges
        // (ASCII) and the missing characters (e.g.: kana/CJK) are added on demand.
        // Without that they fall back to the space glyph and "disappear". KernSmith implements
        // IGrowableFontCreator; 4096 is the safe cap for the in-memory atlas.
        TextRuntime.UseAutomaticFontGrowth = true;
        TextRuntime.MaxInMemoryFontAtlasSize = 4096;

        // Gum's automatic oversampling: with the camera magnified (`Renderer.Camera.Zoom =
        // DesignViewport.Scale` — 2.813 on a 4K monitor) the glyph rasterized at the design size
        // would be STRETCHED and blur. With this on, Gum re-rasterizes the font at the native scale and
        // draws compensating — the same practical effect as Unity's TMP SDF, but native.
        // (Gum 2026.9.2.1 = the 02/09/2026 release, which fixed oversampling on MonoGame.)
        TextRuntime.UseFontOversampling = true;

        Console.WriteLine(
            $"[GumFonts] Dynamic fonts registered: {Display}, {Ui}, {Comic}, {UiJa}, {UiKo}, {UiZh}.");
    }

    /// <summary>
    /// Returns the UI font suited to the culture, ready to assign to a Gum text's `FontFamily`:
    /// Latin uses the **family** <see cref="Ui"/>; Japanese, Korean and Chinese use the **path**
    /// of their own TTF (CJK), because Gum's glyph growth only works for file fonts.
    /// The comparison is by **2-letter prefix**, case-insensitive, so `"ja-JP"` and
    /// `"zh-TW"` are also resolved. Values are `Trim()`ed before the comparison.
    /// </summary>
    /// <param name="culture">Culture code (e.g.: <c>"pt-BR"</c>, <c>"ja"</c>, <c>"zh-CN"</c>).</param>
    /// <returns>Value for Gum's `FontFamily` (family or `.ttf` path).</returns>
    public static string ForCulture(string culture)
    {
        culture = culture?.Trim() ?? string.Empty;

        if (culture.Length == 0)
        {
            return Ui;
        }

        // 2-letter prefix of the code (accepts "ja", "ja-JP", "zh-Hans"...).
        var prefixLength = 0;
        while (prefixLength < culture.Length && culture[prefixLength] != '-' && culture[prefixLength] != '_')
        {
            prefixLength++;
        }

        var prefix = culture.Substring(0, prefixLength);

        if (string.Equals(prefix, "ja", StringComparison.OrdinalIgnoreCase))
        {
            return UiJaFile;
        }

        if (string.Equals(prefix, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return UiKoFile;
        }

        if (string.Equals(prefix, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return UiZhFile;
        }

        return Ui;
    }

    /// <summary>
    /// Relative path (from `Content/Gum`) of the TTF registered for a family. Needed
    /// to build a `BitmapFont` **directly** through KernSmith: `BmFont.Generate` takes a file
    /// path, not a family name (it is `GumFontGenerator` that resolves the registered family).
    /// </summary>
    public static string FileOf(string family) => family switch
    {
        Display => $"{FontsFolder}/RaspoutineDemiBold_TB.otf",
        Ui => $"{FontsFolder}/Berlin Sans FB Demi Bold.ttf",
        Comic => $"{FontsFolder}/KOMIKAX_.ttf",
        _ => $"{FontsFolder}/{family}.ttf",
    };

    /// <summary>
    /// Registers a font file under a family. A missing file produces a **warning**, not an error:
    /// the game keeps the Gum default font instead of not opening.
    /// </summary>
    private static void Register(string family, string fileName)
    {
        var relativePath = $"{FontsFolder}/{fileName}";

        var onDisk = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Gum", FontsFolder, fileName);
        if (!File.Exists(onDisk))
        {
            Console.WriteLine($"[GumFonts] WARNING: font not found ({onDisk}). '{family}' will be unavailable.");
            return;
        }

        // RegisterFont is STATIC: it registers in KernSmith's font directory and the creator
        // (installed in Gum right below) resolves the family at runtime.
        KernSmithFontCreator.RegisterFont(family, relativePath, style: null, faceIndex: 0);
    }
}
