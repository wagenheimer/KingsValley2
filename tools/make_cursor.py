"""Gera o cursor do jogo (arte ORIGINAL do projeto, nada da ROM): seta dourada egípcia com faixa de
lápis-lazúli — o mesmo cursor que o futuro Level Editor vai usar.

Saída (Content/):
  cursor.png           16x16 — cursor virtual (gamepad/editor), desenhado em unidades de design
  cursor_hardware.png  48x48 — cursor do mouse (3x, pixels de tela); hotspot = ponta (0, 0)

Uso: python tools/make_cursor.py
"""
import os
import struct
import zlib

# O contorno escuro · G ouro · H brilho · D ouro escuro · B lápis-lazúli · L lápis claro · . transparente
ART = [
    "O...............",
    "OO..............",
    "OHO.............",
    "OHGO............",
    "OHGBO...........",
    "OHGBLO..........",
    "OHGBLBO.........",
    "OHGBLBGO........",
    "OHGBLBGGO.......",
    "OHGBBBGGDO......",
    "OHGGGGGDDDO.....",
    "OHGOOGDOOOOO....",
    "OGO.OGDO........",
    "OO...OGDO.......",
    "O....OGDO.......",
    "......OO........",
]
PALETTE = {
    "O": (36, 18, 0, 255),       # marrom quase preto (contorno)
    "G": (255, 196, 36, 255),    # ouro
    "H": (255, 238, 146, 255),   # brilho do ouro
    "D": (182, 109, 0, 255),     # ouro na sombra
    "B": (36, 73, 219, 255),     # lápis-lazúli
    "L": (109, 146, 255, 255),   # lápis claro
    ".": (0, 0, 0, 0),
}


def write_png(path, pixels, width, height):
    raw = b"".join(b"\x00" + bytes(c for px in row for c in px) for row in pixels)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def scaled(scale):
    rows = []
    for line in ART:
        row = [PALETTE[c] for c in line for _ in range(scale)]
        rows.extend([row] * scale)
    return rows


content = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "KingsValley.Desktop", "Content")
write_png(os.path.join(content, "cursor.png"), scaled(1), 16, 16)
write_png(os.path.join(content, "cursor_hardware.png"), scaled(3), 48, 48)
print("cursor.png (16x16) e cursor_hardware.png (48x48) gerados")
