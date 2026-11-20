#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Engine.Navigation;

/// <summary>
/// Controls the player's automatic movement in response to clicks or taps (Point & Click / Tap-to-Move).
/// Converts the route computed by <see cref="RoomPathfinder"/> into <see cref="PlayerInput"/> commands on every logic tick.
/// </summary>
public sealed class PointAndClickNavigator
{
    private List<NavWaypoint>? _waypoints;
    private int _currentWaypointIndex;

    /// <summary>Current final destination in design pixels (or null if inactive).</summary>
    public Point? TargetPixel { get; private set; }

    /// <summary>Optional action to trigger on successfully reaching the final destination.</summary>
    public Action<StageSession>? OnArrival { get; set; }

    /// <summary>Indicates whether a route is currently being executed.</summary>
    public bool HasPath => _waypoints != null && _currentWaypointIndex < _waypoints.Count;

    /// <summary>Cancels any automatic navigation immediately.</summary>
    public void Cancel()
    {
        _waypoints = null;
        _currentWaypointIndex = 0;
        TargetPixel = null;
        OnArrival = null;
    }

    /// <summary>
    /// Sets a new destination clicked or tapped in the room.
    /// </summary>
    public bool SetTarget(StageSession session, int targetPixelX, int targetPixelY)
    {
        // If the click is outside the room's playable area, ignore it
        if (targetPixelX < 0 || targetPixelX >= GameConstants.PlayfieldWidth ||
            targetPixelY < 0 || targetPixelY >= GameConstants.PlayfieldHeight)
        {
            Cancel();
            return false;
        }

        var player = session.Player;
        bool canJump = player.Holding == ItemKind.None;

        var path = RoomPathfinder.FindPath(
            session.Map,
            player.X,
            player.Y,
            targetPixelX,
            targetPixelY,
            canJump: canJump,
            doors: session.GetRevolvingDoorPassages());

        if (path == null || path.Count == 0)
        {
            Cancel();
            return false;
        }

        _waypoints = path;
        _currentWaypointIndex = 0;
        TargetPixel = new Point(targetPixelX, targetPixelY);
        return true;
    }

    /// <summary>
    /// Generates the next PlayerInput to follow the route. Returns PlayerInput.None if inactive.
    /// </summary>
    public PlayerInput Update(StageSession session)
    {
        if (_waypoints == null || _currentWaypointIndex >= _waypoints.Count)
        {
            Cancel();
            return PlayerInput.None;
        }

        var player = session.Player;

        // If the player is falling, jumping, dying, leaving or digging, wait for the physics to resolve
        if (player.State is PlayerState.Falling or PlayerState.Dying or PlayerState.Exiting or PlayerState.Digging or PlayerState.Throwing or PlayerState.Jumping)
            return PlayerInput.None;

        var wp = _waypoints[_currentWaypointIndex];
        int targetX = wp.PixelX;
        int targetY = wp.PixelY;

        int dx = targetX - player.X;
        if (dx > GameConstants.PlayfieldWidth / 2)
            dx -= GameConstants.PlayfieldWidth;
        else if (dx < -GameConstants.PlayfieldWidth / 2)
            dx += GameConstants.PlayfieldWidth;

        int dy = targetY - player.Y;
        if (dy > GameConstants.PlayfieldHeight / 2)
            dy -= GameConstants.PlayfieldHeight;
        else if (dy < -GameConstants.PlayfieldHeight / 2)
            dy += GameConstants.PlayfieldHeight;

        bool wrapPendingX = (player.X > GameConstants.PlayfieldWidth - 32 && targetX < 32) ||
                            (player.X < 32 && targetX > GameConstants.PlayfieldWidth - 32);
        bool wrapPendingY = (player.Y > GameConstants.PlayfieldHeight - 32 && targetY < 32) ||
                            (player.Y < 32 && targetY > GameConstants.PlayfieldHeight - 32);

        bool reachedX;
        bool reachedY;

        if (player.State == PlayerState.Climbing)
        {
            if (wp.Action != NavAction.Climb)
            {
                // While on the ladder, waypoints that are not Climb (Walk, Fall, Jump)
                // have not been reached yet
                reachedX = false;
                reachedY = false;
            }
            else
            {
                // On the ladder the player's X is snapped to the ladder column
                reachedX = true;
                reachedY = !wrapPendingY && Math.Abs(dy) <= Physics.ClimbSpeed;
            }
        }
        else if (wp.Action is NavAction.Jump or NavAction.Fall)
        {
            // Landed from the jump or the fall: tolerance of up to 1 cell
            reachedX = !wrapPendingX && Math.Abs(dx) <= GameConstants.CellSize;
            reachedY = !wrapPendingY && Math.Abs(dy) <= GameConstants.CellSize;
        }
        else
        {
            reachedX = !wrapPendingX && Math.Abs(dx) <= Physics.WalkSpeed;
            reachedY = !wrapPendingY && Math.Abs(dy) <= Physics.ClimbSpeed;
        }

        if (reachedX && reachedY)
        {
            _currentWaypointIndex++;
            if (_currentWaypointIndex >= _waypoints.Count)
            {
                var arrival = OnArrival;
                Cancel();
                arrival?.Invoke(session);
                return PlayerInput.None;
            }
            wp = _waypoints[_currentWaypointIndex];
            targetX = wp.PixelX;
            targetY = wp.PixelY;
            dx = targetX - player.X;
            dy = targetY - player.Y;
        }

        bool left = false;
        bool right = false;
        bool up = false;
        bool down = false;
        bool action = false;

        if (player.State == PlayerState.Climbing)
        {
            if (wp.Action == NavAction.Fall)
            {
                // If it is a side fall (dx != 0), move to the side to leave the ladder and enter a free fall
                // If it is a fall through the bottom of the ladder (dx == 0), go down through the end of the ladder
                if (dx < 0) left = true;
                else if (dx > 0) right = true;
                else down = true;
            }
            // Vertical screen wrap detection (e.g.: floor ladder leaving through the ceiling)
            else if (player.Y > GameConstants.PlayfieldHeight - 32 && targetY < 32)
            {
                down = true;
            }
            else if (player.Y < 32 && targetY > GameConstants.PlayfieldHeight - 32)
            {
                up = true;
            }
            else if (dy < 0)
                up = true;
            else if (dy > 0)
                down = true;
            else
            {
                // Aligned at the exact cell height (dy == 0): move sideways if the next waypoint is sideways
                if (dx < 0) left = true;
                else if (dx > 0) right = true;
            }
        }
        else
        {
            // PlayerState.Standing
            if (wp.Action == NavAction.Climb)
            {
                // Aligns horizontally with the ladder
                if (Math.Abs(dx) > 1)
                {
                    if (dx < 0) left = true;
                    else if (dx > 0) right = true;
                }

                // When over or almost over the ladder (within 4 px),
                // also triggers the vertical command to enter it immediately without stuttering
                if (Math.Abs(dx) <= 4)
                {
                    if (dy < 0) up = true;
                    else down = true;
                }
            }
            else if (wp.Action == NavAction.Jump)
            {
                action = true;
                if (dx < 0) left = true;
                else if (dx > 0) right = true;
            }
            else
            {
                // Walk / Fall
                if (dx < 0)
                    left = true;
                else if (dx > 0)
                    right = true;
                else if (wp.Action == NavAction.Fall)
                {
                    // Aligned in the hole column but still on the ground:
                    // takes one more step to leave the support entirely and fall
                    if (player.Facing == Facing.Left) left = true;
                    else right = true;
                }
            }
        }

        return new PlayerInput(left, right, up, down, action);
    }
}
