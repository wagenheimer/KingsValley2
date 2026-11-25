#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Animates the MSX1 (TMS9918A SCREEN 2) background patterns:
/// - Eye blinking for all 6 pyramids (routine 0x58D0, periods 12/18/16/5/12/4 ticks with ping-pong frames)
/// - Mural/torch light wave (routine 0x5915, 8 frames cycling every 4 ticks)
/// Faithfully replicates the VRAM pattern animation executed by the original Konami MSX1 ROM.
/// </summary>
public sealed class Msx1PatternAnimator
{
    private sealed class AnimatedCell
    {
        public int X;
        public int Y;
        public string Type = string.Empty; // "eye" or "torch"
        public int Offset;
        public byte[] Pat = Array.Empty<byte>();
        public byte[] Col = Array.Empty<byte>();
    }

    private sealed class PyramidAnim
    {
        public int Period;
        public int BasePattern;
        public int PatternCount;
        public byte[][][] Frames = Array.Empty<byte[][]>(); // [4 frames][pattern][8 rows]
        public int Counter;
        public int Direction; // 0 = up, 1 = down
        public int Frame; // 0, 1, 2, 3 ping-pong
    }

    private sealed class TorchAnim
    {
        public int Period = 4;
        public byte[][][] Frames = Array.Empty<byte[][]>(); // [8 frames][4 patterns][8 rows]
        public int Counter;
        public int Frame; // 0..7
    }

    private static readonly Color[] TmsColors =
    {
        Color.Black,
        Color.Black,
        new Color(33, 200, 66),
        new Color(94, 220, 120),
        new Color(84, 85, 237),
        new Color(125, 118, 252),
        new Color(212, 82, 77),
        new Color(66, 235, 245),
        new Color(252, 85, 84),
        new Color(255, 121, 120),
        new Color(212, 193, 84),
        new Color(230, 206, 128),
        new Color(33, 176, 59),
        new Color(201, 91, 186),
        new Color(204, 204, 204),
        Color.White,
    };

    private readonly Dictionary<int, PyramidAnim> _pyramids = new();
    private readonly TorchAnim _torch = new();
    private readonly Dictionary<string, List<AnimatedCell>> _rooms = new(StringComparer.OrdinalIgnoreCase);

    private float _timeAccumulator;
    private const float TickSeconds = 1f / 30f;
    private string? _loadedDir;

