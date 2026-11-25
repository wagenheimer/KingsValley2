#nullable enable
using System;
using Konamiman.Z80dotNet;

namespace KingsValley.Engine.Extraction;

/// <summary>
/// Minimal Z80 + Konami SCC mapper + V9938 (MSX2 VDP) emulator used to execute the authentic
/// Konami MSX2 cartridge routines (0x5C80 prepara_fase and 0x5CF5 pinta_pantalla) directly in-process.
/// This extracts genuine 4bpp tileblocks (blocks_p1..6.bin), murals, decorations and room backgrounds
/// without any external emulator or Python dependency.
/// </summary>
public sealed partial class Msx2VdpRunner
{
    private readonly byte[] _rom;
    private readonly byte[] _mem = new byte[0x10000];
    private readonly Z80Processor _cpu = new();
    private readonly V9938 _vdp = new();

    public byte[] Vram => _vdp.Vram;
    public (int R, int G, int B)[] Palette => _vdp.Pal;

    public Msx2VdpRunner(byte[] rom)
    {
        _rom = rom ?? throw new ArgumentNullException(nameof(rom));
        _cpu.Memory = new MachineMemory(this);
        _cpu.PortsSpace = new MachinePorts(this);

        // Map initial 8KB banks to windows
        SetBank(0x4000, 0);
        SetBank(0x6000, 1);
        SetBank(0x8000, 2);
        SetBank(0xA000, 3);

        // BIOS VDP port variables and non-Japanese flag
        _mem[0x0006] = 0x98;
        _mem[0x0007] = 0x98;
        _mem[0x002B] = 0x11;
        _mem[0xFFA7] = 0xC9; // H.PHYD: no disk
    }

    private void SetBank(int window, int bank)
    {
        int numBanks = _rom.Length / 0x2000;
        if (numBanks == 0) return;
        int b = (bank % numBanks) * 0x2000;
        Array.Copy(_rom, b, _mem, window, 0x2000);
    }

    private byte ReadMemory(ushort addr) => _mem[addr];

    private void WriteMemory(ushort addr, byte val)
    {
        if (addr >= 0x4000 && addr < 0xC000)
        {
            // Konami SCC mapper: writes to (addr & 0x1800) == 0x1000 switch bank for window (addr & 0xE000)
            if ((addr & 0x1800) == 0x1000)
            {
                SetBank(addr & 0xE000, val);
            }
            return;
        }

        _mem[addr] = val;
    }

    private byte ReadPort(byte port)
    {
        return port switch
        {
            0x98 => _vdp.ReadData(),
            0x99 => _vdp.ReadStatus(),
            _ => 0xFF
        };
    }

    private void WritePort(byte port, byte val)
    {
        switch (port)
        {
            case 0x98: _vdp.WriteData(val); break;
            case 0x99: _vdp.WriteControl(val, this); break;
            case 0x9A: _vdp.WritePalette(val); break;
            case 0x9B: _vdp.WriteIndirect(val, this); break;
        }
    }

    public void SetVdpRegister(int reg, byte val)
    {
        _vdp.SetRegister(reg, val);
        if (reg < 8) _mem[0xF3DF + reg] = val;
        else if (reg < 24) _mem[0xFFE7 + reg - 8] = val;
    }

