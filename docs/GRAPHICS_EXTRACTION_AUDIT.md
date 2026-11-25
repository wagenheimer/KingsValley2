# Graphics extraction audit

This review separates extraction integrity, hardware decoding and visual fidelity. A successful build or an existing atlas is not evidence that every cartridge graphic has been recovered.

## Repairs implemented

- Removed automatic companion-cartridge extraction and legacy mixed-cache adoption. Every job captures its cartridge identity and a fixed MSX1/MSX2 destination before execution.
- Added immutable generations, isolated staging, preserved incomplete attempts and an atomic current-generation pointer. Partial, unsupported, failed or cancelled results cannot replace a complete generation.
- Added exact ROM SHA-1, schema/profile revision, per-file SHA-256, required-component checks and explicit readiness. The runtime rejects unmanifested legacy graphics and opposite-flavor inheritance; development bundles need the same contract.
- Removed the zero-filled MSX2 block fallback. Missing input, wrong cartridge identity, blank tables and missing texture files are errors.
- Replaced the success-only MSX2 screen stub with native cartridge execution for the Konami logo, Japanese title, start menu and world map. Remaining recipes are explicitly incomplete.
- Removed the procedural/embedded actor, item, terrain, glyph, cursor and HUD substitutes from the extraction recipes. Application startup text remains readable without a ROM; its UI font is not exported as cartridge content.
- Removed empty MSX2 unarmed item-use slots and death slots beyond the seven frames used by the port. Removed 80 duplicate cross-flavor door names. With the native cursors, the MSX2 atlas has 629 unique, nontransparent regions, including a native HUD script and ROM font patterns.
- Corrected MSX1 player composition using the original four SAT planes, native colors, priority and Y offsets. The old two-half composition was incorrect. The old clipped standing-pose door sequence was removed.
- MSX1 reads ten unarmed source frames and twelve held-item source frames from bank 0E's `84D7` table. Native loader `5680` maps logical held-use poses 10..13 to source entries 8..11, retaining both phases/directions. Death uses `8589`; level entry uses eight native poses loaded by `531F` and composed by `8CE4`. Table `8597` belongs to Game Over and was an incorrect earlier door-art interpretation.
- Selected MSX1 rooms are redrawn by `5AEC` after stage initialization, rather than assuming the loader retained the requested room or sleeping a fixed frame count.
- Captured original item/soulstone/coffin rectangles with stage/room/coordinate sidecars. Then cleared collectible state and let the cartridge redraw clean backgrounds. Runtime collection no longer replaces those clean cells with guessed mural patches.
- Replaced the embedded clean-door hexadecimal picture with native pattern/color data and `Rooms/door_clean.bin`.
- Added a fixed-TMS9918 palette flag to the existing 38-byte screen header, avoiding conversion of MSX1 colors through the V9938 three-bit palette. Legacy MSX2 binaries retain their format.
- Replaced nearest-TMS-color matching in the cutscene-strip builder with lossless native palette indexing. MSX2 retains its independently extracted five-pose strip. MSX1 intro playback now uses a native vblank timeline and its dedicated sprite strip, with every replayed actor pixel compared against the original SAT layer.
- Added pure SCREEN 2 address masking, sprite-mode-1 decoding, bounded RLE, PNG integrity/alpha inspection and atlas validation to `Wagenheimer.MsxGraphics`.
- Reports now include graphics search, a review filter, visible-pixel counts, duplicate/transparent warnings, expandable validation details and post-generation integrity checks. Generated diagnostics stay English independently of OS exception localization.

## Evidence and its limits

The local MSX1 cartridge produced 60 stage files and 154 native room dumps. The current MSX1 recipe produces 273 artifacts, 508 unique, nontransparent atlas regions and 703 report previews. Two independent complete extractions passed manifest verification with identical SHA-256 hashes for all 273 artifacts; all required inventory checks passed. The current MSX2 recipe produces 165 artifacts, 629 unique, nontransparent atlas regions and 722 previews. Two independent final MSX2 extractions passed required checks and produced identical artifact hashes. The final solution build passed with zero warnings/errors and all 241 tests passed. Browser checks found thirteen pyramid-6 mechanism previews, zero review warnings, no JavaScript errors and no horizontal overflow at 390 pixels. Inventory readiness does not establish independent pixel fidelity or complete gameplay acceptance.

Two extraction orders were compared in isolated collection roots: MSX2 then MSX1, and MSX1 then MSX2. Every artifact path/hash matched for the same cartridge. Manifests themselves contain generation timestamps and are not expected to be byte-identical.

