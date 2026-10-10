using Screencap.Core.Annotations;

namespace Screencap.Core.Settings;

/// <summary>Remembered tool settings (sizes in logical points, colors ARGB).</summary>
public sealed class ToolMemory
{
    public uint Color { get; set; } = 0xFFFF3B30;

    /// <summary>Line width per tool name (Arrow, Rectangle, Ellipse).</summary>
    public Dictionary<string, double> StrokeByTool { get; set; } = [];

    public double CounterSize { get; set; } = 3.5;

    public double TextSize { get; set; } = 3.5;
    public string? FontFamily { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public TextStyle TextStyle { get; set; }

    public ObscureStyle ObscureStyle { get; set; }
    public double ObscureStrength { get; set; } = 5;
}
