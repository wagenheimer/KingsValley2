namespace KingsValley.Core.Stages;

/// <summary>Terrain class of an 8x8 cell (2 bits in the original — see <c>docs/ROM_EXTRACTION.md</c>).</summary>
public enum TerrainKind : byte
{
    /// <summary>Empty (shows the background mural).</summary>
    Empty = 0,

    /// <summary>Ladder (written on top of the terrain by the ladder table).</summary>
    Ladder = 1,

    /// <summary>Diggable block (golden/colored brick of each pyramid).</summary>
    Soft = 2,

    /// <summary>Indestructible wall.</summary>
    Wall = 3,
}

/// <summary>Items that stay on the ground (code = low nibble of the record in the ROM).</summary>
public enum ItemKind : byte
{
    /// <summary>None (empty hands).</summary>
    None = 0,

    /// <summary>Knife — reusable weapon, thrown in a straight line.</summary>
    Knife = 1,

    /// <summary>Boomerang — reusable weapon, goes and comes back.</summary>
    Boomerang = 2,

    /// <summary>Shovel — digs the ground 1 cell (single use).</summary>
    Shovel = 3,

    /// <summary>Pickaxe — digs the ground 2 cells (single use).</summary>
    Pickaxe = 4,

    /// <summary>Hammer — breaks the wall ahead 1 cell (single use).</summary>
    Hammer = 5,

    /// <summary>Drill — bores the wall ahead 2 cells (single use).</summary>
    Drill = 6,
}

/// <summary>
/// Enemies (type in the 1st byte of the sarcophagus record, <c>E2C0</c>: <c>0x80 | type</c>) and the pushable
/// block (mechanism 5). Behaviors ported from the ROM — see <c>docs/ROM_PORT_HANDOFF.md</c>.
/// </summary>
public enum EnemyKind : byte
{
    /// <summary>Type 1 — white mummy (AD80): walks, falls off cliffs, turns at walls, does <b>not</b> climb ladders.</summary>
    Mummy = 1,

    /// <summary>Type 2 — yellow mummy (AE23): turns to the player and <b>uses ladders</b> to chase him.</summary>
    GoldMummy = 2,

    /// <summary>Type 3 (B2AA): appears in a cloud as a statue and walks by <b>hopping</b> (yellow); uses ladders.</summary>
    Guard = 3,

    /// <summary>Type 4 (B68E): red stone that <b>becomes a terrain block</b>, blinks, rolls at the player and bounces.</summary>
    Rock = 4,

    /// <summary>Pushable block (mechanism 5 — does not kill; it is pushed and falls).</summary>
    MovableBlock = 5,
}

/// <summary>Stage mechanisms (<c>E600</c> list).</summary>
public enum MechanismKind : byte
{
    /// <summary>Swivel door (vertical bar; crossed by pushing).</summary>
    RotatingDoor = 1,

    /// <summary>A identificar.</summary>
    Unknown2 = 2,

    /// <summary>Triggered column that grows downward one diggable cell at a time.</summary>
    FallingWall = 3,

    /// <summary>A identificar.</summary>
    Unknown4 = 4,

    /// <summary>Pushable block.</summary>
    MovableBlock = 5,
}

/// <summary>Extensions for the stage enums.</summary>
public static class StageKindExtensions
{
    /// <summary>Bloqueia a passagem?</summary>
    public static bool IsSolid(this TerrainKind kind) => kind is TerrainKind.Soft or TerrainKind.Wall;

    /// <summary>Reusable weapon (thrown) instead of a single-use tool?</summary>
    public static bool IsWeapon(this ItemKind kind) => kind is ItemKind.Knife or ItemKind.Boomerang;

    /// <summary>Tool that digs the ground (below and ahead)?</summary>
    public static bool DigsFloor(this ItemKind kind) => kind is ItemKind.Shovel or ItemKind.Pickaxe;

    /// <summary>Tool that digs the wall (ahead)?</summary>
    public static bool DigsWall(this ItemKind kind) => kind is ItemKind.Hammer or ItemKind.Drill;


    /// <summary>Kills the player on contact?</summary>
    public static bool IsHostile(this EnemyKind kind) => kind != EnemyKind.MovableBlock;
}

/// <summary>Object position (1-based room, top-left corner pixels).</summary>
public readonly record struct StagePoint(int Room, int X, int Y);

