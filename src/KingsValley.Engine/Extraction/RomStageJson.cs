#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KingsValley.Core.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Writes the level JSON the game loads (<c>stageNN.json</c>) from the cartridge image alone: the
/// static tables (<see cref="RomLevels"/>) plus the state the game builds in RAM
/// (<see cref="RomStageState"/>).
/// </summary>
/// <remarks>
/// Same schema the old pipeline produced with <c>tools/kv2-extract/levels/extract.py</c>, so
/// <c>StageLoader.Parse</c> reads it unchanged. The <c>mechanisms[].raw</c> field keeps the 10 bytes of
/// each record: the loader takes the mechanism height and the rotating door phase from them.
/// </remarks>
public static class RomStageJson
{
    /// <summary>Builds the JSON of one stage.</summary>
    public static string Build(byte[] rom, int stage)
    {
        RomLevels levels = new(rom);
        RomStage state = RomStageState.Read(rom, stage);
        return Build(levels, state, stage);
    }

    /// <summary>Builds the JSON of one stage with pre-read tables and state for speed.</summary>
    public static string Build(RomLevels levels, RomStage state, int stage)
    {
        return Build(levels.Read(stage),state,stage);
    }

    public static string Build(RomLevel tables,RomStage state,int stage)
    {
        var rooms = new JsonArray();
        foreach (RomRoom room in tables.Rooms)
        {
            var rows = new JsonArray();
            foreach (string row in room.Rows)
                rows.Add(row);

            rooms.Add(new JsonObject
            {
                ["index"] = room.Index,
                ["rows"] = rows,
                ["background"] = $"s{stage:00}_r{room.Index}.png",
            });
        }

        var grid = new JsonArray();
        foreach (int[] row in tables.RoomGrid)
        {
            var cells = new JsonArray();
            foreach (int cell in row)
                cells.Add(cell);

            grid.Add(cells);
        }

        var ladders = new JsonArray();
        foreach (RomLadder ladder in tables.Ladders)
            ladders.Add(Ladder(ladder));

        var golden = new JsonArray();
        foreach (RomLadder ladder in tables.GoldenLadders)
            golden.Add(Ladder(ladder));

        var items = new JsonArray();
        foreach (RomItem item in tables.Items)
        {
            items.Add(new JsonObject
            {
                ["type"] = item.Type,
                ["room"] = item.Room,
                ["y"] = item.Y,
                ["x"] = item.X,
            });
        }

        var stones = new JsonArray();
        foreach (StagePoint stone in state.SoulStones)
            stones.Add(Point(stone));

        var coffins = new JsonArray();
        foreach (StageCoffin coffin in state.Coffins)
        {
            // `level` is not in the cartridge: the game raises it while the room runs (the old dumps
            // caught 1..3). It is left out so the loader keeps its default instead of inventing one.
            coffins.Add(new JsonObject
            {
                ["type"] = coffin.Type,
                ["room"] = coffin.Room,
                ["y"] = coffin.Y,
                ["x"] = coffin.X,
            });
        }

        var mechanisms = new JsonArray();
        foreach (StageMechanism mechanism in state.Mechanisms)
        {
            mechanisms.Add(new JsonObject
            {
                ["type"] = mechanism.Type,
                ["y"] = mechanism.Y,
                ["x"] = mechanism.X,
                ["room"] = mechanism.Room,
                ["raw"] = mechanism.Raw,
            });
        }

        var document = new JsonObject
        {
            ["stage"] = stage,
            ["pyramid"] = tables.Pyramid,
            ["roomGrid"] = grid,
            ["rooms"] = rooms,
            ["ladders"] = ladders,
            ["goldenLadders"] = golden,
            ["items"] = items,
            ["soulStones"] = stones,
            ["exitDoor"] = new JsonObject
            {
                ["room"] = state.ExitDoor.Room,
                ["y"] = state.ExitDoor.Y,
                ["x"] = state.ExitDoor.X,
                ["w"] = state.ExitDoor.Width,
                ["h"] = state.ExitDoor.Height,
            },
            ["coffins"] = coffins,
            ["mechanisms"] = mechanisms,
            ["playerStart"] = Point(state.PlayerStart),
            ["music"] = tables.Music,
        };

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes the stages (up to <paramref name="maxStages"/>) into <paramref name="directory"/>.</summary>
    /// <param name="rom">Cartridge image.</param>
    /// <param name="directory">Destination folder (created).</param>
    /// <param name="progress">Receives (stage, total).</param>
    /// <param name="maxStages">Number of stages to extract (default 60).</param>
    public static void WriteAll(byte[] rom, string directory, Action<int, int>? progress = null, int maxStages = 60)
    {
        int total = Math.Clamp(maxStages, 1, RomLevels.StageCount);
        var levels = new RomLevels(rom);
        var machine = new Wagenheimer.MsxAudio.Extraction.MiniMsx(rom, Wagenheimer.MsxAudio.Extraction.RomMapper.KonamiScc);
        machine.Boot(2_000_000);
        ushort stageLoader = RomStageState.ResolveStageLoader(rom);

        string tempDir = directory + ".tmp";
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
        Directory.CreateDirectory(tempDir);

        for (int stage = 1; stage <= total; stage++)
        {
            RomStage state = RomStageState.Read(machine, stageLoader, stage);
            string json = Build(levels, state, stage);
            File.WriteAllText(Path.Combine(tempDir, $"stage{stage:00}.json"), json);
            progress?.Invoke(stage, total);
        }

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.Move(tempDir, directory);
    }

    private static JsonObject Ladder(RomLadder ladder) => new()
    {
        ["room"] = ladder.Room,
        ["col"] = ladder.Column,
        ["row"] = ladder.Row,
        ["length"] = ladder.Length,
    };

    private static JsonObject Point(StagePoint point) => new()
    {
        ["room"] = point.Room,
        ["y"] = point.Y,
        ["x"] = point.X,
    };
}
