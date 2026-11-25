"""Monta a imagem de uma sala do King's Valley II (MSX2) emulando o pipeline do
proprio jogo (44D4) e as rotinas que ele chama, a partir da ROM + da VRAM/RAM do dump.

Ordem exata do 44D4 (os `CALL 0x41xx` sao troca de banco do mapper SCC, nao desenho;
no mapper SCC 0x7000 -> janela 0x6000, 0x9000 -> 0x8000, 0xB000 -> 0xA000):

  d7: CALL 41cc   -> banco 14 em 0x8000, banco 15 em 0xA000
  da: CALL 44f2   -> MURAL (opaco, 32 codigos de 8x8, repetido 4x na horizontal)
  dd: CALL 4606   -> DECORACOES (transparente; lista 0x806A+2*E242, registos de 3 bytes)
  e0: CALL 41b0   -> banco 10 em 0x6000, 11 em 0x8000, 12 em 0xA000
  e3: CALL 4535   -> MAPA da sala (2 bits/celula em 0xE900+(sala-1)*224) com autotile
  e6: CALL 418a   -> banco 1 em 0x6000, 2 em 0x8000, 3 em 0xA000
  e9: CALL 46a4   -> SARCOFAGOS (E2C0, 5 bytes) -> 3x2 em 0x93AC
  ec: CALL 651d   -> PEDRAS-ALMA (E700, 8 bytes) -> 2x2 em 0x9324

Escadas douradas: a *cauda* do 4535 (4576..45c8) desenha-as DEPOIS do mapa:
  - tabela 0xE7C0 (16 registos de 4 bytes: sala, y, x, altura|0x80), escrita pelo 44AC
    a partir da tabela 0x60F0 do banco 10 (a mesma que o port le para `goldenLadders`);
  - nao desenha quando E200 == 11 (4581);
  - 56DE repinta o MURAL na area de 2 colunas x altura (transparente, 5767) e 4690 desenha
    os blocos 1 (esq) e 2 (dir) - os bytes 0x45C9/0x45CA - em cada linha (4587..45c8).
    A repintura existe porque os blocos 1/2 sao transparentes em parte: sem ela a escada
    normal (blocos 3/4, do mapa) apareceria por baixo.

Fora do 44D4 mas visivel no ecra (cada um le a sua tabela):
  - PORTA  (9010: E2F1..E2F6 = y,x,sala,w,h,frame)
      4x4 em 0x9340+16*frame (banco 2) + `h` pregos: 41bf -> banco 13 em 0xA000,
      ponteiro 0xB9AD+2*w e o registo da posicao (x += (v&3)*8, y += ((v&12)<<1)),
      tile 0x60 se 0xF0F4 == 0 senao 0xAE.
  - ITENS  (E300, 8 bytes: tipo,y,x,sala,anim,..) -> 2x2 em 0x9334 (banco 2).

Os codigos de bloco sao indices de 8x8 na pagina de padroes (VRAM 0x8000):
codigo n -> x=(n&31)*8, y=(n>>5)*8 - a convencao que a ROM usa em 523d.
"""
import numpy as np

import rom_backgrounds as rb

# --- RAM do jogo ---
RAM_MAP = 0xE900          # mapa 2 bits por sala; 224 bytes por sala
RAM_COFFINS = 0xE2C0      # 8 registos de 5 bytes
RAM_STONES = 0xE700       # 16 registos de 8 bytes
RAM_ITEMS = 0xE300        # 8 registos de 8 bytes
RAM_DOOR = 0xE2F1         # y,x,sala,w,h,frame
RAM_DOOR_STUD = 0xF0F4    # 0 -> prego 0x60, !=0 -> prego 0xAE
RAM_GOLDEN = 0xE7C0       # 16 registos de 4 bytes: sala, y, x, altura|0x80
RAM_STATE = 0xE200        # == 11 -> o 4581 nao desenha as escadas douradas

