#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KingsValley.Core.Screens;
using KingsValley.Core.Stages;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Dumps every gameplay room of every stage from the MSX1 cartridge into the native screen format used by
/// <see cref="ScreenAssembler"/>. No PNG is involved: each <c>sNN_rMM.bin</c> is a 38-byte header plus the
/// 4bpp indices of the 256x192 SCREEN 2 image the game itself drew in the VDP.
/// </summary>
/// <remarks>
/// <para>
/// The machine boots once, walks through the title/start-select screens into gameplay, and stays there. For each
/// room the cartridge's own stage loader (<c>0x5AAB</c>) is called with the pyramid/stage/room variables at
/// <c>E241</c>/<c>E242</c>/<c>E243</c>; after a fixed number of frames the VRAM is decoded with
/// <see cref="RomScreens.DecodeScreen2"/>. Everything is frame-driven, so the output is deterministic.
/// </para>
/// <para>
/// A blank capture (the game drifted into a cutscene while idling) triggers one reboot of the machine and one
/// retry for that room. Normal rooms never need it, but it keeps a long extraction robust.
/// </para>
/// </remarks>
public static class RomRoomDumps
{
    /// <summary>Cartroutine that loads a stage/room into RAM and draws it.</summary>
    private const ushort LoadRoomAddress = 0x5AAB;

    private const ushort PyramidVariable = 0xE241;
    private const ushort StageVariable = 0xE242;
    private const ushort RoomVariable = 0xE243;

    /// <summary>Frames after calling the loader, before the VDP snapshot (4 s at 60 Hz).</summary>
    private const int SettleFrames = 240;

    /// <summary>A real room paints far more than this many coloured pixels; less means a bad capture.</summary>
    private const int MinimumLitPixels = 1000;

    private static readonly ScreenColor[] TmsPalette = BuildTmsPalette();

