namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Bounded, immutable decoder for the IPS binary patch format, including RLE and optional final size.</summary>
public static class IpsPatch
{
    public static byte[] Apply(ReadOnlySpan<byte> source,ReadOnlySpan<byte> patch,int maximumSize=8*1024*1024)
    {
        if(maximumSize<0 || source.Length>maximumSize)throw new ArgumentOutOfRangeException(nameof(maximumSize));
        if(patch.Length<8 || !patch[..5].SequenceEqual("PATCH"u8))throw new InvalidDataException("Invalid IPS header.");
        byte[] output=source.ToArray();int cursor=5;
        while(true)
        {
            if(cursor+3>patch.Length)throw new InvalidDataException("Truncated IPS record or missing EOF.");
            if(patch.Slice(cursor,3).SequenceEqual("EOF"u8))
            {
                cursor+=3;
                if(cursor==patch.Length)return output;
                if(cursor+3!=patch.Length)throw new InvalidDataException("Unexpected bytes after IPS EOF.");
                int size=(patch[cursor]<<16)|(patch[cursor+1]<<8)|patch[cursor+2];
                if(size>maximumSize)throw new InvalidDataException("IPS final size exceeds the configured limit.");
                Array.Resize(ref output,size);return output;
            }
            if(cursor+5>patch.Length)throw new InvalidDataException("Truncated IPS record header.");
            int address=(patch[cursor]<<16)|(patch[cursor+1]<<8)|patch[cursor+2];
            int length=(patch[cursor+3]<<8)|patch[cursor+4];cursor+=5;
            bool rle=length==0;
            if(rle)
            {
                if(cursor+3>patch.Length)throw new InvalidDataException("Truncated IPS RLE record.");
                length=(patch[cursor]<<8)|patch[cursor+1];cursor+=2;
                if(length==0)throw new InvalidDataException("Empty IPS RLE record.");
            }
            if(address>maximumSize-length)throw new InvalidDataException("IPS record exceeds the configured limit.");
            int payload=rle?1:length;
            if(cursor+payload>patch.Length)throw new InvalidDataException("Truncated IPS record payload.");
            if(output.Length<address+length)Array.Resize(ref output,address+length);
            if(rle)output.AsSpan(address,length).Fill(patch[cursor]);
            else patch.Slice(cursor,length).CopyTo(output.AsSpan(address,length));
            cursor+=payload;
        }
    }
}
