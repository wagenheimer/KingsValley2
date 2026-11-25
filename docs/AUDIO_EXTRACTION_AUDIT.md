# Audio extraction audit — profile revision 5

Updated 2026-11-23. The production extractor runs the cartridge sound driver in process through MSXAudio. The game never launches or requires openMSX. It chooses an embedded profile by ROM SHA-1, and keeps MSX1/MSX2 packages separate.

## Reproduced and corrected

- **Jump appeared as 0:00:** the MSXScope duration formatter truncated fractional seconds. The final requested UI uses `mm:ss`; positive durations below one second display `00:01`. Millisecond/decimal formatting was an intermediate diagnostic presentation and is superseded.
- **Jump was actually silent with AUTO:** the automatic mixer classified channels with less than 0.4 seconds of activity as silent, then removed them. Original/Stereo played the same saved VGM correctly. AUTO now retains audible transients and accounts for notes held after the last write. Synthetic PSG/SCC regressions failed before the fix and pass afterwards.
- **Missing IDs:** the previous range excluded ID 32 (`play_start`) and ID 2 (an audible unused tone). Profiles now inspect the entire driver table, IDs 1..65. Stop command 1 and five unused silent entries remain in diagnostics, not the playable catalog.
- **Arbitrary effect cut:** ID 55 is a repeating effect used by `end_scr3`. It is now named `ending_danger`, with a measured driver cycle of 19/60 seconds and an initial 1/60 second prefix. It no longer has an arbitrary 12-second endpoint. The package and game playback support sustained effects until explicitly stopped.
- **MSXScope loops:** the player waited for the end of a multi-cycle recording instead of the declared loop end, and moved repeated-event timestamps backwards. It now splits playback at the actual loop boundary, preserves chip state and schedules repeated writes forwards. Regressions check successive cycles and keep the transport time within the track duration. Seeking also restores register telemetry.

## Extraction architecture

Each ID starts with a fresh machine. KV2 profiles map ROM banks 4/5/6, initialize at `6000`, submit commands at `6003` and tick only `6006` at 60 Hz. Mappings are restored before every call because the driver changes them. Three ticks consume the stop command before the requested sound starts. Initial PSG/SCC state is preserved.

The tested MSX2 patch uses an external SCC in primary slot 2. Its authentic 32-byte ROM routine at file offset `7F10` is copied to RAM `F87F`. The SCC capture handles mirrored enable/frequency registers and maps test/deformation writes separately from wave RAM.

`DriverStateObserver` is reusable. A profile declares activity bytes and RAM state ranges. For KV2 these are the eight 0x33-byte channel records at `E000..E197`, sound flags at `E1CB` and mixer state at `E1E3`. The driver clears each channel's first byte on stop. The state includes pointers, waits, envelopes, vibrato and repetition counters; a pointer alone cannot establish a loop.

The observer requires three identical complete state observations at equal intervals before accepting a cycle. Finite capture stops after all eight channels end, retaining mute writes and a short tail. Full KV2 extraction fails if an audible ID neither ends nor establishes a cycle before the safety limit. Timed-event heuristics remain available for other profiles without a state contract; KV2 revision 5 does not rely on them.

RAM completeness is a profile-specific contract, not a universal inference: future games need their own validated state ranges. This establishes the driver's control-flow period, not hardware-certified PCM phase equivalence.

## Packages and validation

- `<MSXMusic>/kings-valley-2/msx1/Audio/audio.msxaudio`
- `<MSXMusic>/kings-valley-2/msx2/Audio/audio.msxaudio`

Profiles require every known audible ID and all established repeating entries, including ID 55. Old revisions, mismatched ROMs and partial diagnostic packages do not satisfy the production cache. Publication is atomic. `capture-report.json` reports per-ID PCM peak, duration, silence and termination evidence. `playback-report.json` records the follow-up checks on saved effects in AUTO, MSXScope's live player and the game engine.

