#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KingsValley.Engine.Assets;

namespace KingsValley.Engine.Extraction;

/// <summary>Identified flavor of a scanned ROM file.</summary>
public enum ScannedRomFlavor
{
    Msx1,
    Msx2,
    Unknown
}

/// <summary>Metadata and status of a ROM file found in the roms folder.</summary>
public sealed record ScannedRomInfo(
    string FilePath,
    string FileName,
    long SizeBytes,
    string Sha1,
    ScannedRomFlavor Flavor,
    string DisplayTag,
    string Description);

/// <summary>
/// Manages the local ROMs folder (%APPDATA%\KingsValley\roms\), handles opening it in the OS file manager,
/// and scans for MSX1 and MSX2 cartridges.
/// </summary>
public static class RomFolderManager
{
    /// <summary>Standard SHA-1 for MSX1 King's Valley II (JP) [RC-760] (GoodMSX dump, 128 KB).</summary>
    public const string Msx1Rc760Sha1 = "CFD872D005B7BD4CDD6E06C4C0162191F0B0415D";

    /// <summary>Standard SHA-1 for MSX2 King's Valley II (JP) [RC-761] (128 KB).</summary>
    public const string Msx2Rc761Sha1 = "F25628E474035E6BF0440B274C5DF04F94C49FB8";

    /// <summary>SHA-1 for MSX2 King's Valley II (JP) [RC-761] SCC enhanced / patched (128 KB).</summary>
    public const string Msx2Rc761SccSha1 = "0C7E0B618C72E7CA08BD756C3103B46E5C580C04";

    /// <summary>SHA-1 for MSX2 King's Valley II boot hack (128 KB).</summary>
    public const string Msx2BootHackSha1 = "74EEAA99BFF38B332F64E9E9F7775F834D1842E7";

    /// <summary>Default system directory for user ROMs: %APPDATA%\KingsValley\roms\</summary>
    public static string DefaultRomsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KingsValley", "roms");

