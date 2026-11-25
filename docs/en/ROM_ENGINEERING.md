# ROM engineering notes

> **Extraction status (2026-11-24):** Current extraction/storage and fidelity evidence are maintained in [EXTRACTION_HANDOFF.md](../EXTRACTION_HANDOFF.md) and [GRAPHICS_EXTRACTION_AUDIT.md](../GRAPHICS_EXTRACTION_AUDIT.md). Historical emulator experiments and port behavior notes below are not claims of complete current cartridge extraction.

How this port was reverse-engineered, what was measured, and every finding that is worth knowing
before touching the logic. The addresses refer to the MSX1 `RC-760` ROM image (banks 0, 1/2/3 and the
`0x0C` module), which is the same code as the MSX2 release.

---

## 1. The clock: one ROM call = one logic tick

This is the single most important model in the project, and the one that cost the most time.

* The VBL handler (`0x402D..0x408E`, hooked at `FD9F`) does, for **every** video frame:
  `CALL 0x541C` + `CALL 0x46D4` (`mode_frame` → `mode_tick` + `mode_input`), guarded by `E205`.
* But the handler has a second guard, **`E206`** (`0x4037`): if it is already set, the handler returns
  **without calling the heavy tick** (`CALL 0x6006`, bank 4). The heavy work takes between one and
  two video frames, so about half of the 60 Hz VBLs are skipped.
* **Measured**: a watchpoint on `E203` (incremented once per `mode_tick`) gives **≈30.7 increments
  per second** over 46 s. Cross-checks: the mummy walks 1.5 px per call (45 px/s), the Vick walks
  2 px per 2 samples at 60 Hz (60 px/s), and the end-stage text (180 units) takes 6 s.

Consequences:

* **Every ROM counter counts 1 per call.** Do not divide by 2. `E286 = 8` is 8 ticks, `E2F7 = 0x1E`
  is 30 ticks, `fall_dt`/`boom_dt` are tick tables.
* `E203` is a **phase generator** for animations: `E203 & 1` halves a handler (the knife/boomerang
  spin), `& 3` (delayed spawns), `& 4` (enemy walk frame), `& 0x80` (`home_vic` pose). It is **not**
  a global halving of the game loop: `mode_play` (`0x4942`) and `mode_clear` (`0x4B06`) have no gate.
* The port's tick is therefore one call; the engine calls `StageSession.Tick` at 30 Hz
  (`GameConstants.FramesPerTick = 2` exists **only** to compute `TickSeconds = 2/60`).

> The Portuguese handoff (`docs/ROM_PORT_HANDOFF.md` §"Laço principal") documents the same model with
> the original evidence; it used to claim 60 Hz, which was corrected in October 2026.

---

## 2. RAM map used by the port

| Address | Meaning |
| :--- | :--- |
| `E200/E201` | game mode / substate (the `0x46ED` table) |
| `E203` | call counter (phases) |
| `E206` | tick lock (see above) |
| `E207`/`E208` | key edges / keys held (bit 0 up, 1 down, 2 left, 3 right, 4 action) |
| `E226..E228` | score, 3-byte BCD |
| `E240` | REST (extra lives) |
| `E242` | stage (1..60), `E241` pyramid, `E243` current room/screen |
| `E245` | next extra-life threshold (BCD high byte; starts at 3 = 30 000) |
| `E250/E251` | pointer to the unpacked 2-bit map (usually `0xE900`) |
| `E257` | stage-clear script index (1-based) |
| `E280..E28F` | Vick state, Y, X, facing, pose, … |
| `E294` | facing (0 left / non-zero right) |
| `E296` | walk/pose timer |
| `E2A7` | air/land strobe (secrets) |
| `E2C0..` | sarcophagus records (spawn timers) |
| `E300..E4FF` | 64 tool slots, 8 bytes each (see below) |
| `E500..` | enemies, 16 slots of 16 bytes |
| `E600..` | 16 actor records of 16 bytes |
| `E700` | soul stones of the current screen (16 × 8) |
| `E7C0..` | secret entrance records (16 × 4) |
| `E900..` | unpacked 2-bit terrain map (192 bytes per screen) |

---

## 3. Terrain, ladders and room assembly

* The terrain is a **2-bit map**, 4 cells per byte, 8 bytes per row, 192 bytes per screen. Type
  `0` air, `1` ladder, `2` diggable, `3` wall. Collision (`map_tile` `0x98E2`, `probe_step` `0x9910`)
  treats `type < 2` as free.
* Ladders are separate records (normal at `0x6078`, golden at `0x60F0`) that **overwrite** the terrain
  cells with type 1.
* The actors (`E600`) stamp their cells as wall at `room_draw` (`stamp_actors_c3` `0x6398`), except
  type 3 (Rock Roll, early return at `0x63B7`) and type 5 (stone, wall only while idle).
