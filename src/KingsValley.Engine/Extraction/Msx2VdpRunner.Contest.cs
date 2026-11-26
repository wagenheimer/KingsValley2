using Konamiman.Z80dotNet;
namespace KingsValley.Engine.Extraction;

// Separate verified Contest boot adapter; the original fast MSX2 recipe remains unchanged.
public sealed partial class Msx2VdpRunner
{
    public void BootContest()
    {
        var registers=(Z80Registers)_cpu.Registers;
        registers.SP=unchecked((short)0xF380);registers.PC=0x40A3;
        bool frs=_rom[0x112]==0xC3 && _rom[0x113]==0x61 && _rom[0x114]==0xF5;
        if(frs)
        {
            // FRS installs its H.KEYI handler in authentic RAM code. Stop before its
            // interrupt-dependent HALT, then invoke the unchanged video initializer directly.
            RunUntil(0x40FA);Invoke(0x4F7F);
        }
        else RunUntil(0x4114);
        Invoke(0x5888); // Native cache of the six pyramid tile sets, normally prepared by the Contest menu.
    }
}
