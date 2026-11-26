# King's Valley II v1.1.0

## More cartridges, independent native extraction

- Fifteen ROM identities have complete graphics generations, up from five. Added original GoodMSX, JoySNES, Ulver, alternate MSX2, official Contest and ASCII8/external-SCC conversions.
- Separate hardware/audio profiles execute each cartridge's own driver. Updated discovery, package validation and generation publication support several hardware recipes within one edition.
- Added native boot handling for FRS MSX2 and native initialization for the MSX1 JoySNES revision, without changing the original fast MSX2 runner.
- Audio is staged until matching graphics are ready. A partial extraction preserves the previous playable audio and graphics generation.

## Edit Contest and offline English translation

- Twenty independent Contest challenges, four publication awards, five authors per publication and six selectable visual themes.
- Native title, publication, author and SCREEN SELECT screens, native completion flow, independent progress/high scores and save-state slots.
- Automatic offline FRS English translation for both original GoodMSX and JoySNES Contest sources. Original ROMs are preserved; exact source/output hashes are checked separately.

## Setup, UI and gameplay

- Clear BROWSE / EXTRACT SELECTED / PLAY flow, ROM discovery beside the executable and a paginated ROM GUIDE with exact hashes and recommendations.
- Revised system menu spacing and graphics selection. MSX1 uses its native object backdrop by default.
- Golden ladders follow their measured use counters, disappear after use and retain their state in saves.
- Rewritten README with five Contest runtime screenshots, an eighteen-identity compatibility matrix, source links and explicit evidence/limitations.

## Validation and remaining cases

The local corpus checks exact hardware selection, all 60 original or 20 Contest stage definitions, source immutability, both English patch outputs and unchanged Contest stages. Full 65-command captures for eleven acquired revisions produced 59 audible entries, six expected silent commands and no capture-limit terminations. Native graphics inventory checks are not exhaustive gameplay or listening acceptance.

- **Missing Text:** recognized for partial extraction; absent source text is not invented and the partial generation is not published for play.
- **Custom I/O conversion:** identified, but its custom loader/mapping is still pending.
- **Legacy boot-patch hash:** source image not located in the audited corpus.
- **MSX1 Contest:** separately documented edition; no verified dump/hash located in this audit.

## Packages and credits

Windows x64, Linux x64, macOS Intel and macOS Apple Silicon packages contain the application, dependencies and the complete original FRS patch archive. No cartridge ROM or extracted game asset package is distributed. Supply a supported ROM; extraction needs no external openMSX installation or MSX BIOS.

The bundled FRS enhancement patches v1.1 retain their author's separate **non-commercial license**. Keep the original archive intact; commercial use requires FRS's permission and patched ROMs must not be redistributed. See `third_party/FRS/README.md` and the original `README.TXT` inside the archive for credits and terms.

Windows runtime captures were reviewed locally. Other platforms are packaged by GitHub Actions; packaging and signing do not establish hands-on gameplay validation.
