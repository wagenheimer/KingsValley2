using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingsValley.Core.Gameplay;

namespace KingsValley.Engine.Input;

/// <summary>
/// Scripted input for documentation/CI: <c>KINGSVALLEY_INPUT=&lt;file&gt;</c> replaces keyboard
/// and gamepad with a reproducible tick sequence (it is what allows recording GIFs of the mechanics).
///
/// Format, one line per tick range (1 tick ≈ 1/30 s):
/// <code>
/// # comment
/// 0-19   R          # 20 ticks holding right
/// 20-24  .
/// 25     A          # one press of the action button (throw/dig)
/// 26-30  RA
/// </code>
/// Keys: <c>L</c> left, <c>R</c> right, <c>U</c> up, <c>D</c> down, <c>A</c> action, <c>.</c> nothing.
/// </summary>
public sealed class InputScript
{
    private readonly List<(int From, int To, PlayerInput Input)> _steps = new();

    /// <summary>Loads the script (throws if the file has an invalid line).</summary>
    public static InputScript Load(string path)
    {
        var script = new InputScript();
        int lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException($"{path}:{lineNo}: expected '<from>[-<to>] <keys>'");

            var span = parts[0].Split('-');
            int from = int.Parse(span[0]);
            int to = span.Length > 1 ? int.Parse(span[1]) : from;

            bool left = false, right = false, up = false, down = false, action = false;
            foreach (char c in parts[1].ToUpperInvariant())
            {
                switch (c)
                {
                    case 'L': left = true; break;
                    case 'R': right = true; break;
                    case 'U': up = true; break;
                    case 'D': down = true; break;
                    case 'A': action = true; break;
                    case '.': break;
                    default: throw new FormatException($"{path}:{lineNo}: unknown key '{c}'");
                }
            }

            script._steps.Add((from, to, new PlayerInput(left, right, up, down, action)));
        }

        return script;
    }

    /// <summary>Input of tick <paramref name="tick"/> (0 = the first game tick).</summary>
    public PlayerInput At(int tick)
    {
        foreach (var step in _steps)
        {
            if (tick >= step.From && tick <= step.To)
                return step.Input;
        }

        return PlayerInput.None;
    }

    /// <summary>Last tick with any input (to know when the recording can stop).</summary>
    public int LastTick => _steps.Count == 0 ? 0 : _steps.Max(s => s.To);
}
