using System.Collections.Generic;
using System.Linq;

namespace KingsValley.Engine.Extraction;

/// <summary>Runtime consumers shared by both recipes. Optional/unused animation slots are not required sprites.</summary>
public static class KingsValleyGraphicsProfiles
{
    public static readonly string[] Screens = {"title","title_en","start_select","pyramid","world_map","world_vick","intro_msg","intro_vick","konami","sound_select","sound_select_cursor","puzzle","puzzle_bg","puzzle_tiles"};
    // Verified MSX2 5F8D/55F0 always draw the Japanese title; no regional title branch exists.
    // Unsupported optional artwork is not an extraction failure or an alias to another cartridge.
    public static IEnumerable<string> RequiredScreens(string variant)=>variant=="msx2"?Screens.Where(n=>n!="title_en"):Screens;
    public static int Revision(string variant)=>variant=="msx2"?3:2;
    public static string MechanismPath(string variant,int pyramid,string file)=>variant=="msx2"?$"Mechanisms/p{pyramid}/{file}":file;
    public static IEnumerable<string> RequiredSprites(string variant = "msx2")
    {
        for (int set = 0; set < 7; set++)
            for (int pose = 0; pose < 14; pose++)
                if (set != 0 || pose < 10) yield return $"vick_s{set}_{pose:00}";
        for (int pose = 0; pose < 7; pose++) yield return $"vick_x_{pose:00}";
        for (int pose = 0; pose < 8; pose++) yield return $"vick_d_{pose:00}";
        for(int type=1;type<=(variant=="msx1"?4:6);type++) for(int frame=0;frame<26;frame++)
            if(variant=="msx1" || frame!=23) yield return $"enemy_t{type}_{frame:00}"; // Only MSX2 BEED slot 23 is empty; MSX1 BECF slot 23 is a real pattern.
        foreach (string item in new[] {"knife","boomerang","shovel","pickaxe","hammer","drill"}) yield return "item_" + item;
        foreach (int frame in new[] {4,6,8,10,12,14}) yield return $"proj_{frame:00}";
        for (int p = 1; p <= 6; p++)
        {
            if(variant=="msx1") yield return $"movable_block_p{p}";
            foreach (string name in new[] {"soulstone","coffin","coffin_ajar","coffin_open","door_f0","door_f1","door_f2","door_stud","ladder"}) yield return $"{name}_p{p}";
            for(int v=0;v<4;v++) {yield return $"wall_p{p}_{v}";yield return $"soft_p{p}_{v}";}
            if(variant=="msx2") for(int b=0;b<16;b++) yield return $"door_clean_{b}_p{p}";
            for (int code = 6; code < 24; code++) yield return $"crumble_{code}_p{p}";
        }
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") yield return "font_" + c;
        for (int n=0;n<10;n++) yield return "digit_"+n;
        if(variant=="msx2") yield return "hud_strip";
        foreach (string name in new[] {"font_excl","arrow","title_cursor"}) yield return name;
    }
}
