using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public class EnemyColorTests
{
    [Theory]
    [InlineData(EnemyKind.Guard)]
    [InlineData(EnemyKind.Rock)]
    [InlineData(EnemyKind.GoldMummy)]
    public void Msx1KeepsNativeKindColorDuringSpecialStates(EnemyKind kind)
    {
        var enemy=new Enemy { Kind=kind, Yellow=true, Vanishing=true };
        Assert.Equal((int)kind,EnemyFrames.ColorType(enemy,msx1:true));
        Assert.Equal(EnemyFrames.VanishColorType,EnemyFrames.ColorType(enemy));
        enemy.Vanishing=false;
        Assert.Equal((int)kind,EnemyFrames.ColorType(enemy,msx1:true));
        Assert.Equal(kind==EnemyKind.Rock?6:2,EnemyFrames.ColorType(enemy));
    }
}
