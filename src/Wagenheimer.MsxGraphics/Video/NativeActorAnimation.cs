using System.Text.Json;

namespace Wagenheimer.MsxGraphics.Video;

public sealed record NativeActorFrame(int X,int Y,int Pose,int Background);

/// <summary>Recorded native-frame positions and deduplicated indexed sprite-strip poses.</summary>
public sealed class NativeActorAnimation
{
    public int Schema {get;set;}=1;
    public int FramesPerSecond {get;set;}=60;
    public int FrameWidth {get;set;}=16;
    public int FrameHeight {get;set;}=32;
    public int PoseCount {get;set;}
    public List<NativeActorFrame> Frames {get;set;}=new();
    public void Validate()
    {
        if(Schema!=1 || FramesPerSecond is <1 or >240 || FrameWidth is <1 or >256 || FrameHeight is <1 or >212 ||
           PoseCount is <1 or >512 || Frames==null || Frames.Count is <1 or >24000 ||
           Frames.Any(f=>f==null || f.Pose< -1 || f.Pose>=PoseCount || f.Background<0 || f.Background>255 || f.X<0 || f.X>255 || f.Y<0 || f.Y>211))
            throw new InvalidDataException("Invalid native actor animation contract.");
    }
    public NativeActorFrame At(double seconds)
    {
        if(!double.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
        return Frames[(int)Math.Min(Frames.Count-1,Math.Max(0,seconds)*FramesPerSecond)];
    }
    public void Save(string path){Validate();File.WriteAllText(path,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
    public static NativeActorAnimation Load(string path)
    {
        var animation=JsonSerializer.Deserialize<NativeActorAnimation>(File.ReadAllText(path))??throw new InvalidDataException("Native actor animation is empty.");
        animation.Validate();return animation;
    }
}
