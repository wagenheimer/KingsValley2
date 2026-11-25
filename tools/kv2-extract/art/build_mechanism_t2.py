import os

# 1. Build MSX2 mechanism_t2
blocks_p1 = open("src/KingsValley.Desktop/Content/Rom/Rooms/blocks_p1.bin", "rb").read()

def get_msx2_block(code):
    if code == 0:
        return [[0]*8 for _ in range(8)]
    base = code * 32
    rows = []
    for r in range(8):
        row_bytes = blocks_p1[base + r*4 : base + (r+1)*4]
        pix = []
        for b in row_bytes:
            pix.append(b >> 4)
            pix.append(b & 0x0F)
        rows.append(pix)
    return rows

# Tables from ROM for MSX2:
# 0x937c: Dir 0 Top: [0xbe, 0x00, 0x95, 0xc1, 0x7a, 0x7b, 0x95, 0xc1]
# 0x9384: Dir 0 Mid: [0xbf, 0x00, 0x96, 0xc1, 0x7c, 0x7d, 0x96, 0xc1]
# 0x938c: Dir 0 Bot: [0xc0, 0x00, 0x97, 0xc3, 0x7e, 0x7f, 0x97, 0xc3]
# 0x9394: Dir 1 Top: [0x00, 0xc4, 0xc7, 0x98, 0x7a, 0x7b, 0xc7, 0x98]
# 0x939c: Dir 1 Mid: [0x00, 0xc5, 0xc8, 0x99, 0x7c, 0x7d, 0xc8, 0x99]
# 0x93a4: Dir 1 Bot: [0x00, 0xc6, 0xc9, 0x9a, 0x7e, 0x7f, 0xc9, 0x9a]

msx2_tables = {
    0: { # Dir 0
        'top': [(0xbe, 0x00), (0x95, 0xc1), (0x7a, 0x7b), (0x95, 0xc1)],
        'mid': [(0xbf, 0x00), (0x96, 0xc1), (0x7c, 0x7d), (0x96, 0xc1)],
        'bot': [(0xc0, 0x00), (0x97, 0xc3), (0x7e, 0x7f), (0x97, 0xc3)]
    },
    1: { # Dir 1
        'top': [(0x00, 0xc4), (0xc7, 0x98), (0x7a, 0x7b), (0xc7, 0x98)],
        'mid': [(0x00, 0xc5), (0xc8, 0x99), (0x7c, 0x7d), (0xc8, 0x99)],
        'bot': [(0x00, 0xc6), (0xc9, 0x9a), (0x7e, 0x7f), (0xc9, 0x9a)]
    }
}

DEST_ROM = "src/KingsValley.Desktop/Content/Rom/Rooms"
APPDATA_ROOMS = os.path.expandvars(r"%APPDATA%/KingsValley/extracted/Rooms")

for d in (0, 1):
    for p in range(4):
        c_top0, c_top1 = msx2_tables[d]['top'][p]
        c_mid0, c_mid1 = msx2_tables[d]['mid'][p]
        c_bot0, c_bot1 = msx2_tables[d]['bot'][p]
        
        t_top0, t_top1 = get_msx2_block(c_top0), get_msx2_block(c_top1)
        t_mid0, t_mid1 = get_msx2_block(c_mid0), get_msx2_block(c_mid1)
        t_bot0, t_bot1 = get_msx2_block(c_bot0), get_msx2_block(c_bot1)
        
        full_24 = []
        for r in range(8): full_24.append(t_top0[r] + t_top1[r])
        for r in range(8): full_24.append(t_mid0[r] + t_mid1[r])
        for r in range(8): full_24.append(t_bot0[r] + t_bot1[r])
        assert len(full_24) == 24
        
        buf = bytearray()
        for row in full_24:
            for c in range(0, 16, 2):
                buf.append((row[c] << 4) | row[c+1])
        assert len(buf) == 192
        
        fout = os.path.join(DEST_ROM, f"mechanism_t2_d{d}_p{p}.bin")
        open(fout, "wb").write(buf)
        print(f"Wrote MSX2 {fout}")

# 2. Build MSX1 mechanism_t2
vram1 = open('tools/kv2-extract/work/msx1_rooms/s02_r01_vram.bin', 'rb').read()
regs1 = open('tools/kv2-extract/work/msx1_rooms/s02_r01_regs.bin', 'rb').read()

pb1 = (regs1[4] & 0x3C) << 11
cb1 = (regs1[3] & 0x80) << 6
pm1 = regs1[4] & 0x03
cm1 = (regs1[3] & 0x03) if (regs1[3] & 0x7F) < 4 else 3
third = 1
p_bank1 = (third & pm1) * 0x800
c_bank1 = (third & cm1) * 0x800

