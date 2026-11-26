using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace KingsValley.Engine.Graphics;

/// <summary>Runtime application-menu bitmap, never cartridge art, a gameplay sprite or an extraction artifact.</summary>
internal sealed class ApplicationMenuCanvas : IDisposable
{
    private readonly Color[] _pixels=new Color[256*212];
    public Texture2D Texture {get;}=new(Nez.Core.GraphicsDevice,256,212);
    public void Clear(Color color)=>Array.Fill(_pixels,color);
    public void Fill(Rectangle rect,Color color)
    {
        for(int y=Math.Max(0,rect.Y);y<Math.Min(212,rect.Bottom);y++)
        for(int x=Math.Max(0,rect.X);x<Math.Min(256,rect.Right);x++)_pixels[y*256+x]=color;
    }
    public void Text(string text,int x,int y,Color color,int scale=1,int advance=6,bool bold=false)
    {
        foreach(char c in text.ToUpperInvariant())
        {
            var glyph=MsxScreen.GetFallbackGlyph(c);
            if(glyph!=null)for(int row=0;row<7;row++)for(int col=0;col<5;col++)
                if((glyph[row]&(1<<(4-col)))!=0)Fill(new(x+col*scale,y+row*scale,scale+(bold?1:0),scale),color);
            x+=advance*scale;
        }
    }
    public void Mini(string text,int x,int y,Color color)
    {
        foreach(char c in text.ToUpperInvariant())
        {
            ushort glyph=MsxScreen.GetMiniGlyph(c);
            for(int row=0;row<5;row++)for(int col=0;col<3;col++)
                if((glyph&(1<<(14-row*3-col)))!=0)Fill(new(x+col,y+row,1,1),color);
            x+=4;
        }
    }
    public void Commit()=>Texture.SetData(_pixels);
    public void Dispose()=>Texture.Dispose();
}
