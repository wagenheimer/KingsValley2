using System.Text.Json;
using KingsValley.Core.Rooms;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

/// <summary>
/// Compares <see cref="RoomAssembler"/> with the assembly done by the Python script
/// (<c>tools/kv2-extract/art/rom_room.py</c>, <c>with_objects=False</c>), which in turn was validated
/// pixel by pixel against the emulator's VRAM (<c>work/room_build.py</c>).
/// </summary>
public class RoomAssemblerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KingsValley.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Repo root (KingsValley.slnx) not found.");
        return dir.FullName;
    }

    private static string RoomsDir() => Path.Combine(RepoRoot(), "src", "KingsValley.Desktop", "Content", "Rom", "Rooms");

    [Theory]
    [InlineData(1, 1, "s1", "odd")]    // stage 1: pyramid 1, odd mural
    [InlineData(2, 1, "s2", "even")]   // stage 2: pyramid 1, even mural
    public void Assemble_MatchesRomReference(int stage, int pyramid, string dump, string parity)
    {
        var roomsDir = RoomsDir();
        var blocksPath = Path.Combine(roomsDir, $"blocks_p{pyramid}.bin");
        if (!File.Exists(blocksPath))
            return; // Skip when running in a clean open-source repo without pre-extracted ROM binaries

        var blocks = File.ReadAllBytes(blocksPath);
        using var murals = JsonDocument.Parse(File.ReadAllText(Path.Combine(roomsDir, "murals.json")));
        using var decorations = JsonDocument.Parse(File.ReadAllText(Path.Combine(roomsDir, "decorations.json")));
        var stageJson = File.ReadAllText(Path.Combine(roomsDir, "..", "Levels", $"stage{stage:D2}.json"));
        var def = StageLoader.Parse(stageJson);

        var muralCodes = murals.RootElement.GetProperty(pyramid.ToString()).GetProperty(parity)
            .EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var decs = decorations.RootElement.GetProperty(stage.ToString()).EnumerateArray()
            .Select(ToDecoration).ToList();

        var room = def.Room(1);
        var assembled = RoomAssembler.Assemble(
            pyramid: pyramid, stage: stage, room: 1,
            blocks: blocks, muralCodes: muralCodes, decorations: decs,
            map: room.Cells, ladders: def.Ladders, goldenLadders: def.GoldenLadders);

        var refPath = Path.Combine(RepoRoot(), "tools", "kv2-extract", "work", "pyr", $"{dump}_room.bin");
        if (!File.Exists(refPath))
            return;
        var refBytes = File.ReadAllBytes(refPath);
        Assert.Equal(refBytes.Length, assembled.Length);

        int diffs = 0, first = -1;
        for (int i = 0; i < assembled.Length; i++)
            if (assembled[i] != refBytes[i]) { if (first < 0) first = i; diffs++; }
        if (diffs > 0)
            throw new Exception(
                $"first diff at (y={first / 256},x={first % 256}): cs={assembled[first]} ref={refBytes[first]}; total {diffs}; " +
                $"map[0,13]={(int)room.Cells[13]} mural=[{string.Join(",", muralCodes.Take(8))}] stage={def.Number}");
    }

    private static DecorationEntry ToDecoration(JsonElement e) =>
        new(
            Kind: e.GetProperty("kind").GetInt32(),
            Shape: e.GetProperty("shape").GetString() ?? "grid",
            Room: e.GetProperty("room").GetInt32(),
            Y: e.GetProperty("y").GetInt32(),
            X: e.GetProperty("x").GetInt32(),
            Rows: e.GetProperty("rows").GetInt32(),
            Cols: e.GetProperty("cols").GetInt32(),
            Height: e.GetProperty("height").GetInt32(),
            Blocks: e.GetProperty("blocks").EnumerateArray().Select(b => b.GetInt32()).ToArray());
}
