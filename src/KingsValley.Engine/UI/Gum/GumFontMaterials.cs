#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Gum;
using Gum.GueDeriving;
using Gum.Wireframe;
using KernSmith;
using KernSmith.Gum;
using KernSmith.Output;
using RenderingLibrary.Graphics.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary.Graphics;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// The game's **font materials** — reproduced from the Unity project
/// (<c>Assets/_Game/Art/fonts/*.asset</c>), where each material is a vertical gradient (top → bottom)
/// applied to the glyph by TextMeshPro's SDF shader.
///
/// <para>
/// 📖 Why this exists: <see cref="Gum.GueDeriving.TextRuntime.OutlineThickness"/> is only the
/// **thickness** and the property-path outline is **always black**
/// ([doc](https://docs.flatredball.com/gum/code/files-and-fonts/fonts/advanced-font-effects)).
/// **Gradient** and **outline color** do not exist in the property surface — only by constructing
/// a <see cref="BitmapFont"/> through KernSmith and assigning it to
/// <see cref="TextRuntime.BitmapFont"/>.
/// </para>
///
/// <para>
/// <b>Soft outline ("blurred", almost a shadow).</b> The original used TMP's "Black Border" material,
/// which in practice is a soft, low-opacity outline. KernSmith has no outline
/// opacity, but it does have a **shadow baked into the atlas** with <c>ShadowOpacity</c>/<c>ShadowBlur</c> — which is
/// exactly that effect. We use a shadow (outline = 0).
/// </para>
///
/// <para>
/// ⚠️ After assigning the <see cref="BitmapFont"/> directly, **do not** touch that text's `Font`,
/// `FontFamily`, `FontSize` or `UseCustomFont` again: Gum's font system regenerates and
/// overwrites the atlas. `FontSize` freezes at the moment the font was generated — that is why
/// the font is generated per (material, size) and cached.
/// </para>
/// </summary>
public static class GumFontMaterials
{
    /// <summary>The Unity materials (the `.asset` name = the original file name).</summary>
    public enum Kind
    {
        /// <summary>
        /// `#FFD800 → #FF8E00` — the **real** gradient of the dialog titles/descriptions (the `.asset`
        /// stores white, but each text overrides it with gold→amber).
        /// </summary>
        Orange,

        /// <summary>`#90FF00 → #6B8F3F` — banner "Order N!" do level start.</summary>
        WhiteGreen,

        /// <summary>`#FFB900 → #F76A07` — o "?" do book e o banner "Water" do level start.</summary>
        Blue,

        /// <summary>`#2E690B` (solid) — "More Games"; the menu buttons use a flat color.</summary>
        Green,

        /// <summary>No gradient (white/white).</summary>
        Plain,
    }

    /// <summary>Gradient (top, bottom) — values read from the Unity scenes, not only from the `.asset` files.</summary>
    private static readonly Dictionary<Kind, (Color Top, Color Bottom)> Gradients = new()
    {
        [Kind.Orange] = (Rgb(1.000f, 0.847f, 0.000f), Rgb(1.000f, 0.557f, 0.000f)),
        [Kind.WhiteGreen] = (Rgb(0.565f, 1.000f, 0.000f), Rgb(0.420f, 0.561f, 0.247f)),
        [Kind.Blue] = (Rgb(1.000f, 0.725f, 0.000f), Rgb(0.969f, 0.416f, 0.027f)),
        [Kind.Green] = (Rgb(0.180f, 0.412f, 0.043f), Rgb(0.180f, 0.412f, 0.043f)),
        [Kind.Plain] = (Color.White, Color.White),
    };

    /// <summary>Opacity of the baked shadow — the original's "blurred outline".</summary>
    private const float ShadowOpacity = 0.55f;

    /// <summary>Blur radius of the shadow (0 = hard edge).</summary>
    private const float ShadowBlur = 3f;

    private static readonly Dictionary<(Kind Kind, int Size, string Family), BitmapFont> Cache = new();

    /// <summary>Texts with a material, to reapply when the screen zoom changes.</summary>
    private static readonly List<(TextRuntime Text, Kind Kind, string Family, int DesignSize)> Applied = new();

    private static GraphicsDevice? _graphicsDevice;

    private static Color Rgb(float r, float g, float b) => new((int)(r * 255), (int)(g * 255), (int)(b * 255));

    /// <summary>
    /// Current Gum camera zoom (= <c>DesignViewport.Scale</c>). The material's atlas is generated
    /// **already multiplied** by it: since oversampling only regenerates when the requested size changes
    /// by 1px+, an atlas already at the zoom size prevents the regeneration — and the material survives.
    /// </summary>
    private static float CurrentZoom
    {
        get
        {
            var zoom = GumService.Default?.Renderer?.Camera?.Zoom ?? 1f;
            return zoom > 0.01f ? zoom : 1f;
        }
    }

    /// <summary>
    /// Keeps the <see cref="GraphicsDevice"/> used to create the atlas textures. Called by
    /// <see cref="GumFonts.Initialize"/>.
    /// </summary>
    public static void SetGraphicsDevice(GraphicsDevice graphicsDevice) => _graphicsDevice = graphicsDevice;

