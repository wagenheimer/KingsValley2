"""Kit de telas para remaster: `.bin` (runtime) <-> PNG (arte).

O jogo lê `.bin` (paleta de 16 cores + índices 4bpp) e monta a textura em runtime. Ninguém pinta
`.bin` à mão: o artista trabalha em **PNG** e este script converte nos dois sentidos.

Formatos
--------
`.bin` (ver KingsValley.Core.Screens.ScreenAssembler):
    32 bytes  paleta VDP (16 cores x 2 bytes: (R << 4) | B, G; 3 bits por canal)
    2 bytes   largura  (ushort little-endian)
    2 bytes   altura   (ushort little-endian)
    1 byte    flags (bit 0 = alfa na cor 0)
    1 byte    reservado
    resto     bitmap 4bpp (nibble alto = pixel par)

Comandos
--------
    # 1. exportar a arte atual para PNG (base para pintar por cima)
    python artkit.py export <pasta-de-bin> <pasta-de-png> [--scale 2]

    # 2. importar o PNG pintado de volta para .bin
    python artkit.py import <pasta-de-png> <pasta-de-bin>

    # 3. criar o pacote de remaster (aparece no GRAPHICS do system menu)
    python artkit.py pack <pasta-do-pacote> --name "Remaster" [--flavor msx2] [--base msx2]

Fluxo completo de um remaster:
    python artkit.py pack ../../src/KingsValley.Desktop/Content/ArtSets/remaster --name "Remaster"
    python artkit.py export ../../src/KingsValley.Desktop/Content/Rom/Art/Screens remaster-src --scale 2
    # ...pinta os PNG em remaster-src (Aseprite/Photoshop/GIMP)...
    python artkit.py import remaster-src ../../src/KingsValley.Desktop/Content/ArtSets/remaster/Art/Screens

Regras de cor: o MSX2 tem 16 cores; o script usa as cores exatas quando o PNG tem <= 16, e quantiza
(median cut) quando tem mais. Cada export gera também um `<tela>.gpl` (paleta do GIMP) e um
`<tela>.json` (tamanho real + ordem da paleta) — é o que faz o ida-e-volta voltar ao mesmo tamanho e
às mesmas cores, mesmo exportando em 2x/3x. Sem o `.json` (tela nova), a paleta é montada do PNG.

Ida-e-volta sem edição é **visualmente idêntica** (0 pixels diferentes nas 14 telas do jogo). Não é
byte a byte quando a paleta repete uma cor (o TMS tem preto nos índices 0 e 1): o PNG não distingue
"preto 0" de "preto 1". A imagem renderizada é a mesma.
"""
import json
import os
import struct
import sys

HEADER = 38
PALETTE_ENTRIES = 16

# Tamanhos conhecidos (usados para aceitar PNG exportado em 2x/3x e reduzir na importação).
KNOWN_SIZES = ((256, 212), (256, 192))


# ── .bin ────────────────────────────────────────────────────────────────────────────────

