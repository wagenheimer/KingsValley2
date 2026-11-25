#nullable enable
using System;
using System.IO;
using KingsValley.Engine.Scenes;
using Microsoft.Xna.Framework;
using Xunit;

namespace KingsValley.Core.Tests;

public class Msx1AnimationTests
{
    private static string GetRoomsDir() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/KingsValley.Desktop/Content/RomMsx1/Rooms"));

    private static bool HasMsx1Assets() =>
        Directory.Exists(GetRoomsDir()) && File.Exists(Path.Combine(GetRoomsDir(), "murals.bin"));

    [Fact]
    public void TestMsx1PatternAnimator_BlinksEyesAndUpdatesPixels()
    {
        if (!HasMsx1Assets())
            return;

        var animator = new Msx1PatternAnimator();
        animator.EnsureLoaded(GetRoomsDir());

        var pixels0 = new Color[256 * 192];
        var pixels1 = new Color[256 * 192];

        // Initial apply at frame 0
        bool applied0 = animator.Apply("s03_r01", 1, pixels0);
        Assert.True(applied0, "s03_r01 should be found and applied");

        // Advance 12 ticks (0.4s) -> should change to frame 1 (shut/unlit)
        bool changed = false;
        for (int i = 0; i < 12; i++)
        {
            if (animator.Update(1f / 30f, 1))
                changed = true;
        }
        Assert.True(changed, "After 12 ticks, eye animation should change frame");

        bool applied1 = animator.Apply("s03_r01", 1, pixels1);
        Assert.True(applied1);

        // Check if pixel at eye pupil (x = 7*8+2, y = 3*8+5) changed!
        int py = 3 * 8 + 5;
        int px = 7 * 8 + 2;
        int idx = py * 256 + px;
        Assert.NotEqual(pixels0[idx], pixels1[idx]);
    }

    [Fact]
    public void TestMsx1PatternAnimator_AllSixPyramidsConfigured()
    {
        if (!HasMsx1Assets())
            return;

        var animator = new Msx1PatternAnimator();
        animator.EnsureLoaded(GetRoomsDir());

        // Test representative rooms for pyramids:
        // Pyramid 1 (s03_r01), Pyramid 2 (s19_r01), Pyramid 3 (s21_r01),
        // Pyramid 4 (s34_r02), Pyramid 5 (s43_r01), Pyramid 6 (s54_r01)
        var testRooms = new (string roomKey, int pyr)[]
        {
            ("s03_r01", 1),
            ("s19_r01", 2),
            ("s21_r01", 3),
            ("s34_r02", 4),
            ("s43_r01", 5),
            ("s54_r01", 6),
        };

        var pixels = new Color[256 * 192];
        foreach (var (roomKey, pyr) in testRooms)
        {
            bool ok = animator.Apply(roomKey, pyr, pixels);
            Assert.True(ok, $"Room {roomKey} in pyramid {pyr} must apply animations");
        }
    }

    [Fact]
    public void TestMsx1PatternAnimator_TorchWaveCyclesEightFrames()
    {
        if (!HasMsx1Assets())
            return;

        var animator = new Msx1PatternAnimator();
        animator.EnsureLoaded(GetRoomsDir());

        // s02_r01 has torch cells
        var pixels = new Color[256 * 192];
        bool ok = animator.Apply("s02_r01", 1, pixels);
        Assert.True(ok, "s02_r01 has torch animations");

        // Advance 4 ticks -> torch wave advances 1 frame
        bool changed = false;
        for (int i = 0; i < 4; i++)
        {
            if (animator.Update(1f / 30f, 1))
                changed = true;
        }
        Assert.True(changed, "Torch wave must advance after 4 ticks");
    }

    [Fact]
    public void TestMsx1PatternAnimator_Stage01Room01_EyesAnimated()
    {
        if (!HasMsx1Assets())
            return;

        var animator = new Msx1PatternAnimator();
        animator.EnsureLoaded(GetRoomsDir());

        var pixels0 = new Color[256 * 192];
        var pixels1 = new Color[256 * 192];

        // s01_r01 has eyes at row 19!
        bool applied0 = animator.Apply("s01_r01", 1, pixels0);
        Assert.True(applied0, "s01_r01 must be present in animations.json");

        // Advance 12 ticks
        for (int i = 0; i < 12; i++)
            animator.Update(1f / 30f, 1);

        bool applied1 = animator.Apply("s01_r01", 1, pixels1);
        Assert.True(applied1);

        // Check if eye pupil at ty=19, tx=7 changed
        int py = 19 * 8 + 5;
        int px = 7 * 8 + 2;
        int idx = py * 256 + px;
        Assert.NotEqual(pixels0[idx], pixels1[idx]);
    }

    [Fact]
    public void TestMsx1Murals_BinaryExistsAndValid()
    {
        if (!HasMsx1Assets())
            return;

        string muralsPath = Path.Combine(GetRoomsDir(), "murals.bin");
        Assert.True(File.Exists(muralsPath), "murals.bin must exist in RomMsx1/Rooms");

        var data = File.ReadAllBytes(muralsPath);
        Assert.Equal(6 * 1024, data.Length);
    }
}
