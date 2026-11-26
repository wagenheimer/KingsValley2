#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Draws the current room like the MSX2: background mural, terrain (8x8, 4 variants per pyramid),
/// ladders, door, sarcophagi, items, soul stones, enemies, projectiles, the explorer and the HUD
/// (HISCORE / SCORE / STAGE / REST) in the bottom strip — all in the 256x212 design space.
/// </summary>
public sealed class RoomRenderer : RenderableComponent
{
    private Texture2D? _contestClear;
    public override void OnRemovedFromEntity(){_contestClear?.Dispose();base.OnRemovedFromEntity();}
    private readonly Func<StageSession?> _session;
    private readonly Func<GameRun> _run;
    private readonly Func<StageSession?, Texture2D?>? _assembled;
    private readonly Func<string?> _banner;
    private readonly Func<Point?>? _targetMarker;
    private readonly Func<int>? _doorFlash;

    /// <summary>Creates the renderer using the room assembled from the ROM at runtime.</summary>
    public RoomRenderer(Func<StageSession?> session, Func<GameRun> run, Func<StageSession?, Texture2D?>? assembled, Func<string?> banner, Func<Point?>? targetMarker = null, Func<int>? doorFlash = null)
    {
        _session = session;
        _run = run;
        _assembled = assembled;
        _banner = banner;
        _targetMarker = targetMarker;
        _doorFlash = doorFlash;
    }

    public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

    public override void OnAddedToEntity()
    {
        base.OnAddedToEntity();
        RenderLayer = RenderLayers.Tile;
    }

    public override void Render(Batcher batcher, Camera camera)
    {
        var s = _session();
        if(RomContent.IsContest && s is {Exit:ExitPhase.ClearScreen})
        {
            // The native completion is a closed playfield plus live HUD; room actors and
            // curtain quads no longer participate in this frame.
            DrawClearScreen(batcher,s);DrawHud(batcher,_run(),s);return;
        }
        MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);
        if (s != null)
            DrawRoom(batcher, s);

        // 72E1: on the stage-end screen the HUD disappears (the longplay confirms: the bottom strip goes all
        // black) and the text appears over the room already darkened by the curtain.
        bool clearScreen = s is { Exit: ExitPhase.ClearScreen };
        if (!clearScreen || RomContent.IsContest)
            DrawHud(batcher, _run(), s);

        // 7296: the curtain closes the room before the stage change — the HUD (outside the 24 rows) stays.
        if (s is { CurtainBands: > 0, Exit: ExitPhase.Curtain or ExitPhase.ClearScreen })
            DrawCurtain(batcher, s.CurtainBands);

        if (clearScreen)
            DrawClearScreen(batcher, s!);

