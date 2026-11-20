"""Gera uma galeria HTML do conteúdo extraído da ROM, para **olhar** o resultado.

O jogo usa `.bin`/JSON (runtime), mas ninguém confere nada assim. Este script monta:

    <saida>/screens/*.png     telas fixas extraídas (título JP/EN, pirâmide...)
    <saida>/stages/stageNN.png cada fase com as salas, itens, pedras e porta
    <saida>/index.html        galeria (abre no navegador)

Uso:
    python art/gallery.py "%APPDATA%/KingsValley/extracted"
    python art/gallery.py "%APPDATA%/KingsValley/extracted" preview --open

Os PNGs são só para conferência — o jogo continua lendo `.bin` (nada de PNG em runtime).
"""
import html
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from artkit import read_bin  # noqa: E402

# Cores do mapa de fase (o JSON é abstrato: parede / cavável / escada / vazio).
CELL_COLORS = {
    "#": (206, 206, 216),   # parede
    "?": (146, 112, 78),    # cavável
    "=": (86, 200, 126),    # escada
    ".": (24, 26, 38),      # vazio
    " ": (24, 26, 38),
}

MARKERS = {
    "stones": (86, 156, 255),    # pedras da alma
    "coffins": (232, 92, 220),   # sarcófagos
    "items": (255, 255, 255),    # itens
    "mechanisms": (86, 232, 232),
}


def arg_value(flag, default=None):
    """Valor de um argumento `--flag valor` (ou o padrão)."""
    if flag in sys.argv:
        return sys.argv[sys.argv.index(flag) + 1]
    return default

CELL = 3
ROOM_W, ROOM_H = 32 * CELL, 24 * CELL
PAD = 5


def screen_png(src_bin, dst_png, scale=2):
    from PIL import Image

    width, height, flags, palette, indices = read_bin(src_bin)
    image = Image.new("RGBA", (width, height))
    image.putdata([palette[i] + ((0 if (flags & 1 and i == 0) else 255),) for i in indices])
    if scale > 1:
        image = image.resize((width * scale, height * scale), Image.NEAREST)
    image.save(dst_png)
    return width, height


