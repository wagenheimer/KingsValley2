import os
import glob
import json
import re
import struct

WORK = r'tools/kv2-extract/work'
ROM_PATH = os.path.join(WORK, 'rom', 'kv2_msx1.rom')
rom = open(ROM_PATH, 'rb').read()

def decompress_konami(data, off):
    out = bytearray()
    while True:
        b = data[off]
        off += 1
        if b == 0:
            break
        if (b & 0x80) == 0:
            cnt = b
            v = data[off]
            off += 1
            out.extend([v] * cnt)
        else:
            cnt = b & 0x7F
            for _ in range(cnt):
                out.append(data[off])
                off += 1
    return bytes(out)

# 1. Extract Pyramids 1..6 specs and frame patterns from ROM at 0x5936 (file 0x1936)
base_table = 0x1936
pyramids_data = {}

for p in range(1, 7):
    off = base_table + p * 10
    chunk = rom[off : off + 10]
    base_pat = chunk[0]
    period = chunk[1]
    ptrs = [struct.unpack('<H', chunk[2+i*2 : 4+i*2])[0] for i in range(4)]
    
    # Decompress each frame pointer (Color Table data)
    frames_patterns = [] # each frame is list of 8-byte color data
    for fi, addr in enumerate(ptrs):
        if addr < 0x8000:
            file_off = 7 * 0x2000 + (addr - 0x6000)
        else:
            file_off = 8 * 0x2000 + (addr - 0x8000)
        decomp = decompress_konami(rom, file_off)
        num_pats = len(decomp) // 8
        pat_list = []
        for pi in range(num_pats):
            pat_bytes = list(decomp[pi*8 : (pi+1)*8])
            pat_list.append(pat_bytes)
        frames_patterns.append(pat_list)
    
    num_pats = len(frames_patterns[0])
    pyramids_data[str(p)] = {
        'period': period,
        'base_pattern': base_pat,
        'pattern_count': num_pats,
        'frames': frames_patterns, # 4 frames, each with num_pats patterns of 8 color bytes
    }
    print(f'Pyramid {p}: base=0x{base_pat:02X}, period={period}, patterns={num_pats}, 4 frames extracted')

# 2. Extract Torch wave from ROM at 0x5930 (file 0x1930)
torch_ptrs = [0x657A, 0x6594, 0x65AC, 0x65C3, 0x65DE, 0x65F8, 0x6610, 0x6627]
torch_frames = []
for fi, addr in enumerate(torch_ptrs):
    file_off = 7 * 0x2000 + (addr - 0x6000)
    decomp = decompress_konami(rom, file_off)
    pat_list = []
    for pi in range(4):
        pat_list.append(list(decomp[pi*8 : (pi+1)*8]))
    torch_frames.append(pat_list)

torch_data = {
    'period': 4,
    'base_pattern': 0x98,
    'pattern_count': 4,
    'frames': torch_frames # 8 frames, each with 4 patterns of 8 color bytes
}
print(f'Torch: base=0x98, period=4, patterns=4, 8 frames extracted')

# 3. Scan all rooms from tools/kv2-extract/work/msx1_rooms/
room_vram_files = sorted(glob.glob(os.path.join(WORK, 'msx1_rooms', '*_vram.bin')))
rooms_output = {}

for vf in room_vram_files:
    m = re.search(r's(\d+)(?:_r(\d+))?_vram\.bin', vf)
    if not m:
        continue
    stage = int(m.group(1))
    room_num = int(m.group(2)) if m.group(2) else 1
    room_key = f's{stage:02d}_r{room_num:02d}'
    
    pyr = (stage - 1) // 10 + 1
    p_spec = pyramids_data[str(pyr)]
    eye_base = p_spec['base_pattern']
    eye_count = p_spec['pattern_count']
    eye_range = set(range(eye_base, eye_base + eye_count))
    
    vram = open(vf, 'rb').read()
    regs_f = vf.replace('_vram.bin', '_regs.bin')
    regs = open(regs_f, 'rb').read() if os.path.exists(regs_f) else bytes([0]*8)
    name_base = (regs[2] & 0x0F) * 0x400 if len(regs) > 2 else 0x3800
    pat_base = (regs[4] & 0x3C) << 11 if len(regs) > 4 else 0x2000
    col_base = (regs[3] & 0x80) << 6 if len(regs) > 3 else 0x0000
    p_mask = regs[4] & 0x03 if len(regs) > 4 else 3
    c_mask = (regs[3] & 0x03) if len(regs) > 3 and (regs[3] & 0x7F) < 4 else 3
    
    room_cells = []
    # Routine 0x4EB7 writes to all 3 banks in VRAM (rows 0..23).
    for ty in range(24):
        third = ty // 8
        p_bank = (third & p_mask) * 0x800
        c_bank = (third & c_mask) * 0x800
        for tx in range(32):
            code = vram[name_base + ty * 32 + tx]
            anim_type = None
            pat_offset = 0
            if code in eye_range:
                anim_type = 'eye'
                pat_offset = code - eye_base
            
            if anim_type:
                # Authentic TMS9918 pattern generator bits at pat_base + p_bank + code * 8
                pat_addr = pat_base + p_bank + code * 8
                pat_bytes = list(vram[pat_addr : pat_addr + 8])
                # Authentic TMS9918 color table bytes at col_base + c_bank + code * 8
                col_addr = col_base + c_bank + code * 8
                col_bytes = list(vram[col_addr : col_addr + 8])
                room_cells.append({
                    'x': tx,
                    'y': ty,
                    'type': anim_type,
                    'code': f'0x{code:02X}',
                    'offset': pat_offset,
                    'pat': pat_bytes,
                    'col': col_bytes
                })
    if room_cells:
        rooms_output[room_key] = room_cells

print(f'Total rooms with animated cells: {len(rooms_output)}')

full_json = {
    'pyramids': pyramids_data,
    'torch': torch_data,
    'rooms': rooms_output
}

out_path = 'src/KingsValley.Desktop/Content/RomMsx1/Rooms/animations.json'
with open(out_path, 'w', encoding='utf-8') as f:
    json.dump(full_json, f, indent=2)

print(f'Successfully wrote animations.json to {out_path} ({os.path.getsize(out_path)} bytes)')
