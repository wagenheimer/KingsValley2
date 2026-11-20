#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace KingsValley.Core.Screens;

/// <summary>RGBA color of a decoded screen palette entry.</summary>
public readonly record struct ScreenColor(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// Decoded data of an MSX2 Screen 5 screen: dimensions, 16-color palette and
/// buffer of indices (0..15) per pixel.
/// </summary>
public sealed class ScreenData
{
    public int Width { get; }
    public int Height { get; }
    public bool HasAlpha { get; }
    public ScreenColor[] Palette { get; }
    public byte[] Indices { get; }

    public ScreenData(int width, int height, bool hasAlpha, ScreenColor[] palette, byte[] indices)
    {
        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
        Palette = palette;
        Indices = indices;
    }
}

/// <summary>
/// Assembles screens and backgrounds from the native binary format of the MSX2 Screen 5 VRAM/VDP.
/// No background or fixed screen uses static PNG files: everything is decoded and assembled
/// at runtime from the VDP palette and the 4bpp nibbles of 16 colors.
///
/// Binary format (.bin):
/// - Bytes 0..31: MSX2 VDP palette (16 colors × 2 bytes: (R &lt;&lt; 4) | B, G; R, G, B in 0..7)
/// - Bytes 32..35: Dimensions (ushort Width, ushort Height in little-endian)
/// - Byte 36: Flags (bit 0: HasAlpha — color 0 is transparent)
/// - Byte 37: Reserved (0)
/// - Bytes 38..: pixel data in 4bpp (2 pixels per byte: high nibble = even pixel, low nibble = odd pixel)
/// </summary>
public static class ScreenAssembler
{
    public const int HeaderSize = 38;
    public const int PaletteEntries = 16;

    /// <summary>Decodes a native MSX2 Screen 5 binary buffer.</summary>
    public static ScreenData Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < HeaderSize)
            throw new ArgumentException("Buffer too small for the MSX2 screen header.", nameof(bytes));

        int width = bytes[32] | (bytes[33] << 8);
        int height = bytes[34] | (bytes[35] << 8);
        byte flags = bytes[36];
        bool hasAlpha = (flags & 1) != 0;

        var palette = new ScreenColor[PaletteEntries];
        for (int i = 0; i < PaletteEntries; i++)
        {
            byte b0 = bytes[i * 2];
            byte b1 = bytes[i * 2 + 1];
            int r3 = (b0 >> 4) & 7;
            int b3 = b0 & 7;
            int g3 = b1 & 7;

            byte r8 = (byte)((r3 * 255) / 7);
            byte g8 = (byte)((g3 * 255) / 7);
            byte b8 = (byte)((b3 * 255) / 7);
            byte a8 = (hasAlpha && i == 0) ? (byte)0 : (byte)255;

            palette[i] = new ScreenColor(r8, g8, b8, a8);
        }

        int totalPixels = width * height;
        var indices = new byte[totalPixels];
        int offset = HeaderSize;
        int p = 0;

        while (p < totalPixels && offset < bytes.Length)
        {
            byte val = bytes[offset++];
            indices[p++] = (byte)((val >> 4) & 0x0F);
            if (p < totalPixels)
                indices[p++] = (byte)(val & 0x0F);
        }

        return new ScreenData(width, height, hasAlpha, palette, indices);
    }

    /// <summary>Decodes a binary file from disk.</summary>
    public static ScreenData DecodeFile(string filePath) => Decode(File.ReadAllBytes(filePath));

    /// <summary>Encodes to the native MSX2 Screen 5 binary format.</summary>
    public static byte[] Encode(int width, int height, IReadOnlyList<ScreenColor> palette, byte[] indices, bool hasAlpha = false)
    {
        var palBytes = new byte[32];
        for (int i = 0; i < PaletteEntries && i < palette.Count; i++)
        {
            var c = palette[i];
            int r3 = (c.R * 7 + 127) / 255;
            int g3 = (c.G * 7 + 127) / 255;
            int b3 = (c.B * 7 + 127) / 255;

            palBytes[i * 2] = (byte)(((r3 & 7) << 4) | (b3 & 7));
            palBytes[i * 2 + 1] = (byte)(g3 & 7);
        }

        int packedSize = (indices.Length + 1) / 2;
        var result = new byte[HeaderSize + packedSize];
        Array.Copy(palBytes, 0, result, 0, 32);

        result[32] = (byte)(width & 0xFF);
        result[33] = (byte)((width >> 8) & 0xFF);
        result[34] = (byte)(height & 0xFF);
        result[35] = (byte)((height >> 8) & 0xFF);
        result[36] = hasAlpha ? (byte)1 : (byte)0;
        result[37] = 0;

        int p = 0;
        int outOffset = HeaderSize;
        while (p < indices.Length)
        {
            byte p0 = (byte)(indices[p++] & 0x0F);
            byte p1 = (p < indices.Length) ? (byte)(indices[p++] & 0x0F) : (byte)0;
            result[outOffset++] = (byte)((p0 << 4) | p1);
        }

        return result;
    }
}
