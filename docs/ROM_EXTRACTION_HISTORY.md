# Historical ROM extraction research — levels, logic, sound and graphics

> **Current-status notice (2026-11-24):** This document preserves earlier reverse-engineering observations and experiments. Its historical cache paths, fallback recipes, source-version assumptions and completion claims are not current operating instructions. Start with [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md), [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md) and [AUDIO_EXTRACTION_AUDIT.md](AUDIO_EXTRACTION_AUDIT.md). Exact cartridge identity and bank mapping govern every routine address. Production extraction is standalone and never requires openMSX.

> How the original's data was taken from the ROM and the longplay, with addresses, formats and the
> stumbles (so as not to repeat them). The tools are in [tools/kv2-extract](../tools/kv2-extract/README.md).
> Links in [REFERENCES.md](REFERENCES.md).

The game now generates an offline, English [extraction report](EXTRACTION_REPORT.md) after each extraction attempt. The report distinguishes available files from verified extraction and identifies incomplete graphics provenance. See the [graphics audit](GRAPHICS_EXTRACTION_AUDIT.md) for current repairs, validation evidence and missing native families.

## ⚖️ Legal notice (read first)

The levels, graphics, music and effects of King's Valley II are **Konami's property**. The tools
read the ROM and write to `src/KingsValley.Desktop/Content/Rom/`. **While the repository is private**
it versions that content and the working material (`tools/kv2-extract/work/`, with the base ROMs).
The **open-source version** will be assembled **without** `Content/Rom/`, without `tools/kv2-extract/work/` and without ROMs,
recordings or dumps — only the clone's code (C#) and the tools, which are ours.

> 🚫 **MANDATORY RULE (Zero PNGs for backgrounds/screens):**
> No PNG file is used as a room background or a static screen (`Screens`).
> Game rooms are assembled 100% at runtime by `RoomAssembler` from `Content/Rom/Rooms/` (`blocks_p*.bin`, murals, decorations and palettes).
> Screens (`world_map`, `title`, `start_select`, `pyramid`, `sound_select`, `puzzle`, etc.) are assembled at runtime by `ScreenAssembler` from `.bin` files in the native MSX2 Screen 5 format (32-byte VDP palette + dimensions + packed 4bpp data).

---

## 1. The ROMs

