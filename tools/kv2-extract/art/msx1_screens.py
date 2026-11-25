"""Converte dumps de VRAM do MSX1 (SCREEN 2) para o formato .bin do port (4bpp + paleta).

Uso (de dentro de tools/kv2-extract/work):
    python ../art/msx1_screens.py <dump_vram.bin> <dump_regs.bin> <saida.bin>

Formato de saida (o mesmo do ScreenAssembler):
    32 bytes de paleta VDP MSX2 ((R<<4)|B, G, 3 bits por canal)
    2 bytes de largura, 2 de altura (256x192 no MSX1)
    1 byte de flags (0), 1 byte reservado, depois o bitmap 4bpp empacotado.

SCREEN 2 do TMS9918: a cor de cada celula vem do color table (fg/bg de 4 bits por 8 linhas),
o padrao e 1bpp. As 16 cores do TMS viram os indices 0..15 da paleta.
"""
import struct
import sys

TMS = [
    (0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
    (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
    (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
    (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255),
]
W, H = 256, 192


def to3(v):
    return max(0, min(7, round(v * 7 / 255)))


def convert(vram, regs):
    """Converte um dump SCREEN 2 (VRAM + regs) no buffer .bin do port."""
    name_base = (regs[2] & 0x0F) * 0x400
    pat_base = (regs[4] & 0x3C) << 11
    col_base = (regs[3] & 0x80) << 6

    p_mask = regs[4] & 0x03
    c_mask = (regs[3] & 0x03) if (regs[3] & 0x7F) < 4 else 3

    idx = bytearray(W * H)
    for ty in range(24):
        third = ty // 8
        p_bank = (third & p_mask) * 0x800
        c_bank = (third & c_mask) * 0x800
        for tx in range(32):
            code = vram[(name_base + ty * 32 + tx) % 0x4000]
            for row in range(8):
                pat = vram[(pat_base + p_bank + code * 8 + row) % 0x4000]
                col = vram[(col_base + c_bank + code * 8 + row) % 0x4000]
                fg, bg = (col >> 4) & 0x0F, col & 0x0F
                for bit in range(8):
                    on = (pat >> (7 - bit)) & 1
                    idx[(ty * 8 + row) * W + tx * 8 + bit] = fg if on else bg

    header = bytearray(32)
    for i, (r, g, b) in enumerate(TMS):
        header[i * 2] = (to3(r) << 4) | to3(b)
        header[i * 2 + 1] = to3(g)
    header += struct.pack("<HH", W, H)
    header.append(0)
    header.append(0)

    body = bytearray((W * H) // 2)
    for n, v in enumerate(idx):
        body[n >> 1] |= v << (4 if (n & 1) == 0 else 0)

    return bytes(header) + bytes(body)


def main():
    vram = open(sys.argv[1], "rb").read()
    regs = open(sys.argv[2], "rb").read()
    open(sys.argv[3], "wb").write(convert(vram, regs))
    print("ok:", sys.argv[3])


if __name__ == "__main__":
    main()
