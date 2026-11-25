using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Core.Tests;

public class Msx1MechanismTests
{
    [LocalRomFact("msx1")]
    public void NativeMechanismFramesHaveCompleteInventoryAndVisiblePixels()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-mechanisms-"+Guid.NewGuid().ToString("N"));
        try
        {
            byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
            Msx1MechanismFrames.Write(rom,root);
            Assert.Equal(13,Directory.GetFiles(Path.Combine(root,"Rooms"),"mechanism_*.bin").Length);
            for(int phase=0;phase<4;phase++)
            {
                AssertFrame($"mechanism_t1_p{phase}.bin",192);
                for(int direction=0;direction<2;direction++) AssertFrame($"mechanism_t2_d{direction}_p{phase}.bin",192);
            }
            AssertFrame("mechanism_t4.bin",128);
            void AssertFrame(string name,int bytes)
            {
                byte[] data=File.ReadAllBytes(Path.Combine(root,"Rooms",name));
                Assert.Equal(bytes,data.Length);
                Assert.Contains(Packed4Bpp.Unpack(data,bytes*2),v=>v>=2);
            }
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
