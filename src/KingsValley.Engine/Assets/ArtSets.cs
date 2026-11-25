#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KingsValley.Engine.Assets;

/// <summary>
/// How the art of a set is organized. It changes the **pipeline**, not the drawing:
/// MSX2 assembles the room from blocks/murals and has the 20 px HUD; MSX1 uses the room screens
/// dumped from the cartridge and the screen has only the 24 rows of the field (256x192).
/// </summary>
public enum ArtFlavor
{
    /// <summary>MSX2 (SCREEN 5, 256x212): rooms assembled from blocks, HUD in the bottom strip.</summary>
    Msx2,

    /// <summary>MSX1 (SCREEN 2, 256x192): rooms dumped from the cartridge, without the HUD strip.</summary>
    Msx1,
}

/// <summary>
/// An **art set** switchable at runtime: the game's originals (MSX2 and MSX1 from their respective
/// cartridge) or a **remaster** with new art.
/// </summary>
/// <param name="Id">Short, stable name (<c>msx2</c>, <c>msx1</c>, <c>remaster</c>) — it is what goes in the save.</param>
/// <param name="Name">Name shown in the system menu.</param>
/// <param name="Root">Set folder (contains <c>Art/</c>, <c>Rooms/</c>…).</param>
/// <param name="Flavor">Room/HUD pipeline the set follows.</param>
/// <param name="BaseId">Set the missing files are taken from (e.g.: remaster → msx2).</param>
/// <param name="ExtractedFirst">
/// Looks for the fixed screens in the folder extracted from the ROM (<c>&lt;MSXMusic&gt;/kings-valley-2/&lt;variant&gt;/Graphics/generations/&lt;id&gt;/Screens</c>)
/// before its own folder. That is the case of MSX1 in the free build, which carries no Konami content.
/// </param>
public sealed record ArtSet(
    string Id,
    string Name,
    string Root,
    ArtFlavor Flavor,
    string? BaseId = null,
    bool ExtractedFirst = false);

/// <summary>
/// Discovers the art sets available on disk. **Adding a remaster is just creating a folder**:
///
/// <code>
/// Content/ArtSets/remaster/
///     artset.json          { "name": "Remaster", "flavor": "msx2", "base": "msx2" }
///     Art/kv2.atlas        (and whatever else the remaster redoes: Art/Screens/*.bin, Rooms/…)
/// </code>
///
/// Whatever does not exist in the remaster is read from the <c>base</c>, so one can start by redoing just one screen.
/// </summary>
public static class ArtSets
{
    /// <summary>Original MSX2 set identifier.</summary>
    public const string Msx2Id = "msx2";

    /// <summary>Original MSX1 set identifier.</summary>
    public const string Msx1Id = "msx1";

    /// <summary>Name of a set's manifest in <c>Content/ArtSets/&lt;id&gt;/</c>.</summary>
    public const string ManifestName = "artset.json";

    private static readonly JsonSerializerOptions ManifestOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Optional art-set manifest (<c>artset.json</c>).</summary>
    private sealed class Manifest
    {
        public string? Name { get; set; }
        public string? Flavor { get; set; }
        public string? Base { get; set; }
    }

    /// <summary>
    /// Lists the sets that exist in <paramref name="contentDirectory"/>, from the most "original" to
    /// the newest (the order is the cycle order in the menu).
    /// </summary>
    /// <param name="contentDirectory">The game's <c>Content</c> folder.</param>
    /// <param name="extractedScreensDirectory">
    /// Folder of the screens extracted from the ROM; when it exists, the MSX1 set enters even without
    /// <c>Content/RomMsx1</c> (it is the free build).
    /// </param>
    public static IReadOnlyList<ArtSet> Discover(string contentDirectory, string? extractedScreensDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);

        var sets = new List<ArtSet>();

        string msx2Root = Path.Combine(contentDirectory, "Rom");
        bool msx2OnDisk = Directory.Exists(Path.Combine(msx2Root, "Art"));
        bool msx2Extracted = Directory.Exists(AudioBootstrap.GraphicsDirectoryFor(ArtFlavor.Msx2));

        if (msx2OnDisk || msx2Extracted)
            sets.Add(new ArtSet(Msx2Id, "MSX2", msx2Root, ArtFlavor.Msx2, ExtractedFirst: msx2Extracted));

        string msx1Root = Path.Combine(contentDirectory, "RomMsx1");
        bool msx1OnDisk = Directory.Exists(Path.Combine(msx1Root, "Art"));
        bool msx1Extracted = !string.IsNullOrWhiteSpace(extractedScreensDirectory) &&
                             Directory.Exists(extractedScreensDirectory);

