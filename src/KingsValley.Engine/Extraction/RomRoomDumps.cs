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
/// <c>E241</c>/<c>E242</c>/<c>E243</c>. The selected room is redrawn synchronously by its native routine;
/// the clean VRAM tables supply both the decoded background and its animated-cell references.
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

    /// <summary>Frames after calling the loader, before the VDP snapshot (12 frames is plenty for VRAM transfer).</summary>
    private const int SettleFrames = 12;

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
        var animations=Msx1PatternAnimations.Create(rom);

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

                byte[] indices = CaptureRoom(ref machine, rom, definition, stage, room, extractedDirectory, cancellationToken);
                int lit = indices.Count(value => value >= 2);
                if (lit < MinimumLitPixels)
                    throw new InvalidDataException($"Room s{stage:00}_r{room:00} remained blank after retry ({lit} visible pixels).");

                animations.CaptureRoom(machine.Vram,machine.VdpRegisters,stage,room);

                File.WriteAllBytes(Path.Combine(dumps, $"s{stage:00}_r{room:00}.bin"),
                    ScreenAssembler.Encode(RomScreens.Width, RomScreens.Height, TmsPalette, indices, paletteModel: ScreenPaletteModel.Tms9918));
            }
        }

        animations.Save(extractedDirectory);
        log?.Invoke("MSX1 pattern animations: native eye and torch frames with clean-room cell references extracted.");
        // Extract clean 64x32 mural tables (murals.bin) directly from the MSX1 ROM segment 14 AFTER room dumps
        try
        {
            log?.Invoke("murals: extracting clean mural tables");
            byte[] murals = GenerateMuralsBin(rom, cancellationToken);
            File.WriteAllBytes(Path.Combine(extractedDirectory, "Rooms", "murals.bin"), murals);
            log?.Invoke("murals: 6 pyramids extracted (6144 bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomRoomDumps] murals extraction: {ex.Message}");
            throw;
        }
    }

    /// <summary>Loads one room and decodes its VRAM; reboots once if the cartridge drifted into a blank screen.</summary>
    private static byte[] CaptureRoom(ref MiniMsx machine, byte[] rom, StageDefinition definition, int stage, int room, string destination,
        CancellationToken token)
    {
        byte[] indices = LoadAndDecode(machine, rom, definition, stage, room, destination, token);
        if (indices.Count(value => value >= 2) >= MinimumLitPixels)
            return indices;

        machine = BootToGameplay(rom, token);
        return LoadAndDecode(machine, rom, definition, stage, room, destination, token);
    }

    private static byte[] LoadAndDecode(MiniMsx machine, byte[] rom, StageDefinition definition, int stage, int room, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        machine.Write(PyramidVariable, (byte)definition.Pyramid);
        machine.Write(StageVariable, (byte)stage);
        machine.Write(RoomVariable, (byte)room);
        machine.CallAddress(RomStageState.ResolveStageLoader(rom), 2_000_000);
        // State 14 is the native entrance animation. Use its neutral living state for a static room capture.
        if(machine.Memory(0xE280)==14) machine.Write(0xE280,0);
        // 5AAB initializes a stage and its starting room. 5AEC redraws the explicitly selected room.
        machine.Write(RoomVariable, (byte)room);
        machine.CallAddress(0x5AEC, 2_000_000);
        if(machine.Memory(StageVariable)!=stage || machine.Memory(RoomVariable)!=room || machine.Memory(0xE280)!=0)
            throw new InvalidDataException($"Native room state mismatch: stage={machine.Memory(StageVariable)}, room={machine.Memory(RoomVariable)}, player={machine.Memory(0xE280)}; expected stage={stage}, room={room}.");
        byte[] original=RomScreens.DecodeScreen2(machine.Vram,machine.VdpRegisters);
        string objects=Path.Combine(destination,"Art","Native"); Directory.CreateDirectory(objects);
        void Crop(string name,int x,int y,int width,int height)
        {
            string path=Path.Combine(objects,name+".bin"); if(File.Exists(path)) return;
            if(x<0 || y<0 || x+width>256 || y+height>192) throw new InvalidDataException("Native object bounds are outside its room: "+name);
            byte[] pixels=new byte[width*height];
            for(int row=0;row<height;row++) Array.Copy(original,(y+row)*256+x,pixels,row*width,width);
            if(pixels.All(p=>p<2)) throw new InvalidDataException("Native object capture is blank: "+name);
            File.WriteAllBytes(path,ScreenAssembler.Encode(width,height,TmsPalette,pixels,paletteModel:ScreenPaletteModel.Tms9918));
            File.WriteAllText(path+".source.json",System.Text.Json.JsonSerializer.Serialize(new {stage,room,x,y,width,height}));
        }
        foreach(var item in definition.Items.Where(i=>i.Position.Room==room)) Crop("item_"+item.Kind.ToString().ToLowerInvariant(),item.Position.X,item.Position.Y,16,16);
        foreach(var stone in definition.SoulStones.Where(s=>s.Room==room)) Crop($"soulstone_p{definition.Pyramid}",stone.X,stone.Y,16,16);
        // Let the cartridge redraw clean mural/terrain itself. Clearing only the state byte keeps
        // the native tables intact and avoids painting guessed mural patches over room decorations.
        for(ushort address=0xE300;address<0xE500;address+=8) machine.Write(address,0);
        for(ushort address=0xE700;address<0xE780;address+=8) machine.Write(address,0);
        machine.CallAddress(0x5AEC,2_000_000);
        token.ThrowIfCancellationRequested();
        return RomScreens.DecodeScreen2(machine.Vram,machine.VdpRegisters);
    }

    /// <summary>
    /// Boots the cartridge and reaches active gameplay using the verified state machine from <see cref="RomScreens.BootToGameplay"/>.
    /// </summary>
    private static MiniMsx BootToGameplay(byte[] rom, CancellationToken token)
    {
        return RomScreens.BootToGameplay(rom, token);
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
    /// Uses a dedicated machine instance so the main gameplay capture is never tainted.
    /// </summary>
    public static byte[] GenerateMuralsBin(byte[] rom, CancellationToken token)
    {
        var machine = BootToGameplay(rom, token);
        byte[] outData = new byte[6 * 1024];

        for (int pyr = 1; pyr <= 6; pyr++)
        {
            token.ThrowIfCancellationRequested();
            int stage = (pyr - 1) * 10 + 1;

            // Load room 1 of this pyramid's first stage so VRAM contains its pattern/color tables
            machine.Write(PyramidVariable, (byte)pyr);
            machine.Write(StageVariable, (byte)stage);
            machine.Write(RoomVariable, 1);
            ushort loader = RomStageState.ResolveStageLoader(rom);
            machine.CallAddress(loader, 2_000_000);
            machine.Write(RoomVariable, 1);
            machine.CallAddress(0x5AEC, 2_000_000);

            byte[] vram = machine.Vram;
            byte[] regs = machine.VdpRegisters;

            int patBase = regs.Length > 4 ? (regs[4] & 0x3C) << 11 : 0x2000;
            int colBase = regs.Length > 3 ? (regs[3] & 0x80) << 6 : 0x0000;
            int pMask = regs.Length > 4 ? (regs[4] & 0x03) : 3;
            int cMask = regs.Length > 3 ? ((regs[3] & 0x7F) < 4 ? (regs[3] & 0x03) : 3) : 3;

            // In MSX1 SCREEN 2, gameplay pattern and color tables reside in the middle third (third 1)
            int third = 1;
            int pBank = (third & pMask) * 0x800;
            int cBank = (third & cMask) * 0x800;

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
                        byte pat = vram[(patBase + pBank + code * 8 + r) & 0x3FFF];
                        byte colB = vram[(colBase + cBank + code * 8 + r) & 0x3FFF];
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
