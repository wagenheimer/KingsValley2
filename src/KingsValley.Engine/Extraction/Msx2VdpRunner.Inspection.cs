using System;
using Konamiman.Z80dotNet;
using KingsValley.Core.Screens;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Engine.Extraction;

// Inspection adapters only. The existing CPU, mapper, VDP commands and fast block recipe are unchanged.
public sealed partial class Msx2VdpRunner
{
    public byte Memory(ushort address) => ReadMemory(address);
    public byte[] VdpRegisters => (byte[])_vdp.Regs.Clone();
    public void SetMemory(ushort address,byte value) => WriteMemory(address,value);
    public void Invoke(ushort address,byte? accumulator=null,ushort? hl=null,ushort? de=null,ushort? bc=null,ushort? ix=null)
    {
        var registers=(Z80Registers)_cpu.Registers;
        if(accumulator.HasValue) registers.A=accumulator.Value;
        if(hl.HasValue) registers.HL=unchecked((short)hl.Value);
        if(de.HasValue) registers.DE=unchecked((short)de.Value);
        if(bc.HasValue) registers.BC=unchecked((short)bc.Value);
        if(ix.HasValue) registers.IX=unchecked((short)ix.Value);
        // Inspection recipes can encounter unsupported BIOS paths. Count BIOS traps too,
        // so a corrupt return chain cannot evade the budget of the original fast runner.
        const ushort sentinel=0xFFFE;
        registers.SP=unchecked((short)0xDFEE);registers.PC=address;
        _mem[0xDFEE]=0xFE;_mem[0xDFEF]=0xFF;
        for(int instruction=0;instruction<2_000_000;instruction++)
        {
            if(registers.PC==sentinel)return;
            if(registers.PC<0x4000)InterceptBios(registers.PC);
            else _cpu.ExecuteNextInstruction();
        }
        throw new TimeoutException($"Native inspection call {address:X4} exceeded its instruction budget at PC={registers.PC:X4}, SP={unchecked((ushort)registers.SP):X4}.");
    }
    public ScreenData SnapshotScreen5()
    {
        if((_vdp.Regs[0]&14)!=6) throw new InvalidOperationException("The VDP is not in SCREEN 5.");
        int page=(_vdp.Regs[2]&0x60)<<10;
        byte[] packed=Vram.AsSpan(page,256*212/2).ToArray();
        var palette=new ScreenColor[16];
        for(int i=0;i<16;i++) palette[i]=new((byte)(Palette[i].R*255/7),(byte)(Palette[i].G*255/7),(byte)(Palette[i].B*255/7));
        // SCREEN 5 color zero displays the R7 backdrop while R8.TP is clear.
        // Resolve its display color without rewriting the original packed indices.
        if((_vdp.Regs[8]&0x20)==0)palette[0]=palette[_vdp.Regs[7]&15];
        return new(256,212,false,palette,Packed4Bpp.Unpack(packed,256*212));
    }
}
