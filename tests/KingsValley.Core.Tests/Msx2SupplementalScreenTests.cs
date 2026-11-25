using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;

namespace KingsValley.Core.Tests;

public class Msx2SupplementalScreenTests
{
    [LocalRomFact("msx2")]
    public void NativeMechanismInventoryHasAllThirteenStatesInEveryPyramid()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx2_scc.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-msx2-mechanisms-"+Guid.NewGuid().ToString("N"));
        try
        {
            var result=GraphicsExtractionCoordinator.Extract(GraphicsExtractionContext.Create(rom,"msx2",root,60));
            var checks=result.Manifest.Checks.Where(c=>c.Component.StartsWith("mechanism-",StringComparison.Ordinal)).ToArray();
            Assert.Equal(78,checks.Length);Assert.All(checks,c=>Assert.Equal("valid",c.State));
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                byte[] blocks=File.ReadAllBytes(Path.Combine(result.Directory,"Rooms",$"blocks_p{pyramid}.bin"));
                foreach(int type in new[]{1,2,4})for(int direction=0;direction<(type==2?2:1);direction++)for(int phase=0;phase<(type==4?1:4);phase++)
                {
                    string name=type==4?"mechanism_t4.bin":type==1?$"mechanism_t1_p{phase}.bin":$"mechanism_t2_d{direction}_p{phase}.bin";
                    byte[] frame=File.ReadAllBytes(Path.Combine(result.Directory,"Rooms",$"Mechanisms/p{pyramid}",name));
                    int columns=type==4?4:2,rows=type==4?1:3;
                    for(int tileY=0;tileY<rows;tileY++)for(int tileX=0;tileX<columns;tileX++)
                    {
                        int table=type==1?0x9374:0x937C+direction*24+tileY*8;
                        byte code=type==4?(byte)0xAD:rom[2*8192+table-0x8000+phase*2+tileX];
                        for(int row=0;row<8;row++)for(int pair=0;pair<4;pair++)
                            Assert.Equal(blocks[code*32+row*4+pair],frame[(tileY*8+row)*columns*4+tileX*4+pair]);
                    }
                }
            }
            Assert.True(result.Manifest.Ready);Assert.True(result.Manifest.Verify(result.Directory,"msx2"));
            result.Manifest.Artifacts.RemoveAll(a=>a.Path=="Rooms/Mechanisms/p6/mechanism_t4.bin");
            Assert.False(result.Manifest.Verify(result.Directory,"msx2"));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [LocalRomFact("msx2")]
    public void NativeScreensIncludePyramidSoundPuzzleAndTransparentPointers()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx2_scc.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-msx2-screens-"+Guid.NewGuid().ToString("N"));
        try
        {
            Msx2Screens.WriteSupported(rom,root);
            // Exact native 5F8D calls 55F0 unconditionally; the MSX1 regional branch is absent.
            Assert.Equal(new byte[]{0xCD,0xF0,0x55},rom.AsSpan(0x1F99,3).ToArray());
            Assert.False(File.Exists(Path.Combine(root,"Screens","title_en.bin")));
            Assert.Equal(new[]{"title_en"},KingsValleyGraphicsProfiles.RequiredScreens("msx1").Except(KingsValleyGraphicsProfiles.RequiredScreens("msx2")));
            foreach(string name in new[]{"konami","title","start_select","world_map","pyramid","intro_msg","sound_select","puzzle","puzzle_bg"})
            {
                var screen=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens",name+".bin"));
                Assert.Equal(256,screen.Width);Assert.Equal(212,screen.Height);
                Assert.True(screen.Indices.Distinct().Count()>1,name);
                Assert.True(screen.Indices.Select(i=>screen.Palette[i]).Distinct().Count()>1,name+" has invisible palette entries");
            }
            CheckText("sound_select","SOUND SELECT",80,24);
            CheckText("puzzle","PUZZLE  GAME",80,24);
            void CheckText(string name,string text,int x,int y)
            {
                var screen=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens",name+".bin"));
                Assert.Equal(new ScreenColor(255,255,255),screen.Palette[11]);
                for(int letter=0;letter<text.Length;letter++)for(int row=0;row<8;row++)for(int col=0;col<8;col++)
                {
                    char c=text[letter];int source=12*8192+0xA91+(c-'A')*8;
                    bool expected=c!=' ' && (rom[source+row]&(128>>col))!=0;
                    Assert.Equal(expected,screen.Indices[(y+row)*256+x+letter*8+col]==11);
                }
            }
            var tiles=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","puzzle_tiles.bin"));
            var puzzle=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","puzzle.bin"));
            Assert.Equal(80,tiles.Width);Assert.Equal(80,tiles.Height);
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
            {
                Assert.Equal(puzzle.Indices[(48+y)*256+88+x],tiles.Indices[y*80+x]);
                Assert.Equal(puzzle.Indices[(112+y)*256+152+x],tiles.Indices[(64+y)*80+64+x]);
            }
            foreach(var entry in new[]{("Screens/sound_select_cursor",16,32),("Art/Native/arrow",16,16),("Art/Native/title_cursor",16,8)})
            {
                var image=ScreenAssembler.DecodeFile(Path.Combine(root,entry.Item1+".bin"));
                Assert.Equal(entry.Item2,image.Width);Assert.Equal(entry.Item3,image.Height);
                Assert.Contains(image.Indices,p=>p>=2);
                if(entry.Item1!="Art/Native/title_cursor") {Assert.True(image.HasAlpha);Assert.Contains((byte)0,image.Indices);}
            }
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
