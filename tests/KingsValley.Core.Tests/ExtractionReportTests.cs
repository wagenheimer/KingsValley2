using Wagenheimer.MsxGraphics.Reports;

namespace KingsValley.Core.Tests;

public sealed class ExtractionReportTests
{
    [Fact]
    public void ReportIsOfflineEscapesNamesAndUsesNativeDisclosureAndMedia()
    {
        var report = new ExtractionReportDocument { GameTitle = "<script>alert(1)</script>", Variant = "msx1" };
        report.Notices.Add("File <incomplete>");
        report.Sounds.Add(new("jump", "Jump", 19, false, 0.46, false, 0, 0, "media/jump.wav", "driver-stopped"));
        string html = HtmlExtractionReport.Render(report);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("<details", html);
        Assert.Contains("<audio controls", html);
        Assert.Contains("00:01", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("fetch(", html);
        Assert.DoesNotContain("autoplay", html);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("partial")]
    public void PartialExtractionNeverClaimsEverythingIsReady(string outcome)
    {
        var report = new ExtractionReportDocument { Outcome = outcome };
        report.Notices.Add("Screens are not yet available.");
        string html = System.Net.WebUtility.HtmlDecode(HtmlExtractionReport.Render(report));
        Assert.Contains("Extraction incomplete", html);
        Assert.Contains("Screens are not yet available", html);
        Assert.DoesNotContain("Everything is ready", html);
    }

    [Fact]
    public void GraphicsSearchAndVisiblePixelEvidenceExplainBlankPreviews()
    {
        var report=new ExtractionReportDocument();
        report.Pictures.Add(new("empty","Sprites",16,16,VisiblePixels:0,Issue:"Fully transparent sprite; review required"));
        report.Checks.Add(new("atlas","missing-or-invalid","Missing native cursor"));
        string html=HtmlExtractionReport.Render(report);
        Assert.Contains("Search graphics",html); Assert.Contains("Needs review",html);
        Assert.Contains("0 visible pixels",html); Assert.Contains("Fully transparent sprite",html);
        Assert.Contains("Validation details",html); Assert.Contains("Missing native cursor",html);
    }

    [Fact]
    public void UntrustedAudioLinkCannotBecomeAnExecutableUrl()
    {
        var report = new ExtractionReportDocument();
        report.Sounds.Add(new("id", "Sound", 1, false, 1, false, 0, 0, "javascript:alert(1)", ""));
        Assert.DoesNotContain("src=\"javascript:", HtmlExtractionReport.Render(report));
    }

    [Fact]
    public void ReportLanguageAndNumberFormattingRemainEnglishOnPortugueseSystems()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            string html = HtmlExtractionReport.Render(new ExtractionReportDocument());
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains("Extraction report", html);
            Assert.Contains("Search the catalog", html);
            Assert.Equal("1.5 MB", HtmlExtractionReport.Size(1572864));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
