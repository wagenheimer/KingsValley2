# kv2-extract — dados do King's Valley II a partir da SUA ROM

Gera `src/KingsValley.Desktop/Content/Rom/` (fases, arte MSX2, áudio). **Nada disso é versionado**
(está no `.gitignore`) — ver o aviso legal em [docs/ROM_EXTRACTION.md](../../docs/ROM_EXTRACTION.md),
que também explica os formatos e o porquê de cada passo.

## Requisitos

- ROM **MSX1 RC-760** (SHA-1 `cfd872d0…0415d`) — fases, lógica e som.
- **openMSX 21** portátil ([releases](https://github.com/openMSX/openMSX/releases)) — usa a C-BIOS livre.
- Python 3.12+ com `opencv-python-headless numpy z80dis soundfile yt-dlp` (num venv).
- Para a arte: o longplay MSX2 (`yt-dlp -f 298 https://www.youtube.com/watch?v=p_avntvfYvM`).

## Pasta de trabalho

Crie `tools/kv2-extract/work/` (ignorada pelo git) com:
`omsx/` (openMSX), `rom/kv2_msx1.rom`, `video/kv2.mp4`, e copie os scripts de `emulator/`, `levels/`,
`audio/`, `art/` e `debug/` para lá. Rode tudo **de dentro** de `work/` com **`python` (sem `-I`)**:
o modo isolado (`-I`) tira o `site-packages` do usuário (onde estão `numpy`/`cv2`) **e** a pasta do
próprio script do `sys.path` — e aí `import numpy` e `import vdp`/`rom_sprites` falham.

## Passos

```bash
# 1. Fases: um dump de RAM/VRAM por stage (entra pela senha) + extração
for s in $(seq 1 60); do printf 'set ::stage %d\nsource grab.tcl\n' $s > g$s.tcl
  omsx/openmsx.exe -machine C-BIOS_MSX2_JP -cart rom/kv2_msx1.rom -script g$s.tcl; done
python extract.py out_levels                       # -> out_levels/stageNN.json
python extract.py ../../../src/KingsValley.Desktop/Content/Rom/Levels

# 2. Som: cada id tocado na tela de senha (snd.tcl = músicas, sfx.tcl = efeitos)
printf 'set ::id 5\nset ::dur 150\nsource snd.tcl\n' > id5.tcl   # e assim por diante
# os VGMs vão para Content/Rom/Audio/Vgm/{Music,Sfx}; o pacote .msxaudio é montado pelo CLI
# do Wagenheimer.MsxAudio, que lê ids e loops do audio.json (nenhuma ferramenta local)
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- \
    import src/KingsValley.Desktop/Content/Rom/Audio/Vgm \
    src/KingsValley.Desktop/Content/Rom/Audio/audio.msxaudio \
    --manifest src/KingsValley.Desktop/Content/Rom/Audio/audio.json --title "King's Valley II"

# 3. Arte MSX2 do longplay
python vindex.py 2.0      # índice de frames nativos (vframes.npz)
python vmatch.py          # casa frames com as salas
python vart2.py           # tiles, murais e objetos
python vsprites_all.py && python sprcolor.py
for n in vick white gold red; do python sprmerge.py $n; done
python hud_build.py && python font_build.py m9/p29 && python screens_build.py ../../../src/KingsValley.Desktop/Content/Rom/Art
# telas da abertura (pirâmide + mapa "DO YOUR BEST !!") dos dumps de VDP do emulador
# (gen/intro_probe.tcl, gen/intro_frames.tcl, gen/intro_frames2.tcl -> work/intro/)
python ../art/intro_build.py ../../../src/KingsValley.Desktop/Content/Rom/Art/Screens
# sprites e objetos EXATOS da ROM MSX2 (substituem os personagens do longplay)
python rom_sprites.py rom/kv2_msx2_scc.rom art2/romspr
python rom_tiles.py art2/romspr pyr/s1 pyr/s11 pyr/s21 pyr/s31 pyr/s41 pyr/s51   # dumps do 1º stage de cada pirâmide
python art_build.py ../../../src/KingsValley.Desktop/Content/Rom/Art
```

Depois: `dotnet build KingsValley.slnx` (copia `Content/Rom` para o output) e
`dotnet test` (o teste `RealStages_LoadWhenExtracted` carrega os 60 stages).

## Scripts

| Pasta | Script | Faz |
| :--- | :--- | :--- |
| `emulator/` | `lib.tcl` | dumps (VRAM/paleta/regs/RAM), teclas, agenda |
| | `stage.tcl` / `stage2.tcl` | navega o menu e digita a senha de `::stage` (MSX1 / MSX2) |
| | `grab.tcl` | dump logo após a carga do stage (breakpoint em `43D3`) |
| | `music.tcl` | grava VGM+WAV do stage |
| | `snd.tcl` / `sfx.tcl` | toca o som `::id` (injeta `CALL 432E`) e grava |
| `levels/` | `extract.py` | fases → JSON (ROM + RAM) |
| | `kv2.py` | leitura das tabelas da ROM |
| `art/` | `vindex.py` `vmatch.py` `vart2.py` | frames nativos, casamento, tiles/murais/objetos |
| | `vsprites*.py` `sprcolor.py` `sprmerge.py` | sprites dos personagens |
| | `hud_build.py` `font_build.py` `screens_build.py` | HUD, fonte A–Z, telas |
| | `rom_sprites.py` | Vick, inimigos e projéteis **direto da ROM** (emula as rotinas do jogo) |
| | `rom_tiles.py` | itens e sarcófago dos blocos 8x8 da página 1 (dump por pirâmide) |
| | `art_build.py` | atlas `kv2` + fundos |
| `debug/` | `vdp.py` `dsheet.py` `sprites.py` `z80dasm.py` | render de dumps, folhas, desmontagem |
