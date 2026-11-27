# Handoff — porting King's Valley II logic from the ROM

> **Current-status notice (2026-11-24):** This document preserves earlier reverse-engineering observations and experiments. Its historical cache paths, fallback recipes, source-version assumptions and completion claims are not current operating instructions. Start with [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md), [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md) and [AUDIO_EXTRACTION_AUDIT.md](AUDIO_EXTRACTION_AUDIT.md). Exact cartridge identity and bank mapping govern every routine address. Production extraction is standalone and never requires openMSX.

> **For any AI (or person) to continue where we stopped.** Read this whole file before touching
> game rules. Historical gameplay checkpoint: 2026-10-17. Current extraction status is linked above.

Sibling documents: [GAME_DESIGN.md](GAME_DESIGN.md) (the game), [ROM_EXTRACTION.md](ROM_EXTRACTION.md)
(formats and routine tables), [`tools/kv2-extract/README.md`](../tools/kv2-extract/README.md)
(scripts), [`tools/kv2-extract/work/README.md`](../tools/kv2-extract/work/README.md) (raw material).

---

## 1. Goal and rules

- **Exact** clone of King's Valley II (Konami, 1988) **MSX2 version**, in MonoGame + Nez (C#).
- The source of truth is the **exact cartridge being reconstructed**. The routine/trace notes below primarily describe earlier MSX2 work; MSX1 addresses and behavior must be verified separately. Derive rules from bank-aware Z80 analysis and label estimates or unverified mappings explicitly.
- Game logic in `src/KingsValley.Core` (pure C#, tested). Drawing in `src/KingsValley.Engine`.
- Each ported rule cites in the comment the **routine address** it came from (e.g. `// AF14`).
- Commits: messages in English in the conventional commits format; the repository is private and versions the
  ROM material (the open-source version will ship without `Content/Rom` and without `tools/kv2-extract/work`).

## 2. Environment (what must exist on the machine)

| Item | Where | Note |
| :--- | :--- | :--- |
| Base ROMs | `tools/kv2-extract/work/rom/` | `kv2_msx2_scc.rom` is the one that runs (MSX2 de-converted to Konami SCC) |
| Disassembly | `tools/kv2-extract/work/dis_b0.txt`, `dis_b1.txt`, `dis_b23.txt` | banks 0 (CPU 4000-5FFF), 1 (6000-7FFF) and 2+3 (8000-BFFF) — **the game logic is in `dis_b23.txt`** |
| Portable openMSX | `tools/kv2-extract/work/omsx/` (**outside git**) | download openMSX 19+ for Windows and unpack it there; machine `C-BIOS_MSX2_JP` |
| Python | `tools/kv2-extract/work/venv/` (**outside git**) | `python -m venv venv` + `pip install numpy opencv-python soundfile z80dis` |
| .NET 10 | — | `dotnet build KingsValley.slnx`, `dotnet test` |

Disassembling a stretch: `venv/Scripts/python.exe -I z80dasm.py rom/kv2_msx2_scc.rom <rom_offset> <cpu_addr> <n>`
(offset = bank × 0x2000 + (addr & 0x1FFF); with the game's mapping 8000→bank 2, A000→bank 3
during the match; graphics tables use 8000→14, A000→15 or 13).

Running the original game on a stage (via the password), in the `work/` folder:

```bash
./omsx/openmsx.exe -machine C-BIOS_MSX2_JP -cart rom/kv2_msx2_scc.rom -romtype KonamiSCC -script gen/X.tcl
```

With `gen/X.tcl`: `set ::stage 5; set ::dumpdir pasta; source stage2.tcl; at 34 {dumpall nome}; at 35 exit`.
The match starts around 33 s of emulated time. `lib.tcl` has `dumpall`, `tap`, `hold`, `at`.

## 3. The method (repeat for each rule)

1. **Find the routine:** watchpoint on a known variable
   (`debug set_watchpoint write_mem 0xE511 {} proc`, or `write_io 0x98` for VRAM) and log
   `[reg pc]` + `debug read "kv2_msx2_scc.rom romblocks" $pc` (bank).
2. **Read** it in `dis_b23.txt` (`awk '$1>="ad80" && $1<"ae40"' dis_b23.txt`). Dispatchers use
   `CALL 0x4099` followed by a table of addresses (2 bytes each) — decode the bytes that appear
   as "instructions" right after the CALL.
3. **Port** to C# with the same state/field names and the address in the comment.
4. **Validate:** record the trace in the emulator (`emulator/trace_enemy.tcl` → 1 line per tick: E203,
   the whole E500 structure, player, sarcophagus) and in the simulation (`tools/kv2-extract/simtrace`:
   `dotnet run -c Release -- <stageNN.json> sim.txt 700`) and compare with `simtrace/compare.py`.
   **Goal: 0 differences.** (White mummy in stage 1: 0 differences over 420 ticks.)
5. Unit test in `tests/KingsValley.Core.Tests` with the rule and the address in the name/comment.

## 4. Memory map (game RAM, the same on MSX1 and MSX2)

| Address | Content |
| :--- | :--- |
| `E203` | **logic call** clock: +1 on each pass of the main loop. The loop runs inside the VBL handler, but the guard `E206` skips ~half of the vblanks → **~30 Hz** (measured: ~30.7/s). Routines that need phase use `E203 & 1` (every 2 calls), `& 3`, `& 4`, `& 0x80` — it is a **phase generator**, not a loop divider. **1 call = 1 port tick** — see "Main loop" below |
| `E207`/`E208` | keys (edge / held): bit0 up, bit1 down, bit2 left, bit3 right, bit4 action |
| `E241` | pyramid (1..6) · `E242` stage · `E243` current room · `E244` room position in the grid |
| `E280` | player state: 0 normal, 1 jump, 2 ladder, 3 fall, 4/5 death, 8/9 throw, 10..13 dig, 14 intro |
| `E281/E282` | Y (fraction/integer) · `E283/E284` X (fraction/integer) |
| `E285` pose · `E286` action timer · `E287` item in hand (1 knife, 2 boomerang, 3 shovel, 4 pickaxe, 5 hammer, 6 drill) |
| `E288` latched keys · `E289` direction locked in the jump · `E292` jump vy · `E294` direction (0 left, ≠0 right) · `E295/E296` animation phase/timer |
| `E298` death (special pose set) · `E2A2` target cell of the digging · `E2A5` head touching |
| `E2C0` | sarcophagi: 8 × 5 bytes `[0x80 armed \| type][counter][room][y][x]` |
| `E2F0..E2F7` | door: `F1` y, `F2` x, `F3` room, `F4` total stones, `F5` missing stones, `F6` frame, `F7` timer |
| `E300` | items/objects: 64 × 8 bytes; byte0 = (state<<4)\|type |
| `E500` | enemies: 8 × 0x20 bytes (see §5) |
| `E700` | soul stones · `E780` room grid · `E800` SAT (buffer) · `D200` sprite colors (buffer) |

### Main loop, state dispatcher and the clock (important for timings)

Discovered while porting the door exit; it applies to **every** counter in the game:

- The VBL handler (`402D..408E`, hung on the BIOS hook at `FD9F`) does, per frame: `CALL 0x541C`
  + **`CALL 0x46D4`** (`4086`), with a re-entrancy guard at `E205`. The `0x46D4` (`mode_frame`) calls
  `mode_tick` + `mode_input`.
- **But the effective logic does NOT run at 60 Hz.** The ISR itself has a second guard, at **`E206`**
  (`htimi_isr`, `0x4037`): if `E206 != 0` the handler does `JP 0x40xx` and exits **without calling
  `CALL 0x6006` (`tick_entry`, bank 04)** — the frame's heavy work. Since that work takes
  more than one video frame, ~half of the vblanks are skipped and the logic loop runs at **~30 Hz**.
  Measured in the emulator (watchpoint at `E203`, 46 s): **~30.7 increments/s** ✓. It matches the three
  movement measurements: mummy 1.5 px/call = 45 px/s; Vick +2 px every 2 samples of 1/60 s =
  60 px/s; and the end-of-stage text step (180 units) in 6 s = 30 units/s.
- `46DA` is the "frame step": it increments **`E203`** (`46E4`), touches `E21A`/`E215` and then does
  `LD A,(0xE200); CALL 0x4099` → jump through the state table, which starts right after the `CALL`
  (`46ED`). Indices: 0→`4709`, 1→`4768`, 2→`4787`, 3→`47C1`, 4→`48AE`, 5→`4942`, 6→`4A1D`, 7→`4A3D`,
  8→`4AEE`, 9→`4B06`, 10→`4B4C`, 11→`4B5D`, 12→`4B6E`, 13→`4BAF`, 14→`4778`, 15→`4725`, 16→`475C`.
- **Each state chooses its own step**, but most gameplay ones have **no** gate: `mode_play`
  (`4942`) and `mode_clear` (`4B06`, the door exit) call the handler **on every call**. The gate
  `LD A,(0xE203); RRA; RET nc` appears at **other** points, and there it is a **phase generator**, not a
  loop divider: `boot_wait` (`472B`, bit 0), `over_wait` (`4A76`, bit 0), `hud_meter` (`5FC6`,
  bit 0), `tick_delayed` (`and 3`), `knife_anim`/`boom_anim` (`and 1`), `dir_left` (`and 4`),
  `home_vic` (`and 0x80`)… In other words: **1 call = 1 port tick**, and the `and`/`rra` becomes parity or
  counting in C# (e.g. the tool animation advances when `TickCount & 1 == 0`).
- Practical consequence: **every ROM counter counts 1 per call**. Use the ROM value directly in
  ticks — never divide by 2. (This document said the opposite; the fix for the tools and the
  door exit, in Oct/2026, was precisely because of that.)
- `CALL 0x4099` = "jump to `table[A]`", with the table starting at the return address
  (`408F`/`4094` are the helpers `HL += A` / `DE += A`). `JP 0x0047` = state change through a routine in
  RAM (`C` = target state).

### Text engine (`51D0`/`51DA`) and how to find the strings

Discovered while porting the end-of-stage screen — it applies to **all** text in the game:

- `51D0` (full script, `HL` = list) and `51DA` (codes only, `DE` = position) draw **tiles**,
  not strings: each code goes to the VDP via `51E8` → `5207` (command, `A=1`) → `523D`
  (tile `n` is at `H = (n & 0x1F)*8`, `L = (n & 0xE0)>>2`). `D` advances **8 per tile** — so the pair
  `(D, E)` is **x and y in pixels** and the text is centered by hand (`(256 - 8·n)/2`).
- Format: `[D][E]` position, codes…, `0xFF` = end, `0xFE` = new position (`0xFE [D] [E]`).
- Encoding (measured): **`0xE1..0xFA` = `A..Z`**, `0xD0..0xD9` = `0..9`, `0xDA` = `©`, `0xDB` = `!`,
  `0x00` = space. The `port` covers A-Z/digits/`!` in the `kv2` atlas (the `!` went in with the Oct/2026 repack).
- **Address → bank**: the ROM has 16 pages of 8 KB; the page covering each window changes with the game.
  Measured: `0x71E6`/`0x697C`/`0x6A0D` are in **page 1** (window `0x6000`, offset = address),
  `0xB256` (the end-of-stage screen script) in **page 12** (window `0xA000`) and `0x5F0E` (the label
  `REST`) in **page 0**. To find a string, search the bytes of the encoding above in the whole ROM
  (`python -c "rom.find(...)"`) — it is faster than guessing the bank.
- Routines mentioned: `4D1F` prints the current player number (the REST) at position `DE`; `4E98` redraws
  the HUD; `697C` generates the **password** and draws it with the label from `69FF`.

### 4.5 MSX2 password (`697C` → `6994`, verification `6A98`, specials `6B24`)

| Step | Address | What it does |
| :--- | :--- | :--- |
| Generate | `6994` | `EDE2 = E242` (**stage**), `EDE3 = E240` (**REST**), `EDE4 = R` (refresh register — **random**), `EDE5 = sum`; rotates the (stage, REST) 16-bit pair **right** by `R & 7` |
| Emit | `69E0`/`69F5` | each byte becomes **2 letters**: `'A' + nibble`, **high** nibble first (`A`..`P`) |
| Verify | `6A98` | letters → values (`−0xE1` letter, `−0xD0` digit), requires `< 0x10`, sum of the first three **==** the fourth (`6AA8`), rotates the pair **left** by `b2 & 7`; rejects stage 0 / `≥ 0x3D` and REST 0 |
| Specials | `6B57`/`6B5F` | `FESTIVAL` and `TRYAGAIN` (ASCII) compared **before** the math (`SUB 0x41`, at `6B47`) |

- **Proof** (what the original shows): the longplay displays `EAEAODGD` at the end of stage 1; the 4th byte `0x63`
  is `0x40 + 0x40 + 0xE3` (the sum) and the rotation of `0xE3 & 7 = 3` gives back `(0x02, 0x02)` = stage 2, REST 2.
- ⚠️ The **msxnet calculator** (`[lives%10][stage/16][stage%16][lives/10]` + `AMCN`) that the port used
  **does not** close with that verification (the sum of `6AA8` does not match) and the `AMCN` chain does not exist in the ROM
  (searched across the 16 pages). It was replaced by the `6994` format — see
  `src/KingsValley.Core/Stages/StagePassword.cs`.
- 🔎 **Measured on 2026-10-17 (openMSX 21.0, `work/gen/st6b.tcl` + `work/gen/pwneg.tcl`)**: the
  **calculator** password — the one the extraction scripts use (`pw` from `work/stage2.tcl`:
  `[lives%10][stage/16][stage%16][lives/10]` + `AMCN`) — **is accepted** by the MSX2 ROM. `JAGJAMCN`
  (stage 6, lives 99) enters the stage: HUD `STAGE 06 / REST 98` and the room comes out identical to the original in
  the dump rendering. The negative control (`QQQQQQQQ`) does **not** enter: it goes back to the title screen.
  ➡️ So the ⚠️ above is **wrong or badly scoped**: the verification of `6A98` accepts the calculator
  family, and the `6994` format is what the *end-of-stage screen* prints. **Open question:**
  decide whether `StagePassword` goes back to the calculator format, or whether both formats are valid
  families (the port keeps writing the `6994` format, which is also accepted).
- `732C` (`INC (0xE240)`) runs **after** the REST print by `4D1F`: the end-of-stage screen password
  is worth **REST + 1** (longplay: screen "REST 01", password that gives back REST 2). In the port:
  `GameRun.ClearScreenPassword` (the "common" `Password` follows the match state).

## 5. Enemy structure (`IX` = E500 + 0x20·n)

| IX+ | Field | IX+ | Field |
| :--- | :--- | :--- | :--- |
| 0 | type (1..4; 5 = death effect) | 0x0E | auxiliary counter (B208/B211) |
| 1 | state | 0x10 | room |
| 2/3 | Y fraction/integer | 0x11 | sprite frame (0xFF invisible) |
| 4/5 | X fraction/integer | 0x13 | bit0 kills on touch, bit1 can be hit |
| 6 | moving (the 656C integrator only moves with 1) | 0x14 | timer |
| 7/8 | vy 8.8 | 0x15 | sarcophagus index |
| 9/0A | vx 8.8 | 0x16 | room position in the grid |
| 0B | direction: 0 up, 1 down, 2 left, 3 right | 0x18 | wall hits (types 1/2) / saved X (type 3) |
| 0C/0D | room map pointer | 0x19 | saved direction · 0x1A..0x1D type 3/4 uses |
| 0x1E/0x1F | colors of the 2 sprites (MSX2) | | |

Dispatcher by type: `65F6` → type 1 `AD80`, 2 `AE23`, 3 `B2AA`, 4 `B68E`, 5 `662D`.
Integrator: `656C` (adds the velocity in the direction; edge → neighboring room). Terrain tests:
`B15E` nothing under the feet (x+2, x+10), `B165` can walk (3 points on the edge), `B17A` fall continues,
`B833` snap to the cell, `B223` direction toward the player, `B23F` player above/below.

## 6. Status

### Done (ported from the ROM and validated)

- Exact sprites and poses (`art/rom_sprites.py`, `art/rom_tiles.py`): Vick (7 sets + death + door),
  enemies (26 frames × colors), projectiles, items, sarcophagus (3 drawings), door (3 frames + pin),
  crumbling blocks.
- Player: collision (98E2/9910), walking with snapping, **jump with the original's quirk** (1 point at y+8,
  landing at any moment with snapping), fall, poses, death, throw (timing), digging with the 4
  tools (pickaxe goes down, drill advances).