The six MSX2 block-table SHA-256 hashes match the previous fast runner's output:

| Pyramid | SHA-256 |
| --- | --- |
| 1 | `e1c0857447480c3431e606bf79d0db2acccd4df878115ac54f8a28f12f0c8e0d` |
| 2 | `6fbef7c74b9c6b3147888af18de213327c7ef0440055ccf0ebcd78f2f1527688` |
| 3 | `d46668dfecc3e1023a078c92efb9d31913fa7f9c1b34823ba02897f27c98e7ab` |
| 4 | `71d3d9b6e7d087427a4364733a717fbb4b6d0194658cb1efc34963537b49a56c` |
| 5 | `a127ac41f81d5210b1d4e3118c70acacfe2b4dbedc94514a71376dc6bd443625` |
| 6 | `fb29aacf971db668099194b8a5db75072db0aaf73579715d5beca63fe2c014b2` |

The runner's existing CPU, mapper, VDP commands and block recipe were preserved. Its original file changes only to a partial-class declaration; inspection/screen adapters are separate.

Native collectible cutouts were compared with the cleaned room region. All six item types and all six pyramid soulstone samples changed after redraw. Tests also check the native palette, missing/wrong-ROM input, cancellation, corrupt PNG/manifest data, atlas ambiguity and partial-result wording. These checks do not replace a real gameplay/reference comparison.

## Known incomplete areas

Both supported cartridge recipes now satisfy their implemented required inventory and integrity checks and can publish validated generations. This is not a claim that every unused graphic or every original scene behavior has been reconstructed.

- The verified MSX2 title entrypoint draws Japanese artwork unconditionally. English title artwork is an unsupported optional capability for this profile, not a fabricated alias or a borrowed MSX1 screen. The title-language preference uses the available native title. Missing supported screens still fail readiness.
- MSX1 screens, cursors, exit-door states, terrain, ladders, movable blocks, sarcophagi and crumbling-terrain frames now have native recipes. Full movement/background restoration still needs interactive acceptance.
- MSX2 mechanism states now have 78 native files (13 per pyramid), checked against each pyramid's original blocks. MSX1 retains its independently verified shared frames and eye/light-wave tables.
- World-map engravings, complete ending/editor presentation and unused-ROM graphic coverage remain outside this implemented consumer inventory; these require separate feature/fidelity work.
- Per-flavor runtime animation mapping, digging/door restoration, palette cycles, sprite overlap, cutscene feet and all-room visual fidelity still need original-ROM/openMSX comparisons.
- Some existing port behaviors/consumer names originate from the MSX2 implementation. Source-derived MSX1 pixels alone do not prove those mappings are correct.

Normal desktop shutdown requests cancellation and waits for the worker to retain its attempt and finish the report. Forced process termination or an unavailable output filesystem cannot guarantee a completed report; neither can publish an incomplete generation as ready.

The stricter availability rule has a deliberate consequence: an old mixed cache or unmanifested private bundle no longer enables PLAY. Both supported cartridges can now produce their required inventory independently. Previous legacy files are preserved for inspection. This remains an engineering branch with interactive fidelity checks outstanding.

## Reference sources

### Opening presentation and MSX1 player-animation follow-up

The current logo binaries are full 256-pixel-wide native frames. The splash formerly applied the old cropped-logo origin (X=40, plus another Y=64 in MSX2), shifting the background and artwork. It also revealed rows from the top of the full bitmap instead of the artwork beginning at native row 64. Rendering now keeps the original full-frame coordinates, samples the ROM screen's own backdrop and applies only the MSX1 ten-row letterbox. The title in the user's MSX1 capture was correctly centered; its missing cursor interval came from a separate 1.9-second static-title splash phase. The interactive menu now owns the first visible title, cursor and input together.

MSX1 held-use poses were incorrectly collapsed into duplicate per-direction frames. Native loader 5680 subtracts two from logical poses 10..13. Every phase of all six held-item sets is now compared with native 565F/9CA3 output. Level-door entry had incorrectly used Game Over table 8597 and writer 9D07. The corrected recipe runs native loader 531F (RLE B61D) and SAT writer 8CE4 for all eight frames, retaining native offsets/colors. Gameplay VRAM is restored after capture so these patterns cannot replace enemy/projectile source patterns. MSX1 door rendering also avoids the ordinary right-facing two-pixel shift, because 8D29 uses E284 directly.

