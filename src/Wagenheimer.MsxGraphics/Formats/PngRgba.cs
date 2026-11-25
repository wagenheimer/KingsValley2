using System.Buffers.Binary;
using System.IO.Compression;

namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Strict CPU-only reader for the non-interlaced RGB/RGBA atlas PNGs written by extractors.</summary>
public sealed record PngRgba(int Width, int Height, byte[] Pixels)
{
    public static PngRgba Decode(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10})) throw new InvalidDataException("Invalid PNG signature.");
        int width = 0, height = 0, channels = 0, offset = 8;
        bool ended = false;
        using var compressed = new MemoryStream();
        while (offset + 12 <= bytes.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || (long)offset + length + 12 > bytes.Length) throw new InvalidDataException("Truncated PNG chunk.");
            var type = bytes.AsSpan(offset + 4, 4);
            var payload = bytes.AsSpan(offset + 8, length);
            uint crc = 0xffffffff;
            foreach (byte value in bytes.AsSpan(offset + 4, length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length, 4))) throw new InvalidDataException("PNG checksum mismatch.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8 || length != 13) throw new InvalidDataException("Invalid PNG header.");
                width = BinaryPrimitives.ReadInt32BigEndian(payload);
                height = BinaryPrimitives.ReadInt32BigEndian(payload[4..]);
                channels = payload[9] switch {2 => 3, 6 => 4, _ => 0};
                if (width <= 0 || height <= 0 || (long)width * height > 16_777_216 || payload[8] != 8 || channels == 0 || payload[10] != 0 || payload[11] != 0 || payload[12] != 0)
                    throw new InvalidDataException("Unsupported atlas PNG format or dimensions.");
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (channels == 0) throw new InvalidDataException("PNG data precedes its header.");
                compressed.Write(payload);
            }
            else if (type.SequenceEqual("IEND"u8)) { if (length != 0) throw new InvalidDataException("Invalid PNG end."); ended = true; offset += length + 12; break; }
            else if ((type[0] & 32) == 0 && !type.SequenceEqual("PLTE"u8)) throw new InvalidDataException("Unsupported critical PNG chunk.");
            offset += length + 12;
        }
        if (!ended || offset != bytes.Length || channels == 0) throw new InvalidDataException("Incomplete PNG.");
        compressed.Position = 0;
        int stride = checked(width * channels);
        byte[] raw = new byte[checked((stride + 1) * height)];
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.ReadExactly(raw);
            if (zlib.ReadByte() != -1) throw new InvalidDataException("PNG data exceeds its dimensions.");
        }
        byte[] rgba = new byte[checked(width * height * 4)];
        byte[] previous = new byte[stride], row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int start = y * (stride + 1), filter = raw[start];
            if (filter > 4) throw new InvalidDataException("Invalid PNG filter.");
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? row[x - channels] : 0, up = previous[x], corner = x >= channels ? previous[x - channels] : 0;
                int prediction = filter switch {0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => Paeth(left, up, corner)};
                row[x] = unchecked((byte)(raw[start + 1 + x] + prediction));
            }
            for (int x = 0; x < width; x++)
            {
                int target = (y * width + x) * 4, source = x * channels;
                row.AsSpan(source, 3).CopyTo(rgba.AsSpan(target));
                rgba[target + 3] = channels == 4 ? row[source + 3] : (byte)255;
            }
            (row, previous) = (previous, row);
        }
        return new(width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, da = Math.Abs(p - a), db = Math.Abs(p - b), dc = Math.Abs(p - c);
        return da <= db && da <= dc ? a : db <= dc ? b : c;
    }

    public int VisiblePixels(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || (long)x + width > Width || (long)y + height > Height) throw new InvalidDataException("Atlas region is outside its texture.");
        int count = 0;
        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++) if (Pixels[(row * Width + col) * 4 + 3] != 0) count++;
        return count;
    }
}
