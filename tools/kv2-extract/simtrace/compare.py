"""Compara a trilha do inimigo no emulador (trace_enemy.tcl) com a da simulação (simtrace).

Uso: python compare.py <trace_emulador.txt> <trace_sim.txt> [ticks]
Alinha as duas pelo nascimento do 1º inimigo e conta ticks com (x, y, estado) diferentes.
"""
import sys

emu = [l.split() for l in open(sys.argv[1])]
sim = [l.split() for l in open(sys.argv[2])]
n = int(sys.argv[3]) if len(sys.argv) > 3 else 400
i0 = next(i for i, l in enumerate(emu) if l[1] != "00")
j0 = next(i for i, l in enumerate(sim) if l[1] != "-")
# estado do original (IX+1) -> EnemyState do C# (0 Emerging, 1 Walking, 2 Pausing, 3 Falling, 4 Climbing)
PHASES = {
    1: {0: 0, 1: 0, 2: 2, 3: 1, 4: 3},          # AD80
    2: {0: 0, 1: 0, 2: 2, 3: 1, 4: 4, 5: 3},    # AE23
    3: {0: 0, 1: 2, 2: 5, 3: 4, 4: 3},          # B2AA
    4: {0: 0, 1: 2, 2: 1, 3: 3, 4: 2, 5: 5},    # B68E
    5: {s: 6 for s in range(16)},               # 662D efeito de morte
}
diffs = 0
for k in range(min(n, len(emu) - i0, len(sim) - j0)):
    e, s = emu[i0 + k], sim[j0 + k]
    a = (int(e[4], 16), int(e[6], 16), PHASES.get(int(e[1], 16), {}).get(int(e[2], 16)))
    b = (int(s[2][1:], 16), int(s[3][1:], 16), int(s[1][1:]))
    if a != b:
        diffs += 1
        if diffs <= 10:
            print(f"tick {k}: emu y,x,st={a}  sim={b}")
print("diferencas:", diffs)
