using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1SupplementalScreenTests
{
    [LocalRomFact("msx1")]
    public void SupplementalScreensAndCursorsHaveNativeDimensionsPaletteAndContent()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-extra-screens-"+Guid.NewGuid().ToString("N"));
        try
        {
            Msx1SupplementalScreens.Write(rom,root);
            foreach(string name in new[]{"start_select","world_map","intro_msg","konami","sound_select","puzzle","puzzle_bg"})
            {
                var screen=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens",name+".bin"));
                Assert.Equal(256,screen.Width);Assert.Equal(192,screen.Height);
                Assert.True(screen.Indices.Distinct().Count()>1,name);
            }
            var tiles=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","puzzle_tiles.bin"));
            Assert.Equal(80,tiles.Width);Assert.Equal(80,tiles.Height);
            var puzzle=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","puzzle.bin"));
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
            {
                Assert.Equal(puzzle.Indices[(48+y)*256+88+x],tiles.Indices[y*80+x]);
                Assert.True(tiles.Indices[(64+y)*80+64+x]<2); // Consumer slot 24 is the native zero/hole tile.
            }
            var hand=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","sound_select_cursor.bin"));
            Assert.Equal(16,hand.Width);Assert.Equal(32,hand.Height);Assert.True(hand.HasAlpha);
            var arrow=ScreenAssembler.DecodeFile(Path.Combine(root,"Art","Native","arrow.bin"));
            Assert.Equal(16,arrow.Width);Assert.Equal(16,arrow.Height);Assert.True(arrow.HasAlpha);
            var cursor=ScreenAssembler.DecodeFile(Path.Combine(root,"Art","Native","title_cursor.bin"));
            Assert.Equal(16,cursor.Width);Assert.Equal(8,cursor.Height);
            Assert.Contains(cursor.Indices,p=>p>=2);
            var source=RomScreens.CaptureGameplay(rom);
            int patternBase=(source.Registers[4]&4)<<11;
            CheckText("sound_select","SOUND SELECT",80,24);
            CheckText("intro_msg","DO YOUR BEST !!",72,32);
            void CheckText(string name,string text,int x,int y)
            {
                var screen=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens",name+".bin"));
                for(int letter=0;letter<text.Length;letter++) for(int row=0;row<8;row++) for(int col=0;col<8;col++)
                {
                    char c=text[letter];int code=c=='!'?0xDB:0xE1+c-'A';
                    bool expected=c!=' ' && (source.Vram[patternBase+code*8+row]&(128>>col))!=0;
                    Assert.Equal(expected,screen.Indices[(y+row)*256+x+letter*8+col]==15);
                }
            }
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
