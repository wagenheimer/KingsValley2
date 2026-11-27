# King's Valley II v1.1.3

## Editor graphics controls

- Switch compatible MSX1/MSX2 artwork directly in Inspector > Settings > Graphics while retaining the current document, room, edits and history.
- Keep Contest artwork confined to its own campaign; load another campaign through From Game.
- Fit the room map and Copy/Paste controls into shorter Inspector panels.

## Save-state isolation and capture diagnostics

- Save-state JSON and thumbnails now honor `KINGSVALLEY_USER_DATA`, consistently with profile storage.
- Persistence tests create a private temporary directory and verify isolation before writing or deleting a slot.
- Explicit background capture continues when the game loses focus and skips the Windows foreground-window request. Both `KINGSVALLEY_CAPTURE_BACKGROUND=1` and a capture directory are required; normal gameplay keeps its existing focus behavior.

## Illustrated documentation and trailer tools

- Expanded the README with six runtime GIFs, two screenshots and coverage of gameplay, MSX1/MSX2, Contest, the editor, portable levels, saves, audio and setup.
- Corrected debug-only shortcut documentation against the current implementation.
- Added reproducible runtime capture, README media generation, shot lists, audio arrangement/mastering, FilmCraft CLI editing and export verification tools.
- Defined a 3:40 horizontal trailer and a separate 0:45 portrait cut with English animated titles. Generated videos, source captures and native editing projects remain local build artifacts, not bundled game assets.

## Validation

`dotnet build KingsValley.slnx` passed with zero warnings and errors. The Core suite passed 324 tests, with 10 optional tests skipped and no failures. The trailer work additionally exercised actual editor painting, Undo/Redo, native theme changes, play-testing and portable document saving. The existing six-theme/full-stage editor and cartridge reconstruction behavior are retained.

No cartridge ROM or extracted game asset package is distributed. Supply a supported ROM. The bundled FRS enhancement patches retain their author's separate non-commercial license; see `third_party/FRS/README.md`.
