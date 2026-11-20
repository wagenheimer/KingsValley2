# Architecture

## Projects

```
KingsValley.slnx
├─ src/KingsValley.Core            pure C#: rules, no MonoGame/Nez/Gum
│   ├─ Common/GameConstants.cs     design size 256x212, tick time, window defaults
│   ├─ Stages/                     StageDefinition, StageLoader (JSON), StageModel, password
│   ├─ Gameplay/                   StageSession (the game state machine — one partial per system)
│   │   ├─ StageSession.cs         tick order, score, exit machine, mechanisms, traps
│   │   ├─ StageSession.Player.cs  walk/jump/climb/fall/throw/dig/door, collision probes
│   │   ├─ StageSession.Enemies.cs enemies loop, tools (projectiles), contact
│   │   ├─ StageSession.Mummy.cs   mummy/gold mummy AI + coffin spawning
│   │   ├─ StageSession.Guard.cs   Pyoncy AI
│   │   ├─ StageSession.Rock.cs    Rock Roll AI
│   │   ├─ StageSession.Dig.cs     digging + crumble
│   │   ├─ Physics.cs              every measured constant, per tick
│   │   ├─ RoomMap.cs              mutable 2-bit-ish terrain of one room
│   │   └─ EnemyFrames.cs          sprite frame rules (IX+0x11)
│   └─ SaveSystem/                 profile, hi-score, passwords
├─ src/KingsValley.Engine          MonoGame + Nez + Gum: scenes, rendering, audio, UI
│   ├─ KingsValleyGame.cs          game loop, draw order, diagnostics, frame capture
│   ├─ Scenes/                     SceneBase, SplashScene, MainMenuScene, LoadingScene, GameScene
│   ├─ Scenes/RoomRenderer.cs      draws map/items/enemies/tools/player from the session
│   ├─ Scenes/RoomBackgroundBuilder.cs  assembled room texture (mural + blocks + mechanisms)
│   ├─ Scenes/PaletteAnimator.cs   colour-6 animation from animations.json
│   ├─ UI/Gum/                     every screen, dialog and the HUD (Gum Forms)
│   ├─ Assets/                     RomContent paths, GameAudio (id → file), GameAssets (atlases)
│   └─ Input/                      UnifiedPointer, InputScript (scripted input for docs/CI)
├─ src/KingsValley.Desktop         entry point + Content/Rom (generated from the ROM)
├─ tests/KingsValley.Core.Tests    the automated suite (86 tests)
├─ lib/Nez                         vendored Nez (MonoGame 3.8)
└─ tools/kv2-extract               the extraction toolchain (Python + openMSX scripts)
                                    + tools/docs = the documentation capture tooling
```

The **Core never references** MonoGame/Nez/Gum: rules are testable head-less and the engine only
reads state and plays events. Anything engine-agnostic that could serve another project goes into the
`Wagenheimer.MonoGameHelper` package instead (design viewport, post-UI overlay, screenshot helper,
audio manager, input manager, atlas scale).

---

## The tick and the draw

```
Nez update ────────────────────────────────────────────────────────────────
GameScene.Update: accumulator += dt
   every 1/30 s -> StageSession.Tick(input)  -> List<GameEvent>
                    GameAudio.Play(event), extra life -> GameRun.GainLife()

Draw ──────────────────────────────────────────────────────────────────────
base.Draw()           1. scene (Nez): background, map, items, enemies, tools, Vick
ScreenFade            2. transition curtain (below the UI)
GumUI.Draw()          3. every menu/dialog/HUD (always on top of the scene)
PostUiOverlay.Draw()  4. cursor, floating text, episodic effects
CaptureSequence()     5. optional frame_*.png dump for the documentation
```

The world uses **design units** (256 px wide, 212 tall) mapped to the native back buffer by a camera
zoom (`DesignViewport.Scale`), never by a reduced render target — the pixel art stays sharp at any
window size (point sampling).

---

## Session state

`StageSession` is a deterministic state machine driven by `Tick(PlayerInput)`:

