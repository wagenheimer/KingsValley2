using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>Horizontal direction.</summary>
public enum Facing
{
    /// <summary>Left.</summary>
    Left = -1,

    /// <summary>Right.</summary>
    Right = 1,
}

/// <summary>Player state (equivalent to byte <c>E280</c> of the original).</summary>
public enum PlayerState
{
    /// <summary>Standing/walking.</summary>
    Standing,

    /// <summary>In the air, following the jump table.</summary>
    Jumping,

    /// <summary>Falling.</summary>
    Falling,

    /// <summary>On the ladder.</summary>
    Climbing,

    /// <summary>Digging (standing still).</summary>
    Digging,

    /// <summary>Throwing (standing still).</summary>
    Throwing,

    /// <summary>Morrendo.</summary>
    Dying,

    /// <summary>Leaving through the door.</summary>
    Exiting,
}

/// <summary>Player commands in one tick (keyboard: arrows + space; F5 = suicide).</summary>
public readonly record struct PlayerInput(bool Left, bool Right, bool Up, bool Down, bool Action)
{
    /// <summary>No key.</summary>
    public static PlayerInput None => default;

    /// <summary>-1, 0 or +1.</summary>
    public int Horizontal => (Right ? 1 : 0) - (Left ? 1 : 0);

    /// <summary>-1 (up), 0 or +1 (down).</summary>
    public int Vertical => (Down ? 1 : 0) - (Up ? 1 : 0);
}

/// <summary>Integer rectangle in pixels (pure C#).</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    /// <summary>Intersects another rectangle?</summary>
    public bool Intersects(Box other) =>
        X < other.X + other.Width && other.X < X + Width && Y < other.Y + other.Height && other.Y < Y + Height;

    /// <summary>Shrinks by <paramref name="inset"/> px on each side.</summary>
    public Box Inset(int inset) => new(X + inset, Y + inset, Width - inset * 2, Height - inset * 2);
}

/// <summary>O explorador (Vick XIII).</summary>
public sealed class Player
{
    /// <summary>X of the top-left corner (px, room coordinates).</summary>
    public int X { get; internal set; }

    /// <summary>Y of the top-left corner (px).</summary>
    public int Y { get; internal set; }

    /// <summary>Which way it faces.</summary>
    public Facing Facing { get; internal set; } = Facing.Right;

    /// <summary>Current state.</summary>
    public PlayerState State { get; internal set; } = PlayerState.Standing;

    /// <summary>What is in the hands (the original carries only one item; with an item it does not jump).</summary>
    public ItemKind Holding { get; internal set; }

    /// <summary>Tick inside the state (jump, dig, die...).</summary>
    public int StateTick { get; internal set; }

    /// <summary>Step counter (walking animation).</summary>
    public int StepCounter { get; internal set; }

    /// <summary>
    /// Sprite pose (byte <c>E285</c> of the original): index 0..13 in the pose set of the item in
    /// hand (<see cref="PoseSet"/>). See <see cref="PlayerPoses"/>.
    /// </summary>
    public int Pose { get; internal set; } = PlayerPoses.StandRight;

    /// <summary>Pose set to draw (the original swaps the pose page by item/state).</summary>
    public PlayerPoseSet PoseSet => State switch
    {
        PlayerState.Dying => PlayerPoseSet.Death,
        PlayerState.Exiting => PlayerPoseSet.Door,
        _ => (PlayerPoseSet)(int)Holding,
    };

    /// <summary>Direction locked at the start of the jump (-1, 0, +1).</summary>
    internal int JumpDirection { get; set; }

    /// <summary>Fraction of X (1/256 px) — the jump moves 1.25 px/tick.</summary>
    internal int XFraction { get; set; }

    /// <summary>Fraction of Y (1/256 px) during the jump (<c>E281</c>).</summary>
    internal int YFraction { get; set; }

    /// <summary>Vertical jump speed in 8.8 (<c>E292</c>).</summary>
    internal int VelocityY { get; set; }

