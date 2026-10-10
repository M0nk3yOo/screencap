using Screencap.Core.Annotations;
using Screencap.Core.Geometry;

namespace Screencap.Core.Tests;

public class AnnotationDocumentTests
{
    [Fact]
    public void UndoRedo_RestoresAddedItems()
    {
        var doc = new AnnotationDocument();
        doc.Add(new RectangleAnnotation { Rect = new RectD(0, 0, 10, 10) });
        doc.Add(new RectangleAnnotation { Rect = new RectD(5, 5, 10, 10) });

        doc.Undo();
        Assert.Single(doc.Items);
        doc.Undo();
        Assert.Empty(doc.Items);
        Assert.False(doc.CanUndo);

        doc.Redo();
        doc.Redo();
        Assert.Equal(2, doc.Items.Count);
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void Undo_RevertsInPlaceMove()
    {
        var doc = new AnnotationDocument();
        var arrow = new ArrowAnnotation { Start = new PointD(0, 0), End = new PointD(10, 0) };
        doc.Add(arrow);

        doc.BeginChange();
        arrow.Move(5, 5);
        doc.NotifyChanged();

        doc.Undo();
        var restored = Assert.IsType<ArrowAnnotation>(Assert.Single(doc.Items));
        Assert.Equal(new PointD(0, 0), restored.Start);
    }

    [Fact]
    public void NewChange_ClearsRedo()
    {
        var doc = new AnnotationDocument();
        doc.Add(new HighlightAnnotation());
        doc.Undo();
        doc.Add(new HighlightAnnotation());
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void Counter_ContinuesAfterHighestNumber()
    {
        var doc = new AnnotationDocument();
        Assert.Equal(1, doc.NextCounterNumber());
        doc.Add(new CounterAnnotation { Number = 1 });
        doc.Add(new CounterAnnotation { Number = 4 });
        Assert.Equal(5, doc.NextCounterNumber());
    }

    [Fact]
    public void HitTest_RectangleOnlyOnOutline()
    {
        var doc = new AnnotationDocument();
        var rect = new RectangleAnnotation { Rect = new RectD(0, 0, 100, 100), StrokeWidth = 4 };
        doc.Add(rect);
        Assert.Same(rect, doc.HitTest(new PointD(1, 50), 3));
        Assert.Null(doc.HitTest(new PointD(50, 50), 3));
    }

    [Fact]
    public void HitTest_ReturnsTopmost()
    {
        var doc = new AnnotationDocument();
        doc.Add(new SpotlightAnnotation { Rect = new RectD(0, 0, 100, 100) });
        var top = new SpotlightAnnotation { Rect = new RectD(0, 0, 100, 100) };
        doc.Add(top);
        Assert.Same(top, doc.HitTest(new PointD(50, 50), 0));
    }
}
