using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>Stage status.</summary>
public enum StageStatus
{
    /// <summary>Jogando.</summary>
    Playing,

    /// <summary>Left through the door.</summary>
    Cleared,

    /// <summary>Died (the scene decides: restart the stage or game over).</summary>
    Dead,
}

/// <summary>
/// Exit phases through the door — the original's <c>E257</c> counter, one step per row of the jump
/// table at <c>71E6</c>. The whole sequence lasts 266 units (the door opens, Vick goes in and the
/// curtain darkens the room) plus the stage-end screen (steps 4..8); only at the end does the stage
/// change (<c>72E1</c> increments <c>E242</c> and <c>7342</c> zeroes <c>E257</c>).
/// </summary>
public enum ExitPhase
{
    /// <summary>Nobody is leaving (playing normally).</summary>
    None,

    /// <summary>71F6 (<c>E257=1</c>): the door opens frame by frame, 30 ticks each.</summary>
    DoorOpening,

    /// <summary>721F (<c>E257=2</c>): Vick enters the door, 8 poses of 16 ticks.</summary>
    Entering,

    /// <summary>7296 (<c>E257=3</c>): the room edges darken from the top and the bottom, 12 steps.</summary>
    Curtain,

    /// <summary>72E1 (<c>E257=4..8</c>): the stage-end screen over the darkened room.</summary>
    ClearScreen,
}

/// <summary>
/// Sub-steps of the stage-end screen — steps 4 to 8 of the <c>71E6</c> table. The text appears
/// as soon as the curtain closes (72E1) and the password only 180 units later (7331 calls <c>697C</c>); then the
/// game waits for a key (733B) and only then changes stage (7342).
/// </summary>
public enum ExitClearStep
{
    /// <summary>Not on the stage-end screen.</summary>
    None,

    /// <summary>72E1: <c>NICE ! / LETS GO NEXT STAGE / SPECIAL BONUS / REST nn</c> por 180 unidades.</summary>
    Texts,

    /// <summary>7331: the password (<c>PASS WORD</c>) appears and the game waits for a key (733B).</summary>
    KeyWait,

    /// <summary>7342: the key already arrived; waits <c>E204</c> units and zeroes <c>E257</c>.</summary>
    Tail,
}

/// <summary>
/// <b>Rules of one stage</b> — synchronous and deterministic: the scene calls <see cref="Tick"/> at ~30 Hz
/// (1 ROM logic call = 1 tick) and animates/sounds the <see cref="Events"/> list.
///
/// The stage has several rooms in an 8x8 grid; leaving through an edge leads to the neighbor room (wrapping at the
/// end of the row/column, like the stage 1 ladder that leaves through the ceiling and comes back through the floor).
/// </summary>
/// <summary>Swivel door rotation state (0..3).</summary>
public sealed class MechanismRotationState
{
    /// <summary>Current phase (0..3).</summary>
    public int Phase;

    /// <summary>Timer (IX+6): counts down to zero to advance the phase.</summary>
    public int Timer = 10;

    /// <summary>Empurrando?</summary>
    public bool Pushing;

    /// <summary>Bit 1 of (IX+7): mechanism rotated to the right (phase 3).</summary>
    public bool Right;

    /// <summary>Pushing to the right?</summary>
    public bool PushRight;
}

public sealed partial class StageSession
{
    private readonly Dictionary<int, RoomMap> _rooms = new();
    private readonly List<GameEvent> _events = new();
    private readonly List<StagePoint> _soulStones;

    /// <summary>Rotation state of the swivel doors (0..3): phase, timer and direction.</summary>
    private readonly Dictionary<MechanismDefinition, MechanismRotationState> _mechanismStates = new();

    /// <summary>Traps (E600 type 4) per mechanism — see <see cref="StepTrap"/>.</summary>
    private readonly Dictionary<MechanismDefinition, TrapState> _traps = new();

    /// <summary>
    /// The death animation ended on this tick (<see cref="StepDeath"/>). The state only becomes
    /// <see cref="StageStatus.Dead"/> at the <b>end</b> of the tick: in the original the death routine ends
    /// inside the game loop itself, so enemies/projectiles still move on this frame and it is the
    /// <b>next</b> state (the "lost a life" one) that freezes them. Measured on the type 2 track
    /// (stage 3): without this the enemy stopped 1 px before the original.
    /// </summary>
    private bool _deathEnded;

    /// <summary>Stage definition (immutable).</summary>
    public StageDefinition Definition { get; }

    /// <summary>Current room (1-based).</summary>
    public int CurrentRoom { get; private set; }

    /// <summary>The player.</summary>
    public Player Player { get; } = new();

    /// <summary>Enemies and blocks (all rooms).</summary>
    public List<Enemy> Enemies { get; } = new();

    /// <summary>Ground items (all rooms).</summary>
    public List<FloorItem> Items { get; } = new();

    /// <summary>Knife/boomerang in flight.</summary>
    public List<Projectile> Projectiles { get; } = new();

