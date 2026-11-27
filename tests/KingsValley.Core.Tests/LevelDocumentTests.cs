using System.Text.Json.Nodes;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public class LevelDocumentTests
{
    [Fact]
    public void CorruptUserDocumentsAreRecoverableAndTemporaryLaddersCannotOverflowTheRoom()
    {
        var error=Assert.Throws<InvalidDataException>(()=>LevelDocument.Parse("{"));
        Assert.True(KingsValley.Engine.Scenes.EditorScene.IsRecoverableEditorAction(error));
        Assert.False(KingsValley.Engine.Scenes.EditorScene.IsRecoverableEditorAction(new NullReferenceException()));
        var stage=JsonNode.Parse(Example())!;
        var ladder=new JsonObject{["room"]=1,["col"]=4,["row"]=23,["length"]=2};
        stage["goldenLadders"]=new JsonArray(ladder);
        Assert.Throws<InvalidDataException>(()=>LevelDocument.Parse(stage.ToJsonString()));
        stage["goldenLadders"]=new JsonArray();
        stage["ladders"]=new JsonArray(ladder.DeepClone());
        Assert.NotNull(LevelDocument.Parse(stage.ToJsonString()));
    }
    private static string Example() => StageTestData.Json(new[] { StageTestData.Box() });

    [Fact]
    public void PortableDocumentsRoundTripAllSourcesAndLegacySlots()
    {
        foreach (string variant in new[] { "msx1", "msx2", "msx2-contest" })
        {
            var original = new LevelDocument("My temple", variant, Example());
            var loaded = LevelDocument.Parse(original.Serialize());
            Assert.Equal(original.Name, loaded.Name); Assert.Equal(variant, loaded.SourceVariant);
            Assert.Equal(original.Stage.PlayerStart, loaded.Stage.PlayerStart);
            Assert.Equal(original.Stage.Rooms[0].Cells, loaded.Stage.Rooms[0].Cells);
        }
        Assert.Equal("msx1", LevelDocument.Parse(Example(), "msx1").SourceVariant);
    }

    [Fact]
    public void MalformedDocumentsNeverBecomeAnEditableStage()
    {
        var document = JsonNode.Parse(new LevelDocument("Test", "msx2", Example()).Serialize())!;
        document["version"] = 99;
        Assert.Throws<InvalidDataException>(() => LevelDocument.Parse(document.ToJsonString()));
        var stage = JsonNode.Parse(Example())!;
        stage["rooms"]![0]!["rows"]![0] = "too short";
        Assert.Throws<InvalidDataException>(() => LevelDocument.Parse(stage.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => LevelDocument.Parse("{"));
    }

    [Fact]
    public void SavingIsAtomicAndCannotOverwriteCartridgeContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "kv-document-" + Guid.NewGuid().ToString("N"));
        try
        {
            string protectedRoot = Path.Combine(root, "cartridge"), file = Path.Combine(protectedRoot, "stage01.json");
            Directory.CreateDirectory(protectedRoot); File.WriteAllText(file, "original");
            var document = new LevelDocument("Test", "msx2", Example());
            Assert.Throws<InvalidOperationException>(() => document.Save(file, new[] { protectedRoot }));
            Assert.Equal("original", File.ReadAllText(file));
            string external = Path.Combine(root, "cartridge-backup", "My temple.kv2level");
            document.Save(external, new[] { protectedRoot });
            Assert.Equal(document.Name, LevelDocument.Load(external).Name);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void UndoGroupsStrokesAndNewChangesInvalidateRedo()
    {
        var history = new EditorHistory(); history.Record("a", "b"); history.Record("b", "c");
        Assert.Equal("b", history.Undo("c")); Assert.Equal("a", history.Undo("b"));
        Assert.Equal("b", history.Redo("a")); history.Record("b", "d"); Assert.False(history.CanRedo);
        history.Reset(); Assert.False(history.CanUndo);
    }
}