- Sarcophagi (671D) and **white mummy, type 1** (AD80): `StageSession.Mummy.cs`. 0 differences.
- Enemy death effect (6608/662D/6706), contact (9AB1), knife hit (9AFE), score (100 / 500).
- Door: frames, pins per missing stone (table B9AD), automatic entry (8FBE), exit (71F6).
- **Door entry art**: routine `5937` decompresses **only two** sources — `0xAB59` into `0xE000`
  (1 pose) and `0xAA00` into `0xE080` (4 poses) —, that is, **5 pattern lines** (`vick_d_00..04`). The
  original's index (`7244`: `2*E258 + bit4(E259)`) goes up to 7, but lines 5..7 receive no art: line
  5 stays **empty** (the normal empty-handed set does not write that line) and 6/7 hold
  **leftovers** from the Vick set assembled earlier by `58DB` (`montage_vick_door.py` shows all 8). Since
  the SAT is only cleared at `7288`, the original *draws* poses 5..7 with that leftover; the port draws
  only 0..4 and does not draw 5..7 (`PlayerPoses.DoorArtPoses`).
- **Door exit sequence** (`StageSession.StepExit`, steps 1..3 of `71E6`): opening (90
  units = 1.5 s), Vick entering (128 units: **8 groups** of 16 units of the index `2*E258 +
  bit4(E259)`, order `1, 0, 3, 2, 5, 4, 7, 6` — `E259` counts from `0x20` downwards, so bit 4
  is 1 in the first half of each group —, with +2 px of ascent every 16) and
  curtain (12 bands of 8 px per side). **266 frame units** = 133 ticks ≈ 4.43 s, matching the
  longplay. Vick is invisible during the opening (the auto-enter clears the SAT at `907F` → `5D41`) and comes back in
  the entry pose; fanfare 0x23 (`E249`/`428A`) replaces the stage music during the exit.
  ⚠️ The interleaved order matters: with the monotonic sequence `0,1,2,3,4` pose 3 (stepping) came
  after 2 (already sunk) and Vick "rose again" — the "crouch/flatten" symptom.
