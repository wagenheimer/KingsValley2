"""High-Definition GIF generator for King's Valley 2 documentation.
Generates razor-sharp pixel-art GIFs at 2x scale (512x424) or 3x scale (768x636)
with adaptive palette and no color crush.
"""
import argparse
import glob
import os
from PIL import Image

def build_hd_gif(folder, out_path, every=2, scale=2, max_colors=128):
    files = sorted(glob.glob(os.path.join(folder, "frame_*.png")))[::every]
    if not files:
        print(f"[!] No frames found in {folder}")
        return False

    frames = []
    for path in files:
        im = Image.open(path).convert("RGB")
        if scale != 1:
            im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
        frames.append(im)

    # To avoid flicker while preserving rich MSX colors, combine sample images to build global palette
    sample_stride = max(1, len(frames) // 10)
    samples = frames[::sample_stride]
    
    # Create a composite strip of samples to generate an optimal global palette
    strip_w = sum(s.width for s in samples[:6])
    strip_h = samples[0].height
    palette_strip = Image.new("RGB", (strip_w, strip_h))
    curr_x = 0
    for s in samples[:6]:
        palette_strip.paste(s, (curr_x, 0))
        curr_x += s.width
        
    global_palette_img = palette_strip.quantize(colors=max_colors, method=Image.Quantize.MEDIANCUT)

    quantized_frames = []
    for f in frames:
        q = f.quantize(palette=global_palette_img, dither=Image.Dither.NONE)
        quantized_frames.append(q)

    delay = int(round(1000 / 60 * every))
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    quantized_frames[0].save(
        out_path,
        save_all=True,
        append_images=quantized_frames[1:],
        duration=delay,
        loop=0,
        optimize=True,
        disposal=2
    )
    size_kb = os.path.getsize(out_path) / 1024
    print(f"[+] HD GIF generated: {out_path} ({len(frames)} frames, {delay}ms, {scale}x scale, {size_kb:.1f} KB)")
    return True

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folder")
    parser.add_argument("out")
    parser.add_argument("--every", type=int, default=2)
    parser.add_argument("--scale", type=int, default=2)
    parser.add_argument("--colors", type=int, default=128)
    args = parser.parse_args()
    build_hd_gif(args.folder, args.out, args.every, args.scale, args.colors)
