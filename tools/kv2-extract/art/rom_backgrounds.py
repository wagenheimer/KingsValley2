"""Fundos (mural + decorações) EXATOS de cada sala, em ÍNDICES de cor (para animar a cor 6 no jogo).

Uso: python rom_backgrounds.py <kv2_msx2_scc.rom> <levels_dir> <saida> <dump_p1> ... <dump_p6>

Emula o desenho da sala do jogo (44D4):
- 44F2: mural = padrão de 8x4 blocos (tabela 0x7FFE do banco 1, por pirâmide; +0x20 nos stages
  pares) repetido 4x na horizontal e 6x na vertical, cópia opaca (5207).
- 4606: decorações da sala — lista por stage (tabela 0x806A, banco 14): registros de 2 bytes
  [y&F8 | tipo(3 bits)] [x&F8 | sala-1]; desenhos por (pirâmide-1)*8 + tipo (tabela 0x800C):
  tipo>>1 = 0 → 4x4 blocos, 1 → 4 linhas x 2, 2 → 2 linhas x 4, 3 → pilar (2 de largura, altura =
  1º byte do registro seguinte). Cópia com transparência (5219: cor 0 não pinta).
Os blocos vêm da página 1 da VRAM de um dump do 1º stage de cada pirâmide (bloco n → x=(n&31)*8,
y=(n>>5)*8). Saída: PNG de 8 bits com o índice de cor (0..15) por pixel, um por sala.
"""
import json
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rom_sprites import Rom  # noqa: E402


def page1(prefix: str) -> np.ndarray:
    v = open(prefix + "_vram.bin", "rb").read()
    a = np.frombuffer(v[0x8000:0x10000], np.uint8).reshape(256, 128)
    img = np.zeros((256, 256), np.uint8)
    img[:, 0::2] = a >> 4
    img[:, 1::2] = a & 15
    return img


def tile(p1: np.ndarray, n: int) -> np.ndarray:
    return p1[(n >> 5) * 8:(n >> 5) * 8 + 8, (n & 31) * 8:(n & 31) * 8 + 8]


def put(img: np.ndarray, p1: np.ndarray, n: int, x: int, y: int, opaque: bool) -> None:
    if not (0 <= x <= 248 and 0 <= y <= 184):
        return
    t = tile(p1, n)
    dst = img[y:y + 8, x:x + 8]
    if opaque:
        dst[:] = t
    else:
        dst[t != 0] = t[t != 0]


def mural(rom: Rom, pyramid: int, stage: int, p1: np.ndarray) -> np.ndarray:
    # 44F2: 0x7FFE + 2*pirâmide (pirâmide >= 1 → já cai em 0x8000, banco 14)
    base = rom.word(0x7FFE + 2 * pyramid) + (0x20 if stage % 2 == 1 else 0)   # 44FE: RRA → ímpar soma 0x20
    codes = rom.d[rom.off(base):rom.off(base) + 32]
    img = np.zeros((192, 256), np.uint8)
    for by in range(6):
        for row in range(4):
            for bx in range(4):
                for col in range(8):
                    put(img, p1, codes[row * 8 + col], (bx * 8 + col) * 8, (by * 4 + row) * 8, True)
    return img


def decorations(rom: Rom, pyramid: int, stage: int, room: int, p1: np.ndarray, img: np.ndarray) -> None:
    lst = rom.off(rom.word(0x806A + 2 * stage))
    d = rom.d
    while d[lst] != 0xFF:
        b0, b1 = d[lst], d[lst + 1]
        nxt = d[lst + 2]
        lst += 3          # 4634/4635: INC HL x2 -> registos de 3 bytes (o 3o e a altura do pilar)
        if (b1 & 7) != room - 1:
            continue
        kind, y, x = b0 & 7, b0 & 0xF8, b1 & 0xF8
        gfx = rom.off(rom.word(0x800C + 2 * ((pyramid - 1) * 8 + kind)))
        shape = kind >> 1
        if shape == 3:   # 4679/4690: pilar de 2 colunas; linhas = altura-1, so usa gfx[0..3]
            height = nxt
            put(img, p1, d[gfx], x, y, False)
            put(img, p1, d[gfx + 1], x + 8, y, False)
            for r in range(height - 2):
                put(img, p1, d[gfx + 2], x, y + (r + 1) * 8, False)
                put(img, p1, d[gfx + 3], x + 8, y + (r + 1) * 8, False)
            continue
        rows, cols = {0: (4, 4), 1: (4, 2), 2: (2, 4)}[shape]
        for r in range(rows):
            for c in range(cols):
                put(img, p1, d[gfx + r * cols + c], x + c * 8, y + r * 8, False)


def main() -> None:
    rom = Rom(open(sys.argv[1], "rb").read())
    levels, out = sys.argv[2], sys.argv[3]
    pages = [page1(p) for p in sys.argv[4:]]
    os.makedirs(out, exist_ok=True)
    count = 0
    for stage in range(1, 61):
        path = os.path.join(levels, f"stage{stage:02d}.json")
        if not os.path.exists(path):
            continue
        rooms = [r["index"] for r in json.load(open(path, encoding="utf-8"))["rooms"]]
        pyramid = (stage - 1) // 10 + 1
        p1 = pages[pyramid - 1]
        for room in rooms:
            img = mural(rom, pyramid, stage, p1)
            decorations(rom, pyramid, stage, room, p1, img)
            cv2.imwrite(os.path.join(out, f"s{stage:02d}_r{room}.png"), img)
            count += 1
    print(count, "fundos indexados ->", out)


if __name__ == "__main__":
    main()
