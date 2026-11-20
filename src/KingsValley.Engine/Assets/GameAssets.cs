#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez.Sprites;
using Nez.Textures;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Assets;

/// <summary>
/// **Global** cache of the game's assets (atlases) — loaded once and released on shutdown
/// (<see cref="Unload"/>). Assets specific to a scene stay in <c>SceneBase.Own(...)</c>.
///
/// > ⚠️ All sprite art comes from an **atlas** (never a loose PNG at runtime) — see `AGENTS.md`.
/// The game atlas (<c>kv2</c>) is the MSX2 one extracted by <c>tools/kv2-extract</c> (see <see cref="RomContent"/>).
/// </summary>
public static class GameAssets
{
    private static SpriteAtlas? _kv2;
    private static SpriteAtlas? _sharedKv2;
    private static SpriteAtlas? _ui;
    private static bool _needsReload;

    /// <summary>MSX2 atlas of the game: tiles, objects, characters and HUD (premultiplied, scene drawing).</summary>
    public static SpriteAtlas? Kv2
    {
        get
        {
            ReloadIfNeeded();
            return _kv2 ??= LoadKv2();
        }
    }

    /// <summary>Master shared MSX2 atlas (fallback for any sprites missing in variant atlases).</summary>
    public static SpriteAtlas? SharedKv2
    {
        get
        {
            ReloadIfNeeded();
            return _sharedKv2 ??= LoadAbsolute(System.IO.Path.Combine(RomContent.SharedRoot, "Art", "kv2.atlas"), premultiplyAlpha: true);
        }
    }

    /// <summary>
    /// Loads the variant's atlas and, if it does not have its own (MSX1 today), uses the shared one.
    /// (Avoids the "atlas not found" warning when the variant simply has no atlas.)
    /// </summary>
    private static SpriteAtlas? LoadKv2()
    {
        var path = RomContent.AtlasPath;
        if (!System.IO.File.Exists(path))
            path = System.IO.Path.Combine(RomContent.SharedRoot, "Art", "kv2.atlas");
        return LoadAbsolute(path, premultiplyAlpha: true);
    }

    /// <summary>Atlas of the room tiles (= <see cref="Kv2"/>).</summary>
    public static SpriteAtlas? Tiles => Kv2;

    /// <summary>Atlas of the game sprites (= <see cref="Kv2"/>).</summary>
    public static SpriteAtlas? Sprites => Kv2;

    /// <summary>**Gum UI** atlas (buttons, frames) — not premultiplied (Gum's blend).</summary>
    public static SpriteAtlas? Ui => _ui ??= LoadAbsolute(ArtResolution.ResolveAtlasPath("Atlases/ui.atlas"), premultiplyAlpha: false);

    private static SpriteAtlas? LoadAbsolute(string fullPath, bool premultiplyAlpha)
    {
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"[GameAssets] atlas not found: {fullPath}");
            return null;
        }

        string texturePath = Path.ChangeExtension(fullPath, ".png");
        if (!File.Exists(texturePath))
        {
            Console.WriteLine($"[GameAssets] atlas texture not found: {texturePath}");
            return null;
        }

        // SpriteAtlasLoader.ParseSpriteAtlas uses TitleContainer.OpenStream for the PNG and therefore only
        // accepts paths relative to the content root. Extracted atlases live in AppData with an absolute path,
        // so load the texture and parse the atlas data directly here (no copy into the game folder).
        Texture2D texture;
        try
        {
            using FileStream stream = File.OpenRead(texturePath);
            texture = premultiplyAlpha
                ? TextureUtils.TextureFromStreamPreMultiplied(stream)
                : Texture2D.FromStream(Nez.Core.GraphicsDevice, stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameAssets] cannot load atlas texture '{texturePath}': {ex.Message}");
            return null;
        }

        IReadOnlyList<AtlasSpriteEntry> entries = AtlasFile.Read(fullPath);
        if (entries.Count == 0)
        {
            texture.Dispose();
            Console.WriteLine($"[GameAssets] atlas has no sprites: {fullPath}");
            return null;
        }

        var atlas = new SpriteAtlas
        {
            Names = new string[entries.Count],
            Sprites = new Sprite[entries.Count],
            AnimationNames = Array.Empty<string>(),
            SpriteAnimations = Array.Empty<SpriteAnimation>(),
        };

        for (int i = 0; i < entries.Count; i++)
        {
            AtlasSpriteEntry entry = entries[i];
            atlas.Names[i] = entry.Name;
            atlas.Sprites[i] = new Sprite(
                texture,
                new Rectangle(entry.X, entry.Y, entry.Width, entry.Height),
                new Vector2(entry.OriginX * entry.Width, entry.OriginY * entry.Height));
        }

        return atlas;
    }

    /// <summary>Preload steps for the loading screen (sliced by time).</summary>
    public static IReadOnlyList<LoadStep> CreatePreloadSteps() => new[]
    {
        new LoadStep("Carregando arte MSX2...", () => { _ = Kv2; }),
        new LoadStep("Carregando audio...", GameAudio.Initialize),
    };

    /// <summary>
    /// Marks the atlases as stale from a background thread (after an extraction). The actual dispose and
    /// reload happen on the next main-thread access, because MonoGame's graphics device is not thread-safe.
    /// </summary>
    public static void Invalidate()
    {
        // Called from the extraction worker: only flip the flag here. The dictionary clear and texture
        // work happen on the main thread in Reload(), because neither is safe to touch while drawing.
        _needsReload = true;
    }

    private static void ReloadIfNeeded()
    {
        if (_needsReload)
            Reload();
    }

    /// <summary>
    /// Re-reads the atlases from disk (after an extraction or an art-set switch). Disposes both the active
    /// and the shared atlases and forgets every sprite cached by <see cref="Graphics.MsxScreen"/>.
    /// </summary>
    public static void Reload()
    {
        _needsReload = false;
        Unload();
        KingsValley.Engine.Graphics.MsxScreen.Reset();
        _kv2 = LoadKv2();
        _sharedKv2 = LoadAbsolute(System.IO.Path.Combine(RomContent.SharedRoot, "Art", "kv2.atlas"),
            premultiplyAlpha: true);
    }

    /// <summary>Alias used by the art-set switcher.</summary>
    public static void Reset() => Reload();

    /// <summary>Releases the global atlases (called on shutdown).</summary>
    public static void Unload()
    {
        DisposeAtlas(ref _kv2);
        DisposeAtlas(ref _sharedKv2);
        DisposeAtlas(ref _ui);
    }

    private static void DisposeAtlas(ref SpriteAtlas? atlas)
    {
        if (atlas == null)
            return;

        ((IDisposable)atlas).Dispose();
        atlas = null;
    }
}
