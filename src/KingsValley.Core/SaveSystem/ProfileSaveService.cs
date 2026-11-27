using System.Text.Json;
using System.Text.Json.Serialization;
using KingsValley.Core.Common;

namespace KingsValley.Core.SaveSystem;

/// <summary>
/// Reads/writes the <see cref="SaveGameData"/> to
/// <c>%AppData%/{CompanyFolder}/{GameTitle}/save.json</c>.
///
/// **Pure C#** (only <c>System.Text.Json</c> + <c>File</c>) — Core does not depend on MonoGame/Nez nor
/// on the helper package. The write is "atomic": it writes to a temporary and swaps, so the save
/// is not corrupted if the game closes in the middle.
/// </summary>
public static class ProfileSaveService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static SaveGameData _cached;

    /// <summary>Path of the save file.</summary>
    public static string SaveFilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userData = Environment.GetEnvironmentVariable("KINGSVALLEY_USER_DATA");
            var dir = string.IsNullOrWhiteSpace(userData) ? Path.Combine(root, GameConstants.CompanyFolder, GameConstants.GameTitle) : Path.GetFullPath(userData);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "save.json");
        }
    }

    /// <summary>Loads the save (or a new one, if it does not exist/is corrupted).</summary>
    public static SaveGameData Load()
    {
        if (_cached != null)
            return _cached;

        try
        {
            var path = SaveFilePath;
            _cached = File.Exists(path)
                ? JsonSerializer.Deserialize<SaveGameData>(File.ReadAllText(path), Options) ?? new SaveGameData()
                : new SaveGameData();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Save] Failed to load ({ex.Message}); using defaults.");
            _cached = new SaveGameData();
        }

        return _cached;
    }

    /// <summary>Writes the save (atomic write).</summary>
    public static void Save(SaveGameData data)
    {
        _cached = data;

        try
        {
            var path = SaveFilePath;
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Save] Failed to write ({ex.Message}).");
        }
    }

    /// <summary>Writes the record if <paramref name="score"/> beats it.</summary>
    public static void UpdateHiScore(int score,string campaign=Campaigns.Original)
    {
        var data = Load();
        var progress=data.ProgressFor(campaign);
        if (score <= progress.HiScore)
            return;

        progress.HiScore = score;
        Save(data);
    }

    /// <summary>Marks a room as completed (and updates the highest level reached).</summary>
    public static void CompleteLevel(int number, int score,string campaign=Campaigns.Original)
    {
        var data = Load();

        var progress=data.ProgressFor(campaign);
        if (!progress.CompletedLevels.Contains(number))progress.CompletedLevels.Add(number);
        progress.TotalScore += score;
        progress.HighestLevel = Math.Max(progress.HighestLevel,Math.Min(number+1,Campaigns.StageCount(campaign)));

        Save(data);
    }
}
