"""Indexa o longplay MSX2: um frame nativo (256x212, cores MSX2 de 9 bits) a cada N segundos.

Saida: vframes.npz com 'frames' (N,212,256,3 uint8) e 't' (segundos).
"""
import sys
import cv2
import numpy as np

X0, Y0, S = 96, 42, 3
step = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
cap = cv2.VideoCapture("video/kv2.mp4")
fps = cap.get(cv2.CAP_PROP_FPS)
total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
every = int(round(fps * step))
frames, times = [], []
i = 0
while True:
    ok = cap.grab()
    if not ok:
        break
    if i % every == 0:
        ok, f = cap.retrieve()
        if ok:
            a = f[Y0:Y0 + 212 * S, X0:X0 + 256 * S].reshape(212, S, 256, S, 3)
            native = np.median(a, axis=(1, 3))
            q = np.clip(np.rint(native / 255 * 7), 0, 7) * 255 / 7
            frames.append(q.astype(np.uint8))
            times.append(i / fps)
    i += 1
np.savez_compressed("vframes.npz", frames=np.stack(frames), t=np.array(times))
print(len(frames), "frames de", total)
