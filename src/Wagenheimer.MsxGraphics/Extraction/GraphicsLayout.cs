namespace Wagenheimer.MsxGraphics.Extraction;

/// <summary>Pure destination calculation; the caller owns collection-root configuration and I/O.</summary>
public static class GraphicsLayout
{
    public static string GetRoot(string collectionRoot, string gameId, string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionRoot);
        ValidateId(gameId, nameof(gameId));
        ValidateId(variant, nameof(variant));
        return Path.Combine(Path.GetFullPath(collectionRoot), gameId, variant, "Graphics");
    }

    private static void ValidateId(string id, string parameter)
    {
        if (string.IsNullOrEmpty(id) || id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) ||
            id[0] is '-' or '_')
            throw new ArgumentException("Use a lowercase identifier containing letters, digits, hyphens or underscores.", parameter);
    }
}
