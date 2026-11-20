#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KingsValley.Core.Common;

namespace KingsValley.Core.SaveSystem;

/// <summary>
/// Service managing SaveState persistence to %AppData%/GreenSauceGames/KingsValley/savestates/.
/// Atomic JSON write + PNG thumbnail storage.
/// </summary>
public static class SaveStateService
{
    public const int MaxSlots = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SaveStatesDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, GameConstants.CompanyFolder, GameConstants.GameTitle, "savestates");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GetJsonPath(int slot) => Path.Combine(SaveStatesDirectory, $"slot{slot}.json");
    public static string GetPngPath(int slot) => Path.Combine(SaveStatesDirectory, $"slot{slot}.png");

    public static bool SlotExists(int slot) => File.Exists(GetJsonPath(slot));

    public static SaveStateData? Load(int slot)
    {
        try
        {
            var path = GetJsonPath(slot);
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SaveStateData>(json, Options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveStateService] Failed to load slot {slot}: {ex.Message}");
            return null;
        }
    }

    public static void Save(int slot, SaveStateData data, byte[]? pngBytes = null)
    {
        try
        {
            var jsonPath = GetJsonPath(slot);
            var tempJson = jsonPath + ".tmp";
            string json = JsonSerializer.Serialize(data, Options);
            File.WriteAllText(tempJson, json);
            File.Move(tempJson, jsonPath, overwrite: true);

            if (pngBytes != null && pngBytes.Length > 0)
            {
                var pngPath = GetPngPath(slot);
                var tempPng = pngPath + ".tmp";
                File.WriteAllBytes(tempPng, pngBytes);
                File.Move(tempPng, pngPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveStateService] Failed to save slot {slot}: {ex.Message}");
        }
    }

    public static void Delete(int slot)
    {
        try
        {
            var jsonPath = GetJsonPath(slot);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);

            var pngPath = GetPngPath(slot);
            if (File.Exists(pngPath)) File.Delete(pngPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveStateService] Failed to delete slot {slot}: {ex.Message}");
        }
    }
}
