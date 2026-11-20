# NEXT_SESSION — work plan

A resume plan. Each task has **where to change**, **how to prove it** and **what has already been
tried** (so as not to repeat dead ends). Suggested order = the order of the sections.

Broader context: [ROM_PORT_HANDOFF.md](ROM_PORT_HANDOFF.md) (backlog and decodings).
Project rules: [../AGENTS.md](../AGENTS.md).

---

## Task 1 — Door entry art (Vick "squashing") ✅ done in `37f3357`

**Symptom:** when entering the door Vick seems to crouch/flatten; it should read as
"walking in".

**Cause (measured in the ROM, `7222`/`7228`/`7244`):** the pose index is `2*E258 + bit4(E259)` and `E259`
counts from `0x20` **downwards**, so bit 4 is 1 in the *first* half of each group of 32 units
→ the real order is **1, 0, 2, 3, 2, 4, 5, 4, 6, 7, 6**. The port used the monotonic order `0,1,2,3,4`, which made
pose 3 (a stepping one) come **after** 2 (already sunk) and Vick "rise again" in the middle of the entry —
the crouch/flatten symptom.

**Done:**
1. `StageSession.StepExit` (`Entering` case): `p.Pose = 2*(t/32) + ((t/16)%2 == 0 ? 1 : 0)` — the
   original's interleaved order (the `Y -= 2` every 16 units was already right).
2. `RoomRenderer.DrawPlayer`: the cut became `PlayerPoses.DoorArtPoses` (5, renamed from
   `DoorPoses`); poses 5..7 remain undrawn, as in the game.
3. Test `EnteringTheDoor_CyclesTheRomPoses_InInterleavedOrder` pins the sequence `1,0,3,2,5,4,7,6`, and
   the door test was adjusted to the new order.

**⚠️ Correction to original step 1 (the hypothesis was wrong):** **there are no 8 art poses.** Routine
`5937` decompresses only **two** sources — `0xAB59` → `0xE000` (1 pose) and `0xAA00` → `0xE080` (4 poses) —
that is, **5 lines** of `0x80` bytes (`vick_d_00..04`), and the extraction (`art/rom_sprites.py`) was already
right. Lines 5..7 are **not** cleared: line 5 stays **empty** (the normal empty-handed set does not
write that line) and 6/7 hold **leftovers** from the Vick set assembled earlier by `58DB` — since
the SAT is only cleared at `7288`, the original *draws* poses 5..7 with that leftover. The port draws 0..4 and
does not draw 5..7 (cleaner than the leftover). Evidence: `work/montage_vick_door.py` →
`work/png/check_door_leftover.png` (the 8 lines assembled from the ROM, 58DB + 5937).

**How to prove (done):**
```powershell
$env:KINGSVALLEY_START_SCENE="level"; $env:KINGSVALLEY_LEVEL="2"; $env:KINGSVALLEY_SKIP_LEVELSTART="1"
$env:KINGSVALLEY_EXIT_HOLD="1"; $env:KINGSVALLEY_WINDOW="256x212"; $env:KINGSVALLEY_SCREENSHOT_FRAME="85"
foreach ($n in 50,55,63,71,79,87,95,103) {
  $env:KINGSVALLEY_EXIT="$n"; $env:KINGSVALLEY_SCREENSHOT="C:\temp\ex_$n.png"
  dotnet run --project src/KingsValley.Desktop
}
```
The log confirms the sequence: tick 50→pose 1, 55→0, 63→3, 71→2, 79→5, 87→4, 95→7, 103→6, with
`Y = 48,46,44,42,40,38,36,34` (−2 every 16 units).

---

## Task 2 — Mechanisms: rotation when pushing ✅ done in `efe8782` (extraction) + `pending` (trigger)

