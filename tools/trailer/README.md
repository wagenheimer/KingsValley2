# Trailer renderer

Requirements: Python 3 with Pillow and NumPy, FFmpeg/FFprobe on PATH, and FilmCraft CLI for native editing. Install the Python dependencies with `python -m pip install Pillow numpy`. The native title treatment uses the installed Segoe UI family.

## Primary workflow: FilmCraft CLI

The selected editing application is FilmCraft 0.20.1. `filmcraft.py` creates an editable native `.fcproj` exclusively through documented CLI commands, saves a command/result log, and renders a real preview. It never overwrites an existing project. Choose a new output directory for each revision.

```powershell
python tools/trailer/filmcraft.py --edit tools/trailer/edit.json --pilot 3 --output artifacts/trailer/filmcraft-review
python tools/trailer/filmcraft.py --edit tools/trailer/edit.json --music artifacts/trailer/music.wav --effects artifacts/trailer/effects.wav --output artifacts/trailer/filmcraft-full --export
python tools/trailer/filmcraft.py --vertical --edit tools/trailer/edit-portrait.json --music artifacts/trailer/portrait/music.wav --effects artifacts/trailer/portrait/effects.wav --output artifacts/trailer/filmcraft-shorts --export
```

Without `--edit`, the original three-shot 15-second impact pilot remains the default. Full landscape edits must total 220 seconds and portrait edits 45 seconds. `--pilot` truncates the approved edit for review. `--clean` omits editorial graphics. PNG sources remain in `artifacts/trailer/frames/<clip>/frame*.png`; preserve these for alternate cuts. The editor needs its actual full-resolution PNG capture sequence too.

FilmCraft cannot decode FFV1 in this installation. Native gameplay PNGs are enlarged losslessly by exactly 4× with Pillow nearest-neighbor resampling. Each clip is prepared and imported once per project, then reused across shots. The decoder's measured image-sequence rate is corrected through `clip.speedDuration` with frame sampling; source offsets are converted by the same ratio. This restores every original 60 fps frame without interpolation. No source pixels are synthesized. Editor footage stays native in landscape and is fitted in portrait.

Gold titles animate through a 120→108→100 scale entrance, position motion and opacity. `--glow` optionally adds a brief 25-intensity/radius-8 FilmCraft glow to title graphics only. Subtitles are independent full text layers. Audio WAVs are placed on separate native tracks; supply prebalanced synchronized mixes. Export uses the actual FilmCraft H.264 exporter at 60 fps and quality 100, requesting a 40 Mbps VBR target. Actual bitrate depends on the footage and is recorded in each exported file. This is a high-quality lossy delivery export, not a lossless master. Keep PNGs and the editable project. Review video duration, motion, title wrapping and soundtrack before delivery.

## Final delivery and verification

Final MP4s are placed in `artifacts/trailer/FINAL/`. Editable delivery projects are in `delivery-horizontal`, `delivery-portrait` and `delivery-clean`. Keep `final-horizontal/sources` and `final-portrait/sources`: the native projects reference those PNG sequences. Original FFV1 recordings remain in `artifacts/trailer/clips/`.

The delivered projects disable the full-frame `wonder_glow` filter. FilmCraft 0.20.1 evaluated it even at zero intensity; disabling it substantially reduced render time. Gold fill, native glyph shadow and animated scale/position/opacity remain. The optional `--glow` switch is available for experiments, but is not recommended for these exports. Automatic GPU composition did not improve the measured test, so delivery uses the default compositor.

`audio.py` assembles extracted cartridge music and selected effects into original PCM tracks and a balanced `mix.wav`. These are editorial arrangements, not a live desktop audio recording. The native project attenuates music by 2.5 dB. The editor history demonstration is deliberately cut at 2?; gameplay remains at its recorded speed. `master_audio.py` then masters the original PCM mix toward -16 LUFS / -1.5 dBTP and muxes AAC at 320 kbps while copying the FilmCraft video stream unchanged. This does not re-encode video or transcode the native AAC audio a second time.

```powershell
python tools/trailer/audio.py artifacts/trailer/edit.json
python tools/trailer/audio.py artifacts/trailer/portrait/edit.json --sources artifacts/trailer/audio
python tools/trailer/master_audio.py artifacts/trailer/delivery-horizontal/trailer.mp4 artifacts/trailer/mix.wav artifacts/trailer/FINAL/Kings-Valley-II-Trailer-1080p60.mp4
python tools/trailer/verify.py artifacts/trailer/FINAL/Kings-Valley-II-Trailer-1080p60.mp4 --edit tools/trailer/edit.json
```

`verify.py` checks dimensions, duration, 60 fps, audio format, full decode integrity and samples every shot. Local HTML/PNG README previews and English production reports live under `artifacts/trailer/`. Technical checks and sampled frames do not establish human approval of the complete edit or soundtrack. Publishing is a separate action.