- **End-of-stage screen** (steps **4..8** of `71E6`, `StageSession.StepClearScreen` + the drawing in
  `RoomRenderer.DrawClearScreen`): the text from `B256` (bank 12) — `NICE ! / LETS GO NEXT STAGE /
  SPECIAL BONUS` — with the label `REST` (`5F0E`) and the number, 180 frame units (3 s) on screen;
  then the password from `697C` (`PASS WORD` + the 8 characters generated in `EDD9`), the wait for a key
  (`733B`) and 32 final units (`7342`). The text positions are the `(D, E)` pair of the
  `51D0`/`51DA` engine and match **pixel by pixel** the longplay (the `!` entered the atlas in Oct/2026 and the password
  started using the ROM's real format, see §4.5 — it changes letters on purpose, because the `R` of the
  original is random).
  The HUD disappears on that screen (the longplay confirms: the bottom band goes black).
- Sounds by original ID at the exact point of the call (`OriginalSounds.cs`).
- **Opening (Konami logo)** — `title_load` (`5B6A`, bank 0) loads the palette `BB8B` and the art;
  `wmap_slide` (`5BC8`) reveals the screen **line by line** with HMMM from `(40,320)` (page 1) to
  `(40,64)` (NX=168, NY=1..48), **one line every 2 frames** of 60 Hz through the `and 1` gate on
  `E2C0` → 48 lines in **1.6 s**. Step 49 (`E2C1` reaches 0) only sets `E2C2` — 2 frames
  after the last copy — and `boot_clear` (`4742`) counts **256 decrements** of `E204`
  (`sub_delay` with A=0: 0 → 0xFF..0x00) before turning the screen off and assembling the Japanese title:
  the logo stays complete for **258 frames ≈ 4.3 s**. The art is the 168x48 crop at `(40,320)` (the
  staging, identical to what is displayed at `(40,64)`) from the VRAM dump `bootlogo3/h0980` with the game's
  palette (#FF6D00, #DA2400, #919191) and matches **0 px** against
  `gfx/stamp_logo_konami.png` from the `0xe1f/KingsValley2` disassembly. In the port: `SplashScene`
  (same position and cadence; the pyramid remains as the transition to the menu).

### Enemies — all 4 types ported and validated against the emulator

| Type | File | Validation (`emulator/trace_enemy.tcl` trace × `simtrace`) |
| :--- | :--- | :--- |
| 1 white mummy (`AD80`) | `StageSession.Mummy.cs` | stage 1: 0 differences over 544 ticks (`work/tr1`) |
| 2 yellow mummy (`AE23`) | `StageSession.GoldMummy.cs` | stage 3: 0 differences over the **whole life** — 517 ticks, including the 50 on the ladder and the whole death (`work/tr3`) |
| 3 the hopper (`B2AA`) | `StageSession.Guard.cs` | stage 5 (cloud + 12 hops): 0 differences over 476 ticks; stage 42 (**ladder + fall**): 0 differences over 700 ticks — two 47-tick climbs and the falls (`work/tr5`, `work/tr42`) |
| 4 the rock (`B68E`) | `StageSession.Rock.cs` | stage 13: 0 differences over 1504 ticks (full cycle, 4 turns, disappears; includes the type 5 death effect) (`work/tr13`) |

The traces are versioned in `tools/kv2-extract/work/tr<N>/trace.txt` (the evidence for `compare.py`,
to revalidate without needing the emulator). Each one's `gen/tr<N>.tcl` reproduces the capture.

Notes worth continuing from:
- Type 2: pause → turns **toward** the player (AFAA); while walking it enters the ladder if the player is at another
  height (B23F) and there is a ladder in the 2 cells (B150 body / B143 feet); ladder at 0x280; leaves when it ends (B0F9).
- Type 3: cloud 48 ticks + statue 16 (colors stay 0A/47); on the ground it waits 8 ticks; 17-tick hop
  (vx 0xF0, Y from table 0xB595, down 0xB5A6 if the head hits); ladder at 0x100 with frames 13/14.
- Type 4: records the wall in the map (`RoomMap.SetRock`, B9A2) when stopped; the knife does not kill it, it only
  turns it into a block (B8C5); blinks 0x3C, rolls 0x200, block 0xB4, bounces (0xB926), disappears on the 4th turn.
- The comparator follows the original's **slot 0**; with 2+ sarcophagi the order of our list may differ
  after the first disappearance (not a logic error).
- **Where the trace ends:** on the frame in which the original **restarts the life** (slot 0 zeroes and `E280`
  becomes `14`, intro) the port has already switched sessions — comparing beyond that makes no sense. The cuts
  above are all of that kind (544, 517, 476, 700, 1504), not logic divergences.
- Player death: the original holds the screen for 222 ticks in state 5 (`GameScene.DeathHoldTicks`).
  At the **end** of the animation (the `AnimPhase == DeathSpins` of `StepDeath`) the original still runs one game
  frame: the enemy walks once more and it is the next state that freezes it. Measured in `work/tr3`
  (stage 3, type 2): with the state change in the middle of the tick the enemy stopped **1 px** before the
  original; the `_deathEnded` of `StageSession.Tick` closes that and stage 3 now matches over the whole life.

### Room assembly (`44D4`) — decoded and validated (0 px of difference)

`44D4` assembles **the whole room** on screen, not just the background. The `41xx` calls are just mapper bank
switches for the SCC (on the SCC, `0x7000` → window `0x6000`, `0x9000` → `0x8000`, `0xB000` → `0xA000`;
`41A6`/`41AB`/`41B0`/`418A` write the three registers, `41B5`/`41BA`/`41BF`/`41CC` only `0xA000`).
The exact order:

| # | Call | What it draws | Where the data is |
| :--- | :--- | :--- | :--- |
| 1 | `41CC` | — | bank 14 at `0x8000`, bank 15 at `0xA000` |
| 2 | `44F2` | **mural** (opaque copy), 32 8×8 codes repeated 4× horizontally and 6× vertically | pointer `word(0x7FFE + 2*pyramid)` + `0x20` if the stage is **odd** (`44FE`), bank 14 |
| 3 | `4606` | **decorations** (transparent) | list `word(0x806A + 2*stage)`: **3-byte records** `[y&#124;type][x&#124;(room−1)][extra]`; graphics in `word(0x800C + 2*((pyramid−1)*8 + type))`, bank 14 |
| 4 | `41B0` | — | bank 10 at `0x6000`, 11 at `0x8000`, 12 at `0xA000` |
| 5 | `4535` | **room map** (transparent) and, at the tail of the same loop, the **golden ladders** | RAM `0xE900 + (room−1)*224`, 2 bits per cell, 768 cells; golden: RAM `0xE7C0` (see below) |
| 6 | `418A` | — | bank 1 at `0x6000`, **2 at `0x8000`**, 3 at `0xA000` |
| 7 | `46A4` | **sarcophagi** (transparent) | RAM `0xE2C0`, 8 records of 5 bytes; draws if `(type & 0x7F) < 3` and `record[2] == room` → **3 rows × 2 columns** of `0x93AC` at `(x = record[4], y = record[3] − 8)` |
| 8 | `651D` | **soul stones** (transparent) | RAM `0xE700`, 16 records of 8 bytes; if `record[0] != 0` and `record[1] == room` → **2×2** of `0x9324` at `(x = record[3], y = record[2])` |

`44D4` starts and ends with the loop over the 8 enemy slots (`E500 + 0x20·n`, only when
`(IX) == 4`): `ba3c` before (`CALL 0xb9e0`) and `ba54` after (`CALL 0xb992`) — it is what erases and
redraws the sarcophagus. The useful reading is the table above.

Outside `44D4` but always visible on screen:

| Object | Routine | Key (RAM) | Graphic (all in bank 2) |
| :--- | :--- | :--- | :--- |
| **items** | `9116` (called from the `90F7` loop) | `0xE300`, 8 records of 8 bytes `[type, y, x, room, anim, …]`; `type & 0xF0` discards (item already collected) | 2×2 of `0x9328 + 4*(type − 1)` — 6 types, up to `0x933F` |
| **door** | `9010` | `0xE2F1..0xE2F6` = `y, x, room, width, height, frame` | 4×4 of `0x9340 + 16*frame` (3 frames) + the **pins**: `41BF` (bank 13 at `0xA000`), pointer `word(0xB9AD + 2*width)`, `height` records `[x += (v&3)*8, y += ((v&12)<<1)]`, pin `0x60` or `0xAE` depending on `0xF0F4` |

The block codes are 8×8 indices in the pattern page (`n → x=(n&31)*8, y=(n>>5)*8`, the same
calculation as `523D`). The 2-bit map uses `0` = nothing, `2` = block `5`, `3` = block `0x5E` and `1` =
**ladder**: `3` in the first cell of the pair and `4` in the next one (the `CP 3` of `45CE` is redundant — the
two branches do `LD A,3`).

#### Golden ladders (the tail of `4535`, `4576`→`45C8`)

The same map loop **ends** by drawing the golden ladders: `4581` bails out if `E200 == 11`;
`4587` walks the **16 entries** of `0xE7C0` (4 bytes: `[room][y][x][height|0x80]`, written by
`44AC` from table `0x60F0` of bank 10 — the **same** one the port already reads for `goldenLadders`),
filters by room and calls `45A7`:

1. `56DE`/`56F8` **repaint the mural** in the area of 2 columns × `height` rows (transparent; `56F8`
   finds the mural code again by the `y` band, `(y&0x18) + ((x>>3)&7)`, the same calculation as the `44F2`
   loop);
2. `4690` draws **blocks `1` (left) and `2` (right)** — the bytes of `0x45C9` — on each row,
   always the same two codes (`HL` is reloaded on each row).

The repaint exists because blocks `1`/`2` are **transparent in the bottom half** (rows 5–7 are
`fa9f0000` / `0000fa9f`): without it the normal ladder (blocks `3`/`4`, from the map) would show underneath the
golden bars. It was that detail that kept pyramids 5 and 6 from matching.

**Validation:** `art/rom_room.py` assembles the room as above and `work/room_build.py` compares it with the
4bpp bitmap of the dump's VRAM `0x0000−0x5FFF`. One valid dump per pyramid (the **1st stage** of each, in
`work/pyr/sN`, read at `t=36 s`) gives **0 px of difference in 49152** in **five of the six**:

