namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Two palette indices per byte, high nibble first. No image/GPU dependencies.</summary>
public static class Packed4Bpp
{
    public static byte[] Pack(ReadOnlySpan<byte> pixels)
    {
        var packed = new byte[pixels.Length / 2 + pixels.Length % 2];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] > 15) throw new ArgumentOutOfRangeException(nameof(pixels), "A 4bpp palette index must be between 0 and 15.");
            if ((i & 1) == 0) packed[i / 2] = (byte)(pixels[i] << 4);
            else packed[i / 2] |= pixels[i];
        }
        return packed;
    }

    public static byte[] Unpack(ReadOnlySpan<byte> packed, int pixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelCount);
        int expected = pixelCount / 2 + pixelCount % 2;
        if (packed.Length != expected)
            throw new InvalidDataException($"Expected {expected} packed bytes for {pixelCount} pixels; found {packed.Length}.");
        var pixels = new byte[pixelCount];
        for (int i = 0; i < pixelCount; i++)
            pixels[i] = (byte)(((i & 1) == 0 ? packed[i / 2] >> 4 : packed[i / 2]) & 15);
        return pixels;
    }
}
