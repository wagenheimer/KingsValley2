namespace KingsValley.Engine.Navigation;

/// <summary>
/// A sliced loading step: a name (for the bar) and the work to perform.
///
/// ⚠️ The <c>GraphicsDevice</c> **is not thread-safe** — creating a texture has to happen on the main thread,
/// so the project's pattern is **slicing the work by time** (the <c>LoadingScene</c> runs each
/// step within a budget of ms per frame) and not using <c>Task.Run</c>.
/// </summary>
public readonly record struct LoadStep(string Name, Action Work);
