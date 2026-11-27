#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KingsValley.Core.Screens;
using KingsValley.Core.Stages;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Engine.Extraction;

public static class GraphicsArtifactValidator
{
    public static GraphicsExtractionManifest Validate(GraphicsExtractionContext context,string directory)
    {
        var result = new GraphicsExtractionManifest {Variant=context.Variant,RomSha1=context.RomSha1,StageLimit=context.StageLimit,ProfileRevision=KingsValleyGraphicsProfiles.Revision(context.Variant)};
        void Check(string name, Action action)
        {
            try { action(); result.Checks.Add(new(name,"valid","Structurally validated; pixel fidelity requires a separate reference comparison.")); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            { result.Checks.Add(new(name,"missing-or-invalid",Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex))); }
        }
        void Screen(string path)
        {
            var screen = ScreenAssembler.DecodeFile(Path.Combine(directory,path));
            if (screen.Indices.All(p=>p<2)) throw new InvalidDataException("Required screen is blank: "+path);
            int height = context.Variant == "msx1" ? 192 : 212;
            if (screen.Width == 256 && screen.Height != height && screen.Height != 32) throw new InvalidDataException("Unexpected screen height: "+path);
        }
        var animationRooms=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (int stage=1;stage<=Assets.CartridgeVariants.StageCount(context.Variant);stage++)
        {
            int number=stage;
            Check($"stage-{stage:00}",()=>
            {
                var definition = StageLoader.LoadFile(Path.Combine(directory,"Levels",$"stage{number:00}.json"));
                if (definition.Number != number || definition.Rooms.Count == 0 || definition.Rooms.Any(r=>r.Cells.Length!=768)) throw new InvalidDataException("Stage identity or room inventory is invalid.");
                if (context.Variant == "msx1") foreach (var room in definition.Rooms)
                {
                    animationRooms.Add($"s{number:00}_r{room.Index:00}");
                    Screen($"Rooms/dumps/s{number:00}_r{room.Index:00}.bin");
                }
            });
        }
        foreach (string name in KingsValleyGraphicsProfiles.RequiredScreens(context.Variant)) Check("screen-"+name,()=>Screen("Screens/"+name+".bin"));
        if (context.Variant != "msx1")
        {
            for(int p=1;p<=6;p++)
            {
                int pyramid=p;
                Check("blocks-"+p,()=>
                {
                    byte[] bytes=File.ReadAllBytes(Path.Combine(directory,"Rooms",$"blocks_p{pyramid}.bin"));
                    if(bytes.Length!=8192 || bytes.All(b=>b==0)) throw new InvalidDataException("Required block table is missing, truncated or blank.");
                });
            }
            foreach(string name in new[]{"murals","decorations","palettes","animations"}) Check("table-"+name,()=>
            { using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"Rooms",name+".json"))); if(json.RootElement.ValueKind!=System.Text.Json.JsonValueKind.Object) throw new InvalidDataException("Invalid room table."); });
        }
        else Check("murals",()=> { if(File.ReadAllBytes(Path.Combine(directory,"Rooms","murals.bin")).Length!=6144) throw new InvalidDataException("Expected six native mural tables."); });
        if (context.Variant == "msx1") Check("editor-backgrounds", () =>
        {
            for (int theme = 1; theme <= 6; theme++)
            {
                string root = Path.Combine(directory, "Rooms", "Backgrounds");
                if (File.ReadAllBytes(Path.Combine(root, $"p{theme}.vdp")).Length != 16392)
                    throw new InvalidDataException("Invalid native editor VDP snapshot.");
                foreach (string name in animationRooms.Concat(new[] { "odd", "even" }))
                    if (File.ReadAllBytes(Path.Combine(root, $"p{theme}", name + ".names")).Length != 768)
                        throw new InvalidDataException("Invalid native editor name table.");
            }
        });
        Check("atlas",()=>
        {
            var image=PngRgba.Decode(File.ReadAllBytes(Path.Combine(directory,"Art","kv2.png")));
            var regions=AtlasInventory.Read(File.ReadAllText(Path.Combine(directory,"Art","kv2.atlas")),image).ToDictionary(r=>r.Name,StringComparer.Ordinal);
            foreach(string name in KingsValleyGraphicsProfiles.RequiredSprites(context.Variant))
                Check("sprite-"+name,()=> { if(!regions.TryGetValue(name,out var region) || region.VisiblePixels==0) throw new InvalidDataException("Required sprite is missing or transparent: "+name); });
            if(File.Exists(Path.Combine(directory,"Art","kv2.generated"))) throw new InvalidDataException("Procedural atlas markers are not accepted.");
        });
        for(int pyramid=1;pyramid<=(context.Variant!="msx1"?6:1);pyramid++)
        {
        int capturedPyramid=pyramid;
        string prefix=context.Variant!="msx1"?$"mechanism-p{pyramid}-":"mechanism-";
        string MechanismFile(string name)=>Path.Combine(directory,"Rooms",KingsValleyGraphicsProfiles.MechanismPath(context.Variant,capturedPyramid,name));
        for(int phase=0;phase<4;phase++)
        {
            int frame=phase;
            Check($"{prefix}1-{phase}",()=> { if(File.ReadAllBytes(MechanismFile($"mechanism_t1_p{frame}.bin")).Length!=192) throw new InvalidDataException("Expected a native 16 by 24 mechanism frame."); });
            for(int d=0;d<2;d++)
            {
                int direction=d;
                Check($"{prefix}2-{d}-{phase}",()=> { if(File.ReadAllBytes(MechanismFile($"mechanism_t2_d{direction}_p{frame}.bin")).Length!=192) throw new InvalidDataException("Expected a native 16 by 24 mechanism frame."); });
            }
        }
        Check(prefix+"4",()=> { if(File.ReadAllBytes(MechanismFile("mechanism_t4.bin")).Length!=128) throw new InvalidDataException("Expected a native 32 by 8 mechanism frame."); });
        }
        if(context.Variant=="msx1") Check("pattern-animations",()=>
        {
            Msx1PatternAnimations.ValidateFile(Path.Combine(directory,"Rooms","animations.json"),animationRooms);
        });
        if(context.Variant=="msx1") Check("intro-animation",()=>
        {
            var animation=Wagenheimer.MsxGraphics.Video.NativeActorAnimation.Load(Path.Combine(directory,"Screens","intro_vick.json"));
            var strip=ScreenAssembler.DecodeFile(Path.Combine(directory,"Screens","intro_vick.bin"));
            if(animation.FramesPerSecond!=60 || animation.FrameWidth!=16 || animation.FrameHeight!=32 ||
               strip.Width!=animation.PoseCount*16 || strip.Height!=32 || !strip.HasAlpha ||
               animation.Frames.Any(f=>f.Background>1) || !animation.Frames.Any(f=>f.Background==1))
                throw new InvalidDataException("The native MSX1 intro timeline does not match its sprite strip.");
        });
        if(context.StageLimit!=Assets.CartridgeVariants.StageCount(context.Variant)) result.Checks.Add(new("scope","partial","Diagnostic stage subsets never replace a complete generation."));
        foreach(string file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))
        {
            if(Path.GetFileName(file)==GraphicsExtractionManifest.FileName) continue;
            using var stream=File.OpenRead(file);
            result.Artifacts.Add(new(Path.GetRelativePath(directory,file).Replace('\\','/'),stream.Length,Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),"Produced by the isolated cartridge recipe; structural validation is recorded separately."));
        }
        result.Ready=result.Checks.All(c=>c.State=="valid"); result.Outcome=result.Ready?"completed":"partial";
        return result;
    }
}