    /// <summary>Portable directory fallback: ./roms/ next to the executable.</summary>
    public static string PortableRomsDirectory { get; } = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "roms");

    /// <summary>Ensures the standard ROMs folder exists on disk.</summary>
    public static void EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(DefaultRomsDirectory))
                Directory.CreateDirectory(DefaultRomsDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomFolderManager] Failed to create roms directory: {ex.Message}");
        }
    }

    /// <summary>Deletes the extracted content directory to allow a clean re-extraction.</summary>
    public static bool PurgeExtractedContent()
    {
        try
        {
            string dir = AudioBootstrap.ExtractedDirectory;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
                Console.WriteLine($"[RomFolderManager] Purged extracted folder: {dir}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomFolderManager] Failed to purge extracted folder: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Opens the ROMs folder in the operating system's default file manager (Explorer, Finder, or file manager).
    /// </summary>
    public static bool OpenRomsFolder()
    {
        EnsureDirectories();

        string folderToOpen = Directory.Exists(DefaultRomsDirectory)
            ? DefaultRomsDirectory
            : AppDomain.CurrentDomain.BaseDirectory;

        try
        {
            // ArgumentList keeps paths with spaces (e.g. "Library/Application Support")
            // as a single argument. Passing a raw arguments string made macOS `open`
            // split it and print "The files ... do not exist." forever.
            var startInfo = new ProcessStartInfo(folderToOpen) { UseShellExecute = true };
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomFolderManager] Cannot open file manager: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Scans the default and portable ROM directories for .rom, .bin and .zip files, computing their
    /// hashes and identifying whether they are MSX1 or MSX2 cartridges.
    /// </summary>
    public static IReadOnlyList<ScannedRomInfo> ScanRoms()
    {
        EnsureDirectories();

        var results = new List<ScannedRomInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var directories = new List<string> { DefaultRomsDirectory };
        if (Directory.Exists(PortableRomsDirectory) &&
            !string.Equals(DefaultRomsDirectory, PortableRomsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            directories.Add(PortableRomsDirectory);
        }

        // Development fallback: check tools/kv2-extract/work/rom if present
        string devWorkRom = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "tools", "kv2-extract", "work", "rom");
        if (Directory.Exists(devWorkRom))
            directories.Add(Path.GetFullPath(devWorkRom));

        foreach (string dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(dir))
                {
                    if (!seenPaths.Add(filePath))
                        continue;

                    string ext = Path.GetExtension(filePath);
                    if (!ext.Equals(".rom", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".bin", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ScannedRomInfo? info = InspectRomFile(filePath);
                    if (info != null)
                        results.Add(info);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RomFolderManager] Error scanning {dir}: {ex.Message}");
            }
        }

        // Sort order: MSX1 first, then MSX2, then others
        return results
            .OrderBy(r => r.Flavor switch
            {
                ScannedRomFlavor.Msx1 => 0,
                ScannedRomFlavor.Msx2 => 1,
                _ => 2
            })
            .ThenBy(r => r.FileName)
            .ToList();
    }

    /// <summary>Computes SHA-1 and inspects ROM header/size to identify the cartridge.</summary>
    public static ScannedRomInfo? InspectRomFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
                return null;

            string sha1Hex = ComputeSha1(filePath);
            long size = fileInfo.Length;
            string fileName = fileInfo.Name;

            // Match known SHA-1 hashes
            if (string.Equals(sha1Hex, Msx1Rc760Sha1, StringComparison.OrdinalIgnoreCase))
            {
                return new ScannedRomInfo(
                    filePath, fileName, size, sha1Hex,
                    ScannedRomFlavor.Msx1,
                    "[MSX1 RC-760]",
                    "ORIGINAL GOODMSX DUMP");
            }

            if (string.Equals(sha1Hex, Msx2Rc761Sha1, StringComparison.OrdinalIgnoreCase))
            {
                return new ScannedRomInfo(
                    filePath, fileName, size, sha1Hex,
                    ScannedRomFlavor.Msx2,
                    "[MSX2 RC-761]",
                    "ORIGINAL KONAMI RC-761");
            }

            if (string.Equals(sha1Hex, Msx2Rc761SccSha1, StringComparison.OrdinalIgnoreCase))
            {
                return new ScannedRomInfo(
                    filePath, fileName, size, sha1Hex,
                    ScannedRomFlavor.Msx2,
                    "[MSX2 RC-761 SCC]",
                    "MSX2 SCC ENHANCED DUMP");
            }

            if (string.Equals(sha1Hex, Msx2BootHackSha1, StringComparison.OrdinalIgnoreCase))
            {
                return new ScannedRomInfo(
                    filePath, fileName, size, sha1Hex,
                    ScannedRomFlavor.Msx2,
                    "[MSX2 BOOT PATCH]",
                    "MSX2 BOOT PATCHED DUMP");
            }

            // Heuristic detection based on size and content
            ScannedRomFlavor flavor = ScannedRomFlavor.Unknown;
            string tag = "[UNKNOWN ROM]";
            string desc = $"ROM FILE ({size / 1024} KB)";

            if (fileInfo.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tag = "[ZIP ARCHIVE]";
                desc = $"COMPRESSED ARCHIVE ({size / 1024} KB)";
            }
            else if (size is 131072 or 262144)
            {
                byte[] header = ReadHeader(filePath, 32);
                bool isMsxCart = header.Length >= 2 && header[0] == 0x41 && header[1] == 0x42; // "AB"

                string upperName = fileName.ToUpperInvariant();
                if (upperName.Contains("MSX1") || upperName.Contains("RC-760") || upperName.Contains("RC760"))
                {
                    flavor = ScannedRomFlavor.Msx1;
                    tag = "[MSX1 CUSTOM]";
                    desc = $"MSX1 CARTRIDGE ({size / 1024} KB)";
                }
                else if (upperName.Contains("MSX2") || upperName.Contains("RC-761") || upperName.Contains("RC761") || upperName.Contains("SCC"))
                {
                    flavor = ScannedRomFlavor.Msx2;
                    tag = "[MSX2 CUSTOM]";
                    desc = $"MSX2 CARTRIDGE ({size / 1024} KB)";
                }
                else if (isMsxCart)
                {
                    flavor = ScannedRomFlavor.Msx1; // fallback default
                    tag = "[MSX CARTRIDGE]";
                    desc = $"MSX CARTRIDGE ({size / 1024} KB)";
                }
            }

            return new ScannedRomInfo(filePath, fileName, size, sha1Hex, flavor, tag, desc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomFolderManager] Failed to inspect {filePath}: {ex.Message}");
            return null;
        }
    }

    private static string ComputeSha1(string filePath)
    {
        using var sha = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static byte[] ReadHeader(string filePath, int bytes)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] buf = new byte[Math.Min(bytes, stream.Length)];
            _ = stream.Read(buf, 0, buf.Length);
            return buf;
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