def get_msx1_tile(code):
    if code == 0:
        return [[0]*8 for _ in range(8)]
    rows = []
    for row in range(8):
        pat = vram1[(pb1 + p_bank1 + code * 8 + row) % 0x4000]
        col = vram1[(cb1 + c_bank1 + code * 8 + row) % 0x4000]
        fg, bg = (col >> 4) & 0x0F, col & 0x0F
        # In SCREEN 2, color 5 is the blue background mural, color 1 is black border
        # We preserve colors, but if pixel is background (5), map to 0 (transparent)
        r_pix = [(fg if ((pat >> (7 - bit)) & 1) else bg) for bit in range(8)]
        r_clean = [(0 if x == 5 else x) for x in r_pix]
        rows.append(r_clean)
    return rows

# Tables from ROM for MSX1:
# 0x9095: Dir 0 Top: [0xb4, 0x00, 0xb7, 0xb9, 0x9c, 0x9d, 0xb7, 0xb9]
# 0x909d: Dir 0 Mid: [0xb5, 0x00, 0xa5, 0xba, 0x9e, 0x9f, 0xa5, 0xba]
# 0x90a5: Dir 0 Bot: [0xb6, 0x00, 0xb8, 0xbb, 0xa6, 0xa7, 0xb8, 0xbb]
# 0x90ad: Dir 1 Top: [0x00, 0xc8, 0xcd, 0xcb, 0x9c, 0x9d, 0xcd, 0xcb]
# 0x90b5: Dir 1 Mid: [0x00, 0xc9, 0xce, 0xa5, 0x9e, 0x9f, 0xce, 0xa5]
# 0x90bd: Dir 1 Bot: [0x00, 0xca, 0xcf, 0xcc, 0xa6, 0xa7, 0xcf, 0xcc]

msx1_tables = {
    0: { # Dir 0
        'top': [(0xb4, 0x00), (0xb7, 0xb9), (0x9c, 0x9d), (0xb7, 0xb9)],
        'mid': [(0xb5, 0x00), (0xa5, 0xba), (0x9e, 0x9f), (0xa5, 0xba)],
        'bot': [(0xb6, 0x00), (0xb8, 0xbb), (0xa6, 0xa7), (0xb8, 0xbb)]
    },
    1: { # Dir 1
        'top': [(0x00, 0xc8), (0xcd, 0xcb), (0x9c, 0x9d), (0xcd, 0xcb)],
        'mid': [(0x00, 0xc9), (0xce, 0xa5), (0x9e, 0x9f), (0xce, 0xa5)],
        'bot': [(0x00, 0xca), (0xcf, 0xcc), (0xa6, 0xa7), (0xcf, 0xcc)]
    }
}

DEST_ROMMSX1 = "src/KingsValley.Desktop/Content/RomMsx1/Rooms"

for d in (0, 1):
    for p in range(4):
        c_top0, c_top1 = msx1_tables[d]['top'][p]
        c_mid0, c_mid1 = msx1_tables[d]['mid'][p]
        c_bot0, c_bot1 = msx1_tables[d]['bot'][p]
        
        t_top0, t_top1 = get_msx1_tile(c_top0), get_msx1_tile(c_top1)
        t_mid0, t_mid1 = get_msx1_tile(c_mid0), get_msx1_tile(c_mid1)
        t_bot0, t_bot1 = get_msx1_tile(c_bot0), get_msx1_tile(c_bot1)
        
        full_24 = []
        for r in range(8): full_24.append(t_top0[r] + t_top1[r])
        for r in range(8): full_24.append(t_mid0[r] + t_mid1[r])
        for r in range(8): full_24.append(t_bot0[r] + t_bot1[r])
        assert len(full_24) == 24
        
        buf = bytearray()
        for row in full_24:
            for c in range(0, 16, 2):
                buf.append((row[c] << 4) | row[c+1])
        assert len(buf) == 192
        
        fout = os.path.join(DEST_ROMMSX1, f"mechanism_t2_d{d}_p{p}.bin")
        open(fout, "wb").write(buf)
        print(f"Wrote MSX1 {fout}")
        
        if os.path.exists(APPDATA_ROOMS):
            f_app = os.path.join(APPDATA_ROOMS, f"mechanism_t2_d{d}_p{p}.bin")
            open(f_app, "wb").write(buf)

print("Mechanism 2 generation finished!")
