# Extraction handoff — free build (review document)

> **Purpose of this document:** let another AI (or a new session) review the extraction work, reproduce
> it from scratch, and continue from a precise state. Everything below is in English per the project
> rule. Measurements are real (taken during the work), not estimates.

---

## 1. What we are building

A **free, public build** of King's Valley II (MonoGame + Nez + Gum port) that ships **zero Konami
content**. On first run the game asks for the player's own cartridge and extracts everything from it:

| Content | Source | Output |
| :--- | :--- | :--- |
| 60 stages (tables) | ROM tables | `Levels/stageNN.json` |
| Stage dynamic state (stones, coffins, door, mechanisms, spawn) | ROM code (`0x5AAB` loader) | same JSON |
| Audio (11 songs + 53 effects) | ROM (VGM register log → chip emulator) | `audio.msxaudio` |
| Fixed screens (title JP/EN, pyramid) | ROM draws them into the VDP | `Screens/*.bin` |
| MSX1 gameplay rooms | cartridge loader `0x5AAB` + VDP snapshot | `Rooms/dumps/sNN_rMM.bin` |
| Sprite atlas (player poses + fallbacks) | ROM sprite layer + generated shapes | `Art/kv2.png` + `Art/kv2.atlas` |
| Report (gallery) | all of the above | `preview/index.html` |

The cartridge alone cannot reproduce the original MSX2 sprite art (enemies, items, doors, HUD). To keep the
game visually faithful when only the MSX1 ROM is extracted, the release ships the original MSX2-derived art
under `src/KingsValley.Desktop/Content/Rom*`; a generated atlas is marked with `Art/kv2.generated` and
`RomContent.AtlasPath` prefers the bundled original whenever both exist. Stages, rooms, fixed screens and
audio still come from the player's cartridge.

---

## 2. Where things run

```
ROM bytes ──► MiniMsx (in-process MSX1 machine, package Wagenheimer.MsxAudio)
                 │  boot, run frames, read VRAM/RAM
                 ├─► RomLevels      (tables)          ─┐
                 ├─► RomStageState  (RAM after loader) ├─► RomStageJson ─► Levels/*.json
                 ├─► RomScreens     (VDP screens)     ─► Screens/*.bin
                 ├─► RomRoomDumps   (loader + VDP)     ─► Rooms/dumps/sNN_rMM.bin
                 ├─► RomSpriteAtlas (poses + fallback) ─► Art/kv2.png/.atlas
                 └─► DirectRomExtractor (audio)       ─► audio.msxaudio
                                                        └─► ExtractionPreview ─► preview/index.html
```

Output folder: `%APPDATA%\KingsValley\extracted\`.

---

## 3. Status (measured)

| Piece | Status | Evidence |
| :--- | :--- | :--- |
| Level tables | ✅ 60/60 | `RomLevelsTests` compares every cell with the shipped JSON |
| Stage state | ✅ 60/60 | `RomStageState` (call `0x5AAB`, 0.4 s/stage) |
| Full level JSON | ✅ 60/60 | parses to the same `StageDefinition`, 19.3 s for all 60 |
| Audio | ✅ 11 + 53 | full profile extraction; game plays `11 songs, 53 effects` |
| Title (JP) | ✅ byte-identical | equal to the openMSX capture (`title.bin`, SHA1 `DA004EF5…`) |
| Title (EN) | ✅ byte-identical | `title_en.bin`, SHA1 `E871C75B…` |
| Pyramid intro | ✅ byte-identical | `pyramid.bin`, SHA1 `8CDD2B60…` |
| Player poses | ⚠️ extracted, **unverified in game** | 14 poses captured by rendering the sprite layer |
| MSX1 room dumps | ✅ generated | native `sNN_rMM.bin`, 24,614 bytes each, every room of all 60 stages |
| Sprite atlas | ✅ complete | real poses where available + fallback names for every renderer sprite |
| Report/gallery | ✅ | 3 screens, 60 stages, 4 pattern sheets, opens at the end |
| Music player | ✅ install on demand | MSXScope 1.1.0 downloaded (37 MB), SHA-256 checked, launched in 2.4 s |

---

## 4. Build, run and test (complete)

### 4.1 Prerequisites

- .NET 10 SDK, Windows (the player installer supports Windows/Linux/macOS; extraction is cross-platform).
- A cartridge image: `King's Valley 2 (J) # MSX.MX1` (128 KB, Konami SCC). The MSX2 dump
  (`.mx2`, ASCII8) is **not needed** for the current pipeline.

### 4.2 Build

```powershell
cd E:\GreenSauceGames\KingsValley
dotnet build KingsValley.slnx     # must be 0 errors
dotnet test                       # 133 tests, all green
```

### 4.3 Clean extraction from scratch

```powershell
Remove-Item "$env:APPDATA\KingsValley\extracted" -Recurse -Force -ErrorAction SilentlyContinue
$env:KINGSVALLEY_ROM="C:\path\to\kv2_msx1.rom"
dotnet run --project src/KingsValley.Desktop
```

