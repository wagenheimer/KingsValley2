#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

public sealed partial class StageSession
{
    /// <summary>
    /// Serializes the complete snapshot of the active gameplay session into a <see cref="StageSessionData"/> DTO
    /// for instant save states (player position/physics/state, enemies, projectiles, mechanisms, doors, items, digging).
    /// </summary>
    public StageSessionData CaptureState()
    {
        var data = new StageSessionData
        {
            StageNumber = Definition.Number,
            CurrentRoom = CurrentRoom,
            Score = Score,
            ExtraLifeThreshold = _extraLifeThreshold,
            TickCount = TickCount,
            DoorOpen = DoorOpen,
            DoorFrame = DoorFrame,
            Exit = Exit,
            ExitStep = ClearStep,
            CurtainBands = CurtainBands,
            Invincible = Invincible,
            DigTimer = _digTimer,
            DigStage = _digStage,
            Player = new PlayerData
            {
                X = Player.X,
                Y = Player.Y,
                Facing = Player.Facing,
                State = Player.State,
                Holding = Player.Holding,
                StateTick = Player.StateTick,
                StepCounter = Player.StepCounter,
                Pose = Player.Pose,
                JumpDirection = Player.JumpDirection,
                XFraction = Player.XFraction,
                YFraction = Player.YFraction,
                VelocityY = Player.VelocityY,
                AnimPhase = Player.AnimPhase,
                AnimTimer = Player.AnimTimer,
            },
        };

        if (Crumble.HasValue)
        {
            var c = Crumble.Value;
            data.Crumble = new DigCrumbleData
            {
                X = c.X,
                Y = c.Y,
                Floor = c.Floor,
                Stage = c.Stage,
                FacingRight = c.FacingRight,
            };
        }

        // Soul stones
        foreach (var ss in _soulStones)
            data.SoulStones.Add(ss);

        // Floor Items
        foreach (var it in Items)
            data.Items.Add(new FloorItemData { Kind = it.Kind, Room = it.Room, X = it.X, Y = it.Y });

        // Enemies
        foreach (var e in Enemies)
        {
            data.Enemies.Add(new EnemyData
            {
                Kind = e.Kind,
                Room = e.Room,
                X = e.X,
                XFraction = e.XFraction,
                Y = e.Y,
                Facing = e.Facing,
                State = e.State,
                Timer = e.Timer,
                ArcTick = e.ArcTick,
                ClimbTargetY = e.ClimbTargetY,
                Tick = e.Tick,
                CoffinIndex = e.CoffinIndex,
                Frame = e.Frame,
                Vanishing = e.Vanishing,
                Phase = e.Phase,
                Dir = e.Dir,
                SavedDir = e.SavedDir,
                YFraction = e.YFraction,
                SpeedY = e.SpeedY,
                SpeedX = e.SpeedX,
                Moving = e.Moving,
                Bumps = e.Bumps,
                RollCycles = e.RollCycles,
                FallTicks = e.FallTicks,
                RockCellCol = e.RockCell?.Column,
                RockCellRow = e.RockCell?.Row,
                Yellow = e.Yellow,
                HopX = e.HopX,
                HopLow = e.HopLow,
                LastLadder = e.LastLadder,
                Reversals = e.Reversals,
                Hops = e.Hops,
                Flags = e.Flags,
            });
        }

        // Projectiles
        foreach (var p in Projectiles)
        {
            data.Projectiles.Add(new ProjectileData
            {
                Kind = p.Kind,
                X = p.X,
                Y = p.Y,
                Room = p.Room,
                Facing = p.Facing,
                Phase = p.Phase,
                Anim = p.Anim,
                Step = p.Step,
                Timer = p.Timer,
                HitLocked = p.HitLocked,
            });
        }

        // Coffins
        foreach (var c in Coffins)
        {
            data.Coffins.Add(new CoffinData
            {
                Room = c.Position.Room,
                X = c.Position.X,
                Y = c.Position.Y,
                Kind = c.Kind,
                Level = c.Level,
                Cooldown = c.Cooldown,
                Armed = c.Armed,
                Open = c.Open,
                Look = c.Look,
            });
        }

        // Rotating doors
        foreach (var kvp in _mechanismStates)
        {
            var m = kvp.Key;
            var st = kvp.Value;
            data.RotatingDoors.Add(new RotatingDoorData
            {
                Room = m.Position.Room,
                X = m.Position.X,
                Y = m.Position.Y,
                Phase = st.Phase,
                Timer = st.Timer,
                Pushing = st.Pushing,
                Right = st.Right,
                PushRight = st.PushRight,
            });
        }

        // Traps
        foreach (var kvp in _traps)
        {
            var m = kvp.Key;
            var st = kvp.Value;
            data.Traps.Add(new TrapData
            {
                Room = m.Position.Room,
                X = m.Position.X,
                Y = m.Position.Y,
                Armed = st.Armed,
                WasArmed = st.WasArmed,
                Count = st.Count,
                Spent = st.Spent,
            });
        }

        // Room maps
        foreach (var kvp in _rooms)
        {
            var cells = kvp.Value.Snapshot();
            data.RoomCells[kvp.Key] = cells.Select(c => (int)c).ToArray();
        }

        return data;
    }

