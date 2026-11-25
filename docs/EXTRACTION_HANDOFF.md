# Extraction handoff

Updated 2026-11-24. This is the current entrypoint for extraction work. The old mixed-cache, procedural-fallback and companion-cartridge recipes are superseded.

## Current contract

Production extraction executes the supplied cartridge in process. It never launches or requires openMSX. Hardware source references and optional independent emulator comparisons are development tools. Exact ROM SHA-1 selects a validated profile; unsupported revisions must not silently reuse addresses.

| Variant | Validated SHA-1 | Graphics at this checkpoint | Audio |
| --- | --- | --- | --- |
| MSX1 RC-760 | `cfd872d005b7bd4cdd6e06c4c0162191f0b0415d` | Required inventory complete: 273 artifacts, 508 atlas regions, 703 previews | 11 music entries, 48 effects |
| MSX2 SCC patch | `0c7e0b618c72e7ca08bd756c3103b46e5c580c04` | Required inventory complete: 165 artifacts, 629 atlas regions, 722 previews | 11 music entries, 48 effects |

See [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md) and [AUDIO_EXTRACTION_AUDIT.md](AUDIO_EXTRACTION_AUDIT.md) for evidence and subsequent updates. Inventory readiness is not independent pixel or gameplay validation.

## Storage and publication

```text
<MSXMusic>/kings-valley-2/<msx1|msx2>/
    Audio/audio.msxaudio
    Graphics/current.json
    Graphics/generations/<id>/graphics-manifest.json
    Graphics/generations/<id>/Levels/
    Graphics/generations/<id>/Rooms/
    Graphics/generations/<id>/Art/
    Graphics/generations/<id>/Screens/
    Graphics/attempts/<id>/
    Report/index.html
```

The default collection is the user's Music/MSXMusic directory, configurable through MSXAudio settings. `current.json` selects a complete immutable generation. Partial, failed and cancelled attempts remain inspectable but cannot replace it. Old `MSXMusic/KingsValley2` and AppData/KingsValley/extracted directories are ignored and preserved; there is no automatic adoption or purge instruction.

The runtime selects only the active flavor's validated assets. Development bundles need the same manifest and inventory contract. MSX1 never inherits MSX2 graphics. MSX2 profile revision 3 uses its native Japanese title; it does not fabricate an English title. Screens and backgrounds are binary native data; atlas textures may be PNG. MSX1 256 by 192 pixels are centered in the 256 by 212 design frame with ten black rows above and below.

## Code map

- `GraphicsExtractionContext`, `Coordinator`, `Manifest`, `GenerationStore`, `ArtifactValidator`: identity, orchestration, integrity and publication.
- `RomScreens`, `Msx1SupplementalScreens`, `RomRoomDumps`, `Msx1*`: native MSX1 recipes.
- `RomDirectExtractor`, `Msx2Screens`, `Msx2VdpRunner`: native MSX2 recipes. Preserve the fast block runner and baseline hashes.
- `Wagenheimer.MsxGraphics`: hardware/format decoding, atlas inspection and offline report rendering.
- MSXAudio: standalone Z80/mapper execution, audio profiles, driver-state observation and chip playback.
- `ExtractionReportBuilder`: English report after every extraction attempt; reports do not convert an incomplete inventory into success.

## Reproduce

Use [GraphicsAudit](../tools/GraphicsAudit/README.md) with a user-supplied ROM and an isolated collection root. Run `extract`, then `verify` on the returned generation. Compare artifact path/SHA-256 pairs between independent runs; manifest timestamps may differ. Never publish ROMs, extracted graphics/audio or dumps with the toolkit.

Run `dotnet build KingsValley.slnx` and `dotnet test tests/KingsValley.Core.Tests`. Local-ROM tests report a skip when their cartridge is absent. Static screenshots, native-routine comparisons, independent emulator comparisons and interactive acceptance are different evidence levels.

## Next work

Follow [the current completion plan](superpowers/plans/2026-11-24-documentation-msx2-completion.md). The future reusable skill/repository proposal is saved in [the toolkit plan](superpowers/plans/2026-11-24-msx-reconstruction-toolkit.md).
