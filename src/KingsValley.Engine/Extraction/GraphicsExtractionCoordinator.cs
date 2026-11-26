#nullable enable
using System;
using System.Threading;

namespace KingsValley.Engine.Extraction;

public sealed record GraphicsExtractionResult(string Directory,GraphicsExtractionManifest Manifest);

public static class GraphicsExtractionCoordinator
{
    public static GraphicsExtractionResult Extract(GraphicsExtractionContext context,Action<string>? log=null,CancellationToken token=default,Action<double>? progress=null)
    {
        string staging=GraphicsGenerationStore.Begin(context.Root);
        var failures=new System.Collections.Generic.List<GraphicsCheck>();
        int steps=0;
        int totalSteps=5;
        void Step(string name,Action action)
        {
            token.ThrowIfCancellationRequested(); log?.Invoke(name); progress?.Invoke(steps/(double)totalSteps);
            try { action(); }
            catch(OperationCanceledException) {throw;}
            catch(NotSupportedException ex) {failures.Add(new(name,"unsupported",Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex)));log?.Invoke(name+": "+Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex));}
            catch(Exception ex) when(ex is not OutOfMemoryException)
            { failures.Add(new(name,"failed",Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex))); log?.Invoke(name+": "+Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex)); }
            finally {steps++;progress?.Invoke(steps/(double)totalSteps);}
        }
        bool cancelled=false;
        try
        {
            Step("stages",()=>
            {
                if(context.Variant==Assets.CartridgeVariants.Contest)ContestCartridge.WriteStages(context.Rom,System.IO.Path.Combine(staging,"Levels"),context.StageLimit);
                else RomStageJson.WriteAll(context.Rom,System.IO.Path.Combine(staging,"Levels"),maxStages:context.StageLimit);
            });
            if(context.Variant==Assets.CartridgeVariants.Contest)
            {
                Step("contest rooms",()=>ContestCartridge.WriteRooms(context.Rom,staging));
                Step("contest screens",()=>ContestCartridge.WriteScreens(context.Rom,staging));
                Step("contest mechanisms",()=>Msx2MechanismFrames.Write(context.Rom,staging,log,token));
                Step("contest atlas",()=>RomDirectExtractor.ExtractMsx2Atlas(context.Rom,staging,log));
            }
            else if(context.Variant=="msx2") Msx2GraphicsPipeline.Extract(context,staging,Step,log,token);
            else Msx1GraphicsPipeline.Extract(context,staging,Step,log,token);
            token.ThrowIfCancellationRequested();
        }
        catch(OperationCanceledException) {cancelled=true;}
        var manifest=GraphicsArtifactValidator.Validate(context,staging);
        manifest.Checks.AddRange(failures);
        manifest.Ready=manifest.Ready && failures.Count==0 && !cancelled;
        manifest.Outcome=cancelled?"cancelled":manifest.Ready?"completed":"partial";
        string published=GraphicsGenerationStore.Finish(context.Root,staging,manifest);
        return new(published,manifest);
    }
}

internal static class Msx2GraphicsPipeline
{
    public static void Extract(GraphicsExtractionContext context,string destination,Action<string,Action> step,Action<string>? log,CancellationToken token)
    {
        step("msx2 rooms",()=>RomDirectExtractor.ExtractMsx2Rooms(context.Rom,destination,log));
        step("msx2 mechanisms",()=>Msx2MechanismFrames.Write(context.Rom,destination,log,token));
        step("msx2 screens",()=>Msx2Screens.WriteSupported(context.Rom,destination,log,token));
        step("msx2 atlas",()=>RomDirectExtractor.ExtractMsx2Atlas(context.Rom,destination,log));
    }
}
internal static class Msx1GraphicsPipeline
{
    public static void Extract(GraphicsExtractionContext context,string destination,Action<string,Action> step,Action<string>? log,CancellationToken token)
    {
        step("msx1 screens",()=>RomScreens.WriteAll(context.Rom,destination,log,token));
        step("msx1 rooms",()=>RomRoomDumps.WriteAll(context.Rom,System.IO.Path.Combine(destination,"Levels"),destination,log,token,context.StageLimit));
        step("msx1 mechanisms",()=>Msx1MechanismFrames.Write(context.Rom,destination,log,token));
        step("msx1 atlas",()=>RomSpriteAtlas.Build(context.Rom,destination,log,token:token));
    }
}