**Current state:** the rotation is now **extracted and drawn per phase** (4 cuts `mechanism_t1_p0..p3.bin`,
16x24, 4bpp, 0 = transparent), and the drawing uses the current phase (`RoomBackgroundBuilder` blits
`mechanism_t1_p{phase}.bin` at the mechanism's position). The rotating door's state machine
(phase 0..3, timer 10/8, direction by (IX+7) bit 1) and the push trigger (0xBABF..0xBB42)
are ported in `StageSession.UpdateMechanisms` + `StepStanding`; the mechanism's cells went back
to being a wall (`SolidForPlayer` no longer ignores them) and Vick pushes from inside (`PlayerState` 6/7
approximated by `StepStanding`).

**Decoded (0xBB43/0xBABF/0x56DE/0x56F8):**
- the art comes out through `0x56DE` (mural in the area + `0xBB93` on top) with `0x5767` (0x5219) drawing
  blocks through the mural code; the pattern of each phase is a list at `0x9374` with offset `2*IX+5`
  (type 1: the 4 pairs of `0x9374`, type 2: `0x937C/0x9384/0x938C` per row);
- the phase (`IX+5`) goes from 0 to 3 (push to the right) or from 3 to 0 (push to the left), with the
  timer `IX+6` at 10 (rest/continuation) or 8 (first push);
- the direction is (IX+7) bit 1 = `(byte3 & 7) != 0`, and `(IX+8)` (the height) is 3 for type 1 mechanisms.

**Extracted art (measured in the dump, validated pixel by pixel against the mural):**
- `work/mech/phase.tcl` (reproduces the 4 dumps `phase0..3` in `work/mech/s2phases/`);
- `art/rom_mechanism.py` writes `mechanism_t1_p0..p3.bin` (16x24, 4bpp, 0 = transparent):
  p0 = 216 px, p1 = 312 px, p2 = 312 px, p3 = 216 px.

**How to prove:** `KINGSVALLEY_LEVEL=2` (or 51) and push the pushable door; the log of
`UpdateMechanisms`/`RoomBackgroundBuilder` must show the phase changing 0→3 (or 3→0) and the art
turning. The test `RotatingDoor_AdvancesPhase_WhenPushed` covers the state machine.

---

## Task 3 — `B00F`/`B0A6`: an enemy from another room crosses into the player's room

It is **decoded** (table `0xB0F1`, conditions per direction, `B096`/`5E38`) but **not
ported** — see `ROM_PORT_HANDOFF.md` §6/§A to-do item 3, which has the full table of the
conditions and the **open question**: the table says direction 1 goes up and 2 to the
right, while the `B00F` block for direction 1 requires the enemy at the bottom — either `B00F`
tests the opposite edge, or the `IX+11` there is not the movement direction. **Closing this requires
an emulator trace** (enemy blocked at the wall + player in the neighboring room).

**Where to change:** `StageSession.Mummy.cs` (`CanMove` fails → call the crossing), with
`RoomGridNavigator.Neighbor` to test the neighboring room.
**How to prove:** the port's `simtrace` + an emulator trace (same method as handoff item 2), cutting
at the room change.

---

## Task 4 — Player: missing measurements ✅ verified (values already in the ROM)

Checked against the ROM routines (and the constants were already correct in the port):

- **Climbing up/down a ladder** — up: probe `y + 0x0A` (`A5A2`, port `LadderUpProbe = 0x0A`); down: `feet = p.Y + Physics.ActorSize` = `y + 0x10` (`A591`, port `StepClimb` branch `input.Down`), with snapping into the cell by the same criterion as going up. Landing at the top: `(FloorDiv(probe) + 1) * 8 - ActorSize` (`A20F`).
- **Side room exit** — `CheckRoomExit`: `dx = cx < 0 ? -1 : cx >= PlayfieldWidth ? 1 : 0` (`A67C`), with `PlayfieldWidth = 256` — matches the ROM's `cx < 0`/`>= 256`.
- **Score** — `Scoring.Enemy = 100` (BCD `0x100` of `9B76`), `Scoring.SoulStone = 500` (BCD `0x500` of `9A04`), both already in the port.

No code change needed; the values were already correct (checked against `ROM_PORT_HANDOFF.md` §7c).

---

## Task 5 — Palette flavor per pyramid ✅ verified

The static colors were already verified: 5 of the 6 pyramids give **0 px** of difference in the
room assembly (`work/room_build.py`); the 6th (`s51`) only differed in the rotating mechanism, which is now
extracted (Task 2). The animation of color 6 (`5C14`, table `0xBD52`: black→red→white every
4 ticks, with an extra effect on pyramid 4 from `5C3E`) is ported in
`PaletteAnimator` and loaded from `animations.json` (extracted from the ROM). No code change.

---

## Task 6 — Opening: Konami logo (line-by-line reveal) ✅ done in `5dfddeb`

**Symptom:** the old `SplashScene` used 4 cuts from the **C-BIOS** boot (`konami_f00*`) and an invented
animation (slide-up + fade) — it was not the original's.

**Done:**
1. **Exact art**: 168x48 cut at `(40,64)` from the boot VRAM (`work/bootlogo3/h0980`) with the
   game's palette; it matches **0 px** with `gfx/stamp_logo_konami.png` from the `0xe1f/KingsValley2`
   disassembly (same mask on all 3 sides: 520 orange, 514 red, 896 gray).
2. **ROM cadence**: `wmap_slide` (`5BC8`) copies **one line every 2 frames** (HMMM from
   `(40,320)` to `(40,64)`, NY=1..48) = **1.6 s**; the full logo stays **258 frames ≈ 4.3 s**
   (2 for the parity gate + 256 decrements of `E204` in `boot_clear`). `SplashScene` uses
   `SecondsPerLine = 2/60` and `LogoHoldSeconds = 258/60`.
3. `konami_f00*.png` deleted; the pyramid (transition to the menu) and the skip remain.

**How to prove:** build + `KINGSVALLEY_START_SCENE=splash KINGSVALLEY_WINDOW=1024x848
KINGSVALLEY_SCREENSHOT=<png>` on frame 250 (hold) → identical to the art; on frame 60 → 31 lines
revealed (~1 s at 60 FPS). Evidence of the HMMM commands: `work/bootlog/writes.txt`.

**Continuation (Oct/2026):** the fade to the pyramid was **removed** — the ROM has no fade. The
`boot_clear` (0x4742) turns the screen off and calls **`title_jp` (0x5F8D)**, which assembles the **Japanese
title** with a hard cut (`scr_reset` + stamp of `stamp_logo_jp`); then `boot_done` (0x475C) runs
`hud_meter` (the GAME/EDIT box slides in over 0x39 steps of 2 frames ≈ 1.9 s) and goes to the menu. The
pyramid on screen `pyramid.bin` is the stage 1 cutscene, **not** the boot. `SplashScene` now shows
logo → **Japanese title** (`title.bin`), with no alpha.

---

## Task 7 — Time model: **1 ROM call = 1 tick** (rate fix) ✅

**Symptom:** the tools (knife/boomerang) moved **2× fast**, and so did the door exit.

**Cause:** the port treated the tick as "2 ROM frames" and divided the ROM counters by 2. Wrong:
the VBL handler is locked by **`E206`** while the previous frame has not finished, so the effective
logic runs at **~30 Hz** — and each routine runs **1× per call**. Measured with a watchpoint at `E203`
for 46 s: **~30.7 increments/s**; it matches walking (+2 px every 2 samples of 1/60 s = 60 px/s),
the mummy (1.5 px/call = 45 px/s) and the end-of-stage text (180 units in 6 s).

**Done (Core):**
1. **Tools**: `UpdateProjectiles` runs **1 step per tick** (before it was 2 sub-steps of 4 px);
   `ProjectileFrameStep` (4) → **`ProjectileStep`** (4 px/tick); `ToolHoldFrames`/`ToolHoldTicks` →
   **`ToolHoldTicks` = 0x18** (24 ticks, was 12); `ToolFallStartFrames` → **`ToolFallStartTicks` = 4**;
   `ToolFallFrames` → **`ToolFallTicks`**; `BoomerangStepFrames` → **`BoomerangStepTicks`**;
   `ThrowTicks` 4 → **8** and `ThrowPoseTick` 2 → **4** (`vic_throw`: `E286 = 8`, one `DEC` per call).
   The animation still runs every 2 calls: `(TickCount & 1) == 0` (`E203 & 1` gate of `knife_anim`/`boom_anim`).
2. **`Physics.FramesPerTick` deleted** (it was the divider trap). Only `GameConstants.FramesPerTick = 2`
   remained, used **only** for the tick time (`2/60 s`) — with the doc warning that it is not for counters.
3. **Door exit**: `StepExit` called **1×** per tick (was 2×) — 266 units ≈ 8.9 s and the
   end-of-stage screen 180 units = 6 s, like the longplay (door closes at 128.0 s, "NICE!" at 134.0 s).
4. **`e300_enemy_hit` moved to the end of the tick** (`CheckToolHits`, after `UpdateMechanisms`): it is the
   last step of `play_tick`, so the hit test sees the positions already moved.
5. Tests: `PhysicsTests` (tables in ticks + `Throw_Windup_IsEightCalls`), `StageSessionTests`
   (8-tick throw, 4 px/tick flight, door 1 unit/tick, 180-tick text, 32-tick tail).

**How to prove:** `dotnet test` (81/81) + `dotnet run` on a stage and throw the knife — it crosses
the room in ~2× the previous time (4 px/tick at 30 Hz = 120 px/s).

---

## Task 8 — Screens and backgrounds **without PNG**: runtime assembly ✅

**Symptom:** the fixed screens (title, map, pyramid, sound test, puzzle, logo) and the room backgrounds
depended on a PNG exported from the emulator — opaque art, without native palette/transparency and outside
the "everything derived from the ROM" model.

**Done:**
1. **Native format** `Content/Rom/Art/Screens/*.bin` (MSX2 Screen 5): 32 B of VDP palette
   (`(R<<4)|B, G`), 4 B of dimensions, 1 B of flags (`bit 0` = `HasAlpha`), 1 B reserved and the 4bpp
   bitmap packed in nibbles.
2. **`KingsValley.Core.Screens.ScreenAssembler`** (pure C#, tested) decodes it; **`ScreenBuilder`**
   builds the `Texture2D` with `SetData<Color>()`. The 9 PNGs were deleted; `world_vick.bin` came in along
   with the map screen.
3. All scenes started reading from there (Splash, MainMenu, SoundTest, Puzzle, WorldMap, Ending/GameOver
   without a static background); `RomContent.ScreenPath` returns `.bin`.
4. The **background PNG** fallback left `RoomRenderer`/`GameScene` — a room is only the ROM assembly.
5. Rule pinned in `AGENTS.md` and `docs/ROM_EXTRACTION.md` ("zero PNG for backgrounds/screens").

**How to prove:** `dotnet test` (`ScreenAssemblerTests` validates the dimensions/alpha of each `.bin`, 116/116)
and the visual gate: `KINGSVALLEY_START_SCENE=menu|editor` + `KINGSVALLEY_SCREENSHOT` → screen identical to
the ROM's (menu `start_select`, editor with the HUD).

---

## Task 9 — Opening: Vick **walking** to the pyramid ✅

**Symptom:** when starting the game, the "opening screen" showed Vick **standing** at the door and with the
wrong sky.

**Cause:** `pyramid.bin` came from a **longplay frame** (`art/screens_build.py`, t=85 s): Vick was
**baked into the bitmap** (impossible to animate) and the palette came from the video (brown sky; the VDP
uses black).

**Measurement (openMSX, `work/gen/intro_probe.tcl` + `intro_frames.tcl` + `intro_frames2.tcl`):** dump of
VRAM + palette + SAT (`RAM E800`, mirrored in VRAM `0xF600`) at 30 Hz:

| What | Measured |
| :--- | :--- |
| Background | bitmap **without** Vick (he is a sprite) + VDP palette |
| Walk | `X = 1 + tick` (1 px/tick = 30 px/s), `Y = 168 → 142 → 160` (goes up/down the base) |
| Poses | 8 ticks each, cycle 16/24/32/24 = the same as the game (`vick_s0_03/04/05/04`) |
| Portal | at `X = 216` switches to the door set (`vick_d_*`), poses 0/80 |
| Fade | palette 13/14 → black between 29.7 s and 32.2 s (≈2.5 s) |
| Message | map with **"DO YOUR BEST !!"** already stamped (`intro_msg.bin`), ~2 s |

**Done:**
1. `art/intro_build.py` assembles `pyramid.bin` (clean background, real palette) and `intro_msg.bin` from
   the dumps (`work/intro/`).
2. `WorldMapScene` (`intro` mode) has the timeline in ticks: walking with the game's poses,
   switching to the door pose, fade and then the map screen; any key skips the current phase.
3. `screens_build.py` no longer provides the pyramid (only title/menu, which have no sprite on top).

**How to prove:** `KINGSVALLEY_START_SCENE=intro` + `KINGSVALLEY_CAPTURE_DIR`/`_FROM`/`_TO` and look at the
frames (Vick appears walking from the left). `ScreenAssemblerTests` validates the new `.bin` files.

---

## Task 10 — **MSX1 ↔ MSX2** graphics variant at runtime (in progress)

**Request:** be able to switch, in game, between the graphics of the two versions (RC-760/MSX1 and SCC/MSX2).

**How it is wired:**
- `SaveGameData.GfxRom` (`"msx2"` default) + `KINGSVALLEY_GFX=msx1|msx2` (diagnostics).
- `Assets/GfxVariant` applies the variant, saves it to the profile and **recreates the current scene**.
- `RomContent.Use(...)` switches the art root: `Content/Rom` (MSX2) ↔ `Content/RomMsx1` (MSX1).
  Levels (`Levels/`) and audio (`Audio/`) are **shared** (same logic/sound in both versions).
- **F11 key** switches in game (the MSX2 atlas is used as a fallback when there is no MSX1 atlas;
  `F2` is already the Sound Test on the debug shortcuts).

**Extraction (openMSX dumps, `work/gen/msx1_check.tcl` / `room_msx1.tcl`):**
- `art/msx1_screens.py` — SCREEN 2 (name table + patterns + colors) → `.bin` 256x192 with the
  TMS9918 palette (16 colors). Already extracted: **title/menu** (`RomMsx1/Art/Screens/title.bin` and `start_select.bin`).
- `art/msx1_rooms_dump.py` — enters the stage via the password and dumps room 1 of each stage, converting
  it to `RomMsx1/Rooms/dumps/sNN_r1.bin`; `RoomBackgroundBuilder` loads the dump directly in MSX1 mode.
- `art/msx1_rooms_walk.py` — walks around the room (holds right ~10 s then left ~12 s) recording
  the VRAM every 0.5 s **named after the current room** (`E243`), so every room reachable on foot
  comes in by itself. It is what completes rooms 2..N of the 46 stages with more than one room.

**Pending (next steps):**
1. Rooms that are only reached by **ladder** (vertical connections: 43 pairs, 27 stages) — the horizontal
   walk does not reach them; it needs a route that goes up/down.
2. The remaining MSX1 screens (valley map, ending, puzzle, sound test, game over) — the pyramid one is done
   and the intro uses the same `intro_vick.bin` (MSX2 sprites for now). **The title already comes out of the ROM at
   runtime** (`Extraction/RomScreens.cs`, see `docs/ROM_EXTRACTION.md` §7d): the same path serves
   the other screens once there is a key/frame script for each one.
3. MSX1 sprites/HUD (1 color per sprite) — today MSX1 mode uses the MSX2 atlas and HUD.
4. The MSX1 screen is 256x192 (without the MSX2's 20 px): MSX1 screens end up with 20 black px at the bottom.

---

## Accepted limitations (documented, not bugs)

| Limitation | Why |
| :--- | :--- |
| **Static items from the ROM are 2×2 tiles** | The original draws item/door directly on VDP page 1; the port uses `FloorItem`/sprites from the atlas. |
| **The Gum font material only works at zoom 1** | Gum's oversampling regenerates the font and discards the `Typeface` at zoom ≠ 1 (see `AGENTS.md`). |
| **The extra life has no visual card** | The original plays sound `0x2A` and increments `REST`; the port does the same (`GameEventKind.ExtraLife`). There is no card art in the atlas. |
| **The title's `hud_meter` is not animated** | `title.bin` already comes with the GAME/EDIT box drawn; the 0x39-step slide is not reproduced. |

---

## How to use this plan

1. **Tasks 1–9 closed.** The section order is still the suggested one.
2. Before coding: `dotnet build KingsValley.slnx` and `dotnet test` (today **116/116**).
3. After any change: **build + tests** and, if you touched art/data,
   `python <script>.py` in `tools/kv2-extract` (each script documents its use at the top of the file;
   run it with the working directory = `tools/kv2-extract`).
4. To *see* the result without playing, use the diagnostic variables (full list in
   `AGENTS.md`): `KINGSVALLEY_START_SCENE`, `KINGSVALLEY_LEVEL`, `KINGSVALLEY_EXIT[_HOLD]`,
   `KINGSVALLEY_SCREENSHOT[_FRAME]`, `KINGSVALLEY_WINDOW`, `KINGSVALLEY_SKIP_LEVELSTART`.
5. When closing a task: mark it here as **done** (with the commit hash) and, if you changed
   any ROM decoding, update `ROM_PORT_HANDOFF.md`.
6. Commit in English, in the `fix(game): …` / `feat(rom): …` pattern (the recent history is the
   reference).
