#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using KingsValley.Core.Gameplay;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;
using Wagenheimer.MsxAudio.Mixing;
using Wagenheimer.MsxAudio.MonoGame;
using Wagenheimer.MsxAudio.Songs;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Music and sound effects of the original game — **policy only**: which song belongs to which
/// moment of King's Valley II.
/// </summary>
/// <remarks>
/// <para><b>Source:</b> <c>Content/Rom/Audio/audio.msxaudio</c> — the VGMs (PSG/SCC register writes)
/// captured from openMSX, see <c>docs/ROM_EXTRACTION.md</c> §6. Playback is the package's
/// <see cref="MsxAudioHost"/> (chips, streaming, profiles, volumes); this class only maps the game's
/// ids and stage order onto it.</para>
///
/// <para><b>Mixing profiles</b> (<see cref="AudioProfile"/>): <c>original</c>, <c>stereo</c>,
/// <c>remaster</c>. Chosen with <c>KINGSVALLEY_AUDIO_PROFILE</c> or through the system menu.</para>
/// </remarks>
public static class GameAudio
{
    private const string PackageFile = "audio.msxaudio";

    /// <summary>Theme of each pair of stages (the original changes it every two stages).</summary>
    private static readonly string[] StageMusic = { "stage_a", "stage_b", "stage_c", "stage_d", "stage_e" };

    private static MsxAudioHost? _host;
    private static bool _initialized;

    /// <summary>Active mixing profile.</summary>
    public static AudioProfile Profile => _host?.Profile ?? AudioProfile.Original;

    /// <summary>Is music enabled (system menu)?</summary>
    public static bool MusicEnabled => _host?.MusicEnabled ?? false;

    /// <summary>Are sound effects enabled (system menu)?</summary>
    public static bool SoundEnabled => _host?.SoundEnabled ?? false;

    /// <summary>Opens the package (once).</summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
        var save = ProfileSaveService.Load();
        AudioProfile profile = AudioProfile.ByName(
            Environment.GetEnvironmentVariable("KINGSVALLEY_AUDIO_PROFILE") ?? save.AudioProfile);

        string? packagePath = AudioBootstrap.PackagePath;
        if (packagePath is null)
        {
            // No package yet: the first run extracts it from the player's ROM (AudioBootstrap).
            // Keep _initialized false so the audio starts as soon as the package appears.
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Console.WriteLine("[GameAudio] no audio package yet — waiting for the ROM extraction.");
            }

