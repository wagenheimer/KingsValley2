#nullable enable
using System;
using System.IO;
using System.Threading;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class ExtractionSafetyTests
{
    [Fact]
    public void AudioBootstrap_Rejects_Msx2_Rom()
    {
        string msx2Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx2_scc.rom");

        if (!File.Exists(msx2Rom))
            return;

        bool started = AudioBootstrap.Start(msx2Rom, out string? error);
        Assert.False(started);
        Assert.NotNull(error);
        Assert.Contains("MSX1", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioBootstrap_Extracts_Msx1_Rom_Successfully()
    {
        string msx1Rom = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "roms", "kv2_msx1.rom");

        if (!File.Exists(msx1Rom))
            return;

        // If package and valid stages already exist, verify them directly
        string stage01Path = Path.Combine(RomContent.LevelsDirectory, "stage01.json");
        if (File.Exists(AudioBootstrap.ExtractedPackagePath) && File.Exists(stage01Path))
        {
            string json = File.ReadAllText(stage01Path);
            Assert.Contains("\"coffins\": [", json);
            Assert.DoesNotContain("\"playerStart\": {\n    \"room\": 1,\n    \"y\": 0,\n    \"x\": 0", json);
            return;
        }

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
        Assert.True(File.Exists(AudioBootstrap.ExtractedPackagePath), $"audio.msxaudio not created: status={AudioBootstrap.Status}");

        string jsonContent = File.ReadAllText(stage01Path);
        Assert.Contains("\"coffins\": [", jsonContent);
    }
}