* The tools (knife etc.) use the same `probe_step` family, so they read the same map.

---

## 4. Tool slots (`E300`)

| Offset | Meaning |
| :--- | :--- |
| +0 | state: high nibble 1..5 (screen/go/fly/wait/fall), low nibble 1..6 (knife, boomerang, shovel, pickaxe, hammer, drill) |
| +1 | Y |
| +2 | X (a **byte**: it wraps to the neighbour room) |
| +3 | screen/room (`IX+3`) |
| +4 | animation frame (0..2 knife, 3..5 boomerang) |
| +5 | step (boomerang profile index, or 0..5 while falling) |
| +6 | timer (counted once per call) |
| +7 | facing |

Key routines: `tick_map_tools` `0xA6BD` (one call per slot per tick), `tool_step_x` `0xA826`
(4 px, wraps rooms), `knife_fly` `0xA717`, `boom_fly` `0xA8C7`, `tool_hold` `0xA814` (0x18 ticks),
`tool_fall` `0xA9BA`, `tool_wrap_y` `0xAA17`, `knife_anim` `0xA87F` / `boom_anim` `0xAA9D`
(`E203 & 1`), `e300_enemy_hit` `0x9AFE` (last step of `play_tick`).

`play_tick` order (`0x5D63`):

```
border_flash, vic_tick, vic_blit, vic_sat, tick_map_tools, tools_sat, probe_pickup,
touch_gems, tick_delayed, tick_enemies, enemy_sat, probe_exit, tick_actors, secret_hit,
tick_ef10, vic_enemy_overlap, e300_enemy_hit, bgm_toggle, hud_world
```

---

## 5. The stage-clear machine (`E257`)

`mode_clear` (`0x4B06`) runs **once per call** (no `E203` gate) and dispatches on `E257 - 1`:

| Step | Ticks | What happens |
| :--- | :--- | :--- |
| 1 `clear_door` | 90 | door opens: a frame every `E2F7 = 0x1E` (30) ticks |
| 2 `clear_walk` | 128 | the Vick enters, 4 groups × 32 ticks, rising 2 px every 16 |
| 3 `clear_wipe` | 48 | curtain closes: 12 bands, one every 4 ticks |
| 4 `clear_card` | 180 | end text (`E204 = 0x3C` then `0x78` ticks) |
| 5..8 | — | score/REST bonus, password, wait for a key (32 ticks after the key) |

Total before the end screen: **266 ticks ≈ 8.9 s**. The longplay confirms the 180-tick text (6 s).

---

## 6. The `E600` actor table

16 records of 16 bytes, loaded by `load_actors_far` / `0x633F`. Layout: `+0` type, `+1` state,
`+2` Y, `+3` X, `+4` screen, `+5` phase (type 1 only: `3 × (byte 3 & 7)`), `+6` timer, `+7` flags,
`+8` height in cells.

| Type | Handler | Port |
| :--- | :--- | :--- |
| 1 coffin/push door | `0xBA85 tick_coffin` | rotating door (`MechanismKind.RotatingDoor`) |
| 2 Pyoncy | `0xBBB0 tick_pyoncy` → `spawn_pyoncy` `0xB274` | spawned as `EnemyKind.Guard` |
| 3 Rock Roll | `0xB68E tick_rockroll` → `spawn_rockroll` `0xB67E` | spawned as `EnemyKind.Rock` |
| 4 trap | `0xBD21 tick_trap` | trap (`MechanismKind.Unknown4`) |
| 5 stone | `0x93F3 tick_stone` | pushable block (`MechanismKind.MovableBlock`) |

The width of a stamped actor comes from `actor_hgt[type]` (`0x6445`: 2, 1, 1, 4, 2) and the height
from byte 8 — the port's `ParseMechanism` uses exactly that.

---

## 7. Audio

* SFX are packed-PSG streams played through `sound_far` (`0x4326`) → `sound_entry`; the trampolines
  `0x41E0..0x430B` pass the id. The extraction records each id in isolation (`Sfx/<name>.vgm`) and
  `Audio/audio.json` maps id → file when the package is rebuilt (`MsxAudioTool import --manifest`).
* The PSG has **one channel per effect**: re-playing the same id restarts it. The port keeps one
  voice per id (the `MsxAudioEngine` sample voices) and stops the fall whistle (`0x3C`)
  when the landing (`0x38`) plays, which is what makes a one-cell fall sound short in the original.
* Music: `stage_a..stage_e` (a theme every two stages), `title`, `menu`, `ending`; the door jingle
  (`0x23`, 9.7 s) stops the stage music for the whole exit sequence.

---

## 8. The extraction toolchain

