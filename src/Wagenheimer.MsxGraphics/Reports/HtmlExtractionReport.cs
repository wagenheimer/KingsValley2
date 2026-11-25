using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Wagenheimer.MsxGraphics.Reports;

/// <summary>Offline HTML presentation. No extraction, emulator, game or graphics-device dependency.</summary>
public static class HtmlExtractionReport
{
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    public static string Duration(double seconds)
    {
        long total = double.IsFinite(seconds) && seconds > 0 ? Math.Max(1, (long)seconds) : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }
    public static string Size(long bytes) => bytes >= 1048576 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576d:F1} MB") : bytes >= 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:F1} KB") : $"{bytes} B";

    public static string Render(ExtractionReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string status = report.Outcome switch { "failed" => "Extraction incomplete", "partial" => "Extraction incomplete", "cancelled" => "Extraction interrupted", "existing" => "Available content report", _ => report.Notices.Count > 0 ? "Completed with observations" : "Extraction complete" };
        var content = new StringBuilder();
        void Add(string html) => content.Append(html);
        Add($"<header class=hero><div><p class=eyebrow>CARTRIDGE ARCHIVE · {H(report.Variant.ToUpperInvariant())}</p><h1>Your ROM.<br>In every detail.</h1><p class=lede>Explore the content found in your extraction of <strong>{H(report.GameTitle)}</strong>.</p><div class=status>{H(status)}</div></div><aside class=identity><span class=edition>{H(report.Variant.ToUpperInvariant())}</span><p>Source cartridge</p><strong>{H(report.RomName)}</strong><p><time datetime=\"{report.GeneratedAt:O}\">{H(report.GeneratedAt.ToString("MMM d, yyyy · HH:mm zzz", CultureInfo.GetCultureInfo("en-US")))}</time></p><small>Generated locally. Available offline.</small></aside></header>");
        Add("<section class=metrics aria-label=\"Library summary\">");
        void Metric(string value, string label) => Add($"<article><strong>{H(value)}</strong><span>{H(label)}</span></article>");
        Metric(report.Files.Count.ToString(), "content files");
        Metric(report.Pictures.Count.ToString(), "graphics previews");
        Metric(report.Sounds.Count(s => s.Music).ToString(), "music tracks");
        Metric(report.Sounds.Count(s => !s.Music).ToString(), "sound effects");
        Add("</section>");
        if (report.Notices.Count > 0)
        {
            Add("<section class=notices aria-labelledby=notice-title><h2 id=notice-title>What you should know</h2><ul>");
            foreach (string notice in report.Notices) Add($"<li>{H(notice)}</li>");
            Add("</ul></section>");
        }
        Add($"<section class=overview id=overview><div><p class=eyebrow>01 / OVERVIEW</p><h2>Your content, organized.</h2></div><dl><div><dt>Stages found</dt><dd>{report.StageCount}</dd></div><div><dt>Rooms referenced by stages</dt><dd>{report.ReferencedRoomCount}</dd></div><div><dt>Content size</dt><dd>{H(Size(report.Files.Sum(f => f.Bytes)))}</dd></div><div><dt>Run duration</dt><dd>{Duration(report.ElapsedSeconds)}</dd></div></dl></section>");
        if(report.Checks.Count>0)
        {
            Add("<details class=file-list><summary>Validation details <span>Structural checks and missing components</span></summary><div class=table-scroll><table><thead><tr><th scope=col>Component</th><th scope=col>Status</th><th scope=col>Evidence</th></tr></thead><tbody>");
            foreach(var check in report.Checks) Add($"<tr><th scope=row>{H(check.Component)}</th><td>{H(check.State=="valid"?"Structurally valid":check.State=="partial"?"Partial":check.State=="unsupported"?"Unsupported":"Needs attention")}</td><td>{H(check.Detail)}</td></tr>");
            Add("</tbody></table></div></details>");
        }
        Add("<section id=graphics><div class=section-title><div><p class=eyebrow>02 / GRAPHICS</p><h2>Take a closer look.</h2></div><p>Screens, scenery, characters and objects.<br>Pixels retain their original resolution.</p></div>");
        if (report.Pictures.Count == 0) Add("<p class=empty>No graphics previews are available for this run. Files and observations are still listed below.</p>");
        Add("<search class=audio-search><label for=graphics-query>Search graphics<input id=graphics-query type=search placeholder=\"Sprite or screen name\"></label><div class=field><label for=graphics-kind>Inspect</label><select id=graphics-kind><option value=all>All graphics</option><option value=review>Needs review</option></select></div><p id=graphics-count aria-live=polite></p></search>");
        foreach (var group in report.Pictures.Select((picture, index) => (picture, index)).GroupBy(p => p.picture.Category))
        {
            Add($"<details class=gallery-group><summary>{H(group.Key)} <span>{group.Count()} previews</span></summary><div class=gallery>");
            foreach (var (picture, index) in group)
                Add($"<figure data-wide=\"{(picture.Width >= 128 ? "true" : "false")}\" data-search=\"{H(picture.Name)}\" data-issue=\"{(picture.Issue == null ? "false" : "true")}\"><div class=pixel-stage><canvas width=\"{picture.Width}\" height=\"{picture.Height}\" data-preview=\"{index}\" role=img aria-label=\"{H(picture.Name)}\">{H(picture.Name)}</canvas></div><figcaption><strong>{H(picture.Name)}</strong><span>{picture.Width} &times; {picture.Height} px{(picture.VisiblePixels.HasValue ? " / " + picture.VisiblePixels + " visible pixels" : "")}</span>{(picture.Issue == null ? "" : "<span class=preview-issue>" + H(picture.Issue) + "</span>")}</figcaption></figure>");
            Add("</div></details>");
        }
        Add("<noscript><p>Enable JavaScript to draw previews from binary data. The summary, file inventory and audio controls work without it.</p></noscript></section>");
        Add("<section id=audio><div class=section-title><div><p class=eyebrow>03 / AUDIO</p><h2>The sound of the cartridge.</h2></div><p>Local previews, ready to play.<br>Your original audio package is preserved.</p></div><search class=audio-search><label for=sound-query>Search the catalog<input id=sound-query type=search placeholder=\"Sound name or number\"></label><div class=field><label for=sound-kind>Show</label><select id=sound-kind><option value=all>All sounds</option><option value=music>Music</option><option value=sfx>Sound effects</option></select></div><p id=sound-count aria-live=polite></p></search><div class=sounds>");
        foreach (var sound in report.Sounds)
        {
            Add($"<article class=sound data-kind=\"{(sound.Music ? "music" : "sfx")}\" data-search=\"{H(sound.Name + " " + sound.Id + " " + sound.OriginalId)}\"><div class=sound-heading><span class=track-id>{sound.OriginalId?.ToString("00") ?? "—"}</span><div><h3>{H(sound.Name)}</h3><span>{(sound.Music ? "Music" : "Sound effect")} · {Duration(sound.Seconds)}{(sound.Loop ? " · Repeating" : " · Plays once")}</span></div></div>");
            if (SafeMediaPath(sound.PreviewPath))
                Add($"<audio controls preload=none src=\"{H(sound.PreviewPath)}\" aria-label=\"Listen to {H(sound.Name)}\"></audio>");
            else Add("<p class=media-unavailable>Preview unavailable. This item remains listed in the catalog.</p>");
            Add($"<details class=sound-info><summary>Sound details</summary><dl><div><dt>Identifier</dt><dd>{H(sound.Id)}</dd></div><div><dt>Exact capture duration</dt><dd>{sound.Seconds.ToString("0.000", CultureInfo.InvariantCulture)} s</dd></div>");
            if (sound.Loop) Add($"<div><dt>Intro / cycle</dt><dd>{sound.LoopStart.ToString("0.000", CultureInfo.InvariantCulture)} s / {sound.LoopSeconds.ToString("0.000", CultureInfo.InvariantCulture)} s</dd></div>");
            Add($"<div><dt>Source and verification</dt><dd>{H(sound.Evidence)}</dd></div></dl></details></article>");
        }
        Add("</div></section><section id=files><div class=section-title><div><p class=eyebrow>04 / INVENTORY</p><h2>Every file counts.</h2></div><p>Content found after this run.<br>Expand the details to inspect file integrity.</p></div><details class=file-list><summary>Browse all files</summary><div class=table-scroll tabindex=0 role=region aria-label=\"File inventory\"><table><thead><tr><th scope=col>File</th><th scope=col>Category</th><th scope=col>Size</th><th scope=col>Details</th></tr></thead><tbody>");
        foreach (var file in report.Files)
            Add($"<tr><th scope=row>{H(file.Name)}</th><td>{H(file.Category)}</td><td class=nowrap>{H(Size(file.Bytes))}</td><td><details><summary>{H(file.Detail.Length > 0 ? file.Detail : "File integrity")}</summary><p class=hash>SHA-256 {H(file.Sha256)}</p></details></td></tr>");
        Add($"</tbody></table></div></details></section><section id=details class=technical><p class=eyebrow>05 / ABOUT THIS EXTRACTION</p><h2>Transparent origins.</h2><p>This report is a snapshot of the content available after the run. A visible preview or an existing file does not, by itself, establish fidelity to the cartridge.</p><details><summary>View technical identification</summary><dl><div><dt>Selected version</dt><dd>{H(report.Variant)}</dd></div><div><dt>ROM</dt><dd>{H(report.RomName)}</dd></div><div><dt>ROM SHA-1</dt><dd class=hash>{H(report.RomSha1)}</dd></div><div><dt>Audio previews</dt><dd>WAV · 16-bit PCM · 22.05 kHz · stereo. Repeating sounds preview one cycle.</dd></div></dl></details></section>");
        using var stream = typeof(HtmlExtractionReport).Assembly.GetManifestResourceStream("MsxGraphics.Report.html")!;
        using var reader = new StreamReader(stream);
        // JSON's default encoder escapes '<', preventing data from terminating the script element.
        string json = JsonSerializer.Serialize(new { Pictures = report.Pictures, Sheets = report.Sheets });
        return reader.ReadToEnd().Replace("@@TITLE@@", H(report.GameTitle + " · Extraction report"))
            .Replace("@@CONTENT@@", content.ToString()).Replace("@@DATA@@", json);
    }

    private static bool SafeMediaPath(string? path) => path is not null && path.StartsWith("media/", StringComparison.Ordinal) &&
        !path.Contains("..", StringComparison.Ordinal) && !path.Any(c => c is ':' or '\\' or '"' or '<' or '>') && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
}