    /// <summary>Walk cycle phase 0..3 (<c>E295</c>) and death.</summary>
    internal int AnimPhase { get; set; }

    /// <summary>Ticks until the next pose change (<c>E296</c>/<c>E286</c>).</summary>
    internal int AnimTimer { get; set; } = 1;

    /// <summary>Collision box.</summary>
    public Box Bounds => new(X, Y, Physics.ActorSize, Physics.ActorSize);
}

/// <summary>State of an enemy (byte 1 of structure <c>E500</c> of the original).</summary>
public enum EnemyState
{
    /// <summary>Coming out of the sarcophagus (does not kill).</summary>
    Emerging,

    /// <summary>Walking.</summary>
    Walking,

    /// <summary>Idle (on landing or before turning at the wall).</summary>
    Pausing,

    /// <summary>Falling.</summary>
    Falling,

    /// <summary>On the ladder (type 2).</summary>
    Climbing,

    /// <summary>In the air (guard's little hop / type 4 bounce).</summary>
    Airborne,

    /// <summary>Hit: frozen until it vanishes.</summary>
    Dying,
}

/// <summary>Enemy (spawned by a sarcophagus) or pushable block.</summary>
public sealed class Enemy
{
    /// <summary>Type.</summary>
    public required EnemyKind Kind { get; init; }

    /// <summary>Room it is in.</summary>
    public int Room { get; internal set; }

    /// <summary>X (px).</summary>
    public int X { get; internal set; }

    /// <summary>Fraction of X (1/256 px), as in the original.</summary>
    internal int XFraction { get; set; }

    /// <summary>Y (px).</summary>
    public int Y { get; internal set; }

    /// <summary>Direction.</summary>
    public Facing Facing { get; internal set; } = Facing.Right;

    /// <summary>State.</summary>
    public EnemyState State { get; internal set; } = EnemyState.Walking;

    /// <summary>Alive (counts for collision)?</summary>
    public bool Alive => State is not EnemyState.Dying;

    /// <summary>Ticks left in the state (pause, leaving the sarcophagus, death...).</summary>
    internal int Timer { get; set; }

    /// <summary>Index in the jump/bounce table.</summary>
    internal int ArcTick { get; set; }

    /// <summary>Vertical target (px) when on the ladder.</summary>
    internal int ClimbTargetY { get; set; }

    /// <summary>General counter (animation).</summary>
    public int Tick { get; internal set; }

    /// <summary>Source sarcophagus (-1 = mechanism).</summary>
    public int CoffinIndex { get; init; } = -1;

    // ── Fields of the original E500 structure (IX+n), used by the ROM-ported AI ──────────

    /// <summary>Sprite frame (<c>IX+0x11</c>); <see cref="EnemyFrames.Hidden"/> = invisible.</summary>
    public int Frame { get; internal set; } = EnemyFrames.Hidden;

    /// <summary>Death effect colors (the original switches the colors to 0x0A/0x4B, <c>6624</c>).</summary>
    public bool Vanishing { get; internal set; }

    /// <summary>Original state (<c>IX+1</c>).</summary>
    internal int Phase { get; set; }

    /// <summary>Original direction (<c>IX+0x0B</c>): 0 up, 1 down, 2 left, 3 right.</summary>
    internal int Dir { get; set; }

    /// <summary>Direction saved when it starts falling (<c>IX+0x19</c>).</summary>
    internal int SavedDir { get; set; }

    /// <summary>Fraction of Y (<c>IX+2</c>).</summary>
    internal int YFraction { get; set; }

    /// <summary>Vertical speed 8.8 (<c>IX+7/8</c>).</summary>
    internal int SpeedY { get; set; }

    /// <summary>Horizontal speed 8.8 (<c>IX+9/0A</c>).</summary>
    internal int SpeedX { get; set; }

