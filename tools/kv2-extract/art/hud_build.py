"""Extrai a faixa do HUD MSX2 e os digitos 0-9 (fonte do jogo) do longplay -> art2/hud/."""
import json
import os
import numpy as np
import cv2

z = np.load("vframes.npz")
F = z["frames"]
m = json.load(open("vmatch.json"))
os.makedirs("art2/hud", exist_ok=True)
votes = {d: [] for d in range(10)}
for key, v in m.items():
    s = int(key.split("-")[0])
    if v[1] < 8:
        continue
    for j in v[3][:10]:
        hud = F[j][192:212]
        for k, digit in ((0, s // 10), (1, s % 10)):
            votes[digit].append(hud[10:18, 176 + 8 * k:184 + 8 * k])
bgcol = None
for d, lst in votes.items():
    s = np.asarray(lst)
    key = (s[..., 0].astype(np.int32) << 16) | (s[..., 1].astype(np.int32) << 8) | s[..., 2]
    out = np.zeros((8, 8), np.int32)
    for y in range(8):
        for x in range(8):
            vals, cnt = np.unique(key[:, y, x], return_counts=True)
            out[y, x] = vals[np.argmax(cnt)]
    img = np.stack([(out >> 16) & 255, (out >> 8) & 255, out & 255], -1).astype(np.uint8)
    ink = img.astype(int).sum(-1) > 300
    bgcol = img[~ink][0] if (~ink).any() else bgcol
    cv2.imwrite(f"art2/hud/digit_{d}.png", np.dstack([img, ink.astype(np.uint8) * 255]))
strip = F[m["1-1"][2]][192:212].copy()
for x0, n in ((24, 6), (88, 6), (176, 2), (224, 2)):
    strip[10:18, x0:x0 + 8 * n] = bgcol
cv2.imwrite("art2/hud/hud_strip.png", np.dstack([strip, np.full(strip.shape[:2], 255, np.uint8)]))
print("ok", {d: len(v) for d, v in votes.items()})