        var banner = _banner();
        if (!string.IsNullOrEmpty(banner))
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 88, GameConstants.DesignWidth, 16), Color.Black);
            MsxScreen.Text(batcher, banner, (GameConstants.DesignWidth - banner.Length * 8) / 2f, 92);
        }

        // Screen flash effect when all soulstones are collected (door unlocks)
        int flash = _doorFlash?.Invoke() ?? 0;
        if (flash > 0)
        {
            float alpha = MathF.Min(0.85f, flash / 5f);
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.White * alpha);
        }
    }

    private void DrawRoom(Batcher batcher, StageSession s)
    {
        var def = s.Definition;
        int p = def.Pyramid;
        var room = def.Room(s.CurrentRoom);

        Texture2D? bg = _assembled?.Invoke(s);
        if (bg != null)
            batcher.Draw(bg, Vector2.Zero, Color.White);

        // SCREEN 2 room dumps are immutable native backgrounds. Newly stamped falling-wall
        // cells use the extracted native soft tile, rather than procedural colored stripes.
        if(RomContent.Flavor==ArtFlavor.Msx1)
            for(int row=0;row<GameConstants.RoomRows;row++)for(int col=0;col<GameConstants.RoomColumns;col++)
            {
                int x=col*8,y=row*8;var terrain=s.Map.GetTerrain(col,row);
                bool newSoft=room.Cells[row*GameConstants.RoomColumns+col]!=TerrainKind.Soft && terrain==TerrainKind.Soft;
                var exit=s.Definition.ExitDoor.Position;
                bool underObject=!RomContent.Msx1OpaqueBackdrop &&
                    ((exit.Room==s.CurrentRoom && x>=exit.X && x<exit.X+32 && y>=exit.Y && y<exit.Y+32)
                    || s.Coffins.Any(c=>c.Position.Room==s.CurrentRoom && c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy &&
                        x>=c.Position.X && x<c.Position.X+16 && y>=c.Position.Y-8 && y<c.Position.Y+16));
                if(!newSoft && !underObject)continue;
                if(terrain is TerrainKind.Soft or TerrainKind.Wall)
                    MsxScreen.Draw(batcher,$"{(terrain==TerrainKind.Soft?"soft":"wall")}_p{p}_0",x,y);
            }

        // Terrain, ladders and mechanisms already come assembled in the texture (RoomAssembler, faithful to the ROM:
        // blocks 3/4 on the ladders, 1/2 on the golden ones, 5/0x5E on the terrain, the mechanism tile over
        // the mural). Before, the sprites were drawn on top and hid what had been assembled.
        DrawCrumble(batcher, s.Crumble, p);
        DrawDoor(batcher, s, p);

        foreach (var c in s.Coffins)
        {
            if (c.Position.Room == s.CurrentRoom && c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy)
            {
                // In MSX1 SCREEN 2 room dumps, the closed coffin is already in the background.
                // We only need to draw dynamic coffin overlays when opening or open (or in MSX2 mode).
                if ((int)c.Look > 0 || !RomContent.Msx1OpaqueBackdrop)
                {
                    MsxScreen.Draw(batcher, CoffinSprite(c.Look, p), c.Position.X, c.Position.Y - CoffinRise);
                }
                else if (RomContent.Flavor != ArtFlavor.Msx1)
                {
                    MsxScreen.Draw(batcher, CoffinSprite(c.Look, p), c.Position.X, c.Position.Y - CoffinRise);
                }
            }
        }

        foreach (var item in s.Items)
        {
            if (item.Room == s.CurrentRoom)
            {
                // Both native pipelines provide clean backgrounds; collectibles are dynamic.
                MsxScreen.Draw(batcher, ItemSprite(item.Kind), item.X, item.Y);
            }
        }

        foreach (var stone in s.SoulStones)
        {
            if (stone.Room == s.CurrentRoom)
            {
                MsxScreen.Draw(batcher, $"soulstone_p{p}", stone.X, stone.Y);
            }
        }

        foreach (var e in s.Enemies)
        {
            if (e.Room == s.CurrentRoom)
                DrawEnemy(batcher, e, p, s.TickCount);
        }

        foreach (var pr in s.Projectiles)
        {
            // A6FF/tool_sat: the tool only appears in the room it is in (SAT per screen).
            if (pr.Room != s.CurrentRoom)
                continue;

            // A87F/A99D: the knife spins 0..2 (proj_04/06/08) and the boomerang 3..5 (proj_10/12/14) —
            // one step per port tick, at the value the tool machine itself keeps.
            int first = pr.Kind == ItemKind.Boomerang ? 10 : 4;
            MsxScreen.Draw(batcher, $"proj_{first + pr.Anim % 3 * 2:00}", pr.X, pr.Y);
        }

        DrawPlayer(batcher, s);

        if (_targetMarker?.Invoke() is Point target)
            DrawTargetMarker(batcher, target, s.TickCount);
    }

    private static void DrawTargetMarker(Batcher batcher, Point target, int tick)
    {
        int tx = (target.X / GameConstants.CellSize) * GameConstants.CellSize;
        int ty = (target.Y / GameConstants.CellSize) * GameConstants.CellSize;
        float alpha = 0.5f + 0.35f * MathF.Sin(tick * 0.25f);
        Color color = Color.Gold * alpha;
        MsxScreen.Fill(batcher, new Rectangle(tx, ty, GameConstants.CellSize, 1), color);
        MsxScreen.Fill(batcher, new Rectangle(tx, ty + GameConstants.CellSize - 1, GameConstants.CellSize, 1), color);
        MsxScreen.Fill(batcher, new Rectangle(tx, ty, 1, GameConstants.CellSize), color);
        MsxScreen.Fill(batcher, new Rectangle(tx + GameConstants.CellSize - 1, ty, 1, GameConstants.CellSize), color);
    }

    /// <summary>AAC9/ABA6: crumbling stages (blocks 6..11 on the floor; 12..17 / 18..23 on the wall).</summary>
    private static void DrawCrumble(Batcher batcher, DigCrumble? crumble, int pyramid)
    {
        if (crumble is not { Stage: >= 0 } c)
            return;

        int first = c.Floor ? 6 : c.FacingRight ? 0x12 : 0x0C;
        int code = first + c.Stage * 2;

        MsxScreen.Draw(batcher, $"crumble_{code}_p{pyramid}", c.X, c.Y);
        MsxScreen.Draw(batcher, $"crumble_{code + 1}_p{pyramid}", c.Floor ? c.X + 8 : c.X, c.Floor ? c.Y : c.Y + 8);
    }

    /// <summary>
    /// 9010: door frame (4x4 blocks, <c>E2F6</c>) and, on top, one stud per soul stone still
    /// missing, at the original table positions (0xB9AD, indexed by the total of stones).
    /// </summary>
    private static void DrawDoor(Batcher batcher, StageSession s, int pyramid)
    {
        var door = s.Definition.ExitDoor.Position;
        if (door.Room != s.CurrentRoom)
            return;

        var layout = DoorStudLayouts.For(s.SoulStoneTotal);

        if (RomContent.Flavor == ArtFlavor.Msx1)
        {
            // In MSX1 SCREEN 2 room dumps, the closed door (frame 0) with all its initial studs is already
            // baked into the background bitmap. RoomBackgroundBuilder cleanly erases collected studs using
            // authentic bilateral clean door tiles with the room's exact VDP palette.
            // We only need to draw dynamic door overlays when the door opens (frame > 0).
            if (s.DoorFrame > 0 || !RomContent.Msx1OpaqueBackdrop)
            {
                MsxScreen.Draw(batcher, $"door_f{s.DoorFrame}_p{pyramid}", door.X, door.Y);
            }
            if(!RomContent.Msx1OpaqueBackdrop)
                for(int i=0;i<s.DoorStuds && i<layout.Length;i++)
                {
                    int b=layout[i];
                    MsxScreen.Draw(batcher,$"door_stud_p{pyramid}",door.X+(b&3)*8,door.Y+(b>>2&3)*8);
                }
        }
        else
        {
            // MSX2: Dynamic 4bpp door and dynamic 4bpp golden ruby studs
            MsxScreen.Draw(batcher, $"door_f{s.DoorFrame}_p{pyramid}", door.X, door.Y);
            for (int i = 0; i < s.DoorStuds && i < layout.Length; i++)
            {
                int b = layout[i];
                MsxScreen.Draw(batcher, $"door_stud_p{pyramid}", door.X + (b & 3) * 8, door.Y + (b >> 2 & 3) * 8);
            }
        }
    }

    private static void DrawEnemy(Batcher batcher, Enemy e, int pyramid, int tick)
    {
        if (e.Kind == EnemyKind.MovableBlock)
        {
            if(RomContent.Flavor==ArtFlavor.Msx1)
            {
                MsxScreen.Draw(batcher,$"movable_block_p{pyramid}",e.X,e.Y);
                return;
            }
            for (int i = 0; i < 4; i++)
                MsxScreen.Draw(batcher, $"soft_p{pyramid}_{i}", e.X + (i % 2) * 8, e.Y + (i / 2) * 8);
            return;
        }

        // Exact MSX2 ROM sprites: frame from table 0xBEED, drawn at (x, y) as in the SAT (6664).
        int frame = EnemyFrames.For(e, tick);
        if (frame == EnemyFrames.Hidden)
            return;

        MsxScreen.Draw(batcher, $"enemy_t{EnemyFrames.ColorType(e, msx1: RomContent.Flavor == ArtFlavor.Msx1)}_{frame:00}", e.X, e.Y);
    }

    /// <summary>
    /// 9881: 2x2 sprites from (x, y - 8); facing right (outside the ladder) the original
    /// offsets 2 px. The pose (<c>E285</c>) already brings the item in hand. On the door <b>opening</b> Vick
    /// keeps standing in the normal pose (<c>7204</c>); on the <b>entry</b> he swaps the pattern page
    /// for the 8 door poses (<c>5687</c> = <c>copy_abb9</c>, <c>rle_abb9</c> → 0xF800) and
    /// <b>never</b> vanishes — what shrinks is the art, at each step of the sinking.
    /// </summary>
    private static void DrawPlayer(Batcher batcher, StageSession s)
    {
        var p = s.Player;
        if (s.Exit == ExitPhase.DoorOpening)
        {
            // 7204: while the door opens Vick stays STANDING, in the normal pose - the SAT is only cleared
            // when he starts to enter (7288). Before, the port hid him here, so he "vanished"
            // the moment the door opened.
            MsxScreen.Draw(batcher, $"vick_s{(int)p.Holding}_{PlayerPoses.Stand(p.Facing):00}",
                p.X+(RomContent.Flavor==ArtFlavor.Msx1 && p.Facing==Facing.Right?PlayerFacingShift:0), p.Y - PlayerSpriteRise);
            return;
        }

        int set = (int)p.PoseSet;
        string name = p.PoseSet switch
        {
            PlayerPoseSet.Death => $"vick_x_{p.Pose:00}",
            PlayerPoseSet.Door => $"vick_d_{p.Pose:00}",
            _ => $"vick_s{set}_{p.Pose:00}",
        };
        int dx = p.Facing == Facing.Right && p.State != PlayerState.Climbing ? PlayerFacingShift : 0;
        if(RomContent.Flavor==ArtFlavor.Msx1 && p.PoseSet==PlayerPoseSet.Door)dx=0; // Native 8D29 uses E284 directly.
        MsxScreen.Draw(batcher, name, p.X + dx, p.Y - PlayerSpriteRise);
    }

    private static string CoffinSprite(CoffinLook look, int pyramid) => look switch
    {
        CoffinLook.Ajar => $"coffin_ajar_p{pyramid}",
        CoffinLook.Open => $"coffin_open_p{pyramid}",
        _ => $"coffin_p{pyramid}",
    };

    /// <summary>The sarcophagus (16x24) stands on the floor: it starts 8 px above the ROM record's y.</summary>
    private const int CoffinRise = 8;

    /// <summary>The player SAT starts 8 rows above <c>E282</c> (Y-9, +1 from the VDP).</summary>
    private const int PlayerSpriteRise = 8;

    /// <summary>Offset when facing right (989D).</summary>
    private const int PlayerFacingShift = 2;

    private static string ItemSprite(ItemKind kind) => kind switch
    {
        ItemKind.Knife => "knife",
        ItemKind.Boomerang => "boomerang",
        ItemKind.Shovel => "shovel",
        ItemKind.Pickaxe => "pickaxe",
        ItemKind.Hammer => "hammer",
        ItemKind.Drill => "drill",
        _ => string.Empty,
    };

    private static void DrawHud(Batcher batcher, GameRun run, StageSession? s)
    {
        const int y = GameConstants.PlayfieldHeight;
        var hudSprite = MsxScreen.Get("hud_strip");
        if (hudSprite != null)
        {
            MsxScreen.Draw(batcher, "hud_strip", 0, y);
        }
        else
        {
            MsxScreen.Fill(batcher, new Rectangle(0, y, GameConstants.DesignWidth, 20), new Color(16, 16, 40));
            MsxScreen.Text(batcher, "HI-SCORE", 16, y + 1);
            MsxScreen.Text(batcher, "SCORE", 88, y + 1);
            MsxScreen.Text(batcher, "STAGE", 168, y + 1);
            MsxScreen.Text(batcher, "REST", 224, y + 1);
        }

        int score = run.Score + (s?.Score ?? 0);
        if(RomContent.IsContest)
        {
            // Contest's native 5C1A script prints both labels at Y=198 on one line.
            void Number(int value,int x)
            {
                string text=Math.Max(0,value).ToString().PadLeft(6,'0');text=text[^6..];
                for(int i=0;i<6;i++)MsxScreen.Draw(batcher,"hud_digit_"+text[i],x+i*8,y+6);
            }
            Number(Math.Max(run.HiScore,score),80);Number(score,192);
            return;
        }
        MsxScreen.Number(batcher, Math.Max(run.HiScore, score), 6, 24, y + 10);
        MsxScreen.Number(batcher, score, 6, 88, y + 10);
        MsxScreen.Number(batcher, run.Stage, 2, 176, y + 10);
        MsxScreen.Number(batcher, Math.Max(0, run.Rest), 2, 224, y + 10);
    }

    /// <summary>
    /// 7296: the stage-change curtain — <paramref name="bands"/> 8 px bands already closed at the top
    /// and the bottom. The original draws 32 blocks of 8x8 per band (the room's 256 columns) at y = 0, 8,
    /// ... and the bottom band mirrors (<c>(0x17 - step) * 8</c>): from 0/184 up to 88/96, when the 24
    /// rows (192 px) are covered. The HUD stays outside the curtain.
    /// </summary>
    private static void DrawCurtain(Batcher batcher, int bands)
    {
        int h = bands * GameConstants.CellSize;
        MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.PlayfieldWidth, h), Color.Black);
        MsxScreen.Fill(batcher, new Rectangle(0, GameConstants.PlayfieldHeight - h, GameConstants.PlayfieldWidth, h), Color.Black);
    }

    /// <summary>
    /// The stage-end screen (steps 4..8 of <c>71E6</c>), over the room already darkened by the curtain:
    /// the text comes from the <c>B256</c> script (bank 12), the "REST" label from the <c>5F0E</c> script and the
    /// password from <c>697C</c> (which generates the 8 characters in <c>EDD9</c> and draws them). The positions are the
    /// <c>(D, E)</c> pair that the text engine of <c>51D0</c>/<c>51DA</c> receives — x and y in pixels.
    /// </summary>
    private void DrawClearScreen(Batcher batcher, StageSession s)
    {
        if(RomContent.IsContest)
        {
            _contestClear??=ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice,RomContent.ScreenPath("contest_clear"));
            if(_contestClear==null)throw new InvalidOperationException("Missing native Contest completion screen. Re-extract the Contest cartridge.");
            // Keep the live score HUD; the native capture's bottom rows are evidence only.
            batcher.Draw(_contestClear,Vector2.Zero,new Rectangle(0,0,256,192),Color.White);
            return;
        }
        // 72E1: INC (0xE242) — the REST shown on screen is already the incremented one (00 -> 01 on the longplay) and the
        // password is the next stage's (7331 -> 697C generates the password after the increment).
        var next = _run().StageCleared();

        MsxScreen.Text(batcher, "NICE !", 104, 32);            // B256: (0x68, 0x20)
        MsxScreen.Text(batcher, "LETS GO NEXT STAGE", 56, 48); // B256: (0x38, 0x30)
        MsxScreen.Text(batcher, "SPECIAL BONUS", 72, 80);      // B256: (0x48, 0x50)
        MsxScreen.Text(batcher, "REST", 96, 104);              // 5F0E em (0x60, 0x68)
        MsxScreen.Number(batcher, next.Rest, 2, 136, 104);     // 7303: 4D1F em (0x88, 0x68)

        if (s.ClearStep != ExitClearStep.Texts)
        {
            MsxScreen.Text(batcher, "PASS WORD", 88, 144);     // 697C: script 69FF em (0x58, 0x90)
            MsxScreen.Text(batcher, next.ClearScreenPassword, 92, 160); // 697C: 51DA em (0x5c, 0xa0)
        }
    }
}
