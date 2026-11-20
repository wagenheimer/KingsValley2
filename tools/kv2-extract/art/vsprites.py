"""Extrai sprites MSX2 do longplay: diferenca contra a 'moda' da sala -> componentes -> catalogo.

Saida: spr/cat.npz (recortes RGBA 24x24 unicos + contagem) e png/spr_cat.png (catalogo numerado).
"""
import json
import os
import numpy as np
import cv2

z = np.load("vframes.npz")
F = z["frames"]
match = json.load(open("vmatch.json"))
os.makedirs("spr", exist_ok=True)


def mode_stack(s):
    key = (s[..., 0].astype(np.int32) << 16) | (s[..., 1].astype(np.int32) << 8) | s[..., 2]
    flat = key.reshape(len(s), -1)
    srt = np.sort(flat, axis=0)
    # moda por coluna via run-length
    out = np.empty(flat.shape[1], np.int32)
    for i in range(flat.shape[1]):
        v, c = np.unique(srt[:, i], return_counts=True)
        out[i] = v[np.argmax(c)]
    out = out.reshape(s.shape[1:3])
    return np.stack([(out >> 16) & 255, (out >> 8) & 255, out & 255], -1).astype(np.uint8)


catalog = {}
for key, (t, score, best, good) in match.items():
    if score < 8 or len(good) < 6:
        continue
    stack = F[good[:40], :192]
    bg = mode_stack(stack)
    for f in stack:
        diff = (np.abs(f.astype(int) - bg.astype(int)).sum(-1) > 40).astype(np.uint8)
        diff = cv2.morphologyEx(diff, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
        n, lab, stats, _ = cv2.connectedComponentsWithStats(diff, 8)
        for i in range(1, n):
            x, y, w, h, area = stats[i]
            if not (8 <= w <= 24 and 10 <= h <= 24 and area >= 30):
                continue
            cx, cy = x + w // 2, y + h // 2
            x0, y0 = max(0, cx - 12), max(0, cy - 12)
            crop = f[y0:y0 + 24, x0:x0 + 24]
            m = (lab[y0:y0 + 24, x0:x0 + 24] == i)
            if crop.shape[:2] != (24, 24):
                continue
            rgba = np.zeros((24, 24, 4), np.uint8)
            rgba[..., :3] = crop
            rgba[..., 3] = m * 255
            # chave: pixels opacos alinhados pelo canto do bbox
            ys, xs = np.nonzero(m)
            core = rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
            k = core.tobytes() + bytes(core.shape[:2])
            if k in catalog:
                catalog[k][1] += 1
            else:
                catalog[k] = [core, 1, key]
items = sorted(catalog.values(), key=lambda v: -v[1])
print(len(items), "recortes unicos")
keep = [it for it in items if it[1] >= 3][:400]
cells = []
for idx, (core, cnt, key) in enumerate(keep):
    tile = np.zeros((32, 32, 3), np.uint8)
    h, w = core.shape[:2]
    a = core[..., 3:4] / 255
    tile[2:2 + h, 2:2 + w] = (core[..., :3] * a).astype(np.uint8)
    big = cv2.resize(tile, (64, 64), interpolation=cv2.INTER_NEAREST)
    cv2.putText(big, str(idx), (1, 62), cv2.FONT_HERSHEY_SIMPLEX, 0.3, (0, 255, 255), 1)
    cells.append(big)
while len(cells) % 20:
    cells.append(np.zeros((64, 64, 3), np.uint8))
rows = [np.hstack(cells[i:i + 20]) for i in range(0, len(cells), 20)]
cv2.imwrite("png/spr_cat.png", np.vstack(rows))
np.save("spr/keep.npy", np.array([k[0] for k in keep], dtype=object), allow_pickle=True)
json.dump([[int(k[1]), k[2]] for k in keep], open("spr/keep.json", "w"))
