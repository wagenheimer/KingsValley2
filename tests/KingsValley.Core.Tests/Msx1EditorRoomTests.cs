using KingsValley.Core.Stages;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Scenes;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1EditorRoomTests
{
    [LocalRomFact("msx1")]
    public void EditedAndNewRoomsMatchNativeEditorTerrainWithoutAnyMsx2Assets()
    {
        byte[] rom = File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KingsValley", "roms", "kv2_msx1.rom"));
        string root = Path.Combine(Path.GetTempPath(), "kv2-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            RomSpriteAtlas.Build(rom, root);
            File.WriteAllBytes(Path.Combine(root, "Rooms", "murals.bin"), RomRoomDumps.GenerateMuralsBin(rom, CancellationToken.None));
            RomStageJson.WriteAll(rom, Path.Combine(root, "Levels"), maxStages: 1);
            Msx1EditorBackgrounds.Write(rom, root, 1);
            Assert.False(File.Exists(Path.Combine(root, "Rooms", "blocks_p1.bin")));
            var builder = new Msx1EditorRoomAssembler(root);
            var machine = RomScreens.BootToGameplay(rom);
            var map = Enumerable.Range(0, 32 * 24).Select(i => (TerrainKind)((i % 32 / 2 + i / 32) % 4)).ToArray();
            for (int pyramid = 1; pyramid <= 6; pyramid++)
            {
                machine.Write(0xE241, (byte)pyramid); machine.Write(0xE242, (byte)((pyramid - 1) * 10 + 1)); machine.Write(0xE243, 1);
                RomStageState.LoadMsx1Theme(machine, rom);
                machine.CallAddress(RomStageState.ResolveStageLoader(rom), 2_000_000);
                machine.CallAddress(0x41D4); machine.CallAddress(0x44FA, 1_000_000);
                for (int i = 0; i < 192; i++)
                    machine.Write((ushort)(0xE900 + i), (byte)((int)map[i * 4] << 6 | (int)map[i * 4 + 1] << 4 | (int)map[i * 4 + 2] << 2 | (int)map[i * 4 + 3]));
                machine.Write(0xE200, 11);
                machine.CallAddress(0x41B8); machine.CallAddress(0x453D, 1_000_000); machine.CallAddress(0x4192);
                byte[] native = RomScreens.DecodeScreen2(machine.Vram, machine.VdpRegisters);
                var actual = builder.Assemble(pyramid, 1, map);
                Assert.Equal(256, actual.Width); Assert.Equal(192, actual.Height);
                for (int i = 0; i < native.Length; i++)
                {
                    var expected = Tms9918.Palette[native[i]]; var color = actual.Palette[actual.Indices[i]];
                    Assert.True(expected.R == color.R && expected.G == color.G && expected.B == color.B,
                        $"Pyramid {pyramid}, pixel {i % 256},{i / 256}: native index {native[i]}, assembled {actual.Indices[i]}.");
                }
                machine.Write(0xE242, 1); machine.Write(0xE243, 1); machine.Write(0xE254, 0);
                machine.CallAddress(0x41D4); machine.CallAddress(0x44FA, 1_000_000);
                machine.Write(0xE242, (byte)((pyramid-1)*10+1));
                machine.CallAddress(0x4607, 1_000_000);
                Assert.Equal(RomScreens.DecodeScreen2(machine.Vram, machine.VdpRegisters), builder.Background(pyramid, 1, 1, null));
                machine.CallAddress(0x4192);
                // Newly added editor rooms have no native room dump to borrow.
                Assert.Equal(actual.Indices, builder.Assemble(pyramid, 63, map).Indices);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
