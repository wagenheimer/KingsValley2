#nullable enable
using System;
using System.Linq;
using Gum.GueDeriving;
using Gum.Wireframe;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Instantiates the Gum project's **`.gusx` screens** at runtime — the "UI in the `.gusx`, logic in C#"
/// pattern. The layout lives in the file, editable in the **Gum Tool**, and C# only
/// finds the elements **by name** and wires the behavior.
///
/// ## Why attach a container, and not the whole screen
///
/// A Gum screen is sized by the canvas and lives in the root on its own. The project's dialogs,
/// on the other hand, are a **zero-size center-anchor layer** (the
/// <see cref="GumDialog.Layer"/>) that animates alpha/position on open and close.
///
/// That is why the screen declares a container called <c>DialogLayer</c> with exactly those semantics
/// (offset 0 from the center, size 0) and everything hangs from it. Here we **take that container
/// out of the screen and attach it** to <see cref="GumDialog.Layer"/>: the animation now applies to the content
/// and the coordinates remain offsets from the screen center (the same space as the Unity prefabs).
/// </summary>
internal static class GumScreens
{
    /// <summary>
    /// Materializes the <paramref name="screenName"/> screen and attaches the
    /// <paramref name="layerName"/> container inside <paramref name="parent"/>.
    /// </summary>
    /// <returns>The attached container, or <c>null</c> if the screen/container do not exist.</returns>
    public static ContainerRuntime? CreateLayer(string screenName, string layerName, ContainerRuntime parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var layer = Materialize(screenName, layerName);
        if (layer == null)
            return null;

        // `AddChild` re-parents (removes from the previous parent), so the container leaves the screen and starts
        // living in the dialog layer.
        parent.AddChild(layer);

        Console.WriteLine(
            $"[GumScreens] screen '{screenName}' instantiated ('{layerName}' attached to the dialog).");

        return layer;
    }

    /// <summary>
    /// Same, but for screens that are **not** a dialog (e.g.: `MainMenu`): attaches the container
    /// straight to the Gum **root**.
    /// </summary>
    public static ContainerRuntime? CreateLayerOnRoot(string screenName, string layerName)
    {
        var layer = Materialize(screenName, layerName);
        if (layer == null)
            return null;

        layer.AddToRoot();

        Console.WriteLine($"[GumScreens] screen '{screenName}' instantiated ('{layerName}' in the root).");

        return layer;
    }

    /// <summary>Instantiates the screen and returns the requested container (without attaching it anywhere).</summary>
    private static ContainerRuntime? Materialize(string screenName, string layerName)
    {
        var project = KingsValleyGame.GumProject;
        if (project == null)
        {
            Console.WriteLine(
                $"[GumScreens] Gum project not loaded — the screen '{screenName}' will be omitted.");
            return null;
        }

        var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
        if (screen == null)
        {
            Console.WriteLine(
                $"[GumScreens] screen '{screenName}' does not exist in the GumProject — omitted.");
            return null;
        }

        // `ToGraphicalUiElement` is an `ElementSave` extension in the global `Gum` namespace; inside
        // `...Engine.UI.Gum` the name `Gum` resolves to the project, hence the qualified call.
        var screenVisual = global::Gum.ElementSaveExtensionMethods.ToGraphicalUiElement(screen);

        // Binds the atlas art BY NAME before returning (the `.gusx` stores no coordinates).
        GumAtlasBinder.Bind(screenVisual);

        if (screenVisual.GetGraphicalUiElementByName(layerName) is not ContainerRuntime layer)
        {
            Console.WriteLine(
                $"[GumScreens] the screen '{screenName}' has no container '{layerName}' — omitted.");
            return null;
        }

        return layer;
    }
}