* one `RoomMap` per room, built from the JSON terrain + ladders + mechanism walls;
* `Player`, `Enemies`, `Coffins`, `Projectiles`, `Items`, `_soulStones`, `Exit` phase + `ClearStep`,
  mechanism/trap state dictionaries, `TickCount`;
* all sound/score/visual facts leave as `GameEvent`s (`Sound`, `EnemyKilled`, `StageCleared`,
  `ExtraLife`, `RoomChanged`, …), so the Core has no idea about audio or sprites.

`Events` is cleared at the start of every tick; the engine consumes it right after `Tick`.

The tick order mirrors `play_tick`: player → tools → delayed spawns (coffins) → enemies → contact →
mechanisms (doors/traps) → tool-vs-enemy hit → death end.

---

## Determinism and the tests

`tests/KingsValley.Core.Tests` (86 tests) covers:

* every measured constant (`PhysicsTests`), including the tick model (`Throw_Windup_IsEightCalls`);
* the stage loader on synthetic stages and on the **real 60 stages** when the ROM data is present;
* the exit machine, the mechanisms (door, trap, stone), the E600 actor spawns, the tool room wrapping;
* enemy/player interactions, digging, scoring and the extra-life thresholds.

The tests that need the ROM data **skip themselves** when `Content/Rom` is not extracted, so the suite
is green on a clean clone.

A 1:1 trace tool (`tools/kv2-extract/simtrace`) compares the port against the emulator tick by tick:
the mummy/gold mummy walks were validated with **0 different frames** over hundreds of ticks.

---

## Diagnostics (the tools behind the docs)

All are environment variables read by the engine; the full list is in `AGENTS.md`.

| Variable | Effect |
| :--- | :--- |
| `KINGSVALLEY_START_SCENE` | `splash` / `menu` / `loading` / `level` |
| `KINGSVALLEY_LEVEL` / `_PASSWORD` | stage to start |
| `KINGSVALLEY_SKIP_LEVELSTART=1` | skip the "Order N!" banner |
| `KINGSVALLEY_PLAYER=room,x,y` | place the Vick (documentation captures) |
| `KINGSVALLEY_INPUT=<file>` | tick-by-tick input script (documentation captures) |
| `KINGSVALLEY_EXIT=N` | jump into the exit sequence, N ticks in (`_EXIT_HOLD=1` freezes) |
| `KINGSVALLEY_SCREENSHOT` + `_FRAME` | one-shot PNG of the final frame and quit |
| `KINGSVALLEY_CAPTURE_DIR` + `_FROM`/`_TO` | PNG sequence and quit (the GIFs) |
| `KINGSVALLEY_WINDOW` | window size (`256x212`, `1024x848`, `1920x1080`) |
| `KINGSVALLEY_ART_SCALE` | atlas resolution (1/2/4) |
| `KINGSVALLEY_FAST_BOOT=1` | near-instant splash |

The capture pipeline lives in `tools/docs`: `capture.ps1` runs each demo (input script + placement +
frame window) and `make_gif.py` builds the GIF at the game's real cadence.

---

## Rendering the room

1. `RoomBackgroundBuilder` assembles a 256×192 texture: the pyramid's mural blocks, the room's mural
   codes, decorations, and the mechanism tiles (`mechanism_t1_p{phase}.bin` for the rotating door,
   `mechanism_t4.bin` for the trap). It rebuilds when the room, the palette animation, the terrain
   version (digging, trap firing) or the crumble stage changes.
2. `RoomRenderer` draws on top: items, soul stones, enemies, tools, the player, then the HUD by Gum.
3. Palette animation changes colour 6 every 4 ticks (`PaletteAnimator`), exactly like `5C14`.

---

## Audio

`GameAudio` maps the `Sound` id carried by each event to a file from `Audio/audio.json`, plays music
by name, and enforces the PSG rule of **one voice per effect** (retrigger = restart). The door jingle
stops the stage music; the landing sound cuts the fall whistle.
