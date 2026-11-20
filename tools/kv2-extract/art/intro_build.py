"""Monta as telas da cutscene de abertura a partir dos dumps do emulador.

Uso (de dentro de tools/kv2-extract/work):
    python ../art/intro_build.py ../../../src/KingsValley.Desktop/Content/Rom/Art/Screens

Entradas (work/intro/, dumps do openMSX; ver gen/intro_*.tcl):
    pyramid_vram.bin / pyramid_pal.bin  - cena da piramide (t=27 s) SEM o Vick (ele e sprite)
    msg_vram.bin     / msg_pal.bin      - mapa do vale com "DO YOUR BEST !!" (t=34 s)

Saidas:
    <saida>/pyramid.bin   - 256x212, MSX2 Screen 5 (fundo limpo, paleta real)
    <saida>/intro_msg.bin - 256x212, mapa + mensagem (ja estampada pelo ROM)

A versao anterior destas telas vinha do longplay (art/screens_build.py): o frame do video
tinha o Vick "assado" na imagem e a paleta do video nao e a do VDP (o ceu saia marrom em vez
de preto), por isso elas foram refeitas do dump de VRAM. O Vick e' sprite (SAT em 0xF600,
lido tambem como E800 na RAM): o port desenha as poses do proprio jogo (vick_s0_03..05)
sobre o fundo e reproduz o caminho/tempo medidos no emulador.
"""
import os
import struct
import sys

W, H = 256, 212


def screen_bin(vram, pal, has_alpha=False):
    """Empacota o bitmap 4bpp + paleta VDP no formato do ScreenAssembler."""
    header = bytearray(pal)
    header += struct.pack("<HH", W, H)
    header.append(1 if has_alpha else 0)
    header.append(0)
    return bytes(header) + vram[: (W * H) // 2]


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)

    pyr_vram = open("intro/pyramid_vram.bin", "rb").read()
    pyr_pal = open("intro/pyramid_pal.bin", "rb").read()
    msg_vram = open("intro/msg_vram.bin", "rb").read()
    msg_pal = open("intro/msg_pal.bin", "rb").read()

    open(os.path.join(out_dir, "pyramid.bin"), "wb").write(screen_bin(pyr_vram, pyr_pal))
    open(os.path.join(out_dir, "intro_msg.bin"), "wb").write(screen_bin(msg_vram, msg_pal))
    print("ok:", out_dir)


if __name__ == "__main__":
    main()
