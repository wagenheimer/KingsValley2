#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace KingsValley.Engine.Extraction;

/// <summary>Immutable generations and atomic pointer publication. Failed attempts never replace a valid generation.</summary>
public static class GraphicsGenerationStore
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTime Pointer,DateTime Manifest,string Directory)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Pointer(string Generation);
    public static string? Current(string root, string variant)
    {
        try
        {
            var pointer = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(Path.Combine(root,"current.json")));
            if (pointer == null || !Guid.TryParseExact(pointer.Generation,"N",out _)) return null;
            string directory = Path.Combine(root,"generations",pointer.Generation);
            DateTime pointerStamp=File.GetLastWriteTimeUtc(Path.Combine(root,"current.json"));
            DateTime manifestStamp=File.GetLastWriteTimeUtc(Path.Combine(directory,GraphicsExtractionManifest.FileName));
            string key=Path.GetFullPath(root)+"|"+variant;
            if(Cache.TryGetValue(key,out var cached) && cached.Pointer==pointerStamp && cached.Manifest==manifestStamp && cached.Directory==directory) return directory;
            var manifest = GraphicsExtractionManifest.Read(directory);
            if(manifest?.Verify(directory,variant)!=true) return null;
            Cache[key]=(pointerStamp,manifestStamp,directory);
            return directory;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public static void Invalidate() => Cache.Clear();
    public static string Begin(string root)
    {
        string path = Path.Combine(root,".staging",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    public static string Finish(string root,string staging,GraphicsExtractionManifest manifest)
    {
        string parent = Path.GetFullPath(Path.Combine(root,".staging")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(staging).StartsWith(parent,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Staging directory is outside this graphics store.");
        manifest.Save(staging);
        if (manifest.Ready && !manifest.Verify(staging,manifest.Variant,manifest.RomSha1)) throw new InvalidDataException("Graphics generation failed publication validation.");
        string id = Guid.NewGuid().ToString("N");
        string target = Path.Combine(root,manifest.Ready ? "generations" : "attempts",id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(staging,target);
        if (manifest.Ready)
        {
            string temp = Path.Combine(root,"current." + id + ".tmp");
            File.WriteAllText(temp,JsonSerializer.Serialize(new Pointer(id)));
            File.Move(temp,Path.Combine(root,"current.json"),overwrite:true);
        }
        return target;
    }
}
