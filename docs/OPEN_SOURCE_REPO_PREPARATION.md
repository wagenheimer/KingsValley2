# Public repository preparation

Updated 2026-11-24. This replaces the old mixed-cache and bundled-cartridge-art proposal.

The public distribution should contain original source code, documentation, extraction tools and synthetic test fixtures. Users provide their own supported cartridge locally. Do not include ROMs, extracted art/audio, VRAM or RAM dumps, generated galleries containing cartridge content, or private emulator work directories. This packaging policy is not a legal determination about any particular distribution.

## Extraction and first run

Use the per-cartridge pipeline documented in [EXTRACTION_HANDOFF.md](EXTRACTION_HANDOFF.md). The canonical root is `<MSXMusic>/kings-valley-2/<msx1|msx2>/`; graphics publication requires a matching complete manifest. Neither legacy AppData caches nor the old mixed KingsValley2 folder are adopted. A file's existence and a generated fallback atlas do not establish playability.

Both supported cartridges have their own audio packages. Both validated cartridges have a complete implemented required graphics inventory; fidelity limitations are tracked in [GRAPHICS_EXTRACTION_AUDIT.md](GRAPHICS_EXTRACTION_AUDIT.md). Do not ship MSX2-derived artwork to repair an MSX1 extraction.

## Review before publication

- Inspect tracked files and history, not only ignore rules. Private `Content/Rom`, `Content/RomMsx1` and `tools/kv2-extract/work` material must not enter the public history.
- Review dependencies and third-party notices. Consultation of openMSX source does not authorize removing its license or copying it into differently licensed code.
- Build and test from a clean source checkout without private bundles. Then exercise first run using a locally supplied supported ROM.
- Validate per-flavor isolation, failed/cancelled attempts, immutable publication and the English offline report.
- Record missing fidelity checks and unsupported cartridge revisions explicitly.

Keep this review current until the repository is published: tracked files, history, dependency notices,
a clean-checkout build and a first run with a locally supplied supported cartridge.