The production pipeline runs directly from a verified user-supplied cartridge. It does not require Python, an external emulator or pre-recorded audio. The older scripts remain research tools.

| Data | Production component | Input |
| --- | --- | --- |
| Stage JSON and dynamic initial state | `RomStageJson` / `RomStageState` | Exact cartridge tables and native loader execution |
| MSX1 rooms/screens/atlas | `RomRoomDumps`, `RomScreens`, `Msx1*` | Native TMS9918 state and cartridge routines |
| MSX2 rooms/screens/atlas | `RomDirectExtractor`, `Msx2Screens`, `Msx2VdpRunner` | Native V9938 data and cartridge routines |
| Music and effects | MSXAudio extraction profiles | Isolated cartridge sound-driver execution and chip-register events |
| Validation and publication | Graphics manifest/generation store and audio package profiles | Cartridge identity, complete inventory and hashes |

Outputs are isolated by cartridge under `<MSXMusic>/kings-valley-2/<msx1|msx2>/`. See [the extraction handoff](../EXTRACTION_HANDOFF.md) and [current audit](../GRAPHICS_EXTRACTION_AUDIT.md) for the directory contract and measured evidence.

---

## 9. The bank `0x0C` module (ending menu, sound select, puzzle) — located

The mode table comment says the ending runs `end_menu` from the **bank `0x0C` module**
(`call 0xB534`). For a long time the bytes there did not look like code because of a **bank-number
confusion**: the mapper writes are **decimal** bank numbers, so `bank 0x0C` is bank **12** =
file offset **`12 * 0x2000 = 0x18000`**, and the module's entry points are at CPU
`0xA000 + (offset inside the bank)`:

| CPU | File | Routine |
| :--- | :--- | :--- |
| `0xB400` | `0x19400` | `load_ef10` — walks the `0xB548` table comparing `E242` (level) and loads the "pyramid FX" record |
| `0xB422` | `0x19422` | `tick_ef10` |
| `0xB51C` | `0x1951C` | `draw_ef10` |
| `0xB534` | `0x19534` | `end_menu` — `CALL 0xB53A` (`IX = EF10`; dispatch: `A-1 == 0` → `0xB917`, else `0xB6B0`), then `JP 0x583B` |

This was **measured**, not guessed: boot the MSX2 ROM, type a stage password (the `load_ef10` only
runs in `room_draw`, never at the title), set a breakpoint at `0xB400` and dump the memory — the code
at `0xB400` matches ROM offset `0x19400` exactly (`tools/kv2-extract/work/gen/module2.tcl`).

With the module disassembled (`z80dasm.py <rom> 18000 a000 2000`), the puzzle and the sound-select
routines are now reachable for the next porting pass:

**Page A — sound select (`0xB917`, `EF10 == 1`)** — implemented, art captured from the VDP dump:
the hand cursor walks **19 keys** at the X positions of table `0xB9FA`
(`2C 33 3C 45 4C 55 5C 6C 73 7C 85 8C 9C A3 AC B5 BC C5 CC`), and the confirm table `0xB98A` maps
each key to a `sfx_` trampoline (id = `(ptr - 0x41E0) / 5 + 1`, checked against `sfx_1d` = 29 at
`0x426C`): ids **3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 22, 39, 40, 29**.

**Page B — the puzzle (`0xB6B0`, `EF10 != 1`)** — implemented. Its substate table sits at `0xB6BE`
(`0xB6CA`, `0xB6F2`, `0xB701`, `0xB749`, `0xB85E`, `0xB89D`): setup (`4E98` HUD, `5D41` SAT wipe,
`56AA`, `57CA`, clear `EF20..EF38`), a fire wait, an animation (`E203 & 3`), and then a **5×5 board
cursor**: `IX+2` (0..4) and `IX+3` (0..4) move with the D-pad (`0xB76B`/`0xB775`/`0xB75F`/`0xB780`,
cursor sound `0x42CB`), with `0xB8CF` drawing the cursor and `0xB7C5` handling a cell.

The screen captured from the VDP dump is **`PUZZLE GAME`**: a 5×5 sliding board with pieces **1..24
and one hole**, plus `PUSH SPACE KEY / END ESC KEY`. The board occupies **x 72..182, y 48..128**
(5 cells of **22×20** pixels; the numbers are the bright bands `120..126` in the last row and the
footer starts at `145`). The port (`PuzzleScene`) draws the pieces from the strip extracted from that capture. The board is
**not solved at start**: substate 3 (`0xB701`) does `LD HL,0xB5E0`, `A = E203 & 3`, `CALL 0x4D4C` (a
`HL += A*2; HL = (HL)` table index) and copies **25 bytes** into `EF20` — one of four fixed scrambled
boards (`0xB5E8`, `0xB601`, `0xB61A`, `0xB633`). The array is **column-major**
(`index = column*5 + row`, `0xB800`), the cursor is `IX+2`/`IX+3` (0..4), and `0xB824` looks for an
adjacent hole in the order **left, right, up, down**. Winning (`0xB85E`) requires `EF20.. = 1,2,…,24`
and grants **+3 REST** (`E240 += 3`, BCD, cap `0x99`).

