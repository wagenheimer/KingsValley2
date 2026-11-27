namespace KingsValley.Core.Stages;

/// <summary>Bounded document snapshots. One pointer stroke is one undo operation.</summary>
public sealed class EditorHistory
{
    private readonly List<string> _undo = new(), _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Reset() { _undo.Clear(); _redo.Clear(); }
    public void Record(string before, string after)
    {
        if (before == after) return;
        _undo.Add(before); while (_undo.Count > 100 || _undo.Count > 1 && _undo.Sum(s => (long)s.Length) > 8 * 1024 * 1024) _undo.RemoveAt(0); _redo.Clear();
    }
    public string Undo(string current) => Move(_undo, _redo, current);
    public string Redo(string current) => Move(_redo, _undo, current);
    private static string Move(List<string> source, List<string> destination, string current)
    {
        if (source.Count == 0) return current;
        var value = source[^1]; source.RemoveAt(source.Count - 1); destination.Add(current); return value;
    }
}