def stage_png(stage_path, dst_png):
    from PIL import Image, ImageDraw

    stage = json.load(open(stage_path, encoding="utf-8"))
    rooms = stage.get("rooms", [])
    if not rooms:
        return None

    cols = min(4, len(rooms))
    rows = (len(rooms) + cols - 1) // cols
    image = Image.new("RGB", (cols * ROOM_W + (cols + 1) * PAD,
                              rows * ROOM_H + (rows + 1) * PAD), (10, 11, 18))
    draw = ImageDraw.Draw(image)

    def origin(room_number):
        index = max(0, min(len(rooms) - 1, int(room_number) - 1))
        r, c = divmod(index, cols)
        return PAD + c * (ROOM_W + PAD), PAD + r * (ROOM_H + PAD)

    for i, room in enumerate(rooms):
        r, c = divmod(i, cols)
        ox = PAD + c * (ROOM_W + PAD)
        oy = PAD + r * (ROOM_H + PAD)

        for y, line in enumerate(room.get("rows", [])):
            for x, char in enumerate(line):
                draw.rectangle(
                    [ox + x * CELL, oy + y * CELL, ox + x * CELL + CELL - 1, oy + y * CELL + CELL - 1],
                    fill=CELL_COLORS.get(char, (255, 0, 255)))

        draw.text((ox + 3, oy + 2), f"R{i + 1}", fill=(255, 255, 120))

    def mark(item, color, size=1):
        ox, oy = origin(item.get("room", 1))
        x = ox + int(item.get("x", 0)) // 8 * CELL
        y = oy + int(item.get("y", 0)) // 8 * CELL
        draw.rectangle([x, y, x + CELL * size - 1, y + CELL * size - 1], outline=color, width=2)

    for key, color in MARKERS.items():
        for item in stage.get(key, []) or []:
            mark(item, color)

    if stage.get("exitDoor"):
        door = stage["exitDoor"]
        mark(door, (255, 214, 64), size=max(1, int(door.get("w", 4)) // 4))

    if stage.get("playerStart"):
        mark(stage["playerStart"], (255, 148, 40), size=2)

    image.save(dst_png)
    return image.size


def build_gallery(source, target):
    root = os.path.abspath(target)
    screens_dir = os.path.join(root, "screens")
    stages_dir = os.path.join(root, "stages")
    os.makedirs(screens_dir, exist_ok=True)
    os.makedirs(stages_dir, exist_ok=True)

    screens = []
    src_screens = os.path.join(source, "Screens")
    if os.path.isdir(src_screens):
        for name in sorted(os.listdir(src_screens)):
            if not name.lower().endswith(".bin"):
                continue
            stem = os.path.splitext(name)[0]
            out = os.path.join(screens_dir, stem + ".png")
            size = screen_png(os.path.join(src_screens, name), out)
            screens.append((stem, out, size))

    # Opcional: uma segunda pasta de telas para comparar (ex.: a arte MSX2 do port).
    compare = []
    compare_source = arg_value("--compare")
    if compare_source and os.path.isdir(compare_source):
        compare_dir = os.path.join(root, "compare")
        os.makedirs(compare_dir, exist_ok=True)
        for name in sorted(os.listdir(compare_source)):
            if not name.lower().endswith(".bin"):
                continue
            stem = os.path.splitext(name)[0]
            out = os.path.join(compare_dir, stem + ".png")
            size = screen_png(os.path.join(compare_source, name), out)
            compare.append((stem, out, size))

    # Folhas de padrões (sprites, tiles, fonte) geradas por art/patterns.py.
    patterns = []
    patterns_dir = os.path.join(root, "patterns")
    if os.path.isdir(patterns_dir):
        for name in sorted(os.listdir(patterns_dir)):
            if not name.lower().endswith(".png"):
                continue
            patterns.append((os.path.splitext(name)[0], os.path.join(patterns_dir, name), (0, 0)))

    stages = []
    src_levels = os.path.join(source, "Levels")
    if os.path.isdir(src_levels):
        for name in sorted(os.listdir(src_levels)):
            if not name.lower().endswith(".json"):
                continue
            stem = os.path.splitext(name)[0]
            out = os.path.join(stages_dir, stem + ".png")
            size = stage_png(os.path.join(src_levels, name), out)
            if size:
                stages.append((stem, out, size))

    stamp = {}
    stamp_path = os.path.join(source, "audio.source.json")
    if os.path.exists(stamp_path):
        stamp = json.load(open(stamp_path, encoding="utf-8"))

    audio_text = ""
    audio_path = os.path.join(root, "audio.txt")
    if os.path.exists(audio_path):
        audio_text = html.escape(open(audio_path, encoding="utf-8", errors="replace").read())

    def cards(items, folder):
        parts = []
        for name, path, size in items:
            rel = os.path.relpath(path, root).replace("\\", "/")
            parts.append(
                f'<a class="card" href="{rel}" target="_blank">'
                f'<img src="{rel}" alt="{html.escape(name)}">'
                f'<span>{html.escape(name)} <em>{size[0]}x{size[1]}</em></span></a>')
        return "\n".join(parts)

    page = f"""<!doctype html>
<html lang="pt-br"><head><meta charset="utf-8">
<title>King's Valley II — conteúdo extraído da ROM</title>
<style>
  :root {{ color-scheme: dark; }}
  body {{ margin:0; background:#0b0c12; color:#e8e8f0;
         font:15px/1.5 system-ui,Segoe UI,Roboto,sans-serif; }}
  header {{ padding:24px 28px; background:linear-gradient(180deg,#151a2b,#0b0c12);
            border-bottom:1px solid #262b3d; }}
  h1 {{ margin:0 0 6px; font-size:22px; }}
  h2 {{ margin:32px 28px 12px; font-size:17px; color:#9fb4ff; }}
  .meta {{ color:#98a0b8; font-size:13px; }}
  .meta code {{ background:#171b28; padding:1px 6px; border-radius:4px; }}
  .grid {{ display:flex; flex-wrap:wrap; gap:14px; padding:0 28px 28px; }}
  .card {{ display:block; text-decoration:none; color:inherit; background:#131725;
           border:1px solid #232941; border-radius:10px; padding:8px;
           transition:transform .12s, border-color .12s; }}
  .card:hover {{ transform:translateY(-2px); border-color:#4a5c99; }}
  .card img {{ display:block; image-rendering:pixelated; max-width:100%; border-radius:6px; }}
  .card span {{ display:block; margin-top:6px; font-size:12px; color:#aab2cc; }}
  .card em {{ color:#6f7a99; font-style:normal; }}
  pre {{ margin:0 28px 28px; padding:14px; background:#131725; border:1px solid #232941;
         border-radius:10px; font-size:12px; color:#a9b6d6; overflow:auto; }}
  .shots {{ display:flex; gap:14px; flex-wrap:wrap; padding:0 28px 28px; }}
  footer {{ padding:18px 28px 40px; color:#5f6880; font-size:12px; }}
</style></head><body>
<header>
  <h1>King's Valley II — conteúdo extraído da ROM</h1>
  <div class="meta">
    ROM: <code>{html.escape(str(stamp.get("rom", "?")))}</code>
    · sha1 <code>{html.escape(str(stamp.get("sha1", "?"))[:12])}</code>
    · {stamp.get("songs", "?")} músicas · {stamp.get("clips", "?")} efeitos
    · {len(stages)} fases · {len(screens)} telas
  </div>
</header>

<h2>Telas fixas ({len(screens)})</h2>
<div class="grid">
{cards(screens, "screens")}
</div>
"""

    if compare:
        page += f"""
<h2>Comparação: arte MSX2 do port ({len(compare)})</h2>
<p class="meta" style="padding:0 28px 8px">Esta arte <b>não</b> vem da ROM MSX1 — é a do MSX2
(longplay). O SCREEN 2 do MSX1 tem 2 cores por linha de 8 px por tile; o SCREEN 5 do MSX2 tem 16.</p>
<div class="grid">
{cards(compare, "compare")}
</div>
"""

    if patterns:
        page += f"""
<h2>Folhas de padrões ({len(patterns)})</h2>
<p class="meta" style="padding:0 28px 8px">Direto da VRAM do jogo: sprites 8x8 e 16x16 montados,
tiles de cenário e a <b>fonte</b> do MSX1 (A–Z, 0–9 nos padrões 192+ e 224+).</p>
<div class="grid">
{cards(patterns, "patterns")}
</div>
"""

    page += f"""
<h2>Fases ({len(stages)})</h2>
<div class="grid">
{cards(stages, "stages")}
</div>
"""

    if audio_text:
        page += f'<h2>Áudio do pacote</h2>\n<pre>{audio_text}</pre>\n'

    page += """<footer>
PNGs gerados para conferência; o jogo lê <code>.bin</code>/JSON (nada de PNG em runtime).
Clique numa imagem para abrir em tamanho real.
</footer></body></html>"""

    index = os.path.join(root, "index.html")
    open(index, "w", encoding="utf-8").write(page)
    return index, len(screens), len(stages)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    source = sys.argv[1]
    target = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith("--") else None
    target = target or os.path.join(source, "preview")

    index, screens, stages = build_gallery(source, target)
    print(f"galeria: {index}")
    print(f"  {screens} telas, {stages} fases")

    if "--open" in sys.argv:
        os.startfile(index)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
