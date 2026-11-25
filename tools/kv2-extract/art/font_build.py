"""Fonte do jogo (A-Z) lida da tabela de padroes do VDP na tela de senha (dump do MSX1) -> art2/hud/font_X.png."""
import sys
import numpy as np
import cv2

prefix = sys.argv[1] if len(sys.argv) > 1 else "m9/p29"
V = open(prefix + "_vram.bin", "rb").read()
R = open(prefix + "_regs.bin", "rb").read()
pgen = (R[4] & 0x3C) << 11
bank = 1 * 0x800  # linha 12 da tela = banco 1 do modo G2
for i in range(26):
    tile = 0xE1 + i
    img = np.zeros((8, 8, 4), np.uint8)
    for y in range(8):
        bits = V[pgen + bank + tile * 8 + y]
        for x in range(8):
            if bits & (0x80 >> x):
                img[y, x] = (255, 255, 255, 255)
    cv2.imwrite(f"art2/hud/font_{chr(65 + i)}.png", img)

# 0xDB = '!' — a mesma linha/banco da tabela de padroes; o "NICE !" do fim de stage usa esse codigo.
img = np.zeros((8, 8, 4), np.uint8)
for y in range(8):
    bits = V[pgen + bank + 0xDB * 8 + y]
    for x in range(8):
        if bits & (0x80 >> x):
            img[y, x] = (255, 255, 255, 255)
cv2.imwrite("art2/hud/font_excl.png", img)
print("ok")
