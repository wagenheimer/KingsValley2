using System.Linq;
using Xunit;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public class RoomPathfinderTests
{
    [Fact]
    public void WalkOnFlatGround_FindsPath()
    {
        var stage = StageTestData.Stage(StageTestData.Json(new[] { StageTestData.Box() }));
        var session = new StageSession(stage);

        // Player is on the ground (row 20, col 8 = x 64, y 160)
        // Target: col 20 (x 160, y 160)
        var path = RoomPathfinder.FindPath(session.Map, 64, 160, 160, 160);

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.All(path, wp => Assert.Equal(NavAction.Walk, wp.Action));
        Assert.Equal(20, path.Last().Col);
    }

    [Fact]
    public void ClimbLadder_FindsPathUp()
    {
        // Room with a floor on row 22 and a ladder from row 14 to 22 in col 10
        string ladders = "[{\"room\":1,\"col\":10,\"row\":14,\"length\":8}]";
        var stage = StageTestData.Stage(StageTestData.Json(new[] { StageTestData.Box() }, ladders: ladders));
        var session = new StageSession(stage);

        // Position on the ground next to the ladder: x = 80 (col 10), y = 160 (row 20)
        // Target at the top of the ladder: col 10, row 14 (x = 80, y = 112)
        var path = RoomPathfinder.FindPath(session.Map, 80, 160, 80, 112);

        Assert.NotNull(path);
        Assert.Contains(path, wp => wp.Action == NavAction.Climb);
    }

    [Fact]
    public void BlockedByWall_AdjustsOrReturnsNull()
    {
        var box = StageTestData.Box();
        // Walls in the middle
        for (int y = 5; y < 22; y++)
        {
            var arr = box[y].ToCharArray();
            arr[15] = '#';
            arr[16] = '#';
            box[y] = new string(arr);
        }

        var stage = StageTestData.Stage(StageTestData.Json(new[] { box }));
        var session = new StageSession(stage);

        // Start on the left (col 4), target beyond the wall (col 25)
        var path = RoomPathfinder.FindPath(session.Map, 32, 160, 200, 160);

        // There is no passage above or below
        Assert.Null(path);
    }

    [Fact]
    public void Stage01_ClimbAndStepToPlatform()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return; // if it runs in an environment without the ROM folder

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the floor of room 1: x = 128 (col 16), y = 160 (row 20)
        // Target on the left platform: x = 88 (col 11), y = 112 (row 14)
        var path = RoomPathfinder.FindPath(session.Map, 128, 160, 88, 112);

        Assert.NotNull(path);
        Assert.NotEmpty(path);
    }

    [Fact]
    public void Stage01_JumpFromTrenchOntoLedge()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the ledge from the screenshot: x = 96, y = 112 (feet at y = 128)
        session.Player.X = 96;
        session.Player.Y = 112;

        session.Tick(new PlayerInput(Left: true, Right: false, Up: false, Down: false, Action: true));
        for (int i = 0; i < 30; i++)
        {
            var p = session.Player;
            System.Console.WriteLine($"tick {i:D2}: x={p.X} y={p.Y} state={p.State}");
            session.Tick(PlayerInput.None);
        }

        Assert.Equal(PlayerState.Standing, session.Player.State);
        Assert.Equal(96, session.Player.Y);
        Assert.True(session.Player.X < 96);
    }

    [Fact]
    public void Stage01_FromTrenchToSoulStone_FindsJumpPath()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the ledge (x = 96, y = 112)
        // Target on the left Soul Stone (x = 72, y = 96)
        var path = RoomPathfinder.FindPath(session.Map, 96, 112, 72, 96);

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.Contains(path, wp => wp.Action == NavAction.Jump);
    }

    [Fact]
    public void Stage01_FromTrenchToSword_FindsLadderClimbPath()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the ledge (x = 96, y = 112)
        // Sword on the ground below (x = 160, y = 160)
        var path = RoomPathfinder.FindPath(session.Map, 96, 112, 160, 160);

        Assert.NotNull(path);
        foreach (var wp in path)
        {
            System.Console.WriteLine($"WP: col={wp.Col} row={wp.Row} (pxX={wp.PixelX}, pxY={wp.PixelY}) action={wp.Action}");
        }
        Assert.NotEmpty(path);
        Assert.Contains(path, wp => wp.Action == NavAction.Climb);
    }

    [Fact]
    public void Stage01_BottomToTopRightLadder_WrapsVertically()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the floor of room 1 near the lower ladder: x = 180, y = 160 (row 20)
        // Target at the top of the upper right ladder: x = 200, y = 32 (col 25, row 4)
        var path = RoomPathfinder.FindPath(session.Map, 180, 160, 200, 32);

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        // The path must go through the lower ladder (row 22) and the upper ladder (row 0 or 1)
        Assert.Contains(path, wp => wp.Col == 25 && wp.Row >= 21);
        Assert.Contains(path, wp => wp.Col == 25 && wp.Row <= 2);
    }

    [Fact]
    public void Stage01_ClickOnBottomLadder_FindsPath()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the floor of room 1: x = 160, y = 160
        foreach (var (clickX, clickY) in new[] { (204, 180), (208, 180), (200, 184), (204, 188), (208, 188), (204, 172) })
        {
            var p = RoomPathfinder.FindPath(session.Map, 160, 160, clickX, clickY);
            System.Console.WriteLine($"Click ({clickX}, {clickY}): End={(p != null && p.Count > 0 ? $"{p.Last().Col},{p.Last().Row}" : "null")}");
        }
        var pathSoul = RoomPathfinder.FindPath(session.Map, 160, 160, 200, 64);
        System.Console.WriteLine($"Path to SoulStone (200, 64): {(pathSoul != null ? $"Found {pathSoul.Count} wps, first climb={pathSoul.FirstOrDefault(w => w.Action == NavAction.Climb)}" : "NULL")}");
        var path = RoomPathfinder.FindPath(session.Map, 160, 160, 204, 180);
        Assert.NotNull(path);
        Assert.NotEmpty(path);
    }

    [Fact]
    public void Stage01_FromTopLadder_ClickInEmptyShaft_FallsDown()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Tests clicks in the shaft on the right side: col 28 (224), col 29 (232) and wall col 30 (240)
        foreach (int clickX in new[] { 224, 228, 232, 240 })
        {
            var s = new StageSession(stage);
            s.Player.X = 200;
            s.Player.Y = 24;
            s.Player.State = PlayerState.Climbing;
            s.Player.Holding = ItemKind.Pickaxe;

            var navigator = new KingsValley.Engine.Navigation.PointAndClickNavigator();
            bool targetSet = navigator.SetTarget(s, clickX, 80);
            Assert.True(targetSet, $"SetTarget should succeed for right shaft clickX={clickX}");

            for (int t = 0; t < 60; t++)
            {
                var input = navigator.Update(s);
                s.Tick(input);
                if (!navigator.HasPath && s.Player.State != PlayerState.Falling)
                    break;
            }

            // Should go down the ladder, walk to the right, fall through the right shaft and land on the lower floor (row 20 / Y = 160)
            Assert.Equal(160, s.Player.Y);
            Assert.Equal(PlayerState.Standing, s.Player.State);
            Assert.InRange(s.Player.X, 220, 232);
        }
    }

    [Fact]
    public void Stage01_FromTopLadder_ClickOnLeftPlatform_FindsPath()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Player on the upper right ladder: x = 200, y = 16 (col 25, row 2)
        // Clicks on the left platform: x = 160, y = 32 (col 20, row 4)
        var path = RoomPathfinder.FindPath(session.Map, 200, 16, 160, 32);

        System.Console.WriteLine($"Path: {(path != null ? $"Found {path.Count} wps: " + string.Join(" -> ", path.Select(w => $"{w.Col},{w.Row}({w.Action})")) : "NULL")}");
        Assert.NotNull(path);
    }

    [Fact]
    public void Stage01_FromTopLadder_ThroughDugHoleToPyramidStep()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage01.json")))
            return;

        var stage = StageLoader.Load(levelPath, 1);
        var session = new StageSession(stage);

        // Opens the bored hole in columns 23 and 24, rows 6 and 7
        session.Map.Dig(23, 6);
        session.Map.Dig(24, 6);
        session.Map.Dig(23, 7);
        session.Map.Dig(24, 7);

        // Player on the upper ladder at (200, 24)
        session.Player.X = 200;
        session.Player.Y = 24;
        session.Player.State = PlayerState.Climbing;
        session.Player.Holding = ItemKind.Pickaxe;

        var nav = new KingsValley.Engine.Navigation.PointAndClickNavigator();
        bool setTargetOk = nav.SetTarget(session, 176, 80);
        Assert.True(setTargetOk);

        for (int t = 0; t < 60; t++)
        {
            var input = nav.Update(session);
            session.Tick(input);
            if (!nav.HasPath && session.Player.State != PlayerState.Falling)
                break;
        }

        // Should go down the ladder, walk to the left, fall through the hole and land on row 10 (Y = 80)
        Assert.Equal(80, session.Player.Y);
        Assert.Equal(PlayerState.Standing, session.Player.State);
        Assert.InRange(session.Player.X, 176, 184);
    }

    [Fact]
    public void Stage02_FallingThroughBottom_WrapsToTopPlatform()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var stage = StageLoader.Load(levelPath, 2);
        var session = new StageSession(stage);

        // Player at the stage 2 starting position (x = 136, y = 144)
        // Target on the upper platform (x = 104, y = 8 = row 1 standing on row 3)
        var path = RoomPathfinder.FindPath(
            session.Map,
            startPixelX: session.Player.X,
            startPixelY: session.Player.Y,
            targetPixelX: 104,
            targetPixelY: 8,
            canJump: true,
            doors: session.GetRevolvingDoorPassages());

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        // The path must go down the lower shaft and fall through the bottom of the screen (NavAction.Fall with vertical wrap)
        Assert.Contains(path, wp => wp.Action == NavAction.Fall);
    }

    [Fact]
    public void Stage02_RevolvingDoor_CanPushThrough()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var stage = StageLoader.Load(levelPath, 2);
        var session = new StageSession(stage);

        // Swivel door at x = 80, y = 104 (col 10..11, row 13..15)
        // Player on the ground to the left of the door (x = 64, y = 112 = row 14)
        // Target on the other side of the door (x = 104, y = 112 = row 14)
        var path = RoomPathfinder.FindPath(
            session.Map,
            startPixelX: 64,
            startPixelY: 112,
            targetPixelX: 104,
            targetPixelY: 112,
            canJump: false,
            doors: session.GetRevolvingDoorPassages());

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        // Path crosses the door (from col 8/9 to col 12)
        Assert.Contains(path, wp => wp.Col >= 12 && wp.Row == 14);
    }

    [Fact]
    public void Stage02_HorizontalScreenWrap_LeftToRight()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var stage = StageLoader.Load(levelPath, 2);
        var session = new StageSession(stage);

        // Player on the open ledge on the left in rows 4..7 (x = 8, y = 48)
        // Target on the open ledge on the right (x = 240, y = 48)
        var path = RoomPathfinder.FindPath(
            session.Map,
            startPixelX: 8,
            startPixelY: 48,
            targetPixelX: 240,
            targetPixelY: 48,
            canJump: false,
            doors: session.GetRevolvingDoorPassages());

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        // Should use few steps through the side wrap instead of going all the way around
        Assert.True(path.Count <= 5, $"Expected a short route with horizontal wrap, but it had {path.Count} waypoints");
    }

    [Fact]
    public void Stage02_Navigator_FollowsVerticalWrapFall_AndLandsOnTopPlatform()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var stage = StageLoader.Load(levelPath, 2);
        var session = new StageSession(stage);

        var nav = new KingsValley.Engine.Navigation.PointAndClickNavigator();
        bool ok = nav.SetTarget(session, 104, 8);
        Assert.True(ok);

        for (int t = 0; t < 120; t++)
        {
            var input = nav.Update(session);
            session.Tick(input);
            if (!nav.HasPath && session.Player.State != PlayerState.Falling)
                break;
        }

        Assert.Equal(8, session.Player.Y);
        Assert.Equal(PlayerState.Standing, session.Player.State);
    }

    [Fact]
    public void Stage02_Navigator_PushesRevolvingDoor()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";

        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var stage = StageLoader.Load(levelPath, 2);
        var session = new StageSession(stage);

        // Places the player to the left of the swivel door at x = 64, y = 112
        session.Player.X = 64;
        session.Player.Y = 112;

        var nav = new KingsValley.Engine.Navigation.PointAndClickNavigator();
        bool ok = nav.SetTarget(session, 104, 112);
        Assert.True(ok);

        for (int t = 0; t < 120; t++)
        {
            var input = nav.Update(session);
            session.Tick(input);
            if (!nav.HasPath)
                break;
        }

        // Should have pushed the door and reached the other side (x >= 96)
        Assert.True(session.Player.X >= 96, $"Player.X = {session.Player.X}");
    }
}



