"""Gera o arquivo binario Content/RomMsx1/Rooms/murals.bin com o bitmap 4bpp
do mural limpo (64x32 pixels = 8x4 celulas) para cada uma das 6 piramides do MSX1.

As formulas e tabelas vem diretamente da ROM Konami do King's Valley 2 (MSX1, RC-760).
Offset das tabelas na ROM (segmento 14):
  P1: 0x1C104 (32 bytes = 4 linhas x 8 colunas)
  P2: 0x1C144
  P3: 0x1C184
  P4: 0x1C1A4
  P5: 0x1C204
  P6: 0x1C244
"""
import os
import struct

WORK = r'tools/kv2-extract/work'
ROM_PATH = os.path.join(WORK, 'rom', 'kv2_msx1.rom')
rom = open(ROM_PATH, 'rb').read()

pyr_offsets = {
    1: 0x1C104,
    2: 0x1C144,
    3: 0x1C184,
    4: 0x1C1A4,
    5: 0x1C204,
    6: 0x1C244
}

out_data = bytearray()

for pyr in range(1, 7):
    st = (pyr - 1) * 10 + 1
    vf = os.path.join(WORK, 'msx1_rooms', f's{st:02d}_r01_vram.bin')
    vram = open(vf, 'rb').read()
    regs_f = vf.replace('_vram.bin', '_regs.bin')
    regs = open(regs_f, 'rb').read() if os.path.exists(regs_f) else bytes([0]*8)
    
    pat_base = (regs[4] & 0x3C) << 11 if len(regs) > 4 else 0x2000
    col_base = (regs[3] & 0x80) << 6 if len(regs) > 3 else 0x0000
    p_mask = regs[4] & 0x03 if len(regs) > 4 else 3
    c_mask = (regs[3] & 0x03) if len(regs) > 3 and (regs[3] & 0x7F) < 4 else 3

    off = pyr_offsets[pyr]
    table = list(rom[off : off + 32])
    
    # Render 64x32 4bpp nibbles
    # 64 cols x 32 rows = 2048 pixels = 1024 bytes packed (2 pixels per byte, high nibble first)
    pixels = bytearray(64 * 32)
    for row in range(4):
        for col in range(8):
            code = table[row * 8 + col]
            for r in range(8):
                pat = vram[(pat_base + code * 8 + r) % 0x4000]
                col_b = vram[(col_base + code * 8 + r) % 0x4000]
                fg = (col_b >> 4) & 0x0F
                bg = col_b & 0x0F
                for c in range(8):
                    bit = (pat >> (7 - c)) & 1
                    pixels[(row * 8 + r) * 64 + (col * 8 + c)] = fg if bit else bg

    # Pack into 4bpp bytes
    packed = bytearray(len(pixels) // 2)
    for i in range(len(packed)):
        hi = pixels[i * 2] & 0x0F
        lo = pixels[i * 2 + 1] & 0x0F
        packed[i] = (hi << 4) | lo
    out_data.extend(packed)
    print(f'Pyramid {pyr}: packed 64x32 mural ({len(packed)} bytes)')

dest = 'src/KingsValley.Desktop/Content/RomMsx1/Rooms/murals.bin'
with open(dest, 'wb') as f:
    f.write(out_data)

print(f'Successfully wrote murals.bin to {dest} ({len(out_data)} bytes)')
