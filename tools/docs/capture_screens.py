import os
import sys
import subprocess
from PIL import Image

REPO_ROOT = r"E:\GreenSauceGames\KingsValley"
MEDIA_DIR = os.path.join(REPO_ROOT, "docs", "en", "media")
DESKTOP_PROJ = os.path.join(REPO_ROOT, "src", "KingsValley.Desktop")
TEMP_DIR = os.path.join(REPO_ROOT, "scratch", "doc_captures")

os.makedirs(TEMP_DIR, exist_ok=True)
os.makedirs(MEDIA_DIR, exist_ok=True)

def run_capture(name, env_vars, frame=80):
    env = os.environ.copy()
    temp_png = os.path.join(TEMP_DIR, f"{name}_raw.png")
    if os.path.exists(temp_png):
        os.remove(temp_png)
        
    env["KINGSVALLEY_SCREENSHOT"] = temp_png
    env["KINGSVALLEY_SCREENSHOT_FRAME"] = str(frame)
    env["KINGSVALLEY_WINDOW"] = "256x212"
    for k, v in env_vars.items():
        env[k] = str(v)
        
    print(f"[*] Capturing {name} (frame {frame})...")
    cmd = ["dotnet", "run", "--project", DESKTOP_PROJ, "--no-build"]
    proc = subprocess.run(cmd, env=env, cwd=DESKTOP_PROJ, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if not os.path.exists(temp_png):
        print(f"[!] Warning: {temp_png} was not generated! Returncode={proc.returncode}")
        stdout_tail = proc.stdout.decode('latin1', errors='replace')[-500:] if proc.stdout else ""
        stderr_tail = proc.stderr.decode('latin1', errors='replace')[-500:] if proc.stderr else ""
        print("Stdout tail:", stdout_tail)
        print("Stderr tail:", stderr_tail)
        return False
    return temp_png

def upscale_image(src_path, dst_path, scale=3):
    img = Image.open(src_path).convert("RGB")
    scaled = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    scaled.save(dst_path)
    print(f"[+] Saved {dst_path} ({scaled.width}x{scaled.height})")

def main():
    targets = [
        # 1. System Menu (ESC) during gameplay
        {
            "name": "sysmenu",
            "frame": 80,
            "env": {
                "KINGSVALLEY_START_SCENE": "level",
                "KINGSVALLEY_LEVEL": "1",
                "KINGSVALLEY_SKIP_LEVELSTART": "1",
                "KINGSVALLEY_SYSMENU": "1"
            }
        },
        # 2. MSX2 Gameplay Stage 1
        {
            "name": "msx2_stage01",
            "frame": 60,
            "env": {
                "KINGSVALLEY_START_SCENE": "level",
                "KINGSVALLEY_LEVEL": "1",
                "KINGSVALLEY_GFX": "msx2",
                "KINGSVALLEY_SKIP_LEVELSTART": "1"
            }
        },
        # 3. MSX1 Gameplay Stage 1
        {
            "name": "msx1_stage01",
            "frame": 60,
            "env": {
                "KINGSVALLEY_START_SCENE": "level",
                "KINGSVALLEY_LEVEL": "1",
                "KINGSVALLEY_GFX": "msx1",
                "KINGSVALLEY_SKIP_LEVELSTART": "1"
            }
        },
        # 4. Title Screen
        {
            "name": "title",
            "frame": 400,
            "env": {
                "KINGSVALLEY_START_SCENE": "splash"
            }
        },
        # 5. World Map Tour
        {
            "name": "worldmap_tour",
            "frame": 60,
            "env": {
                "KINGSVALLEY_START_SCENE": "worldmap"
            }
        },
        # 6. Stage Editor
        {
            "name": "editor",
            "frame": 40,
            "env": {
                "KINGSVALLEY_START_SCENE": "editor"
            }
        },
        # 7. Puzzle Minigame
        {
            "name": "puzzle",
            "frame": 40,
            "env": {
                "KINGSVALLEY_START_SCENE": "puzzle"
            }
        },
        # 8. Sound Test
        {
            "name": "soundtest",
            "frame": 40,
            "env": {
                "KINGSVALLEY_START_SCENE": "soundtest"
            }
        }
    ]

    for t in targets:
        raw_png = run_capture(t["name"], t["env"], t["frame"])
        if raw_png:
            out_png = os.path.join(MEDIA_DIR, f"{t['name']}.png")
            upscale_image(raw_png, out_png, scale=3)

    # Now create the side-by-side comparison MSX2 vs MSX1
    msx2_raw = os.path.join(TEMP_DIR, "msx2_stage01_raw.png")
    msx1_raw = os.path.join(TEMP_DIR, "msx1_stage01_raw.png")
    if os.path.exists(msx2_raw) and os.path.exists(msx1_raw):
        create_comparison(msx2_raw, msx1_raw, os.path.join(MEDIA_DIR, "msx_comparison.png"))

def create_comparison(msx2_path, msx1_path, out_path):
    im2 = Image.open(msx2_path).convert("RGB")
    im1 = Image.open(msx1_path).convert("RGB")
    
    # Scale both to 2x or 3x
    scale = 3
    w2, h2 = im2.width * scale, im2.height * scale
    w1, h1 = im1.width * scale, im1.height * scale
    
    im2_scaled = im2.resize((w2, h2), Image.NEAREST)
    im1_scaled = im1.resize((w1, h1), Image.NEAREST)
    
    gap = 24
    comp_w = w2 + w1 + gap
    comp_h = max(h2, h1)
    
    comp = Image.new("RGB", (comp_w, comp_h), (20, 24, 30))
    comp.paste(im2_scaled, (0, 0))
    comp.paste(im1_scaled, (w2 + gap, 0))
    
    comp.save(out_path)
    print(f"[+] Saved comparison: {out_path} ({comp_w}x{comp_h})")

if __name__ == "__main__":
    main()
