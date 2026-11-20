using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
var def = StageLoader.LoadFile(args[0]);
var s = new StageSession(def);
using var w = new StreamWriter(args[1]);
for (int t = 0; t < int.Parse(args[2]); t++)
{
    s.Tick(PlayerInput.None);
    var e = s.Enemies.FirstOrDefault(x => x.Kind != EnemyKind.MovableBlock);
    w.WriteLine(e == null ? $"{s.TickCount} -" : $"{s.TickCount} s{(int)e.State} y{e.Y:x2} x{e.X:x2} f{e.Frame} p{(int)s.Player.State} {s.Status}");
}
