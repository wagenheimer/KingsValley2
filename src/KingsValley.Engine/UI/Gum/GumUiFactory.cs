#nullable enable
using System;
using Gum.GueDeriving;
using Nez.Sprites;
using KingsValley.Engine.Assets;
using Microsoft.Xna.Framework;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Sprite lookup in the Nez <c>SpriteAtlas</c> (the atlas Gum loads) — the rest of the
/// UI assembly left C#: the screen lives in the `.gusx` and the runtime only swaps the art of **some**
/// sprites (the goal card token, the power-up badge, the power-up shop icons).
///
/// ## Atlas art **by name** (never by coordinate in the XML)
///
/// Gum only selects an atlas frame by **coordinates** (`TextureLeft/Top/Width/Height`), which
/// breaks silently when the atlas is repacked. That is why the project's rule is: the
/// <c>.gusx</c> declares only the **size/pivot/position**, and C# writes the sprite region by
/// **name** in the Nez atlas (<see cref="ApplySprite"/>) — which indexes the sprites by name. If the
/// atlas changes, only the name matters.
///
/// ## The pitfall this helper still guards
///
/// <c>SpriteAtlas.GetSprite</c> **crashes** when the name does not exist (index −1), so the name is
/// checked in <c>Names</c> first. And when assigning the art to a Gum sprite you must set
/// <c>TextureWidth/Height</c> by hand — assigning <c>Texture</c> does **not** sync it.
/// </summary>
internal static class GumUiFactory
{
    /// <summary>
    /// Looks up a sprite in the atlas by name; returns <c>null</c> (with a warning) if the atlas or the sprite
    /// do not exist.
    /// </summary>
    public static Nez.Textures.Sprite? FindSprite(SpriteAtlas? atlas, string spriteName)
    {
        if (atlas == null)
            return null;

        var names = atlas.Names;
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] == spriteName)
                return atlas.GetSprite(spriteName);
        }

        Console.WriteLine($"[GumUiFactory] sprite '{spriteName}' not found in the atlas — omitted.");
        return null;
    }

    /// <summary>
    /// Binds a <see cref="SpriteRuntime"/> to an atlas sprite **by name** — the robust path
    /// for atlas art (no coordinates in the <c>.gusx</c>). When <paramref name="maxWidth"/> and
    /// <paramref name="maxHeight"/> are positive, it resizes preserving the aspect ratio inside
    /// that box; otherwise it keeps the size declared in the layout.
    /// </summary>
    /// <returns><c>true</c> if it applied the art; <c>false</c> if the target/atlas/sprite do not exist.</returns>
    public static bool ApplySprite(
        SpriteRuntime? target,
        SpriteAtlas? atlas,
        string spriteName,
        float maxWidth = 0f,
        float maxHeight = 0f)
    {
        if (target == null)
            return false;

        var sprite = FindSprite(atlas, spriteName);
        if (sprite == null)
            return false;

        var rect = sprite.SourceRect;

        target.Texture = sprite.Texture2D;
        target.TextureWidth = sprite.Texture2D.Width;
        target.TextureHeight = sprite.Texture2D.Height;
        target.SourceRectangle = rect;
        target.TextureAddress = global::Gum.Managers.TextureAddress.Custom;

        if (maxWidth > 0f && maxHeight > 0f)
        {
            float scale = MathF.Min(maxWidth / rect.Width, maxHeight / rect.Height);
            target.WidthUnits = global::Gum.DataTypes.DimensionUnitType.Absolute;
            target.HeightUnits = global::Gum.DataTypes.DimensionUnitType.Absolute;
            target.Width = rect.Width * scale;
            target.Height = rect.Height * scale;
        }

        return true;
    }
}