    /// <summary>
    /// Restores the gameplay session state from a <see cref="StageSessionData"/> snapshot,
    /// reconstructing actors, soulstones, items, mechanisms, and physics state faithfully.
    /// </summary>
    public void RestoreFrom(StageSessionData data)
    {
        CurrentRoom = data.CurrentRoom;
        Score = data.Score;
        _extraLifeThreshold = data.ExtraLifeThreshold;
        TickCount = data.TickCount;
        DoorOpen = data.DoorOpen;
        DoorFrame = data.DoorFrame;
        Exit = data.Exit;
        ClearStep = data.ExitStep;
        CurtainBands = data.CurtainBands;
        _digTimer = data.DigTimer;
        _digStage = data.DigStage;

        if (data.Crumble != null)
        {
            var c = data.Crumble;
            Crumble = new DigCrumble(c.X, c.Y, c.Floor, c.Stage, c.FacingRight);
        }
        else
        {
            Crumble = null;
        }

        // Player
        Player.X = data.Player.X;
        Player.Y = data.Player.Y;
        Player.Facing = data.Player.Facing;
        Player.State = data.Player.State;
        Player.Holding = data.Player.Holding;
        Player.StateTick = data.Player.StateTick;
        Player.StepCounter = data.Player.StepCounter;
        Player.Pose = data.Player.Pose;
        Player.JumpDirection = data.Player.JumpDirection;
        Player.XFraction = data.Player.XFraction;
        Player.YFraction = data.Player.YFraction;
        Player.VelocityY = data.Player.VelocityY;
        Player.AnimPhase = data.Player.AnimPhase;
        Player.AnimTimer = data.Player.AnimTimer;

        // Soul stones
        _soulStones.Clear();
        foreach (var ss in data.SoulStones)
            _soulStones.Add(ss);

        // Floor items
        Items.Clear();
        foreach (var it in data.Items)
            Items.Add(new FloorItem { Kind = it.Kind, Room = it.Room, X = it.X, Y = it.Y });

        // Enemies
        Enemies.Clear();
        foreach (var ed in data.Enemies)
        {
            var e = new Enemy
            {
                Kind = ed.Kind,
                CoffinIndex = ed.CoffinIndex,
            };
            e.Room = ed.Room;
            e.X = ed.X;
            e.XFraction = ed.XFraction;
            e.Y = ed.Y;
            e.Facing = ed.Facing;
            e.State = ed.State;
            e.Timer = ed.Timer;
            e.ArcTick = ed.ArcTick;
            e.ClimbTargetY = ed.ClimbTargetY;
            e.Tick = ed.Tick;
            e.Frame = ed.Frame;
            e.Vanishing = ed.Vanishing;
            e.Phase = ed.Phase;
            e.Dir = ed.Dir;
            e.SavedDir = ed.SavedDir;
            e.YFraction = ed.YFraction;
            e.SpeedY = ed.SpeedY;
            e.SpeedX = ed.SpeedX;
            e.Moving = ed.Moving;
            e.Bumps = ed.Bumps;
            e.RollCycles = ed.RollCycles;
            e.FallTicks = ed.FallTicks;
            e.RockCell = ed.RockCellCol.HasValue && ed.RockCellRow.HasValue
                ? (ed.RockCellCol.Value, ed.RockCellRow.Value)
                : null;
            e.Yellow = ed.Yellow;
            e.HopX = ed.HopX;
            e.HopLow = ed.HopLow;
            e.LastLadder = ed.LastLadder;
            e.Reversals = ed.Reversals;
            e.Hops = ed.Hops;
            e.Flags = ed.Flags;

            Enemies.Add(e);
        }

        // Projectiles
        Projectiles.Clear();
        foreach (var pd in data.Projectiles)
        {
            var p = new Projectile
            {
                Kind = pd.Kind,
            };
            p.X = pd.X;
            p.Y = pd.Y;
            p.Room = pd.Room;
            p.Facing = pd.Facing;
            p.Phase = pd.Phase;
            p.Anim = pd.Anim;
            p.Step = pd.Step;
            p.Timer = pd.Timer;
            p.HitLocked = pd.HitLocked;

            Projectiles.Add(p);
        }

        // Coffins
        foreach (var cd in data.Coffins)
        {
            var c = Coffins.FirstOrDefault(x => x.Position.Room == cd.Room && x.Position.X == cd.X && x.Position.Y == cd.Y);
            if (c != null)
            {
                c.Cooldown = cd.Cooldown;
                c.Armed = cd.Armed;
                c.Open = cd.Open;
                c.Look = cd.Look;
            }
        }

        // Rotating doors
        foreach (var rd in data.RotatingDoors)
        {
            var m = _mechanismStates.Keys.FirstOrDefault(k => k.Position.Room == rd.Room && k.Position.X == rd.X && k.Position.Y == rd.Y);
            if (m != null && _mechanismStates.TryGetValue(m, out var st))
            {
                st.Phase = rd.Phase;
                st.Timer = rd.Timer;
                st.Pushing = rd.Pushing;
                st.Right = rd.Right;
                st.PushRight = rd.PushRight;
            }
        }

        // Traps
        foreach (var td in data.Traps)
        {
            var m = _traps.Keys.FirstOrDefault(k => k.Position.Room == td.Room && k.Position.X == td.X && k.Position.Y == td.Y);
            if (m != null && _traps.TryGetValue(m, out var st))
            {
                st.Armed = td.Armed;
                st.WasArmed = td.WasArmed;
                st.Count = td.Count;
                st.Spent = td.Spent;
            }
        }

        // Room maps
        foreach (var kvp in data.RoomCells)
        {
            if (_rooms.TryGetValue(kvp.Key, out var rm))
            {
                var cells = kvp.Value.Select(v => (TerrainKind)v).ToArray();
                rm.RestoreCells(cells);
            }
        }

        // Clear and re-mark push stones
        foreach (var rm in _rooms.Values)
            rm.ClearRock();

        foreach (var b in Enemies.Where(e => e.Kind == EnemyKind.MovableBlock))
            MarkBlock(b, true);
    }
}
