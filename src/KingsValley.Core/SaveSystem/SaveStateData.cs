#nullable enable
using System;
using System.Collections.Generic;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.SaveSystem;

/// <summary>
/// Persisted gameplay snapshot (SaveState).
/// Holds run progression, stage session state, actors, mutable maps, and quick metadata.
/// </summary>
public sealed class SaveStateData
{
    public int Version { get; set; } = 1;
    public int Slot { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // Quick summary for UI display
    public int Stage { get; set; }
    public int Pyramid { get; set; }
    public int Room { get; set; }
    public int Score { get; set; }
    public int Lives { get; set; }
    public int SoulStonesRemaining { get; set; }
    public int TotalSoulStones { get; set; }
    public string GfxRom { get; set; } = "msx2";

    // Full game session state
    public GameRunData Run { get; set; } = new();
    public StageSessionData Session { get; set; } = new();
}

public sealed class GameRunData
{
    public int Stage { get; set; }
    public int Lives { get; set; }
    public int HiScore { get; set; }
    public int StageScore { get; set; }
    public bool Invincible { get; set; }
    public bool ContinueEnabled { get; set; }
}

public sealed class StageSessionData
{
    public int StageNumber { get; set; }
    public int CurrentRoom { get; set; }
    public int Score { get; set; }
    public int ExtraLifeThreshold { get; set; }
    public int TickCount { get; set; }
    public bool DoorOpen { get; set; }
    public int DoorFrame { get; set; }
    public ExitPhase Exit { get; set; }
    public ExitClearStep ExitStep { get; set; }
    public int ExitTicks { get; set; }
    public int CurtainBands { get; set; }
    public bool Invincible { get; set; }

    public PlayerData Player { get; set; } = new();
    public List<StagePoint> SoulStones { get; set; } = new();
    public List<FloorItemData> Items { get; set; } = new();
    public List<EnemyData> Enemies { get; set; } = new();
    public List<ProjectileData> Projectiles { get; set; } = new();
    public List<CoffinData> Coffins { get; set; } = new();
    public List<RotatingDoorData> RotatingDoors { get; set; } = new();
    public List<TrapData> Traps { get; set; } = new();
    public List<FallingWallData> FallingWalls { get; set; } = new();
    public DigCrumbleData? Crumble { get; set; }
    public int DigTimer { get; set; }
    public int DigStage { get; set; }

    /// <summary>Room index -> cell kinds (32x24 = 768 entries).</summary>
    public Dictionary<int, int[]> RoomCells { get; set; } = new();
}

public sealed class PlayerData
{
    public int X { get; set; }
    public int Y { get; set; }
    public Facing Facing { get; set; }
    public PlayerState State { get; set; }
    public ItemKind Holding { get; set; }
    public int StateTick { get; set; }
    public int StepCounter { get; set; }
    public int Pose { get; set; }
    public int JumpDirection { get; set; }
    public int XFraction { get; set; }
    public int YFraction { get; set; }
    public int VelocityY { get; set; }
    public int AnimPhase { get; set; }
    public int AnimTimer { get; set; }
}

public sealed class FloorItemData
{
    public ItemKind Kind { get; set; }
    public int Room { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class EnemyData
{
    public EnemyKind Kind { get; set; }
    public int Room { get; set; }
    public int X { get; set; }
    public int XFraction { get; set; }
    public int Y { get; set; }
    public Facing Facing { get; set; }
    public EnemyState State { get; set; }
    public int Timer { get; set; }
    public int ArcTick { get; set; }
    public int ClimbTargetY { get; set; }
    public int Tick { get; set; }
    public int CoffinIndex { get; set; }
    public int Frame { get; set; }
    public bool Vanishing { get; set; }
    public int Phase { get; set; }
    public int Dir { get; set; }
    public int SavedDir { get; set; }
    public int YFraction { get; set; }
    public int SpeedY { get; set; }
    public int SpeedX { get; set; }
    public bool Moving { get; set; }
    public int Bumps { get; set; }
    public int RollCycles { get; set; }
    public int FallTicks { get; set; }
    public int? RockCellCol { get; set; }
    public int? RockCellRow { get; set; }
    public bool Yellow { get; set; }
    public int HopX { get; set; }
    public bool HopLow { get; set; }
    public int LastLadder { get; set; }
    public int Reversals { get; set; }
    public int Hops { get; set; }
    public int Flags { get; set; }
}

public sealed class ProjectileData
{
    public ItemKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Room { get; set; }
    public Facing Facing { get; set; }
    public ProjectilePhase Phase { get; set; }
    public int Anim { get; set; }
    public int Step { get; set; }
    public int Timer { get; set; }
    public bool HitLocked { get; set; }
}

public sealed class CoffinData
{
    public int Room { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public EnemyKind Kind { get; set; }
    public int Level { get; set; }
    public int Cooldown { get; set; }
    public bool Armed { get; set; }
    public bool Open { get; set; }
    public CoffinLook Look { get; set; }
}

public sealed class RotatingDoorData
{
    public int Room { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Phase { get; set; }
    public int Timer { get; set; }
    public bool Pushing { get; set; }
    public bool Right { get; set; }
    public bool PushRight { get; set; }
}

public sealed class TrapData
{
    public int Room { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Armed { get; set; }
    public bool WasArmed { get; set; }
    public int Count { get; set; }
    public bool Spent { get; set; }
}

public sealed class FallingWallData
{
    public int Room { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Triggered { get; set; }
    public bool Finished { get; set; }
    public int Rows { get; set; }
    public int Countdown { get; set; }
}

public sealed class DigCrumbleData
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool Floor { get; set; }
    public int Stage { get; set; }
    public bool FacingRight { get; set; }
}
