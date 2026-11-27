#nullable enable
using KingsValley.Core.Stages;

namespace KingsValley.Engine.Extraction;

/// <summary>Preserves native SCREEN 2 tables for every editor theme and cartridge room placement.</summary>
public static class Msx1EditorBackgrounds
{
    public static void Write(byte[] rom, string destination, int stages, CancellationToken token = default)
    {
        var machine = RomScreens.BootToGameplay(rom, token);
        string root = Path.Combine(destination, "Rooms", "Backgrounds");
        Directory.CreateDirectory(root);
        for (int theme = 1; theme <= 6; theme++)
        {
            token.ThrowIfCancellationRequested();
            machine.Write(0xE241, (byte)theme);
            RomStageState.LoadMsx1Theme(machine, rom);
            string folder = Path.Combine(root, $"p{theme}");
            Directory.CreateDirectory(folder);
            for (int parity = 1; parity <= 2; parity++)
            {
                machine.Write(0xE242, (byte)parity);
                machine.CallAddress(0x41D4);
                machine.CallAddress(0x44FA, 1_000_000);
                SaveNames(Path.Combine(folder, parity == 1 ? "odd.names" : "even.names"));
                machine.CallAddress(0x4192);
            }
            for (int stage = 1; stage <= stages; stage++)
            {
                var definition = StageLoader.Load(Path.Combine(destination, "Levels"), stage);
                foreach (var room in definition.Rooms)
                {
                    token.ThrowIfCancellationRequested();
                    int nativeStage = (stage - 1) / 10 + 1 == theme ? stage : (theme - 1) * 10 + 1;
                    machine.Write(0xE242, (byte)nativeStage);
                    machine.Write(0xE243, (byte)room.Index);
                    machine.Write(0xE254, 0);
                    machine.CallAddress(0x41D4);
                    machine.Write(0xE242, (byte)stage);
                    machine.CallAddress(0x44FA, 1_000_000);
                    machine.Write(0xE242, (byte)nativeStage);
                    machine.CallAddress(0x4607, 1_000_000);
                    SaveNames(Path.Combine(folder, $"s{stage:00}_r{room.Index:00}.names"));
                    machine.CallAddress(0x4192);
                }
            }
            byte[] snapshot = new byte[8 + 16384];
            Array.Copy(machine.VdpRegisters, snapshot, 8);
            Array.Copy(machine.Vram, 0, snapshot, 8, 16384);
            File.WriteAllBytes(Path.Combine(root, $"p{theme}.vdp"), snapshot);
        }
        void SaveNames(string path)
        {
            int address = (machine.VdpRegisters[2] & 15) << 10;
            File.WriteAllBytes(path, machine.Vram.AsSpan(address, 768).ToArray());
        }
    }
}
