#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>MSX1 source-only atlas. Missing families stay missing; no geometric or embedded substitutes.</summary>
public static class RomSpriteAtlas
{
    public static void Build(byte[] rom, string extractedDirectory, Action<string>? log = null,
        bool force = false, int poseFrames = 120 * 60, CancellationToken token = default)
    {
        var identity = RomExtractionProfiles.FindForRom(rom);
        if (identity?.GameId != "kings-valley-2" || identity.Variant != "msx1")
            throw new InvalidDataException("The MSX1 atlas recipe requires the verified MSX1 cartridge.");
        token.ThrowIfCancellationRequested();
        var machine = RomScreens.BootToGameplay(rom, token);
        var sprites = new List<RomDirectExtractor.DirectSprite>();
        int Offset(int address) => 14 * 8192 + address - 0x8000;
        int Word(int address) { int offset = Offset(address); return rom[offset] | (rom[offset + 1] << 8); }
        byte[] Rgba(byte[] indices, bool alpha)
        {
            var result = new byte[indices.Length * 4];
            for (int i = 0; i < indices.Length; i++)
            {
                var c = Tms9918.Palette[indices[i]];
                result[i * 4] = c.R; result[i * 4 + 1] = c.G; result[i * 4 + 2] = c.B;
                result[i * 4 + 3] = alpha && indices[i] == 0 ? (byte)0 : (byte)255;
            }
            return result;
        }
        // 9CA3 constructs the four native SAT records: plane offsets and colors are not two
        // flat 16x16 halves. In particular the two black outline planes have distinct Y origins.
        machine.CallAddress(0x4192, 100_000); // verified MSX1 page_banks_123
        machine.CallAddress(0x9CA3, 100_000);
        byte[] normalAttributes = Enumerable.Range(0,16).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
        byte[] Pose(int pointer, byte[] attributes)
        {
            token.ThrowIfCancellationRequested();
            byte[] planes = KonamiRle.Decode(rom,Offset(pointer),128);
            byte[] vram = new byte[16384], registers = (byte[])machine.VdpRegisters.Clone();
            registers[1] = 2; registers[5] = 0x76; registers[6] = 3;
            planes.CopyTo(vram,0x1800); attributes.CopyTo(vram,0x3B00); vram[0x3B10]=208;
            byte[] frame = Tms9918.DecodeSprites(vram,registers,scanlineLimit:false);
            int x = attributes[1], y = machine.Memory(0xE282)-8;
            byte[] pixels = new byte[16*32];
            for(int row=0;row<32;row++) for(int col=0;col<16;col++)
                if(x+col>=0 && x+col<256 && y+row>=0 && y+row<192) pixels[row*16+col]=frame[(y+row)*256+x+col];
            return Rgba(pixels,true);
        }
        // MSX1 84D7 has ten unarmed source frames and twelve held-item source frames.
        // Native 5680..5686 subtracts two from logical use poses 10..13 before lookup.
        for(int set=0;set<7;set++)
        {
            int table=Word(0x84D7+set*2);
            for(int pose=0;pose<(set==0?10:14);pose++)
            {
                int source=pose<10?pose:pose-2;
                sprites.Add(new($"vick_s{set}_{pose:00}",16,32,Pose(Word(table+source*2),normalAttributes)));
            }
        }
        for(int pose=0;pose<7;pose++) sprites.Add(new($"vick_x_{pose:00}",16,32,Pose(Word(0x8589+pose*2),normalAttributes)));
        // 8597/9D07 belong to the game-over animation, not the level's exit sequence.
        // Native 531F loads B61D; 8CE4 selects its eight plane groups and SAT offsets.
        byte[] gameplayVram=(byte[])machine.Vram.Clone();
        byte originalX=machine.Memory(0xE284),originalY=machine.Memory(0xE282);
        machine.CallAddress(0x531F,500_000);
        machine.Write(0xE284,80);
        for(int pose=0;pose<8;pose++)
        {
            machine.Write(0xE282,80);machine.Write(0xE258,(byte)(pose/2));
            machine.Write(0xE259,(byte)(pose%2==1?0x12:2));
            machine.CallAddress(0x8CE4,100_000);
            byte[] attributes=Enumerable.Range(0,16).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
            byte[] pixels=Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,80,72,16,32);
            sprites.Add(new($"vick_d_{pose:00}",16,32,Rgba(pixels,true)));
        }
        Array.Copy(gameplayVram,machine.Vram,gameplayVram.Length);
        machine.Write(0xE284,originalX);machine.Write(0xE282,originalY);
        // Fixed screens/room capture supplies authentic pattern bytes; colors come from SCREEN 2 attributes.
        byte[] Tile(int code)
        {
            byte[] vram = (byte[])machine.Vram.Clone();
            int names = (machine.VdpRegisters[2] & 15) << 10;
            vram[names] = (byte)code;
            byte[] frame = Tms9918.DecodeScreen2(vram,machine.VdpRegisters);
            byte[] pixels = new byte[64];
            for (int y=0;y<8;y++) Array.Copy(frame,y*256,pixels,y*8,8);
            return pixels;
        }
        for (int i=0;i<26;i++) sprites.Add(new("font_"+(char)('A'+i),8,8,Rgba(Tile(0xE1+i),true)));
        for (int i=0;i<10;i++) sprites.Add(new("digit_"+i,8,8,Rgba(Tile(0xD0+i),true)));
        sprites.Add(new("font_excl",8,8,Rgba(Tile(0xDB),true)));
        // Native clean door pattern order, verified against the MSX1 stage-1 pattern/color tables.
        int[] doorCodes = Enumerable.Range(0,16).Select(i=>(int)rom[2*8192+0x9059-0x8000+i]).ToArray();
        byte[] door = new byte[32*32];
        for(int i=0;i<16;i++)
        {
            byte[] tile=Tile(doorCodes[i]);
            sprites.Add(new($"door_clean_msx1_{i}",8,8,Rgba(tile,false)));
            for(int y=0;y<8;y++) Array.Copy(tile,y*8,door,((i/4)*8+y)*32+(i%4)*8,8);
        }
        string rooms=Path.Combine(extractedDirectory,"Rooms"); Directory.CreateDirectory(rooms);
        File.WriteAllBytes(Path.Combine(rooms,"door_clean.bin"),ScreenAssembler.Encode(32,32,RomScreens.TmsPalette,door,paletteModel:ScreenPaletteModel.Tms9918));
        string native=Path.Combine(extractedDirectory,"Art","Native");
        if(Directory.Exists(native)) foreach(string path in Directory.EnumerateFiles(native,"*.bin").OrderBy(p=>p,StringComparer.Ordinal))
        {
            var image=ScreenAssembler.DecodeFile(path);
            string name=Path.GetFileNameWithoutExtension(path);
            sprites.Add(new(name,image.Width,image.Height,Rgba(image.Indices,image.HasAlpha)));
            if(name.StartsWith("item_",StringComparison.Ordinal)) sprites.Add(new(name[5..],image.Width,image.Height,Rgba(image.Indices,image.HasAlpha)));
        }
        string art=Path.Combine(extractedDirectory,"Art"); Directory.CreateDirectory(art);
        sprites.AddRange(Msx1EnemySprites.Extract(rom,machine,token));
        sprites.AddRange(Msx1ProjectileSprites.Extract(rom,machine,token));
        sprites.AddRange(Msx1ObjectSprites.Extract(rom,machine,token));
        RomDirectExtractor.PackSprites(sprites,art,"kv2",log);
        RomDirectExtractor.BuildIntroVickScreen(sprites,Path.Combine(extractedDirectory,"Screens"),RomScreens.TmsPalette,ScreenPaletteModel.Tms9918);
        Msx1IntroAnimation.Write(rom,extractedDirectory,log,token);
        log?.Invoke($"MSX1 atlas: {sprites.Count} source-derived regions. Remaining object families require native recipes; no substitutes were generated.");
    }
}
