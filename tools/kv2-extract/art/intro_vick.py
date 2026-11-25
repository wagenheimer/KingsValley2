"""Gera o sprite do Vick da abertura (5 poses 16x32) com as cores do ROM.

Uso (de dentro de tools/kv2-extract/work):
    python ../art/intro_vick.py ../../../src/KingsValley.Desktop/Content/Rom/Art

Entrada: o atlas kv2 (`kv2.png` + `kv2.atlas`), que ja e' a arte do ROM extraida.
Por que recolorir: a arte do atlas pinta o corpo do Vick com **ciano** (36,255,255), mas no
longplay da abertura ele e' **azul** — no ROM o sprite usa as cores 13/14 da paleta do VDP
(13 = (1,1,7) = (36,36,255); 14 = (7,6,5) = (255,218,182) = pele). Aqui o ciano vira o azul
da paleta 13 e a pele continua igual; o contorno preto fica preto.

Saida: <art>/Screens/intro_vick.bin — 80x32 (5 poses de 16x32), 4bpp com alfa:
    0 = transparente, 1 = azul (corpo), 2 = pele, 3 = preto (contorno)
"""
import os
import struct
import sys
import zlib

POSES = ("vick_s0_03", "vick_s0_04", "vick_s0_05", "vick_d_00", "vick_d_01")
FRAME_W, FRAME_H = 16, 32

BLUE = (1, 1, 7)          # paleta 13 do VDP (0..7 por canal)
SKIN = (7, 6, 5)          # paleta 14
BLACK = (0, 0, 0)


def read_png(path):
    d = open(path, 'rb').read()
    pos = 8
    w = h = 0
    depth = ctype = 0
    idat = b''
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos + 4])[0]
        typ = d[pos + 4:pos + 8]
        data = d[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if typ == b'IHDR':
            w, h, depth, ctype = struct.unpack('>IIBB', data[:10])
        elif typ == b'IDAT':
            idat += data
        elif typ == b'IEND':
            break
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    bpp = max(1, ch * depth // 8)
    stride = (w * ch * depth + 7) // 8
    rows = []
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1:
                line[i] = (line[i] + a) & 0xFF
            elif f == 2:
                line[i] = (line[i] + b) & 0xFF
            elif f == 3:
                line[i] = (line[i] + (a + b) // 2) & 0xFF
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        prev = line
        rows.append(bytes(line))
    return w, h, ch, rows


def read_atlas(path):
    rects = {}
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    for i, line in enumerate(lines):
        name = line.strip()
        if name in POSES and i + 1 < len(lines):
            x, y, w, h = (int(v) for v in lines[i + 1].strip().split(","))
            rects[name] = (x, y, w, h)
    return rects


def classify(r, g, b, a):
    if a < 40:
        return 0
    if r + g + b < 200:
        return 3            # contorno preto
    if b > r + 40 and g > r + 40 and g > 150:
        return 1            # ciano -> azul do ROM
    return 2                # pele / restante


def main():
    art = sys.argv[1] if len(sys.argv) > 1 else "."
    atlas_png = os.path.join(art, "kv2.png")
    atlas_txt = os.path.join(art, "kv2.atlas")
    _, _, ch, rows = read_png(atlas_png)
    rects = read_atlas(atlas_txt)
    for p in POSES:
        if p not in rects:
            raise SystemExit("pose ausente no atlas: " + p)

    width = FRAME_W * len(POSES)
    header = bytearray(32)
    for i, (r, g, b) in enumerate(((0, 0, 0), BLUE, SKIN, BLACK)):
        header[i * 2], header[i * 2 + 1] = ((r << 4) | b), g
    header += struct.pack("<HH", width, FRAME_H)
    header.append(1)        # HasAlpha: cor 0 transparente
    header.append(0)

    body = bytearray((width * FRAME_H) // 2)
    for n, pose in enumerate(POSES):
        x0, y0, _, _ = rects[pose]
        for y in range(FRAME_H):
            line = rows[y0 + y]
            for x in range(FRAME_W):
                o = (x0 + x) * ch
                idx = classify(line[o], line[o + 1], line[o + 2], line[o + 3] if ch == 4 else 255)
                if idx == 0:
                    continue
                pos = y * width + n * FRAME_W + x
                body[pos >> 1] |= idx << (4 if (pos & 1) == 0 else 0)

    out = os.path.join(art, "Screens", "intro_vick.bin")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    open(out, "wb").write(bytes(header) + bytes(body))
    print("ok:", out)


if __name__ == "__main__":
    main()