    public void EnsureLoaded(string roomsDir)
    {
        if (_loadedDir == roomsDir)
            return;

        _pyramids.Clear();
        _rooms.Clear();
        _loadedDir = roomsDir;

        var animPath = Path.Combine(roomsDir, "animations.json");
        if (!File.Exists(animPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(animPath));
            var root = doc.RootElement;

            if (root.TryGetProperty("pyramids", out var pyrEl))
            {
                foreach (var prop in pyrEl.EnumerateObject())
                {
                    if (!int.TryParse(prop.Name, out int pyrNum))
                        continue;

                    var pAnim = new PyramidAnim
                    {
                        Period = prop.Value.GetProperty("period").GetInt32(),
                        BasePattern = prop.Value.GetProperty("base_pattern").GetInt32(),
                        PatternCount = prop.Value.GetProperty("pattern_count").GetInt32(),
                    };

                    var framesList = new List<byte[][]>();
                    foreach (var fEl in prop.Value.GetProperty("frames").EnumerateArray())
                    {
                        var patList = new List<byte[]>();
                        foreach (var patEl in fEl.EnumerateArray())
                        {
                            var rowBytes = new byte[8];
                            int r = 0;
                            foreach (var b in patEl.EnumerateArray())
                            {
                                if (r < 8) rowBytes[r++] = (byte)b.GetInt32();
                            }
                            patList.Add(rowBytes);
                        }
                        framesList.Add(patList.ToArray());
                    }
                    pAnim.Frames = framesList.ToArray();
                    _pyramids[pyrNum] = pAnim;
                }
            }

            if (root.TryGetProperty("torch", out var torchEl))
            {
                _torch.Period = torchEl.GetProperty("period").GetInt32();
                var framesList = new List<byte[][]>();
                foreach (var fEl in torchEl.GetProperty("frames").EnumerateArray())
                {
                    var patList = new List<byte[]>();
                    foreach (var patEl in fEl.EnumerateArray())
                    {
                        var rowBytes = new byte[8];
                        int r = 0;
                        foreach (var b in patEl.EnumerateArray())
                        {
                            if (r < 8) rowBytes[r++] = (byte)b.GetInt32();
                        }
                        patList.Add(rowBytes);
                    }
                    framesList.Add(patList.ToArray());
                }
                _torch.Frames = framesList.ToArray();
            }

            if (root.TryGetProperty("rooms", out var roomsEl))
            {
                foreach (var roomProp in roomsEl.EnumerateObject())
                {
                    var cellList = new List<AnimatedCell>();
                    foreach (var cellEl in roomProp.Value.EnumerateArray())
                    {
                        var cell = new AnimatedCell
                        {
                            X = cellEl.GetProperty("x").GetInt32(),
                            Y = cellEl.GetProperty("y").GetInt32(),
                            Type = cellEl.GetProperty("type").GetString() ?? string.Empty,
                            Offset = cellEl.GetProperty("offset").GetInt32(),
                        };
                        if (cellEl.TryGetProperty("pat", out var patProp))
                        {
                            var patList = new List<byte>();
                            foreach (var p in patProp.EnumerateArray())
                                patList.Add((byte)p.GetInt32());
                            cell.Pat = patList.ToArray();
                        }
                        var colList = new List<byte>();
                        foreach (var c in cellEl.GetProperty("col").EnumerateArray())
                            colList.Add((byte)c.GetInt32());
                        cell.Col = colList.ToArray();
                        cellList.Add(cell);
                    }
                    _rooms[roomProp.Name] = cellList;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Msx1PatternAnimator] Failed to parse animations.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Advances the animations at 30 Hz using elapsed delta time. Returns true if any frame changed.
    /// </summary>
    public bool Update(float deltaTime, int pyramid)
    {
        _timeAccumulator += deltaTime;
        bool changed = false;
        while (_timeAccumulator >= TickSeconds)
        {
            _timeAccumulator -= TickSeconds;
            if (AdvanceTick(pyramid))
                changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Advances one logic tick (~30 Hz) matching Z80 routines 0x5915 (torch) and 0x58D0 (eye).
    /// </summary>
    private bool AdvanceTick(int pyramid)
    {
        bool changed = false;

        // 1. Torch light wave (routine 0x5915: if ((++Counter & 3) == 0) Frame = (Frame + 1) & 7)
        _torch.Counter++;
        if ((_torch.Counter & 3) == 0)
        {
            _torch.Frame = (_torch.Frame + 1) & 7;
            changed = true;
        }

        // 2. Pyramid eye animation (routine 0x58D0: counts to Period, then ping-pongs 0, 1, 2, 3, 2, 1, 0...)
        if (_pyramids.TryGetValue(pyramid, out var pAnim) && pAnim.Frames.Length > 0)
        {
            pAnim.Counter++;
            if (pAnim.Counter >= pAnim.Period)
            {
                pAnim.Counter = 0;
                if (pAnim.Direction == 0)
                {
                    pAnim.Frame++;
                    if (pAnim.Frame >= 3)
                        pAnim.Direction = 1;
                }
                else
                {
                    pAnim.Frame--;
                    if (pAnim.Frame <= 0)
                        pAnim.Direction = 0;
                }
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Applies the animated pattern pixels for the specified room onto the target pixel buffer (256x192).
    /// </summary>
    public bool Apply(string roomKey, int pyramid, Color[] pixels, Color[]? palette = null)
    {
        if (!_rooms.TryGetValue(roomKey, out var cells) || cells.Count == 0)
            return false;

        _pyramids.TryGetValue(pyramid, out var pAnim);

        foreach (var cell in cells)
        {
            byte[]? colorFrame = null;
            if (cell.Type == "eye")
            {
                if (pAnim != null && pAnim.Frames.Length > 0 && pAnim.Frame < pAnim.Frames.Length)
                {
                    var pFrames = pAnim.Frames[pAnim.Frame];
                    if (cell.Offset < pFrames.Length)
                        colorFrame = pFrames[cell.Offset];
                }
            }
            else if (cell.Type == "torch")
            {
                if (_torch.Frames.Length > 0 && _torch.Frame < _torch.Frames.Length)
                {
                    var tFrames = _torch.Frames[_torch.Frame];
                    if (cell.Offset < tFrames.Length)
                        colorFrame = tFrames[cell.Offset];
                }
            }

            if (colorFrame == null || colorFrame.Length < 8)
                continue;

            byte[] patBytes = (cell.Pat.Length >= 8) ? cell.Pat : cell.Col;
            if (patBytes == null || patBytes.Length < 8)
                continue;

            for (int r = 0; r < 8; r++)
            {
                byte pat = patBytes[r];
                byte col = colorFrame[r];
                int fgIdx = (col >> 4) & 0x0F;
                int bgIdx = col & 0x0F;
                Color fg = (palette != null && fgIdx < palette.Length) ? palette[fgIdx] : TmsColors[fgIdx];
                Color bg = (palette != null && bgIdx < palette.Length) ? palette[bgIdx] : TmsColors[bgIdx];

                int py = cell.Y * 8 + r;
                if (py < 0 || py >= 192) continue;

                for (int b = 0; b < 8; b++)
                {
                    int px = cell.X * 8 + b;
                    if (px < 0 || px >= 256) continue;

                    pixels[py * 256 + px] = ((pat & (1 << (7 - b))) != 0) ? fg : bg;
                }
            }
        }

        return true;
    }
}
