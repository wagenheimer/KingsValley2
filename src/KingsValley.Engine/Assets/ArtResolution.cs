#nullable enable
using System;
using System.Globalization;
using System.IO;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Resolves **which art resolution** is in use (1x, 2x, 4x) and the path of the matching atlas.
///
/// Used to validate the multi-resolution pipeline without changing code: the atlases are generated with
/// an identical layout at different scales (see `tools/make_atlas_2x.ps1`) and the game only switches
/// set, applying `BaseScale = 1 / ArtScale` to every board visual.
///
/// <code>
/// $env:KINGSVALLEY_ART_SCALE = "2"
/// dotnet run --project src/KingsValley.Desktop
/// </code>
///
/// Without the variable, it uses **1x** (the current art). See "resolution independence" in `AGENTS.md`.
/// </summary>
public static class ArtResolution
{
    /// <summary>Environment variable that selects the art resolution.</summary>
    public const string ScaleEnvironmentVariable = "KINGSVALLEY_ART_SCALE";

    private static bool _resolved;

    /// <summary>Art scale in use: `1` = 1x (default), `2` = 2x, `4` = 4x.</summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>File suffix: empty at 1x, `@2x`, `@4x`.</summary>
    public static string Suffix { get; private set; } = string.Empty;

    /// <summary>
    /// Converts the path of a 1x atlas into the path of the atlas of the resolution in use.
    /// `Content/Atlases/tokens.atlas` -> `Content/Atlases/tokens@2x.atlas` when the scale is 2.
    /// </summary>
    public static string ResolveAtlasPath(string baseAtlasPath)
    {
        EnsureResolved();

        if (Suffix.Length == 0)
            return baseAtlasPath;

        var directory = Path.GetDirectoryName(baseAtlasPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(baseAtlasPath);
        var extension = Path.GetExtension(baseAtlasPath);

        var fileNameWithSuffix = fileName + Suffix + extension;
        return directory.Length == 0 ? fileNameWithSuffix : Path.Combine(directory, fileNameWithSuffix);
    }

    /// <summary>
    /// Resolves the scale and the suffix once. Invalid values fall back to 1x.
    /// </summary>
    private static void EnsureResolved()
    {
        if (_resolved)
            return;

        _resolved = true;

        var raw = Environment.GetEnvironmentVariable(ScaleEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw)
            || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0f
            || parsed == 1f)
        {
            // 1x is the default: no suffix, original files.
            return;
        }

        Scale = parsed;
        Suffix = string.Create(CultureInfo.InvariantCulture, $"@{parsed:0}x");

        Console.WriteLine($"[ArtResolution] Arte em {parsed:0}x (sufixo '{Suffix}').");
    }
}
