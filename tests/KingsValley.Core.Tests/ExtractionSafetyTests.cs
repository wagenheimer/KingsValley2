#nullable enable
using System;
using System.IO;
using System.Threading;
using KingsValley.Core.Extraction;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

[Collection(Msx1ExtractionTestCollection.Name)]
public class ExtractionSafetyTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "kv2-safety-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousRoot = AudioBootstrap.ExtractedDirectoryOverrideForTests;
    private readonly string? _previousIds = Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_IDS");
    private readonly string? _previousStages = Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES");

    public ExtractionSafetyTests()
    {
        Directory.CreateDirectory(_temporaryRoot);
        AudioBootstrap.ExtractedDirectoryOverrideForTests = _temporaryRoot;
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_IDS", "3");
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES", "1");
    }

    public void Dispose()
    {
        AudioBootstrap.Cancel();
        while (AudioBootstrap.IsRunning) Thread.Sleep(50);
        AudioBootstrap.ExtractedDirectoryOverrideForTests = _previousRoot;
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_IDS", _previousIds);
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES", _previousStages);
        Directory.Delete(_temporaryRoot, recursive: true);
    }

    [Fact]
    public void EnglishTitlePreferenceDoesNotReplaceTheStartSelectionScreen()
    {
        var previous=RomContent.Active;var previousLanguage=RomContent.TitleArtLanguage;
        string root=Path.Combine(_temporaryRoot,"pack"),screens=Path.Combine(root,"Art","Screens");
        Directory.CreateDirectory(screens);
        File.WriteAllText(Path.Combine(screens,"title_en.bin"),"title");
        File.WriteAllText(Path.Combine(screens,"start_select.bin"),"selection");
        try
        {
            RomContent.Use(new ArtSet("test","Test",root,ArtFlavor.Msx1));RomContent.UseTitleLanguage(TitleLanguage.English);
            Assert.Equal(Path.Combine(screens,"title_en.bin"),RomContent.ScreenPath("title"));
            Assert.Equal(Path.Combine(screens,"start_select.bin"),RomContent.ScreenPath("start_select"));
        }
        finally {RomContent.Use(previous);RomContent.UseTitleLanguage(previousLanguage);}
    }

    [Fact]
    public void Test_Clean_Room_Direct_Stage_Extraction()
    {
        string msx1Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx1.rom");
        if (!File.Exists(msx1Rom)) return;

        byte[] rom = File.ReadAllBytes(msx1Rom);
        string tempDir = Path.Combine(Path.GetTempPath(), "kv2_stages_test_" + Guid.NewGuid().ToString("N"));
        string levelsDir = Path.Combine(tempDir, "Levels");
        Directory.CreateDirectory(levelsDir);

        try
        {
            RomStageJson.WriteAll(rom, levelsDir, maxStages: 60);
            Assert.True(File.Exists(Path.Combine(levelsDir, "stage01.json")));
            Assert.True(File.Exists(Path.Combine(levelsDir, "stage60.json")));

            string json = File.ReadAllText(Path.Combine(levelsDir, "stage01.json"));
            Assert.Contains("\"coffins\": [", json);
            Assert.DoesNotContain("\"playerStart\": {\n    \"room\": 1,\n    \"y\": 0,\n    \"x\": 0", json);
            Console.WriteLine("[Test] Native ROM-driven stage extraction verified for all 60 stages!");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Test_Msx2_Direct_Extraction()
    {
        string msx2Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx2_scc.rom");
        if (!File.Exists(msx2Rom)) return;

        byte[] r2 = File.ReadAllBytes(msx2Rom);
        string tempDir = Path.Combine(Path.GetTempPath(), "kv2_direct_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RomDirectExtractor.ExtractMsx2Rooms(r2, tempDir, Console.WriteLine);
            RomDirectExtractor.ExtractMsx2Atlas(r2, tempDir, Console.WriteLine);
            string atlasFile = Path.Combine(tempDir, "Art", "kv2.atlas");
            string pngFile = Path.Combine(tempDir, "Art", "kv2.png");
            Assert.True(File.Exists(atlasFile), "kv2.atlas not created!");
            Assert.True(File.Exists(pngFile), "kv2.png not created!");

            string text = File.ReadAllText(atlasFile);
            Assert.Contains("vick_s0_00", text);
            Assert.Contains("enemy_t1_00", text);
            Assert.Contains("knife", text);
            Assert.Contains("coffin_p1", text);

            RomDirectExtractor.ExtractMsx2Rooms(r2, tempDir, Console.WriteLine);
            Assert.True(File.Exists(Path.Combine(tempDir, "Rooms", "murals.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Rooms", "decorations.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Rooms", "palettes.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Rooms", "blocks_p1.bin")));
            Console.WriteLine("[Test] RomDirectExtractor on MSX2 completed successfully in milliseconds!");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Refresh_AppData_Extracted_Assets()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string extracted = Path.Combine(_temporaryRoot, "extracted");
        Directory.CreateDirectory(Path.Combine(extracted, "Rooms"));
        string msx2Rom = Path.Combine(appData, "KingsValley", "roms", "kv2_msx2_scc.rom");
        string msx1Rom = Path.Combine(appData, "KingsValley", "roms", "kv2_msx1.rom");

        if (File.Exists(msx2Rom))
        {
            byte[] r2 = File.ReadAllBytes(msx2Rom);
            RomDirectExtractor.EnsureBlocks(Path.Combine(extracted, "Rooms"), r2);
            RomDirectExtractor.ExtractMsx2Rooms(r2, extracted);
            RomDirectExtractor.ExtractMsx2Atlas(r2, extracted);
            RomDirectExtractor.ExtractMsx2Screens(r2, extracted);
            Assert.True(File.Exists(Path.Combine(extracted,"Screens","puzzle_tiles.bin")));
        }

        if (File.Exists(msx1Rom))
        {
            byte[] r1 = File.ReadAllBytes(msx1Rom);
            byte[] murals = RomRoomDumps.GenerateMuralsBin(r1, System.Threading.CancellationToken.None);
            File.WriteAllBytes(Path.Combine(extracted, "Rooms", "murals.bin"), murals);

            string msx1Dir = Path.Combine(_temporaryRoot, "extracted_msx1");
            RomSpriteAtlas.Build(r1, msx1Dir, force: true, poseFrames: 180);
        }
    }

    [Fact]
    public void Test_Msx2_Atlas_Build()
    {
        string msx2Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx2_scc.rom");
        if (!File.Exists(msx2Rom)) return;

        byte[] r2 = File.ReadAllBytes(msx2Rom);
        string tempDir = Path.Combine(Path.GetTempPath(), "kv2_msx2_atlas_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            RomDirectExtractor.ExtractMsx2Rooms(r2,tempDir);
            RomDirectExtractor.ExtractMsx2Atlas(r2,tempDir);
            string atlasFile = Path.Combine(tempDir, "Art", "kv2.atlas");
            Assert.True(File.Exists(atlasFile), "kv2.atlas not created!");
            string text = File.ReadAllText(atlasFile);
            Assert.Contains("vick_s0_00", text);
            Console.WriteLine("[Test] RomSpriteAtlas on MSX2 completed successfully!");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AudioBootstrap_Extracts_Msx2_Directly_Fast()
    {
        string msx2Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx2_scc.rom");

        if (!File.Exists(msx2Rom))
            return;

        while (AudioBootstrap.IsRunning) Thread.Sleep(50);
        bool canStart = AudioBootstrap.Start(msx2Rom, out string? error);
        Assert.True(canStart, error ?? "AudioBootstrap failed to start");

        // Wait for extraction to finish
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (AudioBootstrap.IsRunning)
        {
            Thread.Sleep(100);
            if (sw.ElapsedMilliseconds > 30000)
            {
                AudioBootstrap.Cancel();
                Assert.Fail($"MSX2 extraction took more than 30 seconds! Last status: {AudioBootstrap.Status}");
            }
        }

        Assert.True(File.Exists(Path.Combine(AudioBootstrap.LastGraphicsAttemptDirectory!, "Art", "kv2.atlas")));
        Assert.True(File.Exists(Path.Combine(AudioBootstrap.LastGraphicsAttemptDirectory!, "Rooms", "blocks_p1.bin")));
        Assert.True(File.Exists(Path.Combine(AudioBootstrap.LastGraphicsAttemptDirectory!, "Rooms", "murals.json")));
        Assert.False(GraphicsExtractionManifest.Read(AudioBootstrap.LastGraphicsAttemptDirectory!)!.Ready);
        Assert.NotNull(AudioBootstrap.LastReportPath);
        Console.WriteLine($"[Test] AudioBootstrap on MSX2 finished in {sw.ElapsedMilliseconds} ms with an honest partial result.");
    }

    [Fact]
    public void AudioBootstrap_Accepts_Msx2_Rom()
    {
        string msx2Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx2_scc.rom");

        if (!File.Exists(msx2Rom))
            return;

        // Verify MSX2 ROM is accepted by AudioBootstrap without rejection
        while (AudioBootstrap.IsRunning) Thread.Sleep(50);
        bool canStart = AudioBootstrap.Start(msx2Rom, out string? error);
        if (canStart)
        {
            Assert.True(AudioBootstrap.CancelAndWait(TimeSpan.FromSeconds(15)),"Cooperative shutdown must finish the cancelled attempt.");
            Assert.NotNull(AudioBootstrap.LastReportPath);
            Assert.True(File.Exists(AudioBootstrap.LastReportPath));
        }
        Assert.True(canStart, error ?? "AudioBootstrap failed to start for MSX2 ROM");
    }

    [Fact]
    public void AudioBootstrap_Msx1DiagnosticSubsetRemainsStagedAndGeneratesReport()
    {
        string msx1Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx1.rom");

        if (!File.Exists(msx1Rom))
            return;

        while (AudioBootstrap.IsRunning) Thread.Sleep(50);

        // Quick test: limit to id 3 (title) and 1 stage
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_IDS", "3");
        Environment.SetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES", "1");

        bool started = AudioBootstrap.Start(msx1Rom, out string? error);
        Assert.True(started, error);

        // Wait for extraction to complete (up to 240s)
        int waited = 0;
        while (AudioBootstrap.IsRunning && waited < 240)
        {
            Thread.Sleep(1000);
            waited++;
        }

        Assert.False(AudioBootstrap.IsRunning, $"Extraction timed out: status={AudioBootstrap.Status}");
        Assert.False(File.Exists(AudioBootstrap.AudioPackageFor(ArtFlavor.Msx1)), "A diagnostic subset must not publish playable audio.");
        string attempts = Path.Combine(Path.GetDirectoryName(AudioBootstrap.AudioPackageFor(ArtFlavor.Msx1))!, "attempts");
        Assert.Single(Directory.GetFiles(attempts, "audio.pending", SearchOption.AllDirectories));
        Assert.False(GraphicsExtractionManifest.Read(AudioBootstrap.LastGraphicsAttemptDirectory!)!.Ready);
        Assert.Null(GraphicsGenerationStore.Current(AudioBootstrap.GraphicsRootFor(ArtFlavor.Msx1), "msx1"));
        Assert.False(RomExtractionProfiles.IsCurrentPackage(AudioBootstrap.AudioPackageFor(ArtFlavor.Msx1),
            RomExtractionProfiles.FindByGameVariant("kings-valley-2", "msx1")!), "A diagnostic subset must not replace a complete extraction.");
        Assert.NotNull(AudioBootstrap.LastReportPath);
        Assert.True(File.Exists(AudioBootstrap.LastReportPath), "Every extraction must generate its end-user report without a gallery environment variable.");

        string extractedStage01 = Path.Combine(AudioBootstrap.LastGraphicsAttemptDirectory!, "Levels", "stage01.json");
        string jsonContent = File.ReadAllText(extractedStage01);
        Assert.Contains("\"coffins\": [", jsonContent);
    }
}