    /// <summary>Sarcophagi.</summary>
    public List<Coffin> Coffins { get; } = new();

    /// <summary>Soul stones still on the ground.</summary>
    public IReadOnlyList<StagePoint> SoulStones => _soulStones;

    /// <summary>Total de soul stones do stage.</summary>
    public int SoulStoneTotal { get; }

    /// <summary>Has the door opened yet?</summary>
    public bool DoorOpen { get; private set; }

    /// <summary>Points gained in this stage.</summary>
    public int Score { get; private set; }

    /// <summary>
    /// <c>E245</c> threshold of the next extra life by score, in units of 10,000 points
    /// (<c>play_init</c> 4C1A sets 3 = 30,000).
    /// </summary>
    private int _extraLifeThreshold = GameConstants.ExtraLifeFirstThreshold;

    /// <summary>Status.</summary>
    public StageStatus Status { get; private set; } = StageStatus.Playing;

    /// <summary>Invincible (FESTIVAL password)?</summary>
    public bool Invincible { get; init; }

    /// <summary>Ticks since the start.</summary>
    public int TickCount { get; private set; }

    /// <summary>Events of the last <see cref="Tick"/>.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Builds the session from the definition.</summary>
    public string CampaignId {get;}
    /// <summary>Contest 4800: E204=180 decrements only on alternate logic calls.</summary>
    public const int ContestClearTicks=360;
    public StageSession(StageDefinition definition,string campaignId=Campaigns.Original)
    {
        Campaigns.StageCount(campaignId);CampaignId=campaignId;
        Definition = definition;
        foreach(var ladder in definition.GoldenLadders)_goldenLadders[ladder]=new GoldenLadderState();
        foreach (var room in definition.Rooms)
            _rooms[room.Index] = new RoomMap(room, definition.Ladders.Concat(definition.GoldenLadders));

        _soulStones = definition.SoulStones.ToList();
        SoulStoneTotal = _soulStones.Count;

        foreach (var item in definition.Items)
            Items.Add(new FloorItem { Kind = item.Kind, Room = item.Position.Room, X = item.Position.X, Y = item.Position.Y });

        // Pushable stone (E600 type 5): the 2x2 wall follows the stone through the map's `SetRock`
        // (in the ROM `stamp_actor` writes wall into the stone's cells while it is stopped and
        // `room_draw` re-stamps it; a fixed wall was left behind after pushing).
        foreach (var m in definition.Mechanisms.Where(m => m.Kind == MechanismKind.MovableBlock))
        {
            var block = new Enemy
            {
                Kind = EnemyKind.MovableBlock,
                Room = m.Position.Room,
                X = m.Position.X,
                Y = m.Position.Y,
            };
            Enemies.Add(block);
            MarkBlock(block, true);
        }

        // Swivel door rotation state (0..3): the ROM table gives the initial phase (0 or 3).
        foreach (var m in definition.Mechanisms.Where(m => m.Kind is MechanismKind.RotatingDoor or MechanismKind.Unknown2))
            _mechanismStates[m] = new MechanismRotationState { Phase = m.InitialPhase, Timer = 10, Right = m.Direction == 1 || m.InitialPhase == 3 };

        // Mark solid mechanisms (rotating doors, traps...) in the room map for collision:
        foreach (var m in definition.Mechanisms.Where(m => m.IsSolid))
        {
            if (_rooms.TryGetValue(m.Position.Room, out var roomMap))
            {
                int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
                if (m.Kind == MechanismKind.Unknown2)
                {
                    int col = m.Direction == 1 ? c0 + 1 : c0;
                    for (int r = r0; r < r0 + m.HeightCells; r++)
                        roomMap.SetMechanism(col, r, true);
                }
                else
                {
                    for (int r = r0; r < r0 + m.HeightCells; r++)
                        for (int c = c0; c < c0 + m.WidthCells; c++)
                            roomMap.SetMechanism(c, r, true);
                }
            }
        }

        // E600 type 4: the trap (4-cell bar that is floor and triggers when Vick leaves the top).
        foreach (var m in definition.Mechanisms.Where(m => m.Kind == MechanismKind.Unknown4))
            _traps[m] = new TrapState();

        foreach (var c in definition.Coffins)
            Coffins.Add(new Coffin { Position = c.Position, Kind = c.Kind, Level = c.Level, Cooldown = CoffinFirstCount });

        CurrentRoom = definition.PlayerStart.Room;
        Player.X = definition.PlayerStart.X;
        Player.Y = definition.PlayerStart.Y;

        Emit(GameEventKind.PlayerAppeared, Player.X, Player.Y);
    }

    /// <summary>Terrain of a room.</summary>
    public RoomMap Room(int index) => _rooms[index];

    /// <summary>Terrain of the current room.</summary>
    public RoomMap Map => _rooms[CurrentRoom];

    /// <summary>Terrain version of the current room (goes up when digging) - to rebuild the cached drawing.</summary>
    public int MapVersion => Map.Version;

