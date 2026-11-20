# Specification for AIs and Developers — King's Valley 2

> Also read [../AGENTS.md](../AGENTS.md) (mandatory rules), [GAME_DESIGN.md](GAME_DESIGN.md)
> (the original game) and [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 0. Current state (summary)

A **working** skeleton inherited from `HopesFarm2.MonoGame` (same stack: MonoGame + Nez + Gum) and
adapted:

- ✅ Solution `KingsValley.slnx` (Core/Engine/Desktop + `lib/Nez` + tests) **builds**.
- ✅ **Core** (pure C#): 16x16 grid, tiles, room session (walk/fall/ladder, dig, build,
  treasure/door, enemies, time, score), 3 rooms, save. **10 green tests**.
- ✅ **Engine**: `KingsValleyGame`, `SceneBase`, `GameNavigation` (+ `LoadStep`), scenes
  Splash/MainMenu/Loading/**Game**, `RenderLayers`, `GameAssets`, `DesignViewportCamera`,
  `ScreenOverlay`, Gum helpers.
- ✅ **Desktop**: entry point + `Content` (Gum project with `MainMenu` + `forms` theme + fonts).
- ⬜ Own art/audio, HUD in Gum, map, room editor, mapped gamepad.

```bash
dotnet build KingsValley.slnx     # 0 erros
dotnet test                       # 10 testes
dotnet run --project src/KingsValley.Desktop
```

---

## 1. Mandatory rules (non-negotiable)

1. **Named RenderLayer** (`KingsValley.Engine.RenderLayers`) - **never** a magic number. In Nez,
   **lower = more in front**.
2. **All UI is Gum** (never `Nez.UI` in new code). Layout in `Content/Gum`; wiring in
   `Engine/UI/Gum`. After editing XML: `dotnet gumcli check` = `No errors found`.
3. **Native resolution** + design units (`SceneResolutionPolicy.None`, camera zoom).
4. **All sprite art comes from atlases**, bound **by NAME** (never a loose PNG at runtime).
5. **Business rule in Core** (pure C#, with tests). The Engine only animates/renders.
6. **Every scene change goes through `GameNavigation`.**
7. **Never edit XML/text with PowerShell's `-replace`** (it corrupts names — `Berlin`→`Ferlin`).
   Use literal `.Replace()`.
8. **Before finishing:** `dotnet build KingsValley.slnx` + `dotnet test` (+ `gumcli check` if you touched
   the UI). No magic numbers; comments in **Portuguese**.

---

## 2. Roadmap by phases

### Phase 0 — Foundation ✅
Solution, layers, design resolution, placeholder scenes, navigation, save, tests.

### Phase 1 — Game rules (Core) ✅ (base)
Grid, tiles, session (walk/fall/ladder/dig/build), treasure/door, enemies, time, score.

**To be completed:**
- **Jump** (confirm whether the original has it) — `LevelSession.Jump()` + gravity.
- Richer **enemies** (types; turn at **edges** and not only at walls; chasing).
- **Keys/locked doors** (if the original has them).
- **Tests** for every new rule (TDD: test first).

### Phase 2 — Content and rooms
- **24 rooms** with increasing difficulty (ASCII → later JSON).
- Room **editor/importer** (`Content/Levels/*.json` + `LevelRepository`), as in JoanOrArc.
- Room selection **map** + progression.

### Phase 3 — Presentation (Engine)
- Own **atlases** (`tiles`, `sprites`, `ui`) via `tools/pack_atlases.ps1`.
- Movement **tweens** (walk/fall) instead of drawing directly per tile.
- **VFX**: dust when digging, flash on death, sparkle on the treasure.
- **Audio** (SFX/music) through the `Wagenheimer.MsxAudio` package (PSG/SCC chips, streaming).

### Phase 4 — UI (Gum)
- **HUD** in Gum: time, treasure, blocks, room (replacing the current overlay).
- **Menu** and **dialogs** (pause, room result, map).
- Dynamic **fonts** (KernSmith) — declare them in `Components/Styles.gucx`.

### Phase 5 — Polish
- **Gamepad** (the `InputHelperManager` is already wired; mapping is missing).
- **Localization** (i18n is already initialized; the content in `Content/Localization` is missing).
- **Fullscreen** (`SetFullScreen` already exists), custom **cursor**.
- **Fidelity gate**: screenshots against references from the original.

---

## 3. Where things live

| Layer | Project | Examples |
| :--- | :--- | :--- |
| Rules | `src/KingsValley.Core` | `World/LevelSession.cs`, `World/LevelLibrary.cs` |
| Engine | `src/KingsValley.Engine` | `Scenes/GameScene.cs`, `Navigation/GameNavigation.cs` |
| Executable + Content | `src/KingsValley.Desktop` | `Program.cs`, `Content/Gum`, `Content/Atlases` |
| Tests | `tests/KingsValley.Core.Tests` | `LevelSessionTests.cs` |

---

## 4. Definition of done (DoD) for a task

- [ ] `dotnet build KingsValley.slnx` with no errors.
- [ ] `dotnet test` green (and a **new test** for the new rule).
- [ ] If you touched UI: `gumcli check` = 0 (+ screenshot reviewed).
- [ ] No magic numbers for `RenderLayer`/Gum units.
- [ ] Comments in Portuguese; rule in Core, not in the Engine.

---

## 5. Diagnostics

| Variable | Effect |
| :--- | :--- |
| `KINGSVALLEY_START_SCENE` | `splash` \| `menu` \| `loading` \| `level` |
| `KINGSVALLEY_LEVEL` | room number (1..24) |
| `KINGSVALLEY_WINDOW` | e.g. `1024x768` |
| `KINGSVALLEY_SCREENSHOT` + `_FRAME` | capture and exit (CI) |
| `KINGSVALLEY_EXIT` | `N` = enters through the door and fast-forwards `N` ticks of the exit sequence (0..250; see below) |
| `KINGSVALLEY_EXIT_HOLD` | `1` = freezes the logic after `KINGSVALLEY_EXIT` (screenshot gate) |

`KINGSVALLEY_EXIT` — reference values of the sequence (266 **frame units** = 133 ticks until the
curtain closes, plus 180 units of the end-of-stage screen; the auto-enter happens on the 1st tick and
costs no unit):

| `N` | State |
| :--- | :--- |
| 1..16 | `DoorOpening`, frame 0 (door closed, **without Vick** — 907F cleared the SAT) |
| 17..31 | `DoorOpening`, frame 1 (half open) |
| 32..46 | `DoorOpening`, frame 2 (open) |
| 47..110 | `Entering` (poses 0→7; rises 2 px every 8 ticks, 14 px at the end) |
| 111..133 | `Curtain` (bands 0→12 of 8 px on each side) |
| 134..223 | `ClearScreen`/`Texts` (72E1: `NICE ! / LETS GO NEXT STAGE / SPECIAL BONUS / REST nn`) |
| 224..250 | `ClearScreen`/`KeyWait` (7331: `PASS WORD` + the password; waits for a key — with no input it stays there) |
| `KINGSVALLEY_ART_SCALE` | `1` \| `2` \| `4` |
| `KINGSVALLEY_FAST_BOOT` | `1` = short splash |
