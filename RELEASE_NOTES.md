# King's Valley II v1.1.2

## A full desktop level editor

- Framed Toolbox, native tile/enemy palettes, Inspector, room map and compact view controls.
- Edit directly in Full stage: paint, erase and place objects without entering isolated room view.
- Cursor-anchored zoom, Space + drag or middle-button panning, Fit Stage and clipped rendering.
- Continuous brush strokes across room borders with one-step Undo/Redo. Selection, fills and individual stamps remain room-bounded.
- Play-test and return with the document, undo history, view mode, zoom and camera position retained.
- From Game creates editable copies of installed MSX1/MSX2 Original stages and the twenty Contest challenges.

## Portable levels and native artwork

- Open and Save As portable `.kv2level` documents with cartridge identity, validated stage data and protected atomic writes.
- Retained ten local slots, migration of legacy slots without overwriting existing copies, unsaved-change confirmation and periodic recovery.
- Native MSX1 editor room assembly fixes the EDIT crash and supports edited/new rooms without borrowing MSX2 graphics.
- Complete native backgrounds and animation in preview and play-test. Theme changes use valid cartridge decoration layouts instead of interpreting another theme's block codes.
- Restored full-height decorative columns and corrected MSX1 theme/common graphics loading during extraction.
- Corrected Guard/Rock poses and readable enemy palette rows.
- Fixed Windows Open/Save dialog structure marshalling and Unicode path handling.

## Maximized-window performance and diagnostics

The editor now renders UI text and controls in physical pixels with Gum camera zoom one. This fixes repeated high-resolution font rasterization that caused approximately 1.5-second Draw stalls and multi-gigabyte temporary allocations after room changes. Gameplay retains its existing font rendering contract.

Automatic, bounded background-written performance logs include input actions, UI construction, room assembly, drawing, frame intervals, allocations and GC counts. Logs are stored in the application's user data `Diagnostics` folder.

## Validation and known limits

The local suite passed 326 tests with 8 optional tests skipped. Validation covers all 140 installed stage templates, native file-dialog ABI/buffer handling, document saves, camera picking, zoom/pan and stroke routing. Isolated runtime probes exercised cross-room Undo/Redo and actual Test/return cycles in MSX1, MSX2 and Contest; maximized/4K captures were reviewed. Automated probes do not replace manual mouse-gesture acceptance.

- MSX1 graphics require extraction again for profile revision 4. Native theme tables, complete decorations and eye/torch animations are now captured independently for all six themes. Previously extracted immutable generations are preserved.
- Full-stage selections and flood fills operate within one room.
- Native file dialogs on Linux/macOS have not received hands-on validation here.

## Packages and credits

Windows x64, Linux x64, macOS Intel and macOS Apple Silicon packages contain the application and dependencies. No cartridge ROM or extracted game asset package is distributed. Supply a supported ROM; extraction does not require an external openMSX installation or MSX BIOS.

The bundled FRS enhancement patches v1.1 retain their author's separate non-commercial license. Keep the original archive intact; commercial use requires FRS's permission and patched ROMs must not be redistributed. See `third_party/FRS/README.md` and the original `README.TXT` inside the archive.

Windows runtime behavior was checked locally. GitHub Actions packaging on other operating systems does not establish hands-on gameplay validation.
