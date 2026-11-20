"""Sprites do King's Valley II (MSX2) lidos DIRETO da ROM, emulando as rotinas do próprio jogo.

Uso: python rom_sprites.py <kv2_msx2_scc.rom> <pasta_saida>

A ROM MSX2 (dump ASCII8) precisa estar "desconvertida" para Konami SCC — ver docs/ROM_EXTRACTION.md.
Endereços abaixo são de CPU com o mapeamento do jogo (41CC: banco 14 em 8000, banco 15 em A000).

Rotinas emuladas (banco fixo 0, CPU 4000-5FFF):
- 4E21/4E27 descompressor: byte n -> bit7=0 repete o byte seguinte n vezes; bit7=1 copia n&7F literais;
  0x80 lê um novo endereço de VRAM (2 bytes); 0x00 encerra.
- 4E60/4E6C cópia espelhada VRAM->VRAM de padrões 16x16 (troca as colunas e inverte os bits).
- 58DB conjunto de poses do Vick por E287&0F (tabela 0x858B); 5886 pose E285 -> meias-linhas
  (tabelas 0x869C topo / 0x86B8 base) copiadas por HMMM para os padrões 0-3.
- 5687 (copy_abb9) porta: rle_abb9 -> 0xF800, 1024 bytes = 8 poses de 128 (medido no emulador:
  bate byte a byte com o dump dos quadros da entrada da porta).
- 5928 (E298: queda/morte, 0x953D) e 5937 (E24C: pushups do pause, 0xAB59 + 0xAA00) vão para
  0xE000 (área de trabalho) — NÃO são a arte da porta.
- 594F + 5696 padrões fixos F880-FFFF (0x97A1, listas 0x98C9 e 0x98EA).
- 6664 frames dos inimigos: pares de padrões em 0xBEED (banco 13), cores por tipo em 0xAD0B.
- Paleta fixa das cores 7-15: lista (índice, RB, G) em 0xB95D (banco 15).
"""
import json
import os
import sys

import cv2
import numpy as np

REV = bytes(int(f"{b:08b}"[::-1], 2) for b in range(256))
PLAYER_COLORS = (13, 14)            # 98B9: D200.. = 0x0D / 0x4E (CC)
ENEMY_COLORS = {1: (11, 12), 2: (10, 7), 3: (11, 12), 4: (11, 12),  # AD0B + 2*tipo (bit CC removido)
                5: (10, 11),  # 6624: cores do efeito de morte (0x0A + 0x4B)
                6: (7, 9)}    # B6CB: pedra (tipo 4) depois de surgir (0x07 + 0x49)
PROJECTILE_COLORS = (11, 7)


class Rom:
    def __init__(self, data: bytes):
        self.d = data

    def off(self, addr: int, b8: int = 14, ba: int = 15) -> int:
        if 0x4000 <= addr < 0x6000:
            return addr - 0x4000
        if 0x8000 <= addr < 0xA000:
            return b8 * 0x2000 + addr - 0x8000
        if 0xA000 <= addr < 0xC000:
            return ba * 0x2000 + addr - 0xA000
        raise ValueError(hex(addr))

    def byte(self, addr: int, **kw) -> int:
        return self.d[self.off(addr, **kw)]

    def word(self, addr: int, **kw) -> int:
        o = self.off(addr, **kw)
        return self.d[o] | self.d[o + 1] << 8


def decompress(rom: Rom, vram: bytearray, src: int, dest: int) -> None:
    p = rom.off(src)
    d = rom.d
    while True:
        n = d[p]
        p += 1
        if n == 0:
            return
        if n & 0x80:
            cnt = n & 0x7F
            if cnt == 0:
                dest = d[p] | d[p + 1] << 8
                p += 2
                continue
            vram[dest:dest + cnt] = d[p:p + cnt]
            p += cnt
            dest += cnt
        else:
            vram[dest:dest + n] = bytes([d[p]]) * n
            p += 1
            dest += n


def mirror_copy(vram: bytearray, dest: int, src: int, count: int) -> None:
    """4E60: dest aponta para a coluna direita do padrão de destino."""
    for _ in range(count):
        for half in range(2):
            for k in range(16):
                vram[dest - 16 * half + k] = REV[vram[src + half * 16 + k]]
        src += 32
        dest += 32


def player_set(rom: Rom, vram: bytearray, s: int) -> None:
    p = rom.off(rom.word(0x858B + 2 * s))
    d = rom.d
    while d[p] != 0xFF:
        b0, b1, b2, b3 = d[p:p + 4]
        p += 4
        if b0 & 0x0F == 0:
            decompress(rom, vram, b2 | b3 << 8, b0 | b1 << 8)
        else:
            mirror_copy(vram, (b0 & 0xF0) | b1 << 8, b2 | b3 << 8, b0 & 0x0F)


def static_patterns(rom: Rom, vram: bytearray) -> None:
    decompress(rom, vram, 0x97A1, 0xF880)
    d = rom.d
    p = rom.off(0x98C9)
    while d[p]:
        n, dst, lo, hi = d[p:p + 4]
        p += 4
        s = rom.off(lo | hi << 8)
        vram[0xF800 + dst * 8:0xF800 + dst * 8 + n * 32] = d[s:s + n * 32]
    p = rom.off(0x98EA)
    while d[p]:
        op, n, a, b = d[p:p + 4]
        p += 4
        src, dst = 0xF800 + a * 8, 0xF800 + b * 8
        if op == 2:
            mirror_copy(vram, dst + 16, src, n)
        else:
            for i in range(n):
                blk = bytes(vram[src + 32 * i:src + 32 * i + 32])
                vram[dst + 32 * i:dst + 32 * i + 16] = blk[0:16][::-1]
                vram[dst + 32 * i + 16:dst + 32 * i + 32] = blk[16:32][::-1]


