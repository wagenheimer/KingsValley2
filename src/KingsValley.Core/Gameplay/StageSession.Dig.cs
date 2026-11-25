using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>Crumbling block: corner cell (px), shape and stage (0..2) of the animation.</summary>
/// <param name="X">X of the first cell.</param>
/// <param name="Y">Y of the first cell.</param>
/// <param name="Floor">Shovel/pickaxe: 2 cells side by side; hammer/drill: 2 stacked.</param>
/// <param name="Stage">Crumbling stage (0..2).</param>
/// <param name="FacingRight">Hammer/drill use different drawings per direction (0xABE9/0xABEF).</param>
public readonly record struct DigCrumble(int X, int Y, bool Floor, int Stage, bool FacingRight);

/// <summary>
/// Digging <b>ported from the ROM</b> (item handlers 3..6 at AA66/AB09/AB5D/ABF5):
/// <list type="bullet">
/// <item>Shovel (3) and pickaxe (4): 2 cells side by side ahead and below the feet (A0A6).</item>
/// <item>Hammer (5) and drill (6): 1 column of 2 cells ahead, at body height (A0F0).</item>
/// <item>Diggable cell only (value 2) — and both must be.</item>
/// <item>Each block crumbles in 3 stages of 8 ticks and vanishes on the next step (AAA9/AB86).</item>
/// <item>The <b>pickaxe keeps going down</b> and the <b>drill keeps advancing</b> (taking Vick
/// 8 px) while the next pair is diggable (AB31/AC1D).</item>
/// </list>
/// </summary>
public sealed partial class StageSession
{
    private const int CrumbleStepTicks = 8;
    private const int CrumbleStages = 3;

    /// <summary>Block crumbling now (null if nobody is digging).</summary>
    public DigCrumble? Crumble { get; private set; }

    private int _digTimer;
    private int _digStage;

    /// <summary>A0A6/A0F0: finds the target; only starts if both cells are diggable.</summary>
    private bool TryStartDig()
    {
        var p = Player;
        var target = DigTarget(p.Holding);
        if (target == null || !PairDiggable(target.Value.X, target.Value.Y, target.Value.Floor))
            return false;

        Crumble = target;
        _digTimer = CrumbleStepTicks;
        _digStage = -1;
        return true;
    }

    private DigCrumble? DigTarget(ItemKind tool)
    {
        var p = Player;
        bool right = p.Facing == Facing.Right;
        if (tool.DigsFloor())
        {
            int x = ((p.X + 4) & ~7) + (right ? 8 : -8);
            return new DigCrumble(x, (p.Y + 16) & ~7, true, -1, right);
        }

        if (tool.DigsWall())
        {
            int x = (p.X + (right ? 0x12 : -2)) & ~7;
            return new DigCrumble(x, p.Y & ~7, false, -1, right);
        }

        return null;
    }

    private bool PairDiggable(int x, int y, bool floor) =>
        Map.At(x, y) == TerrainKind.Soft &&
        Map.At(floor ? x + 8 : x, floor ? y : y + 8) == TerrainKind.Soft;

    /// <summary>One digging tick; returns true when the item runs out.</summary>
    private bool StepDig()
    {
        if (Crumble is not { } c)
            return true;

        if (--_digTimer > 0)
            return false;

        _digTimer = CrumbleStepTicks;
        if (++_digStage < CrumbleStages)
        {
            Crumble = c with { Stage = _digStage };
            return false;
        }

        // 63ED: both cells become empty.
        Map.Dig(RoomMap.FloorDiv(c.X), RoomMap.FloorDiv(c.Y));
        Map.Dig(RoomMap.FloorDiv(c.Floor ? c.X + 8 : c.X), RoomMap.FloorDiv(c.Floor ? c.Y : c.Y + 8));
        Emit(GameEventKind.Dug, c.X, c.Y, Player.Holding);

        var next = NextDigTarget(c);
        if (next != null && PairDiggable(next.Value.X, next.Value.Y, next.Value.Floor))
        {
            if (Player.Holding == ItemKind.Drill)
                Player.X += c.FacingRight ? 8 : -8;   // AC4E: the drill takes Vick along
            Crumble = next;
            _digStage = -1;
            return false;
        }

        Crumble = null;
        return true;
    }

    /// <summary>AB31 (pickaxe: one row below) and AC1D (drill: one column ahead).</summary>
    private DigCrumble? NextDigTarget(DigCrumble c) => Player.Holding switch
    {
        ItemKind.Pickaxe => c with { Y = c.Y + 8, Stage = -1 },
        ItemKind.Drill => c with { X = c.X + (c.FacingRight ? 8 : -8), Stage = -1 },
        _ => null,
    };
}