| Dump | Pyramid / stage | Result |
| :--- | :--- | :--- |
| `work/pyr/s1` | 1 / 1 | **0 px** (0/768 blocks) |
| `work/pyr/s11` | 2 / 11 | **0 px** |
| `work/pyr/s21` | 3 / 21 | **0 px** |
| `work/pyr/s31` | 4 / 31 | **0 px** |
| `work/pyr/s41` | 5 / 41 | **0 px** (golden ladder of `E7C0[0]`: `y=64 x=160 alt=8`) |
| `work/pyr/s51` | 6 / 51 | 779 px / 16 blocks — **only** the rotating door (see below) |
| `work/pyr/s2` | 1 / **2** (even) | 645 px / 12 blocks — only the 2 type 1 mechanisms (`y=72 x=160`, `y=104 x=80`) |
| `work/pyr/s12` | 2 / **12** (even) | 214 px / 8 blocks — only the type 4 mechanism (`y=128 x=80`) |
| `work/room1/a36` | 1 / 1 | **0 px** (the reference dump, room by room) |

**The EVEN mural variant is validated**: the `s2`/`s12` dumps (stages 2 and 12, the `+0` of table
`0x7FFE`, against the `+0x20` of the odd ones) close the mural, the decorations and the map; what is left are the
mechanisms. And the **block table is per pyramid and stable across stages**: `rb.page1` of `s1` and `s2`
have the 256 used blocks **identical** (same for `s11`/`s12`), although the rest of the page (outside the 32×8
block grid) differs — VRAM garbage.

