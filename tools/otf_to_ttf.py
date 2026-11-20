# Converte as fontes CJK do Unity (OpenType/CFF, .otf) para TrueType (.ttf).
#
# Por que: o Gum só reconhece uma fonte-arquivo quando o caminho termina em ".ttf"
# (`BmfcSave.IsFontFilePath`), e esse é o único caminho em que o KernSmith registra um modelo
# "cresível" — necessário para o crescimento automático de glifos (kana/CJK adicionados sob
# demanda). O FreeType do KernSmith rasteriza OTF e TTF, mas o Gum recusa ".otf" como arquivo.
#
# O que faz, para cada fonte (as 3 CJK: ja/ko/zh):
#   1. Converte os contornos CFF para `glyf` (quadráticos) com o cu2qu do fontTools.
#   2. Regrava o arquivo como TrueType (sfntVersion 0x00010000) com a extensão .ttf.
#
# As fontes de origem ficam no projeto Unity (`Assets/_Game/Art/fonts/...`). O resultado é
# versionado em `Content/Gum/Fonts/` (é o asset de runtime — não rode isto a cada build).
#
# Requisitos: Python 3 com `fonttools` (`pip install fonttools`).
#
# Uso:
#   python tools/otf_to_ttf.py

import sys
import time

try:
    from fontTools.ttLib import TTFont
    from fontTools.pens.cu2quPen import Cu2QuPen
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from fontTools.ttLib.tables._g_l_y_f import table__g_l_y_f
    from fontTools.ttLib.tables._l_o_c_a import table__l_o_c_a
except ImportError:
    sys.exit("fonttools nao encontrado. Rode: pip install fonttools")


def otf_to_ttf(src, dst, max_err=1.0):
    """Converte um OTF/CFF em TTF/TrueType (contornos quadraticos)."""
    font = TTFont(src)

    # Ja e TrueType: so regrava (idempotente).
    if "glyf" in font:
        font.save(dst)
        return

    glyph_order = font.getGlyphOrder()
    glyph_set = font.getGlyphSet()

    glyf = {}
    for name in glyph_order:
        pen = TTGlyphPen(None)
        glyph_set[name].draw(Cu2QuPen(pen, max_err, reverse_direction=True))
        glyf[name] = pen.glyph()

    glyf_table = table__g_l_y_f()
    glyf_table.glyphs = glyf
    glyf_table.glyphOrder = glyph_order
    font["glyf"] = glyf_table
    font["loca"] = table__l_o_c_a()
    font["maxp"].numGlyphs = len(glyph_order)

    for tag in ("CFF ", "CFF2", "VORG"):
        if tag in font:
            del font[tag]

    font.sfntVersion = "\x00\x01\x00\x00"
    font.save(dst)


# (origem, destino) — origens no projeto Unity; destinos no Content do port.
FONTS = [
    (
        r"E:\GreenSauceGames\HopesFarm2\Assets\_Game\Art\fonts\base\JP e KR\KozGoPro-Bold.otf",
        r"src\HopesFarm2.Desktop\Content\Gum\Fonts\KozGoPro-Bold.ttf",
    ),
    (
        r"E:\GreenSauceGames\HopesFarm2\Assets\_Game\Art\fonts\base\NotoSansKR-Regular.otf",
        r"src\HopesFarm2.Desktop\Content\Gum\Fonts\NotoSansKR-Regular.ttf",
    ),
    (
        r"E:\GreenSauceGames\HopesFarm2\Assets\_Game\Art\fonts\base\Chinese\SourceHanSansSC-Medium.otf",
        r"src\HopesFarm2.Desktop\Content\Gum\Fonts\SourceHanSansSC-Medium.ttf",
    ),
]

if __name__ == "__main__":
    import os

    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    for src, dst_rel in FONTS:
        dst = os.path.join(repo_root, dst_rel)
        if not os.path.exists(src):
            print(f"AVISO: origem nao encontrada, pulando: {src}")
            continue
        started = time.time()
        otf_to_ttf(src, dst)
        size_mb = os.path.getsize(dst) / (1024 * 1024)
        print(f"OK {os.path.basename(dst)} ({size_mb:.2f} MB, {time.time() - started:.1f}s)")
