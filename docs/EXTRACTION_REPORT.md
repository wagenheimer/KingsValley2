# Automatic extraction reports

The game generates an English, offline HTML report after every extraction attempt that starts successfully. This includes successful, partial, failed and cancelled runs. No gallery environment variable is required and no browser window opens automatically. Normal desktop shutdown cooperatively cancels an active job and waits for its report; forced termination cannot guarantee report completion.

Default locations:

```text
<MSXMusic>/kings-valley-2/msx1/Report/index.html
<MSXMusic>/kings-valley-2/msx2/Report/index.html
```

The report directory follows the configured shared collection root. `AudioBootstrap.LastReportPath` exposes the latest generated report for future game-UI integration. A failure to write the report is logged and does not invalidate otherwise usable extracted content.

## What the player can inspect

- Cartridge variant, ROM filename/hash, generation timestamp and run outcome.
- File counts, graphics previews, music/effect counts, stages, referenced rooms and content size.
- Native binary screen/room previews drawn into browser canvases, and sprite previews cropped from existing actor atlases.
- Searchable graphics with a review filter, visible-pixel counts and explicit transparent/duplicate warnings.
- Expandable structural-validation and integrity details.
- Searchable music/effect catalog with original numeric IDs, readable English names, capture durations and loop details.
- Offline WAV previews with native audio controls. Only one preview plays at a time; playback never starts automatically.
- Complete file inventory with sizes and SHA-256 hashes.
- MSX1 native animation inventory with frame counts, room/cell coverage and a notice when extracted light-wave streams have no references in the captured gameplay rooms.
- Explicit notices for partial data, unsupported extraction and unverified graphics provenance.

Graphics are now collected from one cartridge-specific attempt or validated generation. Matching manifests supply component checks and file hashes; legacy/unmatched files are explicitly unverified. Transparent regions and duplicate names are flagged and can be found with the graphics review filter. Hash changes after generation are reported separately. For the current per-cartridge readiness and outstanding fidelity checks, see [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md). Audio is included only when its game, variant and ROM hash match the current request.

## Files and performance

`index.html` embeds its styles, script, graphics data and atlas images. It needs no server, CDN, external font or internet connection. Native room/screen data is not exported to PNG files. The browser draws previews only as they become visible.

`media/` contains WAV previews rendered from the user's audio package. Previews use 16-bit stereo PCM at 22.05 kHz, and repeating entries preview their intro plus one cycle. Filenames include the source package's fingerprint and sound identifier hash; unchanged previews are reused. Copy the whole report directory when moving it to another machine.

`report.json` stores the structured inventory and presentation data. HTML/JSON are replaced using temporary files. Preview audio is also published atomically. No ROM or extracted asset is added to the source repository.

The original audio package and graphics source files are not modified. The obsolete environment-variable-driven development gallery is no longer invoked by the game's extraction flow.

## Reuse and validation

`Wagenheimer.MsxGraphics.Reports` contains game-independent data models and the HTML renderer. The game-specific `ExtractionReportBuilder` locates assets and supplies their metadata. The new graphics library has no MonoGame, Nez, Gum, MSXAudio or King's Valley dependency.

Tests cover escaping, unsafe media URLs, partial-result wording, English output under a Portuguese process culture, binary screen previews without background PNG exports, mismatched audio identity, and automatic generation during a real local-ROM extraction. Browser checks cover desktop/mobile layout, local-file playback, searching, category filtering and canvas rendering.
