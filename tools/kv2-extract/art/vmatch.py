"""Acha, para cada (stage, sala), o frame do longplay MSX2 que melhor bate com o terreno extraido.

Score de Fisher: distancia entre as cores medias das classes (parede/cavavel/vazio) dividida pela
variancia interna. Saida: vmatch.json {"stage-room": [t, score, frame_index]}.
"""
import json
import numpy as np

z = np.load("vframes.npz")
frames, times = z["frames"], z["t"]
# features por celula (32x24): cor media
F = frames[:, :192].reshape(len(frames), 24, 8, 32, 8, 3).mean(axis=(2, 4)).astype(np.float32)
V = frames[:, :192].reshape(len(frames), 24, 8, 32, 8, 3).astype(np.float32).var(axis=(2, 4)).mean(-1)

out = {}
for s in range(1, 61):
    d = json.load(open(f"out_levels/stage{s:02d}.json"))
    for room in d["rooms"]:
        cells = np.array([[".?=#".index(c) for c in row] for row in room["rows"]])
        masks = [cells == k for k in (0, 2, 3)]
        if masks[2].sum() < 10 or masks[0].sum() < 10:
            continue
        means = []
        within = np.zeros(len(F), np.float32)
        for m in masks:
            if m.sum() == 0:
                means.append(None)
                continue
            mu = F[:, m].mean(axis=1)                      # (N,3)
            means.append(mu)
            within += ((F[:, m] - mu[:, None]) ** 2).sum(axis=(1, 2)) / m.sum()
        between = np.zeros(len(F), np.float32)
        pairs = [(a, b) for a in range(3) for b in range(a + 1, 3) if means[a] is not None and means[b] is not None]
        for a, b in pairs:
            between += ((means[a] - means[b]) ** 2).sum(1)
        score = between / (within + 50)
        i = int(np.argmax(score))
        good = [int(j) for j in np.nonzero(score > 0.6 * score[i])[0] if abs(times[j] - times[i]) < 400]
        out[f"{s}-{room['index']}"] = [float(times[i]), float(score[i]), i, good]
json.dump(out, open("vmatch.json", "w"), indent=0)
print(sum(len(v[3]) for v in out.values()), "frames bons")