The MSX1 intro no longer shares an authored MSX2-style path, fade or gameplay-door pose pair. Native state 4869/6639 is recorded at emulated vblank cadence using SAT records 4..7 written by 74A1. The result is 764 frames at 60 Hz, five deduplicated sprite poses and exact per-frame coordinates, visibility and message-phase selection. `intro_vick.json` accompanies the native binary strip. The reusable `NativeActorAnimation` model validates and samples this data; game addresses stay in the cartridge recipe. A fresh native execution replayed every recorded actor pixel without differences. This verifies the standalone emulator's sprite output, not independent hardware timing certification.

MSX1 graphics profile revision 2 requires the timeline and eighth door pose. Both independent complete extractions contain 273 artifacts, 508 atlas regions and 703 report previews. All 241 tests pass and the solution builds without warnings/errors.


### Native MSX2 completion and validation boundaries

The final MSX2 recipe executes start selection (`677E`), the world map (`6F84`), pyramid intro (`6807`), intro card (`6945`) and bank-0C sound/puzzle dispatch (`B534`). The bank-C selector is `41B5`, not MSX1's `41BD`. Puzzle writer `B7EC` exports native IDs 1..24 into slots 0..23 and native zero into slot 24. UI initialization preserves native gameplay palette entries that the partial sound/puzzle palette lists do not update. Tests compare exact SOUND SELECT and PUZZLE GAME glyph bits and their visible white palette entry, not just nonzero indices.

Title `5F8D` unconditionally calls Japanese renderer `55F0`; its call bytes are checked against the supplied cartridge. MSX1's BIOS-region selection must not be extrapolated to this profile. `title_en.bin` is not produced or required for MSX2; all other supported screen requirements remain intact. Profile revision 3 records the updated MSX2 contract; MSX1 now uses revision 2.

SCREEN 5 snapshots preserve packed indices and resolve palette entry zero through R7 while R8.TP is clear, following the VDP transparency rule. The native logo therefore retains its white backdrop. The reusable `V9938Sprites` decoder interprets mode-2 scanline colors, early clock, priority and CC plane combinations for the arrow and sound pointer. Synthetic fixtures cover overlapping color planes and an orphan CC plane. Native captures retain alpha when entering the atlas. No openMSX source was copied. Runtime review also caught black UI glyphs and missing HUD labels: MSX2 native white is palette entry 11, while entry 15 is black. Atlas glyphs now retain native white, and HUD capture runs native font initialization `5BEB` before text stream `5EF2`. A pixel comparison checks the HUD label at its native origin against the extracted H glyph.

The complete actor renderer restores the mural before writing mechanisms. A scene crop therefore includes background fragments even after an initial clear. The final recipe uses native fill `4FED`, then row writer `BB63` or trap writer `BDC0` with the original IX state/table pointers. Every packed pixel of all 78 exported frames is compared with its corresponding native block/table entry. The runtime selects `Rooms/Mechanisms/pN` and invalidates cached frames when changing pyramids. The manifest verifier rejects omission of required mechanism artifacts/checks, room tables or sprite checks even if the remaining hashes match.

The fast MSX2 block runner remains unchanged. The six baseline block hashes below still match. Additional inspection calls count BIOS traps toward their instruction budget, preventing unsupported return paths from escaping the bound. Runtime captures were inspected at 1024 by 848 for title, start selection, Sound Test, Puzzle, pyramid intro and stage 1. Sound Test/Puzzle/stage 1 were captured again after the final font/HUD repair. All captured processes exited successfully; the final HUD contains its native HISCORE/SCORE/STAGE/REST labels and the additional UI text is visible. The canonical revision-3 generation contains the same 165 artifact hashes as both independent final roots; its report includes 722 graphics previews, 59 existing audio entries and 166 files including the audio package. These static captures do not establish interactive behavior or independent original-emulator equivalence.

### Native MSX1 supplemental screens and cursor provenance

The supplemental recipes execute the cartridge's start menu (`6588`), world map (`6DEE`), intro card (`6611` then `6752`), sound module (`B530`/`B909`) and puzzle module (`B530`/`B6AC`). The Konami logo is captured only after native mode step `46DB` reaches boot substate 3 with all six reveal rows complete. Screen decoding explicitly resolves the VDP backdrop, preserving the logo's white background without changing raw SCREEN 2 capture heuristics.