        // MSX1 also enters when only the extracted screens exist: in the free build there is no Content/Rom*
        // and the cartridge art is generated on the first boot.
        if (msx1OnDisk || msx1Extracted)
            sets.Add(new ArtSet(Msx1Id, "MSX1", msx1Root, ArtFlavor.Msx1, ExtractedFirst: true));

        sets.AddRange(DiscoverCustom(contentDirectory, sets.Select(s => s.Id)));
        return sets;
    }

    /// <summary>Finds the set by id; returns the first available one when the id does not exist.</summary>
    public static ArtSet? Find(IReadOnlyList<ArtSet> sets, string? id)
    {
        if (sets.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(id))
        {
            ArtSet? match = sets.FirstOrDefault(s =>
                string.Equals(s.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;
        }

        return sets[0];
    }

    /// <summary>
    /// Path of an art file: the active set and, if it does not have the file, the <c>base</c>.
    /// Returns the active set's path when it does not exist anywhere (the caller decides).
    /// </summary>
    public static string Resolve(IReadOnlyList<ArtSet> sets, ArtSet active, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(active);

        string primary = Path.Combine(active.Root, relativePath);
        if (File.Exists(primary))
            return primary;

        foreach (string id in Chain(sets, active))
        {
            ArtSet? fallback = sets.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

            if (fallback is null)
                continue;

            string candidate = Path.Combine(fallback.Root, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        return primary;
    }

    /// <summary>Same as <see cref="Resolve"/>, for a folder (e.g.: <c>Rooms</c>).</summary>
    public static string ResolveDirectory(IReadOnlyList<ArtSet> sets, ArtSet active, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(active);

        string primary = Path.Combine(active.Root, relativePath);
        if (Directory.Exists(primary))
            return primary;

        foreach (string id in Chain(sets, active))
        {
            ArtSet? fallback = sets.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

            if (fallback is null)
                continue;

            string candidate = Path.Combine(fallback.Root, relativePath);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return primary;
    }

    /// <summary>Ids of the active set's fallback chain (the <c>base</c>, then its base…).</summary>
    private static IEnumerable<string> Chain(IReadOnlyList<ArtSet> sets, ArtSet active)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { active.Id };
        string? id = active.BaseId;

        // Hop limit so a badly configured chain (a → b → a) does not hang the boot.
        for (int hops = 0; !string.IsNullOrWhiteSpace(id) && hops < 8; hops++)
        {
            if (!visited.Add(id))
                yield break;

            ArtSet? candidate = sets.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (candidate == null || candidate.Flavor != active.Flavor) yield break;
            yield return id;

            ArtSet? next = sets.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

            id = next?.BaseId;
        }
    }

    /// <summary>Reads <c>Content/ArtSets/&lt;id&gt;/</c> (folders created by the player or by a remaster).</summary>
    private static IEnumerable<ArtSet> DiscoverCustom(string contentDirectory, IEnumerable<string> taken)
    {
        string custom = Path.Combine(contentDirectory, "ArtSets");
        if (!Directory.Exists(custom))
            yield break;

        var ids = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        foreach (string directory in Directory.GetDirectories(custom).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(id) || ids.Contains(id))
                continue;

            // A set with no art is an empty folder (or only the manifest): it does not enter the menu.
            if (!Directory.Exists(Path.Combine(directory, "Art")))
                continue;

            Manifest manifest = ReadManifest(directory);

            ArtFlavor flavor = string.Equals(manifest.Flavor, "msx1", StringComparison.OrdinalIgnoreCase)
                ? ArtFlavor.Msx1
                : ArtFlavor.Msx2;

            string name = string.IsNullOrWhiteSpace(manifest.Name) ? id.ToUpperInvariant() : manifest.Name!;
            string? baseId = string.IsNullOrWhiteSpace(manifest.Base) ? (flavor == ArtFlavor.Msx1 ? Msx1Id : Msx2Id) : manifest.Base;

            ids.Add(id);
            yield return new ArtSet(id, name, directory, flavor, baseId);
        }
    }

    private static Manifest ReadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestName);
        if (!File.Exists(path))
            return new Manifest();

        try
        {
            return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), ManifestOptions) ?? new Manifest();
        }
        catch (JsonException)
        {
            // A broken manifest cannot keep the game from opening: falls back to the default (MSX2, base msx2).
            Console.WriteLine($"[ArtSets] warning: {path} is invalid, using the default");
            return new Manifest();
        }
    }
}
