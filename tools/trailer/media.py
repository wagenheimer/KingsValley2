"""Build README media exclusively from recorded runtime takes (requires FFmpeg)."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
GIFS = {
    "action": [("action", 0, 6)],
    "tools": [("dig", 0, 5), ("knife", 0, 3)],
    "msx": [("action", 0, 4), ("msx1", 0, 4)],
    "contest": [("contest", 0, 6)],
    "editor": [("editor_edit", 0, 6)],
    "test": [("editor_theme", 4, 1), ("editor_test", 2, 4), ("editor_return", 0, 1)],
}
STILLS = {"options": ("options", 2), "saves": ("saves", 2)}
LIMIT = 5_000_000


def run(args):
    subprocess.run([str(a) for a in args], check=True)


def probe(path):
    data = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_streams", "-show_format",
        "-of", "json", str(path)], text=True))
    stream = next(s for s in data["streams"] if s["codec_type"] == "video")
    duration = data["format"].get("duration", stream.get("duration"))
    if duration is None:
        raise RuntimeError(f"No measurable duration: {path}")
    return stream["width"], stream["height"], float(duration)


def checked_source(sources, name, end):
    path = sources / f"{name}.mkv"
    width, height, duration = probe(path)
    if duration + 0.001 < end:
        raise RuntimeError(f"Take {name} is too short: {duration:.3f}s; needs {end:.3f}s")
    return path, width, height


def make_gif(name, sources, output):
    segments = GIFS[name]
    args = ["ffmpeg", "-v", "error", "-y"]
    filters = []
    expected_size = None
    for index, (take, start, length) in enumerate(segments):
        path, width, height = checked_source(sources, take, start + length)
        # Gameplay uses exact integer enlargement. Desktop UI uses a half-size preview.
        size = (width // 2, height // 2) if width > 512 else (width * 2, height * 2)
        if expected_size is not None and size != expected_size:
            raise RuntimeError(f"Cannot concatenate different frame sizes for {name}")
        expected_size = size
        args += ["-i", path]
        filters.append(f"[{index}:v]trim=start={start}:duration={length},setpts=PTS-STARTPTS,"
                       f"fps=15,scale={size[0]}:{size[1]}:flags=neighbor,setsar=1[v{index}]")
    labels = "".join(f"[v{i}]" for i in range(len(segments)))
    filters.append(f"{labels}concat=n={len(segments)}:v=1:a=0,split[a][b]")
    filters.append("[a]palettegen=stats_mode=diff[p]")
    filters.append("[b][p]paletteuse=dither=none:diff_mode=rectangle[out]")
    destination = output / f"showcase_{name}.gif"
    with tempfile.TemporaryDirectory(dir=output) as temporary:
        candidate = Path(temporary) / destination.name
        run(args + ["-filter_complex", ";".join(filters), "-map", "[out]",
                    "-an", "-loop", "0", candidate])
        if candidate.stat().st_size >= LIMIT:
            raise RuntimeError(f"{destination.name} exceeds the 5 MB budget; choose a shorter real take")
        candidate.replace(destination)
    print(f"Created {destination} ({destination.stat().st_size:,} bytes)", flush=True)


def make_still(name, sources, output):
    take, time = STILLS[name]
    path, width, height = checked_source(sources, take, time + 1 / 60)
    destination = output / f"showcase_{name}.png"
    with tempfile.TemporaryDirectory(dir=output) as temporary:
        candidate = Path(temporary) / destination.name
        run(["ffmpeg", "-v", "error", "-y", "-i", path, "-ss", time,
             "-frames:v", "1", "-vf", f"scale={width * 3}:{height * 3}:flags=neighbor",
             "-update", "1", candidate])
        candidate.replace(destination)
    print(f"Created {destination}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", type=Path, default=ROOT / "artifacts/trailer/clips")
    parser.add_argument("--output", type=Path, default=ROOT / "docs/en/media")
    parser.add_argument("--only", nargs="+", choices=[*GIFS, *STILLS],
                        help="Generate an available subset; missing or short sources fail explicitly")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for name in args.only or [*GIFS, *STILLS]:
        (make_gif if name in GIFS else make_still)(name, args.sources, args.output)


if __name__ == "__main__":
    main()
