namespace Wagenheimer.MsxGraphics.Formats;

/// <summary>Lossless conversion to a native palette. Never silently picks a nearest color.</summary>
public static class PaletteIndexing
{
    public static byte[] FromRgba(ReadOnlySpan<byte> rgba,IReadOnlyList<(byte R,byte G,byte B)> palette,bool transparentZero)
    {
        if(rgba.Length%4!=0 || palette.Count!=16) throw new ArgumentException("Expected RGBA pixels and a sixteen-color palette.");
        byte[] result=new byte[rgba.Length/4];
        for(int pixel=0;pixel<result.Length;pixel++)
        {
            var source=rgba.Slice(pixel*4,4);
            if(source[3]==0 && transparentZero) continue;
            if(source[3]!=255) throw new InvalidDataException("The native image cannot represent partial alpha.");
            int found=-1;
            for(int index=transparentZero?1:0;index<palette.Count;index++)
                if(source[0]==palette[index].R && source[1]==palette[index].G && source[2]==palette[index].B) {found=index;break;}
            if(found<0) throw new InvalidDataException("A source pixel is absent from its declared native palette.");
            result[pixel]=(byte)found;
        }
        return result;
    }
}
