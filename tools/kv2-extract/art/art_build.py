"""Monta Content/Rom/Art: atlas (kv2.png + kv2.atlas, formato do Nez) + fundos por sala.

Entrada: art2/ (tiles, objetos, fundos), spr/*_merged.pkl (sprites), out_levels (posicoes).
Uso: python art_build.py <pasta_Art>
"""
import json
import os
import shutil
import sys
import numpy as np
import cv2

out = sys.argv[1]
os.makedirs(os.path.join(out, "Backgrounds"), exist_ok=True)
sprites = {}  # nome -> BGRA


def bgra(img_bgr, alpha=None):
    a = np.full(img_bgr.shape[:2], 255, np.uint8) if alpha is None else alpha
    return np.dstack([img_bgr, a])


# ── tiles de terreno (parede = 3, cavavel = 2), 4 variantes por (col%2, lin%2) ──
for p in range(1, 7):
    t = cv2.imread(f"art2/tiles_p{p}.png")
    for v in range(4):
        cx, cy = v % 2, v // 2
        sprites[f"wall_p{p}_{v}"] = bgra(t[cy * 8:cy * 8 + 8, cx * 8:cx * 8 + 8])
        sprites[f"soft_p{p}_{v}"] = bgra(t[cy * 8:cy * 8 + 8, 16 + cx * 8:16 + cx * 8 + 8])

# ── objetos: transparencia = diferenca contra o fundo da sala onde foram recortados ──
CoffinRise = 8
ITEM = {1: "knife", 2: "boomerang", 3: "shovel", 4: "pickaxe", 5: "hammer", 6: "drill"}
bg_cache = {}


def bg_of(s, r):
    k = (s, r)
    if k not in bg_cache:
        bg_cache[k] = cv2.imread(f"art2/bg/s{s:02d}_r{r}.png")
    return bg_cache[k]


def masked_mode(samples):
    """samples: lista de (crop, mask). Moda por pixel so entre amostras opacas."""
    h, w = samples[0][0].shape[:2]
    img = np.zeros((h, w, 4), np.uint8)
    for y in range(h):
        for x in range(w):
            votes = {}
            opaque = 0
            for c, m in samples:
                if m[y, x]:
                    opaque += 1
                    votes[tuple(c[y, x])] = votes.get(tuple(c[y, x]), 0) + 1
            if votes and opaque * 2 >= len(samples):
                img[y, x, :3] = max(votes, key=votes.get)
                img[y, x, 3] = 255
    return img


z = np.load("vframes.npz")
F = z["frames"]
match = json.load(open("vmatch.json"))
mode_cache = {}


def room_mode(s, r):
    k = (s, r)
    if k not in mode_cache:
        good = match[f"{s}-{r}"][3][:40]
        st = F[good, :192].astype(np.int32)
        key = (st[..., 0] << 16) | (st[..., 1] << 8) | st[..., 2]
        srt = np.sort(key, axis=0)
        # moda aproximada: valor mais frequente por coluna
        flat = srt.reshape(len(st), -1)
        res = np.empty(flat.shape[1], np.int32)
        for i in range(flat.shape[1]):
            v, c = np.unique(flat[:, i], return_counts=True)
            res[i] = v[np.argmax(c)]
        res = res.reshape(192, 256)
        mode_cache[k] = np.stack([(res >> 16) & 255, (res >> 8) & 255, res & 255], -1).astype(np.uint8)
    return mode_cache[k]


samples = {}
for s in range(1, 61):
    d = json.load(open(f"out_levels/stage{s:02d}.json"))
    p = (s - 1) // 10 + 1
    objs = [(ITEM[o["type"]], o["room"], o["y"], o["x"], 16, 16, False) for o in d["items"]]
    objs += [(f"soulstone_p{p}", o["room"], o["y"], o["x"], 16, 16, True) for o in d["soulStones"]]
    e = d["exitDoor"]
    objs.append((f"door_p{p}", e["room"], e["y"], e["x"], 32, 32, True))
    # o sarcófago tem 16x24 e fica em pé no chão: começa 8 px ACIMA do y gravado na ROM
    objs += [(f"coffin_p{p}", c["room"], c["y"] - CoffinRise, c["x"], 24, 16, True) for c in d["coffins"]]
    objs += [(f"ladder_p{p}", L["room"], L["row"] * 8, L["col"] * 8, 8, 16, True)
             for L in d["ladders"] if L["length"] >= 2]
    for name, r, y, x, h, w, per_pyr in objs:
        if f"{s}-{r}" not in match or match[f"{s}-{r}"][1] < 8:
            continue
        m = room_mode(s, r)[y:y + h, x:x + w]
        b = bg_of(s, r)[y:y + h, x:x + w]
        if m.shape[:2] != (h, w):
            continue
        mask = np.abs(m.astype(int) - b.astype(int)).sum(-1) > 30
        samples.setdefault(name, []).append((m, mask))
for name, lst in samples.items():
    sprites[name] = masked_mode(lst[:60])

# ── sprites dos personagens: EXATOS, lidos da ROM MSX2 por rom_sprites.py (art2/romspr) ──
ROMSPR = "art2/romspr"
if not os.path.isdir(ROMSPR):
    sys.exit(f"falta {ROMSPR}: rode antes  python rom_sprites.py <kv2_msx2_scc.rom> {ROMSPR}")
for f in sorted(os.listdir(ROMSPR)):
    if f.endswith(".png"):
        sprites[os.path.splitext(f)[0]] = cv2.imread(os.path.join(ROMSPR, f), cv2.IMREAD_UNCHANGED)

# ── HUD (faixa + digitos da fonte do jogo, ver hud_build.py) ──
for f in sorted(os.listdir("art2/hud")):
    sprites[os.path.splitext(f)[0]] = cv2.imread(os.path.join("art2/hud", f), cv2.IMREAD_UNCHANGED)

# ── empacotamento (prateleiras, padding 2 + extrusao de 1 px) ──
names = sorted(sprites, key=lambda n: (-sprites[n].shape[0], n))
W = 512
x = y = shelf = 0
pos = {}
PAD = 2
for n in names:
    h, w = sprites[n].shape[:2]
    if x + w + PAD > W:
        x, y, shelf = 0, y + shelf + PAD, 0
    pos[n] = (x + 1, y + 1)
    x += w + PAD
    shelf = max(shelf, h)
H = 1
while H < y + shelf + PAD:
    H *= 2
sheet = np.zeros((H, W, 4), np.uint8)
for n, (px, py) in pos.items():
    img = sprites[n]
    h, w = img.shape[:2]
    sheet[py:py + h, px:px + w] = img
    # extrusao: copia a borda para o padding (alpha da propria borda)
    sheet[py - 1, px:px + w] = img[0]
    sheet[py + h, px:px + w] = img[-1]
    sheet[py - 1:py + h + 1, px - 1] = sheet[py - 1:py + h + 1, px]
    sheet[py - 1:py + h + 1, px + w] = sheet[py - 1:py + h + 1, px + w - 1]
cv2.imwrite(os.path.join(out, "kv2.png"), sheet)
with open(os.path.join(out, "kv2.atlas"), "w", newline="\n") as f:
    for n in sorted(pos):
        px, py = pos[n]
        h, w = sprites[n].shape[:2]
        f.write(f"{n}\n{px},{py},{w},{h}\n0,0\n")
    f.write("\n")
for src in os.listdir("art2/bg"):
    shutil.copy(os.path.join("art2/bg", src), os.path.join(out, "Backgrounds", src))
print(len(sprites), "sprites no atlas", f"{W}x{H}")
