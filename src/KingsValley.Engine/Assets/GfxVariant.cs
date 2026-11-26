#nullable enable
using System;
using System.Linq;
using KingsValley.Core.SaveSystem;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.Scenes;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Chooses and switches the **art set** (original MSX2, original MSX1 or a remaster), at runtime and
/// persisted in the save.
///
/// Each set lives in its own folder (<see cref="ArtSets"/>): the originals in <c>Content/Rom</c> and
/// <c>Content/RomMsx1</c>, and a remaster in <c>Content/ArtSets/&lt;id&gt;/</c> — creating the folder
/// (and the <c>artset.json</c>) is enough for it to appear in the menu, without touching code. Switching the set unloads
/// the global atlases/textures and **recreates the current scene**, so everything is read from the right folder.
/// Stages and audio resolve through the active cartridge variant. Graphics cycling stays within its campaign.
///
/// Precedence at initialization: <c>KINGSVALLEY_GFX=&lt;id&gt;</c> (diagnostic) > save.
/// Key <c>F11</c> goes to the next set (<c>F2</c> is the Sound Test of the debug shortcuts).
/// </summary>
public static class GfxVariant
{
    /// <summary>Active art set.</summary>
    public static ArtSet Current => RomContent.Active;

    /// <summary>Available sets, in cycle order.</summary>
    public static System.Collections.Generic.IReadOnlyList<ArtSet> Available => RomContent.Available;

    /// <summary>Reads the set from the environment/save and prepares the paths. Call once at boot.</summary>
    public static void Initialize()
    {
        RomContent.Initialize();

        string? env = Environment.GetEnvironmentVariable("KINGSVALLEY_GFX");
        string? saved = ProfileSaveService.Load().GfxRom;
        string chosen = string.IsNullOrWhiteSpace(env) ? saved : env;

        ArtSet? target = ArtSets.Find(RomContent.Available, chosen);
        if (target != null && !RomContent.IsVariantAvailable(target.Variant) && RomContent.CanPlayAny)
        {
            var fallback = RomContent.GetFirstPlayableFlavor();
            target = ArtSets.Find(RomContent.Available, fallback == ArtFlavor.Msx1 ? ArtSets.Msx1Id : ArtSets.Msx2Id) ?? target;
        }

        if (target != null)
            RomContent.Use(target);

        Console.WriteLine($"[GfxVariant] art: {Current.Name} ({Current.Id}, {Current.Flavor}, root {Current.Root})");
        Console.WriteLine($"[GfxVariant] sets: {string.Join(", ", Available.Select(s => $"{s.Id}={s.Name}"))}");
    }

    /// <summary>Goes to the next available set (F11 and the GRAPHICS line of the menu) with in-place hot-reload.</summary>
    public static void Toggle() => Cycle(+1, reloadScene: false);

    /// <summary>Walks the sets in the requested direction (cycles at the ends).</summary>
    /// <param name="direction">+1 for next, -1 for previous.</param>
    /// <param name="reloadScene">Force recreation of the current scene? (default false: hot-reload in place).</param>
    public static void Cycle(int direction, bool reloadScene = false)
    {
        // Only cycle through flavors that are currently extracted and available
        var playableSets = RomContent.Available.Where(s => s.CampaignId==Current.CampaignId && RomContent.IsVariantAvailable(s.Variant)).ToList();
        if (playableSets.Count <= 1)
            return;

        int index = playableSets.FindIndex(s => s.Id == Current.Id);
        if (index < 0) index = 0;
        int next = ((index + direction) % playableSets.Count + playableSets.Count) % playableSets.Count;

        Set(playableSets[next].Id, reloadScene);
    }

    /// <summary>Defines the set (saved in the profile; in-place hot-reload by default).</summary>
    public static void Set(string id, bool reloadScene = false)
    {
        ArtSet? target = ArtSets.Find(RomContent.Available, id);
        if (target is null || target.Id == Current.Id)
            return;

        if (!RomContent.IsVariantAvailable(target.Variant))
        {
            Console.WriteLine($"[GfxVariant] Flavor {target.Flavor} ({target.Name}) is not extracted. Redirecting to extraction screen.");
            Nez.Core.Scene = new FirstRunScene($"{target.Name.ToUpperInvariant()} GRAPHICS REQUIRE EXTRACTION - INSERT {target.Name.ToUpperInvariant()} ROM");
            return;
        }

        var previous=Current;
        RomContent.Use(target);

        var save = ProfileSaveService.Load();
        if(previous.CampaignId==Core.Common.Campaigns.Original)save.OriginalGfxRom=previous.Id;
        save.GfxRom = target.Id;
        if(target.CampaignId==Core.Common.Campaigns.Original)save.OriginalGfxRom=target.Id;
        ProfileSaveService.Save(save);

        // Texturas/atlases globais foram carregados da pasta antiga.
        GameAssets.Unload();
        MsxScreen.Reset();

        Console.WriteLine($"[GfxVariant] art: {Current.Name} ({Current.Id}, root {Current.Root})");

        if (reloadScene)
            ReloadScene();
        else
            (Nez.Core.Scene as SceneBase)?.OnGfxVariantChanged();
    }

    private static int IndexOf(ArtSet set)
    {
        var sets = RomContent.Available;
        for (int i = 0; i < sets.Count; i++)
        {
            if (string.Equals(sets[i].Id, set.Id, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    /// <summary>Recreates the current scene with the new art (keeps the stage in play, if any).</summary>
    private static void ReloadScene()
    {
        switch (Nez.Core.Scene)
        {
            case GameScene game:
                GameNavigation.GoToGame(game.Run);
                break;
            case MainMenuScene:
                GameNavigation.GoTo(GameSceneId.MainMenu);
                break;
            case SplashScene:
                GameNavigation.GoTo(GameSceneId.Splash);
                break;
            case WorldMapScene:
                GameNavigation.GoToIntro(Core.Gameplay.GameRun.New(0));
                break;
            default:
                GameNavigation.GoTo(GameSceneId.MainMenu);
                break;
        }
    }
}