    /// <summary>Solid mechanisms of the current room (the push doors occupy cells).</summary>
    public IEnumerable<MechanismDefinition> RoomMechanisms =>
        Definition.Mechanisms.Where(m => m.IsSolid && m.Position.Room == CurrentRoom);

    /// <summary>
    /// Does the cell belong to a solid mechanism? The original writes WALL into the map on those cells
    /// (63DA..6438) and the mechanism itself is drawn on top, but Vick CROSSES it by pushing -
    /// so the player's collision ignores them.
    /// </summary>
    public bool IsMechanismCell(int column, int row)
    {
        foreach (var m in RoomMechanisms)
        {
            int c0 = m.Position.X >> 3, r0 = m.Position.Y >> 3;
            if (m.Kind == MechanismKind.Unknown2)
            {
                int col = m.Direction == 1 ? c0 + 1 : c0;
                if (column == col && row >= r0 && row < r0 + m.HeightCells)
                    return true;
            }
            else if (column >= c0 && column < c0 + m.WidthCells && row >= r0 && row < r0 + m.HeightCells)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Current rotation phase of the swivel door (0..3) — 0 = closed to the right, 3 = closed to the left.</summary>
    public int MechanismPhase(MechanismDefinition m) =>
        _mechanismStates.TryGetValue(m, out var st) ? st.Phase : m.InitialPhase;

    /// <summary>All swivel doors in the current room with the current phase (for drawing).</summary>
    public IEnumerable<(MechanismDefinition Mechanism, int Phase)> RoomMechanismPhases =>
        RoomMechanisms.Select(m => (m, MechanismPhase(m)));

    /// <summary>Swivel door passages in the current room for navigation/pathfinding.</summary>
    public IEnumerable<RevolvingDoorPassage> GetRevolvingDoorPassages()
    {
        foreach (var m in RoomMechanisms)
        {
            if (m.Kind is not (MechanismKind.RotatingDoor or MechanismKind.Unknown2)) continue;
            if (!_mechanismStates.TryGetValue(m, out var st)) continue;
            int c0 = m.Position.X >> 3;
            int r0 = m.Position.Y >> 3;
            int row = r0 + m.HeightCells - 2;
            int leftCol = c0 - 2;
            int rightCol = c0 + m.WidthCells;
            bool canPushRight, canPushLeft;
            if (m.Kind == MechanismKind.Unknown2)
            {
                canPushRight = m.Direction == 0;
                canPushLeft = m.Direction == 1;
            }
            else
            {
                canPushRight = !st.Right || (st.Pushing && st.PushRight);
                canPushLeft = st.Right || (st.Pushing && !st.PushRight);
            }
            yield return new RevolvingDoorPassage(leftCol, rightCol, row, canPushRight, canPushLeft);
        }
    }

    /// <summary>
    /// Swivel door state machine (0xBABF..0xBB42, 0x827F..0x829B, 0xBC15..0xBC4E): a push when Vick is adjacent
    /// and presses the right direction, with a 10/8 frame timer; for type 1 the phase rises/falls up to 3/0 and the
    /// door "locks" on that side (bit 1 of (IX+7)); for type 2 (one-way swivel door) it does a 360-degree rotation (0 -> 1 -> 2 -> 3 -> 0).
    /// </summary>
    private void UpdateMechanisms(PlayerInput input)
    {
        UpdateFallingWalls();
        foreach (var m in RoomMechanisms)
        {
            if (m.Kind == MechanismKind.Unknown4)
            {
                StepTrap(m);
                continue;
            }

            if (m.Kind is not (MechanismKind.RotatingDoor or MechanismKind.Unknown2)) continue;
            var st = _mechanismStates[m];
            int mx = m.Position.X, my = m.Position.Y;
            int height = m.HeightCells;

            if (st.Pushing)
            {
                st.Timer--;
                if (st.Timer > 0) continue;
                if (m.Kind == MechanismKind.Unknown2)
                {
                    // 0xBC37: Phase advances 0 -> 1 -> 2 -> 3 -> 0
                    st.Phase++;
                    if (st.Phase >= 4)
                    {
                        st.Phase = 0;
                        st.Pushing = false;
                        st.Timer = 10;
                    }
                    else
                    {
                        st.Timer = 8;
                    }
                }
                else
                {
                    // animation step (0xBB0E/0xBB2B) for type 1:
                    if (st.Right)
                    {
                        if (st.Phase > 0) st.Phase--;
                        if (st.Phase == 0)
                        {
                            st.Pushing = false;
                            st.Right = false;
                        }
                        else st.Timer = 10;
                    }
                    else
                    {
                        if (st.Phase < 3) st.Phase++;
                        if (st.Phase == 3)
                        {
                            st.Pushing = false;
                            st.Right = true;
                        }
                        else st.Timer = 10;
                    }
                }
                continue;
            }

            // push (0xBABF..0xBB42, 0xBC15..0xBC4E): Vick must be standing, within the mechanism's vertical band,
            // up to 20 px from the door side, pressing the direction.
            if (Player.State != PlayerState.Standing) continue;
            int feet = Player.Y + Physics.ActorSize;
            if (feet <= my || feet > my + height * GameConstants.CellSize) continue;
            st.Timer--;
            if (st.Timer > 0) continue;
            st.Timer = 0;

            if (m.Kind == MechanismKind.Unknown2)
            {
                if (m.Direction == 0)
                {
                    // Push from the left to the right:
                    if (Player.X >= mx) { st.Timer = 10; continue; }
                    if (Player.X + 20 < mx) { st.Timer = 10; continue; }
                    if (!input.Right) { st.Timer = 10; continue; }
                    st.Phase = 1;
                    st.Pushing = true;
                    st.PushRight = true;
                    st.Timer = 8;
                    PlaySound(OriginalSounds.PushDoor, m.Position.Room);
                }
                else
                {
                    // Push from the right to the left:
                    if (Player.X < mx) { st.Timer = 10; continue; }
                    if (Player.X - 20 >= mx) { st.Timer = 10; continue; }
                    if (!input.Left) { st.Timer = 10; continue; }
                    st.Phase = 1;
                    st.Pushing = true;
                    st.PushRight = false;
                    st.Timer = 8;
                    PlaySound(OriginalSounds.PushDoor, m.Position.Room);
                }
                continue;
            }

            if (!st.Right)
            {
                // push from the right: player on the left, right key.
                if (Player.X >= mx) { st.Timer = 10; continue; }
                if (Player.X + 20 < mx) { st.Timer = 10; continue; }
                if (!input.Right) { st.Timer = 10; continue; }
                st.Phase = System.Math.Min(3, st.Phase + 1);
                st.Pushing = true;
                st.PushRight = true;
                st.Timer = 8;
                // 0xBABF..0xBAC4: the counter reached zero and the phase advanced — the ROM plays sfx_21 here.
                PlaySound(OriginalSounds.PushDoor, m.Position.Room);
                if (st.Phase == 3) { st.Right = true; st.Pushing = false; st.Timer = 10; }
            }
            else
            {
                // push from the left: player on the right, left key.
                if (Player.X < mx) { st.Timer = 10; continue; }
                if (Player.X - 20 >= mx) { st.Timer = 10; continue; }
                if (!input.Left) { st.Timer = 10; continue; }
                st.Phase = System.Math.Max(0, st.Phase - 1);
                st.Pushing = true;
                st.PushRight = false;
                st.Timer = 8;
                // 0xBAF4..0xBB05: same for the other side (the ROM does `jp sfx_21`).
                PlaySound(OriginalSounds.PushDoor, m.Position.Room);
                if (st.Phase == 0) { st.Right = false; st.Pushing = false; st.Timer = 10; }
            }
        }
    }

    private void Emit(GameEventKind kind, int x = 0, int y = 0, ItemKind item = ItemKind.None, int sound = 0) =>
        _events.Add(new GameEvent(kind, x, y, item, sound));

    /// <summary>State of a trap (E600 type 4) — the counters <c>IX+0x0A</c>, <c>IX+0x0B</c> and <c>IX+6</c>.</summary>
    private sealed class TrapState
    {
        public bool Armed;
        public bool WasArmed;
        public int Count;
        public bool Spent;
    }

    /// <summary>The trap already fired (its drawing is gone and the map cells were already zeroed).</summary>
    public bool IsMechanismSpent(MechanismDefinition m) =>
        m.Kind == MechanismKind.Unknown4 && _traps.TryGetValue(m, out var st) && st.Spent;

    /// <summary>
    /// 0xBD21 <c>tick_trap</c> (E600 type 4): the 4-cell bar is <b>floor</b>. Vick on top of it
    /// (feet at <c>0x14</c> from the bar's Y, inside the 8 px band, and the X center less than 0x20 from its X)
    /// keeps <c>IX+0x0A</c> armed. When leaving the top <b>standing</b>, <c>IX+6</c> counts 2 and the
    /// <c>trap_punch</c> (0xBD7A) plays 0x27; the <c>trap_erase</c> (0xBD65) zeroes the 4 cells — the
    /// passage opens and the floor disappears (whoever is on top falls).
    /// </summary>
    private void StepTrap(MechanismDefinition m)
    {
        if (!_traps.TryGetValue(m, out var st) || st.Spent)
            return;
        if (m.Position.Room != CurrentRoom)
            return;

        int dy = Player.Y + 0x14 - m.Position.Y;
        bool armed = dy is >= 0 and < 8 && ((Player.X + 8 - m.Position.X) & 0xFF) < 0x20;

        st.WasArmed = st.Armed;
        st.Armed = armed;
        if (armed)
            return;

        if (!st.WasArmed || Player.State != PlayerState.Standing)
            return;

        if (++st.Count < 2)
            return;

        st.Spent = true;
        int col = m.Position.X >> 3, row = m.Position.Y >> 3;
        for (int c = col; c < col + m.WidthCells; c++)
            Map.Clear(c, row);

        PlaySound(OriginalSounds.Trap, m.Position.Room);
    }

    /// <summary>
    /// 4C20 <c>add_score</c>: adds the points (in BCD in the original) and, when the high byte of the score
    /// (<c>E228</c>, the 10,000 digits) reaches the <c>E245</c> threshold, grants an extra life
    /// (<c>E240 += 1</c>, cap 0x99) with the "card" sound (42AB). The threshold starts at 3 (30,000 points,
    /// <c>play_init</c>) and goes up by 3 on each extra life; adding 3 to 0x99 overflows the byte and saturates
    /// at <c>0xFF</c> (2,550,000 points).
    /// </summary>
    private void AddScore(int points)
    {
        Score += points;

        // The 3-byte BCD score (E226..E228) has the same numeric value as Score / 10,000.
        if (Score / 10000 < _extraLifeThreshold)
            return;

        _extraLifeThreshold = _extraLifeThreshold >= 0x99
            ? 0xFF
            : _extraLifeThreshold + GameConstants.ExtraLifeThresholdStep;
        Emit(GameEventKind.ExtraLife, sound: OriginalSounds.ExtraLifeByScore);
    }

    /// <summary>Original sound (B294: the mummy's only play if it is in the player's room).</summary>
    private void PlaySound(int sound, int room = 0)
    {
        if (room == 0 || room == CurrentRoom)
            Emit(GameEventKind.Sound, sound: sound);
    }

    /// <summary>Advances one logic tick — 1 ROM logic call (~1/30 s).</summary>
    public IReadOnlyList<GameEvent> Tick(PlayerInput input)
    {
        _events.Clear();
        if (Status != StageStatus.Playing)
            return _events;

        TickCount++;
        // 71DE/49B4: during the exit through the door the original only runs the E257 state machine —
        // enemies, sarcophagi and projectiles stay still (the game switches to state 9).
        if (Player.State == PlayerState.Exiting)
        {
            UpdatePlayer(input);
            return _events;
        }

        UpdatePlayer(input);
        if (Status != StageStatus.Playing)
            return _events;

        UpdateProjectiles();
        UpdateCoffinsOriginal();
        UpdateEnemies();
        CheckEnemyContact();
        UpdateMechanisms(input);
        UpdateGoldenLadders();
        // 4711: the e300_enemy_hit is the LAST step of play_tick (after vic_enemy_overlap) — the
        // hit test sees the positions already moved on this tick.
        CheckToolHits();
        // The death ended on this tick: the "lost a life" state (which freezes enemies and projectiles)
        // only comes in now, after their frame — see _deathEnded.
        if (_deathEnded)
            Status = StageStatus.Dead;
        return _events;
    }

    /// <summary>Suicide (F5 in the original): dies and restarts the stage losing a life.</summary>
    public void Suicide() => Die(force: true);

    /// <summary>
    /// Diagnostic (<c>KINGSVALLEY_PLAYER=room,x,y</c>): places Vick in a room/position — used by the
    /// documentation scripts (`tools/docs`) to record each mechanic without playing through the level.
    /// </summary>
    public void PlacePlayer(int room, int x, int y)
    {
        if (room < 1 || room > Definition.Rooms.Count)
            return;

        CurrentRoom = room;
        Player.X = x;
        Player.Y = y;
        Player.State = PlayerState.Standing;
        Player.Pose = PlayerPoses.Stand(Player.Facing);
        Player.AnimPhase = 0;
        Player.AnimTimer = 1;
    }

    /// <summary>
    /// Diagnostic (<c>KINGSVALLEY_EXIT</c>): takes every soul stone, opens the door and places Vick
    /// on top of it — on the next tick <see cref="AtOpenDoor"/> (8FBE) enters by itself, like the
    /// game. Useful to get straight to the exit sequence (71E6) without playing the whole stage.
    /// </summary>
    public void DebugEnterDoor()
    {
        if (Status != StageStatus.Playing || Player.State != PlayerState.Standing)
            return;

        _soulStones.Clear();
        DoorOpen = true;
        var d = Definition.ExitDoor.Position;
        CurrentRoom = d.Room;
        // 8FD3: (door x + 10) - x in [0,4) and (door y + 0x12) - y in [0,4) — center of the window.
        Player.X = d.X + 8;
        Player.Y = d.Y + 0x12 - 2;
    }

    // ── Collection, door and exit ───────────────────────────────────────────────────

    private void CollectAt(Box body)
    {
        for (int i = _soulStones.Count - 1; i >= 0; i--)
        {
            var s = _soulStones[i];
            if (s.Room != CurrentRoom || !body.Inset(2).Intersects(new Box(s.X, s.Y, 16, 16)))
                continue;

            _soulStones.RemoveAt(i);
            AddScore(Scoring.SoulStone);
            Emit(GameEventKind.CollectedSoulStone, s.X, s.Y, sound: OriginalSounds.PickSoulStone);
            if (_soulStones.Count == 0 && !DoorOpen)
            {
                DoorOpen = true;
                var d = Definition.ExitDoor.Position;
                Emit(GameEventKind.DoorOpened, d.X, d.Y, sound: OriginalSounds.DoorUnlocked);
            }
        }

        if (Player.Holding != ItemKind.None)
            return;

        var item = Items.FirstOrDefault(it => it.Room == CurrentRoom && body.Inset(2).Intersects(it.Bounds));
        if (item == null)
            return;

        Items.Remove(item);
        Player.Holding = item.Kind;
        Emit(GameEventKind.PickedItem, item.X, item.Y, item.Kind, OriginalSounds.PickItem);
    }

    /// <summary>Box of the exit door (in its own room).</summary>
    public Box DoorBounds
    {
        get
        {
            var d = Definition.ExitDoor;
            return new Box(d.Position.X, d.Position.Y, d.WidthCells * GameConstants.CellSize, d.HeightCells * GameConstants.CellSize);
        }
    }

    /// <summary>
    /// 8FBE: with all the stones, the player enters by itself when standing at the door position:
    /// (door y + 0x12) - y in [0,4) and (door x + 10) - x in [0,4).
    /// </summary>
    private bool AtOpenDoor()
    {
        if (!DoorOpen || CurrentRoom != Definition.ExitDoor.Position.Room || Player.State != PlayerState.Standing)
            return false;

        var d = Definition.ExitDoor.Position;
        int dy = d.Y + 0x12 - Player.Y, dx = d.X + 10 - Player.X;
        return dy is >= 0 and < 4 && dx is >= 0 and < 4;
    }

    /// <summary>Door frame (<c>E2F6</c>): 0 closed, 1 opening, 2 open.</summary>
    public int DoorFrame { get; private set; }

    /// <summary>Exit phase through the door (<c>E257</c>); <see cref="ExitPhase.None"/> = playing.</summary>
    public ExitPhase Exit { get; private set; }

    /// <summary>
    /// 8 px bands already darkened on each side of the room (0..12) — the <c>72D2</c> curtain that closes
    /// the stage before the stage change. The drawing lives in the renderer.
    /// </summary>
    public int CurtainBands { get; private set; }

    /// <summary>
    /// Sub-step of the stage-end screen (steps 4..8 of <c>71E6</c>); only meaningful with
    /// <see cref="Exit"/> = <see cref="ExitPhase.ClearScreen"/>. The password only exists from
    /// <see cref="ExitClearStep.KeyWait"/> on (7331 calls <c>697C</c>, which draws it).
    /// </summary>
    public ExitClearStep ClearStep { get; private set; }

    /// <summary>Studs on the door: one per soul stone still missing (9045).</summary>
    public int DoorStuds => _soulStones.Count;

    // ⚠️ These constants are CALL units of the original — 1 call = 1 port tick. The
    // ROM state dispatcher (46D4) runs inside the VBL ISR (4086), but the ISR is held by
    // E206 while the previous frame has not finished: the effective rate is ~30 Hz (measured: E203 ~30.7/s).
    // The state 9 handler (4B06 → clear_tick) does not have the `LD A,(0xE203); RRA; RET nc` gate of the
    // game states — but the gate only halves a rate that IS ALREADY ~30 Hz, so clear_tick
    // runs at the same rate as the others: 266 units ≈ 8.9 s. Checked on the longplay: the door closes at
    // 128.0 s and "NICE! LET'S GO NEXT STAGE" appears at 134.0 s → 6 s for the text (E204 =
    // 0x3C + 0x78 = 180 units) = 30 units/s ✓. StepExit is called ONCE per tick.
    private const int DoorStepUnits = 0x1E;   // 7204: E2F7 = 0x1E (30 units per door frame)
    private const int DoorFrames = 3;         // E2F6: 0 closed, 1 opening, 2 open (72BB)
    private const int EnterPoseUnits = 16;    // 7230: the pose changes when bit 4 of E259 clears
    private const int EnterPoses = 8;         // 7228/722A: 4 steps (E258) x 2 sub-poses = 128 units
    private const int EnterRise = 2;          // 7235: rises 2 px every 16 units
    private const int CurtainStepUnits = 4;   // 7292: E259 = 4 (one curtain step every 4 units)
    private const int CurtainBandsTotal = 12; // 72BA: E258 reaches 12 — 24 rows of 8 px = the room
    private const int ClearTextUnits = 0x3C + 0x78; // 72F5/732D: E204 = 0x3C and then 0x78 (180 units = 6 s)
    private const int ClearTailUnits = 32;    // 733B: E204 = the key code — 32 units (estimated)

    /// <summary>
    /// One <b>unit</b> of the exit sequence through the door (jumps of <c>71E6</c>, one per
    /// <c>E257</c>) — <b>266 units</b> (~8.9 s) until the curtain closes, plus the stage-end
    /// screen (steps 4..8, see <see cref="ExitClearStep"/>):
    /// <list type="number">
    /// <item><b>1</b> (71F6): the door opens one frame every 30 units and the game only waits 30 more
    /// (<c>E2F0</c> 0/1 animate, 2 only counts) — <b>90</b> units;</item>
    /// <item><b>2</b> (721F): Vick enters the door — the pose changes every <c>E259</c> = 16 units and on
    /// each change he rises 2 px (7235). That is 4 steps of <c>E258</c> (722A <c>CP 4</c>) = <b>128</b>
    /// units; at <c>722C</c> (<c>JP nc,0x7288</c>) the original clears the SAT (5D41). The entry art
    /// (<c>5937</c>) covers only 5 pattern rows — poses 5..7 draw the residue that was in
    /// VRAM (5 empty), and the port does not draw any of them;</item>
    /// <item><b>3</b> (7296): the room darkens in 12 steps of 4 units (24 rows of 8 px, from the top and
    /// the bottom) — <b>48</b> units — and, when it closes, <c>E257 = 4</c>;</item>
    /// <item><b>4..8</b> (72E1..7342): the stage-end screen — the text for <b>180</b> units
    /// (6 s), the password and the wait for a key, and 32 more units after it. Only then
    /// <c>E257 = 0</c> and the stage changes.</item>
    /// </list>
    /// <para>
    /// ⚠️ <c>E257</c> is <b>1-based</b>: the runner does <c>LD A,(0xE257); DEC A</c> (71E1..71E3) before the
    /// jump through the table that starts right after the <c>CALL</c> (71E6) — <c>E257 = 1</c> lands on 71F6
    /// (step 1). The auto-enter (8FBE) zeroes <c>E2F0/E257/E258/E259</c>, clears the SAT (907F) and plays sound
    /// 0x23; the port's <see cref="ExitPhase"/> is just the phase name.
    /// </para>
    /// <para>
    /// ⚠️ Pose order: the original's raw index is <c>2*E258 + bit4(E259)</c> (7244..724D).
    /// <c>E259</c> counts down from <c>0x20</c> (7222), so bit 4 is 1 in the <b>first</b>
    /// half of each step: the order is <b>1, 0, 3, 2, 5, 4, 7, 6</b> — the (step, still) pair of each
    /// group. That interleave is what gives the walking bob; the monotonic order 0,1,2,3,4 made pose 3
    /// (step) come after 2 (already sunk), so Vick "went up again" — the symptom of
    /// "crouching/flattening". Both validated cartridges provide eight native entry poses:
    /// MSX1 uses loader 531F and SAT writer 8CE4; MSX2 uses its ABB9 pattern stream.
    /// The earlier hypothesis that poses 5..7 should be hidden was incorrect.
    /// </para>
    /// </summary>
    private void StepExit(PlayerInput input)
    {
        if (Exit == ExitPhase.None || Status != StageStatus.Playing)
            return;   // the tick's 2nd unit may fall after the end (the stage already changed)

        var p = Player;
        int t = p.StateTick++;
        switch (Exit)
        {
            case ExitPhase.DoorOpening:
    // 7204: E2F6 (door frame) rises every 30 units; at E2F0 = 2 the game waits for the last block.
                DoorFrame = t / DoorStepUnits;
                if (t >= DoorStepUnits * DoorFrames - 1)
                {
                    DoorFrame = 2;                    // 7214: E257 = 2 (enters)
                    Exit = ExitPhase.Entering;
                    p.StateTick = 0;
                }

                return;

            case ExitPhase.Entering:
                // 7230/7244: the raw pose index is 2*E258 + bit4(E259). E259 counts down from 0x20,
                // so bit 4 is 1 in 0x1F..0x10 and 0 in 0x0F..0x01 and on the reload (0x20). When E259
                // reaches zero (0x01->0x00) E258 increments BEFORE the pose is computed, so the transition
                // (unit 31) uses the next group with bit 4 = 0: the real order is
                // 1, 0, 2, 3, 2, 4, 5, 4, 6, 7, 6 (poses 2/4/6 are the group transition ones).
                int grupo = t / (EnterPoseUnits * 2);            // 7228: E258 (CP 4 → 4 groups)
                int unidade = t % (EnterPoseUnits * 2);          // 0..31 inside the group
                int pose;
                if (unidade <= EnterPoseUnits - 1) pose = grupo * 2 + 1;       // bit4 = 1 (0x1F..0x10)
                else if (unidade <= EnterPoseUnits * 2 - 2) pose = grupo * 2;   // bit4 = 0 (0x0F..0x01)
                else pose = (grupo + 1) * 2;                                    // unit 31: reload 0x20, E258++
                p.Pose = Math.Min(EnterPoses - 1, pose);
                // 7235: Y -= 2 when bit 4 clears — 7 rises of 2 px (16, 32, ... 112).
                if (t % EnterPoseUnits == EnterPoseUnits - 1 && t < EnterPoses * EnterPoseUnits - 1)
                    p.Y -= EnterRise;

                if (t >= EnterPoses * EnterPoseUnits - 1)
                {
                    Exit = ExitPhase.Curtain;         // 7288: CALL 5D41 (clears the SAT) and E257 = 3
                    CurtainBands = 0;
                    p.StateTick = 0;
                }

                return;

            case ExitPhase.Curtain:
                // 7299: E259 counts 4 units and only then the curtain closes one more band on each side.
                if (t % CurtainStepUnits == CurtainStepUnits - 1)
                    CurtainBands++;
                if (CurtainBands >= CurtainBandsTotal)
                {
                    Exit = ExitPhase.ClearScreen;     // 72E1: E257=4 — the stage-end screen
                    ClearStep = ExitClearStep.Texts;
                    Emit(GameEventKind.StageCleared, p.X, p.Y);
                    p.StateTick = 0;
                }

                return;

            case ExitPhase.ClearScreen:
                StepClearScreen(input, t);
                return;
        }
    }

    /// <summary>
    /// Steps 4 to 8 of the exit (72E1..7342): the stage-end text, the password and the wait for the key.
    /// Step 5 (7313) still counts a bonus into <c>E240</c> (2 per second, up to 0x99) that goes into the
    /// score — the screen does not change, so it is not ported.
    /// </summary>
    private void StepClearScreen(PlayerInput input, int t)
    {
        if(CampaignId==Campaigns.Contest)
        {
            // Contest 47CF/4800: no bonus/password/key wait; return to publication selection.
            if(t>=ContestClearTicks-1){Status=StageStatus.Cleared;ClearStep=ExitClearStep.None;}
            return;
        }
        switch (ClearStep)
        {
            case ExitClearStep.Texts:
                // 72F5: E204 = 0x3C (60 units) and step 5 (7324) reloads with 0x78 (120) — the text
                // stays 180 units (6 s, measured on the longplay) on screen before the password.
                if (t >= ClearTextUnits - 1)
                {
                    ClearStep = ExitClearStep.KeyWait;   // 7331: CALL 697C draws the password; E257 = 7
                    Player.StateTick = 0;
                }

                return;

            case ExitClearStep.KeyWait:
                // 733B: LD A,(0xE207); AND A; RET z — E207 is the key/trigger. The port does not read the key
                // code, only the command (space/Z), which is what the original accepts here.
                if (input.Action)
                {
                    ClearStep = ExitClearStep.Tail;      // 730B: E204 = the key code; E257 = 8
                    Player.StateTick = 0;
                }

                return;

            case ExitClearStep.Tail:
                // 7342: DEC (0xE204); RET nz; LD (0xE257),0 — the E204 here is the key code, so
                // the real wait depends on which key was pressed (32 units is an estimate).
                if (t >= ClearTailUnits - 1)
                {
                    Status = StageStatus.Cleared;
                    ClearStep = ExitClearStep.None;
                }

                return;
        }
    }

    private void Clear()
    {
        Player.State = PlayerState.Exiting;
        Player.StateTick = 0;
        Player.Pose = 0;
        DoorFrame = 0;
        Exit = ExitPhase.DoorOpening;
        CurtainBands = 0;
        ClearStep = ExitClearStep.None;
        Emit(GameEventKind.DoorEntered, Player.X, Player.Y, sound: OriginalSounds.DoorEnter);
    }

    private void Die(bool force = false)
    {
        if ((Invincible && !force) || Player.State == PlayerState.Dying)
            return;

        Player.State = PlayerState.Dying;
        Player.StateTick = 0;
        Player.Pose = 0;
        Player.AnimPhase = 0;
        Player.AnimTimer = PlayerPoses.DeathFrameTicks;
        Emit(GameEventKind.PlayerDied, Player.X, Player.Y, sound: OriginalSounds.PlayerHit);
    }

    // ── Room change ─────────────────────────────────────────────────────────────────

    private void CheckRoomExit()
    {
        int cx = Player.X + Physics.ActorSize / 2, cy = Player.Y + Physics.ActorSize / 2;
        int dx = cx < 0 ? -1 : cx >= GameConstants.PlayfieldWidth ? 1 : 0;
        int dy = cy < 0 ? -1 : cy >= GameConstants.PlayfieldHeight ? 1 : 0;
        if (dx == 0 && dy == 0)
            return;

        CurrentRoom = RoomGridNavigator.Neighbor(Definition.RoomGrid, CurrentRoom, dx, dy);
        Player.X -= dx * GameConstants.PlayfieldWidth;
        Player.Y -= dy * GameConstants.PlayfieldHeight;
        // The tools are NOT erased: each one keeps its own room (IX+3) and, in the room it
        // is in, it keeps flying/falling (0xA826 step_wrap / 0xAA17 tool_wrap_y) — as in the original.
        ClearDangerousEnemiesNearPlayer();   // AFB4 (4AFB): arriving in the room, removes whoever would kill immediately
        Emit(GameEventKind.RoomChanged, Player.X, Player.Y);
    }
}

/// <summary>Score (values to confirm in the original — soul stone observed on the longplay).</summary>
public static class Scoring
{
    /// <summary>Soul stone (observado: +500 no HUD do longplay).</summary>
    public const int SoulStone = 500;

    /// <summary>Enemy hit: BCD 0x100 added at 9B76 (the soul stone adds 0x500 at 9A04).</summary>
    public const int Enemy = 100;
}
