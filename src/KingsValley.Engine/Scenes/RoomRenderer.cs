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
        MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);
        var s = _session();
        if (s != null)
            DrawRoom(batcher, s);

        // 72E1: on the stage-end screen the HUD disappears (the longplay confirms: the bottom strip goes all
        // black) and the text appears over the room already darkened by the curtain.
        bool clearScreen = s is { Exit: ExitPhase.ClearScreen };
        if (!clearScreen)
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

        // Terrain, ladders and mechanisms already come assembled in the texture (RoomAssembler, faithful to the ROM:
        // blocks 3/4 on the ladders, 1/2 on the golden ones, 5/0x5E on the terrain, the mechanism tile over
        // the mural). Before, the sprites were drawn on top and hid what had been assembled.
        DrawCrumble(batcher, s.Crumble, p);
        DrawDoor(batcher, s, p);

        foreach (var c in s.Coffins)
        {
            if (c.Position.Room == s.CurrentRoom && c.Kind is EnemyKind.Mummy or EnemyKind.GoldMummy)
            {
                if (RomContent.Flavor == ArtFlavor.Msx1 && RomContent.Msx1OpaqueBackdrop)
                    MsxScreen.Fill(batcher, new Rectangle(c.Position.X, c.Position.Y - CoffinRise, 16, 24), Color.Black);
                MsxScreen.Draw(batcher, CoffinSprite(c.Look, p), c.Position.X, c.Position.Y - CoffinRise);
            }
        }

        foreach (var item in s.Items)
        {
            if (item.Room == s.CurrentRoom)
            {
                if (RomContent.Flavor == ArtFlavor.Msx1 && RomContent.Msx1OpaqueBackdrop)
                    MsxScreen.Fill(batcher, new Rectangle(item.X, item.Y, 16, 16), Color.Black);
                MsxScreen.Draw(batcher, ItemSprite(item.Kind), item.X, item.Y);
            }
        }

        foreach (var stone in s.SoulStones)
        {
            if (stone.Room == s.CurrentRoom)
            {
                if (RomContent.Flavor == ArtFlavor.Msx1 && RomContent.Msx1OpaqueBackdrop)
                    MsxScreen.Fill(batcher, new Rectangle(stone.X, stone.Y, 16, 16), Color.Black);
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

    private static readonly Color[] TmsColors =
    {
        Color.Transparent,
        Color.Black,
        new Color(33, 200, 66),
        new Color(94, 220, 120),
        new Color(84, 85, 237),
        new Color(125, 118, 252),
        new Color(212, 82, 77),
        new Color(66, 235, 245),
        new Color(252, 85, 84),
        new Color(255, 121, 120),
        new Color(212, 193, 84),
        new Color(230, 206, 128),
        new Color(33, 176, 59),
        new Color(201, 91, 186),
        new Color(204, 204, 204),
        Color.White,
    };

    private static readonly Dictionary<int, Texture2D> Msx1CrumbleTextures = new();

    private static void DrawMsx1Crumble(Batcher batcher, int code, int x, int y, int pyramid)
    {
        if (!Msx1CrumbleTextures.TryGetValue(pyramid, out var tex) || tex.IsDisposed)
        {
            tex = BuildMsx1CrumbleTexture(pyramid);
            if (tex is not null)
                Msx1CrumbleTextures[pyramid] = tex;
        }

        if (tex is not null)
        {
            int blockIdx = Math.Clamp(code - 6, 0, 17);
            batcher.Draw(tex, new Vector2(x, y), new Rectangle(blockIdx * 8, 0, 8, 8), Color.White);
        }
    }

    private static Texture2D? BuildMsx1CrumbleTexture(int pyramid)
    {
        int pyr = Math.Clamp(pyramid, 1, 6);
        string path = System.IO.Path.Combine(RomContent.SharedRoot, "Rooms", $"blocks_p{pyr}.bin");
        if (!System.IO.File.Exists(path))
            return null;

        var raw = System.IO.File.ReadAllBytes(path);
        const int count = 18;
        var pixels = new Color[count * 8 * 8];

        for (int i = 0; i < count; i++)
        {
            int code = 6 + i;
            int byteOffset = code * 32;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    byte b = raw[byteOffset + r * 4 + c / 2];
                    int pal = (c % 2 == 0) ? (b >> 4) : (b & 0x0F);
                    Color color = (pal != 15 && pal < TmsColors.Length) ? TmsColors[pal] : Color.Transparent;
                    pixels[r * (count * 8) + i * 8 + c] = color;
                }
            }
        }

        var tex = new Texture2D(Nez.Core.GraphicsDevice, count * 8, 8);
        tex.SetData(pixels);
        return tex;
    }

    /// <summary>AAC9/ABA6: crumbling stages (blocks 6..11 on the floor; 12..17 / 18..23 on the wall).</summary>
    private static void DrawCrumble(Batcher batcher, DigCrumble? crumble, int pyramid)
    {
        if (crumble is not { Stage: >= 0 } c)
            return;

        int first = c.Floor ? 6 : c.FacingRight ? 0x12 : 0x0C;
        int code = first + c.Stage * 2;
        if (RomContent.Flavor == ArtFlavor.Msx1)
        {
            DrawMsx1Crumble(batcher, code, c.X, c.Y, pyramid);
            DrawMsx1Crumble(batcher, code + 1, c.Floor ? c.X + 8 : c.X, c.Floor ? c.Y : c.Y + 8, pyramid);
            return;
        }

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

        if (RomContent.Flavor == ArtFlavor.Msx1 && RomContent.Msx1OpaqueBackdrop)
            MsxScreen.Fill(batcher, new Rectangle(door.X, door.Y, 32, 32), Color.Black);

        MsxScreen.Draw(batcher, $"door_f{s.DoorFrame}_p{pyramid}", door.X, door.Y);
        var layout = DoorStudLayouts.For(s.SoulStoneTotal);
        for (int i = 0; i < s.DoorStuds && i < layout.Length; i++)
        {
            int b = layout[i];
            MsxScreen.Draw(batcher, $"door_stud_p{pyramid}", door.X + (b & 3) * 8, door.Y + (b >> 2 & 3) * 8);
        }
    }

    private static void DrawEnemy(Batcher batcher, Enemy e, int pyramid, int tick)
    {
        if (e.Kind == EnemyKind.MovableBlock)
        {
            for (int i = 0; i < 4; i++)
                MsxScreen.Draw(batcher, $"soft_p{pyramid}_{i}", e.X + (i % 2) * 8, e.Y + (i / 2) * 8);
            return;
        }

        // Exact MSX2 ROM sprites: frame from table 0xBEED, drawn at (x, y) as in the SAT (6664).
        int frame = EnemyFrames.For(e, tick);
        if (frame == EnemyFrames.Hidden)
            return;

        MsxScreen.Draw(batcher, $"enemy_t{EnemyFrames.ColorType(e)}_{frame:00}", e.X, e.Y);
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
                p.X, p.Y - PlayerSpriteRise);
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
        MsxScreen.Draw(batcher, "hud_strip", 0, y);
        int score = run.Score + (s?.Score ?? 0);
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
