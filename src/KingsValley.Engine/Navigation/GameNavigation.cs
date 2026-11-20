#nullable enable
using System;
using System.Collections.Generic;
using Nez;
using KingsValley.Core.Stages;
using KingsValley.Engine.Scenes;

namespace KingsValley.Engine.Navigation;

/// <summary>Identifier of each game scene.</summary>
public enum GameSceneId
{
    /// <summary>Opening (logo with fade in/out).</summary>
    Splash,

    /// <summary>Menu principal.</summary>
    MainMenu,

    /// <summary>Carregamento fatiado.</summary>
    Loading,

    /// <summary>The game room (platform/puzzle).</summary>
    Level,

    /// <summary>Interlude between pyramids (the original's <c>mode_world</c>).</summary>
    WorldMap,

    /// <summary>Sound test (o <c>snd_sel</c> do menu final).</summary>
    SoundTest,

    /// <summary>End of the game (<c>mode_end</c>) and the final menu.</summary>
    Ending,

    /// <summary>Puzzle game of the final menu (page B of <c>end_menu</c>).</summary>
    Puzzle,

    /// <summary>Stage editor (the port's last item).</summary>
    Editor,

    /// <summary>Game over + continue (o <c>mode_over</c> do original).</summary>
    GameOver,
}

/// <summary>
/// **SINGLE point of scene switching.** Never instantiate scenes scattered around the code.
///
/// In Nez, assigning <c>Core.Scene</c> when there is already an active scene performs a **deferred switch**
/// (applied at the end of the frame), which avoids destroying the current scene in the middle of its own <c>Update</c>.
/// </summary>
public static class GameNavigation
{
    /// <summary>Currently active scene.</summary>
    public static Scene? Current => Nez.Core.Scene;

    /// <summary>Identifier of the active scene (derived from the type).</summary>
    public static GameSceneId? CurrentId => Current switch
    {
        SplashScene => GameSceneId.Splash,
        MainMenuScene => GameSceneId.MainMenu,
        LoadingScene => GameSceneId.Loading,
        GameScene => GameSceneId.Level,
        WorldMapScene => GameSceneId.WorldMap,
        SoundTestScene => GameSceneId.SoundTest,
        EndingScene => GameSceneId.Ending,
        PuzzleScene => GameSceneId.Puzzle,
        EditorScene => GameSceneId.Editor,
        GameOverScene => GameSceneId.GameOver,
        _ => null,
    };

    /// <summary>Goes to the given scene.</summary>
    public static void GoTo(GameSceneId id) => Nez.Core.Scene = CreateScene(id);

    /// <summary>Loads a scene through the loading screen (with sliced steps).</summary>
    public static void GoToLoading(GameSceneId target, IEnumerable<LoadStep>? steps = null, float minimumDuration = 0.9f)
        => Nez.Core.Scene = new LoadingScene(target, steps, minimumDuration);

    /// <summary>Starts a run (menu: NORMAL or password).</summary>
    public static void GoToGame(KingsValley.Core.Gameplay.GameRun run) => Nez.Core.Scene = new GameScene(run);

    /// <summary>Starts an Attract Mode gameplay demo.</summary>
    public static void GoToDemo(int stage = 1) =>
        Nez.Core.Scene = new GameScene(KingsValley.Core.Gameplay.GameRun.New(0, stage), isDemo: true);

    /// <summary>
    /// Opening cutscene: before the first stage the original shows Vick in front of the pyramid
    /// ("DO YOUR BEST !!" on the map) — same art/screen as the world, with the encouragement text.
    /// </summary>
    public static void GoToIntro(KingsValley.Core.Gameplay.GameRun run) =>
        Nez.Core.Scene = new WorldMapScene(run, intro: true);

    /// <summary>Interlude between pyramids (<c>mode_world</c> of the original) and then the run.</summary>
    public static void GoToWorldMap(KingsValley.Core.Gameplay.GameRun run) => Nez.Core.Scene = new WorldMapScene(run);

    /// <summary>Game over (<c>mode_over</c>): clean screen, "game  over" and the <c>F5</c> continue.</summary>
    public static void GoToGameOver(KingsValley.Core.Gameplay.GameRun run) => Nez.Core.Scene = new GameOverScene(run);

    /// <summary>End of the game (<c>mode_end</c>) with the final score.</summary>
    public static void GoToEnding(KingsValley.Core.Gameplay.GameRun run, int pyramid = 1) =>
        Nez.Core.Scene = new EndingScene(run, pyramid);

    /// <summary>
    /// Stage in memory for the **editor play-test**: when != null, <see cref="GameScene"/> uses
    /// this stage instead of reading <c>Content/Rom/Levels/stageNN.json</c>. The editor turns it on/off.
    /// </summary>
    public static StageDefinition? StageOverride { get; set; }

    /// <summary>Instantiates the scene for the identifier.</summary>
    public static Scene CreateScene(GameSceneId id) => id switch
    {
        GameSceneId.Splash => new SplashScene(),
        GameSceneId.MainMenu => new MainMenuScene(),
        GameSceneId.Loading => new LoadingScene(),
        GameSceneId.Level => new GameScene(),
        GameSceneId.WorldMap => new WorldMapScene(Core.Gameplay.GameRun.New(0)),
        GameSceneId.SoundTest => new SoundTestScene(),
        GameSceneId.Ending => new EndingScene(Core.Gameplay.GameRun.New(0)),
        GameSceneId.Puzzle => new PuzzleScene(),
        GameSceneId.Editor => new EditorScene(),
        GameSceneId.GameOver => new GameOverScene(KingsValley.Core.Gameplay.GameRun.New(0)),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Cena desconhecida."),
    };
}
