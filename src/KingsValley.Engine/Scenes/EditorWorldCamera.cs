using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace KingsValley.Engine.Scenes;

public readonly record struct EditorWorldHit(int Room, Point Cell);
public readonly record struct EditorWorldCameraState(float Scale,Vector2 Translation,Rectangle Viewport);

/// <summary>Logical UI coordinates to native room/cell coordinates in the full-stage canvas.</summary>
public sealed class EditorWorldCamera
{
    private static readonly float[] Steps = { .125f, .25f, .5f, .75f, 1, 1.5f, 2, 3, 4, 6, 8 };
    public float Scale { get; private set; } = 1;
    public Vector2 Translation { get; private set; }
    public Rectangle Viewport { get; private set; }
    public bool Initialized { get; private set; }
    public EditorWorldCameraState Capture() => new(Scale,Translation,Viewport);
    public void Restore(EditorWorldCameraState state)
    {Scale=state.Scale;Translation=state.Translation;Viewport=state.Viewport;Initialized=true;}

    public void Fit(Rectangle viewport, int[,] grid)
    {
        Viewport=viewport;
        var rooms=EditorWorkspaceLayout.WorldRooms(viewport,grid);
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
            if(grid[y,x]>0 && rooms.TryGetValue(grid[y,x],out var bounds))
            {
                Scale=bounds.Width/256f;
                Translation=new Vector2(bounds.X-x*256*Scale,bounds.Y-y*192*Scale);
                Initialized=true;return;
            }
    }
    public void Resize(Rectangle viewport)
    {
        Translation+=(viewport.Center-Viewport.Center).ToVector2();Viewport=viewport;
    }
    public Vector2 ScreenToWorld(Vector2 point) => (point-Translation)/Scale;
    public void Zoom(bool inward,Vector2 anchor)
    {
        Vector2 world=ScreenToWorld(anchor);
        float next=inward?Steps.FirstOrDefault(s=>s>Scale+.001f,Steps[^1]):Steps.LastOrDefault(s=>s<Scale-.001f,Steps[0]);
        next=inward?Math.Max(Scale,next):Math.Min(Scale,next);
        Scale=next;Translation=anchor-world*Scale;
    }
    public void Pan(Point delta) => Translation+=delta.ToVector2();
    public Rectangle RoomBounds(int column,int row) => new(
        (int)MathF.Round(Translation.X+column*256*Scale),(int)MathF.Round(Translation.Y+row*192*Scale),
        (int)MathF.Round(256*Scale),(int)MathF.Round(192*Scale));
    public Dictionary<int,Rectangle> VisibleRooms(int[,] grid)
    {
        var result=new Dictionary<int,Rectangle>();
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            int id=grid[y,x];if(id==0)continue;
            var bounds=RoomBounds(x,y);if(bounds.Intersects(Viewport))result[id]=bounds;
        }
        return result;
    }
    public EditorWorldHit? Hit(Point point,int[,] grid)
    {
        if(!Viewport.Contains(point))return null;
        // Use the rounded rectangles actually drawn, including fractional zoom levels.
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            if(grid[y,x]==0)continue;
            var bounds=RoomBounds(x,y);if(!bounds.Contains(point))continue;
            return new(grid[y,x],new((point.X-bounds.X)*32/bounds.Width,(point.Y-bounds.Y)*24/bounds.Height));
        }
        return null;
    }
    public void CenterRoom(int id,int[,] grid)
    {
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)if(grid[y,x]==id)
        {Translation=Viewport.Center.ToVector2()-new Vector2(x*256+128,y*192+96)*Scale;return;}
    }
    public static Point StageCell(int room,Point local,int[,] grid)
    {
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)if(grid[y,x]==room)return new(x*32+local.X,y*24+local.Y);
        throw new ArgumentException("The room is missing from the stage map.",nameof(room));
    }
    public static EditorWorldHit? HitStageCell(Point cell,int[,] grid)
    {
        if(cell.X is <0 or >=256 || cell.Y is <0 or >=192)return null;
        int room=grid[cell.Y/24,cell.X/32];
        return room==0?null:new(room,new(cell.X%32,cell.Y%24));
    }
    public static IEnumerable<Point> StrokeCells(Point from,Point to)
    {
        int x=from.X,y=from.Y,dx=Math.Abs(to.X-x),dy=-Math.Abs(to.Y-y),sx=x<to.X?1:-1,sy=y<to.Y?1:-1,error=dx+dy;
        while(true)
        {
            yield return new(x,y);if(x==to.X&&y==to.Y)yield break;
            int twice=2*error;if(twice>=dy){error+=dy;x+=sx;}if(twice<=dx){error+=dx;y+=sy;}
        }
    }
}