Expected console output (order may vary):

```
[AudioBootstrap] extracting the audio ...
[RomScreens] screen title / title_en / pyramid
[AudioBootstrap] room 1/12 of stage 1 ... room 8/8 of stage 60
[ExtractionPreview] report: 3 screens, 60 stages, 4 pattern sheets
[AudioBootstrap] atlas: 14 real player poses captured
[AudioBootstrap] atlas: 544 sprites (512xN)
[AudioBootstrap] 11 songs, 53 effects
```

The browser opens `preview/index.html` at the end (disable with `KINGSVALLEY_NO_GALLERY=1`).
Audio extraction takes ~5 minutes; everything else ~30 s.

### 4.4 Run a specific state (diagnostics)

```powershell
# straight into stage 3 with the MSX1 art set
$env:KINGSVALLEY_START_SCENE="level"; $env:KINGSVALLEY_LEVEL="3"
$env:KINGSVALLEY_SKIP_LEVELSTART="1"; $env:KINGSVALLEY_GFX="msx1"
dotnet run --project src/KingsValley.Desktop

# screenshot and exit (no window interaction)
$env:KINGSVALLEY_SCREENSHOT="C:\temp\shot.png"; $env:KINGSVALLEY_SCREENSHOT_FRAME="120"
dotnet run --project src/KingsValley.Desktop
```

Keys: **F11** cycles art sets; **ESC** opens the system menu **only during gameplay**; the menu rows
are MUSIC, SOUND, AUDIO, GRAPHICS, LANGUAGE, PLAYER, SCREEN, RESUME, TITLE.

### 4.5 Verify the extracted files

```powershell
Get-ChildItem "$env:APPDATA\KingsValley\extracted" -Recurse -File |
  Select-Object @{n='rel';e={$_.FullName.Replace("$env:APPDATA\KingsValley\extracted\","")}},Length
```

Expected: `Screens` (3 × 24.6 KB), `Levels` (60 JSON), `Rooms/dumps` (~180 × 24.6 KB),
`Art` (`kv2.png` + `kv2.atlas` with 544 sprites), `audio.msxaudio`, `audio.source.json`, `preview/`.

---

## 5. Open problems (the review target)

### 5.1 "Nothing works" when testing the MSX1 atlas — likely causes

1. **The save stores the art set.** `%APPDATA%\GreenSauceGames\King's Valley II\save.json` keeps
   `"GfxRom": "msx1"` after a test run, so the game keeps starting in MSX1 (blue Japanese title) even
   when the tester expects the MSX2 art. Set it back to `"msx2"` or press **F11**.
2. **ESC does nothing on the title/menu** — by design (`SystemMenu.CanOpen()` requires `GameScene`).
3. **The extracted folder can go missing** after manual backup/restore experiments. If `extracted` does
   not exist, the game has no atlas and MSX1 mode draws no sprites.
4. **The extracted atlas is stale.** Old runs left only `vick_s0_00`…`vick_s0_13`, and the old
   `>5000`-byte check could hide a good generated atlas. Delete `extracted/Art/kv2.*` and re-extract:
   `RomSpriteAtlas.Build` now writes every name the renderer asks for and `RomContent.AtlasPath`
   accepts the complete file.
5. **Sprite anchor is an assumption.** Poses are cropped 16×24 with the feet on the box's bottom edge,
   and `MsxScreen.DrawActor` anchors the bottom at `y + 16`. If the cartridge's player Y is the *tile
   top* instead of the feet, the sprite will float or sink by up to 8 px.

### 5.2 Resolved in this pass / what remains

| Item | Status | Notes |
| :--- | :--- | :--- |
| Player pose sets, `vick_x_*`, `vick_d_*` | ✅ names complete | real poses are reused; missing variants get recognisable fallback shapes |
| Enemies (`enemy_t*`) | ✅ names complete | fallback shapes until the SAT entries are identified per type |
| Coffins / doors / items / font | ✅ names complete | generated in the fixed TMS9918 palette |
| MSX1 room coverage | ✅ all stages | `RomRoomDumps` boots once and calls `0x5AAB` per room, so ladder-only rooms are included |
| MSX2 art from the ROM | ⚠️ optional | needs the V9938 (SCREEN 5 + command engine) in the machine; the `.mx2` dump boots but stays in text mode today |

---

## 6. Packaging rules (must not regress)

- Release builds **must not** contain `Content/Rom`, `Content/RomMsx1`, VGMs or `audio.msxaudio`.
- The free build selects the `msx1` art set when `Content/Rom/Art` is absent and the extracted MSX1 data is
  complete (`RomContent.Initialize`, with `GfxVariant.Initialize` still honouring an explicit save/env choice).
- `ArtSets` (see `docs/ART_SETS.md`) resolves art per file with a fallback chain, so a remaster pack can
  replace a single screen.