| File (user's Downloads) | SHA-1 | openMSX status | Use |
| :--- | :--- | :--- | :--- |
| `King's Valley II … (JP)[RC-760].rom` (MSX1, 128 KB) | `cfd872d005b7bd4cdd6e06c4c0162191f0b0415d` | **GoodMSX** (original dump) | **levels, logic, sound** |
| `King's Valley II … (JP)[SCC][RC-761].rom` (MSX2, 128 KB) | `f25628e474035e6bf0440b274c5df04f94c49fb8` | **BAD Dump** | does not boot on C-BIOS |
| `King's Valley II … [cr Sigma][SCC].dsk` (720 KB) | `05aef43e408bff9fb1c1c74182397ab2b4519f8b` | disk crack | needs a disk BIOS (not available) |

**The MSX2 dump is a conversion to the ASCII8 mapper**: the original switches banks at `7000/9000/B000`
(Konami SCC); this one writes at `68FF/70FF/78FF` (`ASCII8`). Even with `-romtype ASCII8` and the boot
bypass (below) it hangs — that is why the **MSX2 graphics come from the longplay** (section 7).

## 2. Emulator

- **openMSX 21.0** (portable) with the free **C-BIOS** (`-machine C-BIOS_MSX2_JP`) — no Konami BIOS.
- **Where it is here:** `tools/kv2-extract/work/omsx/` (outside git) — openMSX **21.0**, from the file
  `openmsx-21.0-windows-vc-x64-bin.zip` in the GitHub releases (the `README` and handoff §2 ask for
  `omsx/openmsx.exe`). Verified on **2026-10-17**: it boots `kv2_msx1.rom` and `kv2_msx2_scc.rom`
  (`-romtype KonamiSCC`), and the dumps render with `python vdp.py` (stage 6 via the password → HUD `STAGE 06`).
- ⚠️ **In PowerShell `openmsx.exe` is a GUI app:** `& .\omsx\openmsx.exe ... -script x.tcl` **does not
  wait** for the process — the harness kills the run midway and the dumps come out empty (it looks like "it did
  not work", but it did). Use `Start-Process -Wait -PassThru` and, preferably, put an
  `after time N exit` on the **first** line of the script: a Tcl error midway does **not** terminate the
  emulator, it stays open (that is what hung the first test).
- Without a window and in turbo: `set renderer none` + `set throttle off` (the `after time` uses
  emulated time). With `renderer none`, **`after frame` does not fire** → use `after time 0.0166833`.
- Dumps: `debug read_block "physical VRAM"|"VDP palette"|"VDP regs"|"memory"`; the debuggable
  `"<cartridge> romblocks"` tells which bank is at each address (essential for finding data).
- Keyboard: `keymatrixdown/up <row> <mask>` (space 8/01, ←8/10, ↑8/20, ↓8/40, →8/80,
  RET 7/80, BS 7/20) and `type <letter>`. **Typing fast loses letters** → one letter every 0.6 s.
- When recording sound, `vgm_rec` saves by default in **`Documents/openMSX` (OneDrive!)** — redirect it
  with `set ::vgm::directory` *after* the first `vgm_rec prefix` (the namespace only exists there).

### The boot (the `H.STKE` trap)

The cartridge's `INIT` (`40A3`) **does not start the game**: it installs the **`H.STKE` (`FEDA`)** hook pointing
to `40B5`, which the Konami BIOS calls after initializing all cartridges. **C-BIOS does not call
`H.STKE`**. With the MSX1 (RC-760) that is not a problem; with the MSX2 dump the game hangs.

## 3. Level format (MSX1 ROM, banks 10–13)

| Data | Where | Format |
| :--- | :--- | :--- |
| **Terrain** pointers | `0x14000` (CPU `6000`, 60 words) | CPU address with bank 10 at `6000` |
| **Terrain** | `0x14168…~0x18121` | **RLE**: each byte = `value(top 2 bits) | count(6 bits)`; `00` terminates. Output = 2-bit cells, 4 per byte, written to `E900` (192 bytes = 32x24 per room) |
| **Ladders** | pointers at CPU `6078` | 2-byte records `E,D`: `DE>>7` = byte in the room, `(E>>5)&3` = cell, `E&31` = length; `FF` = next room; `00` terminates. Writes the value 1 in 2 columns |
| **Golden ladders** | pointers at CPU `60F0` | same format; they also feed list `E7C0` |
| **Items** | pointers at CPU `AFB1` (bank 13 at `A000`) | 3 bytes `(room<<4|type), y, x`; `FF` terminates |
| **Room grid** | CPU `AB5A` + 8·(stage−1) (bank 13) | 8 bytes = 8 rows of bitmask; rooms numbered in reading order |

Decompressor: `43D3` (stage = `E242`); ladders: `441E`; items: `9E1F`; grid: `5BA3`.

**Validation:** decompressed rooms = the grid's rooms across all 60 stages; total **154 rooms**, equal to the number
published on msx.org. The editor's file format (`.elg`) has **2343 bytes** (examples on msxnet).

## 3.1 Stage state: running the game on the machine (no external emulator)

The tables in §3 give terrain, ladders, items and grid. The rest (soul stones, door, crates, mechanisms
and Vick's start) **the game writes to RAM when loading the stage** — before, that came from an openMSX dump.

It is already possible to get that **in process**, driving the game itself through the password screen (the same
navigation as `tools/kv2-extract/emulator/stage.tcl`): boot the ROM on the package's minimal machine
(`Wagenheimer.MsxAudio.Extraction.MiniMsx`), keys on the MSX matrix (row/mask), password and `RET`.

- Password: `l0 l1 s0 s1` where `l` = tens/units of lives (99 → `J J`) and `s = stage − 1` in hex
  (`A` = 0, `B` = 1…), followed by `AMCN`. For `stage 2` → `JABJAMCN` (confirmed: loads stage 2).
- Navigation: `8/0x01` (space) at 12 s and 16 s, `8/0x40` (down) at 19 s, `8/0x01` at 20.5 s,
  `8/0x10` (left) at 22 s, password digits every 0.6 s from 23 s, `7/0x80` (RET) at 28.5 s.
- After that the RAM has the objects: `E700` (soul stones: flag, room, y, x), `E2C0` (crates),
  `E2F0` (door), `E280` (Vick) and `E900` (decompressed terrain).

Measured for **stage 2** (RAM dump × `stage02.json`): the four soul stones match exactly
(`y/x` = 8/104, 48/144, 80/80, 112/160), the crate matches (room 1, 144/104) and the door matches in position and
width; only `playerStart` differs because the JSON was read a few ticks after loading (Vick had already moved).

## 4. RAM map (during the game, MSX1)
| Address | Content |
| :--- | :--- |
| `E242` | current stage (1..60) · `E241` pyramid · `E243` current room |
| `E280..E28F` | player: `E280` state, `E282` y, `E284` x, `E285` frame, `E288` direction |
| `E2C0` | sarcophagi: `state, timer, room, y, x` (8 bytes) |
| `E2F0` | exit door: `?, y, x, room, width, height` |
| `E300` | items/objects in play (8 bytes: `type, y, x, room, …`) |
| `E500` | active enemy (instance) |
| `E600` | dynamic stage objects (16 bytes: `type, ?, y, x, room, …`) |
| `E700` | soul stones: `flag, room, y, x` (8 bytes) |
| `E780` | 8x8 grid of rooms (copy of the bitmask) |
| `E7C0` | active golden ladders |
| `E900` | decompressed terrain (192 bytes per room) |
| `E132…E17A` | sound driver channels |

## 5. Measured physics

Script `emulator/` + `debug/`: enters stage 1, holds keys and records `E280..E28F` every
frame. Results in [GAME_DESIGN.md §3.2](GAME_DESIGN.md#32-explorer-movement).

## 6. Sound — VGM + chip emulator (not a recording)

**The game does not play an emulator recording.** The source is the **VGM** — the exact log of the PSG/SCC
register writes — and the audio is **rendered** by the package's chip emulator
(`Wagenheimer.MonoGameHelper.Audio`). That eliminates the three defects of the old path: a click at the
loop seam, codec artifacts and inconsistent levels between tracks.

### 6.1 Capture (VGM only)

1. **Play any sound:** inject `CALL 432E` with `A = id` (push the PC + `reg pc 0x432E`) — the driver
   lives at `6003` (banks 4–6). Do it on the **password screen**: the title restarts the sound on each demo
   cycle.
2. **Record:** `vgm_rec start PSG SCC` (exact log). The old path's `record start -audioonly`
   **is no longer used** (the WAV was only for comparison; see §6.5).
3. **Clean effects:** send id **1** (stop) **0.5 s before** the effect — `sfx.tcl` already does that and
   it is what guarantees the effect's VGM does not include the background music.
4. **Who is who:** breakpoint at `432E` logging `A` during a scripted match.
5. **Music per stage:** driver bytes repeat in pairs with period 10 → `((stage-1)%10)/2` (ids 5–9).

> ⚠️ Long music: record **with room to spare** (≥ 2× the loop). The loop of `stage_e` (id 9) only appears
> clearly in a **300 s** capture (`vgm_long/long_id09…vgm`); with 120 s the detector saw a partial
> stretch of 28.8 s (it was the wrong loop the port used to play).

### 6.2 Exact loop (detection in the write sequence)

The sound driver repeats the music **restarting the data**, so the write sequence repeats
letter by letter. `VgmLoopFinder` looks for that repetition and returns `start + duration` with sample
precision:

- it requires the repetition to cover **at least one full period** (`run >= k`);
- if the capture ends early, it accepts when the repeated stretch covers ≥ 90% of the period;
- `MsxAudioTool import --manifest` writes the result into the package itself
  (`audio.msxaudio`), using `audio.json` as the table of ids and of the loop intent.

| Track | id | loop |
| :--- | :--: | :--- |
| `title` | 3 | no (jingle) |
| `menu` | 17 | 0.319 s + 8.544 s |
| `stage_a` | 5 | 0.202 s + 32.041 s |
| `stage_b` | 6 | 10.548 s + 30.706 s |
| `stage_c` | 7 | 0.250 s + 44.858 s |
| `stage_d` | 8 | 1.238 s + 28.837 s |
| `stage_e` | 9 | 0.320 s + **67.287 s** |
| `music_10` | 10 | 28.488 s + 38.450 s |
| `music_13` (map) | 13 | 0.252 s + 29.905 s |
| `ending` | 14 | no (jingle) |
| `music_18` | 18 | 0.009 s + 21.361 s |

### 6.3 Render (chip emulator)

The emulator lives in the **`Wagenheimer.MsxAudio`** package (see §6.6) and emulates the cartridge's two chips:

| Chip | Implementation | Detail |
| :--- | :--- | :--- |
| **PSG AY-3-8910** | faithful port of **emu2149** (the same core as VGMPlay) | 3 channels, 17-bit LFSR noise, 16 envelopes, volume table measured from the AY |
| **Konami SCC (K051649)** | faithful port of the MAME/libvgm core | 5 channels of 32-byte wavetable, 16.16 phase accumulator, key-on at `0x8F` |

- internal render at **`rate × oversampling`** (4× by default) and decimation by a **windowed-sinc FIR with a
  Blackman window** → no aliasing from sampling a square wave at 44.1 kHz;
- **high-pass** at ~12 Hz removes the chip's DC offset (the AY delivers a continuous level with the channel off);
- PSG clock **1,789,772.5 Hz** and SCC clock **1,789,772.5 Hz** (in VGM, the SCC uses half of the
  "classic" clock — `f = clock / (16 × (period + 1))`, checked against the capture: 670 Hz for
  register 166).

### 6.4 Mix profiles (`AudioProfile`)

| Profile | What changes |
| :--- | :--- |
| `original` (default) | faithful to the hardware: the 8 channels centered, only the DC cut |
| `stereo` | PSG at −30/0/+30% and SCC at −55/−25/0/+25/+55% + 16 kHz low-pass |
| `remaster` | 8× oversampling, wider pan, 17 kHz low-pass, short reverb and peak normalization |

Choose with `KINGSVALLEY_AUDIO_PROFILE=original|stereo|remaster` (or `GameAudio.SetProfile`).

### 6.5 Validation against openMSX

Comparison with a fresh capture of the same VGM (the old `audio_build` is no longer used):

| Measure | Result |
| :--- | :--- |
| **LTAS** (shape of the average spectrum) | correlation **0.998**, uniform deviation of **−3.4 dB** (gain; fixed in `DefaultMasterGain = 1/27000`) |
| **Amplitude envelope** | correlation **0.95** (constant offset of ~8 ms = openMSX `record` latency) |
| **Loop seam** | jump of **0.025** in `stage_a` against a typical delta of **0.047** (before: **0.360** in `stage_c`) |
| **Peak** | 0.392 in the render × 0.405 in the capture (≈0.3 dB) |

> *Sample-by-sample* correlation with the capture is not usable as a metric: openMSX's `record`
> resamples and delays, and any fraction of a millisecond misaligns a square wave. Spectrum and
> envelope are the stable measures.

### 6.6 Tools

The chip emulator, the VGM reader and the playback engine live in the
**`Wagenheimer.MsxAudio`** package (`K:\Games\Open Source\MsxAudio`) — see the README there (and
`docs/EXTRACTION.md` for the extraction pipeline). The project uses:

| Piece | Where | Role |
| :--- | :--- | :--- |
| `Wagenheimer.MsxAudio` | package | chips, VGM, loop, render, `.msxaudio` package, engine (streaming) and extraction via openMSX |
| `Wagenheimer.MsxAudio.MonoGame` | package | `MsxAudioPlayer` (DynamicSoundEffectInstance) used by the game |
| `MsxAudioTool` (the package's CLI) | `K:\Games\Open Source\MsxAudio\tools\MsxAudioTool` | `import` (VGM → package), `render`, `verify`, `extract`/`capture`/`scan` (ROM) |
| `Content/Rom/Audio/audio.msxaudio` | generated content | 11 songs + 46 effects (compressed VGMs, ~1 MB) |
| `Content/Rom/Audio/audio.json` | build input | id of each sound in the ROM + "loops" — read by `import --manifest` |

```bash
# pipeline de conteúdo: VGMs -> pacote .msxaudio (ids e loops vêm do audio.json)
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- \
    import src/KingsValley.Desktop/Content/Rom/Audio/Vgm \
           src/KingsValley.Desktop/Content/Rom/Audio/audio.msxaudio \
    --manifest src/KingsValley.Desktop/Content/Rom/Audio/audio.json --title "King's Valley II"

# render de uma música/efeito do pacote (WAV 16 bits) com perfil
# (id = chave do audio.json, ex.: stage_a, jump)
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- \
    render src/KingsValley.Desktop/Content/Rom/Audio/audio.msxaudio stage_a saida.wav --profile stereo

# lista o que está no pacote (ids, durações, loops)
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- \
    info src/KingsValley.Desktop/Content/Rom/Audio/audio.msxaudio

# comparação numérica com uma captura do openMSX (correlação/alinhamento/RMS)
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- verify <vgm> <captura.wav>
```

> ⚠️ **Loop detection only on music.** A short effect also "repeats writes" and the detector
> can find a false loop (on `jump` it found 0.384 s + 5.807 s) — then the render cut the whole
> sound. In `import --manifest`, only the `music` entries go through loop detection (and those
> marked `"loop": false`, like `title` and `ending`, stay loopless); `sfx` effects never.

The game plays **only** the package, through the chips: the OGG/`AudioManager` path no longer exists (it was a
fallback from when the port still used recordings).

### 6.7 MSX2 (RC-761): attempt and limit

The MSX2 dump only runs after being **de-converted** to Konami SCC (see §7b), and in it the sound capture comes out
**with no SCC writes at all** (PSG only) — C-BIOS/openMSX does not expose the SCC for that cartridge. Since the
MSX2 music is precisely the SCC remix, the capture would end up **worse** than the MSX1 one. That is why the port
keeps the MSX1 sound (complete, PSG+SCC). To revisit: it needs a good MSX2 dump (the current one is
**BAD Dump**) and an emulator that exposes that cartridge's SCC.


## 7. MSX2 graphics (from the longplay)

The 720p MSX2 longplay has an exact **scale 3**: usable area `x 96..863`, `y 42..677` = 256x212.

1. **Frame index** (`art/vindex.py`): 1 frame every 2 s, median of each 3x3 block, colors
   quantized to the MSX2's 9 bits.
2. **Frame ↔ room matching** (`art/vmatch.py`): Fisher score between the extracted terrain (wall /
   diggable / empty) and the average colors per cell → the timestamps grow with the stage (1-1 at 106 s … 60-x
   at ~10,800 s).
3. **Tiles and murals** (`art/vart2.py`): per-pixel mode of the cells of each class (4 variants 8x8 per
   pyramid); mural per room with holes filled by the pattern's period.
4. **Objects** (`art/art_build.py`): crop at the known positions; transparency = difference
   against the room's own mural.
5. **Characters** (`art/vsprites*.py`, `sprcolor.py`, `sprmerge.py`): difference against the room's
   "mode" → components → grouping by key color (cyan Vick, white mummy, golden, red) →
   **merging instances of the same pose** (a pixel only goes in if it appears in ≥30% of them).
6. **HUD and font**: HUD band and digits from the video; **A–Z font** read from the VDP pattern table
   on the password screen (tile = `0xE0 + letter`).
7. **Atlas** `kv2.png/.atlas` (Nez format, padding 2 + extrusion) + murals in `Backgrounds/` +
   screens in `Screens/`.

The MSX1 sprite patterns do **not** appear in the MSX2 ROM (other drawings or compressed).

## 7b. **Exact** MSX2 sprites and objects, read from the ROM (they replace the longplay ones)

The MSX2 ROM (`kv2.rom`, ASCII8 mapper) runs on openMSX after being **de-converted to Konami SCC**
(`kv2_msx2_scc.rom`: bank writes `32 FF 68/70/78` → `32 00 70/90/B0`). The RAM map is the same as
the MSX1's. With I/O watchpoints (ports 0x98/0x9B) and breakpoints we found the routines and **emulated them in
Python** (`art/rom_sprites.py`, `art/rom_tiles.py`), without depending on video:

| Routine (CPU) | What it does |
| :--- | :--- |
| `4E21`/`4E27` | RLE decompressor → VRAM (n<0x80 repeats; n&0x7F literals; 0x80 new address; 0 end) |
| `4E60`/`4E6C` | copies 16x16 patterns VRAM→VRAM **mirroring** (the MSX does not mirror in hardware) |
| `5029` | VDP HMMM command (source H,L+page A&3 → destination D,E+page A>>2) |
| `58DB` | assembles Vick's poses on page 1 (lines 192–207) per item in hand `E287` (table `0x858B`) |
| `5886` | copies pose `E285` (tables `0x869C` top / `0x86B8` bottom) to patterns 0–3 on every change |
| `5928` / `5937` | special poses: fall/death `E298` (`0x953D`) and door `E24C` (`0xAB59`+`0xAA00`) |
| `594F` + `5696` | fixed patterns F880–FFFF (`0x97A1`; lists `0x98C9` copy and `0x98EA` mirror/flip) |
| `6664` | enemy SAT: frame `IX+0x11` → pattern pair at `0xBEED` (bank 13); colors `IX+1E/1F` |
| `ACE4` | colors per type (table `0xAD0B`): types 1/3/4 `0B`+`4C`, type 2 `0A`+`47` (CC bit = OR → black outline, color 15) |
| `9116` / `5219` | items on the floor: 4 8x8 blocks of page 1 (table `0x9328`), code n → (n&31)·8, (n>>5)·8, transparent |
| `AD53` | sarcophagus 16x24 (blocks `0x93B2`), drawn **8 px above** the record's y |
| `B95D` | fixed palette (colors 7–15) in a list (index, RB, G); colors 1–6 per pyramid in `0xB97A`/`0xB9F8` |

Each character is **2 overlapping sprites** (color A + color B with the CC bit); where they overlap the color is
A|B (15 = black). Vick occupies 2x2 sprites from (x, y−8), +2 px facing right (`9881`).

**Vick's poses (`E285`, read from the code):** walk {0,1,2,1}/{3,4,5,4} every 4 ticks (`9F19`);
fall 1/4; ladder 6↔7 every 4 ticks; jump 8/9; use item 10→11 / 12→13. Death (`A31C`): spin 0..3
every 3 ticks, 5 times, then pose 6 for 0x28 ticks.
**Enemy frames:** mummies walking 1/2 (left) 3/4 (right) alternating by `E203&4` (`AF14`); facing
front 0; ladder 5/6; type 3: 9/11 in the air, 10/12 on the ground, appears as statue 7/8 (colors of type 2);
type 4: 15/16 standing, 17..20 rolling.

**Physics read from the code:** jump `A1C1` — velocity `E292` starts at 0xFC00 (−4) and adds 0x80 per tick
up to +4, Y in 8.8 (peak 14 px, 15 ticks); jump dx ±0x140 only with direction at the start (`A136`);
throw 8 ticks with the pose advancing on the 4th (`A384`); dig 0x20 ticks and only if there is something to dig
(`A0A6`/`A0F0`).

The full disassembly is in `tools/kv2-extract/work/dis_*.txt`.

## 7c. Logic ported from the ROM (validated tick by tick against the emulator)

| Part | Routines | Rule |
| :--- | :--- | :--- |
| Collision | `98E2`, `9910` | 2-bit cell (0 empty, 1 ladder, 2 diggable, 3 wall); 3 points per edge: top/bottom at x+2/+8/+14, sides at y/+6/+12 |
| Walk | `9F9A` | hit → x snaps to the cell (for left it rounds up) |
| Jump | `A190`/`A1C1`/`A16D`/`A535` | Y 8.8 with vy −4..+4 (+0x80/tick); in the air **a single point** at y+8 (the original's `LD A,A` bug); lands at any moment if the feet (x+3/8/13, y+16) touch something and **rounds Y to the cell** — that is what lets you climb onto a block by jumping; the head touching does not brake |
| Fall | `A2F0` | 4 px/tick, lands on any non-empty cell (ladder included) |
| Sarcophagus | `671D`, `AD2C`, `AE35`, `AE55` | counter 0x14 (x4 ticks), then 0x30; the mummy is born **at the record's y**, invisible 0x20, appears with the sarcophagus open 0x20, **the sarcophagus closes**, pause 0x20 |
| White mummy (type 1) | `AD80`..`AE1A`, `656C` | born facing against the player and turns on the 1st pause; walks 0x180; no ground (x+2/x+10) snaps and falls at 0x500; lands, pauses 0x10 and turns; wall: pauses 0x10 facing it and turns; 16 hits or stuck on both sides → disappears; lives in any room and crosses rooms at the edges |
| Enemy death | `6608`/`662D`/`6706` | frames 0x15,0x16,0x16 (8 ticks each, colors 0A/4B); then the sarcophagus re-arms |
| Contact | `9AB1` | only with bit 0 of `IX+13`; (y+8)−ey ∈ [0,16), (x+10)−ex ∈ [0,20); no ladder rule |
| Knife hits | `9AFE`/`9B80` | (y+12)−ey ∈ [0,24), \|ex−x\| < 12; +100 points (BCD 0x100; the stone adds 0x500) |
| Dig | `A0A6`/`A0F0`, `AA66`..`AC58` | shovel/pickaxe: 2x1 pair ahead-below; hammer/drill: 1x2 column ahead; only cell 2; 3 stages of 8 ticks; **the pickaxe keeps going down**, **the drill keeps advancing and carries Vick** |
| Door | `9010`, `8FBE`, `71F6` | one pin (block 0xAE) per missing stone, positions from table `0xB9AD`; with all of them, it enters by itself when you stand in position; the door opens in 2x30 ticks |
| Sounds | `4326` + trampolines `41E0..430B` | each call has a fixed ID (see `OriginalSounds.cs`); 56/60 are **landing/fall**, not footsteps |
| Color cycle | `5C14` (table `0xBD52`) | palette color 6 pulses black→red→white→red every 4 ticks |

## 7d. MSX1 screens **drawn by the cartridge itself** (no emulator, no dump)

The fixed screens used to come from openMSX dumps. Now they come out of the ROM **on the free version's first boot**:
the package's machine (`Wagenheimer.MsxAudio.Extraction.MiniMsx`) runs the cartridge, the game draws on the VDP and
the VRAM is decoded into the port's native format. Implementation:
`src/KingsValley.Engine/Extraction/RomScreens.cs`; wired in `AudioBootstrap` (writes
`%USERPROFILE%\Music\MSXMusic\KingsValley2\Screens\title.bin`) and read by `RomContent.ScreenPath` (the extracted folder
has priority).

> **Where the extracted content lives:** the shared **MSXMusic** folder — `<MSXMusic>/KingsValley2`, i.e.
> `%USERPROFILE%\Music\MSXMusic\KingsValley2` on Windows, `~/Music/MSXMusic/KingsValley2` on Linux/macOS. The
> MSXScope player extracts to the same root (one subfolder per game), and the root can be moved with the
> `MSXAUDIO_HOME` environment variable or with `musicRoot` in `msxaudio.json`. A machine that already had
> `%APPDATA%\KingsValley\extracted` keeps its files: the first run adopts them automatically.

```
dotnet test --filter RomScreens        # compara com RomMsx1/Art/Screens/title.bin (byte a byte)
```

**Validation:** the extracted title is **byte-for-byte identical** (SHA1 `DA004EF57D01…`) to
`Content/RomMsx1/Art/Screens/title.bin`, which came from openMSX, palette included — that is, running the ROM on the
machine reproduces the same frame as the external emulator, pixel by pixel.

### 7d.1 The two traps that kept the screen from existing

| Symptom | Real cause | Fix |
| :--- | :--- | :--- |
| VRAM filled up but **stayed blank**; the RLE decompressor `4E28` ran and nothing appeared | the cartridge does `LD A,(0x0007); LD C,A; OUT (C),A` — it reads **the VDP data port from the BIOS header**. With the machine's BIOS all `RET` (`0xC9`), every decompressed byte went to **port 0** | the machine fills `0x0006`/`0x0007` with `0x98`, like the real BIOS |
| the game read a VDP register that **we had not written** (`R1=0x40` instead of `0x62`) | the game builds the next value from `RG1SAV` (`0xF3E0`), the copy the BIOS keeps in RAM; without the mirror the value came out truncated | every register write (port `0x99` **and** `WRTVDP`) now updates `RG0SAV..RG7SAV` at `0xF3DF` |

### 7d.2 SCREEN 2 decoding (the MSX masks, not the "raw" TMS ones)

```
nomes    = (R2 & 0x0F) * 0x400     padrões = (R4 & 0x3C) * 0x800     cores = (R3 & 0x80) * 0x40
cor da célula = linha da tabela de cores do BLOCO de 8 padrões (code / 8), não do padrão
```

It is the same calculation as `art/msx1_screens.py` — the decode was ported to C# with its own test
(`RomScreensTests.DecodeScreen2_ReadsTheTablesFromTheRegisters`).

### 7d.3 The cartridge ships **two** title screens

`52A0` reads the low nibble of the BIOS byte at **`0x002B`** (in a real BIOS it is the destination byte of a
jump, so it varies between machines) and chooses between the two compressed blocks: `0x9625` (**English**
title) and `0x9438` (**Japanese** title). openMSX's C-BIOS has `0x00` there, and it is the
Japanese variant that is in all of the port's reference material. `RomScreens` pins `0x00` explicitly
(`MiniMsx.SetBiosByte`) so as not to depend on the BIOS fill.

> **In the port:** the player chooses in the **system menu** (`ESC` → `LANGUAGE`: `JAPANESE` / `ENGLISH`), which
> saves to `SaveGameData.TitleLanguage` and swaps the art on the next title scene. Both screens are
> extracted (`Screens/title.bin` and `Screens/title_en.bin`) and the choice can also be forced with
> `KINGSVALLEY_TITLE_LANG=jp|en` (diagnostics/CI).

### 7d.4 Automatic extraction: state-driven navigation (no clock, no playing by hand)

A menu screen is not "wait N seconds and dump": the game ignores keys while the menu is still
coming in, and the cursor blinks. `RomScreens` drives the machine by **state**:

| Piece | Rule |
| :--- | :--- |
| "Screen ready" | fewer than **300 pixels** changing per frame, for **120 frames in a row** (2 s). Drawing changes thousands; the menu cursor changes 128 |
| "Screen is the text menu" | band of lit pixels: menu ≈ 2,176, title ≈ 23,585, pyramid ≈ 22,018 |
| Key ignored | `TapUntilScreen` taps and **verifies**; if it did not open the menu, it taps again (up to 6 times) |
| Cursor phase | after stabilizing, it watches 24 frames and keeps the **fullest** one — it is the phase the openMSX dump has |

The sequence that comes out of this (measured, not configured):

```
boot → título (2 toques no total)
      → START SELECT (menu de texto)
      → pirâmide (intro do stage 1)
```

Screens generated today: `title.bin` (Japanese), `title_en.bin` (English) and `pyramid.bin` — all three in **4.5 s**
of machine time, and the pyramid is **byte for byte** equal to the `Content/RomMsx1/Art/Screens/pyramid.bin` that came
from openMSX (`RomScreensTests.Pyramid_IsReachedAndDrawnExactlyAsTheShippedScreen`).

> Since everything is counted in **frames** (not seconds), the extraction is deterministic: the same ROM generates
> the same files on any machine, and the test fails if one day it stops generating them.

### 7d.5 Seeing what was extracted (HTML gallery)

The game reads `.bin`/JSON, so you cannot "open and look". The gallery solves that:

```bash
cd tools/kv2-extract
python art/gallery.py "%USERPROFILE%/Music/MSXMusic/KingsValley2" preview --open
```

It generates `<extracted>/preview/index.html` with the extracted screens (2x), the 60 levels drawn (walls,
ladders, stones, sarcophagi, items, door and the initial Vask) and the package's audio list. The PNGs are
only for checking — none of that goes into the game.

### 7d.7 MSX1 sprites: how the cartridge draws (what has been mapped so far)

The MSX1's named atlas is **not** closed yet; what has been measured (for the next session):

| Piece | Where |
| :--- | :--- |
| **Shadow SAT** (32 entries of 4 bytes: y, x, pattern, color) | RAM `0xE800` |
| Color/pattern block copied along | RAM `0xEA00` |
| Who **writes** the shadow SAT (measured with `MiniMsx.RamWriteHook`) | `0x4127` |
| Shadow → VDP copier (with anti-limit rotation of 4 sprites/row) | `0x40EF` (rotates) and `0x4105` (copies `E800`/`EA00`) |
| Copies an entity's **patterns** to VRAM (pointer at `IX+2/IX+3`) | `0x419E` (4-byte entities: `IX+0`=slot, `IX+1`=pattern offset, `IX+2/3`=pointer) |
| Vick's pose | RAM `E285` (0..13), patterns 0–15 rewritten on every change |
| Vick in the shadow SAT | entries #0–#3, `x = E284 + 2`, `y = E282 − 2 / − 5 / − 9 / + 7`, colors 7/10/1/1, patterns 0/4/8/12 |

What is missing: reading the **pose → pattern pointer table** (Vick's entity in the list that `0x419E`
walks). With it the names `vick_s0_00`… come out and the MSX1 atlas becomes usable by the game.

> Already extracted and usable today: the pattern sheets (`art/patterns.py`) with the 256 sprite
> patterns, the 41 assembled 16×16 sprites, the 256 scenery tiles and the **font** (A–Z, 0–9).

### 7d.6 MSX2 ROM (RC-761): what we have today

The MSX2 ROM is **ASCII8** (bank writes at `0x7000`), and the package already knows how to map that format
(`RomMapper.Ascii8`): the cartridge **initializes** on the machine, but it stays in text mode because the MSX2
graphics extraction requires the **V9938** (SCREEN 5, 4bpp and the HMMM command engine) — separate work. The port's
MSX2 art comes from the longplay + sprites/tiles read from the ROM in Python (`art/rom_sprites.py`).

## 8. Useful debugging

- `debug/vdp.py` renders a dump (G1–G4 + sprites); `debug/dsheet.py` makes a contact sheet.
- `debug/z80dasm.py` disassembles the ROM (`python -I z80dasm.py rom offset cpu n`).
- ⚠️ **Do not name a script `dis.py`**: it shadows the standard library's `dis` module (NumPy
  imports `inspect` → `dis`). Always run with `python -I`.

## Audio extraction update (2026-11-23)

The current production audio pipeline and remaining limitations are documented in [AUDIO_EXTRACTION_AUDIT.md](AUDIO_EXTRACTION_AUDIT.md). It uses isolated in-process sound-driver execution, separate MSX1/MSX2 packages, PCM validation and versioned provenance. Earlier openMSX capture instructions and manual loop-calibration notes in this document describe historical workflows, not the game's current extraction dependency.
