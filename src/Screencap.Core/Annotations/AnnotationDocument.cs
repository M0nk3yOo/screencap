using Screencap.Core.Geometry;

namespace Screencap.Core.Annotations;

/// <summary>
/// The editable state of one screenshot — annotations and crop — plus snapshot-based
/// undo/redo. Call <see cref="BeginChange"/> before mutating so the previous state can be restored.
/// </summary>
public sealed class AnnotationDocument
{
    private const int MaxHistory = 100;

    private sealed record State(List<Annotation> Items, IntRect? Crop);

    private readonly List<Annotation> _items = [];
    private readonly LinkedList<State> _undo = new();
    private readonly Stack<State> _redo = new();

    public AnnotationDocument() { }

    public AnnotationDocument(IEnumerable<Annotation> items, IntRect? crop = null)
    {
        _items.AddRange(items);
        Crop = crop;
    }

    public IReadOnlyList<Annotation> Items => _items;

    /// <summary>
    /// Visible part of the image in image pixels; null = uncropped. Cropping is
    /// non-destructive: annotations keep full-image coordinates and can be re-cropped.
    /// </summary>
    public IntRect? Crop { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True once the user changed something since the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

    public void BeginChange()
    {
        _undo.AddLast(Snapshot());
        if (_undo.Count > MaxHistory) _undo.RemoveFirst();
        _redo.Clear();
    }

    public void Add(Annotation annotation)
    {
        BeginChange();
        _items.Add(annotation);
        NotifyChanged();
    }

    public void Remove(Annotation annotation)
    {
        if (!_items.Contains(annotation)) return;
        BeginChange();
        _items.Remove(annotation);
        NotifyChanged();
    }

    /// <summary>Changes the stacking order (0 = bottom, drawn first).</summary>
    public void MoveTo(Annotation annotation, int index)
    {
        var current = _items.IndexOf(annotation);
        index = Math.Clamp(index, 0, _items.Count - 1);
        if (current < 0 || current == index) return;
        BeginChange();
        _items.RemoveAt(current);
        _items.Insert(index, annotation);
        NotifyChanged();
    }

    public void SetHidden(Annotation annotation, bool hidden)
    {
        if (annotation.Hidden == hidden || !_items.Contains(annotation)) return;
        BeginChange();
        annotation.Hidden = hidden;
        NotifyChanged();
    }

    public void SetCrop(IntRect? crop)
    {
        if (crop == Crop) return;
        BeginChange();
        Crop = crop;
        NotifyChanged();
    }

    public void Clear()
    {
        if (_items.Count is 0) return;
        BeginChange();
        _items.Clear();
        NotifyChanged();
    }

    /// <summary>Signals an in-place edit (move, text change) after <see cref="BeginChange"/>.</summary>
    public void NotifyChanged()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Last is not { } last) return;
        _undo.RemoveLast();
        _redo.Push(Snapshot());
        Restore(last.Value);
    }

    public void Redo()
    {
        if (_redo.Count is 0) return;
        _undo.AddLast(Snapshot());
        Restore(_redo.Pop());
    }

    public void MarkSaved() => IsDirty = false;

    /// <summary>Topmost annotation under the point, or null.</summary>
    public Annotation? HitTest(PointD p, double tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (!_items[i].Hidden && _items[i].HitTest(p, tolerance)) return _items[i];
        return null;
    }

    public int NextCounterNumber() =>
        _items.OfType<CounterAnnotation>().Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;

    private State Snapshot() => new(_items.Select(a => a.Clone()).ToList(), Crop);

    private void Restore(State state)
    {
        _items.Clear();
        _items.AddRange(state.Items);
        Crop = state.Crop;
        NotifyChanged();
    }
}
