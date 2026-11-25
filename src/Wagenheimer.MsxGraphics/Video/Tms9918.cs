namespace Wagenheimer.MsxGraphics.Video;

/// <summary>SCREEN 2 and sprite-mode-1 decoding. Register masking follows the TMS9918A address bus.</summary>
public static class Tms9918
{
    public static readonly (byte R, byte G, byte B)[] Palette =
    {
        (0,0,0),(0,0,0),(33,200,66),(94,220,120),(84,85,237),(125,118,252),(212,82,77),(66,235,245),
        (252,85,84),(255,121,120),(212,193,84),(230,206,128),(33,176,59),(201,91,186),(204,204,204),(255,255,255)
    };

    // Reference: openMSX CharacterConverter::renderGraphic2 and VDP::update*Base,
    // revision fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a. Independently implemented.
    public static byte[] DecodeScreen2(ReadOnlySpan<byte> vram, ReadOnlySpan<byte> registers, bool resolveBackdrop = false)
    {
        if (vram.Length < 0x4000 || registers.Length < 8) throw new ArgumentException("SCREEN 2 requires 16 KiB VRAM and eight registers.");
        int nameBase = (registers[2] & 15) << 10;
        int patternBase = (registers[4] & 4) << 11;
        int colorBase = (registers[3] & 128) << 6;
        int patternMask = ((registers[4] & 3) << 11) | ((registers[3] & 31) << 6) | 63;
        int colorMask = ((registers[3] & 127) << 6) | 63;
        byte[] pixels = new byte[256 * 192];
        for (int y = 0; y < 192; y++)
            for (int tx = 0; tx < 32; tx++)
            {
                int code = vram[nameBase + (y / 8) * 32 + tx];
                int index = (y / 64) * 2048 + code * 8 + (y & 7);
                byte bits = vram[patternBase | (index & patternMask)], colors = vram[colorBase | (index & colorMask)];
                for (int x = 0; x < 8; x++)
                {
                    byte paletteIndex=(byte)((bits & (128 >> x)) != 0 ? colors >> 4 : colors & 15);
                    pixels[y * 256 + tx * 8 + x]=paletteIndex==0 && resolveBackdrop?(byte)(registers[7]&15):paletteIndex;
                }
            }
        return pixels;
    }

    /// <summary>Renders an isolated native SAT and crops an on-screen region without changing the input snapshot.
    /// Sprite size, magnification, pattern base, priority and early clock retain their hardware meaning.</summary>
    public static byte[] DecodeSpriteRegion(ReadOnlySpan<byte> vram, ReadOnlySpan<byte> registers,
        ReadOnlySpan<byte> attributes, int x, int y, int width, int height, bool scanlineLimit = false)
    {
        if(vram.Length<0x4000 || registers.Length<8) throw new ArgumentException("Sprite decoding requires 16 KiB VRAM and eight registers.");
        if(attributes.Length==0 || attributes.Length>128 || attributes.Length%4!=0)
            throw new ArgumentException("An isolated sprite table requires one to thirty-two complete four-byte records.",nameof(attributes));
        if(x<0 || y<0 || width<=0 || height<=0 || width>256 || height>192 || x>256-width || y>192-height)
            throw new ArgumentOutOfRangeException(nameof(width),"The sprite region must fit inside the 256 by 192 display.");
        byte[] isolated=vram[..0x4000].ToArray();
        int sat=(registers[5]&127)<<7;
        attributes.CopyTo(isolated.AsSpan(sat));
        if(attributes.Length<128) isolated[sat+attributes.Length]=208;
        byte[] frame=DecodeSprites(isolated,registers,scanlineLimit),result=new byte[width*height];
        for(int row=0;row<height;row++) frame.AsSpan((y+row)*256+x,width).CopyTo(result.AsSpan(row*width));
        return result;
    }

    /// <summary>Renders 32 sprite attributes in hardware priority, including early clock and Y wrapping.
    /// Color zero is transparent. The optional four-sprites-per-scanline limit matches real hardware.</summary>
    public static byte[] DecodeSprites(ReadOnlySpan<byte> vram, ReadOnlySpan<byte> registers, bool scanlineLimit = true)
    {
        if (vram.Length < 0x4000 || registers.Length < 8) throw new ArgumentException("Sprite decoding requires 16 KiB VRAM and eight registers.");
        byte[] result = new byte[256 * 192];
        int sat = (registers[5] & 127) << 7, patterns = (registers[6] & 7) << 11;
        int size = (registers[1] & 2) != 0 ? 16 : 8, zoom = (registers[1] & 1) + 1;
        int[] counts = new int[192];
        for (int sprite = 0; sprite < 32; sprite++)
        {
            int offset = (sat + sprite * 4) & 0x3fff, rawY = vram[offset];
            if (rawY == 208) break;
            int sy = rawY >= 224 ? rawY - 255 : rawY + 1;
            int sx = vram[(offset + 1) & 0x3fff];
            int attribute = vram[(offset + 3) & 0x3fff], color = attribute & 15;
            if ((attribute & 128) != 0) sx -= 32;
            int pattern = vram[(offset + 2) & 0x3fff];
            if (size == 16) pattern &= ~3;
            for (int y = 0; y < size * zoom; y++)
            {
                int py = sy + y;
                if (py < 0 || py >= 192) continue;
                if (++counts[py] > 4 && scanlineLimit) continue;
                if (color == 0) continue;
                for (int x = 0; x < size * zoom; x++)
                {
                    int px = sx + x, dx = x / zoom, dy = y / zoom;
                    if (px < 0 || px >= 256 || result[py * 256 + px] != 0) continue;
                    int address = patterns + pattern * 8 + (dx / 8) * 16 + dy;
                    if ((vram[address & 0x3fff] & (128 >> (dx & 7))) != 0) result[py * 256 + px] = (byte)color;
                }
            }
        }
        return result;
    }
}
