import glob
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT_DIR = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'src', 'KingsValley.Desktop', 'Content'))
MURALS_BIN = os.path.join(CONTENT_DIR, 'RomMsx1', 'Rooms', 'murals.bin')
murals_raw = open(MURALS_BIN, 'rb').read()

# Load murals for pyramids 1..6
murals = []
for p in range(6):
    chunk = murals_raw[p * 1024 : (p + 1) * 1024]
    mural = bytearray(64 * 32)
    for i in range(1024):
        b = chunk[i]
        mural[i * 2] = b >> 4
        mural[i * 2 + 1] = b & 0x0F
    murals.append(mural)

# Load all stage definitions
stages = {}
for sf in glob.glob(os.path.join(CONTENT_DIR, 'Rom', 'Levels', 'stage*.json')):
    with open(sf, 'r', encoding='utf-8') as f:
        d = json.load(f)
    stages[d['stage']] = d

target_dirs = [
    os.path.join(CONTENT_DIR, 'RomMsx1', 'Rooms', 'dumps'),
]
appdata = os.environ.get('APPDATA', '')
if appdata:
    app_dumps = os.path.join(appdata, 'KingsValley', 'extracted', 'Rooms', 'dumps')
    if os.path.exists(app_dumps):
        target_dirs.append(app_dumps)

total_cleaned = 0

for target_dir in target_dirs:
    print(f'Processing {target_dir}...')
    for bf in sorted(glob.glob(os.path.join(target_dir, 's*.bin'))):
        base = os.path.basename(bf)
        # format: sNN_rMM.bin
        parts = base[:-4].split('_')
        if len(parts) != 2:
            continue
        try:
            stage_num = int(parts[0][1:])
            room_num = int(parts[1][1:])
        except ValueError:
            continue

        stage_d = stages.get(stage_num)
        if not stage_d:
            continue

        pyr = (stage_num - 1) // 10 + 1
        mural = murals[pyr - 1]

        with open(bf, 'rb') as f:
            raw = bytearray(f.read())

        if len(raw) < 38 + 24576:
            continue

        w = raw[32] | (raw[33] << 8)
        h = raw[34] | (raw[35] << 8)
        if w != 256 or h != 192:
            continue

        def restore_cell(col, row):
            muralCol = col % 8
            muralRow = row % 4
            for r in range(8):
                py = row * 8 + r
                if py >= 192: continue
                my = muralRow * 8 + r
                for c in range(8):
                    px = col * 8 + c
                    if px >= 256: continue
                    mx = muralCol * 8 + c
                    pal_idx = mural[my * 64 + mx]
                    idx = py * w + px
                    byte_idx = 38 + (idx >> 1)
                    if (idx & 1) == 0:
                        raw[byte_idx] = (raw[byte_idx] & 0x0F) | (pal_idx << 4)
                    else:
                        raw[byte_idx] = (raw[byte_idx] & 0xF0) | (pal_idx & 0x0F)

        # 1. Restore soulstones
        for s in stage_d.get('soulStones', []):
            if s.get('room') == room_num:
                c0 = s['x'] >> 3
                r0 = s['y'] >> 3
                for dr in range(2):
                    for dc in range(2):
                        restore_cell(c0 + dc, r0 + dr)

        # 2. Restore items
        for it in stage_d.get('items', []):
            if it.get('room') == room_num:
                c0 = it['x'] >> 3
                r0 = it['y'] >> 3
                for dr in range(2):
                    for dc in range(2):
                        restore_cell(c0 + dc, r0 + dr)

        with open(bf, 'wb') as f:
            f.write(raw)
        total_cleaned += 1

print(f'Successfully stripped soulstones and items from {total_cleaned} room dumps!')
