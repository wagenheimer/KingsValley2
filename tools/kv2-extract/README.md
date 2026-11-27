# kv2-extract — optional research helpers

The production graphics and audio extraction is the in-process C# pipeline. See
[GraphicsAudit](../GraphicsAudit/README.md) and [ROM_EXTRACTION.md](../../docs/ROM_EXTRACTION.md).
It needs only a user-supplied cartridge: no emulator, Python, GPU or game window.

The Python and openMSX Tcl scripts in this folder are the historical research toolkit used to decode
cartridge routines and to compare evidence during development. They are not required to build, run
or play the game. They assume a local `work/` directory (ignored by git) holding ROM dumps, an
emulator and reference captures.

## Rules

- Never commit ROMs, extracted art or audio, VRAM/RAM dumps or emulator captures.
- Treat dumped numbers as evidence, not configuration: cartridge identity and bank mapping govern
  every address.
- Use the production extractor for anything the game consumes at runtime.

## Folders

| Folder | Contents |
| --- | --- |
| `art/` | tile, sprite and screen research scripts |
| `levels/` | stage table decoding |
| `emulator/` | openMSX Tcl recipes for diagnostics and reference captures |
| `simtrace/` | tick-by-tick comparison against the port |
