#nullable enable
using System;
using global::Gum.Forms.Controls;
using global::Gum.Wireframe;

namespace KingsValley.Engine.UI.Gum;

/// <summary>
/// Creates **Gum Forms** controls using the **project components** (our art + our
/// states) — the "reusable" part of the UI: one component per control, one helper, zero manual hit-testing.
///
/// ## Why a factory (and not `DefaultFormsTemplates`)
///
/// A project component with `ButtonBehavior` is instantiated by the
/// <c>DefaultFromFileButtonRuntime</c> runtime, whose `AfterFullCreation` **already creates and binds** the
/// `FormsControl` to the visual. So:
///
/// | Attempt | Result |
/// | :--- | :--- |
/// | `FrameworkElement.DefaultFormsTemplates[typeof(Button)] = new VisualTemplate(...)` | ❌ Gum rejects: *"the assigned Visual is already the Visual for another framework element"* |
/// | `CreateGueForElement` + `new Button(visual)` | ❌ does not create the children → *"does not have an instance called 'TextInstance'"* |
/// | **`ToGraphicalUiElement` + using the runtime's `FormsControl`** | ✅ children created + control bound |
///
/// (`DefaultFormsTemplates` is for **visuals in code**, like the V3 built-ins — not for
/// file components with a behavior.)
///
/// ## Usage
///
/// <code>
/// var ok = GumFormsTheme.CreateButton("Options/Button");
/// ok.Text = Localization.Loc["ok"];
/// ok.Click += OnOk;
/// container.AddChild(ok);      // Forms extension: adds the `Visual`
/// </code>
/// </summary>
internal static class GumFormsTheme
{
    /// <summary>Project button component (`Options/Button`).</summary>
    public const string ButtonComponent = "Controls/GameButton";

    /// <summary>
    /// Finds the Forms <see cref="Button"/> of an **instance declared in the screen** (`.gusx`) — the
    /// idiomatic path: the UI lives in Gum and C# only wires `Text`/`Click`/`IsEnabled`.
    /// Returns <c>null</c> if the name does not exist (the dialog degrades instead of breaking).
    /// </summary>
    public static Button? FindButton(GraphicalUiElement? root, string instanceName)
        => Find<Button>(root, instanceName);

    /// <summary>Same, for a radio (`Controls/GameRadio`).</summary>
    public static RadioButton? FindRadio(GraphicalUiElement? root, string instanceName)
        => Find<RadioButton>(root, instanceName);

    /// <summary>Same, for a checkbox (`Controls/GameCheckBox`).</summary>
    public static CheckBox? FindCheckBox(GraphicalUiElement? root, string instanceName)
        => Find<CheckBox>(root, instanceName);

    /// <summary>Forms control bound to a screen instance, by name.</summary>
    public static TControl? Find<TControl>(GraphicalUiElement? root, string instanceName)
        where TControl : FrameworkElement
    {
        if (root?.GetGraphicalUiElementByName(instanceName) is InteractiveGue gue
            && gue.FormsControlAsObject is TControl control)
        {
            return control;
        }

        Console.WriteLine($"[GumFormsTheme] '{instanceName}' ({typeof(TControl).Name}) not found on the screen — ignored.");
        return null;
    }

    /// <summary>
    /// Instantiates the component and returns the Forms <see cref="Button"/> already bound to it.
    /// Throws if the component does not exist — it is a configuration error, not a runtime one.
    /// </summary>
    public static Button CreateButton(string componentName = ButtonComponent)
    {
        var visual = CreateVisual(componentName);

        if (visual.FormsControlAsObject is Button button)
            return button;

        // Component without `ButtonBehavior`: falls back to the Forms control over the created visual.
        return new Button(visual);
    }

    /// <summary>
    /// Materializes a **project component** (with the children and the internal `FormsControl` already
    /// created) to be attached to the tree by the caller — it is the path of the "blocks" that are not a
    /// full screen (e.g.: `Controls/GameMessage`, the small frame + buttons).
    ///
    /// The internal controls are found by name (`Find&lt;Button&gt;(visual, "btPrimary")`) and the
    /// variant layout is applied with <c>visual.ApplyStateRecursive(category, state)</c>.
    /// </summary>
    public static InteractiveGue CreateComponent(string componentName) => CreateVisual(componentName);

    /// <summary>
    /// Materializes the project component as a visual, **with the children** (it is
    /// <c>ToGraphicalUiElement</c> that runs `CreateChildrenRecursively` and `AfterFullCreation`).
    /// </summary>
    private static InteractiveGue CreateVisual(string componentName)
    {
        var component = global::Gum.Managers.ObjectFinder.Self.GetElementSave(componentName)
            ?? throw new InvalidOperationException(
                $"Gum component '{componentName}' not found in the project — see GumFormsTheme.");

        var visual = (InteractiveGue)global::Gum.ElementSaveExtensionMethods.ToGraphicalUiElement(component);

        // Binds the atlas art BY NAME (the component declares only size/pivot/position).
        GumAtlasBinder.Bind(visual);

        return visual;
    }
}
