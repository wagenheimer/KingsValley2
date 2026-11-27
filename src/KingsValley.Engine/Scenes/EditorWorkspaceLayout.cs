using Microsoft.Xna.Framework;

namespace KingsValley.Engine.Scenes;

/// <summary>Desktop layout in window pixels; native room pixels always use integer zoom.</summary>
public sealed record EditorWorkspaceLayout(Rectangle Left, Rectangle Right, Rectangle Canvas, Rectangle Room, int Zoom)
{
    public static System.Collections.Generic.Dictionary<int,Rectangle> WorldRooms(Rectangle canvas, int[,] grid)
    {
        int minX=7,minY=7,maxX=0,maxY=0;
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)if(grid[y,x]>0)
        {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
        int columns=maxX-minX+1,rows=maxY-minY+1;
        float fit=Math.Min((canvas.Width-32f)/columns/256,(canvas.Height-32f)/rows/192);
        float scale=fit>=1?Math.Max(1,(float)Math.Floor(fit*2)/2):fit>=0.5f?0.5f:fit>=0.25f?0.25f:0.125f;
        int width=(int)(256*scale),height=(int)(192*scale);
        int originX=canvas.Center.X-columns*width/2,originY=canvas.Center.Y-rows*height/2;
        var result=new System.Collections.Generic.Dictionary<int,Rectangle>();
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
            if(grid[y,x]>0)result[grid[y,x]]=new(originX+(x-minX)*width,originY+(y-minY)*height,width,height);
        return result;
    }
    public static int UiScale(int width,int height) => width >= 2200 && height >= 1300 ? 2 : 1;

    public static Point ToUiPoint(Point clientPoint,Point clientSize,Point backBuffer)
    {
        int scale=UiScale(backBuffer.X,backBuffer.Y);
        return new((int)((long)clientPoint.X*backBuffer.X/Math.Max(1,clientSize.X)/scale),
            (int)((long)clientPoint.Y*backBuffer.Y/Math.Max(1,clientSize.Y)/scale));
    }

    public static EditorWorkspaceLayout Calculate(int width, int height, int requestedZoom = 0)
    {
        int left = width >= 1300 ? 192 : 176, right = width >= 1000 ? 224 : 0;
        var canvas = new Rectangle(left + 16, 154, Math.Max(256, width - left - right - 32), Math.Max(192, height - 194));
        int maximum = Math.Max(1, Math.Min(canvas.Width / 256, canvas.Height / 192));
        int zoom = requestedZoom <= 0 ? maximum : Math.Clamp(requestedZoom, 1, maximum);
        var room = new Rectangle(canvas.Center.X - 128 * zoom, canvas.Center.Y - 96 * zoom, 256 * zoom, 192 * zoom);
        return new(new(0, 104, left, height - 140), new(width - right, 104, right, Math.Min(600,height - 140)), canvas, room, zoom);
    }
    public Point? CellAt(Point point) => Room.Contains(point) ? new Point((point.X - Room.X) / Zoom / 8, (point.Y - Room.Y) / Zoom / 8) : null;
}
