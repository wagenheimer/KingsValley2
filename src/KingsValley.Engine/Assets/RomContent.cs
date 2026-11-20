#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using KingsValley.Core.SaveSystem;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Title art language. The cartridge carries both versions (Japanese and English) and picks by the
/// machine's region; here the player chooses, in the system menu.
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
/// Stages and audio are the same in any art set and live in <c>Content/Rom</c>. The art comes from the
/// **active set** (<see cref="ArtSet"/>): the originals (<c>Content/Rom</c>, <c>Content/RomMsx1</c>)
/// or a remaster in <c>Content/ArtSets/&lt;id&gt;/</c> — see <see cref="ArtSets"/>.
/// <list type="bullet">
/// <item><c>Rooms/</c> — room assembly (MSX2) or dumped room screens (MSX1);</item>
/// <item><c>Art/Screens/</c> — fixed screens in the native format (Screen 5 / Screen 2);</item>
/// <item><c>Art/kv2.atlas</c> — sprite/tile atlas.</item>
/// </list>
///
/// This content is generated locally by <c>tools/kv2-extract</c> from the user's own
/// ROM and is <b>not</b> versioned (see <c>docs/ROM_EXTRACTION.md</c>). If the folder does not exist, the
/// game shows the warning instead of opening an empty room.
/// </summary>
public static class RomContent
{
    /// <summary>Game <c>Content</c> folder (swappable in tests).</summary>
    public static string ContentDirectory { get; private set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");

    private static string Base => ContentDirectory;

    /// <summary>Shared root (stages and audio, identical in every set).</summary>
    public static string SharedRoot => Path.Combine(ContentDirectory, "Rom");

    /// <summary>Art sets found on disk (the order is the cycle order in the menu).</summary>
    public static IReadOnlyList<ArtSet> Available { get; private set; } = Array.Empty<ArtSet>();

    /// <summary>Active art set.</summary>
    public static ArtSet Active { get; private set; } =
        new(ArtSets.Msx2Id, "MSX2", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Rom"),
            ArtFlavor.Msx2);

    /// <summary>Root of the active set (<c>Content/Rom</c>, <c>Content/RomMsx1</c>, <c>Content/ArtSets/…</c>).</summary>
    public static string Root => Active.Root;

    /// <summary>Pipeline of the active set (what changes the room assembly and the HUD).</summary>
    public static ArtFlavor Flavor => Active.Flavor;

    /// <summary>
    /// In MSX1 mode: items, soulstones, coffins and doors have a solid black backdrop
    /// (authentic to MSX1 SCREEN 2 attribute limits). Default true.
    /// </summary>
    public static bool Msx1OpaqueBackdrop { get; set; } = true;

    /// <summary>
    /// Title art language (the cartridge carries Japanese and English). Set at boot from the save
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
        if (!string.IsNullOrWhiteSpace(contentDirectory))
            ContentDirectory = contentDirectory;

        string? previousId = Active.Id;
        Available = ArtSets.Discover(ContentDirectory,
            Path.Combine(AudioBootstrap.ExtractedDirectory, "Screens"));

        ArtSet? previous = ArtSets.Find(Available, previousId);
        if (previous != null && IsFlavorAvailable(previous.Flavor))
        {
            // An explicit, playable choice (save or KINGSVALLEY_GFX) always wins.
            Active = previous;
        }
        else
        {
            // The stored set may be stale after a fresh extraction (e.g. MSX2 selected but only the MSX1
            // ROM was inserted). Auto-select a flavor that can actually run.
            ArtFlavor playable = GetFirstPlayableFlavor();
            string preferredId = playable == ArtFlavor.Msx1 ? ArtSets.Msx1Id : ArtSets.Msx2Id;
            Active = ArtSets.Find(Available, preferredId)
                     ?? previous
                     ?? (Available.Count > 0 ? Available[0] : Active);
        }

        try
        {
            var save = ProfileSaveService.Load();
            Msx1OpaqueBackdrop = save.Msx1OpaqueBackdrop;
        }
        catch
        {
            // Fallback to default
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
    /// Folder of the 60 original stages (<c>stageNN.json</c>) — the same in both versions. In the free version,
    /// the files are generated from the ROM on the first boot (<see cref="Extraction.RomStageJson"/>) in
    /// <c>%APPDATA%\KingsValley\extracted\Levels</c>, which then takes priority.
    /// </summary>
    public static string LevelsDirectory
    {
        get
        {
            string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Levels");
            if (Directory.Exists(extracted) && IsValidStageDirectory(extracted))
                return extracted;

            return Path.Combine(SharedRoot, "Levels");
        }
    }

    private static bool IsValidStageDirectory(string dir)
    {
        string stage1 = Path.Combine(dir, "stage01.json");
        if (!File.Exists(stage1)) return false;

        try
        {
            string json = File.ReadAllText(stage1);
            // Corrupted stage extraction has empty coffins on stage 1 and player start at 0,0
            if (json.Contains("\"y\": 0,\n      \"x\": 0") ||
                json.Contains("\"y\": 0,\r\n      \"x\": 0") ||
                json.Contains("\"y\": 0,\n    \"x\": 0") ||
                json.Contains("\"y\": 0,\r\n    \"x\": 0"))
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
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
            // A generated MSX1 fallback atlas keeps the game running but is not the original art. When the
            // application ships the real MSX2-derived atlas, it must win: that is what makes gameplay,
            // enemies, items and the HUD faithful even when only the MSX1 ROM was extracted.
            if (Active.ExtractedFirst && !IsGeneratedAtlasSelected())
            {
                string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Art", "kv2.atlas");
                if (IsUsableAtlas(extracted))
                    return extracted;
            }

            return ArtSets.Resolve(Available, Active, Path.Combine("Art", "kv2.atlas"));
        }
    }

    /// <summary>
    /// True when the extracted atlas is the generated approximation and the application has real art to
    /// substitute for it.
    /// </summary>
    private static bool IsGeneratedAtlasSelected()
    {
        string marker = Path.Combine(AudioBootstrap.ExtractedDirectory, "Art", "kv2.generated");
        if (!File.Exists(marker))
            return false;

        return File.Exists(Path.Combine(ContentDirectory, "Rom", "Art", "kv2.atlas")) ||
               File.Exists(Path.Combine(ContentDirectory, "RomMsx1", "Art", "kv2.atlas"));
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
        // The cartridge carries the title in Japanese and in English and picks by the machine's region; here the
        // player chooses (LANGUAGE in the system menu). The title is also the menu screen, and the
        // GAME/EDIT box already comes in it.
        if (name is "title" or "start_select" && TitleArtLanguage != TitleLanguage.Japanese)
        {
            string english = ResolveScreen("title_en");
            if (File.Exists(english))
                return english;
        }

        return ResolveScreen(name);
    }

    private static string ResolveScreen(string name)
    {
        // In the free version the screens come from the ROM on the first boot and the extracted folder has priority (only the
        // MSX1 set uses this: the extracted screens are SCREEN 2, 256x192).
        if (Active.ExtractedFirst)
        {
            string extracted = Path.Combine(AudioBootstrap.ExtractedDirectory, "Screens", name + ".bin");
            if (File.Exists(extracted))
                return extracted;
        }

        return ArtSets.Resolve(Available, Active, Path.Combine("Art", "Screens", name + ".bin"));
    }

    /// <summary>Were the minimum data (stages + a playable art set) generated?</summary>
    public static bool IsAvailable => CanPlayAny;

    /// <summary>Are the 60 game stages extracted and ready to play?</summary>
    public static bool AreStagesAvailable =>
        File.Exists(Path.Combine(LevelsDirectory, "stage01.json"));

    /// <summary>Is the audio package (PSG/SCC) extracted and ready?</summary>
    public static bool IsAudioAvailable =>
        AudioBootstrap.HasPackage;

    /// <summary>
    /// Are the MSX1 graphics extracted or present? The free build needs all three: the title screen, a
    /// usable generated atlas and at least one native room dump. A missing room dump means the game would
    /// fall back to the MSX2 assembly, which a public build does not ship.
    /// </summary>
    public static bool IsMsx1Available
    {
        get
        {
            string extracted = AudioBootstrap.ExtractedDirectory;
            if (File.Exists(Path.Combine(extracted, "Screens", "title.bin")) &&
                HasUsableAtlas() &&
                File.Exists(Path.Combine(extracted, "Rooms", "dumps", "s01_r01.bin")))
                return true;

            string disk = Path.Combine(ContentDirectory, "RomMsx1");
            return File.Exists(Path.Combine(disk, "Art", "Screens", "title.bin")) &&
                   File.Exists(Path.Combine(disk, "Art", "kv2.atlas")) &&
                   File.Exists(Path.Combine(disk, "Rooms", "dumps", "s01_r01.bin"));
        }
    }

    /// <summary>
    /// Whether any atlas can render the game: the extracted one, the bundled MSX2 art, or the MSX1 pack.
    /// </summary>
    private static bool HasUsableAtlas()
    {
        if (IsUsableAtlas(Path.Combine(AudioBootstrap.ExtractedDirectory, "Art", "kv2.atlas")))
            return true;

        return File.Exists(Path.Combine(ContentDirectory, "Rom", "Art", "kv2.atlas")) ||
               File.Exists(Path.Combine(ContentDirectory, "RomMsx1", "Art", "kv2.atlas"));
    }

    /// <summary>A generated atlas is small but must still contain the real player sprite name.</summary>
    private static bool IsUsableAtlas(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 200)
                return false;

            return File.ReadAllText(path).Contains("vick_s0_00", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Are the MSX2 graphics, rooms, and atlas extracted or present?</summary>
    public static bool IsMsx2Available
    {
        get
        {
            string diskAtlas = Path.Combine(ContentDirectory, "Rom", "Art", "kv2.atlas");
            string diskMurals = Path.Combine(ContentDirectory, "Rom", "Rooms", "murals.json");
            if (File.Exists(diskAtlas) && File.Exists(diskMurals))
                return true;

            string extractedAtlas = Path.Combine(AudioBootstrap.ExtractedDirectory, "Art", "kv2.atlas");
            string extractedMurals = Path.Combine(AudioBootstrap.ExtractedDirectory, "Rooms", "murals.json");
            return File.Exists(extractedAtlas) && File.Exists(extractedMurals);
        }
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
        AreStagesAvailable && (IsMsx1Available || IsMsx2Available);

    /// <summary>Returns the first flavor that is currently playable.</summary>
    public static ArtFlavor GetFirstPlayableFlavor()
    {
        if (IsMsx2Available) return ArtFlavor.Msx2;
        if (IsMsx1Available) return ArtFlavor.Msx1;
        return ArtFlavor.Msx2;
    }
}
