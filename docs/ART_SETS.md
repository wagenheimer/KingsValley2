# Art sets

Updated 2026-11-24. An art set chooses a rendering pipeline and may override same-flavor assets. It does not authorize mixing cartridges.

| Built-in ID | Flavor | Development bundle | Extracted graphics |
| --- | --- | --- | --- |
| `msx1` | TMS9918 SCREEN 2 | `Content/RomMsx1` | `<MSXMusic>/kings-valley-2/msx1/Graphics/generations/<id>` |
| `msx2` | V9938 SCREEN 5 | `Content/Rom` | `<MSXMusic>/kings-valley-2/msx2/Graphics/generations/<id>` |

`current.json` selects a complete generation. Runtime availability verifies matching identity, inventory and artifact hashes. A development bundle needs the equivalent contract. MSX2 is preferred when available; explicit playable selections remain supported. Neither built-in set inherits the other.

## Custom packs

Place a pack under `Content/ArtSets/<id>/` with `Art/` and an optional `artset.json`:

```json
{"name":"Remaster","flavor":"msx2","base":"msx2"}
```

The pack may override `Art/kv2.atlas` with its texture, `Art/Screens/*.bin`, and `Rooms/`. Missing files may resolve through a base of the **same flavor** only. The chain is bounded and cycle checked. Discovery alone does not prove the underlying flavor is playable. Custom artwork must not be reported as extracted cartridge artwork.

## Rendering and controls

MSX2 rooms use native 4bpp blocks, murals, decorations and palette animation. MSX1 uses native 256 by 192 room dumps and dynamic objects, centered vertically with ten black rows in the 256 by 212 frame. Never stretch MSX1 to 212 rows.

Use the system menu GRAPHICS setting or F11 to cycle available art sets. `KINGSVALLEY_GFX=<id>` is the diagnostic selection; F2 is Sound Test, not graphics selection.

Fixed screens use a 38-byte header: palette 32 bytes, little-endian width/height four bytes, flags one byte and reserved one byte, followed by packed 4bpp pixels. Flag bit zero enables transparent color zero; bit one selects the fixed TMS9918 palette. Legacy flag-zero V9938 screens keep their contract. No PNG background or fixed screen is loaded by the game. Sprite atlases may use PNG.

`tools/kv2-extract/art/artkit.py` is an optional artist workflow, not the production extraction or a fidelity oracle. Inspect its supported formats before using it on a new palette-model header; do not assume a rendered-color round trip preserves native palette indices or alpha. Export from the selected validated generation's `Screens` directory, never the old mixed cache.

See [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md) for current paths and [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md) for evidence and limitations.


## MSX1 object background presentation

The system menu's **OBJECT BG** row selects **ORIGINAL** or **TRANSPARENT** in MSX1. It displays **N/A (MSX2)** for MSX2. Original preserves the cartridge's black tile-object backgrounds; Transparent reveals the room around items, soulstones, coffins and exit doors. The change applies immediately and is saved. The toggle itself plays no cursor/confirmation effect; ordinary menu navigation keeps its existing feedback.

This is an optional runtime presentation copy, not an extraction change. The reusable `SpriteBackdrop.RemoveConnectedBlack` removes border-connected black/transparent pixels and preserves enclosed black details. It only applies to eligible MSX1 object regions, including item-name aliases; actors, projectiles and terrain remain unchanged. Native atlas files and manifests are never rewritten. Baked coffin/door backgrounds are restored from the native mural and terrain before drawing the optional copies.

For reproducible captures, `KINGSVALLEY_MSX1_OBJECT_BG=original|transparent` overrides the initial display preference without saving it. This setting controls object backgrounds, not the VDP display border or MSX1's ten-pixel presentation letterbox.