            return;
        }

        _initialized = true;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        _host = new MsxAudioHost(packagePath, profile)
        {
            MusicEnabled = save.MusicEnabled,
            SoundEnabled = save.SfxEnabled,
        };

        Console.WriteLine($"[GameAudio] PSG+SCC chips (profile {_host.Profile.Name}): " +
                          $"{_host.Library.Songs.Count} songs, {_host.Library.Clips.Count} effects " +
                          $"(ready in {watch.ElapsedMilliseconds} ms, from {Path.GetFileName(packagePath)}).");
    }

    private static bool _warnedMissing;

    /// <summary>Enables/disables music (system menu).</summary>
    public static void SetMusicEnabled(bool enabled)
    {
        Initialize();
        if (_host is not null) _host.MusicEnabled = enabled;
    }

    /// <summary>Enables/disables sound effects (system menu).</summary>
    public static void SetSoundEnabled(bool enabled)
    {
        Initialize();
        if (_host is not null) _host.SoundEnabled = enabled;
    }

    /// <summary>Keeps the stream fed. Call once per frame.</summary>
    public static void Update()
    {
        if (!_initialized)
        {
            // The first-run extraction finished (or a package appeared): start playing.
            if (AudioBootstrap.HasPackage)
                Initialize();
            else
                return;
        }

        _host?.Update();
    }

    /// <summary>Switches the mixing profile (resumes the current song).</summary>
    public static void SetProfile(string profile)
    {
        Initialize();
        _host?.SetProfile(profile);
        Console.WriteLine($"[GameAudio] audio profile: {Profile.Name}");
    }

    // ── Music ────────────────────────────────────────────────────────────────────

    /// <summary>Title music (plays once).</summary>
    public static void PlayTitle() => PlayMusic("title", loop: false);

    /// <summary>Menu/password/cutscene music.</summary>
    public static void PlayMenu() => PlayMusic("menu");

    /// <summary>World map BGM (<c>world_scr</c> 0x6F84 → <c>sfx_0d</c> = id 0x0D).</summary>
    public static void PlayWorldMap() => PlayMusic("music_13");

    /// <summary>Ending theme (<c>mode_end</c>).</summary>
    public static void PlayEnding() => PlayMusic("ending", loop: false);

    /// <summary>
    /// Background music for a stage (changes every 2 stages, as in the original).
    /// </summary>
    /// <remarks>
    /// 4926/4388: the original <b>always re-arms the stage music</b> — it sends silence (id 0x82) and
    /// then the theme — both after dying (4923/492C) and after clearing a stage. Without that, when
    /// the next stage uses the same theme (1→2, 3→4…), the music keeps playing from where it was.
    /// </remarks>
    public static void PlayStage(StageDefinition stage)
    {
        StopFallSounds();
        PlayMusic(StageMusic[stage.MusicIndex], restart: true);
    }

    public static void PlayMusic(string name, bool loop = true, bool restart = true)
    {
        Initialize();

        try
        {
            _host?.PlayMusic(name, loop, restart);
        }
        catch (Exception ex)
        {
            // A partial or mismatched package (e.g. a test extraction) must not kill the game: the id
            // simply is not there. Warn once per id and keep running without that track.
            WarnOnce($"music '{name}': {ex.Message}");
            return;
        }

        Console.WriteLine($"[GameAudio] music '{name}' (loop={loop}) playing={IsMusicPlaying}");
    }

    public static void StopMusic()
    {
        Initialize();
        _host?.StopMusic();
    }

    /// <summary>Is music still playing?</summary>
    public static bool IsMusicPlaying => _host?.IsMusicPlaying == true;

    // ── Effects and menu sounds ──────────────────────────────────────────────────

    /// <summary>Menu cursor / password key (id 50 of the original).</summary>
    public static void PlayCursor() => PlayEffect("password_key");

    /// <summary>Menu confirm (id 16 of the original).</summary>
    public static void PlayConfirm() => PlayEffect("menu_confirm");

    private static void PlayEffect(string name)
    {
        Initialize();

        try
        {
            _host?.PlayEffect(name);
        }
        catch (Exception ex)
        {
            WarnOnce($"effect '{name}': {ex.Message}");
        }
    }

    /// <summary>Warns about a missing id once (a partial package would otherwise flood the log).</summary>
    private static void WarnOnce(string message)
    {
        if (_warned.Add(message))
            Console.WriteLine($"[GameAudio] skipped {message}");
    }

    private static readonly HashSet<string> _warned = new();

    private static void StopEffect(string name) => _host?.StopEffect(name);

    /// <summary>Stops a sound effect by its original ROM routine ID.</summary>
    public static void StopSound(int originalId)
    {
        if (_host?.FindByOriginalId(originalId) is { } song)
            _host.StopEffect(song.Id);
    }

    /// <summary>
    /// Cuts the falling whistles for both player (0x3C / 'step_b') and enemies (0x2C / 'id_44').
    /// In the original PSG, landing, impact or new fall preempts the single effect channel immediately.
    /// </summary>
    public static void StopFallSounds()
    {
        _host?.StopEffect("step_b");
        _host?.StopEffect("player_fall");
        _host?.StopEffect("id_44");
        _host?.StopEffect("enemy_fall");
        StopSound(OriginalSounds.PlayerFall);
        StopSound(OriginalSounds.EnemyFall);
    }

    /// <summary>Stops all sound effects currently playing.</summary>
    public static void StopAllEffects()
    {
        Initialize();
        _host?.StopAllEffects();
    }

    /// <summary>
    /// The 19 sound-select keys, in the order of table <c>0xB98A</c> in bank 12 (pointers to the
    /// <c>sfx_</c> trampolines: id = (ptr - 0x41E0) / 5 + 1, confirmed by <c>sfx_1d</c> = 29 at 0x426C).
    /// </summary>
    private static readonly int[] SoundSelectIds =
    {
        3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 39, 40, 29,
    };

    /// <summary>
    /// **Sound test** entries (the <c>snd_sel</c> of the ending menu): the 19 piano keys, in ROM order.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name, bool Music)> SoundTestEntries()
    {
        Initialize();

        var entries = new List<(int, string, bool)>();
        foreach (int id in SoundSelectIds)
        {
            MsxAudioSong? song = _host?.FindByOriginalId(id);
            entries.Add((id, song?.Name ?? $"id_{id:00}", song?.Music ?? false));
        }

        return entries;
    }

    /// <summary>Plays one sound-select key (song loops, effect plays once).</summary>
    public static void PlaySoundTest(int id, string name, bool music)
    {
        if (!music)
            PlayEffect(name);
        else
            PlayMusic(name);
    }

    /// <summary>
    /// Plays the original sound carried by the event (id of routine 4326 in the ROM).
    /// </summary>
    /// <remarks>
    /// Entering the door (8FFD → 428A, id 0x23) plays a 9.7 s jingle that covers the whole exit
    /// sequence (266 frame units = 133 ticks ≈ 4.4 s, and keeps playing after it): it takes the
    /// place of the stage music, so the music stops first.
    /// </remarks>
    public static void Play(GameEvent e)
    {
        if (e.Sound == 0)
            return;

        Initialize();
        if (_host is null)
            return;

        if (e.Sound == OriginalSounds.DoorEnter)
        {
            StopFallSounds();
            _host.StopMusic();
        }

        // The fall whistle (0x3C for player, 0x2C for enemies) is a ~1.2-1.8 s sound. In the original the PSG has ONE channel per
        // effect: when landing, impact, death, or another fall plays, it takes the channel and cuts the
        // whistle immediately — on a short fall the original only plays its beginning. Without this the
        // whistle kept going after landing ("long sound on a short fall").
        if (e.Sound is OriginalSounds.PlayerLand or OriginalSounds.PlayerHit or OriginalSounds.PlayerDeathJingle
            or OriginalSounds.EnemyLanded or OriginalSounds.RockStop or OriginalSounds.EnemyHit or OriginalSounds.EnemyVanish
            or OriginalSounds.CoffinClose
            or OriginalSounds.PlayerFall or OriginalSounds.EnemyFall)
        {
            StopFallSounds();
        }

        if (!_host.PlayEffectByOriginalId(e.Sound))
            Console.WriteLine($"[GameAudio] sound {e.Sound} (0x{e.Sound:X2}) not extracted");
    }

    /// <summary>Releases the host (shutdown).</summary>
    public static void Unload()
    {
        _host?.Dispose();
        _host = null;
        _initialized = false;
    }
}
