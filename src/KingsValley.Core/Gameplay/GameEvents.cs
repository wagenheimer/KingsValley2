using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>Event types that the Engine animates/sounds (each has an original sound).</summary>
public enum GameEventKind
{
    /// <summary>The player appeared in the stage (sound id 30).</summary>
    PlayerAppeared,

    /// <summary>Pulou (id 19).</summary>
    Jumped,

    /// <summary>Took a step (ids 56/60 alternating).</summary>
    Step,

    /// <summary>Pegou um item (id 25).</summary>
    PickedItem,

    /// <summary>Picked up a soul stone.</summary>
    CollectedSoulStone,

    /// <summary>All soul stones: the door opened.</summary>
    DoorOpened,

    /// <summary>Dug one or more cells.</summary>
    Dug,

    /// <summary>Threw knife/boomerang (id 28 looping during the flight).</summary>
    Threw,

    /// <summary>The knife hit and fell (id 54).</summary>
    ProjectileStopped,

    /// <summary>Killed an enemy.</summary>
    EnemyKilled,

    /// <summary>An enemy vanished in sparks without being hit (stuck or 16 wall hits, sound of 42B7).</summary>
    EnemyVanished,

    /// <summary>A sarcophagus opened and released a mummy (id 37).</summary>
    CoffinOpened,

    /// <summary>Morreu (id 40 + jingle id 41).</summary>
    PlayerDied,

    /// <summary>Changed room.</summary>
    RoomChanged,

    /// <summary>Reached the door with all the stones: the exit sequence starts.</summary>
    DoorEntered,

    /// <summary>Only an original sound (no visual effect of its own).</summary>
    Sound,

    /// <summary>Left through the door: stage cleared.</summary>
    StageCleared,

    /// <summary>
    /// Extra life by score: the "card" rises with sound 0x2A (4C20 <c>add_score</c>, when the
    /// score reaches the <c>E245</c> threshold — 30,000, 60,000, 90,000…).
    /// </summary>
    ExtraLife,
}

/// <summary>One event of the tick, with position (room px) and the item involved when there is one.</summary>
/// <remarks><c>Sound</c> is the original sound ID (<see cref="OriginalSounds"/>); 0 = no sound.</remarks>
public readonly record struct GameEvent(GameEventKind Kind, int X = 0, int Y = 0, ItemKind Item = ItemKind.None, int Sound = 0);