In `s51` the 16 remaining cells are exactly the **two type 1 mechanisms** of `E600`
(`y=152 x=40` and `y=152 x=200`, 2×4 blocks each) at the instant of the dump — a **rotating** object, drawn
by the frame loop and **not** by `44D4`. The block on screen there does not exist in page 1 (not even as a
transparent overlay of any of the 256), and the two copies of the same door have different pixels from each
other (`(19,5)` and `(19,25)` diverge on 3 rows): it is the rotation, not the background. It is left for item 6.

`a39` (same room, half a second later) differs only in the sarcophagus's 6 cells: it is the **sarcophagus
opening** — `46A4` draws `0x93AC` while the enemy is dormant and the `AD53` of the enemy state machine
(bank 3) draws `0x93B2`/`0x93B8` on top when it wakes up. Dynamic state, not background.

**Pillar (`4679`):** the `xi == 3` decorations use `C = record[2]` (the 3rd byte) as the height, but
`4690` draws **`C − 1` rows** and only uses `gfx[0..3]` (row 0 with `gfx[0..1]`, the following ones with
`gfx[2..3]`) — the old note about a "bottom row" with `gfx[4..5]` was wrong.

### To do (in order)

1. **End-of-stage screen — closed**: the `!` glyph (0xDB) entered the atlas (Oct/2026 repack) and the
   `E240` of step 5 is explained: it **is the REST** (`4D1F` prints it at `0x8868`), the `DAA` of `7320`
   gives the "+1" of the "SPECIAL BONUS" (the port's `RestPerStage`) and the `INC (HL)` of `732C` runs **after**
   the print, so the screen's password is worth REST+1 (`GameRun.ClearScreenPassword`).
2. **Type 3 trace on the ladder/fall and type 2 on the ladder — closed.** Type 3 (stage 42, `work/tr42`)
   climbs the ladder twice (47 ticks each) and falls, 0 differences over 700 ticks; type 2 (stage 3,
   `work/tr3`) climbs for 50 ticks and matches over the whole life (517 ticks). The trace also found the **extra
   frame** at the end of the death (see the note in the table above) — fixed in the port.
3. `B00F`/`B0A6`: an enemy in another room crosses into the player's room. **Ported** (commit a186498). Full reading of `B00F`/`B0A6` (bank 2, called from `ADD4` in the mummy's walk and from
   `AEC6`/`AF5D`/`B4BB`/`B754`):
   - `B00F` bails out (0) if `E243 == IX+0x10` (the enemy is already in the player's room) or if `IX+11`
     (the direction) is 0. Otherwise `9A0B` gives `D` = x and `E` = y of the PLAYER, and `L`/`H` = y/x of the ENEMY
     (`IX+3`/`IX+5`), and the direction picks one of the 3 blocks (the 4th, with `IX+11 >= 4`, is unreachable):
     | `IX+11` | requires from the enemy | requires from the player | returns |
     | :--- | :--- | :--- | :--- |
     | 1 | `y >= 0x98` | `y < 0x10` and `\|x − D\| < 0x30` | 2 |
     | 2 | `x < 0x18` | `x >= 0xD8` and `\|y − E\| < 0x30` | 3 |
     | 3 | `x >= 0xD8` | `x < 0x18` and `\|y − E\| < 0x30` | 4 |
   - before returning, `B096` calls `5E38` with `B = IX+0x16` (the room's cell in the grid) and compares the
     neighbor with `E244`: it only returns if the neighboring room in the movement direction is the player's.
   - the caller (`ADD4`) does `DEC A`; `CP (IX+11)`; if different, it exits — so `B0A6` only runs when the
     returned direction "matches" (that is how it tests for non-zero).
   - `B0A6` adds the delta from `0xB0F1` (4 words, per direction): **`10 00`** (+0x10 in Y), **`f0 00`**
     (−0x10 in Y), **`00 10`** (+0x10 in X), **`00 f0`** (−0x10 in X); it tests the 4 corners with `B0E5`
     (`98E6`) and writes `IX+3`/`IX+5` = original position + delta (`B216`/`B21C` reload `L`/`H` from
     `IX+3`/`IX+5`, the `B0D7`). The `C`/`B` at the end are the delta's (`B0B6`–`B0D3` only touch `L`/`H`
     for the corner tests).
   - ⚠️ **Open point — RESOLVED**: the table says direction 1 goes up (delta f0 00 =
      -0x10 in Y), 2 to the right (00 10) and 3 to the left (00 f0). The B00F conditions are
      consistent with that: direction 1 requires the enemy at the bottom (y >= 0x98) and the player at the top
      (y < 0x10), 2 requires the enemy on the left (x < 0x18) and the player on the right (x >= 0xD8), 3 the
      opposite. B00F tests the edge the enemy leaves from (the edge opposite to the movement) — with no
      ambiguity. The port↔ROM mapping is romDir = portDir ^ 1 (0↔1, 2↔3). Ported in
      StageSession.Mummy.cs (TryTraverseRoom, called in MummyWalk and MummyPause when CanMove fails).

Old reference (partial, kept):
   `B00F` returns the direction (1..4) that leads to the player's room, or 0 if the enemy is already in it (`E243`
   = player's room, compared with `IX+0x10`); the choice comes from the edge the enemy is on and the
   player's position (`CALL 9A0B` → D/E) and the four routines are symmetric (e.g. direction 4 requires
   `y < 0x18`, player with `E >= 0x98` and `|x − D| < 0x30`). The caller does `DEC A` + `CP (IX+11)` (the
   current direction) and only then jumps to `B0A6`, which adds the delta from table `0xB0F1`
   (`10 00`/`F0 00`… = ±0x10) and tests the four corners with `B0E5` (`98E6`) before writing the new
   position to `IX+3`/`IX+5`; `B096` (`5E38`) is what decides the room change. Still missing: reading `5E38`,
   `9A0B` and `408F`.
