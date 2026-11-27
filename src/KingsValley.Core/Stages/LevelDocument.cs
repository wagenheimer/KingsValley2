using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KingsValley.Core.Stages;

/// <summary>Portable level data. Cartridge artwork and sound never belong in a document.</summary>
public sealed record LevelDocument(string Name, string SourceVariant, string StageJson)
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const string Extension = ".kv2level";
    public StageDefinition Stage => StageLoader.Parse(StageJson);

    public string Serialize()
    {
        Validate(StageJson);
        ValidateVariant(SourceVariant);
        return new JsonObject { ["format"] = "kingsvalley.level", ["version"] = 1,
            ["name"] = Name, ["sourceVariant"] = SourceVariant, ["stageData"] = JsonNode.Parse(StageJson) }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static LevelDocument Parse(string json, string legacyVariant = "msx2")
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The level file exceeds 4 MiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.TryGetProperty("format", out var format))
            {
                if (format.GetString() != "kingsvalley.level" || root.GetProperty("version").GetInt32() != 1)
                    throw new InvalidDataException("This level document format or version is unsupported.");
                var result = new LevelDocument(root.GetProperty("name").GetString() ?? "Untitled",
                    root.GetProperty("sourceVariant").GetString() ?? "", root.GetProperty("stageData").GetRawText());
                ValidateVariant(result.SourceVariant); Validate(result.StageJson); return result;
            }
            if (root.TryGetProperty("editorSourceVariant", out var source)) legacyVariant = source.GetString() ?? legacyVariant;
            ValidateVariant(legacyVariant); Validate(json);
            return new LevelDocument("Imported level", legacyVariant, json);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException or ArgumentException)
        { throw new InvalidDataException("The level file is invalid: " + ex.Message, ex); }
    }

    private static void ValidateVariant(string variant)
    {
        if (variant is not ("msx1" or "msx2" or "msx2-contest")) throw new InvalidDataException("Unknown level source variant.");
    }

    public static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var rooms = root.GetProperty("rooms");
        int count = rooms.GetArrayLength();
        if (count is < 1 or > 64) throw new InvalidDataException("A level must contain 1 to 64 rooms.");
        for (int index = 0; index < count; index++)
        {
            var room = rooms[index]; var rows = room.GetProperty("rows");
            if (room.GetProperty("index").GetInt32() != index + 1 || rows.GetArrayLength() != 24)
                throw new InvalidDataException("Rooms must be consecutive and contain 24 rows.");
            foreach (var row in rows.EnumerateArray())
                if (row.GetString() is not { Length: 32 } line || line.Any(c => !".?=#".Contains(c)))
                    throw new InvalidDataException("Each room row must contain 32 valid terrain cells.");
        }
        var grid = root.GetProperty("roomGrid"); var seen = new HashSet<int>();
        if (grid.GetArrayLength() != 8) throw new InvalidDataException("The room map must be 8 by 8.");
        foreach (var row in grid.EnumerateArray())
        {
            if (row.GetArrayLength() != 8) throw new InvalidDataException("The room map must be 8 by 8.");
            foreach (var value in row.EnumerateArray())
            {
                int id = value.GetInt32();
                if (id < 0 || id > count || id != 0 && !seen.Add(id)) throw new InvalidDataException("The room map contains an invalid or repeated room.");
            }
        }
        if (seen.Count != count) throw new InvalidDataException("Every room must appear in the room map.");
        var stage = StageLoader.Parse(json);
        if (stage.Number is < 1 or > 60 || stage.Pyramid is < 1 or > 6 || stage.MusicIndex is < 0 or > 4)
            throw new InvalidDataException("The stage, pyramid or music selection is invalid.");
        var points = stage.SoulStones.Concat(stage.Items.Select(i => i.Position)).Concat(stage.Coffins.Select(c => c.Position))
            .Concat(stage.Mechanisms.Select(m => m.Position)).Append(stage.PlayerStart).Append(stage.ExitDoor.Position);
        if (points.Any(p => p.Room < 1 || p.Room > count || p.X < 0 || p.X > 255 || p.Y < 0 || p.Y > 191))
            throw new InvalidDataException("An object is outside its room.");
        // Native stage 6 includes a ladder ending below row 23. StageLoader clips
        // its cells to the room; preserve the cartridge record when importing it.
        if (stage.Ladders.Concat(stage.GoldenLadders).Any(l => l.Room < 1 || l.Room > count || l.Column < 0 || l.Column > 30 || l.Row < 0 || l.Row >= 24 || l.Length < 1 || l.Length > 32))
            throw new InvalidDataException("A ladder is outside its room.");
        if (stage.GoldenLadders.Any(l=>l.Row+l.Length>24))
            throw new InvalidDataException("A temporary ladder must fit completely inside its room.");
    }

    public static LevelDocument Load(string path, string legacyVariant = "msx2")
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("The level file exceeds 4 MiB.");
        return Parse(File.ReadAllText(path), legacyVariant);
    }

    public void Save(string path, IEnumerable<string> protectedDirectories)
    {
        string target = Path.GetFullPath(path);
        if (Path.GetExtension(target).ToLowerInvariant() is not (".kv2level" or ".json"))
            throw new InvalidOperationException("Save a level as .kv2level or .json; cartridge files cannot be overwritten.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string directory in protectedDirectories)
        {
            string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (target.Equals(full, comparison) || target.StartsWith(full + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Game cartridge content is read-only. Choose another folder.");
        }
        string data = Serialize();
        if (Encoding.UTF8.GetByteCount(data) > MaximumBytes) throw new InvalidDataException("The level file exceeds 4 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, data, new UTF8Encoding(false)); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
