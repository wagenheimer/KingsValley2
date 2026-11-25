"""Junta instancias da mesma pose (mesma mascara da cor-chave) para tapar os furos do recorte."""
import pickle
import sys
import numpy as np
import cv2

name = sys.argv[1]
keycond = {
    "vick": lambda p: (p[..., 0] > 200) & (p[..., 1] > 180) & (p[..., 2] < 120),
    "white": lambda p: (p[..., 0] > 200) & (p[..., 1] > 200) & (p[..., 2] > 200),
    "gold": lambda p: (p[..., 0] < 120) & (p[..., 1] > 150) & (p[..., 2] > 200),
    "red": lambda p: (p[..., 0] < 120) & (p[..., 1] < 120) & (p[..., 2] > 180),
}[name]
items = pickle.load(open(f"spr/{name}.pkl", "rb"))
groups = {}
for core, cnt, key in items:
    km = keycond(core[..., :3].astype(int)) & (core[..., 3] > 0)
    ys, xs = np.nonzero(km)
    if len(ys) < 6:
        continue
    y0, x0 = ys.min(), xs.min()
    sig = km[y0:ys.max() + 1, x0:xs.max() + 1]
    k = sig.tobytes() + bytes(sig.shape)
    groups.setdefault(k, []).append((core, cnt, y0, x0))
out = []
for k, members in groups.items():
    votes = {}
    total = 0
    for core, cnt, y0, x0 in members:
        total += cnt
        h, w = core.shape[:2]
        for y in range(h):
            for x in range(w):
                if core[y, x, 3]:
                    p = (y - y0, x - x0)
                    votes.setdefault(p, {}).setdefault(tuple(core[y, x, :3]), 0)
                    votes[p][tuple(core[y, x, :3])] += cnt
    if total < 3:
        continue
    ys = [p[0] for p in votes]; xs = [p[1] for p in votes]
    oy, ox = min(ys), min(xs)
    img = np.zeros((max(ys) - oy + 1, max(xs) - ox + 1, 4), np.uint8)
    thr = max(2, 0.3 * total) if total >= 6 else 1
    for (y, x), cols in votes.items():
        c = max(cols, key=cols.get)
        if cols[c] < thr:
            continue
        img[y - oy, x - ox, :3] = c
        img[y - oy, x - ox, 3] = 255
    out.append((total, len(members), img))
out.sort(key=lambda v: -v[0])
pickle.dump(out, open(f"spr/{name}_merged.pkl", "wb"))
cells = []
for idx, (total, n, img) in enumerate(out[:120]):
    tile = np.zeros((22, 22, 3), np.uint8)
    h, w = min(img.shape[0], 22), min(img.shape[1], 22)
    tile[:h, :w] = (img[:h, :w, :3] * (img[:h, :w, 3:4] / 255)).astype(np.uint8)
    big = cv2.resize(tile, (66, 66), interpolation=cv2.INTER_NEAREST)
    cv2.putText(big, f"{idx}:{n}", (1, 64), cv2.FONT_HERSHEY_SIMPLEX, 0.3, (0, 255, 255), 1)
    cells.append(big)
while len(cells) % 15:
    cells.append(np.zeros((66, 66, 3), np.uint8))
cv2.imwrite(f"png/sprm_{name}.png", np.vstack([np.hstack(cells[i:i + 15]) for i in range(0, len(cells), 15)]))
print(name, len(out), "poses")
