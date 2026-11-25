"""Objetos de bitmap do MSX2 (itens no chão e sarcófago) EXATOS, a partir dos blocos 8x8 da página 1.

Uso: python rom_tiles.py <pasta_saida> <dump_p1> <dump_p2> ... <dump_p6>

Cada dump é o prefixo de um dumpall do openMSX (…_vram.bin / …_pal.bin) feito no 1º stage da pirâmide.
O jogo desenha objetos copiando blocos 8x8 da página 1 com transparência (5219, operação 0x48):
código n -> origem (x = (n & 31) * 8, y = (n >> 5) * 8). As listas de códigos estão no código:
- 9116: itens, tabela 0x9328 (banco 2), 4 códigos (2x2) por tipo 1..6.
- 9010: porta de saída (3 quadros 4x4, 0x9340) + um pino 0xAE por pedra que falta.
- AD53: sarcófago 3x2 (16x24, 8 px acima do y do registro): fechado 0x93AC, entreaberto 0x93B2, aberto 0x93B8.
"""
import os
import sys

import cv2
import numpy as np

ITEM_TILES = {
    "knife": [129, 130, 188, 189],
    "boomerang": [0, 0, 137, 138],
    "shovel": [134, 135, 184, 136],
    "pickaxe": [185, 187, 186, 131],
    "hammer": [180, 181, 182, 132],
    "drill": [178, 179, 183, 133],
}
DOOR_FRAMES = [  # 9019: tabela 0x9340, 3 quadros 4x4 (E2F6)
    [99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114],
    [100, 88, 89, 101, 104, 90, 91, 105, 108, 92, 93, 109, 112, 120, 120, 113],
    [115, 88, 89, 116, 117, 90, 91, 118, 117, 92, 93, 118, 119, 120, 120, 121],
]
DOOR_STUD = 0xAE  # 9071: pino de pedra da alma que falta (MSX2)
COFFIN_TILES = {  # AE6F/AD2C/AE39: fechado, entreaberto, aberto
    "coffin": list(range(155, 161)),
    "coffin_ajar": list(range(161, 167)),
    "coffin_open": list(range(167, 173)),
}


def load(prefix: str):
    v = open(prefix + "_vram.bin", "rb").read()
    raw = open(prefix + "_pal.bin", "rb").read()
    pal = []
    for i in range(16):
        b0, b1 = raw[2 * i], raw[2 * i + 1]
        r, b, g = (b0 >> 4) & 7, b0 & 7, b1 & 7
        pal.append(tuple(int(c * 255 / 7) for c in (r, g, b)))
    a = np.frombuffer(v[0x8000:0x10000], np.uint8).reshape(256, 128)
    page1 = np.zeros((256, 256), np.uint8)
    page1[:, 0::2] = a >> 4
    page1[:, 1::2] = a & 15
    return page1, pal


def obj(page1: np.ndarray, codes: list, cols: int) -> np.ndarray:
    def tile(n: int) -> np.ndarray:
        if n == 0:
            return np.zeros((8, 8), np.uint8)
        return page1[(n >> 5) * 8:(n >> 5) * 8 + 8, (n & 31) * 8:(n & 31) * 8 + 8]
    rows = [np.hstack([tile(c) for c in codes[r * cols:(r + 1) * cols]]) for r in range(len(codes) // cols)]
    return np.vstack(rows)


def bgra(idx: np.ndarray, pal: list) -> np.ndarray:
    out = np.zeros(idx.shape + (4,), np.uint8)
    for i in range(1, 16):
        r, g, b = pal[i]
        out[idx == i] = (b, g, r, 255)
    return out


def main() -> None:
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for p, prefix in enumerate(sys.argv[2:], start=1):
        page1, pal = load(prefix)
        for name, codes in COFFIN_TILES.items():
            cv2.imwrite(os.path.join(out, f"{name}_p{p}.png"), bgra(obj(page1, codes, 2), pal))
        for f, codes in enumerate(DOOR_FRAMES):
            cv2.imwrite(os.path.join(out, f"door_f{f}_p{p}.png"), bgra(obj(page1, codes, 4), pal))
        cv2.imwrite(os.path.join(out, f"door_stud_p{p}.png"), bgra(obj(page1, [DOOR_STUD], 1), pal))
        for code in range(6, 0x18):   # AB03/ABE9/ABEF: etapas do bloco esfarelando
            cv2.imwrite(os.path.join(out, f"crumble_{code}_p{p}.png"), bgra(obj(page1, [code], 1), pal))
        if p == 1:
            for name, codes in ITEM_TILES.items():
                cv2.imwrite(os.path.join(out, name + ".png"), bgra(obj(page1, codes, 2), pal))
    print("objetos ->", out)


if __name__ == "__main__":
    main()
