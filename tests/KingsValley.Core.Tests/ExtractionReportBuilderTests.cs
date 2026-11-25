using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Songs;

namespace KingsValley.Core.Tests;

public sealed class ExtractionReportBuilderTests
{
    [Fact]
    public void PackedMsx1MechanismsUseNativePaletteAndHaveOfflinePreviews()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-mechanism-report-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"graphics","Rooms"));
            byte[] pixels=new byte[128]; Array.Fill(pixels,(byte)0x51);
            File.WriteAllBytes(Path.Combine(root,"graphics","Rooms","mechanism_t4.bin"),pixels);
            ExtractionReportBuilder.Build(new(Path.Combine(root,"graphics"),null,Path.Combine(root,"report"),"msx1.rom","abc","msx1","partial",1));
            using var report=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"report","report.json")));
            var picture=Assert.Single(report.RootElement.GetProperty("Pictures").EnumerateArray());
            Assert.Equal(32,picture.GetProperty("Width").GetInt32());
            Assert.Equal(8,picture.GetProperty("Height").GetInt32());
            Assert.Equal("Mechanisms",picture.GetProperty("Category").GetString());
            var palette=picture.GetProperty("PaletteRgba").GetBytesFromBase64();
            Assert.Equal(new byte[]{125,118,252,255},palette.AsSpan(5*4,4).ToArray());
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
    [Fact]
    public void BuildsAnOfflineReportFromBinaryScreensWithoutExportingBackgroundPngs()
    {
        string root = Path.Combine(Path.GetTempPath(), "kv2-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "graphics", "Screens"));
            File.WriteAllBytes(Path.Combine(root, "graphics", "Screens", "title.bin"),
                ScreenAssembler.Encode(2, 2, new ScreenColor[16], new byte[] { 1, 2, 3, 4 }));
            var request = new ExtractionReportRequest(Path.Combine(root, "graphics"), null, Path.Combine(root, "report"),
                "cartridge.rom", "abc", "msx1", "completed", 2.5);
            string report = ExtractionReportBuilder.Build(request);
            Assert.True(File.Exists(report));
            Assert.True(File.Exists(Path.Combine(root, "report", "report.json")));
            string html = File.ReadAllText(report);
            Assert.Contains("title", html);
            Assert.Contains("data-preview", html);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "report"), "*.png", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void APackageFromTheOtherCartridgeIsReportedInsteadOfBorrowed()
    {
        string root = Path.Combine(Path.GetTempPath(), "kv2-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            using var audio = new MsxAudioLibrary("wrong variant");
            audio.Manifest.Identity = new() { GameId = "kings-valley-2", Variant = "msx2", RomSha1 = "wrong" };
            string package = Path.Combine(root, "audio.msxaudio"); audio.Save(package);
            string report = ExtractionReportBuilder.Build(new(root, package, Path.Combine(root, "report"),
                "msx1.rom", "abc", "msx1", "failed", 1));
            string html = System.Net.WebUtility.HtmlDecode(File.ReadAllText(report));
            Assert.Contains("does not match", html);
            Assert.Contains("Extraction incomplete", html);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
