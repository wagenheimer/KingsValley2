namespace KingsValley.Engine.Graphics;

public static class Msx1ObjectPresentation
{
    public static bool HasBlackBackdrop(string name)=>name.StartsWith("item_",StringComparison.Ordinal)
        || name is "knife" or "boomerang" or "shovel" or "pickaxe" or "hammer" or "drill"
        || name.StartsWith("soulstone_",StringComparison.Ordinal) || name.StartsWith("coffin_",StringComparison.Ordinal)
        || name.StartsWith("door_f",StringComparison.Ordinal) || name.StartsWith("door_stud_",StringComparison.Ordinal);
}
