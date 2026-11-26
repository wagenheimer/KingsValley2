using System.IO.Compression;
using System.Security.Cryptography;

namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Downloads and retains a complete, pinned patch archive; reads a bounded verified entry.</summary>
public static class VerifiedPatchArchive
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    public static async Task<byte[]> ReadAsync(Uri uri, string cachePath, string archiveSha256,
        string entryName, string entrySha256, CancellationToken cancellationToken = default)
    {
        const int limit = 2 * 1024 * 1024;
        byte[]? archive = null;
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= limit)
        {
            var cached = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            if (Matches(cached, archiveSha256)) archive = cached;
        }
        if (archive == null)
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Patch downloads require HTTPS.");
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > limit) throw new InvalidDataException("Patch archive exceeds the size limit.");
                output.Write(buffer, 0, read);
            }
            archive = output.ToArray();
            if (!Matches(archive, archiveSha256)) throw new InvalidDataException("The downloaded patch archive failed its SHA-256 check.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, archive, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, cachePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException("The patch archive is missing the required entry.");
        if (entry.Length > limit) throw new InvalidDataException("Patch entry exceeds the size limit.");
        using var stream = entry.Open();
        byte[] patch = new byte[(int)entry.Length];
        await stream.ReadExactlyAsync(patch, cancellationToken);
        if (!Matches(patch, entrySha256)) throw new InvalidDataException("The patch entry failed its SHA-256 check.");
        return patch;
    }

    private static bool Matches(byte[] bytes, string expected) =>
        Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase);
}
