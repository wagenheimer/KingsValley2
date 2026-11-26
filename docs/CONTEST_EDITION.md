# Edit Contest edition

This special collection contains twenty custom-stage contest entries, grouped
into four publication awards with five authors each. It is a separate campaign
from the original sixty stages. The [illustrated README overview](../README.md#edit-contest-edition)
shows the opening, publication and author selectors, six-theme selector and gameplay
captured from the supported English working cartridge.

The supplied ZIP is the MSX2 JoySNES revision, SHA-1 `4a157e6e217f6af4ab3dec5d7dd5188e5cef7954`. The [openMSX software database](https://github.com/openMSX/openMSX/blob/master/share/softwaredb.xml) independently identifies this cartridge and its KonamiSCC mapper. Other Contest revisions are not automatically admitted by filename.

## Playing

Select **GAME → CONTEST**. The native publication menu offers MSX Magazine, MSX-FAN, Comptiq and Beep awards, followed by the selected publication's five author names. **SCREEN SELECT** then chooses one of six visual themes. Use UP/DOWN and ENTER (gamepad D-pad/A); ESC/B returns one step. Application stage numbers 1–20 identify these selections deterministically. Completing a challenge returns to the native publication selector. Original play retains its Start Select/password flow.

The framed native Contest title precedes publication selection, using the cartridge's `0x20` logic-call title counter. Mouse users can click a publication/author, click the theme preview to cycle themes, and click **SCREEN SELECT** to start. The application campaign chooser remains separate from these native cartridge screens.

Contest completion, high scores and save-state slots are independent; save states preserve the chosen visual theme. Legacy original saves retain their existing paths and fields. GRAPHICS/F11 cycles only within the current campaign. Campaign selection is application UI, while all thirty Contest menu frames are extracted native SCREEN 5 binaries, displayed through Gum. Authentic Japanese names/logos remain unchanged source artwork. `KINGSVALLEY_IGNORE_INPUT=1` suppresses physical input in these selectors and debug shortcuts for reproducible captures.

## Independent extraction

The supported Japanese cartridge is translated automatically during extraction
using the complete bundled FRS enhancement patch v1.1 archive. No internet
connection is required. The source ROM is preserved; only a private working
copy is patched. English title/menu artwork and romanized authors are then
extracted from that copy. The twenty stages and campaign progress are unchanged.
See [FRS credits, license and exact hashes](../third_party/FRS/README.md).
The report records both cartridge identities. For native Japanese diagnostic
extraction, set `KINGSVALLEY_CONTEST_LANGUAGE=jp` before starting the application.

Use **BROWSE** on Cartridge Setup, select the owned ZIP, then **EXTRACT SELECTED**. No original cartridge or external emulator is required. Output lives under `<MSXMusic>/kings-valley-2/msx2-contest/`, with independent `Audio`, validated `Graphics/generations`, `current.json`, and `Report/index.html`.

Graphics profile revision 3 requires the native completion screen and all thirty native menu frames as well as twenty stages, native binary screens, six block/palette sets, 78 mechanism frames and a 637-region source-derived atlas, including ten digits captured through the cartridge's native HUD font writer. Older complete generations remain immutable, but must be re-extracted for this revision. Audio contains 11 songs and 48 effects; six additional driver commands are expected to be silent. The report records cartridge identity and presentation limits.

## Native mappings

`E21C` selects group 0–3; `E21D` selects entry 0–4. `F0F6/F0F7` are cartridge selection offsets. Deterministic extraction sets those offsets to zero, covering all twenty selections once. The native default theme is pyramid 1 and the internal stage variable is 1. The port honors extracted pyramid/music fields rather than deriving them from the application challenge number.

The loader `5A73` produces terrain at `E900`, items at `E300`, mechanisms at `E600`, stones at `E700`, and the grid at `E788`. The grid workspace ends before `E7C0`, where golden-ladder records begin. Reading an unrestricted 64-byte grid consumes ladder bytes as fake rooms. Full preparation starts at `5A67` and needs the six-tileset cache built by `5888`.

Contest boot enters `40A3` and finishes initialization at `4114`. `Msx2VdpRunner.Contest.cs` supplies an independent adapter; the original fast runner remains unchanged. Door poses are at bank 0E `9EF0`, enemy pose pairs at bank 0D `B2A2`, and the font at ROM offset `1673C`. Mechanism writers are `8D23/8F80`, with tables at `6935/693D`. Palette and animation tables come from the Contest cartridge.

The native mural writer always uses the base mural table, without alternating by challenge number. A clean room from challenge 3 matches its native SCREEN 5 writer in all 49,152 indices after removing dynamic stones. Structural inventory checks are distinct from exhaustive gameplay, listening and independent-emulator acceptance.

## Current scope

Publication rendering uses native `5270` and marker updater `5D12`; author selection uses `51AC`, `5389` and updater `5D7D`. `5888` prepares SCREEN SELECT and `59A9` paints its selected theme. Cartridge offsets remain zero so labels match the deterministic extracted level mapping. The selected theme changes presentation without rewriting extracted source metadata or terrain. The thirty full frames include the authentic markers and preserve native 256×212 geometry.

## Native completion

The framed artwork is the edition's opening title, not an additional winner certificate. Contest completion follows its own `47CF` state machine: the existing native door-entry poses and 48-call closing curtain lead to the ROM's **CONGRATULATION** screen. The exact singular spelling is preserved in source artwork. The `E204=180` counter decrements only on alternate logic calls (`4800`), so this screen lasts 360 port ticks (12 seconds at 30 Hz), without an original-campaign bonus/password screen or a key wait. Native execution then reaches mode `03`, phase `03` (`480A` → `45A3`), which returns directly to publication selection without replaying the framed title.

`contest_clear.bin` contains the native full-frame capture, including its representative zero-score HUD as evidence. Runtime draws only its top 192 rows and keeps the actual live score HUD below. Saves restored during this sequence retain their campaign and remaining tick count. No original world-tour, ending/puzzle screens, ending music or invented award certificate are substituted.

`KINGSVALLEY_EXIT=280` with `KINGSVALLEY_EXIT_HOLD=1` captures the completion screen without waiting through the door animation. Diagnostic door completions do not award profile progress.

Set `KINGSVALLEY_TEST_CONTEST_ROM` to a locally owned ROM or ZIP for native regression tests. Runtime review supports `KINGSVALLEY_GFX=msx2-contest` and `KINGSVALLEY_START_SCENE=campaign`, `contest`, or `level`. Solving every challenge remains a separate gameplay acceptance task.

For deterministic menu screenshots, `KINGSVALLEY_CONTEST_MENU=publication`, `author`, or `theme` bypasses the preceding pages; combine with `KINGSVALLEY_IGNORE_INPUT=1` to freeze physical input. Native regression checks require four distinct publication frames, twenty distinct author frames and six distinct theme frames.
