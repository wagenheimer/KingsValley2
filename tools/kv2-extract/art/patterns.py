"""Folhas de padrões a partir de um dump de VRAM do MSX1 (sprite + cenário).

O SCREEN 2 monta a imagem de tiles de 8x8:
    0x1800  padrões de SPRITE (1 bit por pixel; a cor vem do sprite attribute table)
    0x2000  padrões do CENÁRIO
    0x0000  tabela de cores (1 byte por linha de 8 px, por bloco de 8 padrões)

Este script lê um dump (VRAM + registradores) e escreve, para conferência:
    spg.png        os 256 padrões de sprite, um a um
    sprites16.png  os sprites 16x16 montados (4 padrões: p, p+1 / p+2, p+3)
    tiles.png      os 256 tiles de cenário com as cores da tabela de cores
    sprites.json   índice dos 16x16 (padrão base, posição na folha, pixels acesos)

Uso:
    python art/patterns.py <vram.bin> <regs.bin> <pasta-de-saida>
"""
import json
import os
import sys

HEADER = 38


def read_vram(path):
    return open(path, "rb").read()


def bases(regs):
    """Bases das tabelas do SCREEN 2, com as máscaras do MSX."""
    return {
        "name": (regs[2] & 0x0F) * 0x400,
        "pattern": (regs[4] & 0x3C) << 11,
        "colour": (regs[3] & 0x80) * 0x40,
        "sprite": (regs[6] & 0x07) << 11,
        "attributes": (regs[5] & 0x7F) << 7,
    }


def palette(colour_byte):
    """(fg, bg) de um byte da tabela de cores."""
    return (colour_byte >> 4) & 0x0F, colour_byte & 0x0F


def write_sheets(vram, regs, out_dir, scale=3):
    from PIL import Image, ImageDraw

    b = bases(regs)
    os.makedirs(out_dir, exist_ok=True)
    written = []

    def tile_sheet(data, colour_of, name, count=256):
        cols = 16
        cell = 8 * scale + 6
        rows = (count + cols - 1) // cols
        image = Image.new("RGB", (cols * cell + 6, rows * (cell + 14) + 6), (18, 20, 28))
        draw = ImageDraw.Draw(image)
        for t in range(count):
            x = (t % cols) * cell + 3
            y = (t // cols) * (cell + 14) + 3
            for row in range(8):
                bits = data[(t * 8 + row) % len(data)]
                for bit in range(8):
                    if (bits >> (7 - bit)) & 1:
                        draw.rectangle(
                            [x + bit * scale, y + 10 + row * scale,
                             x + bit * scale + scale - 1, y + 10 + row * scale + scale - 1],
                            fill=colour_of(t, row))
            draw.text((x, y), str(t), fill=(150, 156, 175))
        path = os.path.join(out_dir, name)
        image.save(path)
        written.append(path)
        return image

    # Padrões de sprite (1 bit por pixel): desenhados em branco para ver a forma.
    spg = vram[b["sprite"]:b["sprite"] + 0x800]
    tile_sheet(spg, lambda t, row: (255, 255, 255), "spg.png")

    # Padrões de cenário com a cor do bloco a que pertencem.
    colours = vram[b["colour"]:b["colour"] + 0x300]
    pat = vram[b["pattern"]:b["pattern"] + 0x800]
    tile_sheet(pat, lambda t, row: _fg(colours, t, row), "tiles.png")

    # Sprites 16x16 (4 padrões: p, p+1 na linha de cima; p+2, p+3 na de baixo).
    groups = []
    for base in range(0, 256, 4):
        if any(spg[(base + q) * 8 + row] for q in range(4) for row in range(8)):
            groups.append(base)

    cell = 16 * scale + 10
    cols = 12
    rows = (len(groups) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell + 8, rows * (cell + 16) + 8), (18, 20, 28))
    draw = ImageDraw.Draw(sheet)
    index = []

    for i, base in enumerate(groups):
        x = (i % cols) * cell + 4
        y = (i // cols) * (cell + 16) + 4
        lit = 0

        for q in range(4):
            tile = base + q
            for row in range(8):
                bits = spg[tile * 8 + row]
                for bit in range(8):
                    if (bits >> (7 - bit)) & 1:
                        px = x + (q % 2) * 8 * scale + bit * scale
                        py = y + 10 + (q // 2) * 8 * scale + row * scale
                        draw.rectangle([px, py, px + scale - 1, py + scale - 1], fill=(255, 255, 255))
                        lit += 1

        draw.rectangle([x - 1, y + 9, x + 16 * scale, y + 9 + 16 * scale], outline=(48, 52, 68))
        draw.text((x, y), f"p{base}", fill=(190, 196, 215))
        index.append({"base": base, "column": i % cols, "row": i // cols, "lit": lit})

    path = os.path.join(out_dir, "sprites16.png")
    sheet.save(path)
    written.append(path)

    data_path = os.path.join(out_dir, "sprites.json")
    json.dump(index, open(data_path, "w", encoding="utf-8"), indent=2)
    written.append(data_path)

    return written, len(groups)


def _fg(colours, tile, row):
    table = [(0, 0, 0), (0, 0, 0), (33, 200, 66), (94, 220, 120),
             (84, 85, 237), (125, 118, 252), (212, 82, 77), (66, 235, 245),
             (252, 85, 84), (255, 121, 120), (212, 193, 84), (230, 206, 128),
             (33, 176, 59), (201, 91, 186), (204, 204, 204), (255, 255, 255)]
    index = (tile * 8 + row) % max(1, len(colours))
    fg, _ = palette(colours[index])
    return table[fg]


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        return 1

    vram = read_vram(sys.argv[1])
    regs = open(sys.argv[2], "rb").read()
    out_dir = sys.argv[3]

    written, groups = write_sheets(vram, regs, out_dir)
    for path in written:
        print(f"  {path}")
    print(f"{groups} sprites 16x16")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
