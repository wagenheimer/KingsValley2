using System.Buffers.Binary;
using System.Text;

namespace KingsValley.Core.Tests;

/// <summary>
/// Guards the alpha channel of the icon SDL loads as the window icon. On macOS that image is
/// also pushed to the Dock, so an opaque BMP would paint a black Dock tile.
/// </summary>
public sealed class IconAlphaTests
{
    private const uint BiBitfields = 3;

    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void IconBmp_HasTrueBgraAlpha()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, "src", "KingsValley.Desktop", "Icon.bmp"));

        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        int pixelOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10));
        int dibSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14));
        Assert.True(dibSize >= 40, "Expected a BITMAPINFOHEADER-compatible DIB header.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
        Assert.Equal(256, width);
        Assert.Equal(256, height);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)));
        Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)));

        // BITMAPV4/V5 (or a BITMAPINFOHEADER with appended masks) stores masks after the
        // 40-byte core DIB header. The fourth one is the alpha mask SDL needs.
        Assert.Equal(BiBitfields, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30)));
        uint redMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(54));
        uint greenMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(58));
        uint blueMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(62));
        uint alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(66));
        Assert.Equal(0x00FF0000u, redMask);
        Assert.Equal(0x0000FF00u, greenMask);
        Assert.Equal(0x000000FFu, blueMask);
        Assert.Equal(0xFF000000u, alphaMask);
        Assert.True(bytes.Length >= pixelOffset + width * height * 4, "BMP pixel data is truncated.");

        var corners = new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1) };
        Assert.Contains(corners, corner => Pixel(bytes, pixelOffset, width, height,
            corner.Item1, corner.Item2, redMask, greenMask, blueMask, alphaMask).A == 0);

        // The master PNG has a gold (251,188,25) center; it must stay opaque after downscaling.
        var center = Pixel(bytes, pixelOffset, width, height, width / 2, height / 2,
            redMask, greenMask, blueMask, alphaMask);
        Assert.Equal(255, center.A);
        Assert.InRange(center.R, 200, 255);
        Assert.InRange(center.G, 120, 220);
        Assert.InRange(center.B, 0, 100);
    }

    [Fact]
    public void IconPng_HasAlphaChannel()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, "src", "KingsValley.Desktop", "Icon.png"));

        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A },
            bytes.Take(8).ToArray());
        Assert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(8, bytes[24]); // bit depth
        Assert.Equal(6, bytes[25]); // color type 6 = truecolor with alpha
    }

    private static (int R, int G, int B, int A) Pixel(byte[] bytes, int pixelOffset, int width, int height,
        int x, int y, uint redMask, uint greenMask, uint blueMask, uint alphaMask)
    {
        int stride = ((width * 32 + 31) / 32) * 4; // DIB rows are padded to 4 bytes.
        int offset = pixelOffset + (height - 1 - y) * stride + x * 4; // BMP rows are bottom-up.
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        return (
            Channel(value, redMask),
            Channel(value, greenMask),
            Channel(value, blueMask),
            Channel(value, alphaMask));
    }

    private static int Channel(uint value, uint mask)
    {
        int shift = 0;
        while (((mask >> shift) & 1) == 0)
            shift++;
        return (int)((value & mask) >> shift);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "KingsValley.Desktop", "Icon.bmp")) &&
                File.Exists(Path.Combine(directory.FullName, "KingsValley.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repo root with src/KingsValley.Desktop/Icon.bmp not found.");
    }
}
