namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Bounded Konami count/literal stream. Address-changing VRAM streams use a different reader.</summary>
public static class KonamiRle
{
    public static byte[] Decode(ReadOnlySpan<byte> rom, int offset, int expectedBytes)
    {
        byte[] output=DecodeBounded(rom,offset,expectedBytes);
        if(output.Length!=expectedBytes) throw new InvalidDataException("RLE stream ended before its expected output.");
        return output;
    }

    /// <summary>Reads a terminated stream whose output size is unknown, subject to a strict byte limit.</summary>
    public static byte[] DecodeBounded(ReadOnlySpan<byte> rom, int offset, int maximumBytes)
    {
        if (offset < 0 || offset >= rom.Length || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(offset));
        byte[] output = new byte[maximumBytes];
        int written = 0;
        while (offset < rom.Length)
        {
            byte operation = rom[offset++];
            if (operation == 0)
            {
                return output.AsSpan(0,written).ToArray();
            }
            int count = operation & 127;
            if (count == 0 || count > maximumBytes-written) throw new InvalidDataException("RLE stream exceeds its output limit or uses an unsupported address operation.");
            if ((operation & 128) != 0)
            {
                if (offset + count > rom.Length) throw new InvalidDataException("Truncated RLE literal.");
                rom.Slice(offset,count).CopyTo(output.AsSpan(written)); offset += count;
            }
            else
            {
                if (offset >= rom.Length) throw new InvalidDataException("Truncated RLE run.");
                output.AsSpan(written,count).Fill(rom[offset++]);
            }
            written += count;
        }
        throw new InvalidDataException("RLE terminator is missing.");
    }
}
