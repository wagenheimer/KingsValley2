# AGENTS.md — Rules for AI Agents

Project: **King's Valley 2** — a clone of the classic **Konami/MSX2 (1988)** game in **MonoGame + Nez + Gum**.

> ℹ️ **Origin of this document:** this `AGENTS.md` was **inherited and adapted** from the base project
> `HopesFarm2.MonoGame` (same stack: MonoGame + Nez + Gum). It concentrates **accumulated knowledge
> about pitfalls** (Gum, atlas, fonts, resolution) that applies to any project on this stack.
>
> ⚠️ Where it mentions **Unity** (port, font materials, `Assets/...`), read that as **history**:
> King's Valley has **no** Unity project — the Gum fonts came from the base, and the game's design is
> MSX2 **256x212** (a 256x192 field = 32x24 cells of 8x8 + 20 HUD rows), not Unity's 1430x768.
>
> For the game itself, the reference is [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) (the original
> King's Valley 2) — **there is no public source code**; the design comes from the game.

## MANDATORY RULE: project language is English

Write all new project-authored text in English, regardless of the language used in the conversation.
This includes user-facing UI, generated reports, labels, logs, exception messages, code comments,
tests, plans and documentation. Keep generated reports in English even on non-English operating
systems. Translate project-authored text introduced or changed during the current task accordingly.
Cartridge bytes and authentic ROM artwork are source data and must remain unchanged.

## MANDATORY RULE: fidelity to the original and ROM content

- **Continuing the ROM port?** Start with [docs/ROM_PORT_HANDOFF.md](docs/ROM_PORT_HANDOFF.md) (method, maps, status and next steps).
- The source of truth is the **original King's Valley II**: levels and logic from the **MSX1 ROM (RC-760)**,
  graphics from each **verified cartridge**; longplay captures are historical reference evidence. Read [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) and
  [docs/ROM_EXTRACTION.md](docs/ROM_EXTRACTION.md) before changing any game rule. **Do not invent**
  numbers: measure them in openMSX (scripts in `tools/kv2-extract`) or mark them as "estimated".
- **There is no Pascal/ZenGL original and no Unity project** for this game — such references are a
  hallucination from another tool.
- Everything in `src/KingsValley.Desktop/Content/Rom/` (level JSON, `kv2` atlas, murals, screens, music)
  is **derived from the Konami ROM**: versioned only while the repo is **private**; the open-source version ships without it.
- The art is **MSX2 pixel art** at 256x212: **`PointClamp`** filter (crisp), integer camera zoom.
- The logic runs at **~30 Hz**, like the original — but **1 ROM logic call = 1 port tick**
  (the ISR locks at `E206` and skips ~half of the vblanks). Every counter read from the ROM counts **1 per
  call**: use the value directly in ticks, **never divide by 2**. `GameConstants.FramesPerTick` (=2)
  exists only for the tick's **time** (`2/60 s`), not for counters.

## MANDATORY RULE: no PNG for backgrounds and screens — everything assembled at runtime

> **NEVER create or use PNG files for backgrounds, rooms or screens (`Screens`).**
> **EVERYTHING must be assembled at runtime from the native ROM / VDP data.**

1. **Gameplay Rooms (`Content/Rom/Rooms/` e `Content/RomMsx1/Rooms/dumps/`)**:
   - Assembled via `KingsValley.Core.Rooms.RoomAssembler` and `KingsValley.Engine.Scenes.RoomBackgroundBuilder`.
   - MSX2: raw 4bpp blocks (`blocks_p*.bin`), mural tables (`murals.json`), decorations (`decorations.json`) and palettes (`palettes.json`) with the color-6 animation cycle.
   - MSX1: binary SCREEN 2 dumps (`sNN_rMM.bin`, 24,614 bytes = 38-byte header + 256×192 4bpp packed bitmap), decoded byte-for-byte from TMS9918 VRAM.
   - There are no PNG files for the backgrounds of the 60 levels.
2. **Game Screens (`Content/Rom/Art/Screens/` e `Content/RomMsx1/Art/Screens/`)**:
   - Assembled via `KingsValley.Core.Screens.ScreenAssembler` and `KingsValley.Engine.Graphics.ScreenBuilder`.
   - Stored in native binary files (`.bin`) in the MSX2 Screen 5 / MSX1 Screen 2 format:
     - **32 bytes**: MSX VDP palette (16 colors × 2 bytes in hardware format `(R << 4) | B, G`).
     - **4 bytes**: Dimensions (`ushort Width`, `ushort Height` in little-endian).
     - **1 byte**: Flags (`bit 0`: `HasAlpha` — color 0 transparent for cursors/clips).
     - **1 byte**: Reserved (0).
     - **Remaining**: 4bpp bitmap packed in nibbles (2 pixels per byte).
   - `ScreenAssembler` decodes this data at runtime producing `ScreenData`, and `ScreenBuilder` builds the `Texture2D` via `SetData<Color>()`.
   - It includes: World Map (`world_map.bin`), tour Vick (`world_vick.bin`), Title (`title.bin`, `title_en.bin`), Start Menu (`start_select.bin`), Pyramid (`pyramid.bin`), Opening map (`intro_msg.bin`), Konami Logo (`konami.bin`), Sound Test (`sound_select.bin`, `sound_select_cursor.bin`), Puzzle (`puzzle.bin`, `puzzle_bg.bin`, `puzzle_tiles.bin`).
   - Every new background or fixed screen MUST follow this binary runtime pipeline, never a static PNG.

## MANDATORY RULE: MSX1 SCREEN 2 decoding and 1:1 letterboxing (NO STRETCH)

> **NEVER stretch or vertically upscale MSX1 (256×192) to MSX2 (256×212).**
> **MSX1 graphics MUST preserve crisp 1:1 square pixels, centered vertically with 10 px black letterbox.**

1. **TMS9918A SCREEN 2 VRAM Addressing Rules**:
   - **Bank Switching (3 Vertical Thirds)**: The 32×24 grid is divided into 3 vertical thirds (rows 0..7, 8..15, 16..23).
     - `third = ty / 8`
     - `pBank = (third & pMask) * 0x800` (Pattern Generator Table bank)
     - `cBank = (third & cMask) * 0x800` (Color Table bank)
   - **Color Table Indexing**: In SCREEN 2, each 8×8 pattern code has 8 dedicated color bytes (1 byte per row).
     - The formula is **strictly**: `colourBase + cBank + code * 8 + row`.
     - **NEVER** use SCREEN 1 indexing `(code / 8) * 8 + row` (that corrupts patterns and creates striped bugs).
2. **Aspect Ratio and Frame Alignment**:
   - Design resolution is **256×212** (MSX2 frame).
   - MSX1 fixed screens and cutscenes are **256×192**.
   - Alignment formula: `offsetY = (GameConstants.DesignHeight - screen.Height) / 2` (`= 10` pixels).
   - Drawing position: `Vector2(0, offsetY)` for screens, and add `offsetY` to all dynamic UI/actor coordinates (e.g. Title Menu cursor: `X = 56, Y = offsetY + 136 + option * 16`; Pyramid intro: `y = IntroYAt(tick) + offsetY`).
   - `ScreenBuilder.BuildTexture` must NEVER apply nearest-neighbor stretching from 192 to 212.
3. **Gameplay Room Dumps vs Dynamic Sprites**:
   - Background dumps (`sNN_rMM.bin`) must have all items and soulstones **stripped/restored to clean mural tiles**.
   - `RoomRenderer.cs` renders items (`ItemSprite(item.Kind)`) and soulstones (`soulstone_p{p}`) as dynamic sprites.
   - If items are baked into the room dump bitmap, collecting an item removes the sprite but leaves a ghost item in the background.

## MANDATORY RULE: cutscene actors per flavor

- MSX1 intro playback uses the native `intro_vick.json` vblank timeline and matching `intro_vick.bin` sprite strip. Positions, jump/landing, raw poses and visibility come from the cartridge's actual SAT records 4..7. Preserve the raw strip/timeline as extraction evidence. The user-requested door presentation reuses the level's back-facing `vick_d_00`/`vick_d_01` for recorded arrival poses 3/4; `Msx1IntroPresentation` removes their seven transparent top rows and restores the second pose's trimmed left column to retain the native actor anchor. Preload the player atlas before this timed scene starts. Add only the 10-pixel presentation letterbox; never apply MSX2 paths or an authored fade to these frames.
- The MSX2 intro retains its independently measured path. Do not transfer that timing, palette or actor origin to MSX1.
- MSX1 level-door poses come from native `531F` / `8CE4`, eight frames. Table `8597` / writer `9D07` belong to Game Over and must not be used as door-entry art.
- Native MSX1 `5680` maps logical held-item poses 10..13 to source indices 8..11. Keep both animation phases and both directions; do not collapse the pairs.

## MANDATORY RULE: cartridge-specific validated graphics generations

- Graphics live under `<MSXMusic>/kings-valley-2/<msx1|msx2>/Graphics/`.
- `current.json` selects an immutable validated generation under `generations/`. Staging and incomplete attempts are separate; a partial/cancelled/failed attempt must not replace a complete generation.
- `graphics-manifest.json` records exact ROM SHA-1, profile/schema revision, inventory checks, artifact SHA-256 and readiness. Structural validity is not evidence of pixel fidelity.
- `AudioBootstrap.GraphicsDirectoryFor(flavor)` and `RomContent` resolve only that flavor. Built-in MSX1 has no MSX2 base; custom packs cannot inherit across flavors.
- Ignore the legacy mixed `MSXMusic/KingsValley2` and `%APPDATA%/KingsValley/extracted` roots. Do not delete or silently adopt them.
- A development bundle must carry the same matching manifest and complete inventory as an extracted generation. A file's existence alone never makes a flavor playable.
- Screens/rooms remain binary at native dimensions. Flag bit 1 identifies the fixed TMS9918 palette; legacy flag-zero V9938 files keep their 38-byte header contract.
- English offline reports live beside Graphics in `Report/` and describe every attempted extraction, including unsupported components and blank/duplicate atlas entries.

## MANDATORY RULE: zero procedural graphics and the Game Over extraction trap

> **NEVER draw sprites, actors, or tiles procedurally (NO boxes, rectangles, or procedural placeholders).**
> **EVERY PIXEL MUST come directly from ROM bytes or authentic VRAM tables.**

1. **Zero Procedural / Geometric Fallbacks (STRICT)**:
   - NEVER use `DrawVick()`, `Canvas.Rect()`, or synthetic box placeholders when extracting or rendering actors.
   - MSX2 Vick walking, door entrance, and death poses MUST be extracted directly from ROM segment tables (`0x869C`, `0x86B8`, `0x953D`, `0xABB9`) via `RomDirectExtractor`.
   - MSX1 Vick poses MUST be captured from the active TMS9918 sprite layer while the player is alive in stage 1 (`RomScreens.CaptureGameplayAssets`).
   - `intro_vick.bin` and `world_vick.bin` MUST contain the 5 authentic 16×32 poses (`vick_s0_03`, `vick_s0_04`, `vick_s0_05`, `vick_d_00`, `vick_d_01`).

2. **The 1400-Frame "Game Over" Extraction Trap**:
   - In `MiniMsx` emulation (`RomRoomDumps.cs` and `RomScreens.cs`), **NEVER** wait blindly for 1400 frames without input.
   - Stage 1 room 1 starts around frame ~820–860 from boot (Title → Start Menu → Pyramid Intro → Room 1).
   - Waiting 1400 frames causes the stage timer to expire or enemies to kill the player, entering **GAME OVER**.
   - During Game Over:
     - Player sprites are cleared from VRAM → `poses.Count == 0`, which triggered procedural fallbacks!
     - VDP Pattern Generator and Color tables are overwritten with Game Over/Title data → dumping rooms at this point corrupts walls into concentric boxes, sand into blue stripes, and murals into red circuit noise!
   - **Rule**: Always capture room dumps and player poses between frames 860 and 920, and verify RAM `0xE280 == 0` (player alive) and `0xE242 == 1` (stage 1 active).

3. **MSX2 vs MSX1 Priority & Engine Architecture**:
   - **MSX2 is ALWAYS Preferred**: When the MSX2 ROM (`kv2_msx2_scc.rom` or `kv2_msx2.rom`) is present, `RomContent.GetFirstPlayableFlavor()` MUST select `ArtFlavor.Msx2`.
   - **MSX2 Pipeline**:
     - `Msx2VdpRunner.cs`: Emulates the V9938 commands (`HMMM`, `LMMM`, `HMMC`) and BIOS traps, extracting the genuine 256 4bpp tileblocks (`blocks_p1.bin` .. `blocks_p6.bin`, 8192 bytes each) in ~5 seconds.
     - `RomDirectExtractor.cs`: Unpacks sprites, murals, decorations, and animations in ~0.2 seconds.
     - Total MSX2 extraction time: **< 6 seconds**. DO NOT touch or rewrite this pipeline; it is already fast, pure C#, and mathematically exact.
   - **MSX1 Pipeline**:
     - Uses TMS9918 SCREEN 2 dumps (`sNN_rMM.bin`, 256×192) centered vertically (`offsetY = 10`), with dynamic items/soulstones restored to murals via `murals.bin`.
     - `RomContent.AtlasPath` resolves the active flavor's validated atlas. MSX1 never borrows the MSX2 atlas or vice versa.
     - Clean MSX1 door restoration uses `Rooms/door_clean.bin` decoded from native pattern/color bytes, with matching `door_clean_msx1_{b}` atlas regions. No embedded hexadecimal image is allowed.


## 🚨 Before anything else: consult the documentation

| When | Where |
| :--- | :--- |
| **Any question about Gum** (layout, units, Forms, events, XML) | **`gum-docs` MCP** (configured in `opencode.json`) — `searchDocumentation` / `getPage` / `askQuestion` tools. Without MCP: `https://docs.flatredball.com/gum/<pagina>.md?ask=<pergunta>` · index in `sitemap.md` |
| **Skills** (durable context, they load on their own) | `.claude/skills/gum-overview`, `gum-layout`, `gum-forms-controls`, `gum-file-format` (XML enum tables), `gumcli`, `monogame-input` |
| **Monogame / C# / tests** | `monogame-*`, `csharp-testing`, `dotnet-patterns` skills |

> **Never answer from "memory" about an API.** It has already cost us: we thought a zero-size parent
> required `ExposeChildrenEvents` and the doc says the property is `RaiseChildrenEventsOutsideOfBounds`; and a
> PowerShell `-replace` corrupted names (`Berlin`→`Ferlin`) and only the game log revealed it
> (`KernSmith: System font not found`).

## Build, tests and execution

```bash
dotnet build KingsValley.slnx            # build obrigatório antes de concluir qualquer tarefa
dotnet test                             # testes de unidade de KingsValley.Core
dotnet gumcli check src/KingsValley.Desktop/Content/Gum/GumProject.gumx   # UI: sempre que mexer em .gusx/.gucx
dotnet run --project src/KingsValley.Desktop
```

Always run `dotnet build KingsValley.slnx` before declaring a task complete.

**Diagnostic variables** (to reach a specific state without playing): `KINGSVALLEY_START_SCENE`
(`splash`/`menu`/`loading`/`level`), `KINGSVALLEY_DIALOG` (`players`, `options`, `gamemode`…),
`KINGSVALLEY_LEVEL` (`3`, `3W`), `KINGSVALLEY_POWERUPS` (`locked`/`empty`/`stock`),
`KINGSVALLEY_SKIP_LEVELSTART=1`, `KINGSVALLEY_FAST_BOOT=1`, `KINGSVALLEY_SCREENSHOT` + `_FRAME`,
`KINGSVALLEY_WINDOW` (`256x212`/`1024x848`/`1920x1080`), `KINGSVALLEY_ART_SCALE` (`1`/`2`),
`KINGSVALLEY_GFX` (`msx2` default / `msx1` / id of a pack in `Content/ArtSets/` — art set;
in the game the **system menu** (GRAPHICS) and the **F11** key cycle them — `F2` is the Sound Test of the debug shortcuts; see `docs/ART_SETS.md`),
`KINGSVALLEY_TITLE_LANG` (`jp` default / `en` — title art language; MSX1 carries both screens; the verified MSX2 profile uses its native Japanese title
and the **system menu** (ESC) also switches and saves the preference), `KINGSVALLEY_MENU_PAGE` (`title`/`select`/`password` — opens the menu directly on the page),
`KINGSVALLEY_SYSMENU=1` (opens the **ESC** menu by itself — screenshot/CI gate),
`KINGSVALLEY_I18N_DEBUG=1`.
For **documentation/CI**: `KINGSVALLEY_PLAYER=room,x,y` (positions Vick), `KINGSVALLEY_INPUT=<file>`
(tick-by-tick input script, see `src/KingsValley.Engine/Input/InputScript.cs`) and
`KINGSVALLEY_CAPTURE_DIR` + `_FROM`/`_TO` (PNG sequence; `tools/docs/capture.ps1` + `make_gif.py`
generate the GIFs for `docs/en/media`).
The complete, commented list is under "Diagnostic environment variables" further below.

> 📘 **Illustrated documentation in English:** [docs/en/README.md](docs/en/README.md) — gameplay with GIFs,
> ROM engineering (time model, `E600`/`E300` tables, findings) and architecture.

---

## MANDATORY RULE: render layers (`RenderLayer`) always named

> **NEVER** use magic numbers (literal `int`s) in `RenderLayer`.
> **ALWAYS** use a named constant from `KingsValley.Engine.RenderLayers`
> (`src/KingsValley.Engine/RenderLayers.cs`).

```csharp
// ❌ ERRADO — proibido
renderer.RenderLayer = -110;
renderer.RenderLayer = 20;

// ✅ CORRETO — sempre nomeado
renderer.RenderLayer = RenderLayers.PowerUpProjectile;
renderer.RenderLayer = RenderLayers.Token;
```

If the desired layer does not exist, **add a named constant** in
`src/KingsValley.Engine/RenderLayers.cs` with a comment explaining its role.
Never reuse an existing constant just to "avoid creating a new one" when the
semantics differ.

### Understanding the Nez rule

In Nez, the **LOWER the `RenderLayer`, the MORE IN FRONT** the element is drawn
(the `DefaultRenderer` walks the renderables in descending `RenderLayer` order).

Ranges in use (`RenderLayers`):

| Range | Use |
| :--- | :--- |
| `1000` | Background (Fit Height) |
| `70`..`10` | Board: borders, tiles, golden tile, water, ice, pieces, selection, overlays |
| `0` | Legacy `Nez.UI` HUD (`RenderLayers.Hud`) — **deprecated**, see the UI (Gum) rule below |
| `-50` | Match particles (vegetable pieces) |
| `-200`..`-280` | **Power-Up effects — ALWAYS on top of everything except the cursor** |
| `-9999` | GamePad virtual cursor — **replaced** by `PostUiOverlay` (see below) |

### Power-Up effects always on top of everything

Bombs, rockets, LightBall, Firecracker and all their beams, rings, flashes,
sparks and explosions **MUST** use the `RenderLayers.PowerUp*` range (`-200` to `-280`).
They are the frontmost elements **of the scene**.

> ⚠️ **Note:** "in front of the scene" does **not** mean "above the UI". The Gum UI is
> drawn **after the whole scene**, and `PostUiOverlay` is drawn after Gum.
> See "Render layers (3-level model)" below.

Internal order of the range (back to front):
`PowerUpTrail` -> `PowerUpAura` -> `PowerUpGlow` -> `PowerUpFireSecondary` ->
`PowerUpFirePrimary` -> `PowerUpProjectile` -> `PowerUpBeamGlow` -> `PowerUpBeamBody` ->
`PowerUpBeamCore` -> `PowerUpRing` -> `PowerUpImpact` -> `PowerUpSparks`.

**Never** put Power-Up VFX on positive layers (e.g.: `2`, `5`): they end up
**behind** the HUD and the board and the effect "disappears".

### When creating new effects/HUD

1. Pick the correct constant in `RenderLayers` (or create a new one).
2. Make sure Power-Up VFX stay in the negative `PowerUp*` range.
3. Never change the value of an existing constant to "adjust" an effect —
   that moves all the other effects that use it. Create a new constant.

---

## Fonts (Gum + KernSmith)

### Never use Gum's default font

Without configuration, Gum uses a **fixed-size bitmap font** with limited coverage
(only the `.gumx` `FontRanges`). It **blurs when scaled** and locks positioning.

The project uses a **dynamic font**: the TTF is rasterized **on demand**, at the requested size —
crisp at any size, without declaring ranges, with accents and CJK ready.

### Original fonts (reused from Unity)

The Unity UI used **TMP SDF with borders** (`Assets/_Game/Art/fonts/`). We reuse the TTFs:

| Constant | File | Use |
| :--- | :--- | :--- |
| `GumFonts.Display` | `RaspoutineDemiBold_TB.otf` | titles/banners |
| `GumFonts.Ui` | `Berlin Sans FB Demi Bold.ttf` | UI/level texts |
| `GumFonts.Comic` | `KOMIKAX_.ttf` | comic style |

They live in **`Content/Gum/Fonts/`** — inside the Gum project, because Gum's `FileManager`
resolves paths relative to the `.gumx` folder (e.g.: `Fonts/Arquivo.otf` → `Content/Gum/Fonts/...`).

### How to register (already done)

`KingsValleyGame.Initialize` → `UI.Gum.GumFonts.Initialize(GraphicsDevice)`, after `GumUI.Initialize`:

```csharp
var creator = new KernSmithFontCreator(graphicsDevice);
KernSmithFontCreator.RegisterFont(family, "Fonts/Arquivo.ttf", style: null, faceIndex: 0);
CustomSetPropertyOnRenderable.InMemoryFontCreator = creator;   // a partir daqui o Gum resolve
```

> ⚠️ `RegisterFont` is **static** and `faceIndex` (4th parameter) is the face index in a TTC — `0`
> for single-face files. A missing font produces a **warning**, not an error (the game opens with the default one).

### Declaring the font: `Font` = FAMILY NAME (⚠️ **do not** use `CustomFontFile`)

Registering in C# **does not** create the elements: every text declares its font in the XML.

| Variable | Type | Role |
| :--- | :--- | :--- |
| **`Font`** | `IsFont` | ✅ **This is the one to use.** The name of an **installed** family (`Berlin Sans FB Demi`, `Raspoutine DemiBold`, `Komika Axis`) — or the path to a `.ttf` |
| `CustomFontFile` | `IsFile` | ❌ **Legacy `.fnt` bitmap flow**: the Gum Tool's "Use Custom Font" file picker **only accepts `.fnt`** (it does not accept `.ttf`). In this project it stays **empty** |
| `UseCustomFont` | bool | Only makes sense together with `CustomFontFile` (`.fnt`) — in this project it stays **false** |
| `FontSize` / `FontScale` | int / float | Size |
| `OutlineThickness` | int | **Native outline** (see below) |

#### Why an installed family (and not the `.ttf` path)

Gum has **two resolvers that disagree** — that is the pitfall that made text fall back to the default font:

| Who | Resolves the font by |
| :--- | :--- |
| **Runtime** (`BmfcSave.ResolveTtfSourcePath`) | `CustomFontFile` (when `UseCustomFont`) and, otherwise, `Font` |
| **Cache generator** (`gumcli fonts`, **Gum Tool**) | **only `Font`** |

If only `CustomFontFile` points to the `.ttf`, the cache is generated with **another name**
(`Font25Arial.fnt`) and the render looks for `Font25Berlin_Sans_FB_Demi_Bold_ttf.fnt` → *"No usable font
could be resolved…"* and the text falls back to **Gum's default font**. With `Font` = an **installed** family,
**all three** agree: the Tool dropdown, `gumcli fonts` (the Windows font service) and the runtime.

```powershell
# pre-requisito do editor: instala as fontes do projeto (per-usuario, sem admin)
powershell -File tools/install_gum_fonts.ps1
# e depois gera o cache de bitmap (.fnt/.png)
dotnet gumcli fonts src/KingsValley.Desktop/Content/Gum/GumProject.gumx
```

> ⚠️ **`.otf` does not work as a font path in Gum** (`BmfcSave.IsFontFilePath` only accepts `.ttf`).
> As an **installed family** it works normally — that is the case of `RaspoutineDemiBold_TB.otf`.
>
> ⚠️ `Content/Gum/FontCache/` is a **generated artifact** (it is in `.gitignore`) — do not version it.
> ⚠️ After generating the cache, **reopen the project in the Gum Tool**: it resolves font/cache on load
> and does not reload on its own.

**Where to change it:** the `forms` theme centralizes everything in **`Components/Styles.gucx`** — each style
(`Title`, `H1`..`H3`, `Normal`, `Small`, `Tiny`, `Strong`, `Emphasis`) declares `FontSize` **and** `Font`
(family name). The controls in `Components/Controls/*` reference these values through
`VariableReferences` (`Font = Components/Styles.Normal.Font`), so **getting `Styles.gucx` right is enough**.

### Font materials (the Unity gradients)

The Unity project has **7 font materials** in `Assets/_Game/Art/fonts/*.asset` — each one is a
**vertical gradient** (top → bottom) painted inside the glyph by TextMeshPro's SDF shader:

| Material | Top | Bottom | Uses in Unity |
| :--- | :--- | :--- | :--- |
| `Green` | `#2E690B` | `#2E690B` | 41 refs (menu button label) |
| `Orange` | `#FEDC7B` | `#FEB04A` | 50 refs (titles/values) |
| `Brown` | `#693714` | `#351B09` | 21 refs |
| `Yellow` | `#FEFF55` | `#FFB800` | 20 refs (highlighted values) |
| `Blue` / `DarkBlue` / `WhiteGreen` | — | — | 1 ref each |
| `DarkOrange` | `#FF7900` | `#E0FEFE` | 0 refs (unused) |

`GumFontMaterials.cs` reproduces this: it generates the atlas with KernSmith using `GradientStart/End*` +
`GradientAngle = 90` and assigns it to `TextRuntime.Typeface`.

> 📖 Gradient and outline color **do not exist** in `TextRuntime`'s property surface
> (only `OutlineThickness`, and the outline **always** comes out black) — that is why the atlas is assembled by hand:
> [Advanced Font Effects](https://docs.flatredball.com/gum/code/files-and-fonts/fonts/advanced-font-effects).
> The complete flow is in [Font Strategies](https://docs.flatredball.com/gum/code/files-and-fonts/fonts/font-strategies).

**Unity's "blurred outline"** = a shadow baked into the atlas (`ShadowOpacity 0.55`, `ShadowBlur 3`,
`OffsetY 2`, black) — KernSmith has no outline opacity, but it does have a soft shadow ✓.
That is why the label **does not** use `OutlineThickness` when there is a material.

⚠️ Three rules when using a material:
1. After assigning the `Typeface`, **do not** touch that `TextRuntime`'s `Font`/`FontFamily`/`FontSize`
   again — Gum's font system regenerates and overwrites the atlas.
2. The font is generated per **(material, size, family)** and cached: if you changed the `FontSize`, call it again.
3. `Typeface` is the current API; `BitmapFont` is a deprecated alias (`CS0618`) —
   see [Migrating to 2026 July](https://docs.flatredball.com/gum/gum-tool/upgrading/migrating-to-2026-july-2nd-release).

**Current state:** it works and is applied to the **dialog titles** (Game Mode, Error, Players,
Options, Create Player, Quit Game…), to the GameMode/Difficulty **descriptions**, to the **level
start banner** ("Order N!" green / "Water" orange) and to the **menu button label** (which got the soft
shadow instead of the hard black outline).

**Known limit (accepted):** the material only holds at the **design resolution (zoom 1)**. At resolutions
with zoom ≠ 1 the oversampling regenerates the regular font and the text goes back to white **without a
gradient** — but it stays **crisp and legible** ✓. **I did not try to "fix" it by generating the atlas
multiplied by the zoom:** `TextRuntime` draws glyphs at the `FontSize` scale and the text ends up
**duplicated/overlapping** (tested at 1920×1080). Oversampling is a preview feature
([doc](https://docs.flatredball.com/gum/code/files-and-fonts/fonts/font-oversampling)) — if one day it
respects the assigned `Typeface`, the material works again at every resolution.

**Three mistakes that cost time (do not repeat):**
1. `GumFontGenerator.Generate(bmfcSave)` **rebuilds the save's options and discards everything** that was
   adjusted (gradient/shadow) — it is only useful when nothing needs customizing. With effects, use
   `BmFont.Generate(caminhoDoTtf, options)` after `GumFontGenerator.BuildOptions(save)`.
2. `BmFont.Generate` takes the **TTF path**, not the family name
   (`Could not find file '...\Berlin Sans FB Demi'`). Use `GumFonts.FileOf(familia)`.
3. The **channel layout** matters: `BuildOptions` chooses based on the save's `OutlineThickness` (0 = "plain
   text", RGB=1white) and because of that the compositor discards the gradient **and** the shadow color →
   with a shadow, leave `options.Channels = null` (full RGBA).

**Oversampling is not the villain** (tested: turning it off changes nothing) — the cause was item 1.

### 📋 Map of the materials (extracted from the Unity scenes)

The `.asset` files are **presets**; the real value is each TMP text's `m_fontColorGradient`. Result of the
scan (211 texts, 88 with a gradient):

| Elements (in the port) | Preset | Real gradient | Flat color |
| :--- | :--- | :--- | :--- |
| Dialog titles (Game Mode, Game Difficulty, Players, Enter your name, Error, Quit Game), Options headers, GameMode/Difficulty descriptions | `Orange` | `#FFD800 → #FF8E00` | white |
| "Order N!" banner | `WhiteGreen` | `#90FF00 → #6B8F3F` | white |
| book "?", "Water" banner | `Blue` | `#FFB900 → #F76A07` | white |
| "More Games" | `Green` | white/white | white |
| **Buttons** (Ok, New, DELETE, Cancel, NO, YES, Play, Options, Quit) | — | **no gradient** | `73,0,0` (dialogs) · `41,108,21` (menu) |
| Level options/labels | — | no gradient | `254,192,164` |

The script that generates this table is at `%TEMP%\map_textos.ps1` (it reads `m_enableVertexGradient`,
`m_fontColorGradient`, `m_fontColor`, `m_fontSize` and each TMP's preset, and exports CSV) — rerun it
if you need to review.

### Outline

Unity used **SDF with borders** ("Black Border"). In Gum the outline is **native**: the text element's
**`OutlineThickness`** variable (`Standards/Text.gutx`) — there is no need to draw
twice.

> 📖 [`TextRuntime.OutlineThickness`](https://docs.flatredball.com/gum/code/gum-code-reference/textruntime)
> controls the **thickness**, but **not the color**: in the property path the outline is **always
> black** (to choose the color you must build the `BitmapFont` with KernSmith and `OutlineR/G/B` — see
> [Advanced Font Effects](https://docs.flatredball.com/gum/code/files-and-fonts/fonts/advanced-font-effects)).
>
> ⚠️ **Dark** text + black outline thickens and becomes a "blob". If the text over dark wood is not
> legible, the fix is a **light text color** (Unity uses white/cream in several places) and not a thicker
> outline. `HasDropshadow` (with `Dropshadow*`) is another option, independent of the outline.

> ⚠️ Do not confuse this with *stroked/gradient* from third-party libraries: here Gum itself draws the
> outline. If the quality is not enough for a specific case, then evaluate
> **FontStashSharp** (native stroked text).

### MANDATORY: desktop editor font scaling and maximized-window performance

The desktop level editor is a **physical-pixel UI**, not the gameplay design-resolution UI.
Keep these two rendering contracts separate:

- **Editor:** Gum camera zoom is **1**, canvas dimensions are the physical back-buffer dimensions,
  positions and sizes go through the workspace's `Position` helper, and font sizes are
  `logicalFontSize * uiScale`. Icons also use the physical UI scale. Mouse hit testing stays in
  logical workspace coordinates through `EditorWorkspaceLayout.ToUiPoint`.
- **Gameplay and other design-resolution screens:** retain their existing design viewport and
  native Gum font oversampling. Do not disable `TextRuntime.UseFontOversampling` globally.
- **Never restore editor camera zoom = `uiScale`.** With reconstructed editor labels at zoom 2,
  maximizing caused repeated font rasterization during `GumUI.Draw`: measured **1.49–1.64 seconds**
  and about **2.22 GB of temporary managed allocations** after room changes. Room assembly and UI
  rebuilding were only a few milliseconds; changing ROM extraction or room logic would not fix it.
- Do not compensate with `FontScale = 1 / zoom`. The editor's coordinates and font sizes already
  describe physical pixels; applying another camera/oversampling scale repeats the work.
- Relevant code: `KingsValleyGame.UpdateGumViewport`, `EditorScene.Workspace` (`Rebuild`, `Label`,
  `Position`, `Icon`) and `EditorWorkspaceLayout.ToUiPoint`. New editor controls must follow this
  same contract, including dialogs and overview previews.
- Verify font/layout changes at the normal window size **and maximized/4K**, including room
  switches, dialogs and returning from Test. Inspect automatic `Diagnostics/editor-performance-*.jsonl`
  logs: `gum.draw`, `ui.rebuild`, `frame.interval`, allocations and `input_to_draw`. A fast room-preview
  average alone is **not** evidence that the whole interface is responsive.
- Separate cold initialization from repeated interaction. The controlled
  `KINGSVALLEY_EDITOR_REBUILD_PROBE=1` test in 3840 × 2160 measured a warm rebuild around **4.45 ms**
  after the correction, without the multi-gigabyte Draw allocation. The user confirmed that
  maximizing was responsive again on 2026-11-26.

### ⚠️ Text crispness: use the NATIVE oversampling (design-resolution screens)

Gum is drawn with `Renderer.Camera.Zoom = DesignViewport.Scale` — **2.813× on a 4K monitor**. A
glyph rasterized at the *design* size (24 px) and **upscaled** by the camera blurs. Gum
**2026.9.2.1** already solves this by itself:

```csharp
// GumFonts.Initialize — junto do UseAutomaticFontGrowth
TextRuntime.UseFontOversampling = true;
```

| | |
| :--- | :--- |
| ✅ **Do** | `UseFontOversampling = true`. Gum re-rasterizes at the native scale and compensates in the draw, hooking into the zoom and the resize **by itself** |
| ❌ **Don't** | `FontSize × zoom` + `FontScale = 1/zoom` by hand: it freezes at the size at which the label was **created** (already tried and removed) |

> The release from **02/09/2026** (exactly the version used here) brings *"Fixed automatic font
> oversampling **never actually engaging** on MonoGame"* — before that the feature existed but **did not
> work** on MonoGame. Worth checking after any Gum update.

**SDF is not the way here.** KernSmith supports it (`WithSdf(true)`; the FreeType backend reports
`SupportsSdf = True`), but an SDF atlas **needs an SDF shader** when drawing — `Gum.MonoGame`
has none, and the official Gum integration (`KernSmithFontCreator`) **does not enable** SDF. Without the
shader the text gets **worse** (gray/blurred). Oversampling delivers the same practical effect as Unity's TMP,
without touching the renderer.

> If one day you want to go further: KernSmith exposes **`BmFontBuilder`** with `WithSdf` /
> `WithSuperSampling` / `WithAntiAlias` / `WithOutline(w, r, g, b)` / `WithGamma` — but there is **no
> hook** to inject it into `KernSmithFontCreator` (the `BmfcSave` it reads only has `FontSize`,
> `OutlineThickness`, `UseSmoothing`, `FontFile`, `Spacing*`, `Ranges`…). Using those extras requires your
> own **`IInMemoryFontCreator`** (a 1-method interface: `BitmapFont TryCreateFont(BmfcSave)`)
> consuming `BmFontBuilder`.

---

## MANDATORY RULE: render layers (3-level model)

The project has **three** drawing layers, in this order (bottom to top):

```
┌─ PostUiOverlay ── cursor, efeitos episódicos e textos acima da UI
├─ Gum UI ────────── toda a interface (sempre acima da cena)
└─ Cena (Nez) ────── RenderLayers internos (1000 background ... -280 power-ups)
```

The order is fixed in `KingsValleyGame.Draw`:

```csharp
base.Draw(gameTime);   // 1. Cena (blit para o backbuffer)
GumUI.Draw();          // 2. UI do Gum
PostUiOverlay.Draw();  // 3. Camada acima da UI
```

### Decision rule: where to draw?

| What you need | Where to put it |
| :--- | :--- |
| Game object (piece, effect, character) | Nez entity + `RenderLayers` |
| Interface (HUD, dialog, button, shop) | **Gum** |
| Something **above the UI** (cursor, screen flash, floating text) | **`PostUiOverlay`** |

> ⚠️ **A Nez `SpriteRenderer` NEVER sits above the UI** — not even with `RenderLayer = -9999`.
> If it needs to be above the UI, it is not a `RenderLayer`: it is `PostUiOverlay`. Check that
> **before** spending time adjusting layers.

### How to use `PostUiOverlay`

`Wagenheimer.MonoGameHelper.UI.PostUiOverlay`
(open-source package — see the "agnostic code" rule below)

1. Implement `IPostUiDrawable` and register it in the entity's lifecycle:

```csharp
public class MeuEfeito : Component, IPostUiDrawable
{
    public int PostUiLayer => PostUiLayers.FloatingText;   // NUNCA use números mágicos
    public bool IsPostUiVisible => true;

    public override void OnAddedToEntity()   { base.OnAddedToEntity();   PostUiOverlay.Register(this); }
    public override void OnRemovedFromEntity(){ PostUiOverlay.Unregister(this); base.OnRemovedFromEntity(); }

    public void DrawPostUi(SpriteBatch spriteBatch)
    {
        // Coordenadas em UNIDADES DE DESIGN — a matriz de escala já vem aplicada.
        SpriteBatchDrawing.FillRectangle(spriteBatch, new Rectangle(10, 10, 100, 20), Color.Red);
    }
}
```

2. Named layers in `PostUiLayers` (`WorldEffect` < `FloatingText` < `Pointer`).
   **The HIGHER the value, the more in front** (the opposite of Nez's `RenderLayer`).

### ⚠️ Proven API pitfalls (do not repeat)

| Pitfall | Reality |
| :--- | :--- |
| `batcher.Draw(texture, Rectangle, Color)` | Nez's Batcher **"RectSize" path does not render** (proven by test: zero pixels). Use `SpriteBatchDrawing.FillRectangle` (position + `Vector2 scale`). |
| `Graphics.Instance.PixelTexture` | Only exists if Nez was initialized **with a `BitmapFont`**. Use `SpriteBatchDrawing.Pixel` (the package's own 1x1). |

---

## MANDATORY RULE: all UI is made in Gum

> **NEVER** use `Nez.UI` in new code (`Label`, `Table`, `Button`, `UICanvas`, `Skin`...).
> **ALWAYS** use **Gum** (`Gum.MonoGame`). `Nez.UI` is being removed from the project.
>
> UI code lives in `src/KingsValley.Engine/UI/Gum/`.
> The layout files live in `src/KingsValley.Desktop/Content/Gum/` (`.gumx` / `.gusx` / `.gucx`).

### How to author: `gumcli`/Gum Tool first, XML edited with `check`

Gum's XML format **is** documented — by the team itself, in the `gum-file-format` skill (which comes from the
Gum repository). Manual editing is allowed, under one non-negotiable condition:

> ⚠️ **`XmlSerializer` ignores any element/tag it does not recognize — no error, no warning.** An
> `<InstanceSave>` instead of `<Instance>` (or `<States>` instead of `<State>`) loads as an **empty**
> element: the UI simply disappears. That is why **every** manual edit ends with
> `dotnet gumcli check` and it has to answer `No errors found`.

```bash
dotnet tool restore                                   # instala o gumcli (.config/dotnet-tools.json)

# Criar um projeto novo (forms = botões/sliders com estados; empty = mínimo)
dotnet gumcli new src/KingsValley.Desktop/Content/Gum/GumProject.gumx --template forms

dotnet gumcli check      src/KingsValley.Desktop/Content/Gum/GumProject.gumx   # OBRIGATÓRIO
dotnet gumcli fonts      src/KingsValley.Desktop/Content/Gum/GumProject.gumx   # antes de screenshot
dotnet gumcli screenshot src/KingsValley.Desktop/Content/Gum/GumProject.gumx <Screen> --width 256 --height 192
dotnet gumcli codegen    src/KingsValley.Desktop/Content/Gum/GumProject.gumx   # opcional
```

**In the XML, never write magic unit numbers.** Enums are written as **int**
(`<Value xsi:type="xsd:int">`). Use the tables in the `.claude/skills/gum-file-format` skill:
`WidthUnits` `0`=Absolute, `1`=PercentageOfParent, `2`=RelativeToParent ·
`XUnits` `0`=PixelsFromLeft, **`6`=PixelsFromCenterX**, **`7`=PixelsFromCenterY**.

**Screen elements are created in the Gum Tool (or generated by a script faithful to the skill); C# only wires
the UI to the logic** (events, texts, navigation), referencing elements **by name**.

### Why Gum

- The layout lives in versionable, reusable **XML** (`Screens/`, `Components/`), editable in the **Gum Tool**.
- **Button states** (Normal/Hover/Pressed/Disabled), **nine-slice** and **animations** with native support.
- **Resolution-independent by design** — see the resolution rule below.

### How to initialize (already done in `KingsValleyGame`)

```csharp
public GumService GumUI => GumService.Default;

protected override void Initialize() => GumUI.Initialize(this, "Gum/GumProject.gumx");
protected override void Update(GameTime gt)  { base.Update(gt); GumUI.Update(gt); }
protected override void Draw(GameTime gt)    { base.Draw(gt);   GumUI.Draw(); }
```

`GumUI.Draw()` **always after** `base.Draw(gameTime)` — Nez blits the scene to the
backbuffer inside `base.Draw` (`Scene.cs:507-515`). Drawing before hides the UI behind the scene.

### Mandatory best practices

1. **Size never comes from the image.** Every element has `Width`/`Height` in design units.
   Never derive size from `Texture.Width`/`Height`.
2. **Layout via anchors/dock** (Dock, Anchor, Percent, Units). Never fixed pixel coordinates —
   the window is resizable (`Window.AllowUserResizing = true`).
3. **Nine-slice assets** for frames and buttons (`frame_popup`, `btn_med`, `goal_toolbar`),
   preserving the borders at any size.
4. **Every button defines the 4 states**: Normal / Hover / Pressed / Disabled
   (`btn_*.focused.png` and `btn_med.disabled.png` art).
5. **`Content/Gum/**` must be in `CopyToOutputDirectory`** — without that the `.gumx` does not load.
6. **Dynamic fonts** to support accents/CJK (see the **Fonts** section above and the
   `monogame-input` skill for input; the screen font is `Font` = registered family).
7. **Element names are stable**: C# references them by name. Renaming in `.gumx` breaks the code.
8. **Business rules outside the UI**: validation and defaults live in `KingsValley.Core`, with tests.
9. **Never** use Nez's `RenderLayer` for Gum UI — the order belongs to Gum (tree / Layered Forms).
10. **A state sprite has ONE owner**: do not put the same sprite in two visibility lists.
    The difficulty radio `check` was in `_difficultyChecks` **and** in `_difficultySprites`
    (which is forced to `Visible = !relaxed`), so **both** showed as checked. Each state
    element answers to **one** rule.
11. **Forms for what is clickable, raw visual for what is decoration**
    ([`for-wpf-users`](https://docs.flatredball.com/gum/code/about/for-wpf-users)): `Button`,
    `CheckBox`, `RadioButton`, `Slider`, `TextBox` are **Forms** (they bring state/hover/click/focus);
    label, icon, frame and HUD bar are raw `TextRuntime`/`SpriteRuntime`. Do not reimplement
    hover/hit-test by hand — that is what Forms already does.
12. **Level HUD hit-test: pass-through root + interactive layers with REAL bounds.**
    (doc: [`event-bounds-and-clipping`](https://docs.flatredball.com/gum/code/events-and-interactivity/event-bounds-and-clipping))
    - **HUD root** (`LevelHudScreen`, screen-sized): `HasEvents = false`,
      `ExposeChildrenEvents = true`, `RaiseChildrenEventsOutsideOfBounds = true`. If the root had
      `HasEvents = true`, the hit-test stops at it and **no child button** receives a click — the symptom is
      "I clicked and nothing happened".
    - **Layer with buttons** (`TopBarLayer`, `PowerUpBarLayer`): **real** `Width/Height`
      (`Absolute`). A parent with `AbsoluteHeight = 0` blocks the hit-test, and
      `RaiseChildrenEventsOutsideOfBounds` **does not replace** the parent's area.
    - These layers are anchored by the **center**: keep `XUnits/YUnits = PixelsFromLeft/Top` and
      position with `X = centroX − Width/2`, `Y = centroY − Height/2`. **Never** use
      `PixelsFromMiddle` in the `SetDesignPosition` of a layer hanging directly off Gum's root: the root
      adds its own center and shifts the whole HUD.
    - **Official diagnostic:** `FormsUtilities.Cursor.GetEventFailureReason(controle)` or
      `GetEventFailureReason("Nome")` in the update. It points to the guilty ancestor; e.g.:
      "cursor is over [LevelHudScreen] instead of [Button named btMenu]".
    - Full-screen dialog (Forms) still uses real bounds (`100%` of the canvas) and children by the center
      (`DialogLayer`/`GumDialog.LayerFillsCanvas`).
    ⚠️ Fix measured in the Level HUD: `LevelHudScreen` pass-through + `TopBarLayer`/`PowerUpBarLayer`
    with real `1100x100` / `320x120` (corner origin, position `center − half`).
13. **Category state that changes size: write the value in every state** (`ApplyState` does not
    reset) **and watch the inherited unit**. `WidthUnits = RelativeToParent` (2) plus the parent:
    a 284 slot became **568**. In a state, change it by **delta** (`0`, `+4`, `−4`).
14. **Generate XML by script**: copy the shape of a variable that already exists in the file and **regenerate**
    instead of patching. And check the result with `gumcli check` + `screenshot` + a pixel diff.
    ⚠️ **Pitfall of the script itself (PowerShell)**: `-replace` treats `\r`/`\n` in the
    replacement string as a **regex escape** and eats surrounding letters — it has already corrupted `Berlin Sans` →
    `Ferlin Sans`, `Bar` → `Far` and `BaseType` → `FaseType`. Use `.Replace()` (literal) to edit
    XML/text, or regenerate the whole file. **Always** check with `gumcli check` + the game log
    (`KernSmith.FontParsingException: System font 'X' not found` = corrupted font).

### RULE: migrating a Unity prefab → `.gusx` screen (tree, unique names, hit-test)

Migrating is **not** "dumping everything at the base": the screen reproduces the **hierarchy** of the Unity prefab
(`formOptions.prefab` → `DialogLayer/Background/MusicSound/MusicVolume/…`). That is what makes the Gum
Tool useful — dragging a container moves its children.

| Lesson | Detail |
| :--- | :--- |
| **An instance name is unique in the SCREEN (not in the parent)** | Gum keeps instances in a **flat list** and references them by qualified name (`Instancia.Propriedade`). Unity's repeated children (`Background`, `Title`, `Checkmark`) need a **prefix** (`ShowHelpBox`, `RelaxedTitle`, `CasualCheck`…). Reusing the same name inside a row only with **Components** (`.gucx`) |
| **LayoutGroup nodes come as `apos 0,0 size 0x0`** | The real position is computed at runtime — it cannot be copied from the prefab. Give the container the **center of the children's box**, preserving the positions the port has already validated (the goal is to keep the visual) |
| **Dialog container** | The screen declares a `DialogLayer` container (center-anchor, size 0) and C# **re-attaches** it to `GumDialog.Layer` (`GumScreens.CreateLayer`) — the open/close animation now applies to the content |
| **Hit-test anchored on the element** | `AbsoluteLeft`/`AbsoluteTop` **already discount the origin** (`XOrigin`). Use them instead of duplicating offsets: the slider derives the offset from the track, the button from the normal sprite, the option row from the `Box` frame. Moving it in the Tool moves the clickable area with it |
| **C# navigates the tree** | `GetGraphicalUiElementByName` is **recursive** — the search works from any level. Navigating (`Background` → `MusicSound` → `MusicVolume`) is living documentation of where each control comes from |
| **Prove the layout did not change** | Compare the frame's **bounding box** with the reference shot (`docs/progress/shots/ui-dialog-*.png`) and log the derived absolute positions against the old constants |

Implemented example: `OptionsDialog` (85 instances, 27 containers) — see `docs/NEXT_SESSION.md`, Task 5.

#### Screen texts: English default in the `.gusx`, **key in C#**

The `.gusx` holds the **English default** text (the screen stays readable in the Gum Tool and exists without code)
and C# **always** overwrites it with `Localization.Loc[...]` — **the screen is not the source of the text**. The
name→key mapping stays in a single place (`OptionsDialog.LocalizeTexts` / `BuildButtons`). Forms buttons
receive `Text` through the control (`GumFormsTheme.FindButton(...).Text = Loc["ok"]`), **not** through the
component's internal name (each button's `TextInstance` would collide between instances).

> ⚠️ **DO NOT** register Gum's `LocalizationService` (the "native" alternative): its `Translate`
> returns the id with a **`(loc)`** suffix when the key does not exist, and `Text.gutx` has
> `LocalizeText = true` by default — so **every** literal or dynamic text (player name,
> "Version 1.101 Beta", `"Score: " + score`) would get `(loc)`. The single source is the project's
> localizer (`Localization.Loc`, with `Localization.Plain` to strip the markup tags from I2/TMP).
>
> Known gaps (`TODO(i18n)`): terms missing from the spreadsheet (`LanguageDialog`, the menu's
> "Player" fallback) and language switching, which **recreates the scene** instead of re-translating the live tree.

### Gum Forms input: bridge with `InputHelperManager` (Phase 0 ✅)

`MonoGameGum`'s default cursor is bound to the **hardware mouse** (`X`/`Y` with `private set`;
`PrimaryPush/Down/Click` read from `MouseState`/`TouchCollection` inside Gum). To use Gum Forms
controls (state, hover, `Click`, focus) with the project's input — including the **GamePad
virtual cursor** — we swap the cursor:

```csharp
// KingsValleyGame.Initialize, depois de InputHelperManager.Initialize
global::Gum.Forms.FormsUtilities.SetCursor(new UI.Gum.InputHelperCursor(InputHelperManager.Instance));
```

`InputHelperCursor : ICursor` (`src/KingsValley.Engine/UI/Gum/InputHelperCursor.cs`) reads the hybrid
pointer (`VirtualCursor.Position` when the device is GamePad/touch, otherwise `Nez.Input.MousePosition`),
returns `X`/`Y` in **screen pixels** and `XRespectingGumZoomAndBounds()` in **design units**.

**Proven pitfalls (do not repeat):**

| Pitfall | Reality |
| :--- | :--- |
| `FormsUtilities.Update` with the window **unfocused** | Early return `if (game != null && !game.IsActive) return;` → **no** UI input is processed. In an automated test, activate the window (`AppActivate`) |
| **A ZERO-size parent blocks the hit-test** | 📖 [official doc](https://docs.flatredball.com/gum/code/events-and-interactivity/event-bounds-and-clipping): *"children controls only respond to cursor events if they are in their parent bounds"*. With the zero-size anchor layer (the standard for design positions), **nothing** inside responds. The fix is `layer.RaiseChildrenEventsOutsideOfBounds = true` (default = `false`) — **not** `ExposeChildrenEvents`, which is not the documented property. Alternative for a full-screen dialog: real bounds (`100%` of the canvas) |
| Where the cursor is read in the hit-test | `CheckHasCursorOverOnThis` uses **`cursor.X`/`cursor.Y`** (pixels) → `camera.ScreenToWorld` → `IsPointInside`. (`XRespectingGumZoomAndBounds()` is used in `isInWindow` and other places) |
| `GumService.Cursor` (typed property) | It does `FormsUtilities.Cursor as MonoGameGum.Input.Cursor` → **null** for a custom cursor. Use `FormsUtilities.Cursor` (the interface) |

**Validation (Phase 0):** a temporary probe with a theme `Button` — `Click` fired with the injected
cursor and `projectDesign == gumDesign == cursorXY == world`. And the current UI (manual controls)
**did not** change (OptionsDialog bbox identical).

> **Status:** the migration to Forms is **complete** — all dialogs, the menu and the HUD are in
> `.gusx`/`.gucx` with Forms controls, and `ClickableRegion`/`DialogButton`/`MenuButton`/`GumSlider`/
> `GumCheckBox` were **deleted** (no reference left). `CreatePlayer` still uses input
> through `Window.TextInput` (description below). The HUD's zero-size anchor layer uses
> `RaiseChildrenEventsOutsideOfBounds = true` — without that **nothing** in the HUD responds to the mouse.

#### Typed text: **do not** use the Forms `TextBox` (decision)

`CreatePlayerDialog` has a name field and input **continues** through `Window.TextInput` (Unicode,
accents) + key edge handling for Backspace/Enter/Escape — **not** through a Forms `TextBox`. Reason: a
`TextBox` would require an `IInputReceiverKeyboard` bridge **and** synthetic input, which **SDL ignores** — the
automated gate (a screenshot at one frame) could not prove that typing works. The current path
is proven and does not depend on synthetic input.

The field's **layout** (the `profile_name_field` art + the text `TextRuntime`, left-aligned) lives
in the `.gusx`; C# only writes the text and the caret (`|` appended — the `profile_name_cursor` art would need
to measure the text width, which Gum only exposes after layout).

#### Visual of Forms controls: **factory** (`DefaultFormsTemplates` does not work for files)

A project component with `ButtonBehavior` is instantiated by the
`DefaultFromFileButtonRuntime` runtime, whose `AfterFullCreation` **already creates and binds** the `FormsControl`.
That is why:

| Attempt | Result |
| :--- | :--- |
| `DefaultFormsTemplates[typeof(Button)] = new VisualTemplate(...)` | ❌ *"the assigned Visual is already the Visual for another framework element"* |
| `CreateGueForElement` + `new Button(visual)` | ❌ does not create the children → *"does not have an instance called 'TextInstance'"* |
| **`ToGraphicalUiElement` + using the runtime's `FormsControl`** | ✅ children created + control bound |

`ToGraphicalUiElement` (not `CreateGueForElement`) is what runs `CreateChildrenRecursively` +
`AfterFullCreation`. `DefaultFormsTemplates` is for **visuals in code** (V3 built-ins).

**Project standard:** `GumFormsTheme.CreateButton("Options/Button")` (factory in
`src/KingsValley.Engine/UI/Gum/GumFormsTheme.cs`) — returns the Forms `Button` already with our art and
our states. The component lives in `Components/Options/Button.gucx`
(`ButtonCategory`: Enabled/Highlighted/Pushed/Focused/Disabled — *Pushed reuses the Focused art*).

### ⚠️ Gum API pitfalls (already paid for — do not repeat)

| Pitfall | Reality |
| :--- | :--- |
| `SpriteRuntime` created in code | `WidthUnits`/`HeightUnits` come as **`PercentageOfSourceFile`** (`Width = 270` = 270% of the texture). Use `DimensionUnitType.Absolute` + set `TextureWidth`/`TextureHeight` (assigning `Texture` does not sync) |
| `SourceRectangle` ignored | It is only applied with **`TextureAddress = Gum.Managers.TextureAddress.Custom`**; with the default (`EntireTexture`) Gum draws the **whole texture** squashed into the element |
| **Atlas art in a `SpriteRuntime`** | Rule: **runtime by name, preview by generated coordinate** — no hand-made rects. The single source is `Content/Gum/atlas-bindings.json` (`atlasBindings` = context-free art; `fileBindings` = contextual art). The **runtime** (`GumAtlasBinder`, called in `GumScreens`/`GumFormsTheme`) reads that JSON and binds the art **by name** in the Nez atlas → repacking the atlas **does not** break the game. The **Gum Tool (visual editor)** keeps working: `tools/sync_gum_preview_coords.ps1` writes the preview coordinates into the XML **from the atlas** (run it after `tools/pack_atlases.ps1`); `tools/audit_gum_atlas_coords.ps1` reports drift. **Context-dependent** art (power-up icon/badge, token, buttons per category) goes in `fileBindings` (preview) and is bound in C# (runtime) with `GumUiFactory.ApplySprite(sprite, atlas, "nome", maxW, maxH)` |
| A **new** screen/component disappears silently | The runtime reads `GumProject.gumx`: a screen needs `<ScreenReference Name="X" />` and a component needs `<ComponentReference Name="Controls/X" />`. Without the registration, `GumScreens` logs *"tela 'X' nao existe no GumProject — omitida"* (or `gumcli check` reports *"references … which is an invalid element"*). Add both entries when creating a new element |
| Child of a **component instance** on a screen | `GetGraphicalUiElementByName("Icon")` returns **null**: for an instance declared in the `.gusx`, the method matches the **qualified** name (`slotShovel.Icon`), not the local one. To find it by local name, descend through `element.Children` comparing `Name` (helper `PowerUpBarView.FindChild<T>`). A component created in code (`GumFormsTheme.CreateComponent`) **does not** have this problem (local names) |
| Runtime of a **component instance** | It is not a `ContainerRuntime`: `layer.GetGraphicalUiElementByName("slotX") as ContainerRuntime` gives **null** (the runtime is another type with an `InteractiveGue` wrapper). Work with `GraphicalUiElement` |
| Screenshot gate × unfocused window | When unfocused the window runs at a **very high framerate** and `dt` becomes tiny: the transition fade (0.25 s) does not finish on the requested frame and the PNG comes out **black**. The gate now only counts frames with the scene ready and the fade open (`KingsValleyGame.Draw`), with a fallback so CI does not hang |
| Atlas drawn by Gum | Load it with **`premultiplyAlpha: false`** (Gum uses `NonPremultiplied`). Scene/overlay atlas → `true` |
| Aligning the text of a `Label` | `XOrigin`/`YOrigin` position the BOX; to align the text use `TextRuntime.HorizontalAlignment`/`VerticalAlignment` |
| `TextRuntime.Color` | It is `Microsoft.Xna.Framework.Color` (MonoGameGum), not `System.Drawing.Color` |
| `Gum.Forms.Controls.*` in the `...Engine.UI.Gum` namespace | `Gum` resolves to the project namespace → qualify it as `global::Gum.Forms.Controls.X` |
| Coordinate system | Y down (same as Gum and MonoGame). There is **no** Unity flip in this project |
| `gumcli` **does not author** `.gusx` | There is no **headless authoring** command (only `import-screen`, for a file that already exists). A **new** screen = write the XML following the `gum-file-format` skill (or generate it by script) and **validate with `gumcli check`** — the Gum Tool is the visual editor |
| `gumcli screenshot` without fonts | The render **does not** have `KernSmith` (the game's dynamic creator): it resolves through the **`FontCache/*.fnt` cache**. Without running `gumcli fonts`, all text falls back to **Gum's default font** ("No usable font could be resolved…"). See the "Fonts" section |
| Duplicate instance name | The `.gusx` keeps instances in a **flat list** and qualifies them by name → names must be **unique in the screen**. See "migrating a Unity prefab → `.gusx` screen" |
| Hit-test with duplicated offset | With **nested** UI, a screen-center offset no longer holds. Use the element's own `AbsoluteLeft`/`AbsoluteTop` (they already discount the origin) |
| `CreateGueForElement(componente)` | **It does not create the children** (only the element's runtime) → a Forms control over that visual fails with *"does not have an instance called 'TextInstance'"*. Use `ToGraphicalUiElement` (the `Gum.ElementSaveExtensionMethods` extension), which runs `CreateChildrenRecursively` + `AfterFullCreation` |
| `ApplyState` **does not reset** variables | Each state only applies what it declares (`SetsValue=true`) — whatever a previous state changed **stays**. Every state of a category must declare **everything** it affects (the theme does this: it sets the colors in all 7 states) |
| `PercentageOfParent` on a sprite (state scaling) | Measured: the variable changes (100→104) but the **render does not** (0/255 difference in the screenshot). For "scale" per state use **`RelativeToParent`** with a px delta (`+7`/`-7`) — then the render does change (17/255) and the instance size still rules the base scale |
| Category variable **at the instance level** inside a component | It breaks `gumcli check` with `InvalidCastException: Unable to cast Int32 to String` (in `GumProjectDependencyWalker`). **Do not declare** `algumaInstancia.AlgumaCategoryState` in a component's Default — the internal controls handle their own state |
| `GumDialog.Layer` with bounds × dialogs in code | ⚠️ The dialog layer is **ZERO-sized** by default and that is **load-bearing**: dialogs assembled in code position their children with `PixelsFromLeft` from the layer's corner (which coincides with the center). Giving it bounds (`PercentageOfParent`) **shifts everything** by `(−DesignWidth/2, −384)`. `.gusx` dialogs **need** bounds (Forms) → `GumDialog.LayerFillsCanvas` (virtual) chooses: `.gusx` = `true`, code = `false` |
| **`Parent` is mandatory on EVERY `.gusx` element** | Without `<Variable Type="string" Name="X.Parent">`, the element becomes a child of the **screen root** and is drawn **after** `DialogLayer` — that is, **on top of everything** (the frame covering the controls). Symptom: *"the components do not appear"*. When generating by script: emit each node's `Parent` **in node order** (the order defines the drawing) |
| Category state that changes **size** | If the state sets `Algo.Width = <px>` and the inherited `Algo.WidthUnits` is **`RelativeToParent`**, the result is `parent + px` (in a 284 slot → **568**, double). In a state, change size by **delta** (`0`/`+4`/`−4`) — or declare the units in the state itself |
| Component attached to a dialog layer | The component root needs a **center anchor** (`X=0` + `XOrigin=Center` + `XUnits=PixelsFromCenterX`, same for Y) — without that it sits at the layer's corner and the content comes out shifted by `(±width/2, ±height/2)` |
| Choosing a component's layout variant from C# | `visual.ApplyStateRecursive("Layout", "YesNo")` applies the category state to the visual **and its children** — that is how `Controls/GameMessage` serves Error/YesNo/QuitGame with **one** block in Gum (positions stay in the file, not in code) |
| Forms `Button`/`RadioButton` have no `Visible` | `Visible` belongs to the **visual**: `controle.Visual.Visible = false` |
| Forms `Button` has no `MouseOver`/`MouseOut` | Hover arrives through the **visual**: `button.Visual.RollOn` / `RollOff` (it is the same source `ButtonBase` uses for the `Highlighted` state) |
| **Button component without `<Behaviors>`** | A `.gucx` with art but **without** `ButtonBehavior`/`RadioButtonBehavior`/… **does not create a `FormsControl`**: `GumFormsTheme.FindButton` returns `null` (and logs "nao encontrado na tela") and the button **never becomes clickable**. `gumcli check` **does not** report it — only the log and the click reveal it. Always check with `Select-String BehaviorName` |
| **`FontSize` is `int`** (never `xsd:float`) | Emitting `Algo.FontSize` as float compiles and passes `gumcli check`, but **crashes at runtime**: `InvalidCastException: Error trying to set FontSize to 23 on TextInstance → Unable to cast Single to Int32`. When generating `.gusx`/`.gucx` by script, write `FontSize` with `xsd:int` |
| `CheckBox.IsChecked` is `bool?` | Three states: compare (`IsChecked == true`) instead of converting directly |
| **Forms `RadioButton`/`CheckBox`: `Text` writes to the `TextInstance`** | For the label to be **inside** the component, the `TextInstance` must be an instance with `Text` **exposed** (`ExposedAsName="Text"`) — the behavior writes through that name. Without it `.Text` silently vanishes and the label "disappears" from the row. The label's offset/size/font are **per instance** (`rbCasual.TextInstance.X` etc.), otherwise every row inherits the first one's layout |
| **Forms `Slider`**: the `ThumbInstance` must be an **`InteractiveGue`** | A `Sprite` is **rejected** with an exception (*"contains a visual ThumbInstance which is not an InteractiveGue"*) → make the handle a **Container with the art inside**. `RangeBase` contract: `TrackInstance` = root width **minus** the handle (`RelativeToParent`), the handle with `X` in **%** of the `TrackInstance` (`Percentage`) + `XOrigin=Center` |
| **Forms `Slider`**: range **0..100** | `RangeBase`/`Slider` **overwrite** `Minimum`/`Maximum`/`Value` in `ReactToVisualChanged` → for a 0..1 volume, assign `Minimum=0`/`Maximum=1` **after** instantiating the visual |
| **Forms `Slider`**: drawing order | The track art (background) must be a child of the **root** and declared **before** `TrackInstance` — otherwise it covers the fill/handle (which live inside `TrackInstance`) |
| `gumcli screenshot` with `Slider` | **It does not render**: without a Forms cursor `RangeBase` crashes (`NullReferenceException` in the `Value` setter). Validate the slider **only in the game** |

---

## MANDATORY RULE: native resolution and resolution independence

> **ALWAYS** render at **native resolution**. The coordinate space remains the
> **256x212 design** (the MSX2 screen) and the mapping is done by **camera zoom**, never by
> a reduced RenderTarget.

### The model (do not change it without reading this)

| Aspect | Value |
| :--- | :--- |
| Resolution policy | `SceneResolutionPolicy.None` — RT = backbuffer, 1:1 blit |
| Coordinate space | design units — fixed height **212** (192 game + 20 HUD), dynamic width |
| Mapping | `zoom = BackBufferHeight / 192f` |
| Gum | `Renderer.Camera.Zoom = zoom` and `CanvasWidth/Height = BackBuffer / zoom` |

`FixedHeight` renders everything at 212 and **stretches** it afterwards — that is what blurs the image. Do not go back to it.

### Display size never comes from the texture

Two separate things:

| Concept | Where it lives | Example |
| :--- | :--- | :--- |
| **Design size** ("how much space it takes on screen") | the art's pixels at **1x** | 74x74, 171x135, 65x65... |
| **Texture resolution** ("how many pixels it has") | art file / atlas | 74x74, 148x148 |

> ⚠️ **A sprite's design size IS its size at 1x.** Therefore the correction factor
> is **uniform per atlas**: `BaseScale = 1 / ArtScale` (1x art → `1`; 2x art → `0.5`).
>
> **NEVER** compute "design size per sprite" (e.g.: `Grid.TabWidth / sprite.SourceRect.Width`).
> Board sprites have **very different** natural sizes (74x74, 171x135, 150x118,
> 65x65, 51x51...), so that calculation would **stretch** each one to 74x74 and break the whole layout.

```csharp
// ✅ CORRETO — um fator por atlas, exposto pelo catálogo
//    GameAssets.ArtScale  (1 hoje; 2 quando a arte for 2x)
float baseScale = BoardAssets.BaseScale;   // = 1 / ArtScale
entity.Transform.SetScale(new Vector2(baseScale));

// ❌ ERRADO — confiar no tamanho natural da textura (quebra com arte 2x)
entity.AddComponent(new SpriteRenderer(sprite));   // desenha no tamanho do PNG

// ❌ ERRADO — derivar "tamanho de design" por sprite (estica sprites menores/maiores)
var errado = new Vector2(Grid.TabWidth / sprite.SourceRect.Width, ...);
```

The `GameAssets` and `GameAssets` catalogs already expose `ArtScale` / `BaseScale`.

### Two pitfalls when applying the base scale

1. **Hierarchy inheritance.** In Nez, the parent's scale multiplies the children's. Tokens are
   **children of the tiles** — scaling the tile scales the token with it. Worse: tokens are
   **detached and reparented** during the animations, so the scale would be lost or doubled.
   The correct solution is for the tile to have its **own child entity for the sprite** (a position
   anchor with scale 1) and each visual to carry its own base scale.
2. **Absolute tweens.** `TweenScaleTo(Vector2.One)`, `1.35f`, `1.50f`, `1.08/0.92`, `0.78f`
   are **absolute**: with a base scale ≠ 1 they reset the piece to the wrong size.
   Every scale tween must be **relative** (`baseScale * 1.35f`).

> **Current status (implemented and validated):** the base scale is applied across the whole board —
> the tile has its **own child entity for the sprite** (`tile_visual_*`) and the scale tweens
> are relative. With 1x art, `BaseScale = 1` and nothing changes visually.

### How to test 2x (procedure already executed successfully)

```bash
powershell -File tools/make_atlas_2x.ps1     # gera tokens@2x / board@2x a partir dos 1x
dotnet build KingsValley.slnx                 # o Content é copiado para o output no build

# 1x
$env:KINGSVALLEY_ART_SCALE = "1"; $env:KINGSVALLEY_SCREENSHOT = "C:\temp\a1.png"
dotnet run --project src/KingsValley.Desktop

# 2x (mesma janela/resolução)
$env:KINGSVALLEY_ART_SCALE = "2"; $env:KINGSVALLEY_SCREENSHOT = "C:\temp\a2.png"
dotnet run --project src/KingsValley.Desktop
```

**Expected result:** the two images must be **practically identical** — same position and
same size for everything, varying only in the texture resampling. The last run measured an average
difference of **0.4%** and no sample above 150/765.

> ⚠️ The `*@2x.*` files are test artifacts (they are in `.gitignore`) and must be in the
> **output** — run `dotnet build` after generating them, otherwise the copy goes stale.

### Do not use `SceneRenderTargetSize` for positioning

With `SceneResolutionPolicy.None` it returns the **native** size, not the design one. Use the
`DesignViewport` helper (`DesignWidth` / `DesignHeight` / `Scale` / `VirtualToScreen()`).

### API details already validated (do not waste time rediscovering them)

| Item | Detail |
| :--- | :--- |
| `SceneResolutionPolicy.None` | RT = backbuffer, 1:1 blit (`Scene.cs:587-592`). The RT **is not** reduced. |
| `Camera.Zoom` | It is **normalized (-1..1)**, mapped between `MinimumZoom`/`MaximumZoom`. |
| `Camera.RawZoom` | The **real** value used in the scale matrix. **Use this one** for the design zoom. |
| `Camera.Origin` | Set to `RT/2` on every size change → the position must compensate (see `DesignViewportCamera`). |
| World zoom | `scene.Camera.RawZoom = DesignViewport.Scale` |
| Gum zoom | `GumUI.Renderer.Camera.Zoom = DesignViewport.Scale` |
| Gum canvas | `GumUI.CanvasWidth/Height` in **design units** (`DesignHeight` = `212`) |
| Drawing order | `GumUI.Draw()` **after** `base.Draw(gameTime)` |
| `Scene.RequestScreenshot` | Captures the scene **before** `GumUI.Draw()` — it **does not** include the Gum UI. Use `BackBufferScreenshot`. |

### Full screen (borderless window) — 4 pitfalls paid for

Switching to full screen is **not** `IsFullScreen = true` + `ApplyChanges()`. The project's model is a
**borderless window** (`HardwareModeSwitch = false`), which **does not change the Windows resolution** — but it
requires 4 precautions, in order (`KingsValleyGame.SetFullScreen`):

| # | Pitfall | Reality |
| :--- | :--- | :--- |
| 1 | `HardwareModeSwitch = true` (default) | It is the classic **exclusive** mode: it changes the Windows video mode. Use `false` (borderless window). |
| 2 | Applying size and `IsFullScreen` at once | When **leaving**, it is mandatory to set `IsFullScreen = false` + `ApplyChanges()` **first** and only **then** reapply `PreferredBackBuffer` + `ApplyChanges()`. Outside that order the real back buffer does not come back. |
| 3 | Trusting `PreferredBackBuffer` | What rules is **`PresentationParameters`** (the real back buffer) **and** the **`GraphicsDevice.Viewport`** — they can diverge; reassert the viewport at the back buffer size. |
| 4 | Expecting the scene to adjust itself | Nez only recreates the `SceneRenderTarget`/resolution when it receives **`CoreEvents.GraphicsDeviceReset`** — which borderless mode **does not fire**. Without `Core.Emitter.Emit(CoreEvents.GraphicsDeviceReset)`, the Gum UI is correct and **only the scenery** renders at the old size (a rectangle in the corner, the rest black). |

> **Symptom → cause:** "everything shifted/stretched" = 2 or 3 (back buffer/design). **"UI correct and
> scenery only in a corner"** = 4 (the `GraphicsDeviceReset`). The video log prints
> `janela | backbuffer real | viewport | design | zoom` — compare the 5; the first one that disagrees is the cause.

### Diagnostics and fidelity gate (already implemented)

```bash
# Captura a tela final (com Gum) e encerra — usado como gate de fidelidade
$env:KINGSVALLEY_SCREENSHOT = "C:\temp\gate.png"
$env:KINGSVALLEY_SCREENSHOT_FRAME = "45"      # opcional
$env:KINGSVALLEY_WINDOW = "1920x1080"         # opcional: testa em outra resolução
dotnet run --project src/KingsValley.Desktop
```

Expected `zoom` values (the Gum overlay shows them on screen):

| Window | `zoom` | Design width |
| :--- | :--- | :--- |
| 256x212 | **1.000** | 256 |
| 1920x1080 | **1.406** | 1365 |
| 2560x1440 | **1.875** | 1365 |

### Fidelity gate

With the window at **256x212** the `zoom` is `1`: the image must be **identical** to the previous one.
Validate screenshots at **256x212, 1024x848, 1920x1080 and 2560x1440**.

### Multi-resolution atlas: use the libGDX `scale:` convention

**DO NOT** create your own "design size" tables/manifests. The `.atlas` format we use
derives from **libGDX**, which already solves this with the **`scale:`** field:

```
tokens@2x.png
...
tk_corn
  rotate: false
  xy: 152, 304
  size: 148, 148
  orig: 148, 148
  offset: 0, 0
```

```
designSize = sourceRect.Size / scale
baseScale  = designSize / sourceRect.Size = 1 / scale
```

1x art → `baseScale = 1`; 2x art → `baseScale = 0.5`. **No code changes when raising the resolution.**

When generating atlases (libGDX TexturePacker, `scale: [1,2,4]`, `scaleSuffix: ["","@2x","@4x"]`):

> ⚠️ **`Allow rotation` = OFF** and **trim = OFF**. The Nez loader
> (`lib/Nez/Nez.Portable/Assets/SpriteAtlases/Loader/SpriteAtlasLoader.cs`) **ignores** rotation,
> `orig` and `offset` — enabling either one makes sprites come out crooked or shifted.

**Guardrail:** every atlas set (1x/2x/4x) must have **the same sprite names**.
If a sprite exists in 2x and is missing in 1x (or vice versa), it is a packing bug.

### RULE: an atlas needs `padding` **with extrusion** (never transparent padding)

> **ALWAYS** pack with **padding + extrusion** (duplicating the edge pixels).
> **NEVER** leave the padding transparent.

**Why:** with linear filtering (`LinearClamp`; the project now uses `PointClamp` for MSX2 pixel art, but the rule holds for anyone going back to linear) the sampler reads up to half a texel
**outside** the sprite rectangle. If there is transparency there, the edge "fades" — and when two
sprites sit side by side (e.g.: board tiles) a dark **seam** appears. Padding alone
**does not** solve it: it only swaps "neighbor bleeding" for a "transparent fringe".

**How it is solved here:** `pack_atlases.ps1` uses `-pad:2` and the vendored packer
(`lib/Nez/Nez.SpriteAtlasPacker/.../ImagePacker.cs`) does the **extrusion in a second pass**,
bleeding **half the padding** to each side (the gap is shared — bleeding the whole padding
would make the last sprite overwrite its neighbor's edge).

**If you swap the packer** (e.g.: libGDX TexturePacker), ensure at minimum:

| Requirement | Reason |
| :--- | :--- |
| `padding` ≥ 2 | separates the sprites |
| **extrusion / bleed** enabled (e.g.: `duplicatePadding`) | fills the gap with the edge color |
| `Allow rotation` = **OFF** | the Nez loader ignores rotation |
| trim = **OFF** | the Nez loader ignores `orig`/`offset` |

**How to validate:** sample the padding pixels around a sprite in the `.atlas` — the alpha must be
**255** (not 0). E.g.: for the rect `304,734,74,74`, the pixels `303`, `378`, `733` and `808` must have
`A=255`.

> ⚠️ **The `A=255` rule applies to OPAQUE sprites** (board tiles, which fill the whole
> rectangle). In a sprite **with transparency** (character, logo), the padding inherits the edge's alpha —
> that is, it ends up with **alpha 0 and the edge's COLOR**. That is correct: with the premultiplied pipeline,
> alpha 0 zeroes the RGB and the edge interpolates without a fringe. **Do not** force alpha 255 in those cases (it would give
> a solid frame around the sprite rectangle).

---

## MANDATORY RULE: all sprite art comes from an ATLAS (never a loose PNG at runtime)

> **ALWAYS** pack the art into an `.atlas` and load it through the atlas.
> **NEVER** load a loose sprite PNG with `Texture2D.FromStream` to draw.

**Why:**

| Reason | Detail |
| :--- | :--- |
| **Correct premultiplication** | The atlas loader already premultiplies (`premultiplyAlpha: true`). A loose PNG **does not** — and with `BlendState.AlphaBlend` (Nez scene and `PostUiOverlay`) that leaves a **light halo** on every semi-transparent edge |
| **Extrusion** | The packer guarantees padding with bleed; loose sprites get no such treatment when placed next to each other |
| **Perf** | One texture instead of N (fewer texture/batch switches) |
| **Multi-resolution** | `ArtScale` (1x/2x/4x) is resolved by `ResolveAtlasPath`, for free |

**How to do it:**

1. (Inherited from HopesFarm — `tools/AssetsSource` and `pack_atlases.ps1` were removed; the game art comes from the ROM via `tools/kv2-extract`.) Put the PNGs in an input folder (or copy them from the
   Unity folders, as `pack_atlases.ps1` does for `logo`).
2. Add the section to `tools/pack_atlases.ps1` (`-pad:2` and `-dontCreateAnimations`).
3. Load it with `SpriteAtlasLoader.ParseSpriteAtlas(ArtResolution.ResolveAtlasPath(...), premultiplyAlpha: true)`
   — in the game, through `GameAssets` (global scope, more than one scene) or through `SceneBase` (scene scope).
4. The **sprite name in the atlas is the file name without the extension**; reference it by that name.

**Exceptions:** a texture **without alpha** (background JPG) or an atlas with **a single sprite** do not
benefit — but if it is a PNG with alpha drawn by the scene/overlay, it **needs**
`SceneBase.LoadTexturePremultiplied` (see the blend states table in `SceneBase`).

**Implemented reference:** `logo.atlas` (16 sprites from `logo_frame`) consumed by `SpriteRig`
via `SpriteRigImage` (texture + `SourceRect`) — see `LogoFrame.Load`.

---

## MANDATORY RULE: engine-agnostic code goes to the open-source package

> All **engine-agnostic** code (not dependent on Nez or Gum) must be **created in the
> `Wagenheimer.MonoGameHelper` package** (`K:\Games\Open Source\MonoGameHelper`), **documented in the
> package README**, and consumed by the project as a dependency.
>
> Only **glue specific** to Nez/Gum/game rules stays in the project (`src/KingsValley.Engine`).

| What | Where | Examples |
| :--- | :--- | :--- |
| Agnostic (pure MonoGame) | **`Wagenheimer.MonoGameHelper`** | `DesignViewport`, `ArtScale`, `PostUiOverlay`, `SpriteBatchDrawing`, `BackBufferScreenshot`, `SafeJsonStorage`, `AudioManager`, `InputHelperManager` |
| Nez-specific | `src/KingsValley.Engine/Graphics/`, `.../Components/` | `DesignViewportCamera`, `VirtualCursorViewComponent` |
| Gum-specific | `src/KingsValley.Engine/UI/Gum/` | screens, dialogs |
| Business rule | `src/KingsValley.Core` (pure C#) | match, grid, profiles, save |

**When creating something new, ask yourself first:** *"would this be useful in another MonoGame project, without Nez and
without Gum?"* If yes, the place is the package — with XML docs and a section in its README.

Package helpers already used by this project:

| Helper | Namespace | Role |
| :--- | :--- | :--- |
| `DesignViewport` | `Wagenheimer.MonoGameHelper.Layout` | design space ↔ native pixels |
| `ArtScale` / `AtlasScaleReader` | `Wagenheimer.MonoGameHelper.Graphics` | multi-resolution art (1x/2x/4x) |
| `PostUiOverlay` / `IPostUiDrawable` / `PostUiLayers` | `Wagenheimer.MonoGameHelper.UI` | layer above the UI |
| `SpriteBatchDrawing` | `Wagenheimer.MonoGameHelper.Graphics` | `Pixel`, `FillRectangle`, `DrawRectangleOutline` |
| `BackBufferScreenshot` / `ScreenshotRequest` | `Wagenheimer.MonoGameHelper.Graphics` | fidelity gate / CI |

---

## MANDATORY RULE: audio comes from VGM + a chip emulator (never from a recording)

> **The sound source is the VGM** (the exact log of PSG/SCC register writes), played by the
> chip emulator. **Never** use a WAV/OGG recorded from the emulator as the source — it has loop
> clicks, codec artifacts and an inconsistent level.

| Piece | Where | Role |
| :--- | :--- | :--- |
| **`Wagenheimer.MsxAudio`** | package (`K:\Games\Open Source\MsxAudio`) | PSG AY-3-8910 + SCC, VGM, loop, render, `.msxaudio` package, streaming engine, sample player and ROM extraction via openMSX |
| **`Wagenheimer.MsxAudio.MonoGame`** | package | `MsxAudioPlayer` (DynamicSoundEffectInstance) |
| `Content/Rom/Audio/audio.msxaudio` | generated content | 11 songs + 46 effects (~1 MB, compressed VGMs) |
| `Content/Rom/Audio/audio.json` | build input | id of each sound in the ROM + "loops"; read by `MsxAudioTool import --manifest` |
| `src/KingsValley.Engine/Assets/GameAudio.cs` | game | wires Core events → `PlaySong`/`PlaySample`; `GameAudio.Update()` every frame |

**Pitfalls already paid for (do not repeat):**

| Pitfall | Reality |
| :--- | :--- |
| **Detecting a loop in an effect** | `jump` "repeats writes" and the detector found `0.384 s + 5.807 s` → the render cut the sound short. Use `--clips` (render up to the last audible instant, no loop) for SFX |
| **Long music with a short capture** | the `stage_e` loop (id 9) only appears with **300 s** of recording; with 120 s the detector saw a partial stretch (28.8 s) |
| **Comparing audio sample by sample with openMSX** | `record` resamples and delays (~8 ms, variable): use **LTAS** (correlation 0.998) and **envelope** (0.95), not sample correlation |
| **AY masks** | on the chip, bit **1 = channel off** (`tmask/nmask`); inverting that turns the tone into noise |
| **SCC ports in the VGM** | `0xD2 pp aa dd` with `pp` = 0 wave (0x00-0x7F), 1 frequency (0x00-0x09), 2 volume (0x00-0x04), 3 key-on, 4 channel 5 wave, 5 test. The VGM's SCC uses **clock 1,789,772.5 Hz** (`f = clock/(16×(period+1))`) |
| **SCC's `0x8F`** | it is **key on/off** (5 bits), not "deformation" |
| **libsndfile's Vorbis** | blows the stack (`0xC00000FD`) with a large block: write in blocks of **8192** samples |
| **`MsxAudioPlayer`** | needs `Update()` per frame (feeds the `DynamicSoundEffectInstance`) |

Regenerate the content (the VGMs live in `Content/Rom/Audio/Vgm/{Music,Sfx}`):

```bash
dotnet run --project "K:/Games/Open Source/MsxAudio/tools/MsxAudioTool" -- \
    import src/KingsValley.Desktop/Content/Rom/Audio/Vgm \
           src/KingsValley.Desktop/Content/Rom/Audio/audio.msxaudio \
    --manifest src/KingsValley.Desktop/Content/Rom/Audio/audio.json --title "King's Valley II"
```

Profiles: `KINGSVALLEY_AUDIO_PROFILE=original|stereo|remaster`. Audio comes **only** from the package through the
chips (there is no longer an OGG/`AudioManager` path).

---

## Scenes, loading and asset lifecycle

### Structure

```
src/KingsValley.Engine/Scenes/
  SceneBase.cs     # base de TODAS as cenas (resolução, câmera de design, overlay, Own/Unload)
  SplashScene.cs   # logo com fade in/out (qualquer input acelera 4x)
  MainMenuScene.cs # menu (placeholder; Fase 2 traz o .gumx)
  LoadingScene.cs  # barra + passos fatiados por tempo
  LevelScene.cs    # tabuleiro de Match-3 (era BootstrapScene)
src/KingsValley.Engine/Navigation/
  GameNavigation.cs # GameSceneId { Splash, MainMenu, Level, Farm } + GoTo / GoToLoading
src/KingsValley.Engine/Assets/
  GameAssets.cs    # cache global (atlases + efeitos) com passos de preload
  ArtResolution.cs # 1x/2x/4x por env var
```

> **Every scene switch goes through `GameNavigation`.** Never do `Core.Scene = new X()` scattered around.

### ⚠️ CRITICAL PITFALL: `Initialize` runs in the base constructor

Nez calls `Scene.Initialize()` **inside the base constructor**, that is, **before** the body of the
child scene's constructor. Consequence: the scene's fields and parameters **are not assigned yet**.

That is why `SceneBase` splits it into two moments:

| Moment | Method | What goes here |
| :--- | :--- | :--- |
| In the base constructor | `Initialize()` (sealed) | resolution, sampler, design camera, overlay cleanup |
| When the scene starts | `OnStart()` → `OnInitialize()` | **content**: entities, assets, reading constructor parameters |

```csharp
// ✅ CORRETO — conteúdo em OnInitialize (chamado por OnStart)
public class MinhaScene : SceneBase
{
    private readonly int _nivel;                       // atribuído pelo construtor...
    public MinhaScene(int nivel) { _nivel = nivel; }   // ...antes de OnStart rodar

    protected override void OnInitialize() { /* usa _nivel com segurança */ }
}

// ❌ ERRADO — ler parâmetros do construtor dentro de Initialize()
public override void Initialize() { UseParametro(_nivel); } // _nivel ainda é default!
```

It was exactly this bug that made `LoadingScene` log `0 passo(s) para Splash` (target = `Splash`
= default value `0`). If you create a new scene, **do not repeat it**.

### Asset lifecycle

| Scope | Where | Release |
| :--- | :--- | :--- |
| **Global** (atlases, particles) | `GameAssets` | `GameAssets.Unload()` on shutdown |
| **Scene** (background, UI) | `SceneBase.Own(...)` / `LoadTexture(...)` | automatic in the scene's `Unload()` |

> ⚠️ `Texture2D.FromStream` is **not** tracked by any ContentManager. Always go through
> `SceneBase.LoadTexture`/`Own`, otherwise it leaks VRAM on every scene change.
>
> `SpriteAtlas` implements `IDisposable` **explicitly** → it needs
> `((IDisposable)atlas).Dispose()`.

### Loading without freezing the window

`GraphicsDevice` **is not thread-safe**: creating a texture has to happen on the main thread, so
`Task.Run` does not help. The pattern is **slicing the work by time**:

```csharp
GameNavigation.GoToLoading(GameSceneId.Level, GameAssets.CreatePreloadSteps());
```

`LoadingScene` runs the `LoadStep`s within a budget of **6 ms per frame**, so the bar
animates and the window responds. Progress = 70% steps + 30% minimum duration (avoids a "blinking" bar).

**When adding heavy work to a scene**, expose it as a `LoadStep` and pass it to `GoToLoading`,
instead of loading everything in the constructor.

### Diagnostic environment variables

| Variable | Effect |
| :--- | :--- |
| `KINGSVALLEY_START_SCENE` | `splash` (default) \| `menu` \| `loading` \| `level` — enters a scene directly |
| `KINGSVALLEY_FAST_BOOT` | `1` = almost instant splash |
| `KINGSVALLEY_SCREENSHOT` + `_FRAME` | captures the final screen and exits (fidelity gate/CI) |
| `KINGSVALLEY_WINDOW` | e.g.: `1920x1080` — tests at another resolution |
| `KINGSVALLEY_ART_SCALE` | `1` (default) \| `2` \| `4` — multi-resolution art |

---

## Gum AI tools (skills, MCP and `gumcli`)

> 🚨 **MANDATORY RULE: before deciding anything about Gum — layout, units, events,
> Forms, XML — **consult the documentation**. Not from memory.**
>
> The `gum-docs` MCP is configured in `opencode.json`. Use the `searchDocumentation`,
> `getPage` and `askQuestion` tools. Without MCP, any page answers over HTTP:
> `https://docs.flatredball.com/gum/<pagina>.md?ask=<pergunta>`. Index: `sitemap.md`.
>
> The `.claude/skills/gum-*` skills load on their own (durable context) and bring the XML
> enum tables — consulting them is exactly what avoids the magic numbers that broke things here the most.

Gum ships **three aids for agents**, and all three are already in use in this repo:

| Piece | What it is | Where |
| :--- | :--- | :--- |
| **Skills** | durable context that loads on its own when the subject matches | `.claude/skills/` — `gum-overview`, `gumcli`, `gum-file-format`, `gum-forms-controls`, `gum-layout`, `monogame-input` |
| **Documentation MCP** | authoritative, **up-to-date** query of the Gum docs | endpoint `https://docs.flatredball.com/gum/~gitbook/mcp`, configured in `opencode.json`; usage doc: <https://docs.flatredball.com/gum/using-gum-with-ai/ai/mcp-server> |
| **`gumcli`** | act on the project and **verify** the result | `dotnet gumcli …` (the repo's local tool) |

> **The MCP works without installing anything**: besides the tool, **any doc page accepts a question over
> HTTP** — `https://docs.flatredball.com/gum/<pagina>.md?ask=<pergunta>&goal=<objetivo>`, and the complete
> index is at `https://docs.flatredball.com/gum/sitemap.md` (the whole body, in
> `llms-full.txt`). It is the fallback path when the MCP is not available.

> The skills come from the Gum repository's **`gum-skills/`** folder and are *3rd-party* (using Gum **from
> inside** a game). Do not confuse them with their repo's **internal** `.claude/skills/`, which describes
> how to work **on** Gum.

**How to (re)install/update a skill** — download to `.claude/skills/<nome>/SKILL.md`:

```bash
curl -sL https://raw.githubusercontent.com/vchelaru/Gum/main/gum-skills/<nome>/SKILL.md \
     -o .claude/skills/<nome>/SKILL.md
```

Doc-recommended set: `gum-overview` (start with it) + `gumcli` + whichever the work touches
among `gum-layout`, `gum-forms-controls` and `gum-file-format`.

### The UI work cycle (author → validate → see)

```
0. dotnet gumcli fonts      <GumProject.gumx>       # 0. cache de bitmap das fontes (1x, ou ao mudar fonte)
1. escrever/editar o .gusx (ou o C#)
2. dotnet gumcli check      <GumProject.gumx>       # 2. valida o XML (pega enum/instância errada)
3. dotnet gumcli screenshot <GumProject.gumx> <Tela># 3. PNG da tela — o agente OLHA o resultado
4. comparar com a referência do jogo real           # docs/progress/shots/ui-dialog-*.png
```

> ⚠️ `screenshot` **does not** have `KernSmith` (the game's dynamic font): without **step 0** all text
> comes out in **Gum's default font**. See the "Fonts" section.

⚠️ **Generating `.gusx` by script** (when there are many instances) is acceptable — as long as the script emits
the XML following the `gum-file-format` skill and `gumcli check` closes at **0** afterwards. Watch out for
**PowerShell**: variables are **case-insensitive**, so `$n` and `$N` are the **same** variable — a
`foreach ($n in $N)` destroys the array (it is what produced "10 instances" instead of 85).

Also available: `gumcli svg`, `gumcli codegen`, `gumcli import-screen` and
`gumcli diff-screenshots`.

### ⚠️ Rule change: hand-editing the XML **is now allowed** (with validation)

The **`gum-file-format`** skill — from the Gum team itself — documents *"reading and **safely
hand-editing** `.gumx`/`.gusx`/`.gucx`/`.gutx` XML"*. This **relaxes** the previous rule ("never
write by hand"):

| Before | Now |
| :--- | :--- |
| **NEVER** write `.gumx`/`.gusx`/`.gucx` by hand — only `gumcli`/Gum Tool | You **may** edit by hand **following the `gum-file-format` skill**, and **always** run `gumcli check` afterwards |

Why validation is mandatory: the schema uses **numeric enums** (`WidthUnits`, `XOrigin`,
`ChildrenLayout`) and **qualified variables** (`InstanceName.Property`) that **break silently**
when written wrong. Creating a screen **from scratch** still favors **HtmlToGum / Gum Tool**; editing
an existing screen can be done by hand with `check` at the end.

> **Opening the screens in the Gum Tool:** the migration to `.gusx` **has already started** — `OptionsDialog` is the pilot
> (UI in the `.gusx`, logic in C# via `GumScreens`), the other dialogs are still assembled in **code**.
> For the Tool, open `GumProject.gumx` and choose the screen; screens still in code **do not** appear there
> (only the `Styles.gucx` theme and the default controls).

## Pitfalls of HUD, fade and diagnostic auditing

| Pitfall | Reality |
| :--- | :--- |
| **Unity anchor ≠ center offset** | A prefab child has `m_AnchorMin/Max` **and** `m_AnchoredPosition`: the position is relative **to the anchor** (parent corner/center), not to the parent's center. Treating `m_AnchoredPosition` as a "center offset" shifts everything — it was the bug with the pill labels/values. Convert: `ref = (anchor − 0.5) × tamanhoDoPai`; `centro = centroDoPai + ref + anchoredPosition`; and flip **Y** (Unity Y-up → Gum Y-down) |
| **Sprite fade in the scene is PREMULTIPLIED** | The scene (Batcher) and `PostUiOverlay` use premultiplied `BlendState.AlphaBlend`: scaling **only the alpha** leaves the sprite "glowing" (full RGB, low alpha). Scale **RGB along with it**: `new Color(r*a, g*a, b*a, a)`. That was the white halo in the board fade |
| **Effect that crosses scene→UI** | Something born on the board and ending over a Gum card (e.g.: goal collection sprite) **cannot** be a `SpriteRenderer`: it goes to **`PostUiOverlay`** (`IPostUiDrawable`), the only one that draws above the scene **and** the HUD |
| **Diagnostics without a console** | The project is a `WinExe`: opening the `.exe` directly **does not** open a console and every `Console.WriteLine` is lost. The output is mirrored to **`hopesfarm2.log`** next to the executable (see `Program.cs`); through `dotnet run` it also appears in the terminal |
| **Scene screenshot ≠ final screen** | `Scene.RequestScreenshot` captures **before** `GumUI.Draw` (no UI). Use `BackBufferScreenshot` (`KINGSVALLEY_SCREENSHOT`) for the final screen |
| **Visual reference of the real game** | Before "adjusting by eye", compare with `docs/reference/*.jpg` (captures of the real game). Every screen/state has one: HUD, level start, power-ups, dialogs. **Ask/request a capture** instead of guessing |

## Other project rules

- **English only**: code, comments, log/console messages, UI text, docs and commit messages are
  **always** in English. (The earlier rule asked for Portuguese; it no longer applies.)
- `KingsValley.Core` is pure C#: **zero** graphical dependencies (no Nez/MonoGame).
  Business rules (match, grid, goals) live in Core; the Engine only animates/renders.
- Never import `UnityEngine`; use `Microsoft.Xna.Framework`.
- Consult the original Unity project at `E:\GreenSauceGames\KingsValley` as the source of truth.

## Documentation

- [docs/AI_MIGRATION_SPEC.md](docs/AI_MIGRATION_SPEC.md) — migration and architecture guide.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — architecture and Unity -> Nez mapping.
- [docs/ART_SETS.md](docs/ART_SETS.md) — art sets (MSX2 / MSX1 / **remaster**: a single folder is enough).
- [src/KingsValley.Engine/RenderLayers.cs](src/KingsValley.Engine/RenderLayers.cs) — render layers.