4. **Exact VRAM backgrounds + color cycle**: **closed.** The room assembly is closed
   (5 of the 6 pyramids with 0 px; the 6th only differed in the rotating mechanism, now extracted and
   ported). The 154 PNGs in `Content/Rom/Art/Backgrounds/` were replaced by
   `RoomAssembler` + `RoomBackgroundBuilder` (mural per pyramid/parity + 8x8 blocks +
   decorations). The animation of color 6 (`5C14`) is ported in `PaletteAnimator` (loaded from
   `animations.json`).
5. **Player measurements**: **verified.** Up: probe `y + 0x0A` (`A5A2`, port
   `LadderUpProbe`); down: `feet = p.Y + Physics.ActorSize` = `y + 0x10` (`A591`); landing at the
   top: `(FloorDiv(probe) + 1) * 8 - ActorSize` (`A20F`). Side room exit (`A67C`):
   `cx < 0` / `>= 256`. Score: `Scoring.Enemy = 100` (BCD `0x100`, `9B76`),
   `Scoring.SoulStone = 500` (BCD `0x500`, `9A04`). Knife/boomerang flight already left
   "estimated" (literal step `4` of `A72A`, boomerang via `Physics.BoomerangSpeedAt`,
   range = 23 ticks).
6. Mechanisms (`E600`): **all 5 types now identified/ported** (see below).
   room assembly (see the leftovers of `s2`, `s12` and `s51` above). What is already read:
   - **Loading/state** (`633F`/`639E`, bank 1): reads the stage table and fills 16 entries of
     **16 bytes** at `0xE600` from `IX`; `(IX+0)` = type, `(IX+2/3)` = y, `(IX+4)` = x,
     `(IX+8)` = one height bit (`(byte>>3)&0x1F`), `(IX+7)` bit 1 = "shift 8 px" (only type 2 with
     `BIT 1`), `(IX+5)` = 3× the index only for type 1, `(IX+9)` = the low nibble of the 1st byte.
   - **They are not in the pattern page**: the mechanism's pixels on screen do not match any of the 256
     blocks (not even as a transparent overlay of any of them) — the drawing comes out another way:
     `63B5`+ blits via `4D7B`, not via `5219`/`5207` of `44D4`.
   - **Observed size**: 2 blocks wide × 3 (stage 2) to 4 (stage 51) high.
   - At rest the drawing is **static** (the two `s51` dumps, 3 s apart, have the same

   - **Types 2 and 3 (1 cell wide)**: identified as **invisible walls** - their
     cells in the map are 3 (wall) and the drawing (0xBC50 / 0x6483) reproduces the mural
     underneath (0 px of difference in s3), so there is no art of their own to extract. Type 2 has 2
     variants (0x937C/0x9384/0x938C or 0x9394/0x939C/0x93A4, selected by (IX+7) bit 1)
     and type 3 is a drawing no-op (0x6460). The port already treats them as walls
     (IsSolid = true, no special tile).
   - **Type 4 (4 cells wide x 1 high)**: drawn with block 0xAD (a "skull/oval"
     with 0s at the edges, that is, transparent at the edges) repeated 4x; the art comes from 0x2008 via HMMM
     (0xBDDE) and block 0xAD (or 0x61 if 0xF0F4=0, but 0xF0F4=1 always) is blitted on top. Ported:
     mechanism_t4.bin (128 bytes = 4x block 0xAD) + RoomBackgroundBuilder (BlitTileWide 32x8).
     core) — the rotation must be triggered by pushing, not continuous.

## 7. Port files (C#)

| File | Content |
| :--- | :--- |
| `Core/Gameplay/StageSession.Player.cs` | player movement/poses (A190, A2F0, 9F9A…) |
| `Core/Gameplay/StageSession.Dig.cs` | digging (AA66..AC58) |
| `Core/Gameplay/StageSession.Mummy.cs` | sarcophagi, type 1, death, integrator, terrain tests |
| `Core/Gameplay/StageSession.Enemies.cs` | enemy loop, contact, knife; types 2..4 still on the measured AI |
| `Core/Gameplay/EnemyFrames.cs`, `PlayerPoses.cs` | frames/poses |
| `Core/Gameplay/OriginalSounds.cs`, `DoorStudLayouts.cs` | sound IDs, door pins |
| `Engine/Scenes/RoomRenderer.cs` | drawing (sprites `vick_s{set}_{pose}`, `enemy_t{type}_{frame}`…) |
| `tools/kv2-extract/simtrace/` | headless simulation + `compare.py` |