Unattended capture uses `KINGSVALLEY_CAPTURE_BACKGROUND=1` together with `KINGSVALLEY_CAPTURE_DIR`; both are required to bypass focus-loss pausing. Normal gameplay keeps its existing focus behavior. Captures and tests must set `KINGSVALLEY_USER_DATA` to an isolated directory; save-state storage now honors that override. The persistence test creates its own temporary root and checks isolation before writing or deleting a slot.

## FFmpeg fallback

For TikTok and Shorts, `--vertical` renders a separate edit totaling exactly 45 seconds at 1080×1920. Reuse the preserved lossless source recordings. Native gameplay remains a complete 1024×848 room at exact 4× scaling, with titles above and subtitles below; no room cropping or stretching occurs. Portrait titles should stay within three short lines (24-character wrap); subtitles should stay within five lines (58-character wrap). Editor footage is supported by fitting it within 1024×700, but gameplay is preferred for mobile readability. `--thumbnail` provides a finished-frame PNG preview. Supply a separately synchronized 45-second effects/music track.

```powershell
python tools/trailer/render.py --vertical --edit artifacts/trailer/edit-portrait.json --music artifacts/trailer/portrait/music.wav --effects artifacts/trailer/portrait/effects.wav --output artifacts/trailer/shorts.mp4 --thumbnail
```

`render.py` assembles the approved 220-second English trailer from actual lossless recordings. It requires Python 3, FFmpeg with libx264 and libass, and FFprobe on PATH. Segoe UI must be installed for the intended typography.

Place FFV1 recordings in `artifacts/trailer/clips/<id>.mkv`, a complete soundtrack in `artifacts/trailer/music.wav`, and the edit in `artifacts/trailer/edit.json`:

```json
[
  {"clip":"opening","start":0,"duration":20,"section":"King's Valley II","title":"An ancient challenge","subtitle":"A faithful return to the labyrinth."},
  {"clip":"gameplay","start":0,"duration":200,"section":"Explore","title":"Every room holds a secret","subtitle":"Real gameplay captured from the port."}
]
```

Replace the example with the approved shot list. Durations must sum to 220 seconds. `start` and `duration` are seconds. Clip paths with extensions resolve relative to the edit; bare IDs resolve beneath `clips/`. Each source must contain its entire requested range, even during a pilot. Short footage is rejected, never frozen or looped.

```powershell
python tools/trailer/render.py --pilot 20 --output artifacts/trailer/pilot.mp4 --thumbnail
python tools/trailer/render.py --output artifacts/trailer/trailer.mp4 --thumbnail
python tools/trailer/render.py --clean --output artifacts/trailer/trailer-clean-lossy-master.mp4
python tools/trailer/render.py --music artifacts/trailer/music.wav --effects artifacts/trailer/effects.wav --output artifacts/trailer/trailer.mp4
python tools/trailer/render.py --pilot 20 --quality-check --output artifacts/trailer/quality-pilot.mp4
```

Gameplay must be native 256×212 or 256×192. It is enlarged exactly four times with nearest-neighbor sampling, surrounded by dark negative space and a cream/gold editorial panel. Editor footage retains its native dimensions when it fits the 1920×1080 canvas; larger footage is fitted with Lanczos. Titles use a restrained fade. Editor titles occupy a dark lower strip; record editor framing with that strip in mind. Clean output removes all text and rules while preserving source framing.

The final output is 1920×1080 at 60 fps, H.264 CRF 14 (clean CRF 12), yuv420p with BT.709 signaling. Each shot is encoded once; final concatenation copies the video stream. These are high-quality **lossy** masters. Preserve FFV1 source recordings as the lossless evidence. Intermediate files and subtitle sources are retained beside the output in `<output-stem>-work/`.

Source-clip audio is omitted. The supplied music WAV provides the soundtrack with a default 0.75 gain and entrance/exit fades. `--effects path.wav` adds a complete synchronized effects recording at gain 1.0. Adjust with `--music-gain` and `--effects-gain`. Mixing disables automatic normalization and applies a 0.95 peak limiter without automatic makeup gain. Missing soundtrack and effects produces a video with no audio stream and an explicit console message. Short audio tracks are rejected. `--music path.wav` overrides the default. Review the final audio by listening; no loudness or perceptual-quality claim is implied.

`--thumbnail` extracts a real frame from the finished output. `--quality-check` compares a representative decoded frame from each shot against an uncompressed PNG of the same composition and prints PSNR. This measures compression, not cartridge fidelity or artistic acceptance. Inspect the pilot at native display size, especially sprite edges, motion, title wrapping, editor coverage, and audio, before rendering the full edit. Subprocess commands use argument arrays and never invoke a shell.