The map installs a shifted alphabet. The following UI recipes therefore start from a fresh native gameplay context. Integration checks compare the rendered SOUND SELECT and DO YOUR BEST text with the original gameplay glyph bits; this caught and fixed a four-character alphabet shift during diagnostic review. Puzzle sheet slots 0..23 contain native numbered pieces 1..24, and slot 24 contains native piece zero (the hole), matching the renderer's existing numbering contract.

The title indicator is not an invented asset: native `4D62` writes glyphs `DC/DD` at (56,136), and the port's former procedural 16-by-7 footprint matches those native glyphs. Its hand-entered colors did not match. The procedural drawing has been replaced by the extracted 16-by-8 region with the original palette. The start-menu arrow comes from native SAT builder `65CF`; the sound pointer composites all six native SAT records from `B9A2` with transparent color zero and opaque black outlines. Native cursor binaries retain their alpha semantics when copied into the atlas.

The game now follows the documented/native MSX1 GAME-to-START SELECT transition (`4845` calls `6588`). Title/start cursors use their native origins. Sound-test and puzzle backgrounds, moving pieces, cursors and pointer input preserve 256-by-192 geometry centered in the 256-by-212 frame. Full interactive navigation and independent openMSX screen comparison remain separate acceptance checks.

Runtime captures of the title, sound test and puzzle were inspected at 1024 by 848 using the published MSX1 generation. They show the native cursor, readable English glyphs and centered native geometry. This verifies static rendering only; keyboard/mouse interaction, puzzle completion and gameplay restoration still require acceptance checks. The current canonical offline report includes 703 graphics previews, 59 audio entries and 274 files including the existing audio package.

### Native MSX1 exit doors, terrain and movable blocks

The three 32 by 32 exit-door states execute native `8C14`, with the written codes checked against bank 02 table `9059`. Studs use the native pattern selected at `8C71`; the clean-door order is read from the same ROM table rather than an embedded index array. Pixel checks establish that `Rooms/door_clean.bin` matches the closed door in all six pyramids. All sixteen stud-layout rows at bank 0D `B9AD` match the port's layout table; index zero is intentionally present in the native pointer table.

Native terrain renderer `453D` selects pattern `05` for diggable terrain and `62` for fixed walls. Both ladder patterns (`03`/`04`) are retained. The four existing soft/wall quadrant names are compatibility aliases of the single native cell pattern, not four distinct animation frames. Each pyramid's own pattern/color tables are used, and tests compare these sprites with a complete native terrain redraw.

Movable blocks use the four-pattern grid at bank 02 `9089`, drawn by native mechanism routine `626B`/`937A`. They now have their own 16 by 16 atlas entry; the MSX1 game renderer and both editor views select it instead of four diggable-terrain sprites. The MSX2 recipe and rendering branch are unchanged. Tests verify the native block pixels, both ladder patterns, all three door frames, stud removal and terrain in all six pyramids. Full interactive pushing/falling and background restoration remain outside this pixel-validation checkpoint.

Historical door/terrain checkpoint (superseded by the supplemental-screen results above): all 229 tests pass and the solution builds with zero warnings/errors. Both extraction roots produce identical hashes for all 261 artifacts. The MSX1 atlas has 505 regions and its report has 689 previews, with no transparent/duplicate regions flagged. Browser checks verify door/block/ladder/terrain filtering, native canvas rendering and no horizontal overflow at 390 pixels. Eleven required components remain missing: nine screen files plus the `arrow` and `title_cursor` sprite recipes. The generation remains partial.

### Native MSX1 sarcophagus and crumbling-terrain follow-up

The MSX1 atlas now reads the three six-code sarcophagus grids from bank 02 tables `90C5`, `90CB` and `90D1`, rendering them through native grid writer `53D6`. The full 16 by 24 region starts at actor Y minus eight, as defined by `AACE`. The earlier room cutout started at the actor's Y coordinate and could include surrounding scenery; that capture and its sidecar are no longer produced. Each pyramid supplies its own live pattern/color tables.

Eighteen crumbling tile codes (`6` through `23`) per pyramid are rendered through native writer `53EF`. These replace embedded Base64 pictures, cached fallback textures and an inconsistent hard-coded palette in `RoomRenderer`. Opaque black pixels are retained. The renderer now resolves both coffin and crumble names through the active flavor's atlas.

An integration test compares every output pixel against native mummy spawning (`A9E8`), opening/closing updates (`6356`, `ABB0`, `ABEA`) and the floor/wall digging handlers (`A629`, `A706`). All six pyramids, three coffin states, three digging phases and both wall directions are exercised. Native digging tables at `A683`, `A769` and `A76F` establish the tile codes used by the port.

