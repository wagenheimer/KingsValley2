using System;
using System.IO;
using System.Linq;
using Xunit;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public class SaveStateTests
{
    [Fact]
    public void CaptureState_And_RestoreFrom_PreservesAllGameplayState()
    {
        var box = StageTestData.Box();
        box[21] = "##" + new string('=', 28) + "##";

        var stageJson = StageTestData.Json(
            new[] { box },
            stones: "[{\"room\":1,\"x\":100,\"y\":160},{\"room\":1,\"x\":140,\"y\":160}]",
            items: "[{\"type\":1,\"room\":1,\"x\":80,\"y\":160}]");

        var def = StageTestData.Stage(stageJson);
        var session = new StageSession(def);

        // Modify session state
        session.Player.X = 120;
        session.Player.Y = 150;
        session.Player.Facing = Facing.Left;
        session.Player.State = PlayerState.Jumping;
        session.Player.Holding = ItemKind.Knife;

        // Dig a soft block in room 1
        Assert.Equal(TerrainKind.Soft, session.Room(1).Get(10, 21));
        bool dug = session.Room(1).Dig(10, 21);
        Assert.True(dug);

        // Capture
        var state = session.CaptureState();
        Assert.Equal(120, state.Player.X);
        Assert.Equal(150, state.Player.Y);
        Assert.Equal(Facing.Left, state.Player.Facing);
        Assert.Equal(ItemKind.Knife, state.Player.Holding);
        Assert.Equal(TerrainKind.Empty, (TerrainKind)state.RoomCells[1][21 * GameConstants.RoomColumns + 10]);

        // Create a new fresh session and restore
        var freshSession = new StageSession(def);
        Assert.Equal(64, freshSession.Player.X);
        Assert.Equal(160, freshSession.Player.Y);
        Assert.Equal(TerrainKind.Soft, freshSession.Room(1).Get(10, 21));

        freshSession.RestoreFrom(state);

        // Verify restored
        Assert.Equal(120, freshSession.Player.X);
        Assert.Equal(150, freshSession.Player.Y);
        Assert.Equal(Facing.Left, freshSession.Player.Facing);
        Assert.Equal(PlayerState.Jumping, freshSession.Player.State);
        Assert.Equal(ItemKind.Knife, freshSession.Player.Holding);
        Assert.Equal(TerrainKind.Empty, freshSession.Room(1).Get(10, 21));
    }

    [Fact]
    public void SaveStateService_SaveAndLoad_Roundtrip()
    {
        const int testSlot = 1;
        var originalData = new SaveStateData
        {
            Slot = testSlot,
            Timestamp = DateTime.UtcNow,
            Stage = 3,
            Pyramid = 2,
            Room = 4,
            Score = 15400,
            Lives = 5,
            SoulStonesRemaining = 2,
            TotalSoulStones = 6,
            GfxRom = "msx1",
            Run = new GameRunData
            {
                Stage = 3,
                Lives = 5,
                HiScore = 20000,
                StageScore = 15400,
                Invincible = true,
                ContinueEnabled = false,
            },
            Session = new StageSessionData
            {
                StageNumber = 3,
                CurrentRoom = 4,
                Score = 400,
                TickCount = 120,
                DoorOpen = false,
                Player = new PlayerData
                {
                    X = 96,
                    Y = 128,
                    Facing = Facing.Right,
                    Holding = ItemKind.Boomerang,
                    State = PlayerState.Standing,
                },
            },
        };

        byte[] fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        try
        {
            SaveStateService.Save(testSlot, originalData, fakePng);

            Assert.True(SaveStateService.SlotExists(testSlot));
            Assert.True(File.Exists(SaveStateService.GetPngPath(testSlot)));

            var loaded = SaveStateService.Load(testSlot);
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded.Stage);
            Assert.Equal(2, loaded.Pyramid);
            Assert.Equal(4, loaded.Room);
            Assert.Equal(15400, loaded.Score);
            Assert.Equal(5, loaded.Lives);
            Assert.Equal(2, loaded.SoulStonesRemaining);
            Assert.Equal(6, loaded.TotalSoulStones);
            Assert.Equal("msx1", loaded.GfxRom);
            Assert.True(loaded.Run.Invincible);
            Assert.Equal(ItemKind.Boomerang, loaded.Session.Player.Holding);
            Assert.Equal(96, loaded.Session.Player.X);
            Assert.Equal(128, loaded.Session.Player.Y);
        }
        finally
        {
            SaveStateService.Delete(testSlot);
            Assert.False(SaveStateService.SlotExists(testSlot));
        }
    }
}