    /// <summary>Moving (<c>IX+6</c>): integrator 656C only adds the speed while it is on.</summary>
    internal bool Moving { get; set; }

    /// <summary>Wall hits (<c>IX+0x18</c>): on the 16th the mummy vanishes.</summary>
    internal int Bumps { get; set; }

    /// <summary>Type 4: how many times it has rolled (<c>IX+0x0E</c>; on the 4th it vanishes).</summary>
    internal int RollCycles { get; set; }

    /// <summary>Type 4: fall ticks (<c>IX+0x1C</c>, saturates at 0xFF).</summary>
    internal int FallTicks { get; set; }

    /// <summary>Type 4: cell (corner) where the stone wrote the block into the map, if it did.</summary>
    internal (int Column, int Row)? RockCell { get; set; }

    /// <summary>Type 3 after turning into a statue: colors 0x0A/0x47 forever (B2DB).</summary>
    public bool Yellow { get; internal set; }

    /// <summary>Type 3: X where the last hop started (<c>IX+0x18</c>; 0xFF = none).</summary>
    internal int HopX { get; set; } = 0xFF;

    /// <summary>Type 3: low hop (table 0xB5A6) — there was an obstacle at head height (<c>IX+0x1A</c>).</summary>
    internal bool HopLow { get; set; }

    /// <summary>Type 3: direction of the last ladder (<c>IX+0x1B</c>; 0xFF = none).</summary>
    internal int LastLadder { get; set; } = 0xFF;

    /// <summary>Type 3: consecutive reversals (<c>IX+0x1C</c>; on the 16th it vanishes).</summary>
    internal int Reversals { get; set; }

    /// <summary>Type 3: hops in a row since the last ladder (<c>IX+0x1D</c>, starts at 0x80).</summary>
    internal int Hops { get; set; } = 0x80;

    /// <summary>Bit 0: kills on touch; bit 1: can be hit (<c>IX+0x13</c>).</summary>
    internal int Flags { get; set; }

    /// <summary>Kills the player on touch?</summary>
    public bool Dangerous => (Flags & 1) != 0;

    /// <summary>Can be hit by knife/boomerang?</summary>
    public bool Shootable => (Flags & 2) != 0;

    /// <summary>On the ladder? (the original only allows the kill if both are, or are not, on the ladder).</summary>
    public bool Climbing => State == EnemyState.Climbing;

    /// <summary>Collision box.</summary>
    public Box Bounds => new(X, Y, Physics.ActorSize, Physics.ActorSize);

    /// <summary>Adds an 8.8 fixed-point offset to the horizontal position.</summary>
    internal void MoveFixed(int deltaFixed)
    {
        int total = (X << 8) + XFraction + deltaFixed;
        X = total >> 8;
        XFraction = total & 0xFF;
    }
}

/// <summary>Item on the ground.</summary>
public sealed class FloorItem
{
    /// <summary>Type.</summary>
    public required ItemKind Kind { get; init; }

    /// <summary>Room.</summary>
    public required int Room { get; init; }

    /// <summary>X (px).</summary>
    public int X { get; internal set; }

    /// <summary>Y (px).</summary>
    public int Y { get; internal set; }

    /// <summary>Collision box.</summary>
    public Box Bounds => new(X, Y, 16, 16);
}

/// <summary>Phase of the thrown tool — the high nibble of <c>slot+0</c> (E300) in the ROM.</summary>
public enum ProjectilePhase
{
    /// <summary>
    /// Phase 2: initializes and probes ahead (A706 <c>knife_go</c> / A8AE <c>boom_go</c>) — one call.
    /// Free, it already flies on this same call; against a block, it starts spinning in place.
    /// </summary>
    Go,

    /// <summary>Phase 3: flying (A717 <c>knife_fly</c> / A8C7 <c>boom_fly</c>).</summary>
    Flying,

    /// <summary>Phase 4: stopped and spinning in place for 0x18 ticks (A743 <c>knife_wait</c> / A975 <c>boom_wait</c>).</summary>
    Waiting,

