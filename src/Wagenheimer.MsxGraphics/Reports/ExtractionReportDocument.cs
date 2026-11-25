namespace Wagenheimer.MsxGraphics.Reports;

/// <summary>User-facing, renderer-independent summary. The host supplies all game knowledge.</summary>
public sealed class ExtractionReportDocument
{
    public string GameTitle { get; set; } = "MSX";
    public string Variant { get; set; } = "";
    public string RomName { get; set; } = "";
    public string RomSha1 { get; set; } = "";
    public string Outcome { get; set; } = "completed";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public double ElapsedSeconds { get; set; }
    public int StageCount { get; set; }
    public int ReferencedRoomCount { get; set; }
    public List<string> Notices { get; } = new();
    public List<ReportArtifact> Files { get; } = new();
    public List<ReportPicture> Pictures { get; } = new();
    public List<ReportSheet> Sheets { get; } = new();
    public List<ReportSound> Sounds { get; } = new();
    public List<ReportCheck> Checks { get; } = new();
}

public sealed record ReportArtifact(string Name, string Category, long Bytes, string Sha256, string Detail);
public sealed record ReportCheck(string Component, string State, string Detail);
public sealed record ReportPicture(string Name, string Category, int Width, int Height,
    string? PackedPixels = null, byte[]? PaletteRgba = null, string? SheetId = null, int X = 0, int Y = 0,
    int? VisiblePixels = null, string? Issue = null);
public sealed record ReportSheet(string Id, string PngDataUri);
public sealed record ReportSound(string Id, string Name, int? OriginalId, bool Music, double Seconds,
    bool Loop, double LoopStart, double LoopSeconds, string? PreviewPath, string Evidence);