    /// <summary>
    /// Writes <c>Rooms/dumps/sNN_rMM.bin</c> for every distinct room of stages 1..<paramref name="stages"/>.
    /// </summary>
    /// <param name="rom">MSX1 (RC-760) cartridge image.</param>
    /// <param name="levelsDirectory">Folder with the already extracted <c>stageNN.json</c> files.</param>
    /// <param name="extractedDirectory">Root of the extracted content (<c>Rooms/dumps</c> is created there).</param>
    /// <param name="log">Receives progress lines such as <c>room 3/12 of stage 1</c>.</param>
    /// <param name="cancellationToken">Checked between stages and rooms.</param>
    /// <param name="stages">How many stages to dump (tests can use a small prefix).</param>
    public static void WriteAll(byte[] rom, string levelsDirectory, string extractedDirectory,
        Action<string>? log = null, CancellationToken cancellationToken = default, int stages = 60)
    {
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentException.ThrowIfNullOrWhiteSpace(levelsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(stages, 1);

        string dumps = Path.Combine(extractedDirectory, "Rooms", "dumps");
        Directory.CreateDirectory(dumps);

        MiniMsx machine = BootToGameplay(rom, cancellationToken);

        // Extract clean 64x32 mural tables (murals.bin) directly from the MSX1 ROM segment 14
        try
        {
            log?.Invoke("murals: extracting clean mural tables");
            byte[] murals = GenerateMuralsBin(machine, rom, cancellationToken);
            File.WriteAllBytes(Path.Combine(extractedDirectory, "Rooms", "murals.bin"), murals);
            log?.Invoke("murals: 6 pyramids extracted (6144 bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomRoomDumps] murals extraction: {ex.Message}");
        }

        for (int stage = 1; stage <= stages; stage++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StageDefinition definition = StageLoader.Load(levelsDirectory, stage);
            int[] rooms = definition.Rooms.Select(r => r.Index)
                .Where(index => index > 0)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();

            for (int i = 0; i < rooms.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int room = rooms[i];
                log?.Invoke($"room {i + 1}/{rooms.Length} of stage {stage}");

                byte[] indices = CaptureRoom(ref machine, rom, definition.Pyramid, stage, room, cancellationToken);
                int lit = indices.Count(value => value >= 2);
                if (lit < MinimumLitPixels)
                    log?.Invoke($"room s{stage:00}_r{room:00}: blank capture ({lit} coloured pixels)");

                File.WriteAllBytes(Path.Combine(dumps, $"s{stage:00}_r{room:00}.bin"),
                    ScreenAssembler.Encode(RomScreens.Width, RomScreens.Height, TmsPalette, indices));
            }
        }
    }

    /// <summary>Loads one room and decodes its VRAM; reboots once if the cartridge drifted into a blank screen.</summary>
    private static byte[] CaptureRoom(ref MiniMsx machine, byte[] rom, int pyramid, int stage, int room,
        CancellationToken token)
    {
        byte[] indices = LoadAndDecode(machine, pyramid, stage, room, token);
        if (indices.Count(value => value >= 2) >= MinimumLitPixels)
            return indices;

        machine = BootToGameplay(rom, token);
        return LoadAndDecode(machine, pyramid, stage, room, token);
    }

    private static byte[] LoadAndDecode(MiniMsx machine, int pyramid, int stage, int room, CancellationToken token)
    {
        machine.Write(PyramidVariable, (byte)pyramid);
        machine.Write(StageVariable, (byte)stage);
        machine.Write(RoomVariable, (byte)room);
        machine.CallAddress(LoadRoomAddress, 2_000_000);

        RunFrames(machine, SettleFrames, token);

        byte[] vram = new byte[machine.Vram.Length];
        Array.Copy(machine.Vram, vram, vram.Length);
        byte[] registers = new byte[machine.VdpRegisters.Length];
        Array.Copy(machine.VdpRegisters, registers, registers.Length);

        return RomScreens.DecodeScreen2(vram, registers);
    }

    /// <summary>
    /// Fixed frame sequence booting the cartridge and reaching gameplay. The same sequence as the verified
    /// gameplay probe: wait for the title, tap Space until the start menu opens, then enter stage 1.
    /// </summary>
    private static MiniMsx BootToGameplay(byte[] rom, CancellationToken token)
    {
        var machine = new MiniMsx(rom, RomMapper.KonamiScc);

        if (!machine.Boot(2_000_000))
            throw new InvalidDataException("the ROM did not boot");

        RunFrames(machine, 1600, token);

        for (int attempt = 0; attempt < 6; attempt++)
        {
            RunFrames(machine, 60, token);
            Tap(machine, token);
            RunFrames(machine, 400, token);
        }

        RunFrames(machine, 1400, token);
        return machine;
    }

    /// <summary>A key press on row 8, bit 0 (Space), long enough for the game's ISR to read it.</summary>
    private static void Tap(MiniMsx machine, CancellationToken token)
    {
        machine.PressKey(8, 0, true);
        RunFrames(machine, 6, token);
        machine.PressKey(8, 0, false);
        RunFrames(machine, 4, token);
    }

    private static void RunFrames(MiniMsx machine, int frames, CancellationToken token)
    {
        for (int i = 0; i < frames; i++)
        {
            token.ThrowIfCancellationRequested();
            machine.Frame();
        }
    }

    /// <summary>The 16 fixed TMS9918 colours, identical to the palette used by <see cref="RomScreens"/>.</summary>
    private static ScreenColor[] BuildTmsPalette()
    {
        (byte R, byte G, byte B)[] tms =
        {
            (0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
            (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
            (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
            (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255),
        };

        var palette = new ScreenColor[tms.Length];
        for (int i = 0; i < tms.Length; i++)
            palette[i] = new ScreenColor(tms[i].R, tms[i].G, tms[i].B);

        return palette;
    }

    private static readonly int[] PyrOffsets = { 0x1C104, 0x1C144, 0x1C184, 0x1C1A4, 0x1C204, 0x1C244 };

    /// <summary>
    /// Generates the 6144-byte murals.bin containing the clean 64x32 4bpp bitmap for all 6 pyramids
    /// decoded directly from MSX1 ROM segment 14 and VRAM pattern tables.
    /// </summary>
    public static byte[] GenerateMuralsBin(MiniMsx machine, byte[] rom, CancellationToken token)
    {
        byte[] outData = new byte[6 * 1024];

        for (int pyr = 1; pyr <= 6; pyr++)
        {
            token.ThrowIfCancellationRequested();
            int stage = (pyr - 1) * 10 + 1;

            // Load room 1 of this pyramid's first stage so VRAM contains its pattern/color tables
            machine.Write(PyramidVariable, (byte)pyr);
            machine.Write(StageVariable, (byte)stage);
            machine.Write(RoomVariable, 1);
            machine.CallAddress(LoadRoomAddress, 2_000_000);
            RunFrames(machine, 60, token);

            byte[] vram = machine.Vram;
            byte[] regs = machine.VdpRegisters;

            int patBase = regs.Length > 4 ? (regs[4] & 0x3C) << 11 : 0x2000;
            int colBase = regs.Length > 3 ? (regs[3] & 0x80) << 6 : 0x0000;

            int off = PyrOffsets[pyr - 1];
            if (off + 32 > rom.Length) continue;

            byte[] pixels = new byte[64 * 32];
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    int code = rom[off + row * 8 + col];
                    for (int r = 0; r < 8; r++)
                    {
                        byte pat = vram[(patBase + code * 8 + r) & 0x3FFF];
                        byte colB = vram[(colBase + code * 8 + r) & 0x3FFF];
                        byte fg = (byte)((colB >> 4) & 0x0F);
                        byte bg = (byte)(colB & 0x0F);
                        for (int c = 0; c < 8; c++)
                        {
                            bool bit = ((pat >> (7 - c)) & 1) != 0;
                            pixels[(row * 8 + r) * 64 + (col * 8 + c)] = bit ? fg : bg;
                        }
                    }
                }
            }

            int destOffset = (pyr - 1) * 1024;
            for (int i = 0; i < 1024; i++)
            {
                byte hi = (byte)(pixels[i * 2] & 0x0F);
                byte lo = (byte)(pixels[i * 2 + 1] & 0x0F);
                outData[destOffset + i] = (byte)((hi << 4) | lo);
            }
        }

        return outData;
    }
}
