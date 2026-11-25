"""Arte MSX2 por sala (fundo) + objetos estaticos, a partir dos frames casados (vmatch.json).

Saida em art2/:
  bg/sNN_rR.png       fundo 256x192 da sala (buracos preenchidos pelo periodo do mural)
  obj/*.png           objetos por piramide (porta, soul stone, sarcofago, itens, escadas)
  tiles_pP.png        parede/cavavel (2x2 variantes 8x8)
"""
import json
import os
import numpy as np
import cv2

z = np.load("vframes.npz")
F = z["frames"]
match = json.load(open("vmatch.json"))
for d in ("art2", "art2/bg", "art2/obj"):
    os.makedirs(d, exist_ok=True)

ITEM_NAMES = {1: "knife", 2: "boomerang", 3: "shovel", 4: "pickaxe", 5: "hammer", 6: "drill"}


def mode_stack(stack):
    s = np.asarray(stack)
    key = (s[..., 0].astype(np.int32) << 16) | (s[..., 1].astype(np.int32) << 8) | s[..., 2]
    flat = key.reshape(len(s), -1)
    out = np.empty(flat.shape[1], np.int32)
    for i in range(flat.shape[1]):
        vals, cnt = np.unique(flat[:, i], return_counts=True)
        out[i] = vals[np.argmax(cnt)]
    out = out.reshape(s.shape[1:3])
    return np.stack([(out >> 16) & 255, (out >> 8) & 255, out & 255], -1).astype(np.uint8)


def fill_periodic(img, known):
    """Preenche pixels desconhecidos copiando do mesmo mural deslocado pelo melhor periodo."""
    best = None
    ys, xs = np.nonzero(known)
    for py in (0, 16, 24, 32, 48, 64, 96):
        for px in (0, 16, 32, 48, 64, 96, 128):
            if px == 0 and py == 0:
                continue
            a = known & np.roll(known, (py, px), (0, 1))
            if a.sum() < 2000:
                continue
            diff = (img[a].astype(int) - np.roll(img, (py, px), (0, 1))[a].astype(int)).__abs__().mean()
            if best is None or diff < best[0]:
                best = (diff, py, px)
    out, k = img.copy(), known.copy()
    if best:
        _, py, px = best
        for n in range(1, 8):
            src = np.roll(out, (n * py, n * px), (0, 1))
            srck = np.roll(k, (n * py, n * px), (0, 1))
            m = ~k & srck
            out[m] = src[m]
            k |= m
            src = np.roll(out, (-n * py, -n * px), (0, 1))
            srck = np.roll(k, (-n * py, -n * px), (0, 1))
            m = ~k & srck
            out[m] = src[m]
            k |= m
    if (~k).any():  # vizinho mais proximo
        dist, idx = cv2.distanceTransformWithLabels((~k).astype(np.uint8), cv2.DIST_L2, 3,
                                                    labelType=cv2.DIST_LABEL_PIXEL)
        ky, kx = np.nonzero(k)
        lab = np.zeros(idx.max() + 1, np.int64)
        lab[idx[k]] = np.arange(len(ky))
        sel = lab[idx[~k]]
        out[~k] = out[ky[sel], kx[sel]]
    return out, best


objects = {}  # (piramide, nome) -> lista de recortes


def add_obj(p, name, frames, y, x, h, w):
    for f in frames:
        c = f[y:y + h, x:x + w]
        if c.shape[:2] == (h, w):
            objects.setdefault((p, name), []).append(c)


tiles = {}
report = []
for s in range(1, 61):
    d = json.load(open(f"out_levels/stage{s:02d}.json"))
    p = (s - 1) // 10 + 1
    for room in d["rooms"]:
        r_idx = room["index"]
        key = f"{s}-{r_idx}"
        if key not in match:
            continue
        good = match[key][3][:60]
        frames = [F[j][:192] for j in good]
        rows = room["rows"]
        mask_obj = np.zeros((24, 32), bool)
        for L in d["ladders"] + d["goldenLadders"]:
            if L["room"] == r_idx:
                mask_obj[L["row"]:L["row"] + L["length"], L["col"]:L["col"] + 2] = True
        for o in d["items"] + d["soulStones"]:
            if o["room"] == r_idx:
                mask_obj[max(0, o["y"] // 8 - 1):o["y"] // 8 + 3, max(0, o["x"] // 8 - 1):o["x"] // 8 + 3] = True
        e = d["exitDoor"]
        if e["room"] == r_idx:
            mask_obj[max(0, e["y"] // 8 - 1):e["y"] // 8 + 5, max(0, e["x"] // 8 - 1):e["x"] // 8 + 5] = True
        for c in d["coffins"]:
            if c["room"] == r_idx:
                mask_obj[max(0, c["y"] // 8 - 1):c["y"] // 8 + 4, max(0, c["x"] // 8 - 1):c["x"] // 8 + 3] = True
        known = np.zeros((192, 256), bool)
        bg = np.zeros((192, 256, 3), np.uint8)
        stack = np.asarray(frames)
        mode = mode_stack(stack) if len(stack) else None
        if mode is None:
            continue
        for r in range(24):
            for c in range(32):
                k = ".?=#".index(rows[r][c])
                cell = mode[r * 8:(r + 1) * 8, c * 8:(c + 1) * 8]
                if k == 0 and not mask_obj[r, c]:
                    bg[r * 8:(r + 1) * 8, c * 8:(c + 1) * 8] = cell
                    known[r * 8:(r + 1) * 8, c * 8:(c + 1) * 8] = True
                elif k in (2, 3) and match[key][1] >= 8:
                    tiles.setdefault((p, k, c % 2, r % 2), []).append(cell)
        filled, per = fill_periodic(bg, known)
        cv2.imwrite(f"art2/bg/s{s:02d}_r{r_idx}.png", filled)
        report.append((key, round(known.mean() * 100), per))
        # objetos (no frame "moda" da sala)
        for o in d["items"]:
            if o["room"] == r_idx:
                add_obj(p, ITEM_NAMES[o["type"]], [mode], o["y"], o["x"], 16, 16)
        for o in d["soulStones"]:
            if o["room"] == r_idx:
                add_obj(p, "soulstone", [mode], o["y"], o["x"], 16, 16)
        if e["room"] == r_idx:
            add_obj(p, "door", [mode], e["y"], e["x"], 32, 32)
        for c in d["coffins"]:
            if c["room"] == r_idx:
                add_obj(p, "coffin", [mode], c["y"], c["x"], 24, 16)
        for L in d["ladders"]:
            if L["room"] == r_idx and L["length"] >= 2:
                add_obj(p, "ladder", [mode], L["row"] * 8, L["col"] * 8, 16, 16)

for (p, name), crops in objects.items():
    cv2.imwrite(f"art2/obj/p{p}_{name}.png", mode_stack(crops))
for p in range(1, 7):
    sheet = np.zeros((16, 32, 3), np.uint8)
    for k, x0 in ((3, 0), (2, 16)):
        for cx in (0, 1):
            for cy in (0, 1):
                st = tiles.get((p, k, cx, cy))
                if st:
                    sheet[cy * 8:(cy + 1) * 8, x0 + cx * 8:x0 + (cx + 1) * 8] = mode_stack(st[:400])
    cv2.imwrite(f"art2/tiles_p{p}.png", sheet)
for r in report:
    print(*r)