---

## 7. File map (what to read)

| File | Role |
| :--- | :--- |
| `src/KingsValley.Core/Extraction/RomLevels.cs` | level tables (terrain RLE, ladders, items, room grid) |
| `src/KingsValley.Engine/Extraction/RomStageState.cs` | boots the machine and calls the cartridge's loader |
| `src/KingsValley.Engine/Extraction/RomStageJson.cs` | writes `stageNN.json` |
| `src/KingsValley.Engine/Extraction/RomScreens.cs` | screens by state (title, pyramid), gameplay VRAM snapshot, **player poses** |
| `src/KingsValley.Engine/Extraction/RomRoomDumps.cs` | native SCREEN 2 dumps for every gameplay room |
| `src/KingsValley.Engine/Extraction/RomSpriteAtlas.cs` | complete generated atlas (real poses + fallback names) |
| `src/KingsValley.Engine/Extraction/ExtractionPreview.cs` | report: PNG writer, stage maps, pattern sheets |
| `src/KingsValley.Engine/Assets/AudioBootstrap.cs` | orchestration (audio → levels → screens → rooms → report → atlas) |
| `src/KingsValley.Engine/Assets/ArtSets.cs` / `RomContent.cs` / `GfxVariant.cs` | art sets, paths, preference for extracted art |
| `src/KingsValley.Engine/Assets/MusicPlayer.cs` | MSXScope install-on-demand + launch |
| `K:\Games\Open Source\MsxAudio\src\Wagenheimer.MsxAudio\Extraction\MiniMsx.cs` | the machine (VDP/VRAM, BIOS services, `RamWriteHook`/`RamReadHook`) |
| `K:\Games\Open Source\MsxAudio\src\Wagenheimer.MsxAudio\Extraction\MsxScope.cs` | player installer (per OS/arch, SHA-256) |
| `tools/kv2-extract/art/*.py` | offline helpers (`artkit.py` PNG ⇄ .bin, `patterns.py`, `gallery.py`) |

---

## 8. Machine facts that were expensive to learn (do not regress)

1. **BIOS port bytes.** The cartridge's RLE decompressor does `LD A,(0x0007); LD C,A; OUT (C),A` — it
   reads the VDP data port **out of the BIOS header**. A BIOS filled with `RET` (0xC9) sends every byte
   to port 0 and the screen stays empty. `MiniMsx` must keep `0x0006`/`0x0007` = `0x98`.
2. **RDP register mirror.** Games rebuild the next VDP value from `RG1SAV` (`0xF3E0`); every register
   write (port and `WRTVDP`) must update `RG0SAV..RG7SAV`.
3. **`WRTVRM` (`0x4D`) matters.** A cartridge wrote 2950 bytes through it in 10 s; without it the VRAM
   stayed at 2 KB.
4. **Two title variants.** The cartridge picks Japanese/English from the low nibble of BIOS
   `0x002B`. The port captures both; the in-game LANGUAGE option switches them.
5. **Sprite system (MSX1).** 32 entries of 4 bytes live in a shadow SAT at RAM `0xE800`; the copier to
   the VDP rotates them (`0x40EF`/`0x4105`) to respect "4 sprites per line". The player is **four
   overlapping 16×16 sprites** (patterns 0/4/8/12, colours 7/10/1/1, x = player+2,
   y = player−2/−5/−9/+7) — compositing the pattern tables gives fragments, **rendering the sprite
   layer** gives the character.
6. **Atlas format** is the project's simplified one, not libGDX:
   ```
   name
   x,y,w,h
   ox,oy
   ```
   (plus a `name.png` beside it). The Nez loader here **does not** accept `size:`/`rotate:` headers.
7. **Cartridge has both screens and sprites in VRAM**, so the extraction reads the VDP instead of
   decoding ROM blobs.

---

## 9. Next steps (priority order)

1. **Verify the player sprite in game** (anchor, size, animation) and fix the crop/anchor if needed.
2. **Replace fallback shapes with captured art**: map the real sprite-layer variants to the renderer's
   names (`vick_s{set}_{pose}`, `vick_x_*`, `vick_d_*`, `enemy_t*`, `coffin_*`) when the SAT bytes are
   identified. The atlas is already complete, so this is fidelity work, not a blocker.
3. **V9938 (SCREEN 5)** for the MSX2 art from the `.mx2` cartridge (optional but desirable).
4. **Full extraction time**: audio dominates (~5 min). Room dumps add only a few seconds; consider a
   progress bar in the first-run screen (it exists, but the stage names could be shown).

---

## 10. How to report a bug from a test run

1. Copy `src/KingsValley.Desktop/bin/Debug/net10.0/kingsvalley2.log` (it lists the art set, the
   package, the fonts and the language).
2. Say which art set was active (line `[GfxVariant] arte: …`).
3. Say what you expected vs. what you saw (screenshot helps).