The same renderer review found MSX1 guards which suppressed initial items and soulstones because older dumps baked those collectibles into the background. Current dumps are clean, so both flavors now draw their active collectibles dynamically. The existing clean-room integration checks establish the absence of baked collectible pixels; full interactive collection/playthrough verification remains pending with the rest of runtime acceptance.

Historical object-state checkpoint (superseded by later results above): all 228 tests pass and the solution builds with zero warnings/errors. Two full extractions produced identical hashes for all 261 artifacts. The offline report contains 599 previews, including eighteen coffin states and 108 crumble tiles; all are searchable and render without browser errors or horizontal overflow at 390 pixels. Required checks still report 89 unresolved MSX1 components, so the result remains partial and is not promoted to a playable generation.

### Native MSX1 background color animations

`Rooms/animations.json` is now generated during clean-room extraction. The six eye definitions come from fixed-bank table `5936 + pyramid * 10`; eight light-wave pointers come from `5930`. Streams use the cartridge's bank-7/bank-8 mapping and the reusable bounded Konami RLE reader. Four eye frame slots per pyramid are retained even when native pointers intentionally repeat. Periods remain ROM logic ticks: 12, 18, 16, 5, 12 and 4 for the six pyramids; the light wave advances every four ticks.

Every captured clean room supplies exact name-table references, pattern bits and color bytes, using SCREEN 2 bank masks. The collector supports both eye and light-wave references; the older Python script registered only eyes. The full 154-room scan found 2,864 eye cells in 100 rooms and zero light-wave cells. The eight native light-wave streams are preserved, but their use on auxiliary screens remains unverified; no gameplay cells are invented. Rooms with no matching cells retain an explicit empty entry so inventory coverage can be validated independently of animated-cell count. Validation checks all referenced room keys, native byte ranges, frame/pattern dimensions, cell positions, offsets and duplicate cells.

Integration verification invokes native `58C5` and `5915`. All extracted color frames match the bytes written by the cartridge into all three VRAM color banks. The existing C# playback matches native decoded pixels over a complete six-step eye ping-pong cycle in each pyramid, including the light-wave clock. This is a direct cartridge-routine comparison within the standalone emulator; comparison with an independent openMSX run remains pending.

The color-animation follow-up passes all 227 tests and the solution build has zero warnings/errors. At that checkpoint, all 272 artifacts matched between independent extraction roots, and the previous 271 artifact hashes remained unchanged. The offline report includes the native animation inventory and the zero-light-wave-reference notice; browser verification found no script errors or horizontal overflow at 390 pixels. The light-wave playback clock is checked with a separate synthetic cell against the live native color bank, since the captured clean rooms contain no matching cells.

### Native MSX1 enemy follow-up

The verified MSX1 cartridge now supplies 104 enemy regions: four native color families and 26 frames each. The recipe executes SAT builder `646E`/`64FD`, checks its output against bank 0D table `BECF`, and renders the live gameplay pattern bytes with the reusable TMS9918 decoder. The color bytes come from bank 03 table `AA83 + kind`. No MSX2 patterns or colors are used.

Unlike MSX2 table `BEED`, MSX1 slot 23 contains a visible pattern and must be retained. Branches at `6429`, `B051` and `B440` skip special color changes on MSX1. Runtime color selection now respects that behavior instead of requesting MSX2-only families 5 and 6. Tests check all 104 regions for visible pixels and exact single-plane native palette colors, plus per-flavor state selection. This establishes source-derived frames and color selection; animation timing and in-game overlap still need reference playback comparison.

### Native MSX1 projectile and mechanism follow-up

The six projectile frames execute `8ECA`/`8F0B` with native E300 records. Their pattern/color attributes are checked against bank 02 tables `8FAF` and `8FAC`. The knife and boomerang each use three single-plane frames; they do not borrow the MSX2 paired patterns or colors. An integration check compares every atlas pixel with the live native pattern bits and ROM color table.

The reusable `Tms9918.DecodeSpriteRegion` renders an isolated attribute table using the original registers, preserving size, magnification, early clock and priority without modifying the supplied snapshot. Both enemy and projectile recipes use it. A ROM-free fixture covers magnification, early clock, pattern-index masking, exclusion of unrelated sprites, bounds and snapshot preservation.

