#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingsValley.Core.SaveSystem;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Title preference. MSX1 carries both regional titles; the verified MSX2 profile uses its native Japanese title when English artwork is unavailable.
/// </summary>
public enum TitleLanguage
{
    /// <summary>Japanese title (エルキーザの封印) — the cartridge original.</summary>
    Japanese,

    /// <summary>English title (KING'S VALLEY II) — the export version.</summary>
    English,
}

/// <summary>
/// Paths of the content <b>derived from the ROM</b>: stages, art and audio.
///
/// Stages, audio and graphics resolve through the active cartridge flavor and validated generation. Art comes from the
/// **active set** (<see cref="ArtSet"/>): the originals (<c>Content/Rom</c>, <c>Content/RomMsx1</c>)
/// or a remaster in <c>Content/ArtSets/&lt;id&gt;/</c> — see <see cref="ArtSets"/>.
/// <list type="bullet">
/// <item><c>Rooms/</c> — room assembly (MSX2) or dumped room screens (MSX1);</item>
/// <item><c>Art/Screens/</c> — fixed screens in the native format (Screen 5 / Screen 2);</item>
/// <item><c>Art/kv2.atlas</c> — sprite/tile atlas.</item>
/// </list>
///
/// This content is generated locally by the standalone extraction pipeline from the user's own
/// ROM and is <b>not</b> versioned (see <c>docs/ROM_EXTRACTION.md</c>). If the folder does not exist, the
/// game shows the warning instead of opening an empty room.
/// </summary>
public static class RomContent
{
    /// <summary>Game <c>Content</c> folder (swappable in tests).</summary>
    public static string ContentDirectory { get; private set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");

    private static string Base => ContentDirectory;

    /// <summary>Development content root for the active cartridge flavor.</summary>
    public static string SharedRoot => Path.Combine(ContentDirectory, CartridgeVariants.ContentFolder(Variant));

    /// <summary>Art sets found on disk (the order is the cycle order in the menu).</summary>
    public static IReadOnlyList<ArtSet> Available { get; private set; } = Array.Empty<ArtSet>();

    /// <summary>Active art set.</summary>
    public static ArtSet Active { get; private set; } =
        new(ArtSets.Msx2Id, "MSX2", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Rom"),
            ArtFlavor.Msx2);

    /// <summary>Root of the active set (<c>Content/Rom</c>, <c>Content/RomMsx1</c>, <c>Content/ArtSets/…</c>).</summary>
    public static string Root => Active.Root;

    /// <summary>Pipeline of the active set (what changes the room assembly and the HUD).</summary>
    public static string Variant=>Active.Variant;
    public static string CampaignId=>Active.CampaignId;
    public static bool IsContest=>CampaignId==Core.Common.Campaigns.Contest;
    public static int StageCount=>CartridgeVariants.StageCount(Variant);
    public static Core.Gameplay.GameRun NewRun(int hiScore,int stage=1,int rest=Core.Common.GameConstants.StartingRest,bool invincible=false,bool continueEnabled=false)=>
        Core.Gameplay.GameRun.New(hiScore,stage,rest,invincible,continueEnabled,CampaignId);
    public static ArtFlavor Flavor => Active.Flavor;

    /// <summary>
    /// In MSX1 mode: items, soulstones, coffins and doors have a solid black backdrop
    /// (authentic to MSX1 SCREEN 2 attribute limits). Always enabled.
    /// </summary>
    public static bool Msx1OpaqueBackdrop => true;

    /// <summary>
    /// Title preference (MSX1 has both variants; unavailable artwork uses the native title). Set at boot from the save
    /// (or from <c>KINGSVALLEY_TITLE_LANG=jp|en</c>) and switched in the system menu.
    /// </summary>
    public static TitleLanguage TitleArtLanguage { get; private set; } = TitleLanguage.English;

    /// <summary>Switches the title art language (the system menu saves and calls this).</summary>
    public static void UseTitleLanguage(TitleLanguage language) => TitleArtLanguage = language;

    /// <summary>Reads <c>"en"</c> (any other value falls back to Japanese, which is the original).</summary>
    public static TitleLanguage ParseTitleLanguage(string? value) =>
        string.Equals(value?.Trim(), "en", StringComparison.OrdinalIgnoreCase)
            ? TitleLanguage.English
            : TitleLanguage.Japanese;

    /// <summary>
    /// Discovers the art sets on disk (<see cref="ArtSets.Discover"/>). Call once at boot,
    /// before any scene reads art. <paramref name="contentDirectory"/> exists for the tests.
    /// </summary>
    public static void Initialize(string? contentDirectory = null)
    {
        Extraction.GraphicsGenerationStore.Invalidate();
        if (!string.IsNullOrWhiteSpace(contentDirectory))
            ContentDirectory = contentDirectory;

        string? previousId = Active.Id;
        Available = ArtSets.Discover(ContentDirectory,
            Path.Combine(AudioBootstrap.GraphicsDirectoryFor(ArtFlavor.Msx1), "Screens"))
            .Where(set => IsVariantAvailable(set.Variant)).ToArray();

        ArtSet? previous = ArtSets.Find(Available, previousId);
        if (previous != null && IsVariantAvailable(previous.Variant))
        {
            // An explicit, playable choice (save or KINGSVALLEY_GFX) always wins.
            Active = previous;
        }
        else
        {
            // The stored set may be stale after a fresh extraction (e.g. MSX2 selected but only the MSX1
            // ROM was inserted). Auto-select a flavor that can actually run.
            ArtFlavor playable = GetFirstPlayableFlavor();
            string preferredId = playable == ArtFlavor.Msx2 ? ArtSets.Msx2Id : ArtSets.Msx1Id;
            Active = ArtSets.Find(Available, preferredId)
                     ?? previous
                     ?? (Available.Count > 0 ? Available[0] : Active);
        }

    }

    /// <summary>
    /// Switches the art set. The switch applies to the next scenes loading the right art
    /// (the textures already loaded stay valid until the scene is recreated).
    /// </summary>
    public static void Use(string id)
    {
        ArtSet? set = ArtSets.Find(Available, id);
        if (set != null)
            Active = set;
    }

    /// <summary>Switches the art set to one already resolved.</summary>
    public static void Use(ArtSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Active = set;
    }

    /// <summary>
    /// Folder of the active cartridge's stages (<c>stageNN.json</c>): 60 original stages or 20 Contest challenges. In the free version,
    /// the files are generated from the ROM on the first boot (<see cref="Extraction.RomStageJson"/>) in
    /// <c>&lt;MSXMusic&gt;/kings-valley-2/&lt;variant&gt;/Graphics/generations/&lt;id&gt;/Levels</c>.
    /// </summary>
    public static string LevelsDirectory=>LevelsForVariant(Variant);
    public static string LevelsForVariant(string variant)
    {
        string extracted=Path.Combine(AudioBootstrap.GraphicsDirectoryForVariant(variant),"Levels");
        return Directory.Exists(extracted)?extracted:Path.Combine(ContentDirectory,CartridgeVariants.ContentFolder(variant),"Levels");
    }

    public static string LevelsFor(ArtFlavor flavor)
    {
        string extracted = Path.Combine(AudioBootstrap.GraphicsDirectoryFor(flavor), "Levels");
        return Directory.Exists(extracted) ? extracted : Path.Combine(ContentDirectory, flavor == ArtFlavor.Msx1 ? "RomMsx1" : "Rom", "Levels");
    }

    /// <summary>Folder of the user's custom levels (created in the Level Editor).</summary>
    public static string CustomLevelsDirectory
    {
        get
        {
            var dir = Path.Combine(Base, "CustomLevels");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Folder of the room assembly (blocks, murals, decorations, palettes).</summary>
    public static string RoomsDirectory
    {
        get
        {
            if (Active.ExtractedFirst)
            {
                string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Rooms");
                if (Directory.Exists(extracted))
                    return extracted;
            }
            return ArtSets.ResolveDirectory(Available, Active, "Rooms");
        }
    }

    /// <summary>
    /// Room support file (murals, palette tables, animation data). The extracted dumps win, but a bundle
    /// may ship shared tables that extraction does not generate; fall back to it when the file is absent.
    /// </summary>
    public static string RoomSupportPath(string fileName)
    {
        if (Active.ExtractedFirst)
        {
            string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Rooms", fileName);
            if (File.Exists(extracted))
                return extracted;
        }

        return Path.Combine(ArtSets.ResolveDirectory(Available, Active, "Rooms"), fileName);
    }

    /// <summary>Atlas of the active set (tiles, objects, characters, HUD).</summary>
    public static string AtlasPath
    {
        get
        {
            if (Active.ExtractedFirst)
            {
                string extracted = Path.Combine(AudioBootstrap.GraphicsDirectoryForVariant(Variant), "Art", "kv2.atlas");
                if (File.Exists(extracted)) return extracted;
            }
            return ArtSets.Resolve(Available, Active, Path.Combine("Art", "kv2.atlas"));
        }
    }

    /// <summary>Audio root (<c>audio.msxaudio</c> + the source VGMs).</summary>
    public static string AudioDirectory => Path.Combine(SharedRoot, "Audio");

    /// <summary>VGM folder (lossless sound source: PSG/SCC register writes).</summary>
    public static string VgmDirectory => Path.Combine(AudioDirectory, "Vgm");

    /// <summary>Music VGMs.</summary>
    public static string VgmMusicDirectory => Path.Combine(VgmDirectory, "Music");

    /// <summary>SFX VGMs.</summary>
    public static string VgmSfxDirectory => Path.Combine(VgmDirectory, "Sfx");

    /// <summary>Fixed screen (title, select, pyramid) assembled at runtime in the native format.</summary>
    public static string ScreenPath(string name)
    {
        // Apply the language preference only to the title. An unavailable regional title
        // uses the active cartridge's native title; START SELECT is a distinct screen.
        if (name == "title" && TitleArtLanguage != TitleLanguage.Japanese)
        {
            string english = ResolveScreen("title_en");
            if (File.Exists(english))
                return english;
        }

        return ResolveScreen(name);
    }

    private static string ResolveScreen(string name)
        =>ResolveScreenFile(name+".bin");

    public static string ScreenMetadataPath(string name)=>ResolveScreenFile(name+".json");

    private static string ResolveScreenFile(string fileName)
    {
        // Prefer the active flavor's validated generation before same-flavor development/custom assets.
        if (Active.ExtractedFirst)
        {
            string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Screens", fileName);
            if (File.Exists(extracted))
                return extracted;
        }

        return ArtSets.Resolve(Available, Active, Path.Combine("Art", "Screens", fileName));
    }

    /// <summary>Were the minimum data (stages + a playable art set) generated?</summary>
    public static bool IsAvailable => CanPlayAny;

    /// <summary>Are the 60 game stages extracted and ready to play?</summary>
    public static bool AreStagesAvailable =>
        System.Linq.Enumerable.Range(1, StageCount).All(n => File.Exists(Path.Combine(LevelsDirectory, $"stage{n:00}.json")));

    /// <summary>Is the audio package (PSG/SCC) extracted and ready?</summary>
    public static bool IsAudioAvailable =>
        AudioBootstrap.HasPackage;

    /// <summary>
    /// Are the MSX1 graphics extracted or present? The free build needs all three: the title screen, a
    /// usable generated atlas and at least one native room dump. A missing room dump means the game would
    /// fall back to the MSX2 assembly, which a public build does not ship.
    /// </summary>
    public static bool IsMsx1Available => HasCompleteGraphics(ArtFlavor.Msx1);
    public static bool IsMsx2Available => HasCompleteGraphics(ArtFlavor.Msx2);

    private static bool HasCompleteGraphics(ArtFlavor flavor)=>IsVariantAvailable(CartridgeVariants.Original(flavor));
    public static bool IsContestAvailable=>IsVariantAvailable(CartridgeVariants.Contest);
    public static bool IsVariantAvailable(string variant)
    {
        string? generation = Extraction.GraphicsGenerationStore.Current(AudioBootstrap.GraphicsRootForVariant(variant), variant);
        if (generation != null) return true;
        // Development bundles require the same manifest/identity contract as extracted generations.
        string disk = Path.Combine(ContentDirectory, CartridgeVariants.ContentFolder(variant));
        return Extraction.GraphicsExtractionManifest.Read(disk)?.Verify(disk, variant) == true;
    }

    /// <summary>Checks whether a specific art flavor has all required assets ready.</summary>
    public static bool IsFlavorAvailable(ArtFlavor flavor) => flavor switch
    {
        ArtFlavor.Msx1 => IsMsx1Available,
        ArtFlavor.Msx2 => IsMsx2Available,
        _ => false,
    };

    /// <summary>Can the player launch the game (at least one flavor available and stages present)?</summary>
    public static bool CanPlayAny =>
        (IsMsx1Available && Directory.Exists(LevelsFor(ArtFlavor.Msx1))) ||
        (IsMsx2Available && Directory.Exists(LevelsFor(ArtFlavor.Msx2))) || IsContestAvailable;

    /// <summary>Returns the first flavor that is currently playable (MSX2 preferred if available).</summary>
    public static ArtFlavor GetFirstPlayableFlavor()
    {
        if (IsMsx2Available) return ArtFlavor.Msx2;
        if (IsMsx1Available) return ArtFlavor.Msx1;
        return ArtFlavor.Msx2;
    }
}
