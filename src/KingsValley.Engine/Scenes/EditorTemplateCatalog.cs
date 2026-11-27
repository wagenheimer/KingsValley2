using KingsValley.Engine.Assets;

namespace KingsValley.Engine.Scenes;

public sealed record EditorTemplateSource(string Variant, string Label, int StageCount, string Directory);

/// <summary>Only complete, installed cartridge generations can supply editor templates.</summary>
public static class EditorTemplateCatalog
{
    public static IReadOnlyList<EditorTemplateSource> Available() => new[]
    {
        (Variant: "msx1", Label: "MSX1 / Original", Count: 60),
        (Variant: "msx2", Label: "MSX2 / Original", Count: 60),
        (Variant: "msx2-contest", Label: "MSX2 / Contest", Count: 20)
    }.Where(source => RomContent.IsVariantAvailable(source.Variant))
        .Select(source => new EditorTemplateSource(source.Variant, source.Label, source.Count, RomContent.LevelsForVariant(source.Variant))).ToArray();
}
