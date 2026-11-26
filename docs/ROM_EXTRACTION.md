# ROM extraction: current implementation

Updated 2026-11-24. Production extraction uses only the user's verified ROM and in-process C# execution. No openMSX executable, longplay frame, pre-recorded audio, companion cartridge or procedural artwork is required. Earlier emulator/video experiments are preserved in [ROM_EXTRACTION_HISTORY.md](ROM_EXTRACTION_HISTORY.md), explicitly as historical research.

## Identity and isolation

| Cartridge | SHA-1 | Profile |
| --- | --- | --- |
| MSX1 RC-760 | `cfd872d005b7bd4cdd6e06c4c0162191f0b0415d` | Native TMS9918 graphics and isolated sound-driver execution |
| MSX1 RC-760 FRS v1.1 | `cb612cbfdf79dabe55164b386ca956a3e187bef7` | Native FRS main-loop intro capture and isolated patched sound-driver execution |
| MSX2 SCC patch | `0c7e0b618c72e7ca08bd756c3103b46e5c580c04` | Native V9938 graphics and isolated sound-driver execution |
| MSX2 Edit Contest (JoySNES) | `4a157e6e217f6af4ab3dec5d7dd5188e5cef7954` | Independent twenty-challenge MSX2 extraction; see [Contest edition](CONTEST_EDITION.md) |
| MSX2 Edit Contest (JoySNES + FRS v1.1) | `e7b0a7b083d2f5bb12fb4ea6b2e822ee6330a15e` | Automatically generated English working copy; exact source/output checks and bundled original patch archive |

