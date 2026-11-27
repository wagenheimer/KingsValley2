"""Arrange extracted cartridge WAVs without recording unrelated desktop audio."""
import argparse
import json
from pathlib import Path
import wave
import numpy as np

RATE = 48000


def read(path):
    with wave.open(str(path), "rb") as source:
        if source.getframerate() != RATE or source.getsampwidth() != 2:
            raise ValueError(f"Expected 48 kHz PCM16: {path}")
        channels = source.getnchannels()
        samples = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2")
    samples = samples.reshape(-1, channels).astype(np.float32) / 32768
    return np.repeat(samples, 2, axis=1) if channels == 1 else samples


def write(path, samples):
    peak = float(np.max(np.abs(samples)))
    if peak >= 1:
        raise ValueError(f"Audio clips before encoding: peak {peak}")
    with wave.open(str(path), "wb") as dest:
        dest.setnchannels(2)
        dest.setsampwidth(2)
        dest.setframerate(RATE)
        dest.writeframes((samples * 32767).astype("<i2").tobytes())
    return peak


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("edit", type=Path)
    parser.add_argument("--sources", type=Path)
    args = parser.parse_args()
    folder = args.edit.resolve().parent
    sources = args.sources or folder / "audio"
    data = json.loads(args.edit.read_text(encoding="utf-8-sig"))
    duration = sum(float(s["duration"]) for s in data["shots"])
    size = round(duration * RATE)
    music, effects = np.zeros((size, 2), np.float32), np.zeros((size, 2), np.float32)
    for entry in data["music"]:
        source = read(sources / (entry["id"] + ".wav"))
        begin, length = round(entry["at"]*RATE), round(entry["duration"]*RATE)
        offset = round(entry.get("start", 0)*RATE)
        segment = source[offset:offset+length].copy()
        if len(segment) != length:
            raise ValueError(f"Music cue exceeds source: {entry}")
        fade = min(round(.4*RATE), length//2)
        segment[:fade] *= np.linspace(0, 1, fade)[:, None]
        segment[-fade:] *= np.linspace(1, 0, fade)[:, None]
        music[begin:begin+length] += segment * entry.get("gain", 1.5)
    for entry in data.get("effects", []):
        segment = read(sources / (entry["id"] + ".wav")) * entry.get("gain", 1.5)
        if "duration" in entry:
            segment = segment[:round(entry["duration"]*RATE)]
        begin = round(entry["at"]*RATE)
        length = min(len(segment), size-begin)
        effects[begin:begin+length] += segment[:length]
    peaks = {"music": write(folder / "music.wav", music),
             "effects": write(folder / "effects.wav", effects)}
    peaks["premix"] = float(np.max(np.abs(music*.75 + effects)))
    write(folder / "mix.wav", music*.75 + effects)
    (folder / "audio-check.json").write_text(json.dumps(peaks, indent=2), encoding="utf-8")
    print(json.dumps(peaks, indent=2))


if __name__ == "__main__":
    main()
