"""Extrai as salas do MSX1 (SCREEN 2) para Content/RomMsx1/Rooms/dumps/sNN_rM.bin.

Uso (de dentro de tools/kv2-extract/work):
    python ../art/msx1_rooms_dump.py <rom> <saida_dumps_dir> [stage ...]

Para cada stage: entra pela senha (stage2.tcl), espera a sala 1 aparecer e dumpar a VRAM,
depois converte para o formato .bin do port. Hoje pega a **sala 1** de cada stage; as demais
exigem andar até as bordas (a grade de salas está no stage JSON).

Requer: openmsx em `omsx/` e o script `gen/room_msx1.tcl` (este script gera por stage).
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from msx1_screens import convert  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.normpath(os.path.join(HERE, "..", "work"))

TCL = """after time 50 exit
set ::stage {stage}
set ::dumpdir [file normalize msx1_rooms]
file mkdir $::dumpdir
source stage2.tcl
proc vd {{name}} {{
  set f [open "$::dumpdir/${{name}}_vram.bin" wb]
  puts -nonewline $f [debug read_block "physical VRAM" 0 16384]
  close $f
  set f [open "$::dumpdir/${{name}}_regs.bin" wb]
  puts -nonewline $f [debug read_block "VDP regs" 0 [debug size "VDP regs"]]
  close $f
}}
at 34 {{vd s{nn}}}
at 35 exit
"""


def dump_stage(rom, out_dir, stage):
    nn = "%02d" % stage
    tcl_path = os.path.join(WORK, "gen", "room_s%s.tcl" % nn)
    open(tcl_path, "w").write(TCL.format(stage=stage, nn=nn))

    exe = os.path.join(WORK, "omsx", "openmsx.exe")
    cmd = [exe, "-machine", "C-BIOS_MSX2_JP", "-cart", rom, "-script", "gen/room_s%s.tcl" % nn]
    subprocess.run(cmd, cwd=WORK, check=False)

    vram = open(os.path.join(WORK, "msx1_rooms", "s%s_vram.bin" % nn), "rb").read()
    regs = open(os.path.join(WORK, "msx1_rooms", "s%s_regs.bin" % nn), "rb").read()

    os.makedirs(out_dir, exist_ok=True)
    open(os.path.join(out_dir, "s%s_r1.bin" % nn), "wb").write(convert(vram, regs))
    print("ok stage", stage)


def main():
    rom = sys.argv[1] if len(sys.argv) > 1 else "rom/kv2_msx1.rom"
    out_dir = sys.argv[2]
    stages = [int(v) for v in sys.argv[3:]] or range(1, 61)
    for stage in stages:
        dump_stage(rom, out_dir, stage)


if __name__ == "__main__":
    main()