The [README cartridge list](../README.md#exact-supported-cartridges) states which
cartridges are needed, the recommended revisions, sizes, and recognized but
unsupported revisions. The setup screen's **H / ROM GUIDE** contains the same
accepted identities, also available through gamepad START or its clickable footer.

Addresses belong to a particular cartridge and mapped bank. Do not reuse an MSX1 CPU address in MSX2 because its name or apparent role matches. For 8 KiB banks, file offset is `bank * 0x2000 + (CPU address & 0x1FFF)` only after establishing the active window/bank mapping. Other ROM revisions are unsupported until separately analyzed and validated.

Each job fixes its variant and ROM identity before extraction. Storage is `<MSXMusic>/kings-valley-2/<msx1|msx2>/`, with separate Audio, Graphics and Report directories. See [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md) for the immutable generation layout and runtime resolution. Legacy mixed caches are ignored and preserved.

## Shared data and reusable code

`RomStageJson` obtains terrain, ladders, room grid, objects and initial state from the supplied cartridge's tables/native loader. Matching values between cartridges do not justify shared output directories. Generic byte codecs, packed pixels, palette indexing, TMS9918 decoding, V9938 sprite decoding, atlas validation and report rendering belong in reusable libraries; cartridge routine addresses remain in game-specific recipes.

## MSX1 graphics

`MiniMsx` executes the original cartridge in process. Capture gameplay only after a living player and active stage/room are established. A blind delay can run into Game Over and destroy the relevant VRAM tables.

SCREEN 2 uses three vertical pattern/color banks. Each pattern has one color byte per row: `colorBase + colorBank + code * 8 + row`. SCREEN 1's grouped color indexing is incorrect here. Sprite composition must respect native planes, per-sprite colors, offsets, priority and pattern byte order. Display-backdrop resolution is distinct from raw VRAM index decoding.

Held-use logical poses 10..13 map to source entries 8..11 through native 5680. The eight level-door poses come from 531F/8CE4; 8597 is Game Over artwork. The intro uses a captured native-vblank actor timeline and dedicated sprite strip rather than a hand-authored path.

The pipeline redraws all referenced rooms natively, removes collectible state before clean backgrounds, and extracts object/actor states from native code/tables. Mechanism, door and terrain tests compare their pixels with cartridge routines. Map initialization installs a shifted alphabet; unrelated UI recipes require the correct native font context.

## MSX2 graphics

`Msx2VdpRunner` supplies the Z80, mapper, BIOS traps and V9938 operations needed by this cartridge. The fast block/room implementation remains unchanged; separate inspection adapters run additional recipes with bounded execution. This is a targeted execution environment, not a claim of complete MSX2 emulation.

| Content | Native entrypoints/data |
| --- | --- |
| Blocks/rooms | `5C80`, `5CF5`; six 8192-byte block tables |
| Logo/title sequence | `46DA`; title `5F8D` calls `55F0` |
| Title marker | `4D75`, original glyphs at (56,140) |
| Start selection | `677E`, SAT builder through `6794` |
| World map | `6F84` |
| Pyramid intro/card | `6807`, `6945` |
| Sound/puzzle dispatch | bank 0C `B534`; bank selector `41B5` |
| Puzzle pieces | bank 0C `B7EC`; native IDs 1..24 map to consumer slots 0..23, native zero to slot 24 |
| Mechanism row/trap writers | `BB63`, `BDC0`; tables `9374`, `937C` onward |

The verified MSX2 title entrypoint draws Japanese artwork unconditionally. Its profile does not promise an English title, and no `title_en` alias is fabricated. MSX1's two titles remain independently extracted. Missing supported content still fails validation.

Hidden-room UI palettes update only selected entries and rely on native gameplay initialization. A valid indexed bitmap can therefore still render invisible glyphs with an incorrectly initialized palette. Native glyph-bit and rendered-color checks cover this failure. SCREEN 5 color zero resolves to the VDP backdrop when transparency is enabled. Mode-2 cursors require per-scanline colors, CC color-plane combination, early clock and priority.

The full mechanism renderer first restores mural pixels. Isolated art capture enters its row-writing tail on an empty native capture surface so transparent pixels do not retain mural fragments. States are stored per pyramid and checked against their own native block tables; the runtime invalidates mechanism caches when changing pyramids.

## Native formats and publication

Room/fixed-screen images remain binary native data, never PNG backgrounds. Screens have a 38-byte header (32 palette bytes, little-endian width/height, flags, reserved byte) followed by packed 4bpp pixels. Flag bit zero enables alpha at index zero; bit one identifies the fixed TMS9918 palette. MSX1 uses native 256 by 192 pixels centered in the 256 by 212 frame. MSX2 uses 256 by 212. Atlas PNG textures are allowed.

The manifest records exact cartridge SHA-1, schema/profile revision, component checks and each artifact's SHA-256. Only a complete verified generation can replace `current.json`. Failed, cancelled and diagnostic subsets remain separate attempts. Readiness describes the implemented required inventory; it does not certify every unused ROM graphic or every gameplay animation.

## Audio

MSXAudio starts a fresh cartridge sound-driver machine per command. Profiles specify banking, initialization, command entrypoint, tick entrypoint, active-channel state and complete loop-observation ranges. It captures PSG/SCC register events rather than audio from gameplay with music and effects mixed together. Finite sounds retain terminal mute writes/tails; repeating sounds require observed complete driver-state cycles.

The supported profiles enumerate IDs 1..65: 11 music entries, 48 effects and six diagnostic stop/silent IDs. Names preserve numeric IDs and follow native call sites; descriptive names are not official track titles. Verify saved playback by decoded PCM, not event count or UI duration. See [AUDIO_EXTRACTION_AUDIT.md](AUDIO_EXTRACTION_AUDIT.md) for exact driver routines, state ranges and measured validation.

## Reproduce and assess

Use [GraphicsAudit](../tools/GraphicsAudit/README.md), two isolated roots, manifest verification and artifact path/hash comparison. Build with `dotnet build KingsValley.slnx`; run `dotnet test tests/KingsValley.Core.Tests`. Native glyph/table comparisons and runtime screenshots supplement structural checks. Independent original-emulator comparison and interactive gameplay acceptance are separate evidence levels.

The automatic English offline report is described in [EXTRACTION_REPORT.md](EXTRACTION_REPORT.md). Current counts, source revisions, validation and limitations live in [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md). The reusable skill/repository proposal is saved in [the toolkit plan](superpowers/plans/2026-11-24-msx-reconstruction-toolkit.md).

## Patched cartridges and compatibility

Compatibility is identified from resolved cartridge bytes, including ROMs inside ZIP archives. A filename, an MSX header, or the mapper alone cannot establish that extraction addresses and driver state still match. Curated profile aliases are shared by discovery, audio extraction, graphics extraction and manifest validation. Unknown revisions remain unsupported until verified; this is not universal support for arbitrary translations, hacks, truncated images or bad dumps.

The MSX1 FRS v1.1 cartridge is independently verified. The [openMSX software database](https://github.com/openMSX/openMSX/blob/master/share/softwaredb.xml) identifies its exact SHA-1 as the FRS patch with KonamiSCC mapping. [FRS documents](https://frs.badcoffee.info/patches.html) changes to DynamicVsync and the jukebox. Its sound driver retains the mapped entrypoints but produces its own native timing; extraction executes those patched bytes without restoring original code. All 65 IDs were captured: 11 songs, 48 effects, six expected silent commands and no capture-limit terminations. A full graphics run completed with 273 validated artifacts and 508 atlas regions. Readiness remains a structural/inventory guarantee, not a claim of complete visual or listening acceptance.

For this revision the intro must enter through the cartridge's title/start menu and retain the main loop. Freezing it after direct setup calls leaves the FRS actor animation stalled. The FRS capture contains 824 vblanks and five source poses; the pose strip is byte-identical to the original RC-760 capture. The original cartridge retains its independent 764-vblank recipe.

To run the optional FRS regression checks, set `KINGSVALLEY_TEST_FRS_ROM` to a locally owned cartridge. No ROM bytes or extracted assets are distributed with those tests.
