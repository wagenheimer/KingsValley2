using System.Globalization;

namespace Wagenheimer.MsxGraphics.Formats;

public sealed record AtlasRegion(string Name, int X, int Y, int Width, int Height, int VisiblePixels);

/// <summary>Validates a simple three-line atlas without a GPU or game-specific assumptions.</summary>
public static class AtlasInventory
{
    public static IReadOnlyList<AtlasRegion> Read(string text, PngRgba texture)
    {
        string[] lines = text.Replace("\r", "").TrimEnd('\n').Split('\n');
        if (lines.Length % 3 != 0) throw new InvalidDataException("Truncated atlas entry.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var regions = new List<AtlasRegion>();
        for (int i = 0; i < lines.Length; i += 3)
        {
            string name = lines[i].Trim();
            if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("Empty or duplicate atlas region: " + name);
            string[] values = lines[i + 1].Split(',');
            if (values.Length != 4 || !values.All(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) throw new InvalidDataException("Malformed atlas rectangle: " + name);
            int[] box = values.Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            int visible = texture.VisiblePixels(box[0], box[1], box[2], box[3]);
            regions.Add(new(name, box[0], box[1], box[2], box[3], visible));
        }
        return regions;
    }
}
