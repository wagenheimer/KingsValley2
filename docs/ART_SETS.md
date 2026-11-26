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

Graphics cycling stays within the active campaign. When only one compatible set is available, GRAPHICS shows its name without adjustment arrows; selecting it explains the limitation in the footer. Long setting values use compact text within a reserved value column so they cannot overlap labels. The TITLE LOGO row shows NATIVE without arrows when the cartridge has no alternate English artwork.

Fixed screens use a 38-byte header: palette 32 bytes, little-endian width/height four bytes, flags one byte and reserved one byte, followed by packed 4bpp pixels. Flag bit zero enables transparent color zero; bit one selects the fixed TMS9918 palette. Legacy flag-zero V9938 screens keep their contract. No PNG background or fixed screen is loaded by the game. Sprite atlases may use PNG.

`tools/kv2-extract/art/artkit.py` is an optional artist workflow, not the production extraction or a fidelity oracle. Inspect its supported formats before using it on a new palette-model header; do not assume a rendered-color round trip preserves native palette indices or alpha. Export from the selected validated generation's `Screens` directory, never the old mixed cache.

See [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md) for current paths and [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md) for evidence and limitations.


## MSX1 object background presentation

MSX1 always preserves the cartridge's original black tile-object backgrounds for items, soulstones, coffins and exit doors. The former OBJECT BG setting has been removed. Legacy saved transparent preferences are ignored.

This is an optional runtime presentation copy, not an extraction change. The reusable `SpriteBackdrop.RemoveConnectedBlack` removes border-connected black/transparent pixels and preserves enclosed black details. It only applies to eligible MSX1 object regions, including item-name aliases; actors, projectiles and terrain remain unchanged. Native atlas files and manifests are never rewritten. Baked coffin/door backgrounds are restored from the native mural and terrain before drawing the optional copies.

The former `KINGSVALLEY_MSX1_OBJECT_BG` diagnostic override is ignored. MSX1 retains its ten-pixel presentation letterbox.
