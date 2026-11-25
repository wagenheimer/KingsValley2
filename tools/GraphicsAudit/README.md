# GraphicsAudit

Standalone graphics inspection and extraction using the game's in-process cartridge recipes. No openMSX installation, Python, GPU, game window or companion cartridge is required.

```powershell
dotnet run --project tools/GraphicsAudit -- extract <cartridge.rom> <collection-root> 60
dotnet run --project tools/GraphicsAudit -- extract <cartridge.rom> <temporary-collection-root> 1
dotnet run --project tools/GraphicsAudit -- verify <generation-directory> msx1
dotnet run --project tools/GraphicsAudit -- inspect <msx1-cartridge.rom>
dotnet run --project tools/GraphicsAudit -- screens <msx1-cartridge.rom> <diagnostic-root>
```

`extract` validates the cartridge fingerprint, writes one isolated attempt, validates its inventory, and generates its offline report. If a matching audio package already exists in the specified collection, its previews are included; graphics extraction does not modify that package. A stage limit below 60 is diagnostic and cannot replace a complete generation.

For `extract`/`verify`, exit code `0` means a complete generation passed validation; `1` means incomplete/invalid; `2` means invalid command usage. Inspect the JSON summary and [audit](../../docs/GRAPHICS_EXTRACTION_AUDIT.md) for the current inventory and fidelity limitations. `inspect` and `screens` return `0` when the requested diagnostic finishes; that does not mean a full graphics generation is ready.

`screens` executes the supplemental native MSX1 UI recipes in a new `screen-probe-<id>` directory and writes a diagnostic report. It never updates `current.json`. It is useful for reviewing text, palette state, native sprite composition and clipping before running a complete extraction.

Outputs:

```text
<collection-root>/kings-valley-2/<msx1|msx2>/
  Graphics/
    .staging/<job-id>/
    attempts/<id>/             # preserved partial/cancelled attempts
    generations/<id>/          # validated complete generations
    current.json               # atomically updated only for complete output
  Report/
    index.html
    report.json
    media/                     # optional audio previews
```

The old `MSXMusic/KingsValley2` cache is never imported. Repeated runs create new attempts; delete only a deliberately chosen temporary collection when finished. Keep the whole `Report` directory when copying an offline report.
