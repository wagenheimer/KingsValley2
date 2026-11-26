using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Engine.Extraction;

/// <summary>Exact FRS recipes for the original GoodMSX and JoySNES Contest cartridges.</summary>
public static class ContestEnglishPatch
{
    public const string SourceSha1 = "4a157e6e217f6af4ab3dec5d7dd5188e5cef7954";
    public const string EnglishSha1 = "e7b0a7b083d2f5bb12fb4ea6b2e822ee6330a15e";
    public const string OriginalSourceSha1 = "2e0436303648097e8e936c5467574aff5e3f1517";
    public const string OfficialEnglishSha1 = "376b4785cab7d60cc083f0c03bd5e8ea2fee8205";
    public const string ArchiveUrl = "https://frs.badcoffee.info/files/KingsValley_II_EnhancementPatches.zip";
    public const string ArchiveSha256 = "9CF7DA18F79FA0778A7CFEE0971C434977CEE14E05AFB93B17D11A1A3B8393E4";
    private const string PatchSha256 = "6673E48BA0703965B5F377662BDBDE7909B6F62CFC0BE92504E850E3638D9E6F";

    public static string Prepare(string resolvedRom, string collectionRoot, Action<string> status, CancellationToken token)
    {
        byte[] source = File.ReadAllBytes(resolvedRom);
        string sourceHash = Hash(source);
        if (sourceHash is not (SourceSha1 or OriginalSourceSha1) || Environment.GetEnvironmentVariable("KINGSVALLEY_CONTEST_LANGUAGE") == "jp") return resolvedRom;
        string expected = sourceHash == OriginalSourceSha1 ? OfficialEnglishSha1 : EnglishSha1;
        string directory = Path.Combine(collectionRoot, ".rom-cache", "kings-valley-2", "english");
        string destination = Path.Combine(directory, expected + ".rom");
        if (File.Exists(destination) && new FileInfo(destination).Length == 131072 && Hash(File.ReadAllBytes(destination)) == expected) return destination;
        status("Preparing English Contest translation: verifying the bundled FRS patch...");
        string archive = Path.Combine(collectionRoot, ".patches", "FRS-KingsValleyII-1.1", "KingsValley_II_EnhancementPatches.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        using (var resource = typeof(ContestEnglishPatch).Assembly.GetManifestResourceStream("KingsValley.FRS.Patches.zip")
            ?? throw new InvalidDataException("The bundled FRS archive is missing."))
        {
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled FRS archive failed its SHA-256 check.");
            File.WriteAllBytes(archive, bytes);
        }
        byte[] patch = VerifiedPatchArchive.ReadAsync(new Uri(ArchiveUrl), archive, ArchiveSha256,
            "KVAL2G.IPS", PatchSha256, token).GetAwaiter().GetResult();
        byte[] translated = Apply(source, patch);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, translated); token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }

    public static byte[] Apply(byte[] source, byte[] patch)
    {
        string sourceHash = Hash(source);
        if (source.Length != 131072 || sourceHash is not (SourceSha1 or OriginalSourceSha1)) throw new InvalidDataException("This cartridge is not a validated Contest translation source.");
        string expected = sourceHash == OriginalSourceSha1 ? OfficialEnglishSha1 : EnglishSha1;
        byte[] translated = IpsPatch.Apply(source, patch, 131072);
        if (translated.Length != 131072 || Hash(translated) != expected) throw new InvalidDataException("The English Contest cartridge failed its SHA-1 check.");
        return translated;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
}
