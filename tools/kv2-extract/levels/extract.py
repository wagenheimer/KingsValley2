"""Extrai os 60 stages do King's Valley II para JSON.

Fontes:
- ROM (RC-760): terreno (RLE 2 bits), escadas, escadas douradas, itens, grade de salas.
- Dumps de RAM logo apos a carga (stages/sNN/t006|t045_mem.bin): soul stones, porta de saida,
  sarcofagos, inimigos e inicio do jogador.

Uso: python extract.py <saida_dir>
"""
import json
import os
import sys

ROM = open("rom/kv2_msx1.rom", "rb").read()
BANK10 = 0x14000   # mapeado em 0x6000 durante a carga do terreno
BANK13 = 0x1A000   # mapeado em 0xA000 durante a carga de itens/grade
ROOM_W, ROOM_H = 32, 24


def w(off):
    return ROM[off] | (ROM[off + 1] << 8)


def b10(addr):
    return BANK10 + (addr - 0x6000)


def b13(addr):
    return BANK13 + (addr - 0xA000)


def table_ptr(table, stage, mapper):
    return mapper(w(mapper(table) + 2 * (stage - 1)))


def terrain(stage):
    p = table_ptr(0x6000, stage, b10)
    cells = []
    while ROM[p]:
        cells.extend([ROM[p] >> 6] * (ROM[p] & 0x3F))
        p += 1
    return cells


def ladders(stage, table):
    """Escadas (tabela 0x6078) ou escadas douradas (0x60F0). Devolve [(sala, col, linha, alt)]."""
    p = table_ptr(table, stage, b10)
    room = 1
    out = []
    while True:
        e = ROM[p]
        if e == 0xFF:
            room += 1
            p += 1
            continue
        if e == 0:
            break
        d = ROM[p + 1]
        de = (d << 8) | e
        off = de >> 7                  # byte dentro da sala (8 bytes por linha)
        sub = (e >> 5) & 3             # celula dentro do byte
        out.append({"room": room, "col": (off % 8) * 4 + sub, "row": off // 8, "length": e & 0x1F})
        p += 2
    return out


def items(stage):
    p = table_ptr(0xAFB1, stage, b13)
    out = []
    while ROM[p] != 0xFF:
        t = ROM[p]
        out.append({"type": t & 0x0F, "room": t >> 4, "y": ROM[p + 1], "x": ROM[p + 2]})
        p += 3
    return out


def room_grid(stage):
    """Grade 8x8 (bitmask por linha); salas numeradas na ordem de leitura."""
    base = b13(0xAB5A) + 8 * (stage - 1)
    grid = []
    n = 1
    for r in range(8):
        row = []
        bits = ROM[base + r]
        for c in range(8):
            if bits & (0x80 >> c):
                row.append(n)
                n += 1
            else:
                row.append(0)
        grid.append(row)
    return grid, n - 1


def ram_objects(stage):
    d = f"stages/s{stage:02d}"
    early = open(f"{d}/t006_mem.bin", "rb").read()
    late = open(f"{d}/t045_mem.bin", "rb").read()
    stones = []
    for a in range(0xE700, 0xE780, 8):
        if early[a]:
            stones.append({"room": early[a + 1], "y": early[a + 2], "x": early[a + 3]})
    # E600: mecanismos do stage (1 = porta giratoria, 5 = bloco empurravel; 2-4 a identificar)
    enemies = []
    for a in range(0xE600, 0xE700, 16):
        if early[a]:
            enemies.append({"type": early[a], "y": early[a + 2], "x": early[a + 3], "room": early[a + 4],
                            "raw": early[a:a + 10].hex()})
    # E2C0: sarcofagos = geradores de inimigos, registros de 5 bytes:
    # [0x80 | tipo do inimigo 1..4][0x10 | nivel][sala][y][x]
    coffins = []
    a = 0xE2C0
    while a < 0xE2F0 and early[a] & 0x80:
        coffins.append({"type": early[a] & 0x0F, "level": early[a + 1] & 0x0F,
                        "room": early[a + 2], "y": early[a + 3], "x": early[a + 4]})
        a += 5
    door = {"room": early[0xE2F3], "y": early[0xE2F1], "x": early[0xE2F2],
            "w": early[0xE2F4], "h": early[0xE2F5]}
    player = {"room": late[0xE243] or 1, "y": late[0xE282], "x": late[0xE284]}
    return stones, enemies, coffins, door, player


def main(outdir):
    os.makedirs(outdir, exist_ok=True)
    summary = []
    for s in range(1, 61):
        cells = terrain(s)
        grid, nrooms = room_grid(s)
        rooms = []
        for r in range(len(cells) // (ROOM_W * ROOM_H)):
            chunk = cells[r * ROOM_W * ROOM_H:(r + 1) * ROOM_W * ROOM_H]
            rows = ["".join(".?=#"[v] for v in chunk[y * ROOM_W:(y + 1) * ROOM_W]) for y in range(ROOM_H)]
            rooms.append({"index": r + 1, "rows": rows})
        stones, enemies, coffins, door, player = ram_objects(s)
        stage = {
            "stage": s,
            "pyramid": (s - 1) // 10 + 1,
            "roomGrid": grid,
            "rooms": rooms,
            "ladders": ladders(s, 0x6078),
            "goldenLadders": ladders(s, 0x60F0),
            "items": items(s),
            "soulStones": stones,
            "exitDoor": door,
            "coffins": coffins,
            "mechanisms": enemies,
            "playerStart": player,
        }
        for room in stage["rooms"]:
            room["background"] = f"s{s:02d}_r{room['index']}.png"
        stage["music"] = ((s - 1) % 10) // 2
        with open(os.path.join(outdir, f"stage{s:02d}.json"), "w", encoding="utf-8") as f:
            json.dump(stage, f, indent=1)
        summary.append((s, len(rooms), nrooms, len(stones), len(stage["items"]), len(enemies), len(coffins)))
    for row in summary:
        print("stage %2d rooms=%d grid=%d stones=%d items=%d enemies=%d coffins=%d" % row)


if __name__ == "__main__":
    main(sys.argv[1])
