# King's Valley II — Game Design (original and clone)

> **Project goal:** a **faithful** clone of *King's Valley II: The Seal of El Giza* (Konami, 1988),
> **MSX2** version, in MonoGame + Nez + Gum.
>
> **Source of truth:** the exact supported cartridge (levels, logic, sounds and graphics); longplay observations remain historical reference evidence. There is
> no public source code. Everything here was **extracted from the ROM**, **measured in the emulator** or
> **observed in the longplay** — and each item says where it came from. The extraction process is in
> [ROM_EXTRACTION.md](ROM_EXTRACTION.md); all links are in [REFERENCES.md](REFERENCES.md).
>
> Legend for the "Source" column: **ROM** = read from the ROM's data/code · **RAM** = measured by reading
> the RAM in openMSX · **Video** = observed in the MSX2 longplay · **Doc** = community sources ·
> **Estimated** = not measured yet (marked in code as "estimated").

---

## 1. The original game

| Item | Value | Source |
| :--- | :--- | :--- |
| Title | *King's Valley II: The Seal of El Giza* (王家の谷 エルギーザの封印) | Doc |
| Publisher | Konami | Doc |
| Year | 1988 (the first *King's Valley* is from 1985) | Doc |
| Versions | **MSX1 (RC-760)** and **MSX2 (RC-761, Japan only)**, 1 Mbit MegaROM with **SCC** | Doc/ROM |
| Contest edition | gold cartridge with 20 user stages (a contest by 4 Japanese magazines) | Doc |
| MSX2 screen | **256x212** (Screen 5): field **256x192** + 20-line HUD band | Video |
| Grid | **8x8 px** per cell → room of **32x24 cells** | ROM |
| Content | **60 stages** in **6 pyramids** of 10, **154 rooms** in total | ROM (matches msx.org) |
| Players | 1 | Doc |

### Story (manual / msxnet)

In the distant future, **Vick XIII**, an interplanetary archaeologist, discovers on the planet **Remool**
that the Egyptian royal family came from there and that Earth's pyramids were devices to carry the souls
of the pharaohs back. With the plundering by explorers (including Vick's ancestors), the tombs got out of
control and are about to self-destruct — with enough energy to destroy Earth. Vick must enter **El Giza**,
the central pyramid, going through the sealed chambers and **collecting and destroying all the soul
stones** along the way.

### MSX1 × MSX2

The **levels are identical** (the editor files work in both versions). The MSX2 has a larger palette and
resolution (the HUD is on screen; on MSX1 only with F2), remixed music and some recolored items/backgrounds.
The MSX1 has a shimmer animation on the soul stones. The current extraction pipeline derives each flavor's levels, audio and graphics from its own verified cartridge. The gameplay port retains historical MSX1/MSX2 research and still requires per-flavor behavior comparisons — see [ROM_EXTRACTION.md](ROM_EXTRACTION.md).

---

## 2. Structure

### 2.1 Pyramids

| Pyramid | Stages | Rooms | Soul stones | Items | Enemies/blocks | Sarcophagi | Golden ladders | Visual (MSX2) |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| 1 | 1–10 | 18 | 48 | 57 | 21 | 14 | 2 | blue-gray wall, **golden** block, gray/purple murals with faces |
| 2 | 11–20 | 23 | 70 | 67 | 19 | 20 | 17 | **blue** block, red/brown diagonal murals |
| 3 | 21–30 | 25 | 94 | 72 | 39 | 16 | 6 | **spherical yellow** block, green vine murals |
| 4 | 31–40 | 32 | 112 | 97 | 20 | 18 | 17 | **yellow-green** block, green/blue murals |
| 5 | 41–50 | 31 | 133 | 89 | 23 | 13 | 21 | **yellow** block, purple/red murals |
| 6 | 51–60 | 25 | 152 | 76 | 58 | 20 | 49 | **light blue** block, brown/green murals |

*(ROM — computed from the 60 extracted stages.)* Soul stones per stage rise from **4 → 16**. The stage
with the most rooms is **44** (6 rooms, 3x2 grid).

Between pyramids the **valley map** appears ("TRY TO TAKE A NEW STEP FORWARD") along with the door/elevator
(Video). Before stage 1: a cutscene of Vick walking to the pyramid → map "DO YOUR BEST !!" →
"STAGE nn / SOUL STONE nn" screen (RAM/MSX1).

### 2.2 Stage and rooms

- A stage has **1 to 6 rooms** placed on an **8x8 grid** (8-byte bitmask per stage in the ROM).
- Leaving through an edge takes you to the **neighboring** room; if there is none, it **wraps around**
  (e.g. in stage 1, the ladder in column 25 exits through the ceiling and reappears on the floor of the
  same room). *(ROM + RAM)*
- There is no scrolling: the screen switches entirely (Doc/Video).
- Each room has its own **background mural** (Video).

### 2.3 Terrain (2 bits per cell)

| Value | Class | Behavior |
| :---: | :--- | :--- |
| 0 | empty | shows the mural |
| 1 | ladder | stored on top of the terrain (crosses the floor) |
| 2 | **diggable block** | solid; disappears with the tools |
| 3 | **wall** | solid and indestructible |

*(ROM — routine `43D3` + tables `0x6000`/`0x6078`/`0x60F0`.)*

---

## 3. Mechanics

### 3.1 Objective

Collect **all the soul stones** in the stage → the **exit door** opens → enter the door (↑) → next
stage. *(Doc + RAM)*

### 3.2 Explorer movement

| Action | Value | Source |
| :--- | :--- | :--- |
| Logic tick | **1 call** of ROM logic (~30 Hz; the ISR locks on `E206`) | RAM + measurement |
| Walk | **2 px/tick** (60 px/s) | RAM (`E284`) |
| Ladder | **2 px/tick**; on entering, X snaps to the ladder column; at the top, you stand on it | RAM |
| Jump (space, empty hands) | dy per tick: `-4 -3 -2 -2 -2 -1 0 0 0 +1 +2 +2 +2 +3 +4` → peak of **14 px**, 30 frames | RAM (`E282`) |
| Walking jump | advance of **1.25 px/tick** (0x140 in 8.8) — covers ~19 px; the sprite (21 px) gets within 19 px of the floor | RAM |
| Jump while holding an item | **does not jump** — the button uses the item (with the knife: holds 8 ticks and the knife comes out 20 px ahead) | RAM/Doc |
| Fall | **4 px/tick**, constant, no horizontal displacement; loses support via the probes at x+3 and x+12 | RAM |
| Pushing a wall | animates in place (frames 3-4-5) | RAM (`E285`) |

Player variables in the original: `E280` state (0 standing, 1 jumping, 2 ladder, 5 dying…),
`E282` y, `E284` x, `E285` animation frame, `E288` direction (08 right / 04 left). *(RAM)*

### 3.3 Items (6 types)

| Code | Item | Type | Effect | Source |
| :---: | :--- | :--- | :--- | :--- |
| 1 | **Knife** | reusable weapon | horizontal throw; kills; falls to the floor and can be picked up again | ROM/RAM/Doc |
| 2 | **Boomerang** | reusable weapon | goes out and comes back to the hand | ROM/Doc |
| 3 | **Shovel** | tool (single use) | digs the floor **1 cell** down | ROM/Doc |
| 4 | **Pickaxe** | tool (single use) | digs the floor **2 cells** down | ROM/Doc |
| 5 | **Hammer** | tool (single use) | breaks the wall ahead **1 cell** | ROM/Doc |
| 6 | **Drill** | tool (single use) | drills through the wall ahead **2 cells** | ROM/Doc |

- You carry only **one** item; with an item in hand you do not jump; you cannot drop it without using it. *(Doc)*
- Totals across the 60 stages: drill 155 · knife 123 · pickaxe 55 · boomerang 43 · hammer 43 · shovel 39. *(ROM)*
- ROM record: 3 bytes `(room<<4 | type), y, x` — pointer table `0xAFB1` (bank 13).

### 3.4 Enemies (they only come out of sarcophagi)

Each **sarcophagus** (`E2C0`, 5-byte record `[0x80|type][0x10|level][room][y][x]`) spawns enemies of
**one type**. Measured frame by frame in the `E500` structure (`[1]` state, `[3]` y, `[4]` x fraction,
`[5]` x) and in the sprite colors (MSX1):

| Type | Color | Behavior (measured) |
| :---: | :--- | :--- |
| 1 | **white** | walks at **0.75 px/frame**; **does not jump and does not climb ladders**; falls off cliffs (does not turn at the edge); at a wall it **stops for 38 frames and turns**; on landing it **stops for 34 frames** and turns toward the player |
| 2 | **yellow** | like the white one, but **climbs up and down ladders** after the player (2.5 px/frame on the ladder) |
| 3 | yellow (statue) | **only moves in hops**: 17 px arc, ~14 px of advance, 16-frame pause between hops |
| 4 | **red** | faster (~1 px/frame); **bounces** on landing (−3 −2 +2 +3 −2 +2 −1 +1) |

- Enemy fall: ~2.5 px/frame. They come out of the sarcophagus ~80 ticks after the start (rising for ~32
  ticks without killing); after an enemy disappears, the next one comes out ~116 ticks later.
- Hit by the knife: **freezes for 24 ticks** and disappears. Enemies that fall through the floor of a room
  **reappear through the ceiling** (vertical wrap-around, like the player).
- Collision (series engine): if one of the two is on a ladder, the other must be too.
- The yellow one's speed equaled the white one's in stage 3 (level 2); the effect of the sarcophagus
  **level** (1–3) has not been measured yet.
- *I did not disassemble the AI code*: the numbers come from observing the RAM in the emulator.

### Mechanisms (`E600` list)

`1` **rotating door** (vertical bar) · `5` **pushable block** · `2`, `3`, `4` to be identified
(they are not enemies — they used to be treated as enemies by mistake).

### 3.5 Other elements

- **Exit door** (4x4 cells, `E2F0`): closed until the last soul stone. *(RAM)*
- **Temporary golden ladders** (table `0x60F0` → list `E7C0`): two armed uses, not a time limit. Climbing arms the record; a subsequent climb-exit/landing strobe consumes a use. On the second use, the top pair becomes soft terrain and the remaining ladder cells are erased. See [Golden ladders](GOLDEN_LADDERS.md). *(Native Contest `8FD5`; original disassembly `BE15`.)*
- **Doors that turn/return**, **bridges that fall after 2 crossings** (HG101) — *to be mapped in the ROM*.
- **Extra stages** (secret): *puzzle* (entrances on stages 20, 35, 43, 52, 55 — gives ~35 lives) and
  *music* (playable keyboard; stages 7, 32, 40, 50, 53). You jump several times in one spot until it
  becomes a portal and press ↓. *(msxnet)*

### 3.6 Lives, score and passwords

| Rule | Value | Source |
| :--- | :--- | :--- |
| Extra lives at the start | **REST 00** | Video |
| Gain | **+1 REST per completed stage** (REST follows the stage in the longplay) | Video |
| Soul stone | **+500** | Video |
| Enemy | 200 | Estimated |
| MSX2 HUD | `HISCORE 000000 · SCORE 000000 · STAGE 00 · REST 00` | Video |
| Password | 8 letters `A`–`P` = bytes (stage, REST, `R`, sum), rotated by `R & 7` | ROM `6994`/`6A98` + longplay (`EAEAODGD` → stage 2, REST 2) |
| End-of-stage screen password | worth **REST + 1** (`INC (0xE240)` in `732C`, the "SPECIAL BONUS") | ROM + longplay (shows REST 01, the password gives REST 2) |
| `FESTIVAL` | invincible (starts at stage 1) | Doc/ROM (text at `0x2B57` = `6B57` of page 1) |
| `TRYAGAIN` | continue after game over | Doc/ROM (`0x2B5F`) |

### 3.7 Original keys

| Key | Action |
| :--- | :--- |
| Arrows | walk / ladder |
| Space | jump (empty hands) or use the item |
| F1 | pause (F2 while paused = stage map) |
| F4 | music on/off |
| F5 | suicide (restarts the stage losing a life) |

### 3.8 Menus

Konami logo → title (**GAME / EDIT**) → **START SELECT** (**NORMAL / PASSWORD / STAGE LOAD**) →
cutscene → map "DO YOUR BEST !!" → "STAGE nn / SOUL STONE nn" → game. The **editor** saves stages of
2343 bytes (`.elg`) to disk/tape. *(RAM/MSX1 + Doc)*

### 3.9 Sound (PSG + SCC)

| id | Use | Loop |
| :---: | :--- | :--- |
| 3 | title | plays once |
| 17 | menu / password / cutscene | 8.544 s |
| 5, 6, 7, 8, 9 | **stage music** — changes every 2 stages: `((stage-1)%10)/2` | 32.0 / 30.7 / 44.9 / 28.8 / 28.8 s |
| 10, 13, 18 | other music (map/extras — to be identified) | 38.5 / 29.9 / 21.4 s |
| 14 | ending/credits (no loop) | — |
| 19 · 25 · 28+54 · 30 · 37 · 40+41 · 50 · 56/60 | jump · pick up item · knife flying + hitting · player appears · sarcophagus opens · death + jingle · password key · footsteps | — |

*(ROM: routine `432E` "play sound A" → driver at `6003`; ids measured in the emulator.)*

---

## 4. Original → clone mapping

| Mechanic | Where it lives | State |
| :--- | :--- | :--- |
| 60 stages / 154 real rooms | `Content/Rom/Levels/stageNN.json` (generated) + `Core/Stages/StageLoader` | ✅ |
| Room grid with wrap-around | `Core/Gameplay/RoomGridNavigator` | ✅ |
| 8x8 terrain + overlaid ladders | `Core/Gameplay/RoomMap` | ✅ |
| Walk / measured jump / fall / ladder | `StageSession.Player.cs` + `Physics` | ✅ (fall estimated) |
| Shovel, pickaxe, hammer, drill | `StageSession.PerformDig` | ✅ |
| Knife and boomerang | `StageSession.Enemies.cs` (projectiles) | ✅ |
| Soul stones → door → exit | `StageSession.CollectAt` / `AtOpenDoor` | ✅ |
| 4 enemy types per sarcophagus (white, yellow, hopper, red) | `StageSession.Enemies.cs` | ✅ measured in the emulator |
| Pushable block | idem | ✅ · rotating doors ⬜ |
| Lives (REST), score, high score, passwords | `GameRun`, `StagePassword`, save | ✅ |
| MSX2 graphics (tiles, murals, objects, characters, HUD, screens) | `Content/Rom/Art` (atlas `kv2`) | ✅ (character sprites from the video) |
| Original music and effects | `Content/Rom/Audio` + `GameAudio` | ✅ (some effects unnamed) |
| Menus (title, START SELECT, password) | `MainMenuScene` | ✅ (EDIT and STAGE LOAD to do) |
| Temporary golden ladders | `StageSession.GoldenLadders.cs`, mutable room assembly and save-state counters | Two-use native behavior and save/restore tested |
| Rotating doors / falling bridges | — | ⬜ (map in the ROM) |
| Extra stages (puzzle / music) | — | ⬜ |
| Map between pyramids, cutscene, ending | — | ⬜ |
| Stage editor (`.elg`) | — | ⬜ |
| Pause with map (F1+F2), F4 music | F1 pause | 🟡 |

## 5. Next steps (suggested order)

1. **Measure what is missing** with the **openMSX** already installed (`tools/kv2-extract/work/omsx`, see
   [ROM_EXTRACTION.md](ROM_EXTRACTION.md) §2 and [tools/kv2-extract](../tools/kv2-extract/README.md)):
   the player's fall, the score per enemy and the room exit. Already measured: the AI of the **4 enemy
   types** — the type 3 track on the ladder/fall and type 2 on the ladder closed in Oct/2026
   (`ROM_PORT_HANDOFF.md` §6) — and the **knife/boomerang**: the knife's step is the literal `4` of `A72A`
   and the boomerang follows the profile of tables `A961`/`A96B` (23-tick trip).
2. **Map remaining bridge and mechanism behavior in the ROM.** Golden ladder counters, arming and
   replacement terrain are now covered by [native checks](GOLDEN_LADDERS.md).
3. **Clean MSX2 sprites**: the character poses come from the video (some noise). Run the MSX2 ROM
   (RC-761) in the emulator and read VRAM directly — the available dump is an ASCII8 conversion that does
   not boot on C-BIOS (see ROM_EXTRACTION.md).
4. Cutscene, map between pyramids, ending ("DANGER !!!" + credits).
5. Extra stages and the `.elg` editor (the 2343-byte format is already identified).
