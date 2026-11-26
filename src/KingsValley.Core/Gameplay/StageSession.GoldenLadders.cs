using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

public sealed partial class StageSession
{
    private readonly Dictionary<LadderDefinition,GoldenLadderState> _goldenLadders=new();
    private bool _goldenPulse;
    private sealed class GoldenLadderState
    {
        public int Remaining=2;
        public bool Armed;
    }
    /// <summary>Only unspent golden ladders participate in background assembly.</summary>
    public IReadOnlyList<LadderDefinition> ActiveGoldenLadders=>Definition.GoldenLadders.Where(l=>_goldenLadders[l].Remaining>0).ToArray();
    public int GoldenLadderUsesRemaining(LadderDefinition ladder)=>_goldenLadders[ladder].Remaining;

    // Native secret_hit: original BE15, verified Contest 8FD5. E7C3 bits 6..7
    // start at 2; bit 5 arms while climbing. Only a subsequent E2A7 strobe consumes it.
    private void UpdateGoldenLadders()
    {
        foreach(var ladder in Definition.GoldenLadders)
        {
            var state=_goldenLadders[ladder];
            if(state.Remaining==0 || ladder.Room!=CurrentRoom)continue;
            int x=ladder.Column*8,y=ladder.Row*8;
            if((byte)(Player.X-x)>=16 || (byte)(Player.Y-(y-20))>=ladder.Length*8+24)continue;
            if(!_goldenPulse)
            {
                if(Player.State==PlayerState.Climbing)state.Armed=true;
                continue;
            }
            _goldenPulse=false;
            if(!state.Armed)continue;
            state.Armed=false;
            if(--state.Remaining!=0)continue;
            // Native 9036: top row becomes soft terrain; the remaining two-cell-wide
            // ladder is erased. This is not merely hiding its artwork.
            var map=Room(ladder.Room);
            for(int row=0;row<ladder.Length;row++)for(int column=0;column<2;column++)
                map.StampTerrain(ladder.Column+column,ladder.Row+row,row==0?TerrainKind.Soft:TerrainKind.Empty);
            Player.Y=(Player.Y+3)&~7;
            PlaySound(OriginalSounds.GoldenLadder);
        }
        _goldenPulse=false;
    }
}
