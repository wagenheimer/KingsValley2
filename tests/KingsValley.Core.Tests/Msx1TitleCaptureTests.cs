using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class Msx1TitleCaptureTests
{
    [LocalRomFact("msx1-frs")]
    public void FrsTitlesMatchCompletedNativeDrawingOutsideBlinkingCursor()
    {
        byte[] rom = File.ReadAllBytes(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_FRS_ROM")!);
        VerifyTitles(rom);
    }

    [RevisionCorpusFact]
    public void AllAcceptedMsx1RevisionsMatchTheirOwnCompletedNativeTitles()
    {
        string root = Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_REVISIONS_DIR")!;
        foreach (var entry in SupportedCartridges.All.Where(entry => entry.Variant == "msx1"))
            VerifyTitles(File.ReadAllBytes(Path.Combine(root, entry.Sha1 + ".rom")));
    }

    private static void VerifyTitles(byte[] rom)
    {
        byte[]? nativeFloor = null;
        foreach (TitleVariant variant in new[] { TitleVariant.Japanese, TitleVariant.English })
        {
            var machine = CartridgeMachines.Create(rom);
            machine.SetBiosByte(0x002B, variant == TitleVariant.Japanese ? (byte)0 : (byte)0xC9);
            Assert.True(machine.Boot(2_000_000));
            int frame;
            for (frame = 0; frame < 2400; frame++)
            {
                machine.Frame();
                if (machine.Memory(0xE200) == 1) break;
            }
            Assert.True(frame < 2400);
            byte[] native = RomScreens.DecodeScreen2(machine.Vram, machine.VdpRegisters);
            // Both locales use the same native floor (5758 / ABE7). Capture its
            // Japanese boot independently: FRS export boot can leave map glyphs here.
            if (variant == TitleVariant.Japanese) nativeFloor = native[(168 * 256)..];
            else nativeFloor!.CopyTo(native, 168 * 256);
            var extracted = RomScreens.CaptureTitle(rom, variant);
            Assert.Equal(256, extracted.Width); Assert.Equal(192, extracted.Height);
            int differences = 0;
            for (int i = 0; i < native.Length; i++)
            {
                int x = i % 256, y = i / 256;
                if (x is >= 56 and < 72 && (y is >= 136 and < 144 || y is >= 152 and < 160)) continue;
                if (native[i] != extracted.Indices[i]) differences++;
            }
            Assert.Equal(0, differences);
        }
    }
}
