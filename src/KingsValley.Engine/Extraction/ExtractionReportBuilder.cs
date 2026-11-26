#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KingsValley.Core.Screens;
using KingsValley.Core.Stages;
using Wagenheimer.MsxAudio.Mixing;
using Wagenheimer.MsxAudio.Songs;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Reports;

namespace KingsValley.Engine.Extraction;

public sealed record ExtractionReportRequest(string GraphicsDirectory, string? AudioPackagePath,
    string ReportDirectory, string RomName, string RomSha1, string Variant, string Outcome,
    double ElapsedSeconds, string? Failure = null, string? PreparationNotice = null);

/// <summary>Collects game-specific evidence. Presentation is supplied by the reusable graphics library.</summary>
public static class ExtractionReportBuilder
{
    public static string Build(ExtractionReportRequest request, Action<string>? log = null)
    {
        var report = new ExtractionReportDocument
        {
            GameTitle = "King's Valley II", Variant = request.Variant, RomName = request.RomName,
            RomSha1 = request.RomSha1, Outcome = request.Outcome, ElapsedSeconds = request.ElapsedSeconds
        };
        Directory.CreateDirectory(request.ReportDirectory);
        var sourceRevision = SupportedCartridges.All.FirstOrDefault(entry => entry.Sha1.Equals(request.RomSha1, StringComparison.OrdinalIgnoreCase));
        if (sourceRevision?.SourceWarning != null) report.Notices.Add("Source cartridge limitation: " + sourceRevision.SourceWarning);
        if (request.PreparationNotice != null) report.Notices.Add(request.PreparationNotice);
        if(request.Variant==Assets.CartridgeVariants.Contest)
            report.Notices.Add("Contest contains four publications with five independent author challenges each. Its native publication, author and SCREEN SELECT menus are extracted as thirty binary frames, including six visual themes. The native CONGRATULATION screen follows door entry and the closing curtain, preserves the live score HUD, and returns to publication selection after 360 logic calls. The framed artwork is the edition title, not a separate winner certificate.");
        if(request.Variant=="msx2")report.Notices.Add(request.RomSha1.Equals("89d96d4b92d9a1c220cae0240cf0c00f310d14aa",StringComparison.OrdinalIgnoreCase)
            ? "The FRS MSX2 revision supplies its own English title artwork. The native title is extracted from this cartridge; no MSX1 title is borrowed."
            : "This MSX2 revision supplies its native title artwork. Optional title-language selection uses only the screens available in the current cartridge generation.");
        if (request.Outcome == "existing") report.Notices.Add("This report was rebuilt from available files. No new extraction was performed to generate this view.");
        if (!string.IsNullOrWhiteSpace(request.Failure)) report.Notices.Add("The run did not finish: " + request.Failure);
        var manifest = GraphicsExtractionManifest.Read(request.GraphicsDirectory);
        if (manifest == null || manifest.Variant != request.Variant || manifest.RomSha1 != request.RomSha1)
            report.Notices.Add("These graphics have no matching cartridge manifest. Their individual origin is unverified; legacy cache files are shown only for inspection.");
        else
        {
            report.Notices.Add("Graphics were produced in a cartridge-specific generation. Structural checks do not establish pixel fidelity against the original.");
            foreach (var check in manifest.Checks) report.Checks.Add(new(check.Component,check.State,check.Detail));
            int unresolved=manifest.Checks.Count(c=>c.State!="valid");
            if(unresolved>0) report.Notices.Add($"{unresolved} graphics checks need attention. Expand the validation details below for the complete inventory.");
            if (!manifest.Ready) report.Notices.Add("Incomplete graphics were not published as a playable generation.");
        }
        CollectGraphics(request.GraphicsDirectory, report);
        if(manifest!=null && manifest.Variant==request.Variant && manifest.RomSha1==request.RomSha1)
        {
            var files=report.Files.ToDictionary(f=>f.Name,StringComparer.Ordinal);
            foreach(var artifact in manifest.Artifacts)
                if(!files.TryGetValue(artifact.Path,out var file) || file.Bytes!=artifact.Bytes || !string.Equals(file.Sha256,artifact.Sha256,StringComparison.OrdinalIgnoreCase))
                    report.Checks.Add(new("integrity-"+artifact.Path,"invalid","The file is missing or differs from its generation hash."));
            if(report.Checks.Any(c=>c.Component.StartsWith("integrity-",StringComparison.Ordinal)))
                report.Notices.Add("Some files changed after this generation was created. See the integrity checks below; the snapshot's original validation no longer establishes their current integrity.");
        }
        CollectAudio(request, report, log);
        string html = HtmlExtractionReport.Render(report);
        AtomicWrite(Path.Combine(request.ReportDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        string path = Path.Combine(request.ReportDirectory, "index.html");
        AtomicWrite(path, html);
        log?.Invoke($"report: {report.Pictures.Count} previews, {report.Sounds.Count} sounds, {report.Files.Count} files → {path}");
        return path;
    }

    private static void CollectGraphics(string root, ExtractionReportDocument report)
    {
        string[] groups = { "Levels", "Rooms", "Screens", "Art", "ArtMsx1" };
        int sheetId = 0;
        foreach (string group in groups)
        {
            string folder = Path.Combine(root, group);
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".bin" or ".json" or ".atlas" or ".png" or ".generated")) continue;
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                string detail = "Individual graphics provenance not yet verified";
                try
                {
                    if (extension == ".bin" && (group == "Screens" || relative.StartsWith("Rooms/dumps/", StringComparison.Ordinal) || relative.StartsWith("Art/Native/",StringComparison.Ordinal)))
                    {
                        ScreenData screen = ScreenAssembler.DecodeFile(file);
                        byte[] rgba = screen.Palette.SelectMany(c => new[] { c.R, c.G, c.B, c.A }).ToArray();
                        report.Pictures.Add(new(Path.GetFileNameWithoutExtension(file), group == "Screens" ? "Screens" : group == "Art" ? "Native object captures" : "Rooms",
                            screen.Width, screen.Height, Convert.ToBase64String(Packed4Bpp.Pack(screen.Indices)), rgba));
                        detail = $"{screen.Width} × {screen.Height} px · 16-color palette";
                    }
                    else if(group=="Rooms" && extension==".bin" && Path.GetFileName(file).StartsWith("mechanism_t",StringComparison.Ordinal))
                    {
                        string name=Path.GetFileNameWithoutExtension(file);
                        bool wide=name=="mechanism_t4";
                        bool valid=wide || Enumerable.Range(0,4).Any(p=>name==$"mechanism_t1_p{p}" || name==$"mechanism_t2_d0_p{p}" || name==$"mechanism_t2_d1_p{p}");
                        if(!valid) throw new InvalidDataException("Unknown native mechanism frame name.");
                        int width=wide?32:16,height=wide?8:24;
                        byte[] packed=File.ReadAllBytes(file);
                        _=Packed4Bpp.Unpack(packed,width*height);
                        byte[] palette=Wagenheimer.MsxGraphics.Video.Tms9918.Palette.SelectMany((c,i)=>new[]{c.R,c.G,c.B,i==0?(byte)0:(byte)255}).ToArray();
                        if(report.Variant!="msx1")
                        {
                            string pyramid=Path.GetFileName(Path.GetDirectoryName(file)!)[1..];
                            using var colors=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Rooms","palettes.json")));
                            palette=colors.RootElement.GetProperty(pyramid).EnumerateArray().SelectMany((c,i)=>new[]{c[0].GetByte(),c[1].GetByte(),c[2].GetByte(),i==0?(byte)0:(byte)255}).ToArray();
                            name=$"p{pyramid}/"+name;
                        }
                        report.Pictures.Add(new(name,"Mechanisms",width,height,Convert.ToBase64String(packed),palette));
                        detail=$"Native {report.Variant.ToUpperInvariant()} mechanism frame: {width} by {height} pixels; cartridge palette";
                    }
                    else if(relative=="Screens/intro_vick.json")
                    {
                        var animation=Wagenheimer.MsxGraphics.Video.NativeActorAnimation.Load(file);
                        detail=$"Native actor timeline: {animation.Frames.Count} vblanks at {animation.FramesPerSecond} Hz, {animation.PoseCount} sprite poses";
                        report.Notices.Add($"The MSX1 intro uses {animation.Frames.Count} recorded native vblanks and {animation.PoseCount} sprite poses, preserving cartridge positions and visibility.");
                    }
                    else if(relative=="Rooms/animations.json" && report.Variant=="msx1")
                    {
                        Msx1PatternAnimations.ValidateFile(file,Array.Empty<string>());
                        using var json=JsonDocument.Parse(File.ReadAllText(file));
                        var rootElement=json.RootElement;
                        int rooms=rootElement.GetProperty("rooms").EnumerateObject().Count();
                        int cells=rootElement.GetProperty("rooms").EnumerateObject().Sum(r=>r.Value.GetArrayLength());
                        int eyes=rootElement.GetProperty("pyramids").EnumerateObject().Sum(p=>p.Value.GetProperty("frames").GetArrayLength());
                        int light=rootElement.GetProperty("torch").GetProperty("frames").GetArrayLength();
                        detail=$"Native color animations: {eyes} eye frames, {light} light-wave frames, {cells} animated cells in {rooms} rooms; timing in ROM logic ticks";
                        if(!rootElement.GetProperty("rooms").EnumerateObject().SelectMany(r=>r.Value.EnumerateArray()).Any(c=>c.GetProperty("type").GetString()=="torch"))
                            report.Notices.Add("Light-wave color streams were extracted, but no captured clean gameplay room references their pattern range. Their use on other screens remains unverified; no room cells were invented.");
                    }
                    else if (group == "Levels" && extension == ".json" && Path.GetFileName(file).StartsWith("stage", StringComparison.Ordinal))
                    {
                        var stage = StageLoader.LoadFile(file);
                        report.StageCount++;
                        report.ReferencedRoomCount += stage.Rooms.Count;
                        detail = $"Stage {stage.Number:00} · pyramid {stage.Pyramid} · {stage.Rooms.Count} rooms";
                    }
                    else if (extension == ".atlas") CollectAtlas(file, report, "sheet" + sheetId++);
                    report.Files.Add(Artifact(file, relative, Category(group), detail));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException or IndexOutOfRangeException or OverflowException)
                {
                    report.Notices.Add($"Could not interpret {relative}: {Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex)}");
                    try { report.Files.Add(Artifact(file, relative, Category(group), "File not validated")); } catch (IOException) { }
                }
            }
        }
        int expected=Assets.CartridgeVariants.StageCount(report.Variant);
        if (report.StageCount < expected) report.Notices.Add($"Found {report.StageCount} of {expected} stages. The graphics library cannot yet be considered complete.");
    }

    private static string Category(string group) => group switch
    { "Levels" => "Stages", "Rooms" => "Rooms and scenery", "Screens" => "Screens", _ => "Characters and objects" };

    private static ReportArtifact Artifact(string path, string name, string category, string detail)
    {
        using var stream = File.OpenRead(path);
        return new(name, category, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), detail);
    }

    private static void CollectAtlas(string file, ExtractionReportDocument report, string key)
    {
        string png = Path.ChangeExtension(file, ".png");
        if (!File.Exists(png)) throw new InvalidDataException("The atlas texture was not found.");
        byte[] data = File.ReadAllBytes(png);
        if (data.Length < 24 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("The atlas texture is not a valid PNG.");
        var texture = PngRgba.Decode(data);
        int sheetWidth = texture.Width, sheetHeight = texture.Height;
        report.Sheets.Add(new(key, "data:image/png;base64," + Convert.ToBase64String(data)));
        var names = new HashSet<string>(StringComparer.Ordinal);
        var emptyNames = new List<string>();
        var duplicateNames = new List<string>();
        string[] lines = File.ReadAllLines(file);
        int count = 0;
        for (int line = 0; line + 2 < lines.Length; line += 3)
        {
            string name = lines[line].Trim();
            string[] parts = lines[line + 1].Split(',');
            if (parts.Length != 4 || !parts.All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) continue;
            int[] box = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (box[0] < 0 || box[1] < 0 || box[2] <= 0 || box[3] <= 0 || (long)box[0] + box[2] > sheetWidth || (long)box[1] + box[3] > sheetHeight)
                throw new InvalidDataException("An atlas region extends beyond its texture.");
            int visible = texture.VisiblePixels(box[0], box[1], box[2], box[3]);
            bool duplicate = !names.Add(name);
            string? issue = duplicate ? "Duplicate atlas name; runtime lookup is ambiguous" : visible == 0 ? "Fully transparent sprite; review required" : null;
            if (visible == 0) emptyNames.Add(name);
            if (duplicate) duplicateNames.Add(name);
            report.Pictures.Add(new(name, "Characters and objects · " + Path.GetFileName(Path.GetDirectoryName(file)), box[2], box[3], SheetId: key, X: box[0], Y: box[1], VisiblePixels: visible, Issue: issue));
            count++;
        }
        if (emptyNames.Count > 0) report.Notices.Add($"Atlas {Path.GetFileName(Path.GetDirectoryName(file))}: {emptyNames.Count} fully transparent regions: {string.Join(", ", emptyNames)}. Blankness alone does not establish that an animation slot is intentionally unused.");
        if (duplicateNames.Count > 0) report.Notices.Add($"Atlas {Path.GetFileName(Path.GetDirectoryName(file))}: {duplicateNames.Count} duplicate names. New generations reject ambiguous atlases.");
        if (count == 0) report.Notices.Add("Atlas " + Path.GetFileName(file) + " contains no recognized regions.");
    }

    private static void CollectAudio(ExtractionReportRequest request, ExtractionReportDocument report, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(request.AudioPackagePath) || !File.Exists(request.AudioPackagePath))
        { report.Notices.Add("The audio package is not yet available for this version."); return; }
        try
        {
            using var library = MsxAudioLibrary.Load(request.AudioPackagePath);
            var identity = library.Manifest.Identity;
            if (identity?.GameId != "kings-valley-2" || identity.Variant != request.Variant ||
                !string.Equals(identity.RomSha1, request.RomSha1, StringComparison.OrdinalIgnoreCase))
            { report.Notices.Add("The audio package does not match this ROM and version. It was not included in the catalog."); return; }
            if (!identity.Complete) report.Notices.Add("This audio package is a partial diagnostic extraction.");
            report.Files.Add(Artifact(request.AudioPackagePath, "Audio/audio.msxaudio", "Audio", "Package identified by ROM and version"));
            string fingerprint = report.Files[^1].Sha256[..16];
            string mediaDirectory = Path.Combine(request.ReportDirectory, "media");
            Directory.CreateDirectory(mediaDirectory);
            foreach (var song in library.Songs.Concat(library.Clips))
            {
                string? preview = null;
                try
                {
                    string nameHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(song.Id)))[..16].ToLowerInvariant();
                    string basename = fingerprint + "_" + nameHash + ".wav";
                    string output = Path.Combine(mediaDirectory, basename);
                    if (request.Outcome is "completed" or "existing" && (!File.Exists(output) || new FileInfo(output).Length <= 44))
                    {
                        log?.Invoke("audio preview: " + song.Name);
                        var vgm = library.LoadVgm(song);
                        double end = song.Loop ? song.LoopStart + song.LoopSeconds : song.Seconds;
                        if (!double.IsFinite(end) || end <= 0 || end > 600) throw new InvalidDataException("Preview duration is outside the supported limit.");
                        var pcm = MsxSoundRenderer.Render(vgm, AudioProfile.Original.WithOversample(1), 22050, 0, end);
                        if (pcm.Peak <= 0) throw new InvalidDataException("The preview produced no audio.");
                        WriteWave(output, pcm);
                    }
                    if (File.Exists(output) && new FileInfo(output).Length > 44) preview = "media/" + basename;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
                { report.Notices.Add($"The preview for {song.Name} could not be generated: {Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex)}"); }
                report.Sounds.Add(new(song.Id, FriendlySound(song.Id), song.OriginalId, song.Music, song.Seconds,
                    song.Loop, song.LoopStart, song.LoopSeconds, preview, string.Join(" · ", song.Tags ?? Array.Empty<string>())));
            }
            string diagnostic = Path.Combine(Path.GetDirectoryName(request.AudioPackagePath)!, "capture-report.json");
            if (File.Exists(diagnostic))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(diagnostic));
                int silent = json.RootElement.EnumerateArray().Count(e => e.TryGetProperty("Silent", out var s) && s.ValueKind == JsonValueKind.True);
                if (silent > 0) report.Notices.Add($"{silent} control commands or silent entries were recorded in diagnostics and are not listed as playable sounds.");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException)
        { report.Notices.Add("Could not open the audio catalog: " + Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex)); }
    }

    private static string FriendlySound(string id) => id switch
    {
        "title" => "Opening", "menu" => "Menu", "puzzle" => "Puzzle", "world_map" => "World map",
        "ending" => "Ending", "ending_final" => "Final sequence theme", "jump" => "Jump", "play_start" => "Game start",
        "game_over" => "Game Over", "ending_danger" => "Ending danger", "ending_key" => "Ending key",
        "ending_flash" => "Ending flash", "ending_stamp" => "Ending stamp", "ending_count" => "Ending count",
        "door_unlocked" => "Door unlocked", "door_enter" => "Entering the door", "extra_life" => "Extra life",
        "coffin_open" => "Sarcophagus opening", "coffin_close" => "Sarcophagus closing", "coffin_pull" => "Pulling the sarcophagus",
        "pickup" => "Item pickup", "pick_soulstone" => "Soulstone pickup", "secret_reveal" => "Secret revealed",
        "dig_shovel" => "Shovel digging", "dig_drill" => "Drilling", "dig_pickaxe" => "Pickaxe digging", "dig_hammer" => "Hammer strike",
        "player_land" => "Explorer landing", "player_fall" => "Explorer falling", "enemy_fall" => "Enemy falling",
        "enemy_land" => "Enemy landing", "enemy_spawn" => "Enemy spawning", "enemy_hit" => "Enemy impact",
        "unused_tone_02" => "Unused tone · 02", "unused_jingle" => "Unused jingle", "unused_click" => "Unused effect",
        _ when id.StartsWith("stage_", StringComparison.Ordinal) => "Stage theme · " + id[^1].ToString().ToUpperInvariant(),
        _ => CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(id.Replace('_', ' '))
    };

    private static void WriteWave(string path, RenderedAudio pcm)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                byte[] bytes = pcm.ToPcmBytes();
                writer.Write("RIFF"u8); writer.Write(36 + bytes.Length); writer.Write("WAVE"u8);
                writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)pcm.Channels);
                writer.Write(pcm.SampleRate); writer.Write(pcm.SampleRate * pcm.Channels * 2);
                writer.Write((short)(pcm.Channels * 2)); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(bytes.Length); writer.Write(bytes);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, contents, new UTF8Encoding(false)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
