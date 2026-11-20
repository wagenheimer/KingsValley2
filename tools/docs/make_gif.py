"""frame_*.png (docs/en/media/frames/<demo>) -> GIF animado.

Uso: python make_gif.py <pasta_de_frames> <saida.gif> [--every N] [--scale S] [--colors C]

Os frames sao capturados pelo jogo a 60 fps (KINGSVALLEY_CAPTURE_DIR); `--every` pega 1 a cada N
e o GIF sai em 60/N fps, que e a cadencia real do jogo.
"""
import argparse
import glob
import os

from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("out")
    ap.add_argument("--every", type=int, default=2)
    ap.add_argument("--scale", type=int, default=2)
    ap.add_argument("--colors", type=int, default=64)
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.folder, "frame_*.png")))[:: args.every]
    if not files:
        raise SystemExit(f"sem frames em {args.folder}")

    frames = []
    for path in files:
        im = Image.open(path).convert("RGB")
        if args.scale != 1:
            im = im.resize((im.width * args.scale, im.height * args.scale), Image.NEAREST)
        frames.append(im)

    # uma paleta unica (primeiro frame) evita o "pisca" de quantizacao frame a frame
    palette = frames[0].quantize(colors=args.colors)
    frames = [f.quantize(palette=palette, dither=Image.NONE) for f in frames]

    delay = int(round(1000 / 60 * args.every))
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    frames[0].save(args.out, save_all=True, append_images=frames[1:], duration=delay,
                   loop=0, optimize=True, disposal=2)
    size = os.path.getsize(args.out) / 1024
    print(f"{args.out}: {len(frames)} frames, {delay} ms, {size:.0f} KB")


if __name__ == "__main__":
    main()
