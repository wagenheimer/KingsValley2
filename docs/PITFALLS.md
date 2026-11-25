# Pitfalls already paid for (read before debugging)

A catalog of what has **already cost time** on this stack (MonoGame + Nez + Gum). Inherited and adapted
from the base project. If a symptom matches an item here, **do not investigate "by eye"** — go straight
to the cause.

---

## Nez

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| Scale/zoom does not apply | `Camera.Zoom` is **normalized (-1..1)** | use `Camera.RawZoom` |
| UI hidden behind the scene | `GumUI.Draw()` before `base.Draw` | draw **after** `base.Draw(gameTime)` |
| Sprite "does not draw" in `PostUiOverlay` | `batcher.Draw(texture, Rectangle, Color)` does not render | `SpriteBatchDrawing.FillRectangle` |
| Something from the game is **never** above the UI | Nez's `SpriteRenderer` | use the **`PostUiOverlay`** (`IPostUiDrawable`) |
| `Graphics.Instance.PixelTexture` is null | it only exists if Nez was initialized with a `BitmapFont` | use `SpriteBatchDrawing.Pixel` |
| Scene with "zeroed" fields at the start | `Scene.Initialize()` runs in the **base constructor** | put the content in `OnStart() → OnInitialize()` |
| Scene change "in the middle of Update" breaks | assigning `Core.Scene` is **deferred** | that is expected — use `GameNavigation` |
| Screenshot without the UI | `Scene.RequestScreenshot` captures **before** Gum | use `BackBufferScreenshot` / `ScreenshotRequest` |

## Gum

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| **No** button responds to clicks | the parent has **zero** size | `RaiseChildrenEventsOutsideOfBounds = true` on the parent |
| Child does not receive events | the parent does **not** expose events | `HasEvents=false` + `ExposeChildrenEvents=true` on the root |
| UI **disappears** without an error | a wrongly named element/tag is **silently ignored** | run `dotnet gumcli check` (it must say `No errors found`) |
| `WidthUnits`/`XUnits` "make no sense" | in the XML the enums are **ints** | see the `gum-file-format` skill (tables) |
| Text does not change color with `OutlineThickness` | `OutlineThickness` does not change the outline **color** | the outline is always black |
| `Button.Text` disappears | the label needs `ExposedAsName="Text"` | adjust the `.gucx` |
| `ApplyState` "does not reset" | it **only applies what is declared** | declare **everything** that affects the state |
| Inherited unit "doubles" the size | `RelativeToParent` **adds** the parent | be careful when changing the unit |
| Custom cursor does not work | `GumService.Cursor` casts and returns `null` | use `FormsUtilities.Cursor` |

## Resolution / coordinates

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| Image **blurred** when enlarging the window | `SceneResolutionPolicy.FixedHeight` stretches | `None` + **camera zoom** (`DesignViewport`) |
| World and UI misaligned | positioning by screen pixel | position in **design units** |
| Element in the wrong place on resize | used `SceneRenderTargetSize` | use `DesignViewport` (`DesignWidth/Height/Scale`) |
| MSX1 screen with **duplicated scanlines / distorted pixels** | `ScreenBuilder` stretched 192 to 212 | **Never stretch 192 into 212**. Use native 1:1 pixels + vertical centering (`offsetY = 10`) |
| MSX1 menu cursor misaligned with text | hardcoded Y coordinates without `offsetY` | use `offsetY = (212 - 192) / 2 = 10` and `offsetY + 136 + opt * 16` |

## MSX1 / SCREEN 2 / ROM Extraction

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| MSX1 screens/rooms with **corrupted colors / striped artifacts** | formula used SCREEN 1 indexing `(code / 8) * 8` | In SCREEN 2 each code has 8 color bytes: `code * 8 + row` |
| Middle and bottom thirds of MSX1 screen with wrong patterns/colors | omitted bank switching | SCREEN 2 has 3 banks of 2 KB (`pBank = (ty / 8 & pMask) * 0x800`, `cBank = (ty / 8 & cMask) * 0x800`) |
| Development assets do not appear | The runtime selects a validated flavor-specific generation | Inspect its `current.json` and manifest under `<MSXMusic>/kings-valley-2/<variant>/Graphics`; never purge or adopt the legacy mixed cache |
| 16-by-16 sprite quadrants appear transposed | Pattern bytes were interpreted as row-major quadrants | The native format stores the left sixteen row bytes first, then the right sixteen row bytes |
| Items/gems **remain on screen after collection** in MSX1 | VRAM dump (`sNN_rMM.bin`) captured during gameplay had items baked into background | Strip items/soulstones from `sNN_rMM.bin` replacing with matching clean mural strips; `RoomRenderer` draws them as sprites |
| Vick sunken or off-position in MSX1 cutscenes (Pyramid intro) | hardcoded MSX2 (212 px) Y-coordinates on MSX1 (192 px) screen | Branch by flavor: `IntroPathMsx1` (`Y=148` on sand, `140` at door) + `offsetY = 10` |

## Atlas

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| Light halo on the edges | atlas drawn with the wrong blend | Gum UI = `premultiplyAlpha:false`; scene = `true` |
| "Seam" between neighboring tiles | **transparent** padding | padding ≥ 2 **with extrusion** |
| Sprites crooked/shifted | `rotation` or `trim` enabled | **OFF** (the Nez loader ignores them) |
| UI breaks when repacking | art bound by coordinates | bind **by NAME** (the `.gusx` does not store `TextureLeft/Top`) |
| Sprite with the wrong size | size came from the texture | size = art pixels at **1x**; `BaseScale = 1/ArtScale` |

## Fonts (KernSmith)

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| Text in the default font ("No usable font…") | only `CustomFontFile` points to the `.ttf` | use **`Font` = family name** (the cache generator only looks at `Font`) |
| Missing font = error | — | it is a **warning**; the game opens with the default one (`tools/install_gum_fonts.ps1`) |
| Blurred text when scaling | fixed-size bitmap font | `TextRuntime.UseFontOversampling = true` |

## PowerShell / tooling

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| Names "corrupted" en masse (`Berlin`→`Ferlin`) | PowerShell's `-replace` is regex **and** case-insensitive | use **literal** `.Replace()` |
| Variables "vanish" in `foreach` | PowerShell is **case-insensitive** (`$n` = `$N`) | rename them |
| Build fails with `MSB3026/MSB3027` | the game is **open** (locked DLL) | close the game before building |

---

## How to use

1. Symptom → look up the matching row here.
2. If it is **not** in the catalog and you lost time on it, **add it** (that is the rule: a paid pitfall
   becomes documentation).
3. When touching the UI, the gate is always `dotnet gumcli check <GumProject.gumx>` = `No errors found`.
