"""Extrai TODAS as salas do MSX1 passeando de um lado para o outro no emulador.

Uso (de dentro de tools/kv2-extract/work):
    python ../art/msx1_rooms_walk.py <rom> <saida_dumps_dir> [stage ...]

Para cada stage: entra pela senha, deixa a sala 1 na tela e depois:
  - segura "direita" por ~10 s, gravando a VRAM a cada 0,5 s com o numero da sala atual (E243);
  - volta segurando "esquerda" por ~12 s, gravando do mesmo jeito.
Cada sala por onde o Vick passa acaba num arquivo `sNN_rMM.bin` (a conversao so pega a ultima
gravacao de cada sala, que e' a mesma imagem).

Salas que so se chegam por escada (conexoes verticais) NAO entram neste passeio.
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from msx1_screens import convert  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.normpath(os.path.join(HERE, "..", "work"))


def run_stage(rom, stage):
    nn = "%02d" % stage
    tcl = open(os.path.join(WORK, "gen", "rooms_walk.tcl")).read()
    path = os.path.join(WORK, "gen", "rooms_walk_s%s.tcl" % nn)
    open(path, "w").write(tcl.replace("@@STAGE@@", str(stage)).replace("@@NN@@", nn))

    exe = os.path.join(WORK, "omsx", "openmsx.exe")
    subprocess.run([exe, "-machine", "C-BIOS_MSX2_JP", "-cart", rom,
                    "-script", "gen/rooms_walk_s%s.tcl" % nn], cwd=WORK, check=False)


def convert_stage(out_dir, stage):
    nn = "%02d" % stage
    rooms_dir = os.path.join(WORK, "msx1_rooms")
    saved = 0
    for name in os.listdir(rooms_dir):
        if not name.startswith("s%s_r" % nn) or not name.endswith("_vram.bin"):
            continue
        base = name[:-len("_vram.bin")]  # sNN_rMM
        vram = os.path.join(rooms_dir, base + "_vram.bin")
        regs = os.path.join(rooms_dir, base + "_regs.bin")
        if not (os.path.exists(vram) and os.path.exists(regs)):
            continue
        data = open(vram, "rb").read()
        if len(data) < 16384:
            continue
        os.makedirs(out_dir, exist_ok=True)
        open(os.path.join(out_dir, base + ".bin"), "wb").write(
            convert(data, open(regs, "rb").read()))
        saved += 1
    print("stage %s: %d sala(s)" % (nn, saved))


def main():
    rom = sys.argv[1] if len(sys.argv) > 1 else "rom/kv2_msx1.rom"
    out_dir = sys.argv[2]
    stages = [int(v) for v in sys.argv[3:]] or range(1, 61)
    for stage in stages:
        run_stage(rom, stage)
        convert_stage(out_dir, stage)


if __name__ == "__main__":
    main()