def read_bin(path):
    data = open(path, "rb").read()
    if len(data) < HEADER:
        raise ValueError(f"{path}: menor que o cabecalho ({len(data)} bytes)")

    width, height = struct.unpack("<HH", data[32:36])
    flags = data[36]

    palette = []
    for i in range(PALETTE_ENTRIES):
        b0, b1 = data[i * 2], data[i * 2 + 1]
        palette.append((((b0 >> 4) & 7) * 255 // 7, (b1 & 7) * 255 // 7, (b0 & 7) * 255 // 7))

    body = data[HEADER:]
    indices = bytearray(width * height)
    for n in range(len(indices)):
        byte = body[n >> 1]
        indices[n] = (byte >> 4) if (n & 1) == 0 else (byte & 15)

    return width, height, flags, palette, indices


def write_bin(path, width, height, palette, indices, flags=0):
    data = bytearray(HEADER)
    for i in range(PALETTE_ENTRIES):
        r, g, b = palette[i] if i < len(palette) else (0, 0, 0)
        r3, g3, b3 = to3(r), to3(g), to3(b)
        data[i * 2] = (r3 << 4) | b3
        data[i * 2 + 1] = g3

    data[32:36] = struct.pack("<HH", width, height)
    data[36] = flags & 1
    data[37] = 0

    body = bytearray((width * height + 1) // 2)
    for n, index in enumerate(indices):
        if n & 1:
            body[n >> 1] |= index & 15
        else:
            body[n >> 1] = (index & 15) << 4

    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, "wb").write(bytes(data) + bytes(body))


def to3(value):
    return max(0, min(7, round(value * 7 / 255)))


# ── PNG <-> .bin ───────────────────────────────────────────────────────────────────────

def export_one(src, dst_png, scale=1):
    from PIL import Image

    width, height, flags, palette, indices = read_bin(src)
    image = Image.new("RGBA", (width, height))
    image.putdata([palette[i] + ((0 if (flags & 1 and i == 0) else 255),) for i in indices])

    if scale > 1:
        image = image.resize((width * scale, height * scale), Image.NEAREST)

    image.save(dst_png)
    write_gpl(os.path.splitext(dst_png)[0] + ".gpl", os.path.basename(dst_png), palette)
    write_sidecar(os.path.splitext(dst_png)[0] + ".json", width, height, flags, palette, scale)
    return width, height


def write_sidecar(path, width, height, flags, palette, scale):
    """Guarda o tamanho real e a ordem da paleta: é o que faz o ida-e-volta ser byte a byte igual."""
    meta = {
        "width": width,
        "height": height,
        "flags": flags,
        "scale": scale,
        "palette": [list(rgb) for rgb in palette],
    }
    open(path, "w", encoding="utf-8").write(json.dumps(meta, indent=2) + "\n")


def read_sidecar(png_path):
    path = os.path.splitext(png_path)[0] + ".json"
    if not os.path.exists(path):
        return None
    try:
        return json.load(open(path, encoding="utf-8"))
    except (OSError, ValueError):
        return None


def write_gpl(path, name, palette):
    lines = ["GIMP Palette", f"Name: {os.path.splitext(name)[0]}", "Columns: 16", "#"]
    for r, g, b in palette:
        lines.append(f"{r:3d} {g:3d} {b:3d}\t-")
    open(path, "w", encoding="utf-8").write("\n".join(lines) + "\n")


def import_one(src_png, dst_bin, target=None):
    from PIL import Image

    meta = read_sidecar(src_png)
    image = Image.open(src_png)
    has_alpha = image.mode in ("RGBA", "LA", "P")
    total_alpha = alpha_used(src_png)
    image = image.convert("RGB")

    # Tamanho: o sidecar manda (tela 80x32 exportada em 2x continua 80x32); sem ele, deduz pelo
    # tamanho conhecido (256x212 / 256x192) e aceita PNG em 2x/3x.
    if meta:
        target = target or (int(meta["width"]), int(meta["height"]))
        flags = int(meta.get("flags", 0)) if not total_alpha else 1
    else:
        target = target or detect_target(image.size)
        flags = 1 if has_alpha and total_alpha else 0

    if image.size != target:
        # Volta por vizinho mais próximo (nada de borrar pixel art).
        image = image.resize(target, Image.NEAREST)

    # Paleta do sidecar (ordem original) ou uma nova, quantizada.
    fixed = [tuple(rgb) for rgb in meta["palette"]] if meta and meta.get("palette") else None
    palette, indices = build_palette(image, fixed)

    write_bin(dst_bin, image.width, image.height, palette, indices, flags)
    return image.width, image.height, len(palette)


def detect_target(size):
    width, height = size
    for known in KNOWN_SIZES:
        if width % known[0] == 0 and height % known[1] == 0:
            return known
    return size


def alpha_used(path):
    from PIL import Image

    image = Image.open(path)
    if image.mode != "RGBA":
        return False
    return image.getchannel("A").getextrema()[0] < 255


def build_palette(image, fixed=None):
    """Devolve (paleta de 16, índices).

    Com ``fixed`` (paleta do sidecar) mantém a ordem e encaixa cada pixel na cor mais próxima — é o
    que preserva o ida-e-volta e evita a tela "mudar de cor" depois de um retoque. Sem ela, usa as
    cores exatas quando o PNG tem <= 16 e quantiza (median cut) acima disso.
    """
    from PIL import Image

    raw = image.tobytes()
    pixels = [(raw[i], raw[i + 1], raw[i + 2]) for i in range(0, len(raw), 3)]

    if fixed:
        palette = fixed
        index_of = {rgb: i for i, rgb in enumerate(palette)}

        indices = bytearray(len(pixels))
        for n, pixel in enumerate(pixels):
            index = index_of.get(pixel)
            if index is None:
                # Cor nova (o artista pintou): encaixa na entrada mais próxima da paleta original.
                index = min(
                    range(len(palette)),
                    key=lambda i: sum((palette[i][c] - pixel[c]) ** 2 for c in range(3)),
                )
                index_of[pixel] = index
            indices[n] = index

        return palette, indices

    colors = image.getcolors(maxcolors=image.width * image.height)
    if colors is not None and len(colors) <= PALETTE_ENTRIES:
        # Caminho exato: cada cor vira uma entrada da paleta.
        ordered = sorted(colors, key=lambda c: (-c[0], c[1]))
        palette = [rgb for _, rgb in ordered]
        index_of = {rgb: i for i, (_, rgb) in enumerate(ordered)}
        return palette, bytearray(index_of[pixel] for pixel in pixels)

    # Mais de 16 cores: quantiza e depois arredonda para os 3 bits por canal do VDP.
    quantized = image.quantize(colors=PALETTE_ENTRIES, method=Image.Quantize.MEDIANCUT)
    raw_palette = quantized.getpalette()[: PALETTE_ENTRIES * 3]
    palette = [tuple(raw_palette[i * 3:i * 3 + 3]) for i in range(PALETTE_ENTRIES)]

    # Duas cores podem cair na mesma entrada de 3 bits: o índice é remapeado.
    snapped = {}
    remap = {}
    for i, rgb in enumerate(palette):
        key = (to3(rgb[0]), to3(rgb[1]), to3(rgb[2]))
        if key not in snapped:
            snapped[key] = len(snapped)
        remap[i] = snapped[key]

    final = [(r * 255 // 7, g * 255 // 7, b * 255 // 7) for r, g, b in snapped]
    values = list(quantized.getdata())
    return final, bytearray(remap[value] for value in values)


# ── pacote de remaster ─────────────────────────────────────────────────────────────────

def create_pack(directory, name, flavor, base):
    os.makedirs(os.path.join(directory, "Art", "Screens"), exist_ok=True)
    manifest = {"name": name, "flavor": flavor, "base": base}
    path = os.path.join(directory, "artset.json")
    open(path, "w", encoding="utf-8").write(json.dumps(manifest, indent=2) + "\n")
    print(f"pacote: {path}")
    print("  Art/Screens/  -> telas importadas")
    print("  Art/kv2.atlas -> se for refazer os sprites (opcional)")


# ── CLI ────────────────────────────────────────────────────────────────────────────────

def arg_value(flag, default=None):
    if flag in sys.argv:
        return sys.argv[sys.argv.index(flag) + 1]
    return default


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0

    command = sys.argv[1]

    if command == "export":
        source, target = sys.argv[2], sys.argv[3]
        scale = int(arg_value("--scale", "1"))
        os.makedirs(target, exist_ok=True)
        files = [f for f in sorted(os.listdir(source)) if f.lower().endswith(".bin")]
        for name in files:
            src = os.path.join(source, name)
            dst = os.path.join(target, os.path.splitext(name)[0] + ".png")
            width, height = export_one(src, dst, scale)
            print(f"{src} -> {dst} ({width}x{height} x{scale})")
        return 0

    if command == "import":
        source, target = sys.argv[2], sys.argv[3]
        size = arg_value("--target")
        target_size = tuple(int(v) for v in size.lower().split("x")) if size else None
        os.makedirs(target, exist_ok=True)
        files = [f for f in sorted(os.listdir(source)) if f.lower().endswith(".png")]
        for name in files:
            src = os.path.join(source, name)
            dst = os.path.join(target, os.path.splitext(name)[0] + ".bin")
            width, height, colors = import_one(src, dst, target_size)
            print(f"{src} -> {dst} ({width}x{height}, {colors} cores)")
        return 0

    if command == "pack":
        create_pack(
            sys.argv[2],
            arg_value("--name", "Remaster"),
            arg_value("--flavor", "msx2"),
            arg_value("--base", "msx2"),
        )
        return 0

    print(f"comando desconhecido: {command}")
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
