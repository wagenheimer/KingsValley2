"""Extrai o grafico das MECANISMOS tipo 1 (porta giratoria) nas 4 fases de rotacao.

O empurrao (0xBABF..0xBB42) incrementa `IX+5` (a fase, 0..3) e o desenho (0xBB43/0xBB93) usa
`2*IX+5` como offset numa lista de blocos (tipo 1: 0x9374; tipo 2: 0x937C/0x9384/0x938C por linha).
A mecanismo NAO sai da tabela de blocos da sala: e blitada por cima do mural (0x56DE devolve o mural
na area antes), entao a unica forma de obter a arte e MEDIR no dump.

A arte e um recorte de 16x24 px (2 celulas x 3 de altura) por fase: na fase 0 e uma barra vertical
(a mesma faixa nas 3 linhas), na fase 3 a barra ja girou para deitada e so ocupa a linha de cima -
por isso nao da para modelar como "um tile 16x8 repetido pela altura".

Uso: python rom_mechanism.py     (dumps em work/mech/s2phases; reproduza com work/mech/phase.tcl)
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rom_sprites import Rom  # noqa: E402
import rom_backgrounds as rb  # noqa: E402
import rom_room as rr  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.join(HERE, "..", "work")
OUT = os.path.join(HERE, "..", "..", "..", "src", "KingsValley.Desktop", "Content", "Rom", "Rooms")

# mecanismo 2 do stage 2 (E610): 2 celulas de largura x 3 de altura, x=80 y=104
X0, Y0, W, H = 80, 104, 16, 24
PHASES = 4


def screen_of(prefix):
    v = open(prefix + "_vram.bin", "rb").read()
    raw = np.frombuffer(v[0:0x6000], np.uint8).reshape(192, 128)
    a = np.zeros((192, 256), np.uint8)
    a[:, 0::2] = raw >> 4
    a[:, 1::2] = raw & 15
    return a


def room_of(prefix, rom):
    """Mural sem a mecanismo: o jogo grava PAREDE no mapa nessas celulas (63DA..6438) e o 56DE
    devolve o mural por cima antes de blitar a mecanismo - a referencia tem de zerar essas celulas."""
    mem = bytearray(open(prefix + "_mem.bin", "rb").read())
    p1 = rb.page1(prefix)
    off = 0xE900 + (mem[0xE243] - 1) * 224
    for row in range(Y0 // 8, (Y0 + H) // 8):
        for col in range(X0 // 8, (X0 + W) // 8):
            i = off + row * 8 + col // 4
            mem[i] &= ~(3 << (6 - 2 * (col % 4)))
    img, _, _ = rr.build(rom, mem, p1, mem[0xE241], mem[0xE242], mem[0xE243], with_objects=True)
    return img


def phase_overlay(prefix, rom):
    """Recorte 16x24 da mecanismo (0 = mural por baixo). Valida re-montando o ecra."""
    scr = screen_of(prefix)
    room = room_of(prefix, rom)
    a = scr[Y0:Y0 + H, X0:X0 + W]
    b = room[Y0:Y0 + H, X0:X0 + W]
    overlay = np.where(a != b, a, 0).astype(np.uint8)

    rebuilt = np.where(overlay != 0, overlay, b)
    diff = int((rebuilt != a).sum())
    if diff:
        raise SystemExit("%s: recorte nao reproduz o dump (%d px)" % (prefix, diff))
    return overlay


def main():
    rom = Rom(open(os.path.join(WORK, "rom", "kv2_msx2_scc.rom"), "rb").read())
    os.makedirs(OUT, exist_ok=True)
    dumps = os.path.join(WORK, "mech", "s2phases", "phase%d")
    for phase in range(PHASES):
        overlay = phase_overlay(dumps % phase, rom)
        out = bytearray()
        for r in range(H):
            for c in range(0, W, 2):
                out.append((int(overlay[r, c]) << 4) | int(overlay[r, c + 1]))
        with open(os.path.join(OUT, "mechanism_t1_p%d.bin" % phase), "wb") as f:
            f.write(bytes(out))
        cols = [c for c in range(W) if overlay[:, c].any()]
        rows = [r for r in range(H) if overlay[r].any()]
        print("mechanism_t1_p%d.bin: %d bytes, %d px, colunas %d..%d linhas %d..%d"
              % (phase, len(out), int((overlay != 0).sum()), cols[0], cols[-1], rows[0], rows[-1]))
    print("ok ->", OUT)


if __name__ == "__main__":
    main()
