using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Scenes;
using Microsoft.Xna.Framework;

namespace KingsValley.Core.Tests;

[Collection(Msx1ExtractionTestCollection.Name)]
public class EditorWorkspaceTests
{
    [Theory]
    [InlineData(1600,1000,4)]
    [InlineData(1280,800,3)]
    public void CompactChromeLeavesRoomForTheLargestNativeIntegerPreview(int width,int height,int expectedZoom)
    {
        var layout=EditorWorkspaceLayout.Calculate(width,height);
        Assert.Equal(expectedZoom,layout.Zoom);
        Assert.True(layout.Room.Top>=layout.Canvas.Top);
        Assert.True(layout.Room.Bottom<height-34);
    }
    [Fact]
    public void OverviewKeepsAdjacentRoomsTouchingAndFitsTheWholeStage()
    {
        int[,] grid=new int[8,8];grid[2,3]=1;grid[2,4]=2;grid[3,4]=3;
        var canvas=new Rectangle(200,180,700,480);
        var rooms=EditorWorkspaceLayout.WorldRooms(canvas,grid);
        Assert.Equal(rooms[1].Right,rooms[2].Left);
        Assert.Equal(rooms[2].Bottom,rooms[3].Top);
        Assert.All(rooms.Values,r=>{Assert.True(canvas.Contains(r));Assert.Equal(r.Width*3,r.Height*4);});
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)grid[y,x]=y*8+x+1;
        Assert.All(EditorWorkspaceLayout.WorldRooms(canvas,grid).Values,r=>Assert.True(canvas.Contains(r)));
    }
    private sealed class EditorCollectionFactAttribute : FactAttribute
    {
        public EditorCollectionFactAttribute()
        {
            if(!Directory.Exists(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_EDITOR_COLLECTION")))
                Skip="Set KINGSVALLEY_TEST_EDITOR_COLLECTION to a local collection with validated MSX1, MSX2 and Contest extractions.";
        }
    }

    [EditorCollectionFact]
    public void NativeEditorBackgroundsPreserveCompleteDecorationsAcrossAllThemes()
    {
        string? previous=AudioBootstrap.ExtractedDirectoryOverrideForTests;
        try
        {
            AudioBootstrap.ExtractedDirectoryOverrideForTests=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_EDITOR_COLLECTION");
            var source=EditorTemplateCatalog.Available().Single(s=>s.Variant=="msx1");
            string root=Path.GetDirectoryName(source.Directory)!;
            var assembler=new Msx1EditorRoomAssembler(root);
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                var animator=new Msx1PatternAnimator();animator.EnsureLoaded(Path.Combine(root,"Rooms"));
                using var animations=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Rooms","animations.json")));
                var room=animations.RootElement.GetProperty("rooms").EnumerateObject().First(r=>
                    (int.Parse(r.Name.Substring(1,2))-1)/10+1==pyramid &&
                    r.Value.EnumerateArray().Any(c=>c.GetProperty("type").GetString()=="eye" && c.GetProperty("pat").EnumerateArray().Any(b=>b.GetInt32()!=0)));
                int stage=int.Parse(room.Name.Substring(1,2)),number=int.Parse(room.Name.Substring(5,2));
                var first=assembler.Background(pyramid,stage,number,animator);
                var mural=assembler.Background(pyramid,stage,63,animator);
                Assert.Equal(256*192,first.Length);
                Assert.Equal(256*192,mural.Length);
                bool changed=false;
                for(int tick=0;tick<120&&!changed;tick++)
                {
                    animator.Update(1f/30f,pyramid);
                    changed=!first.SequenceEqual(assembler.Background(pyramid,stage,number,animator));
                }
                Assert.True(changed,$"Theme {pyramid} did not animate its native decoration.");
                Assert.Equal(assembler.Background(pyramid,stage,63,null),assembler.Background(pyramid,stage,64,null));
            }
        }
        finally{AudioBootstrap.ExtractedDirectoryOverrideForTests=previous;GraphicsGenerationStore.Invalidate();}
    }

    [EditorCollectionFact]
    public void AllNormalAndContestTemplatesBecomePortableEditableCopies()
    {
        string? previous=AudioBootstrap.ExtractedDirectoryOverrideForTests;
        try
        {
            AudioBootstrap.ExtractedDirectoryOverrideForTests=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_EDITOR_COLLECTION");
            var sources=EditorTemplateCatalog.Available();
            Assert.Equal(new[]{"msx1","msx2","msx2-contest"},sources.Select(s=>s.Variant));
            foreach(var source in sources)
            {
                Assert.Equal(source.Variant=="msx2-contest"?20:60,source.StageCount);
                for(int number=1;number<=source.StageCount;number++)
                {
                    string file=Path.Combine(source.Directory,$"stage{number:00}.json");
                    byte[] before=File.ReadAllBytes(file);
                    var document=KingsValley.Core.Stages.LevelDocument.Parse(File.ReadAllText(file),source.Variant);
                    var copy=KingsValley.Core.Stages.LevelDocument.Parse(document.Serialize());
                    Assert.Equal(number,copy.Stage.Number);Assert.Equal(source.Variant,copy.SourceVariant);
                    Assert.Equal(before,File.ReadAllBytes(file));
                }
            }
        }
        finally {AudioBootstrap.ExtractedDirectoryOverrideForTests=previous;GraphicsGenerationStore.Invalidate();}
    }
    [Fact]
    public void HighDpiPointerMappingUsesTheSameCoordinateSpaceAsTheUi()
    {
        Assert.Equal(new Point(600,350),EditorWorkspaceLayout.ToUiPoint(new Point(600,350),new Point(1280,720),new Point(2560,1440)));
        Assert.Equal(new Point(1200,700),EditorWorkspaceLayout.ToUiPoint(new Point(1200,700),new Point(1280,720),new Point(1280,720)));
    }
    [Theory]
    [InlineData(1600,1000)] [InlineData(1280,800)] [InlineData(1024,768)] [InlineData(800,768)]
    public void RoomViewportPreservesNativePixelsAndHitTesting(int width,int height)
    {
        var layout=EditorWorkspaceLayout.Calculate(width,height);
        Assert.Equal(256*layout.Zoom,layout.Room.Width);Assert.Equal(192*layout.Zoom,layout.Room.Height);
        Assert.True(layout.Room.Left>=layout.Left.Right);Assert.True(layout.Room.Bottom<=height-36);
        Assert.Equal(new Point(0,0),layout.CellAt(new Point(layout.Room.X,layout.Room.Y)));
        Assert.Equal(new Point(31,23),layout.CellAt(new Point(layout.Room.Right-1,layout.Room.Bottom-1)));
        Assert.Null(layout.CellAt(new Point(layout.Room.Right,layout.Room.Bottom)));
    }

    [Fact]
    public void MerelyFindingAContestRomOrIncompleteGraphicsDoesNotExposeTemplates()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-editor-sources-"+Guid.NewGuid().ToString("N"));
        string? previous=AudioBootstrap.ExtractedDirectoryOverrideForTests;
        try
        {
            AudioBootstrap.ExtractedDirectoryOverrideForTests=root;
            string graphics=AudioBootstrap.GraphicsRootForVariant("msx2-contest");
            Directory.CreateDirectory(graphics);File.WriteAllText(Path.Combine(graphics,"current.json"),"{}");
            Assert.DoesNotContain(EditorTemplateCatalog.Available(),s=>s.Variant=="msx2-contest");
        }
        finally { AudioBootstrap.ExtractedDirectoryOverrideForTests=previous;GraphicsGenerationStore.Invalidate();if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
