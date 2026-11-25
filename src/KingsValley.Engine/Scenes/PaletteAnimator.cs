#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Animates color 6 (and, in pyramid 4, color 5) the way the original does (routine <c>5C14</c>):
/// each slot has a counter (0..period-1), a direction (up/down) and an index in the ramp;
/// every <c>period</c> frames the index advances (up to <c>limit-1</c>, down to 0) and the
/// ramp value is written into the palette entry.
/// </summary>
public sealed class PaletteAnimator
{
    private sealed class Slot
    {
        public int Colour;
        public int Limit;
        public int Period;
        public Color[] Ramp = Array.Empty<Color>();
        public int Counter;
        public int Direction; // 0 = going up, 1 = going down
        public int Index;
        public Color Current;
    }

    private readonly List<Slot> _slots = new();

    public void Load(GraphicsDevice device, int pyramid, JsonDocument animations, JsonDocument palettes)
    {
        _slots.Clear();
        var pal = palettes.RootElement.GetProperty(pyramid.ToString());
        var anim = animations.RootElement.GetProperty(pyramid.ToString());
        foreach (var slotEl in anim.EnumerateArray())
        {
            var slot = new Slot
            {
                Colour = slotEl.GetProperty("colour").GetInt32(),
                Limit = slotEl.GetProperty("limit").GetInt32(),
                Period = slotEl.GetProperty("period").GetInt32(),
            };
            var ramp = new List<Color>();
            foreach (var c in slotEl.GetProperty("ramp").EnumerateArray())
            {
                ramp.Add(new Color(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32()));
            }
            slot.Ramp = ramp.ToArray();
            // initial value = the palette's static color (before the animation starts)
            slot.Current = ReadPalette(pal, slot.Colour);
            _slots.Add(slot);
        }
    }

    private static Color ReadPalette(JsonElement pal, int colour)
    {
        var c = pal[colour];
        return new Color(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());
    }

    /// <summary>Advances the animation one frame (60 Hz). Returns true if any color changed.</summary>
    public bool Tick()
    {
        bool changed = false;
        foreach (var s in _slots)
        {
            s.Counter++;
            if (s.Counter < s.Period)
                continue;
            s.Counter = 0;

            if (s.Direction == 0)
            {
                s.Index++;
                if (s.Index >= s.Limit - 1)
                    s.Direction = 1;
            }
            else
            {
                s.Index--;
                if (s.Index <= 0)
                    s.Direction = 0;
            }

            if (s.Index < 0) s.Index = 0;
            if (s.Index >= s.Ramp.Length) s.Index = s.Ramp.Length - 1;

            var next = s.Ramp[s.Index];
            if (next != s.Current)
            {
                s.Current = next;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Returns the animated color for the given index (or the static color if not animated).</summary>
    public Color GetColor(int index, Color staticColor)
    {
        foreach (var s in _slots)
            if (s.Colour == index)
                return s.Current;
        return staticColor;
    }

    public bool HasSlots => _slots.Count > 0;
}
