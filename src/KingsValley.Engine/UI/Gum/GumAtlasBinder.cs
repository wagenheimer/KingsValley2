#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Gum.GueDeriving;
using Gum.Wireframe;
using Nez.Sprites;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Binds the art **by name** into atlas sprites of the project's screens/components, walking the Gum
/// tree after materializing it. It is the "keeper" of the <c>AGENTS.md</c> rule:
/// the runtime resolves the art by **name** in the Nez atlas — repacking the atlas does not break the UI.
///
/// ## Source of truth: <c>Content/Gum/atlas-bindings.json</c>
///
/// The <c>atlas -&gt; (element name -&gt; sprite)</c> map is read from <c>atlasBindings</c> in the JSON.
/// The same file feeds <c>tools/sync_gum_preview_coords.ps1</c>, which writes the
/// **preview** coordinates into the XML (so the **Gum Tool / visual editor** shows the art). Thus:
/// <list type="bullet">
/// <item><b>Runtime</b> — binds by name (this binder); it does not depend on the coordinates.</item>
/// <item><b>Visual editor</b> — uses the preview coordinates, generated from the atlas (never by hand).</item>
/// <item><b>Guardian</b> — <c>tools/audit_gum_atlas_coords.ps1</c> reports drift after a repack.</item>
/// </list>
///
/// ## What does NOT go into the map (context-dependent art)
///
/// Sprites whose art changes per **instance/state** stay in the JSON's <c>fileBindings</c> (preview) and
/// are bound in C# (runtime): <c>Icon</c>/<c>Badge</c>/<c>Token</c> and the buttons per category
/// (<c>GameIconButton</c>/<c>GameMenuButton</c>).
/// </summary>
internal static class GumAtlasBinder
{
    private static Dictionary<string, Dictionary<string, string>>? _map;

    /// <summary>
    /// Map <c>atlas -&gt; (element name -&gt; sprite)</c>, read from
    /// <c>Content/Gum/atlas-bindings.json</c> (<c>atlasBindings</c>). Empty if the file is missing (the
    /// runtime falls back to the preview coordinates already in the XML).
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> Map => _map ??= LoadMap();

    /// <summary>Binds the art of every sprite in the map starting from <paramref name="root"/> (inclusive).</summary>
    public static void Bind(GraphicalUiElement? root)
    {
        if (root == null)
            return;

        BindRecursive(root);
    }

    private static void BindRecursive(GraphicalUiElement element)
    {
        if (element is SpriteRuntime sprite)
            BindSprite(sprite);

        foreach (var child in element.Children)
            BindRecursive(child);
    }

    private static void BindSprite(SpriteRuntime sprite)
    {
        var atlasKey = AtlasKeyOf(sprite);
        if (atlasKey == null
            || !Map.TryGetValue(atlasKey, out var byElement)
            || !byElement.TryGetValue(sprite.Name ?? string.Empty, out var spriteName))
        {
            return;
        }

        GumUiFactory.ApplySprite(sprite, AtlasOf(atlasKey), spriteName);
    }

    /// <summary>
    /// Finds the atlas by the **texture already loaded** in the sprite: Gum resolves the `SourceFile`
    /// (`dialogs.png` etc.) to a texture with the same dimensions as the atlas PNG. Since each
    /// project atlas has distinct dimensions, comparing `Width/Height` identifies the atlas without
    /// depending on reading the path (Gum's `SourceFileName` is write-only).
    /// </summary>
    private static string? AtlasKeyOf(SpriteRuntime sprite)
    {
        var texture = sprite.Texture;
        if (texture == null)
            return null;

        foreach (var key in Map.Keys)
        {
            var atlas = AtlasOf(key);
            if (atlas is { Sprites.Length: > 0 }
                && atlas.Sprites[0].Texture2D.Width == texture.Width
                && atlas.Sprites[0].Texture2D.Height == texture.Height)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Atlases available for binding by name (the key comes from `atlas-bindings.json`).</summary>
    private static SpriteAtlas? AtlasOf(string key) => key switch
    {
        "ui" => Assets.GameAssets.Ui,
        "sprites" => Assets.GameAssets.Sprites,
        "tiles" => Assets.GameAssets.Tiles,
        _ => Assets.GameAssets.Ui,
    };

    private static Dictionary<string, Dictionary<string, string>> LoadMap()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var path = Path.Combine(AppContext.BaseDirectory, "Content", "Gum", "atlas-bindings.json");
        if (!File.Exists(path))
            path = Path.Combine("Content", "Gum", "atlas-bindings.json");

        if (!File.Exists(path))
        {
            Console.WriteLine("[GumAtlasBinder] atlas-bindings.json not found — the art stays at the preview coordinates.");
            return result;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<BindingsFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (file?.AtlasBindings != null)
            {
                foreach (var (atlas, elements) in file.AtlasBindings)
                    result[atlas] = new Dictionary<string, string>(elements, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GumAtlasBinder] failed to read atlas-bindings.json: {ex.Message}");
        }

        return result;
    }

    private sealed class BindingsFile
    {
        public Dictionary<string, Dictionary<string, string>>? AtlasBindings { get; set; }
    }
}
