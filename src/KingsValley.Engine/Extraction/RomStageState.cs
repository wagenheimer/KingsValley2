#nullable enable
using System;
using System.Collections.Generic;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Reads the **dynamic** part of a stage (soul stones, exit door, sarcophagi, mechanisms and the player
/// start) by running the cartridge in the package's minimal machine.
/// </summary>
/// <remarks>
/// <para>These values are not tables: the game writes them into RAM while loading the stage. The old
/// pipeline took an openMSX RAM dump after entering the stage by password; here the cartridge boots and
/// its own loader (<c>0x5AAB</c>, which reads the stage number from <c>E242</c>) is called once — no
/// menu, no password, milliseconds instead of half a minute per stage.</para>
///
/// <para>It also decompresses the terrain into <c>E900</c>, which is what the room renderer needs.</para>
/// </remarks>
public sealed class RomStageState
{
    /// <summary>Game routine that loads a stage (stage number in <c>E242</c>, pyramid in <c>E241</c>).</summary>
    private const ushort StageLoader = 0x5AAB;

    private const int StageVariable = 0xE242;
    private const int PyramidVariable = 0xE241;
    private const int RoomVariable = 0xE243;

    /// <summary>Resolves the stage loader address for this cartridge (0x5AAB for MSX1, 0x5CA4 for MSX2).</summary>
    public static ushort ResolveStageLoader(byte[] rom)
    {
        // Search in fixed bank 0 (0x0000..0x2000 in ROM, mapped at 0x4000..0x5FFF)
        // for "LD HL, 0xE2C0; LD DE, 0xE2C1" (21 C0 E2 11 C1 E2)
        int max = Math.Min(rom.Length - 6, 0x2000);
        for (int i = 0; i <= max; i++)
        {
            if (rom[i] == 0x21 && rom[i + 1] == 0xC0 && rom[i + 2] == 0xE2 &&
                rom[i + 3] == 0x11 && rom[i + 4] == 0xC1 && rom[i + 5] == 0xE2)
            {
                return (ushort)(i + 0x4000);
            }
        }
        return 0x5AAB;
    }

    /// <summary>Loads a stage and returns what the game put in RAM.</summary>
    /// <param name="rom">128 KB cartridge image.</param>
    /// <param name="stage">1..60.</param>
    public static RomStage Read(byte[] rom, int stage)
    {
        ArgumentNullException.ThrowIfNull(rom);
        if (stage is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(stage));

        var machine = CartridgeMachines.Create(rom);
        machine.Boot(2_000_000);
        ushort stageLoader = ResolveStageLoader(rom);
        return Read(machine, stageLoader, stage);
    }

    /// <summary>Reads a stage reusing an already booted <see cref="MiniMsx"/> instance for speed.</summary>
    public static RomStage Read(MiniMsx machine, ushort stageLoader, int stage)
    {
        ArgumentNullException.ThrowIfNull(machine);
        if (stage is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(stage));

        for (int a = 0xE2C0; a < 0xE300; a++) machine.Write((ushort)a, 0);
        for (int a = 0xE600; a < 0xE780; a++) machine.Write((ushort)a, 0);

        machine.Write(StageVariable, (byte)stage);
        machine.Write(PyramidVariable, (byte)((stage - 1) / 10 + 1));
        machine.Write(RoomVariable, 1);

        machine.CallAddress(stageLoader, 500_000);

        return Dump(machine, stage);
    }

    internal static RomStage Dump(MiniMsx machine, int stage)
    {
        byte At(int address) => machine.Memory((ushort)address);

        // Soul stones: 8-byte records at E700 (flag, room, y, x).
        var stones = new List<StagePoint>();
        for (int a = 0xE700; a < 0xE780; a += 8)
        {
            if (At(a) != 0)
                stones.Add(new StagePoint(At(a + 1), At(a + 2), At(a + 3)));
        }

        // Mechanisms: 16-byte records at E600 (type, ?, y, x, room).
        var mechanisms = new List<StageMechanism>();
        for (int a = 0xE600; a < 0xE700; a += 16)
        {
            if (At(a) != 0)
            {
                // The 10 record bytes carry the height (byte 8) and the rotating door phase (byte 5).
                string raw = string.Empty;
                for (int i = 0; i < 10; i++)
                    raw += At(a + i).ToString("X2");

                mechanisms.Add(new StageMechanism(At(a), At(a + 2), At(a + 3), At(a + 4), raw));
            }
        }

        // Sarcophagi: 5-byte records at E2C0 ([80|type][10|level][room][y][x]).
        var coffins = new List<StageCoffin>();
        for (int a = 0xE2C0; a < 0xE2F0; a += 5)
        {
            if (At(a) == 0)
                break;

            coffins.Add(new StageCoffin(At(a) & 0x0F, At(a + 1) & 0x0F,
                At(a + 2), At(a + 3), At(a + 4)));
        }

        var door = new StageDoor(At(0xE2F3), At(0xE2F1), At(0xE2F2), At(0xE2F4), At(0xE2F5));
        var player = new StagePoint((byte)(At(RoomVariable) == 0 ? 1 : At(RoomVariable)), At(0xE282), At(0xE284));

        if (player.X == 0 && player.Y == 0)
        {
            throw new InvalidOperationException(
                $"Stage {stage} extraction produced invalid player position (0,0). " +
                "Stage extraction requires the MSX1 RC-760 cartridge.");
        }

        return new RomStage(stage, stones, mechanisms, coffins, door, player);
    }
}

/// <summary>Room + position inside it.</summary>
public readonly record struct StagePoint(int Room, int Y, int X);

/// <summary>A dynamic object of the stage (type, position, room).</summary>
public readonly record struct StageMechanism(int Type, int Y, int X, int Room, string Raw);

/// <summary>An enemy generator (sarcophagus).</summary>
public readonly record struct StageCoffin(int Type, int Level, int Room, int Y, int X);

/// <summary>The exit door of the stage.</summary>
public readonly record struct StageDoor(int Room, int Y, int X, int Width, int Height);

/// <summary>What the game builds in RAM when the stage loads.</summary>
public sealed record RomStage(
    int Stage,
    IReadOnlyList<StagePoint> SoulStones,
    IReadOnlyList<StageMechanism> Mechanisms,
    IReadOnlyList<StageCoffin> Coffins,
    StageDoor ExitDoor,
    StagePoint PlayerStart);
