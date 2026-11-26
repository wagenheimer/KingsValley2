# ROM compatibility audit

Updated 2026-11-26. The goal is support for every identifiable King's Valley II
cartridge revision, without accepting unrelated files or inventing missing art.
This is a finite catalogue audit, not a claim that every future or private hack
has been found.

## Sources and acquisition

The [openMSX software database](https://raw.githubusercontent.com/openMSX/openMSX/master/share/softwaredb.xml)
contains fifteen King's Valley II ROM identities across MSX1, MSX2 and MSX2 Contest,
including hacks and bad dumps. Two project-validated combinations are additional
identities. A legacy boot-patch hash from the existing scanner is retained as pending.
The [machine-readable inventory](data/rom-revisions.json) records status, profile and
individual source URLs without including ROM bytes.

Its validation snapshot records fifteen complete graphics generations, one partial
generation, and full 65-command audio captures for eleven acquired revisions.
Baseline revisions without a new audio snapshot retain their earlier validation;
an absent snapshot field is not a new audio test result.

Acquisition indexes checked in this audit:

- [MSX1 ROM catalogue](https://download.file-hunter.com/Games/MSX1/ROM/): original, FRS and JoySNES.
- [MSX2 ROM catalogue](https://download.file-hunter.com/Games/MSX2/ROM/): original, alternate, Ulver, FRS, JoySNES and three Contest revisions.
- [Bad dump catalogue](https://download.file-hunter.com/Games/BadDump/): slot-2 conversions, Missing Text and the I/O-mapped conversion.
- [FRS's original patch page](https://frs.badcoffee.info/patches.html): enhancement descriptions and author distribution.

Fifteen catalogued images were acquired to an ignored local diagnostic corpus for
hashing and execution. No cartridge images, extracted game assets or patched ROMs
are added to source control. Filenames and archive IDs are descriptive; hashes
remain the identity check.

## Revision matrix

| Revision | ROM SHA-1 | Status |
| --- | --- | --- |
| MSX1 RC-760 original / GoodMSX | `cfd872d005b7bd4cdd6e06c4c0162191f0b0415d` | Supported |
| MSX1 ASCII8 + external SCC slot-2 conversion | `f3306e6f25d111da21ce66db3404f5f48acb25a1` | Supported |
| MSX1 FRS v1.1 | `cb612cbfdf79dabe55164b386ca956a3e187bef7` | Supported |
| MSX1 JoySNES | `7f6c5ec94d35981547a277c308ea9c2d2ab92e0f` | Supported |
| MSX2 RC-761 original / GoodMSX | `5ec8811254dc762c6852289a08acf22954404f0d` | Supported |
| MSX2 ASCII8 + external SCC slot-2 conversion | `f25628e474035e6bf0440b274c5df04f94c49fb8` | Supported |
| MSX2 Ulver crack | `b5acea41aeabb5daa4183953f800d47db0c39fb5` | Supported |
| MSX2 FRS v1.1 | `89d96d4b92d9a1c220cae0240cf0c00f310d14aa` | Supported |
| MSX2 alternate dump (3164) | `22704b82d159d963f1192d7a81810aa2be8b6042` | Supported |
| MSX2 JoySNES | `9ef68fecc91bf5066b9eb9a796412ab00e5746bf` | Supported |
| MSX2 Missing Text (damaged) | `fa750d3f622717fd1dfd0b2740fe69b49ba62ac8` | Partial only: source text is missing |
| MSX2 I/O-mapped conversion (6768) | `c4e138d533525d4b9064b2c265c4d2fdc5fa898d` | Pending: custom loader / I/O mapping |
| MSX2 Edit Contest original / GoodMSX | `2e0436303648097e8e936c5467574aff5e3f1517` | Supported |
| MSX2 Edit Contest FRS v1.1 (English) | `376b4785cab7d60cc083f0c03bd5e8ea2fee8205` | Supported |
| MSX2 Edit Contest JoySNES | `4a157e6e217f6af4ab3dec5d7dd5188e5cef7954` | Supported |
| MSX2 SCC slot patch (project baseline) | `0c7e0b618c72e7ca08bd756c3103b46e5c580c04` | Supported |
| MSX2 Contest JoySNES + FRS local output | `e7b0a7b083d2f5bb12fb4ea6b2e822ee6330a15e` | Supported |
| Legacy boot-patch identity (image not located) | `74eeaa99bff38b332f64e9e9f7775f834d1842e7` | Pending: source image unavailable |

## What support means

For a playable generation, the cartridge must produce all sixty original stages
or twenty Contest challenges, required binary screens, room/block data, mechanism
frames and atlas entries. Publication rechecks the exact source identity and
artifact hashes. Each hardware profile is independently selectable even when
several profiles belong to the same gameplay variant.

Audio is captured for all 65 driver commands. Complete captures contain 11 songs,
48 effects and six expected silent commands, with no capture-limit terminations.
This is execution/inventory evidence, not exhaustive listening or hardware testing.
The acquired revisions are never silently replaced with a different cartridge.

New audio is staged until that revision's graphics are ready. A partial extraction
keeps the previous playable audio and graphics pointer; its pending audio remains
available to that attempt's report and is not advertised to MSXScope as a published package.

### Revision-specific work

- **FRS MSX2:** separate boot adapter stops before its interrupt-dependent HALT and
  invokes the cartridge's own video initializer. The original fast runner is unchanged.
- **MSX1 JoySNES:** its patch replaces keyboard reading with a SNES-adapter protocol.
  Capture enters through native font/intro initialization instead of sending keyboard
  input to a missing peripheral. Actors, poses and room pixels still come from its ROM.
- **Slot-2 conversions:** use ASCII8 banking, external SCC slot 2 and each image's own
  ROM-to-RAM helper code. The graphics runner uses a private execution image that
  translates 28 measured mapper-write operands for the MSX2 conversion; source files,
  graphics bytes and manifest identities remain unchanged.
- **Contest English:** both Japanese revisions apply the complete bundled FRS archive
  offline. GoodMSX produces `376b4785cab7d60cc083f0c03bd5e8ea2fee8205`; JoySNES produces
  `e7b0a7b083d2f5bb12fb4ea6b2e822ee6330a15e`. Source and effective identities are reported.

## Remaining cases

**Missing Text:** audio and surviving graphics can be inspected, but the native cursor
and text data are absent. The extraction remains partial and cannot replace a complete
generation. Correct text cannot be recovered from absent bytes without another authentic
source or an independently verified repair patch.

**I/O conversion `c4e138...`:** lacks a normal AB cartridge header, reorganizes banks,
uses custom I/O ports and relocates data pointers. It needs a dedicated loader/mapping
recipe; the ordinary cartridge paths must not admit it as if it were an original.

**Legacy boot patch `74eeaa...`:** recognized by an older scanner constant, but not found
in the checked catalogue/corpus. Its recipe cannot be validated without its bytes.

A separate [MSX1 Edit Contest edition is documented by Generation MSX](https://generation-msx.nl/software/konami/kings-valley-ii-game-edit-contest/7552).
No verified image/hash for that edition was located in this audit; it is not assumed
to be the MSX2 Contest cartridge or mapped onto MSX2 assets.

The official MAME [MSX1](https://raw.githubusercontent.com/mamedev/mame/master/hash/msx1_cart.xml)
and [MSX2](https://raw.githubusercontent.com/mamedev/mame/master/hash/msx2_cart.xml)
software lists were also checked. Their three relevant images match the MSX1 original,
MSX2 original and MSX2 Contest hashes already listed here; they add no MSX1 Contest dump.
Catalogue quality labels differ: MAME marks the MSX2 original and Contest as `baddump`,
while openMSX labels those same hashes GoodMSX. Compatibility and extraction evidence
are recorded separately from either catalogue's quality label.

Disk conversions, compilations and newly modified private ROMs need their own discovery
and validation. The current recipes do not provide universal compatibility with arbitrary
files just because the filename contains King's Valley II.
