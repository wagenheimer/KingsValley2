namespace Wagenheimer.MsxGraphics.Video;

/// <summary>Isolated mode-2 sprite decoding for 256-pixel bitmap modes. Color zero is transparent.</summary>
public static class V9938Sprites
{
    public static byte[] DecodeRegion(byte[] vram,byte[] registers,byte[] attributes,byte[] colors,
        int x,int y,int width,int height,bool scanlineLimit=false)
    {
        ArgumentNullException.ThrowIfNull(vram);ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(attributes);ArgumentNullException.ThrowIfNull(colors);
        if(vram.Length!=131072 || registers.Length<7 || attributes.Length%4!=0 || attributes.Length>128 || colors.Length!=attributes.Length*4)
            throw new ArgumentException("Expected 128 KiB VRAM, VDP registers and sixteen color bytes per sprite.");
        if(width<=0 || height<=0 || x<0 || y<0 || x+width>256 || y+height>212)
            throw new ArgumentOutOfRangeException(nameof(width),"The sprite region must fit the native frame.");
        int size=(registers[1]&2)!=0?16:8,scale=(registers[1]&1)!=0?2:1,patternBase=(registers[6]&63)<<11;
        var pixels=new byte[width*height];
        for(int line=0;line<height;line++)
        {
            var visible=new List<(int Index,int Row,byte Color,int X)>();
            for(int sprite=0;sprite<attributes.Length/4;sprite++)
            {
                int rawY=attributes[sprite*4];if(rawY==216)break;
                int row=(y+line-rawY-1)&255;if(row>=size*scale)continue;
                if(scanlineLimit && visible.Count==8)break;
                row/=scale;byte color=colors[sprite*16+row];
                visible.Add((sprite,row,color,attributes[sprite*4+1]-((color&128)!=0?32:0)));
            }
            int first=visible.FindIndex(s=>(s.Color&64)==0);
            if(first<0)continue; // A leading CC plane has no base sprite on this scanline.
            bool Hit((int Index,int Row,byte Color,int X) sprite,int screenX)
            {
                int column=screenX-sprite.X;if(column<0 || column>=size*scale)return false;
                column/=scale;int pattern=attributes[sprite.Index*4+2]&(size==16?252:255);
                int address=(patternBase+pattern*8+sprite.Row+(column/8)*16)&131071;
                return (vram[address]&(128>>(column&7)))!=0;
            }
            for(int sprite=visible.Count-1;sprite>=first;sprite--)
            {
                var current=visible[sprite];byte color=(byte)(current.Color&15);if(color==0)continue;
                for(int column=0;column<width;column++)
                {
                    if(!Hit(current,x+column))continue;
                    byte combined=color;
                    for(int next=sprite+1;next<visible.Count && (visible[next].Color&64)!=0;next++)
                        if(Hit(visible[next],x+column))combined|=(byte)(visible[next].Color&15);
                    pixels[line*width+column]=combined;
                }
            }
        }
        return pixels;
    }
}