    /// <summary>
    /// Applies a material to a text: generates (once per material+size+font) the atlas with the gradient
    /// and soft shadow and assigns it. The text color itself no longer matters — the atlas paints it.
    /// </summary>
    /// <returns><c>false</c> if it could not be generated (the text keeps the Gum font).</returns>
    public static bool Apply(TextRuntime text, Kind kind, string family, int fontSize, bool bold = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var graphicsDevice = _graphicsDevice;
        if (graphicsDevice == null)
            return false;

        // ⚠️ The atlas is ALWAYS generated at the design size (never multiplied by the zoom):
        // a larger atlas makes TextRuntime draw the glyphs duplicated/overlapping.
        // Accepted consequence: at resolutions with zoom != 1 the oversampling regenerates the regular font
        // and the material does not show (the text stays crisp and legible, just without the gradient).
        var font = GetOrCreate(graphicsDevice, kind, family, fontSize, bold);
        if (font == null)
            return false;

        // `Typeface` is the current API (`BitmapFont` is the deprecated alias — see the "Migrating to
        // 2026 July" doc); in the MonoGame backend it receives the `BitmapFont`.
        text.Typeface = font;

        // Keeps it to reapply when the window changes size (the zoom changes).
        Applied.RemoveAll(a => ReferenceEquals(a.Text, text));
        Applied.Add((text, kind, family, fontSize));

        return true;
    }

    /// <summary>
    /// Reapplies all materials (the atlas is regenerated at the new zoom). Call on resize.
    /// </summary>
    public static void ReapplyAll()
    {
        // Copy it: Apply changes the list.
        foreach (var (text, kind, family, size) in Applied.ToArray())
            Apply(text, kind, family, size);
    }

    /// <summary>
    /// Applies a material to the text of a screen instance (the path by name, like the rest of the code):
    /// `GumFontMaterials.ApplyTo(layer, "Title", Kind.Orange, 48)`.
    /// </summary>
    public static bool ApplyTo(GraphicalUiElement? root, string instanceName, Kind kind, int fontSize, bool bold = false)
    {
        if (root?.GetGraphicalUiElementByName(instanceName) is not TextRuntime label)
            return false;

        return Apply(label, kind, GumFonts.Ui, fontSize, bold);
    }

    /// <summary>Returns (and caches) the material's font at the requested size.</summary>
    public static BitmapFont? GetOrCreate(GraphicsDevice graphicsDevice, Kind kind, string family, int fontSize, bool bold = false)
    {
        var key = (kind, fontSize, family + (bold ? "|bold" : ""));
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var (top, bottom) = Gradients[kind];

            var save = new BmfcSave
            {
                FontName = family,
                FontSize = fontSize,
                IsBold = bold,
            };

            var options = GumFontGenerator.BuildOptions(save);

            // Vertical gradient (top -> bottom), like the Unity material.
            options.GradientStartR = top.R;
            options.GradientStartG = top.G;
            options.GradientStartB = top.B;
            options.GradientEndR = bottom.R;
            options.GradientEndG = bottom.G;
            options.GradientEndB = bottom.B;
            options.GradientAngle = 90f;
            options.GradientMidpoint = 0.5f;

            // Soft "outline": black shadow of low opacity, baked into the atlas.
            options.Outline = 0;
            options.ShadowR = 0;
            options.ShadowG = 0;
            options.ShadowB = 0;
            options.ShadowOffsetX = 0;
            options.ShadowOffsetY = 2;
            options.ShadowOpacity = ShadowOpacity;
            options.ShadowBlur = (int)ShadowBlur;
            options.HardShadow = false;

            // ⚠️ The channel layout is the critical point: `BuildOptions` chooses by the BmfcSave's
            // `OutlineThickness` (0 here = "plain text": alpha=glyph, RGB=1white) and with that the
            // compositor discards the gradient AND the shadow color. The doc prescribes leaving
            // `Channels` **unset** on a font with a shadow, so KernSmith keeps full RGBA:
            // https://docs.flatredball.com/gum/code/files-and-fonts/fonts/advanced-font-effects
            options.Channels = null;

            // The shadow needs room in the cell, otherwise the blur is cut at the edge.
            options.Padding = new KernSmith.Padding(4, 4, 4, 4);

            // ⚠️ Do NOT use `GumFontGenerator.Generate(save)`: it rebuilds the options from the
            // save and discards everything adjusted above (gradient/shadow). The effects path
            // is `BmFont.Generate` receiving the already modified `options` (see Flow 1 of the doc).
            // `BmFont.Generate` takes the TTF PATH (not the family): it is `GumFontGenerator`
            // that resolves family -> file. Without it: "Could not find file '...\Berlin Sans FB Demi'".
            var fontFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Gum", GumFonts.FileOf(family));
            var result = BmFont.Generate(fontFile, options);
            var font = CreateBitmapFont(result, graphicsDevice);

            Cache[key] = font;
            return font;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GumFontMaterials] failed to generate '{kind}' {fontSize}px: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Wraps the KernSmith result in a <see cref="BitmapFont"/> — one <see cref="Texture2D"/>
    /// per atlas page, then the <c>BitmapFont(Texture2D[], string)</c> constructor.
    /// It is the same step that <c>KernSmithFontCreator</c> uses internally.
    /// </summary>
    private static BitmapFont CreateBitmapFont(BmFontResult result, GraphicsDevice graphicsDevice)
    {
        var textures = new Texture2D[result.Pages.Count];
        for (var i = 0; i < result.Pages.Count; i++)
        {
            var page = result.Pages[i];
            var texture = new Texture2D(
                graphicsDevice, page.Width, page.Height, false, SurfaceFormat.Color);
            texture.SetData(page.PixelData);
            textures[i] = texture;
        }

        return new BitmapFont(textures, result.FntText);
    }
}
