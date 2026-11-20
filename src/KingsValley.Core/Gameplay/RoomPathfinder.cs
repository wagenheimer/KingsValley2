#nullable enable
using System;
using System.Collections.Generic;
using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Movement type to reach the next navigation node.
/// </summary>
public enum NavAction
{
    Walk,
    Climb,
    Fall,
    Jump
}

/// <summary>
/// Point on the room grid (8x8 cells) with the expected action.
/// </summary>
public readonly record struct NavWaypoint(int Col, int Row, NavAction Action)
{
    public int PixelX => Col * GameConstants.CellSize;
    public int PixelY => Row * GameConstants.CellSize;
}

/// <summary>
/// Swivel door crossing on the room grid, connecting the column on the left and the one on the right.
/// </summary>
public readonly record struct RevolvingDoorPassage(
    int LeftCol,
    int RightCol,
    int Row,
    bool CanPushRight,
    bool CanPushLeft);

/// <summary>
/// A* algorithm adapted to the physics and movement rules of King's Valley 2 (MSX2).
/// Navigates platforms, 16 px ladders, vertical falls and jumps over gaps.
/// </summary>
public static class RoomPathfinder
{
    private readonly record struct NodeKey(int Col, int Row, bool OnLadder);

    /// <summary>
    /// Finds the shortest path between the player's current position and the clicked target.
    /// Returns the ordered waypoint list or null if unreachable.
    /// </summary>
    public static List<NavWaypoint>? FindPath(
        RoomMap map,
        int startPixelX,
        int startPixelY,
        int targetPixelX,
        int targetPixelY,
        bool canJump = true,
        Func<int, int, bool>? isSolidExtra = null,
        IEnumerable<RevolvingDoorPassage>? doors = null)
    {
        int startCol = Math.Clamp(RoomMap.FloorDiv(startPixelX + 4), 0, GameConstants.RoomColumns - 2);
        int startRow = Math.Clamp(RoomMap.FloorDiv(startPixelY + 4), 0, GameConstants.RoomRows - 2);

        int targetCol = Math.Clamp(RoomMap.FloorDiv(targetPixelX), 0, GameConstants.RoomColumns - 2);
        int targetRow = Math.Clamp(RoomMap.FloorDiv(targetPixelY), 0, GameConstants.RoomRows - 2);

        // If a ladder was clicked (it has 2 columns), align to the left column where the 16 px actor fits
        if (map.Get(targetCol, targetRow) == TerrainKind.Ladder)
        {
            if (targetCol > 0 && map.Get(targetCol - 1, targetRow) == TerrainKind.Ladder)
                targetCol--;
        }
        else if (targetRow < GameConstants.RoomRows - 1 && map.Get(targetCol, targetRow + 1) == TerrainKind.Ladder)
        {
            // Clicked right at the opening above the ladder to go down: enters the ladder fully
            if (targetCol > 0 && map.Get(targetCol - 1, targetRow + 1) == TerrainKind.Ladder)
                targetCol--;
            targetRow++;
        }

        // If the 16 px actor (2 columns) hits the wall on the right, but fits in the column on the left, align
        if (targetCol > 0 && IsSolid(map, targetCol + 1, targetRow, isSolidExtra) &&
            !IsSolid(map, targetCol, targetRow, isSolidExtra) && !IsSolid(map, targetCol - 1, targetRow, isSolidExtra))
        {
            targetCol--;
        }

        bool startOnLadder = IsLadderAtBody(map, startCol, startRow);

        // If the clicked target is a solid wall, look for the nearest reachable cell
        if (IsSolid(map, targetCol, targetRow, isSolidExtra) || IsSolid(map, targetCol + 1, targetRow, isSolidExtra))
        {
            var adjusted = FindClosestWalkableTarget(map, targetCol, targetRow, isSolidExtra);
            if (adjusted.HasValue)
            {
                targetCol = adjusted.Value.Col;
                targetRow = adjusted.Value.Row;
            }
            else
            {
                // It may be a click on the wall next to an open vertical shaft/gap:
                // Probes adjacent columns to see if there is a drop to the floor
                int[] probeCols = { -1, -2, 1, 2 };
                foreach (int dc in probeCols)
                {
                    int c = targetCol + dc;
                    if (c >= 0 && c <= GameConstants.RoomColumns - 2 && !IsSolid(map, c, targetRow, isSolidExtra) && !IsSolid(map, c + 1, targetRow, isSolidExtra))
                    {
                        int landing = FindLandingRow(map, c, targetRow, isSolidExtra);
                        if (landing != -1)
                        {
                            targetCol = c;
                            targetRow = landing;
                            break;
                        }
                    }
                }
            }
        }
        else if (!IsLadderAtBody(map, targetCol, targetRow) && !IsGrounded(map, targetCol, targetRow, isSolidExtra))
        {
            // The click was in the air / empty space ("empty part"):
            // Projects down to the floor right below where the character will fall
            int landing = FindLandingRow(map, targetCol, targetRow, isSolidExtra);
            if (landing != -1)
            {
                targetRow = landing;
            }
            else
            {
                // Sonda colunas adjacentes no ar
                int[] probeCols = { -1, 1, -2, 2 };
                bool found = false;
                foreach (int dc in probeCols)
                {
                    int c = targetCol + dc;
                    if (c >= 0 && c <= GameConstants.RoomColumns - 2 && !IsSolid(map, c, targetRow, isSolidExtra) && !IsSolid(map, c + 1, targetRow, isSolidExtra))
                    {
                        int l = FindLandingRow(map, c, targetRow, isSolidExtra);
                        if (l != -1)
                        {
                            targetCol = c;
                            targetRow = l;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    var adjusted = FindClosestWalkableTarget(map, targetCol, targetRow, isSolidExtra);
                    if (adjusted.HasValue)
                    {
                        targetCol = adjusted.Value.Col;
                        targetRow = adjusted.Value.Row;
                    }
                }
            }
        }

        var startNode = new NodeKey(startCol, startRow, startOnLadder);
        var openSet = new PriorityQueue<NodeKey, int>();
        var gScore = new Dictionary<NodeKey, int>();
        var cameFrom = new Dictionary<NodeKey, (NodeKey Parent, NavAction Action)>();

        gScore[startNode] = 0;
        openSet.Enqueue(startNode, Heuristic(startCol, startRow, targetCol, targetRow));

        NodeKey? bestEndNode = null;
        int closestDistance = int.MaxValue;
        NodeKey closestNode = startNode;

        int iterations = 0;
        const int maxIterations = 800;

        while (openSet.Count > 0 && ++iterations < maxIterations)
        {
            var current = openSet.Dequeue();

            int dcDirect = Math.Abs(current.Col - targetCol);
            int dc = Math.Min(dcDirect, GameConstants.RoomColumns - dcDirect);
            int drDirect = Math.Abs(current.Row - targetRow);
            int dr = Math.Min(drDirect, GameConstants.RoomRows - drDirect);
            int dist = dc + dr;
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closestNode = current;
            }

            // Reached the target:
            // On a ladder, it requires reaching the clicked row so the character enters it fully.
            // On flat ground, accepts a 1-row tolerance if it is already in the same column.
            bool isLadderTarget = IsLadderAtBody(map, targetCol, targetRow);
            if (current.Col == targetCol && (current.Row == targetRow || (!isLadderTarget && Math.Abs(current.Row - targetRow) <= 1)))
            {
                bestEndNode = current;
                break;
            }

            foreach (var (neighbor, action, cost) in GetNeighbors(map, current, canJump, isSolidExtra, doors))
            {
                int tentativeG = gScore[current] + cost;
                if (!gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    gScore[neighbor] = tentativeG;
                    cameFrom[neighbor] = (current, action);
                    int priority = tentativeG + Heuristic(neighbor.Col, neighbor.Row, targetCol, targetRow);
                    openSet.Enqueue(neighbor, priority);
                }
            }
        }

        var end = bestEndNode ?? (closestDistance <= 2 ? closestNode : (NodeKey?)null);
        if (!end.HasValue || end.Value == startNode)
            return null;

        // Rebuilds the waypoint list
        var path = new List<NavWaypoint>();
        var curr = end.Value;
        while (cameFrom.TryGetValue(curr, out var info))
        {
            path.Add(new NavWaypoint(curr.Col, curr.Row, info.Action));
            curr = info.Parent;
        }

        path.Reverse();
        return path;
    }

    private static int Heuristic(int c1, int r1, int c2, int r2)
    {
        int dcDirect = Math.Abs(c1 - c2);
        int dcWrap = GameConstants.RoomColumns - dcDirect;
        int dc = Math.Min(dcDirect, dcWrap);

        int drDirect = Math.Abs(r1 - r2);
        int drWrap = GameConstants.RoomRows - drDirect;
        int dr = Math.Min(drDirect, drWrap);
        return dc * 4 + dr * 2;
    }

    private static IEnumerable<(NodeKey Neighbor, NavAction Action, int Cost)> GetNeighbors(
        RoomMap map,
        NodeKey node,
        bool canJump,
        Func<int, int, bool>? isSolidExtra,
        IEnumerable<RevolvingDoorPassage>? doors)
    {
        int c = node.Col;
        int r = node.Row;

        if (node.OnLadder)
        {
            // 1. Climb the ladder
            if (r > 0 && !IsHeadBlocked(map, c, r - 1, isSolidExtra))
            {
                if (IsLadderAtBody(map, c, r - 1))
                {
                    yield return (new NodeKey(c, r - 1, true), NavAction.Climb, 8);
                }
                else if (CanOccupy(map, c, r - 1, isSolidExtra))
                {
                    // Reached the top of the ladder: stands on the platform/ladder top
                    yield return (new NodeKey(c, r - 1, false), NavAction.Climb, 8);
                }
            }
            else if (r <= 1 && IsLadderAtBody(map, c, 0))
            {
                // Vertical wrap upwards: leaves through the top of the screen and enters at the bottom of the ladder
                int bottomRow = GameConstants.RoomRows - 2;
                if (IsLadderAtBody(map, c, bottomRow))
                {
                    yield return (new NodeKey(c, bottomRow, true), NavAction.Climb, 8);
                }
            }

            // 2. Go down the ladder
            if (r < GameConstants.RoomRows - 1)
            {
                if (IsLadderAtBody(map, c, r + 1))
                {
                    yield return (new NodeKey(c, r + 1, true), NavAction.Climb, 8);
                }
                else if (CanOccupy(map, c, r + 1, isSolidExtra) && IsGrounded(map, c, r + 1, isSolidExtra))
                {
                    // Touches the floor coming out of the ladder
                    yield return (new NodeKey(c, r + 1, false), NavAction.Climb, 8);
                }
                else
                {
                    // The ladder ends in the air: goes down through the bottom of the ladder and falls free!
                    int landing = FindLandingRow(map, c, r + 1, isSolidExtra);
                    if (landing != -1)
                        yield return (new NodeKey(c, landing, false), NavAction.Fall, 8 + FallDistance(r, landing) * 2);
                }
            }

            if (r >= GameConstants.RoomRows - 3)
            {
                // Vertical wrap downwards: leaves through the bottom of the screen and enters at the top of the ladder
                if (IsLadderAtBody(map, c, 0))
                {
                    yield return (new NodeKey(c, 0, true), NavAction.Climb, 8);
                }
            }

            // 3. Leave the ladder sideways: in the original King's Valley 2, it is only possible to leave sideways
            // if the feet are resting on floor/platform (Grounded). It is not possible to walk in the air leaving the ladder.
            if (IsGrounded(map, c, r, isSolidExtra))
            {
                if (c > 0 && CanOccupy(map, c - 1, r, isSolidExtra))
                    yield return (new NodeKey(c - 1, r, false), NavAction.Walk, 4);
                else if (c == 0 && CanOccupy(map, GameConstants.RoomColumns - 2, r, isSolidExtra))
                    yield return (new NodeKey(GameConstants.RoomColumns - 2, r, false), NavAction.Walk, 4);

                if (c < GameConstants.RoomColumns - 2 && CanOccupy(map, c + 1, r, isSolidExtra))
                    yield return (new NodeKey(c + 1, r, false), NavAction.Walk, 4);
                else if (c == GameConstants.RoomColumns - 2 && CanOccupy(map, 0, r, isSolidExtra))
                    yield return (new NodeKey(0, r, false), NavAction.Walk, 4);
            }
        }
        else
        {
            // Swivel doors in the room (crosses by pushing to the other side)
            if (doors != null)
            {
                foreach (var door in doors)
                {
                    if (Math.Abs(r - door.Row) <= 1)
                    {
                        if (door.CanPushRight && (c == door.LeftCol || c == door.LeftCol + 1))
                        {
                            if (CanOccupy(map, door.RightCol, door.Row, isSolidExtra))
                            {
                                if (IsGrounded(map, door.RightCol, door.Row, isSolidExtra))
                                    yield return (new NodeKey(door.RightCol, door.Row, false), NavAction.Walk, 20);
                                else
                                {
                                    int landing = FindLandingRow(map, door.RightCol, door.Row, isSolidExtra);
                                    if (landing != -1)
                                        yield return (new NodeKey(door.RightCol, landing, false), NavAction.Fall, 20 + FallDistance(door.Row, landing) * 2);
                                }
                            }
                        }

                        if (door.CanPushLeft && (c == door.RightCol || c == door.RightCol - 1))
                        {
                            if (CanOccupy(map, door.LeftCol, door.Row, isSolidExtra))
                            {
                                if (IsGrounded(map, door.LeftCol, door.Row, isSolidExtra))
                                    yield return (new NodeKey(door.LeftCol, door.Row, false), NavAction.Walk, 20);
                                else
                                {
                                    int landing = FindLandingRow(map, door.LeftCol, door.Row, isSolidExtra);
                                    if (landing != -1)
                                        yield return (new NodeKey(door.LeftCol, landing, false), NavAction.Fall, 20 + FallDistance(door.Row, landing) * 2);
                                }
                            }
                        }
                    }
                }
            }

            // Standing state (on the ground)

            // 1. Walk left
            if (c > 0 && !IsSideBlocked(map, c - 1, r, isSolidExtra))
            {
                if (IsGrounded(map, c - 1, r, isSolidExtra))
                {
                    yield return (new NodeKey(c - 1, r, false), NavAction.Walk, 4);
                }
                else
                {
                    // Platform drop
                    int landing = FindLandingRow(map, c - 1, r, isSolidExtra);
                    if (landing != -1)
                        yield return (new NodeKey(c - 1, landing, false), NavAction.Fall, 6 + FallDistance(r, landing) * 2);
                }
            }
            else if (c == 0 && !IsSideBlocked(map, GameConstants.RoomColumns - 2, r, isSolidExtra))
            {
                // Horizontal wrap to the left (leaves on the left side and enters on the right)
                int wrapCol = GameConstants.RoomColumns - 2;
                if (IsGrounded(map, wrapCol, r, isSolidExtra))
                {
                    yield return (new NodeKey(wrapCol, r, false), NavAction.Walk, 4);
                }
                else
                {
                    int landing = FindLandingRow(map, wrapCol, r, isSolidExtra);
                    if (landing != -1)
                        yield return (new NodeKey(wrapCol, landing, false), NavAction.Fall, 6 + FallDistance(r, landing) * 2);
                }
            }

            // 2. Walk right
            if (c < GameConstants.RoomColumns - 2 && !IsSideBlocked(map, c + 1, r, isSolidExtra))
            {
                if (IsGrounded(map, c + 1, r, isSolidExtra))
                {
                    yield return (new NodeKey(c + 1, r, false), NavAction.Walk, 4);
                }
                else
                {
                    // Platform drop
                    int landing = FindLandingRow(map, c + 1, r, isSolidExtra);
                    if (landing != -1)
                        yield return (new NodeKey(c + 1, landing, false), NavAction.Fall, 6 + FallDistance(r, landing) * 2);
                }
            }
            else if (c == GameConstants.RoomColumns - 2 && !IsSideBlocked(map, 0, r, isSolidExtra))
            {
                // Horizontal wrap to the right (leaves on the right side and enters on the left)
                if (IsGrounded(map, 0, r, isSolidExtra))
                {
                    yield return (new NodeKey(0, r, false), NavAction.Walk, 4);
                }
                else
                {
                    int landing = FindLandingRow(map, 0, r, isSolidExtra);
                    if (landing != -1)
                        yield return (new NodeKey(0, landing, false), NavAction.Fall, 6 + FallDistance(r, landing) * 2);
                }
            }

            // 3. Enter the ladder
            // Climb on an aligned ladder
            int ladderCol = FindLadderCol(map, c, r);
            if (ladderCol != -1)
            {
                if (IsLadderAtBody(map, ladderCol, r - 1) && !IsHeadBlocked(map, ladderCol, r - 1, isSolidExtra))
                    yield return (new NodeKey(ladderCol, r - 1, true), NavAction.Climb, 12);
            }

            // Climb down a ladder that starts below the feet
            int ladderBelowCol = FindLadderCol(map, c, r + 2);
            if (ladderBelowCol != -1)
            {
                yield return (new NodeKey(ladderBelowCol, r + 1, true), NavAction.Climb, 12);
            }

            // 4. Jump (if it can jump)
            if (canJump && r > 1 && !IsHeadBlocked(map, c, r - 1, isSolidExtra))
            {
                // a) Jump up steps (1 cell = 8 px or 2 cells = 16 px higher)
                foreach (int dy in new[] { -2, -1 })
                {
                    int targetRow = r + dy;
                    if (targetRow < 1)
                        continue;

                    // Jump up to the left
                    foreach (int dc in new[] { -1, -2, -3 })
                    {
                        int targetCol = c + dc;
                        if (targetCol < 0) targetCol += GameConstants.RoomColumns;
                        if (targetCol >= 0 && targetCol <= GameConstants.RoomColumns - 2 && CanOccupy(map, targetCol, targetRow, isSolidExtra) && IsGrounded(map, targetCol, targetRow, isSolidExtra))
                        {
                            yield return (new NodeKey(targetCol, targetRow, false), NavAction.Jump, 25);
                            break;
                        }
                    }

                    // Jump up to the right
                    foreach (int dc in new[] { 1, 2, 3 })
                    {
                        int targetCol = c + dc;
                        if (targetCol > GameConstants.RoomColumns - 2) targetCol -= GameConstants.RoomColumns;
                        if (targetCol >= 0 && targetCol <= GameConstants.RoomColumns - 2 && CanOccupy(map, targetCol, targetRow, isSolidExtra) && IsGrounded(map, targetCol, targetRow, isSolidExtra))
                        {
                            yield return (new NodeKey(targetCol, targetRow, false), NavAction.Jump, 25);
                            break;
                        }
                    }
                }

                // b) Straight jump over a hole/gap (same height r)
                int leftJumpCol = c - 2;
                if (leftJumpCol < 0) leftJumpCol += GameConstants.RoomColumns;
                if (leftJumpCol <= GameConstants.RoomColumns - 2 && CanOccupy(map, leftJumpCol, r, isSolidExtra) && IsGrounded(map, leftJumpCol, r, isSolidExtra))
                    yield return (new NodeKey(leftJumpCol, r, false), NavAction.Jump, 25);

                int rightJumpCol = c + 2;
                if (rightJumpCol > GameConstants.RoomColumns - 2) rightJumpCol -= GameConstants.RoomColumns;
                if (rightJumpCol >= 0 && CanOccupy(map, rightJumpCol, r, isSolidExtra) && IsGrounded(map, rightJumpCol, r, isSolidExtra))
                    yield return (new NodeKey(rightJumpCol, r, false), NavAction.Jump, 25);

                // c) Jump down to a lower platform over a gap (dy = +1 or +2)
                foreach (int dy in new[] { 1, 2 })
                {
                    int targetRow = r + dy;
                    if (targetRow >= GameConstants.RoomRows - 2)
                        continue;

                    foreach (int dc in new[] { -2, -3 })
                    {
                        int targetCol = c + dc;
                        if (targetCol >= 0 && CanOccupy(map, targetCol, targetRow, isSolidExtra) && IsGrounded(map, targetCol, targetRow, isSolidExtra))
                        {
                            yield return (new NodeKey(targetCol, targetRow, false), NavAction.Jump, 26);
                            break;
                        }
                    }

                    foreach (int dc in new[] { 2, 3 })
                    {
                        int targetCol = c + dc;
                        if (targetCol <= GameConstants.RoomColumns - 2 && CanOccupy(map, targetCol, targetRow, isSolidExtra) && IsGrounded(map, targetCol, targetRow, isSolidExtra))
                        {
                            yield return (new NodeKey(targetCol, targetRow, false), NavAction.Jump, 26);
                            break;
                        }
                    }
                }
            }
        }
    }

    private static bool CanOccupy(RoomMap map, int c, int r, Func<int, int, bool>? isSolidExtra) =>
        !IsSolid(map, c, r, isSolidExtra) &&
        !IsSolid(map, c + 1, r, isSolidExtra) &&
        !IsSolid(map, c, r + 1, isSolidExtra) &&
        !IsSolid(map, c + 1, r + 1, isSolidExtra);

    private static bool IsHeadBlocked(RoomMap map, int c, int r, Func<int, int, bool>? isSolidExtra) =>
        IsSolid(map, c, r, isSolidExtra) || IsSolid(map, c + 1, r, isSolidExtra);

    private static bool IsSideBlocked(RoomMap map, int c, int r, Func<int, int, bool>? isSolidExtra) =>
        IsSolid(map, c, r, isSolidExtra) ||
        IsSolid(map, c + 1, r, isSolidExtra) ||
        IsSolid(map, c, r + 1, isSolidExtra) ||
        IsSolid(map, c + 1, r + 1, isSolidExtra);

    public static bool IsGrounded(RoomMap map, int c, int r, Func<int, int, bool>? isSolidExtra)
    {
        int feetRow = r + 2;
        if (feetRow >= GameConstants.RoomRows)
            return false;

        bool solid = IsSolid(map, c, feetRow, isSolidExtra) || IsSolid(map, c + 1, feetRow, isSolidExtra);
        if (solid)
            return true;

        // The top of a ladder also counts as ground
        return map.IsLadderTop(c * GameConstants.CellSize, feetRow * GameConstants.CellSize) ||
               map.IsLadderTop((c + 1) * GameConstants.CellSize, feetRow * GameConstants.CellSize);
    }

    private static bool IsLadderAtBody(RoomMap map, int c, int r)
    {
        return map.Get(c, r) == TerrainKind.Ladder ||
               map.Get(c + 1, r) == TerrainKind.Ladder ||
               map.Get(c, r + 1) == TerrainKind.Ladder ||
               map.Get(c + 1, r + 1) == TerrainKind.Ladder;
    }

    private static int FindLadderCol(RoomMap map, int c, int r)
    {
        int[] probeCols = { c, c + 1, c - 1 };
        foreach (int col in probeCols)
        {
            if (col >= 0 && col < GameConstants.RoomColumns && map.Get(col, r) == TerrainKind.Ladder)
            {
                return map.Get(col - 1, r) == TerrainKind.Ladder ? col - 1 : col;
            }
        }
        return -1;
    }

    private static int FallDistance(int startR, int landingR) =>
        landingR > startR ? (landingR - startR) : (GameConstants.RoomRows - startR + landingR);

    private static int FindLandingRow(RoomMap map, int c, int startRow, Func<int, int, bool>? isSolidExtra)
    {
        for (int r = startRow + 1; r <= GameConstants.RoomRows - 2; r++)
        {
            if (IsSideBlocked(map, c, r, isSolidExtra))
                return -1; // hit an obstacle while falling
            if (IsGrounded(map, c, r, isSolidExtra))
                return r;
        }

        // Fall through the bottom of the screen (vertical wrap: leaves at the bottom and enters at the top of the room)
        if (IsHeadBlocked(map, c, GameConstants.RoomRows - 1, isSolidExtra))
            return -1;

        if (IsHeadBlocked(map, c, 0, isSolidExtra))
            return -1;

        for (int r = 0; r <= startRow; r++)
        {
            if (IsSideBlocked(map, c, r, isSolidExtra))
                return -1;
            if (IsGrounded(map, c, r, isSolidExtra))
                return r;
        }

        return -1;
    }

    private static bool IsSolid(RoomMap map, int c, int r, Func<int, int, bool>? isSolidExtra)
    {
        if (c < 0 || c >= GameConstants.RoomColumns || r < 0 || r >= GameConstants.RoomRows)
            return true;
        if (isSolidExtra != null && isSolidExtra(c, r))
            return true;
        return map.Get(c, r).IsSolid();
    }

    private static (int Col, int Row)? FindClosestWalkableTarget(
        RoomMap map,
        int targetCol,
        int targetRow,
        Func<int, int, bool>? isSolidExtra)
    {
        // Try adjacent: above, left, right and around doors/structures
        int[] dCols = { 0, -1, 1, -2, 2, -3, 3 };
        int[] dRows = { 0, -1, 1, -2, 2 };

        foreach (int dr in dRows)
        {
            foreach (int dc in dCols)
            {
                int c = targetCol + dc;
                int r = targetRow + dr;
                if (c >= 0 && c <= GameConstants.RoomColumns - 2 && r >= 0 && r <= GameConstants.RoomRows - 2)
                {
                    if (CanOccupy(map, c, r, isSolidExtra) && (IsGrounded(map, c, r, isSolidExtra) || IsLadderAtBody(map, c, r)))
                        return (c, r);
                }
            }
        }
        return null;
    }
}