/// <summary>Ladder: column/row of the top (cells), length in cells, 2 cells wide.</summary>
public sealed record LadderDefinition(int Room, int Column, int Row, int Length);

/// <summary>Item on the ground.</summary>
public sealed record ItemDefinition(ItemKind Kind, StagePoint Position);

/// <summary>Mechanism at the initial position.</summary>
public sealed record MechanismDefinition(
    MechanismKind Kind,
    StagePoint Position,
    int WidthCells = 0,
    int HeightCells = 0,
    int InitialPhase = 0,
    int Direction = 0)
{
    /// <summary>
    /// Does it occupy solid cells in the map? (type 3 does not: 63B7 does RET before writing; the stone, type 5,
    /// uses the dynamic wall of <see cref="RoomMap.SetRock"/>, which follows the stone when pushed).
    /// </summary>
    public bool IsSolid => Kind is not (MechanismKind.FallingWall or MechanismKind.MovableBlock)
        && WidthCells > 0 && HeightCells > 0;
}

/// <summary>Sarcophagus: spawns enemies of type <paramref name="Kind"/> (level <paramref name="Level"/>, 1..3).</summary>
public sealed record CoffinDefinition(StagePoint Position, EnemyKind Kind, int Level);

/// <summary>Exit door (4x4 cells).</summary>
public sealed record DoorDefinition(StagePoint Position, int WidthCells, int HeightCells);

/// <summary>One room (screen) of 32x24 cells.</summary>
public sealed class RoomDefinition
{
    /// <summary>Room number (1-based, in grid order).</summary>
    public required int Index { get; init; }

    /// <summary>Base terrain (no ladders), index <c>row * 32 + column</c>.</summary>
    public required TerrainKind[] Cells { get; init; }
}

/// <summary>Immutable definition of a stage (from <c>Content/Rom/Levels/stageNN.json</c>).</summary>
public sealed class StageDefinition
{
    /// <summary>Stage number (1..60).</summary>
    public required int Number { get; init; }

    /// <summary>Pyramid (1..6) — defines palette, tiles and mural.</summary>
    public int? SourcePyramid {get;init;}
    private int? _presentationPyramid;
    public int Pyramid => _presentationPyramid ?? SourcePyramid ?? (Number - 1) / Common.GameConstants.StagesPerPyramid + 1;
    /// <summary>Returns an independent presentation choice without changing extracted terrain or source metadata.</summary>
    public StageDefinition WithPyramid(int pyramid)
    {
        if(pyramid is <1 or >6)throw new ArgumentOutOfRangeException(nameof(pyramid));
        var copy=(StageDefinition)MemberwiseClone();copy._presentationPyramid=pyramid;return copy;
    }

    /// <summary>Background music index (0..4): the original changes it every 2 stages.</summary>
    public int? SourceMusicIndex {get;init;}
    public int MusicIndex => SourceMusicIndex ?? ((Number - 1) % Common.GameConstants.StagesPerPyramid) / 2;

    /// <summary>8x8 grid of rooms (0 = no room). <c>[row, column]</c>.</summary>
    public required int[,] RoomGrid { get; init; }

    /// <summary>Rooms, in grid order.</summary>
    public required IReadOnlyList<RoomDefinition> Rooms { get; init; }

    /// <summary>Normal ladders.</summary>
    public required IReadOnlyList<LadderDefinition> Ladders { get; init; }

    /// <summary>Golden ladders (temporary).</summary>
    public required IReadOnlyList<LadderDefinition> GoldenLadders { get; init; }

    /// <summary>Items (weapons and tools).</summary>
    public required IReadOnlyList<ItemDefinition> Items { get; init; }

    /// <summary>Soul stones — collecting all of them opens the door.</summary>
    public required IReadOnlyList<StagePoint> SoulStones { get; init; }

    /// <summary>Exit door.</summary>
    public required DoorDefinition ExitDoor { get; init; }

    /// <summary>Sarcophagi — the only enemy spawners in the original.</summary>
    public required IReadOnlyList<CoffinDefinition> Coffins { get; init; }

    /// <summary>Mechanisms (swivel doors, pushable blocks...).</summary>
    public required IReadOnlyList<MechanismDefinition> Mechanisms { get; init; }

    /// <summary>Where the player starts.</summary>
    public required StagePoint PlayerStart { get; init; }

    /// <summary>Returns the room by number (1-based).</summary>
    public RoomDefinition Room(int index) => Rooms[index - 1];
}