The port reproduces all of it: the four boards, the column-major win check, the adjacency order and
the `+3` reward (`KINGSVALLEY_PUZZLE_BOARD=0..3` picks the board, `KINGSVALLEY_PUZZLE_SOLVED=1` enters
the solved state for captures). The board art is 5×5 cells of **14×14 at (89,49), pitch 16** — measured
from the capture, matching the ROM's 16-pixel step.

### The ending text card (`end_text`, `0x4B9C`)

`end_text` clears the screen, waits **`0x78` = 120 ticks** (`ld a,078h / call sub_delay`), pages banks
`E/F` and indexes the table at **`0x9D37`** (`(EF10-1)*2`):

| `EF10` | Pointer | `[D][E]` | Text |
| :--- | :--- | :--- | :--- |
| 1 | `0x9D3B` | `50 50` → (80, 80) | **MUSIC STAGE** |
| 2 | `0x9D49` | `48 50` → (72, 80) | **PUZZLE STAGE** |

The table has **exactly two entries** — the bytes right after the second stream (`b8 9d d3 9d …`) are
graphics/level data, not a pointer table (decoding them as pointers produces garbage). The stream
format is `[D][E]` (x, y in MSX pixels) followed by glyphs: `0xE1..0xFA` = **A..Z**, `0xD0..0xD9` =
**0..9**, `0x00` = space, `0xFF` = end. `EndingScene` reproduces the blank wait, the card (position
from the table) and then the score card. Both cards were captured from the port
(`docs/en/media/end_music.png`, `end_puzzle.png`); `KINGSVALLEY_PYRAMID=1|2` selects them.

### The ending script and its five pages (bank `0x0C`)

`end_script` (`0x7353`, bank 1-3, table `d_735f` with **65 entries**) drives the whole ending. Its text
steps call **`print_12`** (`0x771C`), which **pages bank `0x0C`** and then runs `print_stream` — so the
strings are in the bank-12 module, not in bank `EF`. The addresses and their content:

| CPU | File | Step | Content |
| :--- | :--- | :--- | :--- |
| `0xACC3` | `0x18CC3` | `end_boot` | (72,72) `congratulations` · (64,88) `to vick x` · (72,104) `you heroically` · (64,120) `unsealed all the` · (64,136) `locks` |
| `0xAD12` | `0x18D12` | `end_txt2` | (40,128) `all you have to do now` · (40,144) `is turn off the swich` · (40,160) `to save the world` |
| `0xAD58` | `0x18D58` | `end_txt3` | (72,160) `push space key` |
| `0xAEDD` | `0x18EDD` | `end_txt4` | (48,48) `thanks to your courage` · (16,80) `the world has been saved` · (88,112) `well done` |
| `0xAF21` | `0x18F21` | `end_txt5` | (56,16) `congratulations` |

**Decoded stream format** (measured): the first two bytes are the start `x, y`; then glyphs are
**ASCII + `0x7C`** (`0xDD` = `a` … `0xF6` = `z`, so `0xE1` = `e`, `0xDF` = `c`), `0x00` = space,
**`0xFE <x> <y>` repositions the cursor**, and `0xFF` ends the stream. Three bytes have no ASCII
counterpart — `0xDB`, `0xF7` and the pair `0xFA 0xFB` — and are **special glyphs of the module's
tileset** (they were skipped in the port; everything else is verbatim, including the ROM's own typo
**"swich"**).

`EndingScene` now runs: 120-tick blank → pyramid name card → the five pages (auto-advance or Space) →
score card → `end_menu`. Captures: `docs/en/media/end_page1.png`, `end_page4.png`.

---

## 10. The stage editor (`cont_edit`, bank 1-3)

The original ships a full stage editor, reached from the title's **game / edit** choice (`E222`, the
blinking `<` `=` at `title_ptr` 0x4D57; the label `txt_edit` is at `0x5F45`). The entry is
`edit_enter` (`0x7B??`): it clears `E260` (legend) and `E261` (subtype), loads the editor font from
bank `D` (`0xBC44`, 2×13 tiles → `F038`) and jumps to the editor state machine.

`cont_edit` (`0x7B08`) dispatches on `E25B` through the table at **`0x7B0E`**:

