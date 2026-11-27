#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using KingsValley.Core.SaveSystem;

namespace KingsValley.Engine.Diagnostics;

/// <summary>Bounded, background-written editor timings. Durations include nested scopes.</summary>
internal static class EditorPerformanceLog
{
    private sealed record Entry(DateTime Utc, string Kind, string Name, double Milliseconds, long AllocatedBytes, string? Context, object? Data);
    private sealed class Aggregate
    {
        public int Count, Over16, Over33, Over100;
        public double Total, Maximum;
        public long Bytes;
    }
    private static readonly ConcurrentQueue<Entry> Pending = new();
    private static readonly Dictionary<string,Aggregate> Totals = new();
    private static readonly object Gate = new();
    private static Timer? _timer;
    private static StreamWriter? _writer;
    private static int _queued, _dropped;
    private static readonly object WriterGate = new();
    private static long _lastUpdate, _lastSummary, _inputStart;
    private static string? _inputName;
    private static bool? _focus;
    public static bool Enabled { get; private set; }
    public static string? FilePath { get; private set; }

    static EditorPerformanceLog() => AppDomain.CurrentDomain.ProcessExit += (_,_) => Stop();

    public static void Start(string context, string? directory = null)
    {
        if (Enabled) { Event("session", "editor_enter", context); return; }
        try
        {
            directory ??= Path.Combine(Path.GetDirectoryName(ProfileSaveService.SaveFilePath)!, "Diagnostics");
            Directory.CreateDirectory(directory);
            FilePath=Path.Combine(directory,$"editor-performance-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.jsonl");
            _writer=new StreamWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite));
            _lastUpdate=0;_lastSummary=Stopwatch.GetTimestamp();_focus=null; Totals.Clear();
            Enabled=true;
            Event("session","start",context,new { version=typeof(EditorPerformanceLog).Assembly.GetName().Version?.ToString(), runtime=Environment.Version.ToString(), os=Environment.OSVersion.ToString(), cpuCount=Environment.ProcessorCount, process=Environment.ProcessId });
            _timer=new Timer(_=>Flush(),null,1000,1000);
            Console.WriteLine("[Editor diagnostics] Automatic performance log: "+FilePath);
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException)
        { Enabled=false; Console.WriteLine("[Editor diagnostics] Logging unavailable: "+ex.Message); }
    }

    public static Scope Measure(string name,string? context=null) => Enabled ? new(name,context) : default;
    public readonly struct Scope : IDisposable
    {
        private readonly string? _name, _context;
        private readonly long _start, _bytes;
        internal Scope(string name,string? context) { _name=name;_context=context;_start=Stopwatch.GetTimestamp();_bytes=GC.GetAllocatedBytesForCurrentThread(); }
        public void Dispose() { if(_name!=null) Record(_name,Stopwatch.GetElapsedTime(_start).TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-_bytes,_context); }
    }

    private static void Record(string name,double ms,long bytes,string? context)
    {
        if(!Enabled)return;
        lock(Gate)
        {
            if(!Totals.TryGetValue(name,out var value))Totals[name]=value=new();
            value.Count++;value.Total+=ms;value.Maximum=Math.Max(value.Maximum,ms);value.Bytes+=bytes;
            if(ms>=16)value.Over16++;if(ms>=33)value.Over33++;if(ms>=100)value.Over100++;
        }
        if(ms>=(name=="frame.interval"?33:16))Enqueue(new(DateTime.UtcNow,"slow",name,ms,bytes,context,null));
    }

    public static void Frame(string context,bool isActive=true)
    {
        if(!Enabled)return;
        if(_focus!=isActive){_focus=isActive;Event("focus",isActive?"active":"inactive",context);}
        long now=Stopwatch.GetTimestamp();
        if(_lastUpdate!=0)Record("frame.interval",Stopwatch.GetElapsedTime(_lastUpdate,now).TotalMilliseconds,0,context);
        _lastUpdate=now;
        if(Stopwatch.GetElapsedTime(_lastSummary,now).TotalSeconds<2)return;
        _lastSummary=now;
        ReportSummary(context);
    }

    internal static void ReportSummary(string context)
    {
        object[] phases;
        lock(Gate)
        {
            phases=Totals.Select(p=>(object)new { name=p.Key,count=p.Value.Count,averageMs=p.Value.Total/Math.Max(1,p.Value.Count),maximumMs=p.Value.Maximum,over16ms=p.Value.Over16,over33ms=p.Value.Over33,over100ms=p.Value.Over100,allocatedBytes=p.Value.Bytes }).ToArray();
            Totals.Clear();
        }
        Event("summary","frame_phases",context,new { phases, gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),heapBytes=GC.GetTotalMemory(false),droppedEvents=_dropped });
    }

    public static void Input(string name,string context)
    {
        if(!Enabled)return;
        _inputStart=Stopwatch.GetTimestamp();_inputName=name;Event("input",name,context);
    }
    public static void DrawCompleted()
    {
        if(!Enabled||_inputStart==0)return;
        Enqueue(new(DateTime.UtcNow,"input_to_draw",_inputName??"action",Stopwatch.GetElapsedTime(_inputStart).TotalMilliseconds,0,"CPU draw completed; excludes monitor presentation latency",null));
        _inputStart=0;
    }
    public static void Event(string kind,string name,string? context=null,object? data=null)
    {if(Enabled)Enqueue(new(DateTime.UtcNow,kind,name,0,0,context,data));}
    private static void Enqueue(Entry entry)
    {
        if(Interlocked.Increment(ref _queued)>1024){Interlocked.Decrement(ref _queued);Interlocked.Increment(ref _dropped);return;}
        Pending.Enqueue(entry);
    }

    internal static void Flush()
    {
        lock(WriterGate)
        {
        try
        {
            while(Pending.TryDequeue(out var entry))
            {
                Interlocked.Decrement(ref _queued);
                if(_writer==null)continue;
                if(_writer.BaseStream.Position>=5*1024*1024){Interlocked.Increment(ref _dropped);continue;}
                _writer.WriteLine(JsonSerializer.Serialize(entry));
            }
            _writer?.Flush();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {Enabled=false;Console.WriteLine("[Editor diagnostics] Logging stopped: "+ex.Message);}
        }
    }
    internal static void Stop()
    {
        _timer?.Dispose();_timer=null;
        lock(WriterGate)
        {
            Event("session","stop");Flush();Enabled=false;_writer?.Dispose();_writer=null;
        }
    }
}