    /// <summary>Phase 5: falling with increasing gravity (A9BA <c>tool_fall</c>).</summary>
    Falling,
}

/// <summary>
/// Knife or boomerang in flight — one of the 64 E300 slots of the ROM (8 bytes each; here only what the port uses).
/// The movement moves <b>1 step per logic call</b> (the knife 4 px, A826 <c>tool_step_x</c>) — and
/// 1 ROM call = 1 port tick.
/// </summary>
public sealed class Projectile
{
    /// <summary>Knife or boomerang.</summary>
    public required ItemKind Kind { get; init; }

    /// <summary>X (px) — <c>IX+2</c>.</summary>
    public int X { get; internal set; }

    /// <summary>Y (px) — <c>IX+1</c>.</summary>
    public int Y { get; internal set; }

    /// <summary>
    /// Room of the tool — <c>IX+3</c> (<c>tool_scr</c> 0xA6FF copies from <c>E243</c>). It <b>changes</b>
    /// when the tool crosses the room border (0xA826 <c>step_wrap</c> → <c>room_link</c>) or falls
    /// through the bottom (0xAA17 <c>tool_wrap_y</c>): in the original the knife keeps flying into the next room.
    /// </summary>
    public int Room { get; internal set; }

    /// <summary>Throw direction (A706/A8AE copy from <c>E294</c>) — <c>IX+7</c>.</summary>
    public Facing Facing { get; internal set; }

    /// <summary>Phase (high nibble of <c>IX+0</c>): starts at <c>go</c>, like <c>throw_done</c> (A38F).</summary>
    public ProjectilePhase Phase { get; internal set; } = ProjectilePhase.Go;

    /// <summary>Animation frame — <c>IX+4</c>: 0..2 on the knife (A87F) and 3..5 on the boomerang (A99D).</summary>
    public int Anim { get; internal set; }

    /// <summary>Step — <c>IX+5</c>: index into <c>boom_dx/boom_dt</c> on the boomerang and 0..5 on the fall.</summary>
    public int Step { get; internal set; }

    /// <summary>Counter in ROM logic <b>calls</b> — <c>IX+6</c> (1 call = 1 port tick).</summary>
    public int Timer { get; internal set; }

    /// <summary>
    /// Hit an enemy (A787 <c>tool_lock</c> with <c>E2E8</c>/<c>EDCD</c>): the next flight step
    /// drops the tool — aligns to the ground, waits 0x18 ticks and falls.
    /// </summary>
    public bool HitLocked { get; internal set; }

}

/// <summary>Sarcophagus: spawns enemies of its type while the player is in the room.</summary>
public sealed class Coffin
{
    /// <summary>Position.</summary>
    public required StagePoint Position { get; init; }

    /// <summary>Type of enemy spawned.</summary>
    public required EnemyKind Kind { get; init; }

    /// <summary>Level (1..3) of the original record — effect still to be measured.</summary>
    public int Level { get; init; } = 1;

    /// <summary>Counter of record <c>E2C0</c> (goes down every 4 ticks while armed).</summary>
    internal int Cooldown { get; set; }

    /// <summary>Armed (bit 7 of the record): counts down to release the next mummy.</summary>
    internal bool Armed { get; set; } = true;

    /// <summary>Sarcophagus open (the mummy is out)?</summary>
    public bool Open { get; internal set; }

    /// <summary>Current drawing: closed (0x93AC), half-open (0x93B2) or open (0x93B8).</summary>
    public CoffinLook Look { get; internal set; } = CoffinLook.Closed;
}

/// <summary>The three sarcophagus drawings in the original.</summary>
public enum CoffinLook
{
    /// <summary>Closed (blocks 155..160).</summary>
    Closed,

    /// <summary>Half-open, when releasing the mummy (161..166).</summary>
    Ajar,

    /// <summary>Open, with the mummy in front (167..172).</summary>
    Open,
}