# --- ROM: graficos dos objetos (todos no banco 2, janela 0x8000) ---
BANK_GFX = 2
GFX_STONE = 0x9324        # 2x2 pedra-alma
GFX_ITEM = 0x9328         # 6 graficos de 2x2 itens, um por tipo: 0x9328 + 4*(tipo-1)
GFX_DOOR = 0x9340         # 4x4 por frame (3 frames de 16 bytes)
GFX_COFFIN = 0x93AC       # 3x2 sarcofago

# --- ROM: escadas douradas (banco 0, janela 0x4000) ---
GOLDEN_LEFT = 0x01        # 0x45C9: codigo da coluna esquerda (todas as linhas)
GOLDEN_RIGHT = 0x02       # 0x45CA: codigo da coluna direita

# --- ROM: tabela dos pregos da porta (banco 13, janela 0xA000) ---
BANK_STUDS = 13
DOOR_STUDS = 0xB9AD       # ponteiro por largura w (a tabela comeca em 0xB9AB, w=0)

ROOM_STRIDE = 224
ROOM_W, ROOM_H = 32, 24


def _icon(rom, p1, img, addr, rows, cols, x, y, b8=BANK_GFX):
    """Desenha rows x cols codigos consecutivos a partir de addr (copia transparente, 5767).

    Devolve as celulas (linha, coluna) de 8x8 que o desenho cobre.
    """
    o = rom.off(addr, b8=b8)
    cells = []
    for r in range(rows):
        for c in range(cols):
            rb.put(img, p1, rom.d[o + r * cols + c], x + c * 8, y + r * 8, False)
            cells.append(((y + r * 8) // 8, (x + c * 8) // 8))
    return cells


def mural(rom, pyramid, stage, p1):
    """44F2: fundo inteiro, opaco. 32 codigos de 8x8; os 8 de cada faixa repetem 4x.

    O desenho vem do `rom_backgrounds.mural` (fonte unica); aqui so devolvemos tambem os codigos.
    """
    base = rom.word(0x7FFE + 2 * pyramid) + (0x20 if stage % 2 == 1 else 0)   # 44FE: RRA -> impar +0x20
    o = rom.off(base)
    codes = list(rom.d[o:o + 32])
    return rb.mural(rom, pyramid, stage, p1), codes


def mural_code(codes, x, y):
    """56F8: codigo do mural na celula (x, y) - 8x4 codigos repetidos pelo ecra."""
    return codes[((y >> 3) & 3) * 8 + ((x >> 3) & 7)]


def tile_map(rom, mem, p1, img, room, stride=ROOM_STRIDE):
    """4535: mapa de 2 bits por celula lido da RAM (0xE900 + (sala-1)*224).

    0 = nada, 2 = bloco 5, 3 = bloco 0x5E, 1 = escada: 3 na primeira celula do par e 4 na
    seguinte (o `CP 3` de 45ce e redundante; as duas ramificacoes fazem `LD A,3`).
    Devolve as celulas com codigo 3/4 (as escadas, que sao o que o autotile desenha).
    """
    off = RAM_MAP + (room - 1) * stride
    prev = 0
    ladders = []
    for n in range(ROOM_W * ROOM_H):
        v = (mem[off + n // 4] >> (6 - 2 * (n % 4))) & 3
        if v == 0:
            continue
        if v == 2:
            code = 5
        elif v == 3:
            code = 0x5E
        else:
            code = 4 if prev == 3 else 3
            ladders.append((n // ROOM_W, n % ROOM_W))
        prev = code
        rb.put(img, p1, code, (n % ROOM_W) * 8, (n // ROOM_W) * 8, False)
    return ladders


def golden_ladders(rom, mem, p1, img, room, codes):
    """4576/45A7: escadas douradas (RAM 0xE7C0, registos de 4 bytes: sala, y, x, altura|0x80).

    Repinta o mural na area (56DE/56F8, transparente) e desenha os blocos 1/2 por linha
    (4690 com a tabela 0x45C9). Devolve as celulas cobertas.
    """
    if mem[RAM_STATE] == 11:
        return []
    cells = []
    for i in range(16):
        e = RAM_GOLDEN + 4 * i
        if mem[e] == 0 or mem[e] != room:
            continue
        y, x, height = mem[e + 1], mem[e + 2], mem[e + 3] & 0x1F
        for r in range(height):
            for c in range(2):
                rb.put(img, p1, mural_code(codes, x + c * 8, y + r * 8), x + c * 8, y + r * 8, False)
        for r in range(height):
            rb.put(img, p1, GOLDEN_LEFT, x, y + r * 8, False)
            rb.put(img, p1, GOLDEN_RIGHT, x + 8, y + r * 8, False)
            cells.append((y // 8 + r, x // 8))
            cells.append((y // 8 + r, x // 8 + 1))
    return cells


def coffins(rom, mem, p1, img, room):
    """46A4: E2C0 (8 registos de 5 bytes). Tipos < 3 e da sala atual -> 3x2 em (x, y-8)."""
    cells = []
    for i in range(8):
        e = RAM_COFFINS + 5 * i
        if (mem[e] & 0x7F) < 3 and mem[e + 2] == room:
            cells += _icon(rom, p1, img, GFX_COFFIN, 3, 2, mem[e + 4], mem[e + 3] - 8)
    return cells


def soul_stones(rom, mem, p1, img, room):
    """651D: E700 (16 registos de 8 bytes: ativo, sala, y, x). 2x2 em (x, y)."""
    cells = []
    for i in range(16):
        e = RAM_STONES + 8 * i
        if mem[e] != 0 and mem[e + 1] == room:
            cells += _icon(rom, p1, img, GFX_STONE, 2, 2, mem[e + 3], mem[e + 2])
    return cells


def items(rom, mem, p1, img, room):
    """Itens da sala (E300, 8 bytes: tipo, y, x, sala, anim). 2x2 em (x, y).

    9116: o tipo escolhe o grafico na tabela 0x9328 + 4*(tipo-1) (0x911B descarta
    os tipos com bits altos, que ja foram apanhados).
    """
    cells = []
    for i in range(8):
        e = RAM_ITEMS + 8 * i
        kind = mem[e]
        if kind == 0 or kind & 0xF0 or mem[e + 3] != room:
            continue
        cells += _icon(rom, p1, img, GFX_ITEM + 4 * (kind - 1), 2, 2, mem[e + 2], mem[e + 1])
    return cells


def door(rom, mem, p1, img, room):
    """9010: porta 4x4 (frame E2F6) + os pregos da tabela 0xB9AD[largura] (banco 13)."""
    if mem[0xE2F3] != room or mem[0xE2F5] == 0:
        return []
    y, x, w, h, frame = mem[0xE2F1], mem[0xE2F2], mem[0xE2F4], mem[0xE2F5], mem[0xE2F6]
    cells = _icon(rom, p1, img, GFX_DOOR + 16 * frame, 4, 4, x, y)

    stud = 0xAE if mem[RAM_DOOR_STUD] else 0x60
    ptr = rom.word(DOOR_STUDS + 2 * w, ba=BANK_STUDS)
    p = rom.off(ptr, ba=BANK_STUDS)
    for k in range(h):
        v = rom.d[p + k]
        sx, sy = x + (v & 3) * 8, y + ((v & 12) << 1)
        rb.put(img, p1, stud, sx, sy, False)
        cells.append((sy // 8, sx // 8))
    return cells


def build(rom, mem, p1, pyramid, stage, room, with_objects=True):
    """Monta a sala inteira (mural + decoracoes + mapa + objetos) como o 44D4 a deixa.

    Devolve (imagem de indices de cor, codigos do mural, celulas de objeto).
    """
    img, codes = mural(rom, pyramid, stage, p1)
    rb.decorations(rom, pyramid, stage, room, p1, img)
    ladders = tile_map(rom, mem, p1, img, room)
    ladders += golden_ladders(rom, mem, p1, img, room, codes)
    obj_cells = []
    if with_objects:
        obj_cells += coffins(rom, mem, p1, img, room)
        obj_cells += soul_stones(rom, mem, p1, img, room)
        obj_cells += items(rom, mem, p1, img, room)
        obj_cells += door(rom, mem, p1, img, room)
    return img, codes, {"ladders": ladders, "objects": obj_cells}
