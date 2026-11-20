#nullable enable
using System;
using System.IO;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Extraction;

/// <summary>The two title screens the cartridge carries: Japanese and English.</summary>
public enum TitleVariant
{
    /// <summary>Japanese title (エルキーザの封印), the one shown on Japanese machines.</summary>
    Japanese,

    /// <summary>English title (KING'S VALLEY II), the one shown on export machines.</summary>
    English,
}

/// <summary>
/// Fixed screens extracted **from the MSX1 ROM running the cartridge itself** on the package's machine
/// (<see cref="MiniMsx"/>) — the same path that already produces the stage state, with no external emulator and
/// no video dump.
///
/// <para><b>How the screen comes out:</b> the cartridge draws on the VDP (name table, 1bpp patterns and the SCREEN 2
/// color table); the machine keeps the VRAM and the registers, and here that VRAM is decoded into
/// a buffer in <see cref="ScreenAssembler"/>'s native format (16-color palette + 4bpp indices).
/// The port draws the screen with no new code, and the generated file is the same <c>.bin</c> that
/// <c>ScreenBuilder</c> already loads.</para>
///
/// <para><b>Validation:</b> the extracted title is identical, pixel by pixel, to
/// <c>Content/RomMsx1/Art/Screens/title.bin</c> that came from openMSX (100% of the 49,152 indices), and the
/// same holds for the openMSX VRAM dump at the same instant.</para>
///
/// <para><b>Two variants in the cartridge:</b> the ROM carries the Japanese title and the English one, and picks by the
/// low nibble of the BIOS byte at <c>0x002B</c> (which in a real BIOS is the destination byte of a jump).
/// Both are extracted: <c>title.bin</c> (Japanese) and <c>title_en.bin</c> (English).</para>
/// </summary>
public static class RomScreens
{
    /// <summary>Screen width of the MSX1 (SCREEN 2).</summary>
    public const int Width = 256;

    /// <summary>Screen height of the MSX1 (24 rows of 8 px; the 20 px HUD of the MSX2 does not exist here).</summary>
    public const int Height = 192;

    /// <summary>
    /// Emulation frames until the title is ready. It serves as a safe limit for the capture by state
    /// (<see cref="CaptureTitle"/>): the game reveals the logo, builds the title and animates the GAME/EDIT box
    /// until ~9 s; from ~9.5 s to ~16 s the screen is stable.
    /// </summary>
    public const int TitleFrame = 780;

    /// <summary>16 fixed TMS9918 colors (the MSX1 has no programmable palette).</summary>
    private static readonly ScreenColor[] TmsPalette = BuildTmsPalette();

    private const int SpaceRow = 8;
    private const int SpaceBit = 0;

    /// <summary>
    /// Runs the ROM for <paramref name="frame"/> frames and returns the screen the cartridge drew.
    /// </summary>
    /// <param name="rom">Cartridge image.</param>
    /// <param name="frame">Emulation frames before the capture.</param>
    /// <param name="variant">
    /// Which title the cartridge should build. The game picks by the low nibble of the BIOS byte at
    /// <c>0x002B</c>: <c>0x00</c> builds the Japanese one, any other value builds the English one.
    /// </param>
    public static ScreenData Capture(byte[] rom, int frame, TitleVariant variant = TitleVariant.Japanese)
    {
        ArgumentNullException.ThrowIfNull(rom);

        MiniMsx machine = Boot(rom, variant);

        for (int i = 0; i < frame; i++)
            machine.Frame();

        return new ScreenData(Width, Height, false, TmsPalette,
            DecodeScreen2(machine.Vram, machine.VdpRegisters));
    }

    /// <summary>
    /// How many pixels may change between two frames and the screen still count as "stopped". The cartridge
    /// menu blinks a 128 px cursor; really drawing changes thousands.
    /// </summary>
    private const int SettleTolerance = 300;

    /// <summary>"Stopped" frames in a row to consider the screen ready (2 s: more than any pause in the drawing).</summary>
    private const int SettleFrames = 120;

