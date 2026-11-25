#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KingsValley.Engine.Assets;

/// <summary>One sprite entry of the simplified Nez atlas format written by the ROM extraction.</summary>
public readonly record struct AtlasSpriteEntry(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float OriginX,
    float OriginY);

/// <summary>
/// Reads the simplified atlas text used by the extracted <c>kv2</c> atlas: one name line, one
/// <c>x,y,w,h</c> line and one <c>originX,originY</c> line per sprite, optionally preceded by a
/// <c>kv2.png</c> header. This is intentionally independent of MonoGame so it can be tested directly.
/// </summary>
public static class AtlasFile
{
    /// <summary>Reads and parses an atlas file, returning an empty list for a malformed file.</summary>
    public static IReadOnlyList<AtlasSpriteEntry> Read(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<AtlasSpriteEntry>();

        return Parse(File.ReadAllLines(path));
    }

    /// <summary>Parses atlas lines; public for tests that do not need a file on disk.</summary>
    public static IReadOnlyList<AtlasSpriteEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<AtlasSpriteEntry>();
        var all = new List<string>(lines);
        int i = 0;

        if (all.Count > 0 && all[0].Trim().EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            i++;

        while (i < all.Count)
        {
            string name = all[i++].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (i + 1 >= all.Count ||
                !TryRect(all[i++], out int x, out int y, out int w, out int h) ||
                !TryOrigin(all[i++], out float ox, out float oy))
                break;

            entries.Add(new AtlasSpriteEntry(name, x, y, w, h, ox, oy));
        }

        return entries;
    }

    private static bool TryRect(string line, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        string[] parts = line.Split(',');
        return parts.Length == 4 &&
               int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
               int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y) &&
               int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out w) &&
               int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
    }

    private static bool TryOrigin(string line, out float x, out float y)
    {
        x = y = 0f;
        string[] parts = line.Split(',');
        return parts.Length == 2 &&
               float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }
}