    private void InterceptBios(ushort pc)
    {
        var regs = (Z80Registers)_cpu.Registers;
        ushort hl = unchecked((ushort)regs.HL);
        ushort de = unchecked((ushort)regs.DE);
        ushort bc = unchecked((ushort)regs.BC);

        switch (pc)
        {
            case 0x0047: // WRTVDP
                SetVdpRegister(regs.C & 0x3F, regs.B);
                break;
            case 0x0171:
            case 0x0053: // NSTWRT / SETWRT
                _vdp.VramDir = hl;
                break;
            case 0x0174:
            case 0x004A: // NRDVRM / RDVRM
                regs.A = _vdp.Vram[hl];
                break;
            case 0x0177:
            case 0x004D: // NWRVRM / WRTVRM
                _vdp.Vram[hl] = regs.A;
                break;
            case 0x005C: // LDIRVM
                for (int k = 0; k < bc; k++)
                    _vdp.Vram[(de + k) & 0xFFFF] = _mem[(hl + k) & 0xFFFF];
                break;
            case 0x0059: // LDIRMV
                for (int k = 0; k < bc; k++)
                    _mem[(de + k) & 0xFFFF] = _vdp.Vram[(hl + k) & 0xFFFF];
                break;
            case 0x016B:
            case 0x0056: // BIGFIL / FILVRM
                for (int k = 0; k < bc; k++)
                    _vdp.Vram[(hl + k) & 0xFFFF] = regs.A;
                break;
            case 0x005F: // CHGMOD
                if (regs.A == 5)
                {
                    SetVdpRegister(0, 0x06);
                    SetVdpRegister(1, 0x60);
                    SetVdpRegister(2, 0x1F);
                    SetVdpRegister(5, 0xEF);
                    SetVdpRegister(6, 0x0F);
                    SetVdpRegister(8, 0x08);
                    SetVdpRegister(9, 0x00);
                    SetVdpRegister(11, 0x00);
                }
                break;
            case 0x0138: // RSLREG
            case 0x013E: // RDVDP
                regs.A = 0;
                break;
            case 0x0141: // SNSMAT
            case 0x000C: // RDSLT
                regs.A = 0xFF;
                break;
            case 0x0024:
            case 0x0062:
            case 0x0090:
            case 0x0041:
            case 0x0044:
                break;
        }

        // Pop return address from stack to resume caller
        ushort sp = unchecked((ushort)regs.SP);
        ushort retAddr = (ushort)(_mem[sp] | (_mem[(sp + 1) & 0xFFFF] << 8));
        regs.SP = unchecked((short)(sp + 2));
        regs.PC = retAddr;
    }

    public void RunUntil(ushort stopAddress, int maxInstructions = 10_000_000)
    {
        var regs = (Z80Registers)_cpu.Registers;
        int count = 0;

        while (regs.PC != stopAddress)
        {
            if (regs.PC < 0x4000)
            {
                InterceptBios(regs.PC);
                continue;
            }

            _cpu.ExecuteNextInstruction();
            count++;
            if (count > maxInstructions)
                throw new TimeoutException($"Z80 execution exceeded {maxInstructions} instructions without reaching 0x{stopAddress:X4}");
        }
    }

    public void Boot()
    {
        var regs = (Z80Registers)_cpu.Registers;
        regs.SP = unchecked((short)0xF380);
        regs.PC = 0x40B5;
        RunUntil(0x4124);
    }

    public void Call(ushort address, int maxInstructions = 10_000_000)
    {
        var regs = (Z80Registers)_cpu.Registers;
        ushort sp = 0xDFF0;

        // Push sentinel return address 0xFFFE
        const ushort sentinel = 0xFFFE;
        sp = (ushort)((sp - 2) & 0xFFFF);
        _mem[sp] = (byte)(sentinel & 0xFF);
        _mem[(sp + 1) & 0xFFFF] = (byte)(sentinel >> 8);
        regs.SP = unchecked((short)sp);

        regs.PC = address;
        RunUntil(sentinel, maxInstructions);
    }

    /// <summary>
    /// Executes prepara_fase (0x5C80) and pinta_pantalla (0x5CF5) for the given stage and room.
    /// Populates VRAM page 0 with the assembled room and VRAM page 1 with authentic 4bpp tileblocks.
    /// </summary>
    public void PrepareAndPaint(int stage, int room = 1)
    {
        _mem[0xE254] = 0;
        _mem[0xE242] = (byte)stage;
        Call(0x5C80); // prepara_fase

        _mem[0xE243] = (byte)room;
        Call(0x5CF5); // pinta_pantalla
    }