    /// <summary>Frames observed after stabilizing, to keep the "fullest" phase (cursor lit).</summary>
    private const int BestPhaseFrames = 24;

    /// <summary>
    /// Title, captured **by state**: it waits for the screen to be drawn and stopped, without depending on a fixed
    /// frame. It is the path of the automatic extraction (and the same that
    /// <see cref="Capture(byte[],int,TitleVariant)"/> per frame reproduces).
    /// </summary>
    public static ScreenData CaptureTitle(byte[] rom, TitleVariant variant = TitleVariant.Japanese)
    {
        ArgumentNullException.ThrowIfNull(rom);

        MiniMsx machine = Boot(rom, variant);
        if (!WaitForScreen(machine, minLit: 20000, maxLit: int.MaxValue, maxFrames: 1600, out _))
            throw new InvalidDataException("the cartridge never drew the title screen");

        // The title blinks its cursor; keep the frame without it (the game draws its own marker).
        return CaptureWithoutCursor(machine);
    }

    /// <summary>
    /// Runs the cartridge into the first playable stage and returns a VRAM snapshot, so the extraction
    /// can also read the **sprite and tile pattern tables** the game left in the VDP.
    /// </summary>
    public static (byte[] Vram, byte[] Registers) CaptureGameplay(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);

        MiniMsx machine = Boot(rom, TitleVariant.Japanese);

        if (!WaitForScreen(machine, minLit: 20000, maxLit: int.MaxValue, maxFrames: 1600, out _))
            throw new InvalidDataException("the cartridge never drew the title screen");

        TapUntilScreen(machine, minLit: 0, maxLit: 5000, attempts: 6); // title -> start select
        Tap(machine, SpaceRow, SpaceBit);                              // start select -> stage intro

        // The intro (pyramid) runs for a while before the first room; the pattern tables are only
        // complete once the stage is on screen.
        for (int i = 0; i < 1400; i++)
            machine.Frame();

        byte[] vram = new byte[machine.Vram.Length];
        Array.Copy(machine.Vram, vram, vram.Length);
        byte[] registers = new byte[machine.VdpRegisters.Length];
        Array.Copy(machine.VdpRegisters, registers, registers.Length);