def palette(rom: Rom) -> list:
    pal = [(0, 0, 0)] * 16
    p = rom.off(0xB95D)
    d = rom.d
    while d[p] != 0xFF:
        i, rb, g = d[p:p + 3]
        r, b, gg = (rb >> 4) & 7, rb & 7, g & 7
        pal[i] = tuple(int(c * 255 / 7) for c in (r, gg, b))
        p += 3
    return pal


def pat16(b: bytes) -> np.ndarray:
    m = np.zeros((16, 16), bool)
    for row in range(16):
        for half in range(2):
            bits = b[half * 16 + row]
            for k in range(8):
                m[row, half * 8 + k] = bits >> (7 - k) & 1
    return m


def compose(a: np.ndarray, b: np.ndarray, ca: int, cb: int) -> np.ndarray:
    """Sprite A (cor ca) + sprite B com bit CC: onde se sobrepõem a cor é ca|cb."""
    idx = np.zeros(a.shape, np.uint8)
    idx[a] = ca
    idx[b] |= cb
    return idx


def to_bgra(idx: np.ndarray, pal: list) -> np.ndarray:
    out = np.zeros(idx.shape + (4,), np.uint8)
    for i in range(1, 16):
        r, g, b = pal[i]
        out[idx == i] = (b, g, r, 255)
    return out


def pose_from_line(line: bytes, colors=PLAYER_COLORS) -> np.ndarray:
    """128 bytes = 4 padrões: topo (A, B) e base (A, B)."""
    top = compose(pat16(line[0:32]), pat16(line[32:64]), *colors)
    bot = compose(pat16(line[64:96]), pat16(line[96:128]), *colors)
    return np.vstack([top, bot])


def page1_line(vram: bytearray, y: int, x: int = 0, nbytes: int = 128) -> bytes:
    a = 0x8000 + y * 128 + x // 2
    return bytes(vram[a:a + nbytes])


def build(rom: Rom) -> dict:
    sprites = {}
    pal = palette(rom)
    tops = [(rom.byte(0x869C + 2 * i), rom.byte(0x869D + 2 * i)) for i in range(14)]
    bots = [(rom.byte(0x86B8 + 2 * i), rom.byte(0x86B9 + 2 * i)) for i in range(14)]
    for s in range(7):
        vram = bytearray(0x20000)
        player_set(rom, vram, s)
        for i, ((ty, tx), (by, bx)) in enumerate(zip(tops, bots)):
            line = page1_line(vram, ty, tx, 64) + page1_line(vram, by, bx, 64)
            sprites[f"vick_s{s}_{i:02d}"] = to_bgra(pose_from_line(line), pal)
    # 5687 (copy_abb9, banco 0) descomprime rle_abb9 (0xABB9) em 0xF800 = as OITO poses da
    # entrada da porta (1024 bytes = 8 × 128; o grupo exibido é 2*E258 + bit4(E259), 0..7).
    # Medido no openMSX: bate byte a byte com o dump da sequência (f100, f162, f235).
    for prefix, streams in (("vick_x", ((0x953D, 0xE000),)),):
        vram = bytearray(0x20000)
        for src, dst in streams:
            decompress(rom, vram, src, dst)
        for i in range(16):
            line = page1_line(vram, 0xC0 + i)
            if any(line):
                sprites[f"{prefix}_{i:02d}"] = to_bgra(pose_from_line(line), pal)
    vram = bytearray(0x20000)
    decompress(rom, vram, 0xABB9, 0xF800)
    for i in range(8):
        line = bytes(vram[0xF800 + i * 128:0xF800 + (i + 1) * 128])
        sprites[f"vick_d_{i:02d}"] = to_bgra(pose_from_line(line), pal)
    vram = bytearray(0x20000)
    static_patterns(rom, vram)
    frames = 26
    for f in range(frames):
        pa = rom.byte(0xBEED + 2 * f, ba=13) // 4
        pb = rom.byte(0xBEEE + 2 * f, ba=13) // 4
        if pa == 0 and pb == 0:
            continue
        a = pat16(vram[0xF800 + pa * 32:0xF800 + pa * 32 + 32])
        b = pat16(vram[0xF800 + pb * 32:0xF800 + pb * 32 + 32])
        for t, cols in ENEMY_COLORS.items():
            sprites[f"enemy_t{t}_{f:02d}"] = to_bgra(compose(a, b, *cols), pal)
    for n in range(4, 16, 2):        # faca/bumerangue em voo: pares, cores 0x0B + 0x47 (SAT medido)
        a = pat16(vram[0xF800 + n * 32:0xF800 + n * 32 + 32])
        b = pat16(vram[0xF800 + (n + 1) * 32:0xF800 + (n + 1) * 32 + 32])
        sprites[f"proj_{n:02d}"] = to_bgra(compose(a, b, *PROJECTILE_COLORS), pal)
    return {"sprites": sprites, "palette": pal}


def main() -> None:
    rom = Rom(open(sys.argv[1], "rb").read())
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    res = build(rom)
    for name, img in res["sprites"].items():
        cv2.imwrite(os.path.join(out, name + ".png"), img)
    with open(os.path.join(out, "palette.json"), "w") as f:
        json.dump(res["palette"], f)
    print(len(res["sprites"]), "sprites ->", out)


if __name__ == "__main__":
    main()
