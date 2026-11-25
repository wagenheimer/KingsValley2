using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public sealed class StageSessionTests
{
    private static StageSession Session(string json) => new(StageTestData.Stage(json));

    private static void Run(StageSession s, PlayerInput input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            s.Tick(input);
    }

    [Fact]
    public void Walk_MovesTwoPixelsPerTick_LikeOriginal()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }));

        Run(s, new PlayerInput(false, true, false, false, false), 10);

        Assert.Equal(64 + 20, s.Player.X);
        Assert.Equal(Facing.Right, s.Player.Facing);
    }

    /// <summary>
    /// Stage 2: the door at (160,72) is born in phase 3 (byte 5 of the record = 3) — it is pushed
    /// from the RIGHT side (holding left), like the ROM's `st.Right`; the one at (80,104) is born in phase 0.
    /// </summary>
    [Fact]
    public void RealStage02_Phase3Door_IsPushedFromTheRight()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;

        var s = new StageSession(StageLoader.Load(levelPath, 2));
        s.PlacePlayer(1, 176, 80);
        var door = s.RoomMechanisms.First(m => m.Position.X == 160 && m.Position.Y == 72);
        Assert.Equal(3, s.MechanismPhase(door));

        Run(s, new PlayerInput(true, false, false, false, false), 40);
        Assert.True(s.Player.X < 176, $"x={s.Player.X}");
        Assert.True(s.MechanismPhase(door) < 3, $"phase={s.MechanismPhase(door)}");
    }

    /// <summary>
    /// E600 type 5 (pushable stone): the 2×2 wall follows the stone (the ROM re-stamps it in
    /// <c>room_draw</c>); before, the fixed wall from the load was left behind after pushing.
    /// </summary>
    [Fact]
    public void RealStage20_StoneWallFollowsTheBlock_WhenPushed()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage20.json")))
            return;

        var s = new StageSession(StageLoader.Load(levelPath, 20));
        var block = s.Enemies.First(e => e.Kind == EnemyKind.MovableBlock && e.Room == s.CurrentRoom);
        int x0 = block.X, y0 = block.Y;

        // the stone blocks its own 2x2 cells
        Assert.True(s.Map.IsSolid(x0, y0), $"the stone at ({x0},{y0}) should block");

        // pushes to the right until it changes cell
        s.Player.X = x0 - 16;
        s.Player.Y = y0;
        Run(s, new PlayerInput(false, true, false, false, false), 80);

        Assert.True(block.X > x0, $"the stone did not move: {block.X}");
        if ((block.X >> 3) != (x0 >> 3))
        {
            Assert.False(s.Map.IsSolid(x0, y0), $"wall left behind at ({x0},{y0})");
            Assert.True(s.Map.IsSolid(block.X, block.Y), $"the stone at ({block.X},{block.Y}) should block");
        }
    }

    /// <summary>
    /// 0xBD21 <c>tick_trap</c> (E600 type 4, stage 7+): the 4-cell bar is <b>floor</b>. Vick on
    /// top of it arms it; stepping off the top while standing the <c>IX+6</c> adds 1 (the <c>IX+0x0B</c> only counts on the next
    /// tick) and, on reaching <b>2</b> — the second exit —, it fires (sound 0x27), the 4 cells of the
    /// map are zeroed (<c>trap_erase</c>) and the floor disappears.
    /// </summary>
    [Fact]
    public void RealStage07_TrapIsFloor_AndFiresOnTheSecondTimeTheVickLeavesIt()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage07.json")))
            return;

        var s = new StageSession(StageLoader.Load(levelPath, 7));
        // stage 7 starts in room 2, which has 2 traps (the one at y=144 is the bridge over the gap)
        var trap = s.RoomMechanisms.First(m => m.Kind == MechanismKind.Unknown4 && m.Position.Y == 144);

        // the 4 cells of the bar (col 14..17, row 18) are wall in the map
        for (int c = 14; c <= 17; c++)
            Assert.True(s.Map.IsSolid(c * 8, 144), $"cell ({c},18) should be the bar");

        // on top of the bar: feet at 160 -> Y = 144 - 16 = 128
        s.Player.X = 112;
        s.Player.Y = 128;
        Run(s, PlayerInput.None, 3);
        Assert.False(s.IsMechanismSpent(trap));

        int fires = 0;

        // 1st exit: arms and disarms once — IX+6 = 1, does not fire yet
        for (int i = 0; i < 20; i++)
        {
            s.Tick(new PlayerInput(false, true, false, false, false));
            fires += s.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.Trap);
        }

        Assert.Equal(0, fires);
        Assert.False(s.IsMechanismSpent(trap));

        // goes back on top of the bar and leaves again: IX+6 reaches 2 and it fires
        for (int i = 0; i < 40; i++)
        {
            s.Tick(new PlayerInput(true, false, false, false, false));
            fires += s.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.Trap);
        }

        for (int i = 0; i < 20; i++)
        {
            s.Tick(new PlayerInput(false, true, false, false, false));
            fires += s.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.Trap);
        }

        Assert.Equal(1, fires);
        Assert.True(s.IsMechanismSpent(trap));
        for (int c = 14; c <= 17; c++)
            Assert.False(s.Map.IsSolid(c * 8, 144), $"cell ({c},18) should have been zeroed");
    }

    /// <summary>
    /// Stage 3 has only its sarcophagus enemy (Gold Mummy) in room 1. E600 in room 2 contains mechanisms
    /// (the type 2 one-way swivel door at (160, 104)), NOT enemies.
    /// </summary>
    [Fact]
    public void RealStage03_HasNoEnemiesInRoom2_AndHasOneWaySwivelDoorMechanism()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage03.json")))
            return;

        var s = new StageSession(StageLoader.Load(levelPath, 3));

        // No dynamic enemy spawns at start in room 2:
        Assert.DoesNotContain(s.Enemies, e => e.Room == 2);

        // Mechanism 2 (one-way swivel door) is present in room 2:
        var door = Assert.Single(s.Definition.Mechanisms, m => m.Kind == MechanismKind.Unknown2 && m.Position.Room == 2);
        Assert.Equal(160, door.Position.X);
        Assert.Equal(104, door.Position.Y);
        Assert.Equal(1, door.Direction); // pushable left
    }

    /// <summary>
    /// 0xA826 <c>step_wrap</c>: the tool's X is a <b>byte</b> — when it overflows 0/0xFF it changes
    /// room (<c>screen_slot</c> + <c>room_link</c>, <c>IX+3</c>) and keeps flying; and
    /// <c>CheckRoomExit</c> can no longer clear the list (the original keeps the tool per room).
    /// Without that the knife thrown at an open border (like the door rows on stage 2) vanished.
    /// </summary>
    [Fact]
    public void Knife_LeavingTheRoom_ContinuesInTheNeighbour_InsteadOfVanishing()
    {
        var rows1 = StageTestData.Box();
        var rows2 = StageTestData.Box();
        // rows 20 and 21 (= the throw Y and the probe's Y+12) without the right wall: the knife leaves
        rows1[20] = "##" + new string('.', 28) + "..";
        rows1[21] = rows1[20];
        rows2[20] = rows1[20];
        rows2[21] = rows1[20];
        var s = Session(StageTestData.Json(new[] { rows1, rows2 },
            grid: "[[1,2,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]",
            items: "[{\"type\":1,\"room\":1,\"y\":160,\"x\":64}]"));

        s.Tick(PlayerInput.None);                                    // picks up the knife
        s.Tick(new PlayerInput(false, false, false, false, true));    // arremessa
        Run(s, PlayerInput.None, 20);                                 // 8 de arremesso + voo
        Assert.Contains(s.Projectiles, p => p.Room == 1);

        // flies until it overflows the border (64 + 0x10 = 80 -> 240 in ~40 ticks) and passes to room 2
        Run(s, PlayerInput.None, 40);
        Assert.Contains(s.Projectiles, p => p.Room == 2);
        Assert.DoesNotContain(s.Projectiles, p => p.Room == 1);

        // changing room does not erase the tool (the original keeps IX+3 per room)
        s.Player.X = 200;
        Run(s, new PlayerInput(false, true, false, false, false), 40);
        Assert.Equal(2, s.CurrentRoom);
        Assert.NotEmpty(s.Projectiles);
    }

    /// <summary>
    /// 63DA..6438: the ROM writes <b>WALL</b> into the map on the cells a mechanism occupies (the extractor's
    /// JSON brings the terrain *before* that). On stage 2 there are 2 swivel doors (type 1, 2x3):
    /// (160,72) = col 20-21 / rows 9-11 and (80,104) = col 10-11 / rows 13-15.
    /// <para>
    /// Crossing the bar <b>by pushing</b> is the mechanic (A350/A36C <c>vic_pull_l/r</c>: the ROM moves
    /// Vick 2 px per call, 18 calls, <b>without</b> a wall probe); what it cannot do is cross the
    /// <b>terrain wall</b> next to it.
    /// </para>
    /// </summary>
    [Fact]
    public void RealStage02_MechanismCellsAreSolid_LikeTheRomWritesAtRuntime()
    {
        string levelPath = @"..\..\..\..\..\src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.Directory.Exists(levelPath))
            levelPath = @"src\KingsValley.Desktop\Content\Rom\Levels";
        if (!System.IO.File.Exists(System.IO.Path.Combine(levelPath, "stage02.json")))
            return;   // without the extracted ROM data the test does not run

        var s = new StageSession(StageLoader.Load(levelPath, 2));

        // the cells of the 2 bars become wall in the map, as the ROM writes at runtime
        foreach (var (cx, cy) in new[] { (20, 9), (21, 9), (20, 11), (21, 11), (10, 13), (11, 15) })
            Assert.True(s.Map.IsSolid(cx * 8, cy * 8), $"cell ({cx},{cy}) should be wall");

        // pushing (holding the direction) the bar's phase advances — that is how it is crossed
        var door = s.RoomMechanisms.First(m => m.Position.X == 80 && m.Position.Y == 104);
        Assert.Equal(0, s.MechanismPhase(door));
        s.Player.X = 64;
        s.Player.Y = 112;
        Run(s, new PlayerInput(false, true, false, false, false), 40);
        Assert.True(s.MechanismPhase(door) > 0, "the bar should have rotated when pushed");

        // the TERRAIN wall next to the door (col 8-9, rows 8-11): standing on row 12 (y=80),
        // walking left from col 12 (x=96) he stops before col 9 — before, he was
        // dragged by the door's "pull" (2 px/call without a probe), which grabbed Vick 3 rows above
        s.Player.X = 96;
        s.Player.Y = 80;
        Run(s, new PlayerInput(true, false, false, false, false), 20);
        Assert.True(s.Player.X >= 80, $"Vick crossed the terrain wall: x={s.Player.X}");

        // without pressing a direction Vick does not enter the bar (the runtime wall holds him)
        s.Player.X = 64;
        s.Player.Y = 112;
        Run(s, PlayerInput.None, 20);
        Assert.Equal(64, s.Player.X);
    }

    /// <summary>
    /// 0xA000 <c>vic_enter_fall</c> does <c>jp sfx_3c</c> ONCE per entry into the fall state, and the
    /// landing (0xA2F0 → <c>jp sfx_38</c>) another: walking on flat ground plays nothing and falling off the edge plays
    /// each sound exactly once (the fall sound is long — 1.85 s in the extracted PSX —, so what
    /// it cannot do is retrigger).
    /// </summary>
    [Fact]
    public void FallingOffALedge_PlaysEachSoundOnce_AndWalkingPlaysNone()
    {
        // flat: no fall/landing sound in 120 ticks of walking
        var flat = Session(StageTestData.Json(new[] { StageTestData.Box() }));
        int flatFalls = 0, flatLands = 0;
        for (int i = 0; i < 120; i++)
        {
            flat.Tick(new PlayerInput(false, true, false, false, false));
            flatFalls += flat.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.PlayerFall);
            flatLands += flat.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.PlayerLand);
        }

        Assert.Equal(0, flatFalls);
        Assert.Equal(0, flatLands);

        // edge: platform on row 20 up to column 11; Vick starts on top (y=144) and, passing
        // column 11, falls two cells to the floor on row 22 (y=160)
        var rows = StageTestData.Box();
        rows[20] = "##" + new string('=', 10) + new string('.', 18) + "##";
        var s = Session(StageTestData.Json(new[] { rows }, player: "{\"room\":1,\"y\":144,\"x\":64}"));

        int falls = 0, lands = 0;
        for (int i = 0; i < 200; i++)
        {
            s.Tick(new PlayerInput(false, true, false, false, false));
            falls += s.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.PlayerFall);
            lands += s.Events.Count(e => e.Kind == GameEventKind.Sound && e.Sound == OriginalSounds.PlayerLand);
        }

        Assert.Equal(1, falls);
        Assert.Equal(1, lands);
    }
    [Fact]
    public void Jump_FollowsMeasuredArc_AndLands()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }));
        var ys = new List<int>();

        s.Tick(new PlayerInput(false, false, false, false, true));
        for (int i = 0; i < 16; i++)
        {
            s.Tick(PlayerInput.None);
            ys.Add(s.Player.Y);
        }

        Assert.Equal(160 - 14, ys.Min());
        Assert.Equal(160, ys[^1]);
        Assert.Equal(PlayerState.Standing, s.Player.State);
    }

    [Fact]
    public void CollectingAllSoulStones_OpensDoor_AndStandingAtDoorClearsStage()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            stones: "[{\"room\":1,\"y\":160,\"x\":64}]", door: "{\"room\":1,\"y\":144,\"x\":56,\"w\":4,\"h\":4}"));

        // 8FBE: with Vick on top of the open door the game enters by itself (sound 0x23) and releases E257.
        s.Tick(PlayerInput.None);
        Assert.True(s.DoorOpen);
        Assert.Equal(Scoring.SoulStone, s.Score);
        Assert.Equal(0, s.DoorStuds);
        Assert.Equal(ExitPhase.DoorOpening, s.Exit);
        Assert.Equal(0, s.DoorFrame);

        // 71F6: the door opens one frame every 30 CALL units (E2F7 = 0x1E). 1 ROM call
        // = 1 port tick, so it is 1 unit per tick (t = 0 on the first StepExit).
        Run(s, PlayerInput.None, 30);
        Assert.Equal(0, s.DoorFrame);            // t=0..29: still the closed frame
        Run(s, PlayerInput.None, 1);
        Assert.Equal(1, s.DoorFrame);            // 7207: E2F6++ e redesenha (9010)
        Run(s, PlayerInput.None, 29);
        Assert.Equal(1, s.DoorFrame);            // t=59: still the second frame
        Run(s, PlayerInput.None, 29);
        Assert.Equal(2, s.DoorFrame);            // t=60..88: third frame (E2F0 = 2)
        Assert.Equal(ExitPhase.DoorOpening, s.Exit);

        Run(s, PlayerInput.None, 1);
        Assert.Equal(ExitPhase.Entering, s.Exit);      // 7214: E257 = 2 (90 door units)

        // 7233/7244: each group (E258) lasts 32 units and the pose pair is interleaved — bit 4 of
        // E259 is 1 in the FIRST half (E259 counts down from 0x20), so the order is 1,0,3,2,...
        // and on each half change Vick rises 2 px (7235).
        int y0 = s.Player.Y;
        Assert.Equal(0, s.Player.Pose);                // Clear() zeroed it and the entry has not run yet
        Run(s, PlayerInput.None, 1);
        Assert.Equal(1, s.Player.Pose);                // t=0: group 0, first half
        Assert.Equal(y0, s.Player.Y);
        Run(s, PlayerInput.None, 15);
        Assert.Equal(1, s.Player.Pose);                // t=15: end of the first half
        Assert.Equal(y0 - 2, s.Player.Y);              // 7235: rises 2 px on the turn
        Run(s, PlayerInput.None, 16);
        Assert.Equal(2, s.Player.Pose);                // t=31: group transition (u=31)
        Assert.Equal(y0 - 4, s.Player.Y);

        // 722A (CP 4): 4 steps of E258 = 128 units; poses 5..7 have no door art (Vick vanished).
        Run(s, PlayerInput.None, 96);
        Assert.Equal(7, s.Player.Pose);                // t=112..127: group 3, final transition (clamp)
        Assert.Equal(y0 - 14, s.Player.Y);             // 7 subidas de 2 px
        Assert.Equal(ExitPhase.Curtain, s.Exit);       // 7288: clears the SAT (5D41) and E257 = 3
        Assert.Equal(0, s.CurtainBands);

        // 7296: the curtain closes one 8 px band on each side every 4 units (E259 = 4).
        Run(s, PlayerInput.None, 4);
        Assert.Equal(1, s.CurtainBands);
        Assert.Equal(StageStatus.Playing, s.Status);
        Run(s, PlayerInput.None, 44);
        Assert.Equal(12, s.CurtainBands);
        Assert.Equal(ExitPhase.ClearScreen, s.Exit);   // 72E1: E257 = 4 — the stage-end screen
        Assert.Equal(ExitClearStep.Texts, s.ClearStep);
        Assert.Equal(StageStatus.Playing, s.Status);   // the stage only changes after the screen (7342)
    }

    [Fact]
    public void ClearScreen_ShowsTextThenPassword_AndWaitsForAKey()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            stones: "[{\"room\":1,\"y\":160,\"x\":64}]", door: "{\"room\":1,\"y\":144,\"x\":56,\"w\":4,\"h\":4}"));

        // 8FBE: Vick is born on top of the door; the whole exit (door + entry + curtain) lasts 266
        // units, and that is where the stage-end screen starts (72E1).
        s.Tick(PlayerInput.None);
        RunUntil(s, PlayerInput.None, x => x.Exit == ExitPhase.ClearScreen);
        Assert.Equal(ExitClearStep.Texts, s.ClearStep);
        Assert.Equal(12, s.CurtainBands);

        // 72F5 (E204 = 0x3C) and 732D (E204 = 0x78): the text stays 180 units = 180 ticks (6 s).
        int toPassword = RunUntil(s, PlayerInput.None, x => x.ClearStep == ExitClearStep.KeyWait);
        Assert.Equal(180, toPassword);                 // 7331: CALL 697C draws the password

        // 733B: LD A,(0xE207); AND A; RET z — without a key the game stays on screen (E257 does not advance).
        Run(s, PlayerInput.None, 120);
        Assert.Equal(ExitClearStep.KeyWait, s.ClearStep);
        Assert.Equal(StageStatus.Playing, s.Status);

        // 730B: with the key, E204 = its code and E257 = 8; 7342 waits and zeroes E257.
        s.Tick(new PlayerInput(false, false, false, false, true));
        Assert.Equal(ExitClearStep.Tail, s.ClearStep);
        Assert.Equal(StageStatus.Playing, s.Status);
        int toEnd = RunUntil(s, PlayerInput.None, x => x.Status == StageStatus.Cleared);
        Assert.Equal(32, toEnd);                       // 32 units (E204 = the key code)
    }

    /// <summary>
    /// 7244: the entry pose is the raw index <c>2*E258 + bit4(E259)</c> and <c>E259</c> counts down from
    /// <c>0x20</c>, so the bit is 1 in 0x1F..0x10 and 0 in 0x0F..0x01 and on the reload (0x20) —
    /// which coincides with the rise of the next group: the real order is
    /// <b>1, 0, 2, 3, 2, 4, 5, 4, 6, 7, 6</b> (poses 2/4/6 are the group transition ones, of 1 frame).
    /// </summary>
    [Fact]
    public void EnteringTheDoor_CyclesTheRomPoses_InInterleavedOrder()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            stones: "[{\"room\":1,\"y\":160,\"x\":64}]", door: "{\"room\":1,\"y\":144,\"x\":56,\"w\":4,\"h\":4}"));

        RunUntil(s, PlayerInput.None, x => x.Exit == ExitPhase.Entering);

        var poses = new List<int>();
        var last = -1;
        // the whole sequence has 266 units and the entry only 128 (64 ticks); we stop at the curtain.
        while (s.Exit == ExitPhase.Entering && poses.Count < 200)
        {
            s.Tick(PlayerInput.None);
            if (s.Player.Pose != last)
            {
                last = s.Player.Pose;
                poses.Add(last);
            }
        }

        Assert.Equal(new[] { 1, 0, 2, 3, 2, 4, 5, 4, 6, 7, 6, 7 }, poses);
        Assert.Equal(7, s.Player.Pose);                // the last group ends on the final transition (clamp)
        Assert.Equal(ExitPhase.Curtain, s.Exit);
    }

    /// <summary>
    /// The swivel door rotates when Vick pushes: the phase (0..3) advances while the player stays
    /// adjacent and presses the direction, and the timer (10/8) must reach zero between steps.
    /// </summary>
    [Fact]
    public void RotatingDoor_AdvancesPhase_WhenPushed()
    {
        var rows = StageTestData.Box();
        rows[16] = "##" + new string('=', 28) + "##";   // floor at y=128 for Vick to stand on
        var mechanisms = "[{\"type\":1,\"room\":1,\"y\":104,\"x\":80,\"raw\":\"0100685001000a000301\"}]";
        var s = Session(StageTestData.Json(new[] { rows }, mechanisms: mechanisms,
            player: "{\"room\":1,\"y\":112,\"x\":64}"));

        var m = s.Definition.Mechanisms[0];
        Run(s, new PlayerInput(false, true, false, false, false), 40);
        Assert.True(s.MechanismPhase(m) > 0, $"phase={s.MechanismPhase(m)} x={s.Player.X} y={s.Player.Y} state={s.Player.State} (expected to advance)");
    }

    [Fact]
    public void RotatingDoor_BlocksPlayer_WhenNotPushable()
    {
        var rows = StageTestData.Box();
        rows[16] = "##" + new string('=', 28) + "##";
        var mechanisms = "[{\"type\":1,\"room\":1,\"y\":104,\"x\":80,\"raw\":\"0100685001000a000301\"}]";
        var s = Session(StageTestData.Json(new[] { rows }, mechanisms: mechanisms,
            player: "{\"room\":1,\"y\":112,\"x\":104}"));

        // Walk left towards x=80: door is 16 wide (x=80..95). Vick starts at x=104, walks left.
        // He should be blocked at the right edge of the door (x >= 96) and NOT pass through!
        Run(s, new PlayerInput(true, false, false, false, false), 40);
        Assert.True(s.Player.X >= 96, $"Vick x={s.Player.X} must be blocked by the rotating door");
    }

    /// <summary>Runs ticks until the condition holds; returns how many ticks it took.</summary>
    private static int RunUntil(StageSession s, PlayerInput input, Func<StageSession, bool> stop, int max = 600)
    {
        for (int i = 1; i <= max; i++)
        {
            s.Tick(input);
            if (stop(s))
                return i;
        }

        throw new InvalidOperationException($"condition not reached in {max} ticks");
    }

    [Fact]
    public void EnteringTheDoor_PlaysTheFanfare_AndFreezesTheRoom()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            coffins: StageTestData.Coffin(1, 200, 160),
            stones: "[{\"room\":1,\"y\":160,\"x\":64}]", door: "{\"room\":1,\"y\":144,\"x\":56,\"w\":4,\"h\":4}"));
        var coffin = s.Coffins[0];

        // 8FBE: Vick is already born on top of the door, so the exit starts on the 1st tick.
        s.Tick(PlayerInput.None);
        Assert.Contains(s.Events, e => e.Kind == GameEventKind.DoorEntered && e.Sound == OriginalSounds.DoorEnter);
        Assert.Equal(CoffinLook.Closed, coffin.Look);

        // 71DE/49B4: during the exit the original only runs the E257 machine — the rest of the room freezes.
        // Without the freeze, in 100 ticks the sarcophagus would already have opened and the enemy come out (671D: 0x14x4).
        // 100 ticks = 100 units: 90 of the door + 10 of the entry (still in Entering).
        Run(s, PlayerInput.None, 100);
        Assert.Equal(ExitPhase.Entering, s.Exit);
        Assert.Equal(CoffinLook.Closed, coffin.Look);
        Assert.Empty(s.Enemies);
    }

    [Fact]
    public void Pickaxe_DigsDownWhileTheFloorBelowIsDiggable()
    {
        var rows = StageTestData.Box();
        rows[20] = "##" + new string('.', 6) + new string('=', 22) + "##";
        rows[21] = rows[20];
        rows[22] = rows[20];
        rows[23] = new string('#', 32);
        var s = Session(StageTestData.Json(new[] { rows },
            items: "[{\"type\":4,\"room\":1,\"y\":144,\"x\":64}]", player: "{\"room\":1,\"y\":144,\"x\":64}"));

        s.Tick(PlayerInput.None); // picks up the pickaxe
        Assert.Equal(ItemKind.Pickaxe, s.Player.Holding);
        s.Tick(new PlayerInput(false, false, false, false, true));
        Run(s, PlayerInput.None, 3 * 32 + 2);

        // A0A6/AB31: pair of cells ahead-below (columns 9 and 10), row by row until the wall.
        Assert.Equal(ItemKind.None, s.Player.Holding);
        for (int r = 20; r <= 22; r++)
        {
            Assert.Equal(TerrainKind.Empty, s.Map.Get(9, r));
            Assert.Equal(TerrainKind.Empty, s.Map.Get(10, r));
        }

        Assert.Equal(TerrainKind.Soft, s.Map.Get(8, 20));
        Assert.Equal(TerrainKind.Soft, s.Map.Get(11, 20));
        Assert.Equal(TerrainKind.Wall, s.Map.Get(9, 23));
    }

    [Fact]
    public void Hammer_BreaksOneColumnOfWallAhead()
    {
        var rows = StageTestData.Box();
        for (int y = 2; y < 22; y++)
            rows[y] = "##" + new string('.', 8) + "==" + new string('.', 18) + "##";
        var s = Session(StageTestData.Json(new[] { rows },
            items: "[{\"type\":5,\"room\":1,\"y\":160,\"x\":64}]", player: "{\"room\":1,\"y\":160,\"x\":64}"));

        s.Tick(PlayerInput.None);
        s.Tick(new PlayerInput(false, true, false, false, false)); // walks to the wall
        Run(s, new PlayerInput(false, false, false, false, true), 1);
        Run(s, PlayerInput.None, Physics.DigTicks);

        Assert.Equal(TerrainKind.Empty, s.Map.Get(10, 20));
        Assert.Equal(TerrainKind.Soft, s.Map.Get(11, 20));
    }

    [Fact]
    public void Knife_KillsMummy_AndDropsToFloor()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            items: "[{\"type\":1,\"room\":1,\"y\":160,\"x\":64}]",
            coffins: StageTestData.Coffin(1, 176, 160)));

        Run(s, PlayerInput.None, StageTestData.SpawnTicks);
        Assert.Equal(ItemKind.Knife, s.Player.Holding);
        s.Tick(new PlayerInput(false, false, false, false, true));
        // 8 of throw + ~21 of flight (4 px/tick) + 1 of tool_lock + 24 of wait + landing
        Run(s, PlayerInput.None, 90);

        Assert.DoesNotContain(s.Enemies, e => e.Alive);
        Assert.Contains(s.Items, i => i.Kind == ItemKind.Knife);
    }

    [Fact]
    public void Throw_HoldsPlayerEightTicks_BeforeKnifeLeaves()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            items: "[{\"type\":1,\"room\":1,\"y\":160,\"x\":64}]"));

        s.Tick(PlayerInput.None);
        s.Tick(new PlayerInput(false, false, false, false, true));
        Run(s, PlayerInput.None, Physics.ThrowTicks - 1);  // A384: E286 = 8 calls until throw_done
        Assert.Empty(s.Projectiles);
        Run(s, PlayerInput.None, 1);
        Assert.Single(s.Projectiles);
        Assert.Equal(ProjectilePhase.Flying, s.Projectiles[0].Phase);
    }

    [Fact]
    public void TouchingMummy_KillsPlayer_UnlessInvincible()
    {
        var json = StageTestData.Json(new[] { StageTestData.Box() }, coffins: StageTestData.Coffin(1, 96, 160));
        var normal = Session(json);
        var festival = new StageSession(StageTestData.Stage(json)) { Invincible = true };

        Run(normal, PlayerInput.None, StageTestData.SpawnTicks + 120);
        Run(festival, PlayerInput.None, StageTestData.SpawnTicks + 120);

        Assert.Equal(StageStatus.Dead, normal.Status);
        Assert.Equal(StageStatus.Playing, festival.Status);
    }

    [Fact]
    public void DeathEndTick_StillUpdatesEnemies()
    {
        // Measured on the emulator trace (work/tr3 = stage 3, type 2): on the frame in which the death
        // animation ends the enemy still moves; what freezes it is the next state ("lost a life"),
        // on the following frame. Without that the enemy stopped 1 px before the original.
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            coffins: StageTestData.Coffin(1, 200, 160), player: "{\"room\":1,\"y\":40,\"x\":64}"));

        Run(s, PlayerInput.None, StageTestData.SpawnTicks);
        var mummy = Assert.Single(s.Enemies);
        Assert.Equal(EnemyState.Walking, mummy.State);

        s.Suicide();   // F5 of the original: dies without the mummy touching
        int guard = 0;
        while (s.Status == StageStatus.Playing && guard++ < 400)
        {
            int x = mummy.X, y = mummy.Y;
            s.Tick(PlayerInput.None);
            if (s.Status != StageStatus.Dead)
                continue;

            Assert.True(mummy.X != x || mummy.Y != y, "the frame that ends the death did not move the enemy");
            return;
        }

        Assert.Fail("the death did not finish");
    }

    [Fact]
    public void Mummy_PausesFacingTheWall_ThenTurns_LikeRom()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            coffins: StageTestData.Coffin(1, 200, 160), player: "{\"room\":1,\"y\":40,\"x\":64}"));

        Run(s, PlayerInput.None, StageTestData.SpawnTicks);
        var mummy = Assert.Single(s.Enemies);
        Assert.Equal(EnemyState.Walking, mummy.State);
        int guard = 0;
        while (mummy.State != EnemyState.Pausing && guard++ < 400)
            s.Tick(PlayerInput.None);

        // ADEA: touches the wall (x snapped to the cell) and stays 0x10 ticks facing it.
        Assert.Equal(Facing.Left, mummy.Facing);
        Assert.Equal(16, mummy.X);
        Run(s, PlayerInput.None, 0x10 - 1);
        Assert.Equal(16, mummy.X);
        Assert.Equal(EnemyState.Pausing, mummy.State);
        s.Tick(PlayerInput.None);
        Assert.Equal(EnemyState.Walking, mummy.State);
        Assert.Equal(Facing.Right, mummy.Facing);
    }

    [Fact]
    public void Coffin_ClosesAfterTheMummyLeaves()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            coffins: StageTestData.Coffin(1, 200, 160), player: "{\"room\":1,\"y\":40,\"x\":64}"));
        var coffin = s.Coffins[0];

        Run(s, PlayerInput.None, 0x14 * 4);
        Assert.Equal(CoffinLook.Ajar, coffin.Look);
        Run(s, PlayerInput.None, 0x20);
        Assert.Equal(CoffinLook.Open, coffin.Look);
        Run(s, PlayerInput.None, 0x20);
        Assert.Equal(CoffinLook.Closed, coffin.Look);
        Assert.Equal(160, s.Enemies[0].Y);   // born at the record's y, standing on the floor
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void OnlyType2_ClimbsLadderTowardPlayer(int type, bool climbs)
    {
        var rows = StageTestData.Box();
        rows[14] = "##" + new string('#', 28) + "##";
        var s = Session(StageTestData.Json(new[] { rows },
            ladders: "[{\"room\":1,\"col\":14,\"row\":14,\"length\":8}]",
            coffins: StageTestData.Coffin(type, 200, 160), player: "{\"room\":1,\"y\":96,\"x\":40}"));

        Run(s, PlayerInput.None, StageTestData.SpawnTicks + 150);

        var e = s.Enemies.Single();
        Assert.Equal(climbs, e.Y < 150);
    }

    [Fact]
    public void JumpWhileWalking_Covers1_25PixelsPerTick()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }, player: "{\"room\":1,\"y\":160,\"x\":120}"));

        s.Tick(new PlayerInput(true, false, false, false, true));
        Run(s, new PlayerInput(true, false, false, false, false), 15);

        Assert.InRange(s.Player.X, 120 - 20, 120 - 18);
    }

    [Fact]
    public void Ladder_ClimbsAndStandsOnTop()
    {
        var rows = StageTestData.Box();
        rows[14] = "##" + new string('#', 28) + "##";
        var s = Session(StageTestData.Json(new[] { rows },
            ladders: "[{\"room\":1,\"col\":8,\"row\":14,\"length\":8}]", player: "{\"room\":1,\"y\":160,\"x\":66}"));

        Run(s, new PlayerInput(false, false, true, false, false), 40);

        // Ladder from row 14 to 21: at the top the feet are at y = 14 * 8 = 112.
        Assert.Equal(64, s.Player.X);
        Assert.Equal(112 - 16, s.Player.Y);
        Assert.Equal(PlayerState.Standing, s.Player.State);
    }

    /// <summary>Ladder that crosses the screen: climbing through the ceiling Vick enters the room above.</summary>
    [Fact]
    public void Ladder_CrossingTheScreen_TakesThePlayerToTheRoomAbove()
    {
        var grid = "[[1,0,0,0,0,0,0,0],[2,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]";
        var s = Session(StageTestData.Json(new[] { StageTestData.Box(), StageTestData.Box() },
            grid: grid,
            ladders: "[{\"room\":1,\"col\":8,\"row\":0,\"length\":24},{\"room\":2,\"col\":8,\"row\":20,\"length\":4}]",
            player: "{\"room\":1,\"y\":160,\"x\":66}"));

        Run(s, new PlayerInput(false, false, true, false, false), 100);

        // climbed through the ceiling: enters the room above and continues on its ladder (bottom of room 2).
        Assert.Equal(2, s.CurrentRoom);
        Assert.True(s.Player.Y is > 0 and < 176, $"Y={s.Player.Y}");
    }

    /// <summary>
    /// Mechanisms: the map gets wall on the cells they occupy (63DA..6438), but Vick
    /// CROSSES them (they are the push doors).
    /// </summary>
    [Fact]
    public void Mechanism_IsSolidInTheMap_ButThePlayerWalksThrough()
    {
        var mechanisms = "[{\"type\":1,\"room\":1,\"y\":104,\"x\":80,\"raw\":\"0100685001000a000301\"}]";
        var rows = StageTestData.Box();
        rows[16] = "##" + new string('=', 28) + "##";   // floor at 128 (the mechanism occupies 104..127)
        var s = Session(StageTestData.Json(new[] { rows },
            mechanisms: mechanisms, player: "{\"room\":1,\"y\":112,\"x\":64}"));

        var m = s.Definition.Mechanisms[0];
        Assert.True(m.IsSolid, $"width={m.WidthCells} height={m.HeightCells}");
        Assert.Equal(2, m.WidthCells);
        Assert.Equal(3, m.HeightCells);

        // the wall is in the map (the assembly draws on top) and the cells are the mechanism's
        Assert.Equal(TerrainKind.Wall, s.Map.Get(10, 13));
        Assert.True(s.IsMechanismCell(10, 13));
        Assert.True(s.IsMechanismCell(11, 15));
        Assert.False(s.IsMechanismCell(10, 16));

        // Vick pushes the swivel door: it rotates (phase 0 -> 3) and he passes through it.
        Run(s, new PlayerInput(false, true, false, false, false), 40);
        Assert.True(s.Player.X > 80, $"X={s.Player.X}");
    }

    [Fact]
    public void LeavingRightEdge_EntersNextRoomInGrid()
    {
        var open = StageTestData.Box();
        for (int y = 2; y < 22; y++)
            open[y] = "##" + new string('.', 30);
        var s = Session(StageTestData.Json(new[] { open, StageTestData.Box() },
            grid: "[[1,2,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]",
            player: "{\"room\":1,\"y\":160,\"x\":230}"));

        Run(s, new PlayerInput(false, true, false, false, false), 12);

        Assert.Equal(2, s.CurrentRoom);
        Assert.InRange(s.Player.X, -8, 16);
    }

    [Fact]
    public void RoomGrid_WrapsAroundSingleRoom()
    {
        var grid = new int[8, 8];
        grid[0, 0] = 1;
        Assert.Equal(1, RoomGridNavigator.Neighbor(grid, 1, 0, -1));
        grid[0, 2] = 2;
        Assert.Equal(2, RoomGridNavigator.Neighbor(grid, 1, 1, 0));
        Assert.Equal(1, RoomGridNavigator.Neighbor(grid, 2, 1, 0));
    }

    [Fact]
    public void WalkingRight_CyclesOriginalPoses_EveryFourTicks()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }));
        var poses = new List<int>();

        for (int i = 0; i < 16; i++)
        {
            s.Tick(new PlayerInput(false, true, false, false, false));
            poses.Add(s.Player.Pose);
        }

        // 9F49: {3,4,5,4}, first change immediate (timer 1) and then every 4 ticks.
        Assert.Equal(new[] { 4, 4, 4, 4, 5, 5, 5, 5, 4, 4, 4, 4, 3, 3, 3, 3 }, poses);
    }

    [Fact]
    public void Death_SpinsFiveTimes_ThenLies_BeforeLifeIsLost()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }));
        s.Suicide();
        int ticks = 0;
        bool sawFinal = false;
        while (s.Status == StageStatus.Playing && ticks < 500)
        {
            s.Tick(PlayerInput.None);
            sawFinal |= s.Player.Pose == PlayerPoses.DeathFinalPose;
            ticks++;
        }

        Assert.Equal(PlayerPoseSet.Death, s.Player.PoseSet);
        Assert.True(sawFinal);
        // 5 giros x 4 poses x 3 ticks + 0x28 ticks deitado.
        Assert.Equal(5 * 4 * 3 + 0x28, ticks);
    }

    [Fact]
    public void DiggingTool_DoesNothing_WhenNothingToDig()
    {
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() },
            items: "[{\"type\":4,\"room\":1,\"y\":160,\"x\":64}]", player: "{\"room\":1,\"y\":160,\"x\":64}"));

        s.Tick(PlayerInput.None);
        s.Tick(new PlayerInput(false, false, false, false, true));

        Assert.Equal(PlayerState.Standing, s.Player.State);
        Assert.Equal(ItemKind.Pickaxe, s.Player.Holding);
    }

    // ── AFB4: on entering the room, an enemy that kills on touch and is closer than 24 px vanishes ────────
    //
    // 4AFB (when the transition ends, E24D=0) calls AFB4, which scans the 8 slots and removes
    // (AFF6 → 6706, no points and no sound) the dangerous ones (bit 0 of IX+0x13) within ±24 px of the player.
    // Room 1 stays open on the right and 2 is closed; Vick walks 2 px/tick and enters room 2 at x=-7.
    // The sarcophagus of room 2 releases the mummy on tick 80; it stays DANGEROUS and still (Pausing, Flags=3)
    // from tick 143 to 175, exactly at the sarcophagus X — it is the window in which the removal is tested.
    private const int MummyPausedTick = 160;   // inside [143, 175]

    private static string TwoRoomsOpenRight() =>
        "[[1,2,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]," +
        "[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]";

    private static string[] OpenRightRoom()
    {
        var rows = StageTestData.Box();
        for (int y = 2; y < 22; y++)
            rows[y] = "##" + new string('.', 30);
        return rows;
    }

    /// <summary>151 ticks standing + 9 walking = enters room 2 on tick 160, with the mummy stopped.</summary>
    private static StageSession EnterSecondRoom(int coffinX)
    {
        var s = Session(StageTestData.Json(new[] { OpenRightRoom(), StageTestData.Box() },
            grid: TwoRoomsOpenRight(),
            coffins: StageTestData.Coffin(1, coffinX, 160, room: 2),
            player: "{\"room\":1,\"y\":160,\"x\":231}"));

        Run(s, PlayerInput.None, MummyPausedTick - 1);
        Assert.Equal(1, s.CurrentRoom);
        Assert.Contains(s.Enemies, e => e.Alive && e.Dangerous);
        Run(s, new PlayerInput(false, true, false, false, false), 9);
        return s;
    }

    [Fact]
    public void EnteringRoomWithEnemyOnTheLandingSpot_ClearsIt_WithoutScoring()
    {
        var s = EnterSecondRoom(coffinX: 16);   // mummy at x=16; Vick enters at x=-7 (dx=23)

        // keeps walking into the room: without AFB4 the mummy would kill at player.X=6
        Run(s, new PlayerInput(false, true, false, false, false), 15);

        Assert.Equal(2, s.CurrentRoom);
        Assert.Equal(StageStatus.Playing, s.Status);      // did not die entering the room
        Assert.DoesNotContain(s.Enemies, e => e.Alive);
        Assert.Equal(0, s.Score);                        // 4AFB does not score (it is AFF6, not the knife)
    }

    [Fact]
    public void EnteringRoomWithEnemyFarAway_KeepsIt()
    {
        var s = EnterSecondRoom(coffinX: 200);   // mummy at x=200; Vick enters at x=-7 (dx=207)

        Assert.Equal(2, s.CurrentRoom);
        Assert.Contains(s.Enemies, e => e.Alive && e.Dangerous);
    }

    [Fact]
    public void JumpingRight_LandsOnTopOfAFullBlock_LikeOriginal()
    {
        // 16 px block (2 cells) right next to it: the original tests walking in the air only at y+8 and lands
        // as soon as the feet touch the block, rounding Y to the cell (A16D/A535/A446).
        var rows = StageTestData.Box();
        rows[20] = "##" + new string('.', 10) + "####" + new string('.', 14) + "##";
        rows[21] = rows[20];
        var s = Session(StageTestData.Json(new[] { rows }, player: "{\"room\":1,\"y\":160,\"x\":78}"));

        s.Tick(new PlayerInput(false, true, false, false, true));
        Run(s, new PlayerInput(false, true, false, false, false), 20);

        Assert.Equal(PlayerState.Standing, s.Player.State);
        Assert.Equal(160 - 16, s.Player.Y);
        Assert.InRange(s.Player.X, 82, 120);
    }

    /// <summary>
    /// AAA9/AB86: the block goes through 3 stages of 8 ticks and only then vanishes; the terrain gets a new version
    /// per dug cell (and the cached room drawing uses that to reassemble itself).
    /// </summary>
    [Fact]
    public void Shovel_CrumbleGoesThroughThreeStages_ThenRemovesThePair()
    {
        var rows = StageTestData.Box();
        rows[20] = "##" + new string('.', 6) + new string('=', 22) + "##";
        var s = Session(StageTestData.Json(new[] { rows },
            items: "[{\"type\":3,\"room\":1,\"y\":144,\"x\":64}]", player: "{\"room\":1,\"y\":144,\"x\":64}"));

        s.Tick(PlayerInput.None);              // picks up the shovel
        Assert.Equal(ItemKind.Shovel, s.Player.Holding);
        int version = s.MapVersion;

        s.Tick(new PlayerInput(false, false, false, false, true));   // A0A6: comeca a cavar
        Assert.NotNull(s.Crumble);
        Assert.Equal(-1, s.Crumble!.Value.Stage);

        Run(s, PlayerInput.None, 8);
        Assert.Equal(0, s.Crumble!.Value.Stage);
        Run(s, PlayerInput.None, 8);
        Assert.Equal(1, s.Crumble!.Value.Stage);
        Run(s, PlayerInput.None, 8);
        Assert.Equal(2, s.Crumble!.Value.Stage);
        Run(s, PlayerInput.None, 8);

        Assert.Null(s.Crumble);
        Assert.Equal(TerrainKind.Empty, s.Map.Get(9, 20));
        Assert.Equal(TerrainKind.Empty, s.Map.Get(10, 20));
        Assert.True(s.MapVersion > version, "the terrain version must go up when digging");
    }

    [Fact]
    public void Drill_DigsWall_FacingLeft()
    {
        var rows = StageTestData.Box();
        // Column 9 is a Soft wall on rows 18 and 19
        // Player at x = 80 (col 10), y = 144 (row 18), facing left
        rows[18] = "##.......=....................##";
        rows[19] = "##.......=....................##";
        rows[20] = "##" + new string('#', 28) + "##";
        var s = Session(StageTestData.Json(new[] { rows },
            items: "[{\"type\":6,\"room\":1,\"y\":144,\"x\":80}]", player: "{\"room\":1,\"y\":144,\"x\":80}"));

        s.Tick(PlayerInput.None); // picks up the drill (type 6)
        Assert.Equal(ItemKind.Drill, s.Player.Holding);

        s.Player.Facing = Facing.Left;
        s.Tick(new PlayerInput(false, false, false, false, true)); // Aciona a furadeira
        Assert.NotNull(s.Crumble);
        Assert.False(s.Crumble!.Value.Floor); // It is a wall (vertical)
        Assert.False(s.Crumble!.Value.FacingRight); // Facing left

        // Runs the crumbling animation (3 stages x 8 ticks + 8 final ticks to remove)
        Run(s, PlayerInput.None, 32);
        Assert.Null(s.Crumble);
        Assert.Equal(TerrainKind.Empty, s.Map.Get(9, 18));
        Assert.Equal(TerrainKind.Empty, s.Map.Get(9, 19));
    }

    /// <summary>
    /// A717/A79F/A743/A9BA: the knife does not teleport to the ground. Flying, it moves 4 px per FRAME (8 per
    /// tick); at the wall it snaps to the cell and spins 0x18 frames (phase 4, <c>tool_hold</c>), falls with
    /// increasing gravity (phase 5) and ONLY then becomes a ground item — the sound (0x36) is the LANDING's, not the
    /// impact's (A76E knife_sfx).
    /// </summary>
    [Fact]
    public void Knife_HitsWall_GyratesFallsAndOnlyThenBecomesAnItem()
    {
        var rows = StageTestData.Box();
        rows[10] = "##" + new string('=', 12) + new string('.', 16) + "##";  // platform where Vick starts
        var s = Session(StageTestData.Json(new[] { rows },
            items: "[{\"type\":1,\"room\":1,\"y\":64,\"x\":64}]", player: "{\"room\":1,\"y\":64,\"x\":64}"));

        s.Tick(PlayerInput.None);              // picks up the knife
        Assert.Equal(ItemKind.Knife, s.Player.Holding);
        s.Tick(new PlayerInput(false, false, false, false, true));
        Run(s, PlayerInput.None, Physics.ThrowTicks);
        Assert.Single(s.Projectiles);
        Assert.Equal(ProjectilePhase.Flying, s.Projectiles[0].Phase);

        // flies to the right wall: touches it, snaps to the cell (does not go into it) and enters the wait
        int guard = 0;
        while (s.Projectiles[0].Phase == ProjectilePhase.Flying && ++guard < 200)
            s.Tick(PlayerInput.None);

        Assert.Equal(ProjectilePhase.Waiting, s.Projectiles[0].Phase);
        Assert.Equal(Physics.ToolHoldTicks, s.Projectiles[0].Timer);
        Assert.Equal(0, s.Projectiles[0].X % GameConstants.CellSize);   // snaps to the cell (& 0xF8)
        Assert.True(s.Projectiles[0].X <= Physics.ToolAlignMaxX);       // and does not go past 0xF0
        Assert.False(s.Map.IsSolid(s.Projectiles[0].X + 8, s.Projectiles[0].Y + 8), "the knife stopped inside the wall");
        Assert.DoesNotContain(s.Events, e => e.Kind == GameEventKind.ProjectileStopped);

        // spins 0x18 ticks and starts falling — it is still a projectile, still in the air
        Run(s, PlayerInput.None, Physics.ToolHoldTicks);
        Assert.Equal(ProjectilePhase.Falling, s.Projectiles[0].Phase);
        Assert.True(s.Projectiles[0].Y < 160, "the knife cannot have landed yet");
        Assert.DoesNotContain(s.Items, i => i.Kind == ItemKind.Knife && i.X > 200);

        // falls to the floor (row 22) and ONLY then becomes an item, with the landing sound
        RunUntil(s, PlayerInput.None, x => x.Projectiles.Count == 0);
        Assert.Contains(s.Items, i => i.Kind == ItemKind.Knife && i.Y == 160);
        Assert.Contains(s.Events, e => e.Kind == GameEventKind.ProjectileStopped && e.Sound == OriginalSounds.KnifeStop);
    }

    /// <summary>
    /// 4C20 <c>add_score</c>: when the high byte of the BCD score reaches the <c>E245</c> threshold (which
    /// <c>play_init</c> 4C1A sets to 3 = 30,000 points), it raises an extra life with sound 0x2A (the
    /// "card") and the next threshold becomes 6 (60,000).
    /// </summary>
    [Fact]
    public void ExtraLife_ComesAtThirtyThousandPoints_WithTheCardSound()
    {
        // 60 soul stones (500 each) on the player's tile = 30,000 points at once
        string stone = "{\"room\":1,\"y\":160,\"x\":64}";
        var stones = "[" + string.Join(",", Enumerable.Repeat(stone, 60)) + "]";
        var s = Session(StageTestData.Json(new[] { StageTestData.Box() }, stones: stones));

        RunUntil(s, PlayerInput.None, x => x.Events.Any(e => e.Kind == GameEventKind.ExtraLife));

        Assert.Equal(30_000, s.Score);
        Assert.Contains(s.Events, e => e.Kind == GameEventKind.ExtraLife && e.Sound == OriginalSounds.ExtraLifeByScore);

        // the threshold became 6 (60,000): with the score standing still no other life comes
        for (int i = 0; i < 5; i++)
        {
            s.Tick(PlayerInput.None);
            Assert.DoesNotContain(s.Events, e => e.Kind == GameEventKind.ExtraLife);
        }
    }
}