| # | Routine | What it does |
| :--- | :--- | :--- |
| 0 | `edit_hud` | `scr_reset` + `sat_wipe`, draws the legend (`edit_legend`) and the screen map (`scr_sat`) |
| 1 | `edit_kind` | moves the legend cursor `E260` (**0..10**) with the D-pad (`sfx_32`); items **9** = play and **10** = minimap jump straight out (`kind_play` / `kind_minimap`), items **4..6** go to `edit_sub` |
| 2 | `edit_sub` | subtype `E261`, clamped by `E263` (the max read from the help stream) |
| 3 | `edit_scr` | picks the screen slot `E262` inside `E788` (the stage's screen ids), cursor SAT from `scr_sat` |
| 4 | `edit_put` | paints the selected tool on the 32×24 map (`scr_ptr`) |
| 5 | `edit_yn` | confirmation |

Supporting routines: `edit_legend` (`E260`), `edit_help` (`0x7C0E`, prints the `0xBCBB` stream for
`E260-4` and stores its max in `E263`), `edit_wipe` (`0x7C27`, VDP `LMMV` wipe of the help area),
`edit_font` (`0x7B33`), `edit_first` (`0x7CFF`, first non-empty entry of `E788` → `E262`),
`kind_sat` (`0x7BDA`, legend cursor sprites at `E800`), `draw_minimap` + the RLE `0xBBFC`
(`rle_minimap` `0x7AA6` → `F880`) and the scan `edit_pat` (`0x7A96`, `pat_15` + clear held + reload).

**Legend and help (bank `D`).** `edit_legend` (`0x867D`) draws the minimap, prints the legend stream at
**`0xBC54`** (x = 40, y = 16 + 16·i) and then a **6×6 map of `E788`** at `0xA090`. The legend has the
**11 items below**, and three of them carry a help stream from the table at **`0xBCBB`**:

| # | Legend | Help (table `0xBCBB`) |
| :--- | :--- | :--- |
| 0 | `floor1` | — |
| 1 | `floor2` | — |
| 2 | `ladder` | — |
| 3 | `player` | — |
| 4 | `enemy` | `slouman` · `flouman` · `pyoncy` · `rock roll` |
| 5 | `trap` | `door1 lr` · `door1 rl` · `door2 lr` · `door2 rl` · `wall` · `floor` · `stone` · `ladder` |
| 6 | `tool weapon` | `knife` · `boomerang` · `scoop` · `pick` · `hammer` · `drill` |
| 7 | `soul stone` | — |
| 8 | `exit door` | — |
| 9 | `save` | — |
| 10 | `end` | — |

⚠️ The bank-`D` font is **shifted four glyphs** from the bank-`C` one: the ending streams decode with
`byte - 0x7C` (`0xE1` = `e`), while the legend/help streams decode with `byte - 0x80` (`0xE1` = `a`,
`0xE6` = `f`). Digits are `0xD0..0xD9` = `0..9` in both.

**Port status:** `EditorScene` (`KINGSVALLEY_START_SCENE=editor`) implements the legend UI (**Tab**
opens it, ◄ ► moves, ▲ ▼ picks the **sub-type**, Space/Enter confirms), the paint cursor on the 32×24
room and the ROM's terrain kinds (empty, diggable, wall, ladder). Placement covers every entry:
`floor1`/`floor2`/`ladder` (terrain), `player` (start), `enemy` (slouman/flouman → coffins;
pyoncy/rock roll → `E600` types 2/3), `trap` (door1/door2 lr/rl → rotating door phase 0/3, wall,
floor, stone, ladder), `tool weapon` (knife/boomerang/scoop/pick/hammer/drill → items), `soul stone`
(toggle) and `exit door`; `save` writes `editor_stage_01.json` as an edit of the *original* document
(so the extraction schema survives) and **re-parses it with `StageLoader.Parse`** as a self-check;
`end` returns to the menu, and **P** is the **play-test** — the edited stage goes straight into
`GameScene` through `GameNavigation.StageOverride` (no disk round trip). Room switching is **Q/E**, erase
is **X**, and **M** opens the **room map** — now drawn as the **6×6** grid of `E788` exactly as
`edit_legend` does (the port's 8×8 `RoomGrid` is a superset; official stages only fill the first 6×6;
◄ ► ▲ ▼ picks a room, Space enters it).
`KINGSVALLEY_EDITOR_SAVE=1` / `_LEGEND=1` / `_TOOL=n` / `_MAP=1` / `_PLAY=1` / `_STAGE=n` drive the
editor headlessly for captures. With the map in, the editor covers the ROM's full legend and every
placement type.

---

## 11. The world-map ceremony (`world_script`, 0x6F63)

`world_script` runs an **11-step** script (`E257`, table `0x6F6E`): `world_scr` → `world_hold` →
`world_tour` → `world_pic0/1/2` → `world_out` → `world_count` → `world_sat` → `world_in` →
`world_exit`.

**The tour is a run-length path** in bank `D`: the table at **`0xBA57`** holds one word per world
(`0xBA61`, `0xBA84`, `0xBACD`, `0xBAF4`, `0xBB0F`), each pointing at `(step, direction)` pairs ended by
`0xFF`. `world_path_scan` picks the **last pair whose step ≤ `EDC8`**, and `world_delta` applies it:

| Direction | Meaning |
| :--- | :--- |
| 0 | hold still |
| 1 | Y + 1 (down) |
| 2 | X − 1 (left) |
| 3 | X + 1 (right) |
| 4 | Y − 1 (up) |

Each step lasts **8 ticks** (`world_step` writes `E204 = 8`), and the tour ends when `EDC8` reaches
`world_len[world]` (table `0x7177`): **`0x58`, `0xB8`, `0x88`, `0x8E`, `0x95`** = 88, 184, 136, 142 and
149 steps. Decoded paths:

| World | Pairs (step, direction) |
| :--- | :--- |
| 1 | (0,2) (3,1) (6,2) (11,0) (25,2) (31,1) (39,2) (44,1) (47,3) (52,1) (57,3) (60,1) … 17 pairs |
| 2 | (0,2) (3,1) (6,2) (11,1) (16,3) (21,1) (24,3) (29,0) (32,3) (46,1) (54,2) (60,0) … 36 pairs |
| 3 | (0,3) (3,1) (11,2) (29,1) (33,3) (57,0) (63,3) (66,1) (69,3) (74,0) (82,2) (87,0) … 19 pairs |
| 4 | (0,0) (8,3) (16,1) (32,3) (48,0) (62,2) (70,0) (75,3) (86,0) (115,2) (121,1) (126,2) … 13 pairs |
| 5 | (0,2) (8,1) (11,2) (17,0) (31,3) (34,0) (39,3) (47,0) (55,2) (63,0) (68,3) (87,0) … 20 pairs |

The port's `WorldMapScene` shows the actual **MSX2 world map screen** (assembled in runtime via `ScreenAssembler` from native VRAM/palette data `world_map.bin` — distinct from `pyramid.bin` used for the initial "DO YOUR BEST !!" intro) with the map BGM and **animates Vick walking** through the pyramid corridors:
in the ROM, `world_xy` (`0x71CA`) stores `(Y, X)` in bytes 0 and 1, copied by `0x6FD5` into `EDCB` (Y) and `EDCC` (X). `tour_vic` (`0xBDDA`, bank `0x0C`) writes `EDCB`/`EDCC` **straight into the SAT**,
so the tour coordinates are **screen pixels** starting at `(78, 43)` for world 1, `(46, 59)` for world 2, `(48, 99)` for world 3, `(83, 102)` for world 4, and `(94, 83)` for world 5. Each step is 8 ticks and follows the direction pairs above. The three `wpic0..2` are **small stamps**, not full pictures: `wpic0`
(`0x5A17`) draws a **12×2**-tile map from bank `EF` `0xA5A6` at `B038`, `wpic1` (`0x5A29`) a **12×4**
from `0xA5BE` at `A838`, and `wpic2` (`0x5A3B`) a **14×6** from `0xA5EE` at `A030` - the world-complete
labels. `draw_tilemap` (`0x573B`) takes those `DE` values as **pixel** coordinates (`D` advances by 8 per
tile), so the stamps sit at **(176,56)**, **(168,56)** and **(160,48)**. The tile indices are plain
box-drawing codes (`0x5C` = left bar, `0x66` = right bar, `0x61` = interior, `0x73` = top bar, corners
`0xB6`, `0x6A`, `0xC4`, `0xBB`…), and the font they index comes from `tiles_wmap` (`0x5B16`): bank
`0x0C` `0xAA29` (10 rows) and `0xAB79` (10 rows) copied to VRAM `0x8030` / `0xB838`. Rendering those
patterns offline did **not** match the VRAM layout yet (the copy size is `0x290A` bytes, so it is not a
plain 8-bytes-per-tile run), and forcing `mode_world` from inside a stage (poking `E200=10` at
`0x46DA`) did not take - so the stamps stay unported and the attempt script is kept at
`tools/kv2-extract/work/gen/wpic.tcl` for the next pass. Everything else of the ceremony (the map, the
tour and the hallway stamps) is in the port.

---

## 12. Game over and continue (`mode_over` 0x4A3D, `mode_cont` 0x4B5D)

When the last life goes, `mode_over` **clears the screen** (`scr_reset`) and prints two streams from
bank 0:

| Stream | CPU | Position | Text |
| :--- | :--- | :--- | :--- |
| `txt_over` | `0x5F4C` | `[0x58][0x58]` = (88, 88) | **`game  over`** (two spaces, as in the ROM) |
| `txt_cont` | `0x5F59` | `[0x50][0x68]` = (80, 104) | **`f5  continue`** — only printed when `E217 \| E254` says there is a continue |

Then `over_hud` (`0x4C8A`) draws the MSX2 box with the scores, and `mode_over` waits **`0x78` = 120
ticks** without a continue or **`0xB4` = 180 ticks** with one; `over_wait` reads the key and either
goes to `mode_cont` (F5) or back to the title (`title_reset`).

**The continue is a password cheat, not a default feature.** `E217` is only ever set by `pwd_cheat`
(`0x6B24`), which also runs `play_clear` (the `play_init` bytes `01 01 01 00 00 03` → **stage 1, REST
1**). On a plain cartridge run `E217 | E254 == 0`, so `mode_over` prints **only** `game  over`, with a
**120-tick** window (`0x78`).

The port matches that: `GameRun.ContinueEnabled` starts **false**, `StagePassword.TryAgain` sets it
(via `GameRun.Cheat`), `FESTIVAL` sets invincibility instead, and both cheats restart at **stage 1
with REST 1**. `GameOverScene` prints `f5  continue` only when the flag is set, waits 120 ticks, and a
continue resets the **score to 0** and **REST to 1** at the same stage (`over_cont` → `goto_stage`).
Tests: `DeviationFixTests.TryAgain_…` / `Festival_…` / `NewGame_DoesNotEnableContinue`.
`KINGSVALLEY_START_SCENE=gameover` opens it for captures.

---

## 13. Open findings and deviations

Everything that could be measured against the ROM is ported. What is left is listed below with the
evidence, and it is either **visual art that only exists in runtime VRAM**, **an engine limitation**
or a **feature the ROM itself disables**.

### Closed (they were real deviations and are now fixed)

| Was | Now |
| :--- | :--- |
| Continue always offered, 180 ticks, restored `StartingRest` | `TRYAGAIN`-only (`pwd_cheat` 0x6B24 → `E217`), **120 ticks** (`0x78`), continue → **stage 1, REST 1, score 0** (`play_init`), window then menu |
| `FESTIVAL`/`TRYAGAIN` jumped to the highest level / kept 3 lives | both run `play_clear`: **stage 1, REST 1**; `FESTIVAL` also sets `E255` (invincibility), `TRYAGAIN` sets `E217` (continue) |
| Puzzle started solved and only played a sound | starts on one of the **four scrambled boards** (`0xB5E8`/`0xB601`/`0xB61A`/`0xB633`, chosen by `E203 & 3`), wins with the **column-major** check (`0xB85E`) and grants **+3 REST** (`E240 += 3`, cap `0x99`) |
| Ending always opened the sound select | the page follows **`EF10`** (`0xB548` table, value persists): `EF10 == 1` → sound select, else **puzzle** — so **stage 60 ends in the puzzle** (`StageFx`) |
| Editor room map drew 8×8 | draws the **6×6** grid of `E788`, exactly as `edit_legend` (0x867D) |
| “Enemies/tools do not collide with the stone” | **false** — `RoomMap.Get` returns `Wall` for `SetRock` cells; proven by `DeviationFixTests` |

### Not deviations (the ROM does the same or the feature is dead)

1. **Secret entrances** (`E7C0`): the loader forces bit 7 (`0x44B4: F6 80`) and `secret_reveal` only
   punches when `(flags & 0xC0) >> 6 == 1` — unreachable in all 60 official stages.
2. **Fall whistle**: the ROM's landing sound (`0x38`) stops the whistle (`0x3C`); the port does the
   same, so the extracted 1.85 s clip is cut exactly like the original.
3. **Stone wall**: the port uses a dynamic wall (`RoomMap.SetRock`) instead of re-stamping the map;
   collision is identical, and no phantom wall is left behind (the ROM re-stamps at `room_draw` for
   the same reason).

### Still open — art / engine only (no game rule affected)

4. **`wpic0..2`** world-complete stamps: 12×2, 12×4 and 14×6 tile maps from bank `EF`
   (`0xA5A6`, `0xA5BE`, `0xA5EE`) drawn at **(176,56)**, **(168,56)** and **(160,48)**
   (`draw_tilemap` 0x573B takes `DE` as pixels); the font comes from `tiles_wmap` (0x5B16 → bank
   `0x0C` `0xAA29`/`0xAB79`, 41 and 10 tiles, 1 bpp expanded with `C` = on/off nibbles). The tile ids
   are **screen-coded** (`tile_src`: `((id & 0x1F) * 8) << 8 | ((id & 0xE0) >> 2)`), so rendering
   them needs the exact VRAM layout — the offline attempt did not match and forcing `mode_world` from
   a stage did not take (`tools/kv2-extract/work/gen/wpic.tcl`).
5. **Hallway stamps** (`strm_hallway`, bank `EF` `0xA3E8`/`0xA4C7`): same `stamp` stream
   (`tile id`, `0xFE dy` = next row, `0xFF` = end) and the same VRAM-font problem.
6. **`EF10` visual module** (`draw_ef10` 0xB51C → `0x56C5`/`0xB4E3`): the *logic* (which stage has
   FX, and the `EF10` value used by the ending) **is** ported in `StageFx`; only the drawing is not.
7. **Trap stamp** (`trap_punch` 0xBD7A): gameplay exact (clears the 1×4 bar via `0x63ED`, plays
   `0x27`); the MSX2 effect is an **HMMM 8×32 from a runtime VRAM stash** (`0x5029`), which cannot be
   rebuilt offline.
8. **Ending glyphs**: three codes have no ASCII counterpart in the ending streams (`0xDB`, `0xF7`,
   `0xFA 0xFB`) — they are punctuation/symbol tiles of that font; everything else is verbatim
   (including the ROM's typo `swich`).
9. **Gum font materials** (the Unity-style gradients) only render at zoom 1: Gum regenerates the
   atlas under oversampling, so text is sharp at every resolution but loses the gradient at zoom ≠ 1.
   Engine limitation, not a game rule.


## Projectile room collision repair (2026-11-24)

The level-3 knife could cross into room 2, ignore its far wall, wrap back into room 1 and become a floor item there. The port incorrectly treated offscreen projectile terrain as unavailable. Both cartridges retain every room's packed terrain at `E840 + room * C0`; offscreen execution switches probe routines instead of skipping collision.

Native MSX1 `A3A6/A3E6` calls `9DAC` with the projectile room's map; MSX2 `A826` reaches `probe_step_de` at `9933`. A byte-coordinate crossing updates the room and probes the destination on that same logic call. Waiting and falling also use the projectile's own floor map (`A597` in MSX1, `AA17` in MSX2). The downward three-point probe samples X+2, X+8 and X+14. At Y=B8, the native fall wraps to Y=8 and tests the destination, even if a single-room topology returns the same room.

`StageSession.Enemies.cs` now retains these collisions for both knives and boomerangs. Terrain ownership follows the projectile room, while visibility, pickup and landing sounds remain tied to the player's current room as before. No extracted art/audio or cartridge package needs regeneration.

Regression tests exercise both horizontal directions, both weapon kinds, same-call destination collision, offscreen landing and vertical self-wrap. Local-cartridge integration checks use the actual level-3 terrain for MSX1 and MSX2 and compare each flight tick's room, X/Y and stop phase with the original native knife routine. They then verify that the dropped knife remains in room 2. This validates the reported traversal; it does not certify every stage's exceptional room-link topology.

Verification: 60 selected gameplay/physics/save-state tests pass, including all eleven new projectile-room cases and the two real-cartridge level-3 comparisons. These targeted checks avoid rerunning unrelated graphic extraction recipes.

Runtime verification also launches MSX1 level 3 at room 1 (104,160) holding the knife, throws it, waits and walks into room 2. The captured game shows the knife at the destination wall/floor, not back in room 1. The diagnostic process exits successfully. The solution build passes with zero warnings/errors.


## Triggered falling walls (2026-11-24)

Level 3 contains E600 type 3 at room 2, X=160, Y=16, height=4 cells. Its extracted record was present but the port skipped `Unknown3`. It is now named `FallingWall`. Native MSX1 B9EF/BA17 and MSX2 BC74/BC9C arm it when the player's center X lies within its eight-pixel column and player Y lies within its encoded height. No initial solid wall is drawn.

MSX1 BA3B/BA5D and MSX2 BCC0/BCE2 advance the active column every 30 logic calls: stamp one soft terrain cell, draw native tile 5 and play sound 31 only in the visible room. The state continues offscreen and clears its native type one interval after the final row. The port preserves that timing, one-trigger-per-post-pass ordering, map changes and remaining countdown across save states. The resulting soft wall can be dug normally and does not re-arm after completion.

Local-ROM tests compare the room-2 terrain with both native routines at every logic tick, including offscreen progress. A save/resume test also checks final terrain and no retrigger after digging. Runtime before/after captures show the native MSX1 soft-tile column sealing the upper passage. MSX2 uses the existing runtime room assembler; MSX1 overlays the added cells with the verified native atlas tile rather than generating graphics.

The combined targeted run passes 67 gameplay, save-state, projectile, wall and presentation tests. The solution build has zero warnings/errors. Original/Transparent captures and the actual system menu were reviewed; native source assets remain unchanged.
