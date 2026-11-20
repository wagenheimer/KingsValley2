"""Separa os recortes por assinatura de cor e monta folhas por personagem."""
import pickle
import numpy as np
import cv2

items = pickle.load(open("spr/all.pkl", "rb"))

def frac(core, cond):
    a = core[..., 3] > 0
    px = core[..., :3][a].astype(int)  # BGR
    if len(px) == 0:
        return 0
    return cond(px).mean()

sig = {
    "vick": lambda p: (p[:, 0] > 200) & (p[:, 1] > 180) & (p[:, 2] < 120),          # ciano
    "white": lambda p: (p[:, 0] > 200) & (p[:, 1] > 200) & (p[:, 2] > 200),
    "gold": lambda p: (p[:, 0] < 120) & (p[:, 1] > 150) & (p[:, 2] > 200),
    "red": lambda p: (p[:, 0] < 120) & (p[:, 1] < 120) & (p[:, 2] > 180),
}
groups = {k: [] for k in sig}
for core, cnt, key in items:
    h, w = core.shape[:2]
    if not (12 <= h <= 18 and 10 <= w <= 18):
        continue
    for k, f in sig.items():
        if frac(core, f) > (0.12 if k == "vick" else 0.30):
            groups[k].append((core, cnt, key))
            break
for k, lst in groups.items():
    lst.sort(key=lambda v: -v[1])
    cells = []
    for idx, (core, cnt, key) in enumerate(lst[:160]):
        tile = np.zeros((20, 20, 3), np.uint8)
        h, w = core.shape[:2]
        tile[2:2 + h, 2:2 + w] = (core[..., :3] * (core[..., 3:4] / 255)).astype(np.uint8)
        big = cv2.resize(tile, (60, 60), interpolation=cv2.INTER_NEAREST)
        cv2.putText(big, str(idx), (1, 58), cv2.FONT_HERSHEY_SIMPLEX, 0.3, (0, 255, 255), 1)
        cells.append(big)
    while len(cells) % 20:
        cells.append(np.zeros((60, 60, 3), np.uint8))
    if cells:
        cv2.imwrite(f"png/spr_{k}.png", np.vstack([np.hstack(cells[i:i + 20]) for i in range(0, len(cells), 20)]))
    pickle.dump(lst, open(f"spr/{k}.pkl", "wb"))
    print(k, len(lst))