| Validated ROM | SHA-1 | Music | Effects | Diagnostic silent IDs |
|---|---|---:|---:|---|
| MSX1 | cfd872d005b7bd4cdd6e06c4c0162191f0b0415d | 11 | 48 | 1, 4, 47, 51, 52, 63 |
| MSX2 SCC patch | 0c7e0b618c72e7ca08bd756c3103b46e5c580c04 | 11 | 48 | 1, 4, 47, 51, 52, 63 |

All 48 saved effects in each variant produced nonzero audio in the AUTO offline renderer, MSXScope live renderer and game playback engine. Validation checks decoded PCM, not event count alone. It does not substitute for listening comparison or hardware validation. Other ROM revisions are not yet validated.

## Disassembly and names

Reference: [0xe1f/KingsValley2](https://github.com/0xe1f/KingsValley2), inspected commit `8ba9be9be9dd92de729c9f4e236b193ed3dc4676`. Driver structure is in `banks/banks_456.asm`; callers are in `banks_0.asm`, `banks_123.asm` and `banks_abc.asm`. No upstream game source or audio assets were imported into this project.

Names follow callers, not unverified comments. Examples: ID 15 is invoked by `life_over` on the route to Game Over despite its thunk's 'extra life' comment; ID 65 is invoked by `case_open`, so the existing `coffin_open` name is retained despite another misleading thunk comment. The extra life awarded by score uses ID 42.

Previously unidentified entries now include `puzzle`, `game_over`, `ending_final`, `coffin_pull`, `ending_passage_clear`, `ending_passage_enter`, `rock_roll_collapse`, `ending_danger`, `ending_key`, `ending_flash`, `ending_passage_land`, `ending_stamp`, `ending_count` and `secret_reveal`. Unused audible entries use explicit technical names: `unused_tone_02`, `unused_jingle`, `unused_click`. These are descriptive catalog names, not claims of official Konami track titles. Original numeric IDs stay stable.

## openMSX reference

Inspected [openMSX](https://github.com/openMSX/openMSX), commit `fcf77ffcf914d9083fb2652fb7a6ab161c90aa0a`: `RomKonamiSCC.cc`, `SCC.cc`, `Scheduler.hh`, `AY8910.cc` and `_vgmrecorder.tcl`. The source comparison informed mapper/register corrections and timing review. No openMSX executable was used for the revision-5 extraction or playback validation; it remains an optional external reference for future comparison, never a game dependency.

The upstream README assigns GPL to source files without another license notice; this comparison did not copy upstream source code.

## Verification of revision 5

Both local ROMs were re-extracted and both canonical packages now carry profile revision 5. All 48 effects per variant passed the three saved-file playback checks. MSXAudio: 155 tests passed, including optional integration checks that exercised both available local ROMs. King's Valley: 184 tests passed. Both solutions built successfully. The historical MSXScope diagnostic recording displayed `461 MS` for jump (before `mm:ss` was restored) and showed nonzero spectrum/scope output under AUTO. The real player's settings were restored after this check.

## Game playback introduction repair (2026-11-24)

The saved `stage_a` VGM is correctly mapped to native ID 5. Both stages 1 and 2 request it, matching the native two-stage music policy. However, `MsxAudioEngine.ChipSongStream` initialized both its event cursor and playback position at the loop start and silently applied the earlier register writes. For both supplied cartridges, this skipped the first 10.683333 seconds whenever the game enabled looping.

The shared MSXAudio engine now starts every new song at sample zero. It consumes the complete introduction with normal chip rendering and only jumps to the loop marker after reaching the loop end. Starting the same song again also replays the introduction. Extraction and package identities are unchanged; existing packages already contain the missing opening. Three regressions failed before the fix and passed afterwards: a distinct synthetic introduction/restart/continued-loop case and saved `stage_a` PCM comparisons with looping enabled/disabled for MSX1 and MSX2. All 161 MSXAudio tests pass.
