"""Telas fixas do MSX2 reconstruidas do longplay -> <out>/Screens.

ATENCAO: a piramide da ABERTURA nao sai mais daqui - ela vinha deste video com o Vick
"assado" no bitmap e com a paleta do video. Agora ela vem do dump de VRD do emulador
(art/intro_build.py + gen/intro_*.tcl). Título e menu continuam vindo do longplay.
"""
import os
import sys
import cv2
import numpy as np

out = os.path.join(sys.argv[1], "Screens")
os.makedirs(out, exist_ok=True)
X0, Y0, S = 96, 42, 3
cap = cv2.VideoCapture("video/kv2.mp4")


def native(t):
    acc = []
    for dt in (0.0, 0.2, 0.4):
        cap.set(cv2.CAP_PROP_POS_MSEC, (t + dt) * 1000)
        ok, f = cap.read()
        a = f[Y0:Y0 + 212 * S, X0:X0 + 256 * S].reshape(212, S, 256, S, 3)
        acc.append(np.median(a, axis=(1, 3)))
    q = np.clip(np.rint(np.median(acc, axis=0) / 255 * 7), 0, 7) * 255 / 7
    return q.astype(np.uint8)


for name, t in (("title", 24.0), ("start_select", 71.0), ("pyramid", 85.0)):
    cv2.imwrite(os.path.join(out, f"{name}.png"), native(t))
print("ok")

# seta vermelha do START SELECT vira sprite (art2/hud/arrow.png) e e apagada da tela
img = cv2.imread(os.path.join(out, "start_select.png"))
red = (img[..., 2] > 150) & (img[..., 1] < 100) & (img[..., 0] < 100)
ys, xs = np.nonzero(red)
y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
arrow = np.dstack([img[y0:y1, x0:x1], (red[y0:y1, x0:x1] * 255).astype(np.uint8)])
cv2.imwrite("art2/hud/arrow.png", arrow)
img[y0:y1, x0:x1] = 0
cv2.imwrite(os.path.join(out, "start_select.png"), img)
print("seta em", x0, y0, x1 - x0, y1 - y0)

# cursor (caneta) do titulo GAME/EDIT vira sprite (art2/hud/title_cursor.png) e e apagado da tela
img = cv2.imread(os.path.join(out, "title.png"))
y0, y1, x0, x1 = 140, 147, 56, 72  # caneta ao lado de GAME (EDIT fica 16 px abaixo)
crop = img[y0:y1, x0:x1]
mask = (crop.sum(-1) > 0)
cv2.imwrite("art2/hud/title_cursor.png", np.dstack([crop, (mask * 255).astype(np.uint8)]))
img[y0:y1, x0:x1] = 0
cv2.imwrite(os.path.join(out, "title.png"), img)
print("cursor do titulo em", x0, y0, x1 - x0, y1 - y0)
