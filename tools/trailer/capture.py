"""Capture genuine runtime frames and preserve them as lossless FFV1 clips."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]


def capture(take, output):
    name = take["id"]
    frames = output / "frames" / name
    frames.mkdir(parents=True, exist_ok=True)
    if any(frames.glob("frame_*.png")):
        raise RuntimeError(f"Frame directory already contains a take: {frames}")
    env = {k: v for k, v in os.environ.items() if not k.startswith("KINGSVALLEY_")}
    env.update({"KINGSVALLEY_START_SCENE": "level", "KINGSVALLEY_GFX": "msx2",
                "KINGSVALLEY_WINDOW": "256x212", "KINGSVALLEY_FAST_BOOT": "1",
                "KINGSVALLEY_SKIP_LEVELSTART": "1", "KINGSVALLEY_TITLE_LANG": "en",
                "KINGSVALLEY_USER_DATA": str(output / "user-data"),
                "KINGSVALLEY_CAPTURE_BACKGROUND": "1", "KINGSVALLEY_IGNORE_INPUT": "1",
                "KINGSVALLEY_CAPTURE_DIR": str(frames),
                "KINGSVALLEY_CAPTURE_FROM": str(take.get("from", 0)),
                "KINGSVALLEY_CAPTURE_TO": str(take.get("from", 0) + round(take["seconds"] * 60) - 1)})
    env.update(take.get("env", {}))
    for key in ("KINGSVALLEY_INPUT",):
        if key in env:
            env[key] = str((ROOT / env[key]).resolve())
    exe = ROOT / "src/KingsValley.Desktop/bin/Debug/net10.0/KingsValley2.exe"
    started = time.time()
    print(f"Recording {name}: {take['seconds']} seconds", flush=True)
    with (output / f"{name}.log").open("w", encoding="utf-8") as log:
        subprocess.run([str(exe)], cwd=exe.parent, env=env, stdout=log,
                       stderr=subprocess.STDOUT, timeout=take["seconds"] * 5 + 90, check=True)
    images = sorted(frames.glob("frame_*.png"))
    expected = round(take["seconds"] * 60)
    if len(images) != expected:
        raise RuntimeError(f"{name}: expected {expected} frames, got {len(images)}; see log")
    clip = output / "clips" / f"{name}.mkv"
    clip.parent.mkdir(exist_ok=True)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                    "-framerate", "60", "-start_number", str(take.get("from", 0)),
                    "-i", str(frames / "frame_%04d.png"), "-c:v", "ffv1", "-level", "3",
                    "-pix_fmt", "bgr0", str(clip)], check=True)
    record = {**take, "frames": len(images), "clip": str(clip.relative_to(output)),
              "wall_seconds": round(time.time()-started, 2), "environment": env}
    # Store only task-specific overrides, never the parent process environment.
    record["environment"] = {k: v for k, v in env.items() if k.startswith("KINGSVALLEY_")}
    (output / f"{name}.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
    print(f"Saved lossless take: {clip}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--only", nargs="*")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/trailer")
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    for take in json.loads(args.manifest.read_text(encoding="utf-8"))["takes"]:
        if args.only and take["id"] not in args.only:
            continue
        capture(take, args.output)


if __name__ == "__main__":
    main()