    /// <summary>
    /// Extracts the 256 authentic 8x8 blocks (4bpp packed, 32 bytes each = 8192 bytes total)
    /// from VRAM page 1 (0x8000..0x10000) for the given pyramid.
    /// </summary>
    public byte[] ExtractBlocks(int pyramid)
    {
        int stage = (pyramid - 1) * 10 + 1;
        PrepareAndPaint(stage, 1);

        // Unpack VRAM page 1 (0x8000..0x10000 = 256 lines of 128 bytes = 256x256 pixels)
        var p1 = new byte[256, 256];
        for (int y = 0; y < 256; y++)
        {
            int rowOff = 0x8000 + y * 128;
            for (int x = 0; x < 128; x++)
            {
                byte b = _vdp.Vram[rowOff + x];
                p1[y, x * 2] = (byte)(b >> 4);
                p1[y, x * 2 + 1] = (byte)(b & 0x0F);
            }
        }

        // Pack 256 tiles of 8x8 into 8192 bytes
        var blocks = new byte[8192];
        for (int n = 0; n < 256; n++)
        {
            int by = (n >> 5) * 8;
            int bx = (n & 31) * 8;
            int blockOff = n * 32;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c += 2)
                {
                    byte p0 = p1[by + r, bx + c];
                    byte p1Val = p1[by + r, bx + c + 1];
                    blocks[blockOff + r * 4 + c / 2] = (byte)((p0 << 4) | (p1Val & 0x0F));
                }
            }
        }

        return blocks;
    }

    private sealed class MachineMemory : IMemory
    {
        private readonly Msx2VdpRunner _runner;
        public MachineMemory(Msx2VdpRunner runner) => _runner = runner;
        public int Size => 0x10000;
        public byte this[int address]
        {
            get => _runner.ReadMemory((ushort)address);
            set => _runner.WriteMemory((ushort)address, value);
        }
        public void SetContents(int startAddress, byte[] contents, int startIndex = 0, int? length = null)
        {
            int count = length ?? contents.Length - startIndex;
            for (int i = 0; i < count; i++) this[startAddress + i] = contents[startIndex + i];
        }
        public byte[] GetContents(int startAddress, int length)
        {
            var res = new byte[length];
            for (int i = 0; i < length; i++) res[i] = this[startAddress + i];
            return res;
        }
    }

    private sealed class MachinePorts : IMemory
    {
        private readonly Msx2VdpRunner _runner;
        public MachinePorts(Msx2VdpRunner runner) => _runner = runner;
        public int Size => 0x100;
        public byte this[int port]
        {
            get => _runner.ReadPort((byte)port);
            set => _runner.WritePort((byte)port, value);
        }
        public void SetContents(int startAddress, byte[] contents, int startIndex = 0, int? length = null) { }
        public byte[] GetContents(int startAddress, int length) => new byte[length];
    }

    /// <summary>Minimal V9938 (MSX2 VDP) implementation supporting the command engine.</summary>
    private sealed class V9938
    {
        public readonly byte[] Vram = new byte[0x20000];
        public readonly byte[] Regs = new byte[64];
        public readonly (int R, int G, int B)[] Pal = new (int, int, int)[16];

        public int VramDir;
        private byte? _controlLatch;
        private byte? _paletteLatch;
        private (int dx, int dy, int nx, int ny, int i, int ix, int iy)? _hmmc;

        public byte ReadData()
        {
            byte v = Vram[VramDir & 0x1FFFF];
            VramDir = (VramDir + 1) & 0x1FFFF;
            return v;
        }

        public byte ReadStatus()
        {
            _controlLatch = null;
            int s = Regs[15] & 15;
            if (s == 2)
            {
                return (byte)(0x80 | (_hmmc != null ? 1 : 0));
            }
            return 0;
        }

        public void WriteData(byte v)
        {
            Vram[VramDir & 0x1FFFF] = v;
            VramDir = (VramDir + 1) & 0x1FFFF;
        }

        public void WriteControl(byte v, Msx2VdpRunner runner)
        {
            if (_controlLatch == null)
            {
                _controlLatch = v;
                return;
            }

            byte first = _controlLatch.Value;
            _controlLatch = null;

            if ((v & 0x80) != 0)
            {
                runner.SetVdpRegister(v & 0x3F, first);
            }
            else
            {
                VramDir = ((Regs[14] & 7) << 14) | ((v & 0x3F) << 8) | first;
            }
        }

        public void WritePalette(byte v)
        {
            if (_paletteLatch == null)
            {
                _paletteLatch = v;
            }
            else
            {
                byte rb = _paletteLatch.Value;
                _paletteLatch = null;
                int palIdx = Regs[16] & 15;
                Pal[palIdx] = ((rb >> 4) & 7, v & 7, rb & 7);
                Regs[16] = (byte)((palIdx + 1) & 15);
            }
        }

        public void WriteIndirect(byte v, Msx2VdpRunner runner)
        {
            int n = Regs[17] & 0x3F;
            if ((Regs[17] & 0x80) == 0)
            {
                Regs[17] = (byte)((Regs[17] & 0xC0) | ((n + 1) & 0x3F));
            }
            if (n != 17)
            {
                runner.SetVdpRegister(n, v);
            }
        }

        public void SetRegister(int n, byte v)
        {
            Regs[n] = v;
            if (n == 44 && _hmmc != null)
            {
                HmmcByte(v);
            }
            else if (n == 46)
            {
                ExecuteCommand(v);
            }
        }

        private int RegWord(int low, int high) => Regs[low] | (Regs[high] << 8);

        private byte GetVramByte(int x, int y) =>
            Vram[((((y & 1023) * 128) + ((x & 255) >> 1))) & 0x1FFFF];

        private void PutVramByte(int x, int y, byte val) =>
            Vram[((((y & 1023) * 128) + ((x & 255) >> 1))) & 0x1FFFF] = val;

        private byte GetPixel(int x, int y)
        {
            byte b = GetVramByte(x, y);
            return (byte)((x & 1) == 0 ? b >> 4 : b & 15);
        }

        private void PutPixel(int x, int y, byte c, int op)
        {
            if ((op & 8) != 0 && c == 0) return; // transparent color 0
            int off = (((y & 1023) * 128) + ((x & 255) >> 1)) & 0x1FFFF;
            byte cur = (x & 1) == 0 ? (byte)(Vram[off] >> 4) : (byte)(Vram[off] & 15);
            int res = (op & 7) switch
            {
                1 => cur & c,
                2 => cur | c,
                3 => cur ^ c,
                4 => (~c) & 15,
                _ => c
            };

            if ((x & 1) == 0)
                Vram[off] = (byte)((Vram[off] & 0x0F) | (res << 4));
            else
                Vram[off] = (byte)((Vram[off] & 0xF0) | (res & 0x0F));
        }

        private void ExecuteCommand(byte v)
        {
            int cmd = v >> 4;
            int op = v & 15;
            int sx = RegWord(32, 33) & 511, sy = RegWord(34, 35) & 1023;
            int dx = RegWord(36, 37) & 511, dy = RegWord(38, 39) & 1023;
            int nx = RegWord(40, 41) & 511, ny = RegWord(42, 43) & 1023;
            if (nx == 0) nx = 512;
            if (ny == 0) ny = 1024;

            int arg = Regs[45];
            int ix = (arg & 4) != 0 ? -1 : 1;
            int iy = (arg & 8) != 0 ? -1 : 1;

            switch (cmd)
            {
                case 0xD: // HMMM: high-speed move VRAM -> VRAM
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i += 2)
                            PutVramByte(dx + ix * i, dy + iy * j, GetVramByte(sx + ix * i, sy + iy * j));
                    break;

                case 0xC: // HMMV: high-speed move VRAM fill
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i += 2)
                            PutVramByte(dx + ix * i, dy + iy * j, Regs[44]);
                    break;

                case 0xE: // YMMM
                    int fin = ix > 0 ? 256 : -1;
                    for (int j = 0; j < ny; j++)
                        for (int x = dx; x != fin; x += 2 * ix)
                            PutVramByte(x, dy + iy * j, GetVramByte(x, sy + iy * j));
                    break;

                case 0x9: // LMMM: logical move VRAM -> VRAM (handles transparency)
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                            PutPixel(dx + ix * i, dy + iy * j, GetPixel(sx + ix * i, sy + iy * j), op);
                    break;

                case 0x8: // LMMV: logical fill
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                            PutPixel(dx + ix * i, dy + iy * j, (byte)(Regs[44] & 15), op);
                    break;

                case 0x7: // LINE
                    int maj = arg & 1;
                    int largo = RegWord(40, 41) & 1023, corto = RegWord(42, 43) & 1023;
                    int lx = dx, ly = dy, e = 0;
                    for (int k = 0; k <= largo; k++)
                    {
                        PutPixel(lx, ly, (byte)(Regs[44] & 15), op);
                        if (maj != 0) ly += iy;
                        else lx += ix;
                        e += corto;
                        if (largo != 0 && e * 2 >= largo)
                        {
                            e -= largo;
                            if (maj != 0) lx += ix;
                            else ly += iy;
                        }
                    }
                    break;

                case 0xF: // HMMC: CPU -> VRAM move
                    _hmmc = (dx, dy, nx, ny, 0, ix, iy);
                    HmmcByte(Regs[44]);
                    break;

                case 0:
                    _hmmc = null;
                    break;
            }
        }

        private void HmmcByte(byte v)
        {
            if (_hmmc == null) return;
            var (dx, dy, nx, ny, i, ix, iy) = _hmmc.Value;
            int porFila = (nx + 1) / 2;
            int j = i / porFila;
            int k = i % porFila;

            PutVramByte(dx + ix * 2 * k, dy + iy * j, v);
            i++;
            if (i >= porFila * ny)
                _hmmc = null;
            else
                _hmmc = (dx, dy, nx, ny, i, ix, iy);
        }
    }
}
