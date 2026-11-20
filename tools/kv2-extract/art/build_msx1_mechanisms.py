"""Gera os arquivos binarios de mecanismos para MSX1:
- Content/RomMsx1/Rooms/mechanism_t1_p0.bin .. p3.bin (16x24, 4bpp, 192 bytes)
- Content/RomMsx1/Rooms/mechanism_t4.bin (32x8, 4bpp, 128 bytes)
E sincroniza com %APPDATA%/KingsValley/extracted/Rooms/ conforme regras do AGENTS.md.
"""
import os

WORK = r'tools/kv2-extract/work'
VRAM_FILE = os.path.join(WORK, 'msx1_rooms', 's02_r01_vram.bin')
REGS_FILE = os.path.join(WORK, 'msx1_rooms', 's02_r01_regs.bin')

vram = open(VRAM_FILE, 'rb').read()
regs = open(REGS_FILE, 'rb').read()

pat_base = (regs[4] & 0x3C) << 11
col_base = (regs[3] & 0x80) << 6
p_mask = regs[4] & 0x03
c_mask = (regs[3] & 0x03) if (regs[3] & 0x7F) < 4 else 3
third = 1
p_bank = (third & p_mask) * 0x800
c_bank = (third & c_mask) * 0x800

def get_tile(code):
    rows = []
    for row in range(8):
        pat = vram[(pat_base + p_bank + code * 8 + row) % 0x4000]
        col = vram[(col_base + c_bank + code * 8 + row) % 0x4000]
        fg, bg = (col >> 4) & 0x0F, col & 0x0F
        rows.append([(fg if ((pat >> (7 - bit)) & 1) else bg) for bit in range(8)])
    return rows

phases = [
    (0xA8, 0xA9),
    (0xAA, 0xAB),
    (0xBF, 0xBE),
    (0xBD, 0xBC)
]

DEST_ROMMSX1 = r'src/KingsValley.Desktop/Content/RomMsx1/Rooms'
APPDATA_ROOMS = os.path.expandvars(r'%APPDATA%/KingsValley/extracted/Rooms')
os.makedirs(DEST_ROMMSX1, exist_ok=True)
if os.path.exists(APPDATA_ROOMS):
    os.makedirs(APPDATA_ROOMS, exist_ok=True)

# Generate 4 phases of Rotating Door (type 1)
for p, (c0, c1) in enumerate(phases):
    t0 = get_tile(c0)
    t1 = get_tile(c1)
    
    # 16x8 block
    block_rows = []
    for r in range(8):
        row = t0[r] + t1[r]
        # Any pixel matching mural background (5) is transparent (0)
        row_clean = [(0 if x == 5 else x) for x in row]
        block_rows.append(row_clean)
        
    # Repeat 3 times to make 16x24 (24 rows)
    full_24 = block_rows * 3
    assert len(full_24) == 24
    
    # Pack into 4bpp nibbles: 24 * 8 = 192 bytes
    buf = bytearray()
    for row in full_24:
        for c in range(0, 16, 2):
            buf.append((row[c] << 4) | row[c+1])
    assert len(buf) == 192
    
    f1 = os.path.join(DEST_ROMMSX1, f'mechanism_t1_p{p}.bin')
    open(f1, 'wb').write(buf)
    print(f'Wrote {f1} ({len(buf)} bytes)')
    
    if os.path.exists(APPDATA_ROOMS):
        f2 = os.path.join(APPDATA_ROOMS, f'mechanism_t1_p{p}.bin')
        open(f2, 'wb').write(buf)
        print(f'Synced {f2}')

# Generate Type 4 mechanism (trap bar: 4 copies of 0x61 = 32x8, 128 bytes)
t4_vram_file = os.path.join(WORK, 'msx1_rooms', 's10_r01_vram.bin')
if os.path.exists(t4_vram_file):
    vram4 = open(t4_vram_file, 'rb').read()
    regs4 = open(t4_vram_file.replace('_vram.bin', '_regs.bin'), 'rb').read()
    pb4 = (regs4[4] & 0x3C) << 11
    cb4 = (regs4[3] & 0x80) << 6
    pm4 = regs4[4] & 0x03
    cm4 = (regs4[3] & 0x03) if (regs4[3] & 0x7F) < 4 else 3
    p_bank4 = (third & pm4) * 0x800
    c_bank4 = (third & cm4) * 0x800

    def get_tile4(code):
        rows = []
        for row in range(8):
            pat = vram4[(pb4 + p_bank4 + code * 8 + row) % 0x4000]
            col = vram4[(cb4 + c_bank4 + code * 8 + row) % 0x4000]
            fg, bg = (col >> 4) & 0x0F, col & 0x0F
            rows.append([(fg if ((pat >> (7 - bit)) & 1) else bg) for bit in range(8)])
        return rows

    t = get_tile4(0x61)
    buf4 = bytearray()
    for r in range(8):
        row32 = t[r] * 4
        for c in range(0, 32, 2):
            buf4.append((row32[c] << 4) | row32[c+1])
    assert len(buf4) == 128
    
    f1 = os.path.join(DEST_ROMMSX1, 'mechanism_t4.bin')
    open(f1, 'wb').write(buf4)
    print(f'Wrote {f1} ({len(buf4)} bytes)')
    
    if os.path.exists(APPDATA_ROOMS):
        f2 = os.path.join(APPDATA_ROOMS, 'mechanism_t4.bin')
        open(f2, 'wb').write(buf4)
        print(f'Synced {f2}')

print('MSX1 mechanisms build complete!')
