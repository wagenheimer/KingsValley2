# King's Valley II enhancement patches — FRS

This directory preserves the **complete, unmodified original ZIP** of FRS's
King's Valley II enhancement patches, version 1.1, copyright 2011 FRS.

- Author and source: https://frs.badcoffee.info/patches.html
- Original download: https://frs.badcoffee.info/files/KingsValley_II_EnhancementPatches.zip
- ZIP SHA-256: `9CF7DA18F79FA0778A7CFEE0971C434977CEE14E05AFB93B17D11A1A3B8393E4`
- Translation credits: Jazzin, BiFi, Vampier, pegase, Metalion, iamweasel2.
- Logo credits: SLotman, pegase.
- Full credits, instructions and legally controlling license: `README.TXT` inside the ZIP.

## Separate license

This archive is **not covered by this repository's open-source license**.
FRS permits free non-commercial use and redistribution only with every included
file together in the same compressed archive. Commercial use requires contacting
FRS. Distribution of patched ROMs is prohibited. Keep this ZIP intact when
redistributing the project or its builds. No cartridge ROM is included here.

## Automatic local translation

The game embeds the complete ZIP, verifies its SHA-256 and applies `KVAL2G.IPS`
in memory to a private copy of the user's supported Japanese Contest cartridge.
It works offline, including on first use. The selected ROM is never overwritten.
The full archive is also retained under `<MSXMusic>/.patches/FRS-KingsValleyII-1.1/`.

The locally validated recipe is deliberately exact:

| Cartridge | SHA-1 |
| --- | --- |
| User-supplied Japanese JoySNES revision | `4a157e6e217f6af4ab3dec5d7dd5188e5cef7954` |
| English working copy produced by this recipe | `e7b0a7b083d2f5bb12fb4ea6b2e822ee6330a15e` |

This differs from the original Gold revision listed in FRS's instructions
(`2e0436303648097e8e936c5467574aff5e3f1517`). The JoySNES combination was
validated locally: all 20 extracted stages remain identical, native English
menus render correctly, and all 65 audio commands were captured. It must not be
represented as the official prepatched Gold ROM. Other revisions require their
own validation. Set `KINGSVALLEY_CONTEST_LANGUAGE=jp` before extraction to retain
the Japanese presentation for diagnostics.

Graphics generations record the effective ROM hash; the extraction report
records source and translated identities. Campaign progress remains the same
20-stage Contest campaign. Existing Japanese generations are retained.
