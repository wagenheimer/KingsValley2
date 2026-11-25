using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KingsValley.Core.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

/// <summary>
/// Compares what <see cref="RomLevels"/> reads from the cartridge with the level JSON the project has
/// been shipping — the table reader must reproduce them exactly.
/// </summary>
/// <remarks>
/// Needs the ROM and the extracted levels on disk; without them the test is a no-op (the repository is
/// private and the ROM is not versioned). Set <c>KINGSVALLEY_TEST_ROM</c> to point at the image.
/// </remarks>
public class RomLevelsTests
{
    private static string? FindRom()
    {
        string? configured = Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_ROM");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        {
            Path.Combine(Path.GetTempPath(), "opencode", "kv2rom", "kv2_msx1.rom"),
            Path.Combine(Path.GetTempPath(), "kv2rom", "kv2_msx1.rom"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Walks up from the test binary until the shipped level folder is found.</summary>
    private static string? FindLevels()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName,
                "src", "KingsValley.Desktop", "Content", "Rom", "Levels");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public void AllStages_MatchTheShippedJson()
    {
        string? rom = FindRom();
        string? levels = FindLevels();

        if (rom is null || levels is null)
            return; // no ROM/level content in this checkout: nothing to compare

        var reader = new RomLevels(File.ReadAllBytes(rom));
        int compared = 0;

        for (int stage = 1; stage <= RomLevels.StageCount; stage++)
        {
            string path = Path.Combine(levels, $"stage{stage:00}.json");
            if (!File.Exists(path))
                continue;

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            RomLevel level = reader.Read(stage);

            // Terrain: the JSON stores rooms as rows of ".?=#".
            JsonElement rooms = root.GetProperty("rooms");
            Assert.Equal(rooms.GetArrayLength(), level.Rooms.Count);

            for (int r = 0; r < level.Rooms.Count; r++)
            {
                JsonElement rows = rooms[r].GetProperty("rows");
                var expected = new List<string>();
                foreach (JsonElement row in rows.EnumerateArray())
                    expected.Add(row.GetString() ?? string.Empty);

                Assert.Equal(expected, level.Rooms[r].Rows);
            }

            // Ladders and golden ladders: room/column/row/length.
            AssertLadders(root.GetProperty("ladders"), level.Ladders, stage, "ladders");
            AssertLadders(root.GetProperty("goldenLadders"), level.GoldenLadders, stage, "goldenLadders");

            // Items: type/room/y/x.
            JsonElement items = root.GetProperty("items");
            Assert.Equal(items.GetArrayLength(), level.Items.Count);

            for (int i = 0; i < level.Items.Count; i++)
            {
                JsonElement expected = items[i];
                Assert.Equal(expected.GetProperty("type").GetInt32(), level.Items[i].Type);
                Assert.Equal(expected.GetProperty("room").GetInt32(), level.Items[i].Room);
                Assert.Equal(expected.GetProperty("y").GetInt32(), level.Items[i].Y);
                Assert.Equal(expected.GetProperty("x").GetInt32(), level.Items[i].X);
            }

            // Room grid and the musical theme.
            JsonElement grid = root.GetProperty("roomGrid");
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    Assert.Equal(grid[r][c].GetInt32(), level.RoomGrid[r][c]);
                }
            }

            Assert.Equal(root.GetProperty("music").GetInt32(), level.Music);
            Assert.Equal(root.GetProperty("pyramid").GetInt32(), level.Pyramid);

            compared++;
        }

        Assert.Equal(RomLevels.StageCount, compared);
        Console.WriteLine($"[RomLevels] {compared} stages reproduced from the cartridge.");
    }

    private static void AssertLadders(JsonElement expected, List<RomLadder> actual, int stage, string what)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);

        for (int i = 0; i < actual.Count; i++)
        {
            JsonElement e = expected[i];
            Assert.Equal(e.GetProperty("room").GetInt32(), actual[i].Room);
            Assert.Equal(e.GetProperty("col").GetInt32(), actual[i].Column);
            Assert.Equal(e.GetProperty("row").GetInt32(), actual[i].Row);
            Assert.Equal(e.GetProperty("length").GetInt32(), actual[i].Length);
        }
    }
}
