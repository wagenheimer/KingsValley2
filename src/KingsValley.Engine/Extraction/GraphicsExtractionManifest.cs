#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace KingsValley.Engine.Extraction;

public sealed record GraphicsArtifact(string Path, long Bytes, string Sha256, string Evidence);
public sealed record GraphicsCheck(string Component, string State, string Detail);
public sealed class GraphicsExtractionManifest
{
    public const string FileName = "graphics-manifest.json";
    public int Schema { get; set; } = 1;
    public int ProfileRevision { get; set; } = 1;
    public string GameId { get; set; } = "kings-valley-2";
    public string Variant { get; set; } = "";
    public string RomSha1 { get; set; } = "";
    public int StageLimit { get; set; }
    public string Outcome { get; set; } = "partial";
    public bool Ready { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GraphicsCheck> Checks { get; set; } = new();
    public List<GraphicsArtifact> Artifacts { get; set; } = new();
    public static GraphicsExtractionManifest? Read(string directory)
    {
        try
        {
            var manifest=JsonSerializer.Deserialize<GraphicsExtractionManifest>(File.ReadAllText(Path.Combine(directory,FileName)));
            return manifest!=null && manifest.Checks!=null && manifest.Artifacts!=null && !manifest.Checks.Any(c=>c==null) && !manifest.Artifacts.Any(a=>a==null) ? manifest : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public void Save(string directory) => File.WriteAllText(Path.Combine(directory,FileName), JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true}));
    public bool Verify(string directory, string variant, string? sha1 = null)
    {
        if(!Assets.CartridgeVariants.All.Contains(variant,StringComparer.Ordinal))return false;
        if(Checks==null || Artifacts==null || Checks.Any(c=>c==null) || Artifacts.Any(a=>a==null)) return false;
        if (Schema != 1 || ProfileRevision != KingsValleyGraphicsProfiles.Revision(variant) || GameId != "kings-valley-2" || Variant != variant || (sha1 != null && RomSha1 != sha1) || !Ready || Outcome != "completed" || StageLimit != Assets.CartridgeVariants.StageCount(variant) || Artifacts.Count == 0 || Checks.Any(c => c.State != "valid")) return false;
        var profile=Wagenheimer.MsxAudio.Extraction.RomExtractionProfiles.FindByGameVariant(GameId,variant);
        if(profile==null || !profile.RomSha1.Contains(RomSha1,StringComparer.OrdinalIgnoreCase)) return false;
        var components=Checks.Select(c=>c.Component).ToHashSet(StringComparer.Ordinal);
        if(!components.Contains("atlas") || Enumerable.Range(1,Assets.CartridgeVariants.StageCount(variant)).Any(n=>!components.Contains($"stage-{n:00}")) || KingsValleyGraphicsProfiles.RequiredScreens(variant).Any(n=>!components.Contains("screen-"+n))) return false;
        var paths=Artifacts.Select(a=>a.Path).ToHashSet(StringComparer.Ordinal);
        if(!paths.Contains("Art/kv2.atlas") || !paths.Contains("Art/kv2.png") || Enumerable.Range(1,Assets.CartridgeVariants.StageCount(variant)).Any(n=>!paths.Contains($"Levels/stage{n:00}.json")) || KingsValleyGraphicsProfiles.RequiredScreens(variant).Any(n=>!paths.Contains("Screens/"+n+".bin"))) return false;
        if(KingsValleyGraphicsProfiles.RequiredSprites(variant).Any(n=>!components.Contains("sprite-"+n)))return false;
        for(int pyramid=1;pyramid<=(variant!="msx1"?6:1);pyramid++)
        {
            string prefix=variant!="msx1"?$"mechanism-p{pyramid}-":"mechanism-";
            bool Mechanism(string component,string file)=>components.Contains(prefix+component) &&
                paths.Contains("Rooms/"+KingsValleyGraphicsProfiles.MechanismPath(variant,pyramid,file));
            for(int phase=0;phase<4;phase++)
            {
                if(!Mechanism($"1-{phase}",$"mechanism_t1_p{phase}.bin"))return false;
                for(int direction=0;direction<2;direction++)
                    if(!Mechanism($"2-{direction}-{phase}",$"mechanism_t2_d{direction}_p{phase}.bin"))return false;
            }
            if(!Mechanism("4","mechanism_t4.bin"))return false;
        }
        if(variant!="msx1")
        {
            for(int pyramid=1;pyramid<=6;pyramid++)
                if(!components.Contains("blocks-"+pyramid) || !paths.Contains($"Rooms/blocks_p{pyramid}.bin"))return false;
            foreach(string table in new[]{"murals","decorations","palettes","animations"})
                if(!components.Contains("table-"+table) || !paths.Contains($"Rooms/{table}.json"))return false;
        }
        else if(!components.Contains("murals") || !components.Contains("pattern-animations") || !components.Contains("intro-animation") ||
                !paths.Contains("Rooms/murals.bin") || !paths.Contains("Rooms/animations.json") || !paths.Contains("Screens/intro_vick.json"))return false;
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        try
        {
            foreach (var artifact in Artifacts)
            {
                string path = Path.GetFullPath(Path.Combine(directory,artifact.Path));
                if (!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
                using var stream = File.OpenRead(path);
                if (stream.Length != artifact.Bytes || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)),artifact.Sha256,StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
