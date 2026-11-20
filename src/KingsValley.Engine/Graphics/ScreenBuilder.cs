#nullable enable
using System;
using System.IO;
using KingsValley.Core.Screens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace KingsValley.Engine.Graphics;

/// <summary>
/// Builds MonoGame textures from the data decoded by <see cref="ScreenAssembler"/>.
/// No background or game screen uses PNG: everything is assembled at runtime.
/// </summary>
public static class ScreenBuilder
{
    /// <summary>
    /// Assembles a <see cref="Texture2D"/> from a native MSX2 Screen 5 binary file (<c>.bin</c>).
    /// </summary>
    public static Texture2D? LoadTexture(GraphicsDevice device, string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var data = ScreenAssembler.DecodeFile(filePath);
        return BuildTexture(device, data);
    }

    /// <summary>
    /// Builds a MonoGame texture from <see cref="ScreenData"/> by assembling the pixels at runtime.
    /// </summary>
    public static Texture2D BuildTexture(GraphicsDevice device, ScreenData data, Color[]? customPalette = null)
    {
        var texture = new Texture2D(device, data.Width, data.Height);
        var pixels = new Color[data.Width * data.Height];

        if (customPalette != null && customPalette.Length >= 16)
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = customPalette[data.Indices[i] & 0x0F];
        }
        else
        {
            var pal = data.Palette;
            var mgPal = new Color[16];
            for (int i = 0; i < 16; i++)
            {
                var sc = pal[i];
                mgPal[i] = new Color(sc.R, sc.G, sc.B, sc.A);
            }

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = mgPal[data.Indices[i] & 0x0F];
        }

        texture.SetData(pixels);
        return texture;
    }
}
