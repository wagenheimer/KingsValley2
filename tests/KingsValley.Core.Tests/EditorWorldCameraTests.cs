using KingsValley.Engine.Scenes;
using Microsoft.Xna.Framework;

namespace KingsValley.Core.Tests;

public class EditorWorldCameraTests
{
    [Fact]
    public void FastStrokeCrossesTheRoomBoundaryWithoutSkippingCellsOrCreatingRooms()
    {
        var grid=Grid();
        var from=EditorWorldCamera.StageCell(1,new(30,5),grid);
        var to=EditorWorldCamera.StageCell(2,new(2,5),grid);
        var hits=EditorWorldCamera.StrokeCells(from,to).Select(p=>EditorWorldCamera.HitStageCell(p,grid)).ToArray();
        Assert.Equal(5,hits.Length);
        Assert.Equal(new EditorWorldHit(1,new(31,5)),hits[1]);
        Assert.Equal(new EditorWorldHit(2,new(0,5)),hits[2]);
        Assert.Null(EditorWorldCamera.HitStageCell(new(0,0),grid));
        Assert.Null(EditorWorldCamera.HitStageCell(new(-1,5),grid));
        Assert.Equal(hits.Reverse(),EditorWorldCamera.StrokeCells(to,from).Select(p=>EditorWorldCamera.HitStageCell(p,grid)));
    }
    private static int[,] Grid(){var grid=new int[8,8];grid[3,2]=1;grid[3,3]=2;return grid;}
    [Fact]
    public void AdjacentRoomsResolveTheSameLocalCellToDifferentRoomIds()
    {
        var grid=Grid();var camera=new EditorWorldCamera();camera.Fit(new(200,150,900,600),grid);
        var bounds=camera.VisibleRooms(grid);
        foreach(int id in new[]{1,2})
        {
            var r=bounds[id];var hit=camera.Hit(new(r.X+(int)(28*camera.Scale),r.Y+(int)(44*camera.Scale)),grid);
            Assert.Equal(new EditorWorldHit(id,new(3,5)),hit);
        }
        Assert.Equal(new EditorWorldHit(2,new(0,0)),camera.Hit(new(bounds[2].Left,bounds[2].Top),grid));
        Assert.Null(camera.Hit(new(0,0),grid));
    }
    [Fact]
    public void ZoomAnchorsTheNativeWorldPointAndPanMovesHitTestingWithTheCanvas()
    {
        var grid=Grid();var camera=new EditorWorldCamera();camera.Fit(new(200,150,900,600),grid);
        var anchor=new Vector2(650,450);var before=camera.ScreenToWorld(anchor);
        camera.Zoom(true,anchor);Assert.InRange(Vector2.Distance(before,camera.ScreenToWorld(anchor)),0,.001f);
        var point=camera.VisibleRooms(grid)[1].Center;var hit=camera.Hit(point,grid);
        camera.Pan(new(20,30));Assert.Equal(hit,camera.Hit(point+new Point(20,30),grid));
    }
    [Fact]
    public void ReturningFromPlayTestCanRestoreTheSameZoomPanAndPickedCell()
    {
        var grid=Grid();var camera=new EditorWorldCamera();camera.Fit(new(200,150,900,600),grid);
        camera.Zoom(true,new(650,450));camera.Pan(new(40,-20));
        var restored=new EditorWorldCamera();restored.Restore(camera.Capture());
        Assert.Equal(camera.VisibleRooms(grid),restored.VisibleRooms(grid));
        var point=camera.Viewport.Center;
        Assert.Equal(camera.Hit(point,grid),restored.Hit(point,grid));
        Assert.Equal(camera.Scale,restored.Scale);
    }
    [Fact]
    public void ResizingRetainsTheWorldCenterAndOffCanvasRoomsAreCulled()
    {
        var grid=Grid();var camera=new EditorWorldCamera();camera.Fit(new(200,150,900,600),grid);
        var center=camera.ScreenToWorld(camera.Viewport.Center.ToVector2());
        camera.Resize(new(200,150,1300,800));
        Assert.InRange(Vector2.Distance(center,camera.ScreenToWorld(camera.Viewport.Center.ToVector2())),0,.001f);
        camera.Pan(new(10000,10000));Assert.Empty(camera.VisibleRooms(grid));
        camera.Fit(camera.Viewport,grid);Assert.Equal(2,camera.VisibleRooms(grid).Count);
    }
}
