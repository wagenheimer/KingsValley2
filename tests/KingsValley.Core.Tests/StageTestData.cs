using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

/// <summary>
/// Synthetic stages for the tests (the real ones come from the ROM and are not in the repository).
/// Same syntax as the extracted JSON: '.' empty, '=' diggable, '#' wall.
/// </summary>
internal static class StageTestData
{
    /// <summary>Closed room with a floor on row 22, wall on the edges.</summary>
    public static string[] Box()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
            rows[y] = y is 0 or 1 or 22 or 23 ? new string('#', 32) : "##" + new string('.', 28) + "##";
        return rows;
    }

    public static string Json(
        string[][] rooms,
        string grid = "[[1,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]",
        string ladders = "[]",
        string items = "[]",
        string stones = "[{\"room\":1,\"y\":160,\"x\":200}]",
        string coffins = "[]",
        string mechanisms = "[]",
        string player = "{\"room\":1,\"y\":160,\"x\":64}",
        string door = "{\"room\":1,\"y\":144,\"x\":32,\"w\":4,\"h\":4}")
    {
        var roomJson = string.Join(",", rooms.Select((r, i) =>
            $"{{\"index\":{i + 1},\"rows\":[{string.Join(",", r.Select(s => $"\"{s}\""))}]}}"));
        return $"{{\"stage\":1,\"roomGrid\":{grid},\"rooms\":[{roomJson}],\"ladders\":{ladders},\"goldenLadders\":[]," +
               $"\"items\":{items},\"soulStones\":{stones},\"exitDoor\":{door},\"coffins\":{coffins}," +
               $"\"mechanisms\":{mechanisms},\"playerStart\":{player}}}";
    }

    public static StageDefinition Stage(string json) => StageLoader.Parse(json);

    /// <summary>Sarcophagus of one enemy type.</summary>
    public static string Coffin(int type, int x, int y, int room = 1) =>
        $"[{{\"type\":{type},\"level\":1,\"room\":{room},\"y\":{y},\"x\":{x}}}]";

    /// <summary>Ticks until the enemy leaves the sarcophagus and starts walking.</summary>
    // 671D: 0x14 x 4 ticks until it opens; AE35/AE55/AD90: 3 x 0x20 ticks until it starts walking.
    public const int SpawnTicks = 0x14 * 4 + 0x20 * 3 + 1;
}
