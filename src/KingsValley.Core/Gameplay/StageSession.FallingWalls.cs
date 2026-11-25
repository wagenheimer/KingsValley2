using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

public sealed partial class StageSession
{
    private readonly Dictionary<MechanismDefinition,FallingWallData> _fallingWalls=new();

    private void UpdateFallingWalls()
    {
        // Native MSX1 BA3B/BA5D and MSX2 BCC0/BCE2: existing activations advance
        // in every room, followed by the single-trigger post-pass B9EF/BC74.
        foreach(var mechanism in Definition.Mechanisms.Where(m=>m.Kind==MechanismKind.FallingWall))
        {
            if(!_fallingWalls.TryGetValue(mechanism,out var state))
                _fallingWalls[mechanism]=state=new FallingWallData {Room=mechanism.Position.Room,
                    X=mechanism.Position.X,Y=mechanism.Position.Y};
            if(!state.Triggered || state.Finished)continue;
            if(--state.Countdown!=0)continue;
            state.Countdown=30;
            if(state.Rows>=mechanism.HeightCells){state.Finished=true;continue;}
            Room(state.Room).StampTerrain(state.X/8,state.Y/8+state.Rows,TerrainKind.Soft);
            state.Rows++;
            PlaySound(OriginalSounds.FallingWall,state.Room);
        }
        foreach(var pair in _fallingWalls)
        {
            var m=pair.Key;var state=pair.Value;
            if(state.Triggered || state.Room!=CurrentRoom)continue;
            // Native byte subtraction/carry tests: center X lies in the eight-pixel column,
            // and the player's Y lies within its encoded height. No authored proximity radius.
            if(((Player.X+8-state.X)&255)>=8 || ((Player.Y-state.Y)&255)>=m.HeightCells*8)continue;
            state.Triggered=true;state.Countdown=30;break;
        }
    }
}
