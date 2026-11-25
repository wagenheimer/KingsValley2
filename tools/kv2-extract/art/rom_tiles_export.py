"""Exporta para o runtime (Content/Rom/Rooms/) os dados da montagem da sala (44D4):

- blocks_pN.bin: 256 blocos 8x8 (4bpp -> 32 bytes cada, 8192 bytes por piramide)
  lidos da pagina de padroes (VRAM 0x8000) de um dump valido da piramide.
- murals.json: codigos do mural por piramide, para os stages pares (base) e impares
  (base + 0x20), lidos da ROM (0x7FFE + 2*piramide).
- decorations.json: lista de decoracoes por stage (0x806A), com o grafico resolvido
  para codigos de bloco (0x800C), prontos a blitar.

Uso (a partir de tools/kv2-extract):
  python art/rom_tiles_export.py
"""
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rom_sprites import Rom
import rom_backgrounds as rb


def blocks_of(prefix):
    p1 = rb.page1(prefix)  # 256x256 indices (4bpp desempacotado)
    out = bytearray()
    for n in range(256):
        t = p1[(n >> 5) * 8:(n >> 5) * 8 + 8, (n & 31) * 8:(n & 31) * 8 + 8]
        for r in range(8):
            for c in range(0, 8, 2):
                out.append((int(t[r, c]) << 4) | int(t[r, c + 1]))
    return bytes(out)


def mural_codes(rom, pyramid, stage_parity):
    base = rom.word(0x7FFE + 2 * pyramid) + (0x20 if stage_parity == "odd" else 0)
    return list(rom.d[rom.off(base):rom.off(base) + 32])


