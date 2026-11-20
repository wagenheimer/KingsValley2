# King's Valley II: The Seal of El Giza (1988) — Modern C# Port & Remake

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![MonoGame](https://img.shields.io/badge/MonoGame-3.8.2-red.svg)](https://monogame.net/)
[![Notarized: macOS](https://img.shields.io/badge/macOS-Notarized%20Developer%20ID-brightgreen.svg)]()

Welcome to the official **v1.0.0** release of **King's Valley II: The Seal of El Giza**, an authentic, cycle-accurate, open-source remake of Konami's legendary 1988 MSX/MSX2 puzzle-platforming masterpiece, written from scratch in **C# (.NET 10)** using **MonoGame**, **Nez**, and **Gum UI**.

---

## 🏛️ About King's Valley II

Originally released in 1988 by Konami for the MSX (RC-760) and MSX2 (RC-761) home computer systems, *King's Valley II* is one of the most celebrated puzzle-platformers in 8-bit gaming history. 

Players guide explorer **Vick** (or his partner **Harry** in 2-player mode) through the perilous depths of ancient Egyptian pyramids. The goal of each room: excavate passageways with shovels, drills, pickaxes, and hammers, collect every sacred **Soul Stone**, evade mummies and spectral pharaohs, and reach the pyramid door before time runs out.

This modern remake faithfully preserves:
* **All 60 Official Stages**: Complete with every mural variation, hieroglyphic decoration, puzzle room, ladder, and one-way gate.
* **Original 30 Hz Physics & Logic**: Precise tick-by-tick movement mechanics, jumping arcs, ladder ascents, digging timings, enemy AI patrols, weapon ballistics (throwing knives and boomerangs), and crumbling block decay.
* **Multi-Format Visual Engine**: 1:1 support for both **MSX1 SCREEN 2 (TMS9918A)** crisp square pixels with authentic color clash and vertical letterboxing, as well as the **MSX2 SCREEN 5 (V9938)** palette, murals, and cycling palettes.
* **Universal Built-in Fallback Font**: Robust procedural 8x8 font system and verified atlas fallback guaranteeing that setup menus, pause menus (`ESC`), dialogs, and navigation text remain 100% visible and readable under all conditions, even prior to ROM extraction or when extracting MSX1-only cartridges.
* **Optimized High-Speed ROM Extraction**: Streamlined direct emulation with real-time per-track progress notifications, $O(n \log n)$ binary-search loop analysis, and calibrated song lengths, reducing initial cartridge extraction time by over 67%.
* **Resilient Data Management**: Quick reset and re-extraction via the `RESET` button, `DEL`/`Backspace` keys, or the `RESET DATA` option in the ESC pause menu on all platforms.

---

## 🎵 8-Channel PSG + SCC Hybrid Audio Architecture

*King's Valley II* features one of the most iconic soundtracks in retro gaming, composed by the masterminds of the **Konami Kukeiha Club (矩形波倶楽部)**:
* **Kazuhiko Uehara (上原和彦)**
* **Kinuyo Yamashita (山下絹代)**
* **Tomoya Tomita (冨田朋也)**
* **Yuji Takenouchi (竹之内裕治)**

### Hardware Architecture:
The game's original audio system utilized an unprecedented hybrid of two legendary soundchips:
1. **AY-3-8910 (PSG)**: 3 square-wave tone channels + 1 noise generator (driving bass kicks, snares, percussion, and dynamic game sound effects).
2. **Konami SCC (Sound Creative Chip / K051649)**: 5 custom 32-byte cyclic wavetable channels (driving rich acoustic-like leads, brass, strings, counter-melodies, and arpeggios).

This remake integrates **[Wagenheimer.MsxAudio](https://github.com/wagenheimer/MsxAudio)**, a dedicated C# synthesis engine that renders both sound generators in real-time at full 48 kHz / 32-bit floating point precision, with channel muting, soloing, and real-time waveform visualization.

> **Tip:** You can inspect, solo, play, and analyze all 57 extracted sound and music tracks using the **MSXScope** player and chip analyzer tool in the MsxAudio suite.

---

## ⚖️ Zero-Asset Clean Architecture & Legal Safety

In strict compliance with copyright regulations and open-source principles:
* **The repository and release binaries contain ZERO proprietary Konami copyrighted assets.**
* No proprietary ROMs, pre-baked background images, or copyrighted audio files are distributed.
* **On first launch**, the game presents a streamlined, clean setup screen with a native OS file picker. Simply point the game to your legally owned **King's Valley II MSX1 ROM** (`.rom`).
* The embedded high-speed extraction engine automatically runs in seconds:
  * Decodes the TMS9918 pattern generator and color tables.
  * Assembles native binary screen buffers (`.bin`) and stage definitions.
  * Captures authentic Konami character poses, mummy sprites, tools, and HUD elements directly from VRAM.
  * Synthesizes audio tracks into native formats stored in `%APPDATA%/KingsValley/` (`~/Library/Application Support/KingsValley/` on macOS, `~/.local/share/KingsValley/` on Linux).
  * Subsequent launches boot instantly into the game!

---

## 💻 Downloads & Platform Installation

### 🍎 macOS (Apple Silicon & Intel)
* **Downloads:**
  * `KingsValley2-v1.0.0-macOS-AppleSilicon.zip` (M1/M2/M3/M4)
  * `KingsValley2-v1.0.0-macOS-x64.zip` (Intel Core)
* **Status:** Fully signed with Apple Developer ID and notarized by Apple Gatekeeper. Includes Hardened Runtime and .NET JIT entitlements.
* **Installation:**
  1. Extract the `.zip` archive.
  2. Drag `KingsValley2.app` to your `/Applications` folder.
  3. Double-click to launch. On first run, select your MSX1 ROM via the native macOS file picker.

### 🪟 Windows (64-bit)
* **Download:** `KingsValley2-v1.0.0-Windows-x64.zip`
* **Installation:**
  1. Extract the `.zip` folder.
  2. Run `KingsValley2.exe`.
  3. Select your MSX1 ROM when prompted.

### 🐧 Linux (64-bit)
* **Download:** `KingsValley2-v1.0.0-Linux-x64.tar.gz`
* **Installation:**
  1. `tar -xzvf KingsValley2-v1.0.0-Linux-x64.tar.gz`
  2. Ensure execution permissions: `chmod +x KingsValley2`
  3. `./KingsValley2`

---

## 🎮 Controls & Shortcuts

| Action | Keyboard | Gamepad (Xbox / PlayStation) |
| :--- | :--- | :--- |
| **Move Left / Right** | `Left / Right Arrows`, `A / D` | `D-Pad Left / Right`, `Left Stick` |
| **Climb Up / Down** | `Up / Down Arrows`, `W / S` | `D-Pad Up / Down`, `Left Stick` |
| **Jump** | `Space`, `Z` | `A / Cross` |
| **Use Tool (Dig / Hammer / Drill)** | `Down + Space`, `Down + Z` | `D-Pad Down + A / Cross` |
| **Throw Weapon (Knife / Boomerang)** | `X`, `Shift` | `X / Square`, `B / Circle` |
| **System Menu** | `Escape` | `Start / Options` |
| **Toggle Graphics (MSX1 / MSX2)** | `F11` (or via ESC Menu) | System Menu |
| **Sound Test & Jukebox** | `F2` | — |
| **ROM Setup & Extraction** | `F12` (or `ESC` on Title / in System Menu) | `Back` (Title Screen) |
| **Reset / Purge Cache** | `Delete` / `Backspace` (in Setup Screen) | — |

---

## 🛠️ Built With

* **[.NET 10](https://dotnet.microsoft.com/)** — Modern C# runtime offering ultra-fast JIT performance.
* **[MonoGame](https://monogame.net/)** — Cross-platform low-level multimedia framework.
* **[Nez](https://github.com/prime31/Nez)** — 2D entity-component-system (ECS) engine.
* **[Gum](https://flatredball.com/gum/)** — High-performance vector/bitmap UI engine.
* **[Wagenheimer.MsxAudio](https://github.com/wagenheimer/MsxAudio)** — Pure C# AY-3-8910 + Konami SCC software synthesizer.
* **[Wagenheimer.MonoGameHelper](https://github.com/wagenheimer/MonoGameHelper)** — MonoGame integration utilities and display helpers.

---

## 📜 Credits & Dedication

Dedicated to the visionary designers, programmers, and composers at **Konami Industry Co., Ltd.** who created the immortal classic in 1988.

*Original King's Valley II © 1988 Konami Industry Co., Ltd. This is a non-commercial, educational open-source preservation project.*
