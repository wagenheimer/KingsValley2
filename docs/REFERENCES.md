# References

All the sources used for the game design, the extraction and the clone.

## The game

| Source | What it has |
| :--- | :--- |
| [Wikipedia — King's Valley II](https://en.wikipedia.org/wiki/King's_Valley_II) | objective, 6 pyramids × 10 stages, editor, versions, contest |
| [Wikipedia — King's Valley](https://en.wikipedia.org/wiki/King%27s_Valley) | the first game (1985) |
| [Hardcore Gaming 101 — King's Valley](https://www.hardcoregaming101.net/kings-valley/) | KV1 and KV2: tools, enemies (white/golden mummies, statues, rocks), bridges, doors, stages with up to 6 screens and wrap-around, music, versions |
| [msxnet (BiFi) — King's Valley II](http://bifi.msxnet.org/msxnet/konami/kingsvalley2/) | story, F1/F2/F4/F5 keys, FESTIVAL/TRYAGAIN passwords, extra stages (puzzle/music), user stages `.elg` |
| [msxnet — password calculator](http://bifi.msxnet.org/msxnet/konami/kingsvalley2/pw-form) | the `…AMCN` family from the calculator **is accepted** by the ROM (measured in openMSX, Oct/2026 — `ROM_PORT_HANDOFF.md` §4.5 records the open question); the port writes the `6994`/`6A98` format, read from the code — see `StagePassword` |
| [msxnet — RC-760](http://www.bifi.msxnet.org/konami/gameinfo/rc760.html) | datasheet for the MSX1 version |
| [GameFAQs — review "Am I the only one…"](https://gamefaqs.gamespot.com/msx/918128-kings-valley-ii-the-seal-of-el-giza/reviews/59667) | single-use tools, reusable weapons, no jumping with an item, enemies |
| [GameFAQs — review "Tomb Raiding: Old-School Edition"](https://gamefaqs.gamespot.com/msx/918128-kings-valley-ii-the-seal-of-el-giza/reviews/163903) | depth of each tool |
| [GameFAQs — cheats](https://gamefaqs.gamespot.com/msx/918128-kings-valley-ii-the-seal-of-el-giza/cheats) | passwords, hidden stages |
| [Generation MSX — cheats (MSX2)](https://www.generation-msx.nl/software/konami/kings-valley-ii---the-seal-of-el-giza/cheats/1079/) | passwords |
| [Konami Wiki (Fandom)](https://konami.fandom.com/wiki/King's_Valley_II) | tools that are new compared to KV1 |
| [MobyGames](https://www.mobygames.com/game/19527/kings-valley-ii/) | datasheet |
| [MSX Games World](https://www.msxgamesworld.com/software.php?id=127) | datasheet, remakes |
| [MSX Translations — user levels](https://www.msxtranslations.com/userlevels/kv2.php) | user stages |
| [The Cutting Room Floor](https://tcrf.net/King's_Valley_II) | unused content (the page blocks automated access) |

## Video

| Source | Use |
| :--- | :--- |
| [King's Valley 2 – MSX2 – Long Play (YouTube)](https://www.youtube.com/watch?v=p_avntvfYvM) | **MSX2 visual reference** (3h07): all 60 stages, menus, HUD, ending — source of the clone's graphics |

## Reverse engineering and related projects

| Source | What it has |
| :--- | :--- |
| [0xe1f/KingsValley2](https://github.com/0xe1f/KingsValley2) | annotated, reassemblable **KV2 MSX2** disassembly; sound driver in `banks_456.asm`, sound callers in `banks_0.asm`, `banks_123.asm` and `banks_abc.asm`. Used to validate extraction state and sound names; caller logic takes precedence over unverified comments. |
| [GuillianSeed/Kings-Valley](https://github.com/GuillianSeed/Kings-Valley) | annotated Z80 disassembly of **KV1** by Manuel Pazos (RC-727: hardware formulas, Name Table `#3800`, sprite masks, player states) |
| [antxiko/KingsValley-disassembly](https://github.com/antxiko/KingsValley-disassembly) | commented disassembly of **KV1** (the series engine: player states, holes, mummies by type/speed, `busca_camino`, fall curve) |
| [msx.org — KV2 MSX2: C11/SDL2 reimplementation + editor](https://www.msx.org/forum/msx-talk/development/king%E2%80%99s-valley-ii-msx2-%E2%80%93-source-faithful-c11sdl2-reimplementation-and-mode) | 60 stages / **154 rooms**, 2343 bytes per stage, up to 6 rooms in a grid, 4 enemy types, objects |
| KV2 local ROM disassembly | in `tools/kv2-extract/work/` (`dis_b0.txt`, `dis_b1.txt`, `dis_b23.txt`): full bank-by-bank disassembly of the **KV2** cartridge (`kv2_msx2_scc.rom` / RC-760 / RC-761) |
| [divby00/kings-valley (Godot)](https://github.com/divby00/kings-valley) | KV1 remake |
| [hanulso78/kings-valley-vga](https://github.com/hanulso78/kings-valley-vga) | KV1 VGA remaster |

## Tools

| Tool | Link |
| :--- | :--- |
| openMSX 21.0 (emulator, Tcl, `vgm_rec`) | <https://github.com/openMSX/openMSX/releases> · <https://openmsx.org> |
| C-BIOS (free BIOS, bundled with openMSX) | <https://cbios.sourceforge.net> |
| yt-dlp (longplay download) | <https://github.com/yt-dlp/yt-dlp> |
| OpenCV (Python) | <https://pypi.org/project/opencv-python-headless/> |
| z80dis (Z80 disassembler) | <https://pypi.org/project/z80dis/> |
| soundfile / libsndfile (OGG Vorbis) | <https://pypi.org/project/soundfile/> |
| VGM format | <https://vgmrips.net/wiki/VGM_Specification> |

## MSX hardware

| Source | Use |
| :--- | :--- |
| [MSX2 Technical Handbook — VDP V9938](https://github.com/Konamiman/MSX2-Technical-Handbook/blob/master/md/Chapter4a.md) | screen modes, tables, mode 2 sprites (basis of `debug/vdp.py`) |
| [MSX Assembly Page — MSX I/O ports overview](https://map.grauw.nl/resources/msx_io_ports.php) | I/O ports (VDP, PSG, PPI) |
| [MSX Wiki — Konami 051649 (SCC)](https://www.msx.org/wiki/Konami_SCC) | sound chip / cartridge mapper |