        return (vram, registers);
    }

    /// <summary>
    /// Captures the player's sprite poses by rendering **only the sprite layer** around him, one image
    /// per pose (the pose index lives in RAM <c>E285</c>). The player is 4 overlapping 16x16 sprites, so
    /// compositing the pattern tables alone gives fragments; rendering the layer does not.
    /// </summary>
    /// <returns>One entry per pose (first image seen), 32x32 pixels, 0 = transparent.</returns>
    public static IReadOnlyList<(int Pose, byte[] Pixels)> CapturePlayerPoses(byte[] rom, int frames = 120 * 60)
    {
        var (poses, _, _) = CaptureGameplayAssets(rom, frames);
        return poses;
    }

    /// <summary>
    /// Runs the cartridge into stage 1 and captures player poses along with VRAM and VDP registers,
    /// providing both sprite layer composite poses and the native pattern/color tables.
    /// </summary>
    public static (IReadOnlyList<(int Pose, byte[] Pixels)> Poses, byte[] Vram, byte[] Registers)
        CaptureGameplayAssets(byte[] rom, int frames = 120 * 60)
    {
        ArgumentNullException.ThrowIfNull(rom);

        const int box = 32;
        MiniMsx machine = Boot(rom, TitleVariant.Japanese);

        if (!WaitForScreen(machine, minLit: 20000, maxLit: int.MaxValue, maxFrames: 1600, out _))
            throw new InvalidDataException("the cartridge never drew the title screen");

        TapUntilScreen(machine, minLit: 0, maxLit: 5000, attempts: 6);
        Tap(machine, SpaceRow, SpaceBit);
        for (int i = 0; i < 1000; i++)
            machine.Frame();

        var poses = new Dictionary<int, byte[]>();

        for (int f = 0; f < frames; f++)
        {
            machine.Frame();

            int pose = machine.Memory(0xE285);
            if (poses.ContainsKey(pose))
                continue;

            byte[] layer = RenderSpriteLayer(machine, machine.Memory(0xE284) - 8, machine.Memory(0xE282) - 16, box);
            if (layer.All(p => p == 0))
                continue;

            poses[pose] = layer;
        }

        byte[] vram = new byte[machine.Vram.Length];
        Array.Copy(machine.Vram, vram, vram.Length);
        byte[] registers = new byte[machine.VdpRegisters.Length];
        Array.Copy(machine.VdpRegisters, registers, registers.Length);

        return (poses.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList(), vram, registers);
    }

    /// <summary>
    /// Draws every visible sprite into a transparent box (0 = empty, colour + 1 otherwise), the way the
    /// chip does it: entry 31 first, entry 0 on top; 16x16 sprites use four patterns (p,p+1 / p+2,p+3).
    /// </summary>
    private static byte[] RenderSpriteLayer(MiniMsx machine, int originX, int originY, int box)
    {
        var layer = new byte[box * box];
        byte[] registers = machine.VdpRegisters;
        int spriteBase = (registers[6] & 0x07) << 11;
        int attributes = (registers[5] & 0x7F) << 7;
        bool big = (registers[1] & 0x02) != 0;

        for (int entry = 31; entry >= 0; entry--)
        {
            int y = machine.Vram[(attributes + entry * 4) & 0x3FFF];
            int x = machine.Vram[(attributes + entry * 4 + 1) & 0x3FFF];
            int pattern = machine.Vram[(attributes + entry * 4 + 2) & 0x3FFF];
            int colour = machine.Vram[(attributes + entry * 4 + 3) & 0x3FFF] & 0x0F;
            if (y == 208 || colour == 0)
                continue;

            y += 1;
            int basePattern = big ? (pattern & 0xFC) : pattern;
            for (int quadrant = 0; quadrant < (big ? 4 : 1); quadrant++)
            {
                int tile = basePattern + quadrant;
                for (int row = 0; row < 8; row++)
                {
                    byte bits = machine.Vram[(spriteBase + tile * 8 + row) & 0x3FFF];
                    for (int bit = 0; bit < 8; bit++)
                    {
                        if (((bits >> (7 - bit)) & 1) == 0)
                            continue;

                        int sx = x + (big ? (quadrant / 2) * 8 : 0) + bit - originX;
                        int sy = y + (big ? (quadrant % 2) * 8 : 0) + row - originY;
                        if (sx < 0 || sx >= box || sy < 0 || sy >= box)
                            continue;

                        layer[sy * box + sx] = (byte)(colour + 1);
                    }
                }
            }
        }

        return layer;
    }

    /// <summary>
    /// Pyramid cutscene (stage 1 intro), captured **by state**: the game enters the
    /// attract mode by itself; two presses take title → START SELECT (text menu) → pyramid.
    /// </summary>
    public static ScreenData CaptureStageIntro(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);

        MiniMsx machine = Boot(rom, TitleVariant.Japanese);

        if (!WaitForScreen(machine, minLit: 20000, maxLit: int.MaxValue, maxFrames: 1600, out _))
            throw new InvalidDataException("the cartridge never drew the title screen");

        // Title → START SELECT: the screen becomes text only (few lit pixels). The game ignores
        // keys while the menu box is still coming in, so it tries again if it did not open.
        if (!TapUntilScreen(machine, minLit: 0, maxLit: 5000, attempts: 6))
            throw new InvalidDataException("the cartridge never opened the start select screen");

        // START SELECT → pyramid (the stage 1 intro).
        Tap(machine, SpaceRow, SpaceBit);
        if (!WaitForScreen(machine, minLit: 15000, maxLit: int.MaxValue, maxFrames: 1200, out _))
            throw new InvalidDataException("the cartridge never drew the pyramid intro");

        return CaptureBestPhase(machine);
    }

    /// <summary>
    /// Decodes the VRAM of an MSX1 in SCREEN 2 into the port's index buffer.
    /// </summary>
    /// <remarks>
    /// The table addresses come from the VDP registers, with the MSX masks (not the "raw"
    /// TMS9918 ones): names = <c>(R2 &amp; 0x0F) × 0x400</c>, patterns = <c>(R4 &amp; 0x3C) × 0x800</c>
    /// and colors = <c>(R3 &amp; 0x80) × 0x40</c>. Each character has its own 8 color bytes in SCREEN 2
    /// (<c>code * 8 + row</c>), and each vertical third has a 2 KB bank (<c>(ty / 8) * 0x800</c>).
    /// </remarks>
    public static byte[] DecodeScreen2(byte[] vram, byte[] registers)
    {
        ArgumentNullException.ThrowIfNull(vram);
        ArgumentNullException.ThrowIfNull(registers);

        int nameBase = (registers[2] & 0x0F) * 0x400;
        int patternBase = (registers[4] & 0x3C) << 11;
        int colourBase = (registers[3] & 0x80) << 6;

        int pMask = registers[4] & 0x03;
        int cMask = (registers[3] & 0x7F) < 4 ? (registers[3] & 0x03) : 3;

        var indices = new byte[Width * Height];

        for (int ty = 0; ty < 24; ty++)
        {
            int third = ty / 8;
            int pBank = (third & pMask) * 0x800;
            int cBank = (third & cMask) * 0x800;

            for (int tx = 0; tx < 32; tx++)
            {
                int code = vram[(nameBase + ty * 32 + tx) & 0x3FFF];

                for (int row = 0; row < 8; row++)
                {
                    byte pattern = vram[(patternBase + pBank + code * 8 + row) & 0x3FFF];
                    byte colours = vram[(colourBase + cBank + code * 8 + row) & 0x3FFF];
                    int foreground = (colours >> 4) & 0x0F;
                    int background = colours & 0x0F;

                    int offset = (ty * 8 + row) * Width + tx * 8;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool on = ((pattern >> (7 - bit)) & 1) != 0;
                        indices[offset + bit] = (byte)(on ? foreground : background);
                    }
                }
            }
        }

        return indices;
    }

    /// <summary>
    /// Extracts the screens to <paramref name="directory"/> (<c>Screens/*.bin</c>), in the native format.
    /// </summary>
    /// <param name="rom">Cartridge image.</param>
    /// <param name="directory">Output folder (the extracted content one).</param>
    /// <param name="log">Receives one line per screen.</param>
    public static void WriteAll(byte[] rom, string directory, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentNullException.ThrowIfNull(directory);

        string screens = Path.Combine(directory, "Screens");
        Directory.CreateDirectory(screens);

        // Each screen is captured by state (the machine waits for the screen to be drawn and stopped), so
        // there is no clock time involved: the same ROM always generates the same files.
        (string Name, Func<ScreenData> Capture)[] captures =
        {
            ("title", () => CaptureTitle(rom)),
            ("title_en", () => CaptureTitle(rom, TitleVariant.English)),
            ("pyramid", () => CaptureStageIntro(rom)),
        };

        foreach ((string name, Func<ScreenData> capture) in captures)
        {
            log?.Invoke($"screen {name}");

            try
            {
                ScreenData screen = capture();
                File.WriteAllBytes(Path.Combine(screens, name + ".bin"),
                    ScreenAssembler.Encode(screen.Width, screen.Height, screen.Palette, screen.Indices));
            }
            catch (InvalidDataException ex)
            {
                // A screen the cartridge did not draw (different ROM?) cannot bring down the whole
                // extraction: the audio and the 60 stages are already done, and the game reports what is missing.
                log?.Invoke($"screen {name}: skipped ({ex.Message})");
                Console.WriteLine($"[RomScreens] {name}: {ex.Message}");
            }
        }
    }

    /// <summary>Initializes the machine with the cartridge and the requested title variant.</summary>
    private static MiniMsx Boot(byte[] rom, TitleVariant variant)
    {
        var machine = new MiniMsx(rom, RomMapper.KonamiScc);
        machine.SetBiosByte(0x002B, variant == TitleVariant.Japanese ? (byte)0x00 : (byte)0xC9);

        if (!machine.Boot(2_000_000))
            throw new InvalidDataException("the ROM did not boot");

        return machine;
    }

    private static ScreenData ScreenOf(MiniMsx machine) =>
        new(Width, Height, false, TmsPalette, DecodeScreen2(machine.Vram, machine.VdpRegisters));

    /// <summary>Pixels with an index other than 0 (the "signature" of each screen, with no reference needed).</summary>
    private static int LitPixels(byte[] screen)
    {
        int lit = 0;
        foreach (byte index in screen)
        {
            if (index != 0)
                lit++;
        }

        return lit;
    }

    /// <summary>How many pixels changed between two frames.</summary>
    private static int ChangedPixels(byte[] a, byte[] b)
    {
        int changed = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                changed++;
        }

        return changed;
    }

    /// <summary>
    /// Advances until the screen is **drawn** (pixels lit in the requested range) and **stopped** — stopped in
    /// the practical sense: fewer than <see cref="SettleTolerance"/> pixels changing per frame, for
    /// <see cref="SettleFrames"/> frames in a row. That is what makes the extraction independent of time.
    /// </summary>
    private static bool WaitForScreen(MiniMsx machine, int minLit, int maxLit, int maxFrames, out int frame)
    {
        byte[] previous = DecodeScreen2(machine.Vram, machine.VdpRegisters);
        int settled = 0;

        for (frame = 0; frame < maxFrames; frame++)
        {
            machine.Frame();
            byte[] current = DecodeScreen2(machine.Vram, machine.VdpRegisters);

            settled = ChangedPixels(current, previous) <= SettleTolerance ? settled + 1 : 0;
            previous = current;

            if (settled < SettleFrames)
                continue;

            int lit = LitPixels(current);
            if (lit >= minLit && lit <= maxLit)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps the "fullest" phase of the screen (cursor lit) — used for the pyramid, which has no cursor.
    /// </summary>
    private static ScreenData CaptureBestPhase(MiniMsx machine) => CapturePhase(machine, fewest: false);

    /// <summary>
    /// Keeps the **emptiest** phase of the screen, i.e. with the blinking cursor off. The title art must
    /// be the clean one: the game draws its own cursor, so a baked one would double it.
    /// </summary>
    private static ScreenData CaptureWithoutCursor(MiniMsx machine) => CapturePhase(machine, fewest: true);

    private static ScreenData CapturePhase(MiniMsx machine, bool fewest)
    {
        byte[] best = DecodeScreen2(machine.Vram, machine.VdpRegisters);
        int bestLit = LitPixels(best);

        for (int i = 0; i < BestPhaseFrames; i++)
        {
            machine.Frame();
            byte[] current = DecodeScreen2(machine.Vram, machine.VdpRegisters);
            int lit = LitPixels(current);

            if (fewest ? lit < bestLit : lit > bestLit)
            {
                best = current;
                bestLit = lit;
            }
        }

        return new ScreenData(Width, Height, false, TmsPalette, best);
    }

    /// <summary>A key press (the MSX matrix is read by the game's interrupt handler).</summary>
    private static void Tap(MiniMsx machine, int row, int bit)
    {
        machine.PressKey(row, bit, true);
        for (int i = 0; i < 6; i++)
            machine.Frame();

        machine.PressKey(row, bit, false);
        for (int i = 0; i < 4; i++)
            machine.Frame();
    }

    /// <summary>
    /// Presses until the screen reaches the target. The game ignores keys while the menu is still coming in, so
    /// a single attempt is not enough — but each one waits a short game time, with no clock.
    /// </summary>
    private static bool TapUntilScreen(MiniMsx machine, int minLit, int maxLit, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            // Waits for the menu to finish coming in before considering the attempt lost.
            for (int f = 0; f < 60; f++)
                machine.Frame();

            Tap(machine, SpaceRow, SpaceBit);

            if (WaitForScreen(machine, minLit, maxLit, maxFrames: 400, out _))
                return true;
        }

        return false;
    }

    /// <summary>Builds the 16 TMS9918 colors in the same table the Python extractor used.</summary>
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
}