The mechanism recipe executes native dispatcher `6254` for type 1 rotating doors, type 2 one-way doors and type 4 trap bars. It verifies the written name-table codes against bank 02 tables `908D`/`9095` and the native type 4 code `61` selected at `BB44`. All thirteen packed frames must match across all six pyramids before any shared mechanism file is written. Native colors are preserved, including the background color that older scripts removed by a fixed color-5 rule. The offline report now renders these packed files with the fixed TMS9918 palette. Type 3 terrain collapse has no independent frame file and remains part of terrain restoration validation.

Follow-up verification: the solution builds with zero warnings/errors and all 225 tests pass with the local ROM prerequisites available. Two full MSX1 extractions into separate collection roots produced identical SHA-256 values for all 271 artifacts. The canonical offline report contains 485 graphics previews, including six projectile and thirteen mechanism previews; browser checks found no JavaScript errors or horizontal overflow at 390 pixels. These checks do not establish runtime animation timing or original-emulator pixel fidelity.

Hardware behavior was checked against openMSX source revision `fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a`:

- [CharacterConverter.cc](https://github.com/openMSX/openMSX/blob/fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a/src/video/CharacterConverter.cc): SCREEN 2 scanline/character addressing.
- [VDP.cc](https://github.com/openMSX/openMSX/blob/fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a/src/video/VDP.cc): color/pattern base masks and the TMS pattern/color register interaction.
- [SpriteChecker.cc](https://github.com/openMSX/openMSX/blob/fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a/src/video/SpriteChecker.cc): pattern quadrants, Y wrapping, early clock, transparency and scanline limits.
- [VDPCmdEngine.cc](https://github.com/openMSX/openMSX/blob/fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a/src/video/VDPCmdEngine.cc): command semantics reviewed without replacing the fast runner's implementation.

Game-specific MSX2 routines were cross-referenced with [0xe1f/KingsValley2](https://github.com/0xe1f/KingsValley2), revision `8ba9be9be9dd92de729c9f4e236b193ed3dc4676`. MSX1 routine/table addresses were checked by disassembling the user's exact cartridge. No openMSX code was copied and the extraction executable has no openMSX dependency.

## Reproduction

See [GraphicsAudit](../tools/GraphicsAudit/README.md). Do not publish ROM files, extracted art or audio recordings with the code.

### Published animation repair validation

Generation `80101af4542b4eb28e4ab177fbab567f` is the canonical MSX1 profile-2 generation. All 273 artifact hashes match both independent extraction roots, and manifest verification passes. The automatic English report contains 703 graphics previews, 59 existing audio entries and 274 files. The final solution build has zero warnings/errors; all 241 tests pass.

Runtime captures cover the native MSX1 intro through its message card and gameplay transition, four level-door countdown samples, and pickaxe/drill use in both directions. The intro actor follows the ground and native jump positions without the former authored offset/fade. Door snapshots now use the dedicated native entry frames. These captures complement exhaustive native sprite-layer comparisons; they do not certify independent hardware timing or all gameplay. Diagnostic captures remain outside project content in the temporary directory.

The opening runtime sequence shows the sword cursor on the first visible title frame (diagnostic frame 278), immediately after the scene transition. MSX1 and MSX2 logo captures preserve their own native origins; MSX1 alone receives the ten-row letterbox.

### Intro door presentation correction

Interactive review rejected the intro's two front-facing arrival poses, while confirming the level-door back-facing animation. `Msx1IntroPresentation` now presents recorded arrival poses 3/4 using the same native `vick_d_00`/`vick_d_01` atlas pair as the level. Their seven transparent top rows are removed from the draw origin; tests verify this padding against native door pixels. The raw five-pose intro strip and complete SAT timeline remain unchanged as extraction evidence. Walking, jump/landing coordinates, timing, visibility and message transition retain their recorded values. This is an explicit requested presentation choice, not a claim that the raw intro SAT uses the level-door artwork.

Runtime follow-up preloads the player atlas during intro initialization, before the timed playback and drawing pass. First-use texture loading must not consume the short door-entry interval. The second raw arrival pose trims one left column, so its displayed back-facing replacement restores the native EDCC anchor; regression checks compare both entry poses with that anchor at every recorded vblank.

Final runtime pixel checks find both back-facing atlas poses at the same fixed visible origin (216,160, including the MSX1 letterbox) in the captured intro. Opaque RGB pixels match the native level-door atlas pair exactly. The complete 241-test suite and all 161 MSXAudio tests pass; the final solution build has zero warnings/errors.
