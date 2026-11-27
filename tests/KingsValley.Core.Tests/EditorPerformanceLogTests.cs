using System.Globalization;
using System.Text.Json;
using KingsValley.Engine.Diagnostics;

namespace KingsValley.Core.Tests;

[Collection(Msx1ExtractionTestCollection.Name)]
public class EditorPerformanceLogTests
{
    [Fact]
    public void AutomaticLogPreservesActionTimingsAndWritesCultureIndependentJson()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv2-performance-"+Guid.NewGuid().ToString("N"));
        var culture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("pt-BR");
            EditorPerformanceLog.Start("fixture",root);
            string path=EditorPerformanceLog.FilePath!;
            EditorPerformanceLog.Input("room_map.1,0.room_2","stage=3 room=1");
            using(EditorPerformanceLog.Measure("ui.rebuild","fixture")) { }
            EditorPerformanceLog.DrawCompleted();
            EditorPerformanceLog.ReportSummary("fixture");
            EditorPerformanceLog.Stop();
            var entries=File.ReadAllLines(path).Select(line=>JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Contains(entries,e=>e.RootElement.GetProperty("Kind").GetString()=="input");
                var draw=Assert.Single(entries,e=>e.RootElement.GetProperty("Kind").GetString()=="input_to_draw");
                Assert.True(draw.RootElement.GetProperty("Milliseconds").GetDouble()>=0);
                var summary=Assert.Single(entries,e=>e.RootElement.GetProperty("Kind").GetString()=="summary");
                var phase=Assert.Single(summary.RootElement.GetProperty("Data").GetProperty("phases").EnumerateArray());
                Assert.Equal("ui.rebuild",phase.GetProperty("name").GetString());
                Assert.Equal(1,phase.GetProperty("count").GetInt32());
                Assert.True(phase.GetProperty("averageMs").GetDouble()>=0);
                Assert.False(EditorPerformanceLog.Enabled);
            }
            finally {foreach(var entry in entries)entry.Dispose();}
        }
        finally
        {
            EditorPerformanceLog.Stop();CultureInfo.CurrentCulture=culture;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
