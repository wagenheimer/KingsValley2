using System.Security.Cryptography;
using System.Text.Json;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxAudio;

if(args.Length<2) { Console.WriteLine("Usage: GraphicsAudit inspect <msx1.rom> | screens <msx1.rom> <diagnostic-root> | extract <rom> <collection-root> [stage-count] | verify <generation-directory> <msx1|msx2>"); return 2; }
if(args[0]=="verify")
{
    var manifest=GraphicsExtractionManifest.Read(args[1]);
    bool valid=args.Length>2 && manifest?.Verify(args[1],args[2])==true;
    Console.WriteLine(valid?"Validated complete generation":"Generation is incomplete, corrupted or has mismatched identity"); return valid?0:1;
}
byte[] rom=File.ReadAllBytes(RomFile.Resolve(args[1]));
var profile=RomExtractionProfiles.FindForRom(rom) ?? throw new InvalidDataException("No verified cartridge profile.");
if(args[0]=="screens" && args.Length>=3)
{
    if(profile.Variant!="msx1") throw new InvalidDataException("This diagnostic screen recipe requires MSX1.");
    string directory=Path.Combine(Path.GetFullPath(args[2]),"screen-probe-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    Msx1SupplementalScreens.Write(rom,directory,Console.WriteLine);
    var screenContext=GraphicsExtractionContext.Create(rom,"msx1",args[2],1);
    var manifest=GraphicsArtifactValidator.Validate(screenContext,directory);manifest.Save(directory);
    string screenReport=ExtractionReportBuilder.Build(new(directory,null,Path.Combine(directory,"Report"),Path.GetFileName(args[1]),screenContext.RomSha1,"msx1","partial",0));
    Console.WriteLine(JsonSerializer.Serialize(new{Directory=directory,Report=screenReport}));return 0;
}
if(args[0]=="inspect")
{
    var machine=RomScreens.BootToGameplay(rom);
    Console.WriteLine($"mode={machine.Memory(0xE200):X2} stage={machine.Memory(0xE242)} room={machine.Memory(0xE243)} player={machine.Memory(0xE280):X2}");
    Console.WriteLine("VDP "+Convert.ToHexString(machine.VdpRegisters));
    int sat=(machine.VdpRegisters[5]&127)<<7;
    Console.WriteLine("SAT "+Convert.ToHexString(machine.Vram.AsSpan(sat,64)));
    for(int frame=0;frame<180;frame++)
    {
        machine.Frame();
        if(frame%10==0) Console.WriteLine($"+{frame} mode={machine.Memory(0xE200):X2} player={machine.Memory(0xE280):X2} xy={machine.Memory(0xE284)},{machine.Memory(0xE282)} SAT={Convert.ToHexString(machine.Vram.AsSpan(sat,16))}");
    }
    return 0;
}
if(args[0]!="extract" || args.Length<3) return 2;
int stages=args.Length>3?int.Parse(args[3]):60;
var context=GraphicsExtractionContext.Create(rom,profile.Variant!,args[2],stages);
var timer=System.Diagnostics.Stopwatch.StartNew();
var result=GraphicsExtractionCoordinator.Extract(context,Console.WriteLine);
string audio=MsxAudioPaths.PackagePath("kings-valley-2",context.Variant,args[2]);
string report=ExtractionReportBuilder.Build(new(result.Directory,File.Exists(audio)?audio:null,Path.GetFullPath(Path.Combine(context.Root,"..","Report")),Path.GetFileName(args[1]),context.RomSha1,context.Variant,result.Manifest.Outcome,timer.Elapsed.TotalSeconds),Console.WriteLine);
Console.WriteLine(JsonSerializer.Serialize(new {Directory=result.Directory,Report=report,Ready=result.Manifest.Ready,Seconds=timer.Elapsed.TotalSeconds,Checks=result.Manifest.Checks.Where(c=>c.State!="valid")},new JsonSerializerOptions{WriteIndented=true}));
return result.Manifest.Ready?0:1;