def decorations_of(rom, stage, pyramid):
    lst = rom.off(rom.word(0x806A + 2 * stage))
    d = rom.d
    out = []
    while d[lst] != 0xFF:
        b0, b1 = d[lst], d[lst + 1]
        nxt = d[lst + 2]
        lst += 3
        if (b1 & 7) != 0:   # sala 1 -> (sala-1) == 0; so a sala 1 (todas as salas dos dumps)
            # mantemos todas as salas; o filtro e feito pelo consumidor
            pass
        kind, y, x, room = b0 & 7, b0 & 0xF8, b1 & 0xF8, (b1 & 7) + 1
        gfx = rom.off(rom.word(0x800C + 2 * ((pyramid - 1) * 8 + kind)))
        shape = kind >> 1
        if shape == 3:
            rows, cols, height = 1, 2, nxt
            codes = [d[gfx], d[gfx + 1], d[gfx + 2], d[gfx + 3]]
            layout = "pillar"
        elif shape == 0:
            rows, cols, height = 4, 4, 4
            codes = [d[gfx + r * 4 + c] for r in range(4) for c in range(4)]
            layout = "grid"
        elif shape == 1:
            rows, cols, height = 4, 2, 4
            codes = [d[gfx + r * 2 + c] for r in range(4) for c in range(2)]
            layout = "grid"
        else:
            rows, cols, height = 2, 4, 2
            codes = [d[gfx + r * 4 + c] for r in range(2) for c in range(4)]
            layout = "grid"
        out.append({
            "kind": kind,
            "shape": layout,
            "room": room,
            "y": y,
            "x": x,
            "extra": nxt,
            "rows": rows,
            "cols": cols,
            "height": height,
            "blocks": codes,
        })
    return out


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.join(here, "..", "..", "..")
    out_dir = os.path.join(root, "src", "KingsValley.Desktop", "Content", "Rom", "Rooms")
    os.makedirs(out_dir, exist_ok=True)

    # 1) blocos por piramide (um dump valido por piramide)
    for pyr in range(1, 7):
        dump_tag = "s%d" % (1 + (pyr - 1) * 10)
        prefix = os.path.join(here, "..", "work", "pyr", dump_tag)
        data = blocks_of(prefix)
        with open(os.path.join(out_dir, "blocks_p%d.bin" % pyr), "wb") as f:
            f.write(data)
        print("blocks_p%d.bin: %d bytes (dump %s)" % (pyr, len(data), dump_tag))

    # 2) codigos do mural por piramide (pares e impares)
    rom = Rom(open(os.path.join(here, "..", "work", "rom", "kv2_msx2_scc.rom"), "rb").read())
    murals = {}
    for pyr in range(1, 7):
        murals[str(pyr)] = {
            "even": mural_codes(rom, pyr, "even"),
            "odd": mural_codes(rom, pyr, "odd"),
        }
    with open(os.path.join(out_dir, "murals.json"), "w", encoding="utf-8") as f:
        json.dump(murals, f, indent=2)
    print("murals.json: %d piramides" % len(murals))

    # 2b) paletas + animacao da cor (5C14) por piramide.
    # Registo em 0xBD52 + 5*piramide (banco 15): [cor, limite, periodo, rampLo, rampHi].
    # A rampa e uma sequencia de words (cor MSX2: r=rb>>4, b=rb&7, g=g&7, escalado *255/7).
    rom = Rom(open(os.path.join(here, "..", "work", "rom", "kv2_msx2_scc.rom"), "rb").read())
    data = rom.d
    base = 15 * 0x2000
    palettes = {}
    animations = {}
    for pyr in range(1, 7):
        dump_tag = "s%d" % (1 + (pyr - 1) * 10)
        pal = open(os.path.join(here, "..", "work", "pyr", dump_tag + "_pal.bin"), "rb").read()
        colors = []
        for i in range(16):
            rb_ = pal[2 * i]
            gg = pal[2 * i + 1]
            r = (rb_ >> 4) & 7
            b = rb_ & 7
            g = gg & 7
            colors.append([int(r * 255 / 7), int(g * 255 / 7), int(b * 255 / 7)])
        palettes[str(pyr)] = colors

        off = base + (0xBD52 + 5 * pyr - 0xA000)
        colour, limit, period, lo, hi = data[off:off + 5]
        ramp_ptr = (hi << 8) | lo
        ramp_off = base + (ramp_ptr - 0xA000)
        # a rampa tem 20 words (40 bytes); so usamos as primeiras `limit` (indices 0..limit-1)
        ramp = []
        for k in range(limit):
            w = data[ramp_off + 2 * k] | (data[ramp_off + 2 * k + 1] << 8)
            rr_ = (w >> 4) & 7
            bb = w & 7
            gg = (w >> 8) & 7
            ramp.append([int(rr_ * 255 / 7), int(gg * 255 / 7), int(bb * 255 / 7)])

        slots = [{"colour": colour, "limit": limit, "period": period, "ramp": ramp}]
        # piramide 4 tem um 2o slot (5C3E..5C4E): cor 6, limite 2, periodo 5, rampa em 0xBDaf
        if pyr == 4:
            extra_ramp_ptr = 0xBDaf
            extra_off = base + (extra_ramp_ptr - 0xA000)
            extra_ramp = []
            for k in range(2):
                w = data[extra_off + 2 * k] | (data[extra_off + 2 * k + 1] << 8)
                rr_ = (w >> 4) & 7
                bb = w & 7
                gg = (w >> 8) & 7
                extra_ramp.append([int(rr_ * 255 / 7), int(gg * 255 / 7), int(bb * 255 / 7)])
            slots.append({"colour": 6, "limit": 2, "period": 5, "ramp": extra_ramp})

        animations[str(pyr)] = slots

    with open(os.path.join(out_dir, "palettes.json"), "w", encoding="utf-8") as f:
        json.dump(palettes, f, indent=2)
    with open(os.path.join(out_dir, "animations.json"), "w", encoding="utf-8") as f:
        json.dump(animations, f, indent=2)
    print("palettes.json: %d piramides" % len(palettes))
    print("animations.json: %d piramides" % len(animations))

    # 3) decoracoes por stage
    decorations = {}
    for stage in range(1, 61):
        pyramid = 1 + (stage - 1) // 10
        decorations[str(stage)] = decorations_of(rom, stage, pyramid)
    with open(os.path.join(out_dir, "decorations.json"), "w", encoding="utf-8") as f:
        json.dump(decorations, f, indent=2)
    total = sum(len(v) for v in decorations.values())
    print("decorations.json: %d stages, %d decoracoes no total" % (len(decorations), total))


if __name__ == "__main__":
    main()
