namespace Screencap.App.Controls;

internal enum EditorTool
{
    Select,
    Crop,
    Arrow,
    Rectangle,
    Ellipse,
    Highlight,
    Text,
    Counter,
    Pixelate,
    Spotlight,
    TextRecognition,
    ColorPicker,
}

/// <summary>
/// Maps the toolbar sliders to annotation sizes. Values are logical points; the canvas
/// multiplies by the capture's scaling so sizes look the same on Retina and normal screens.
/// </summary>
internal static class ToolSizes
{
    public const double MinStroke = 1;
    public const double MaxStroke = 12;
    public const double DefaultStroke = 3.5;

    public const double MinStrength = 1;
    public const double MaxStrength = 10;
    public const double DefaultStrength = 5;

    public static double FontFor(double stroke) => 10 + stroke * 3;

    public static double CounterRadiusFor(double stroke) => 7 + stroke * 1.8;

    /// <summary>Inverse of <see cref="FontFor"/>, to show a selected text's size on the slider.</summary>
    public static double StrokeForFont(double font) => (font - 10) / 3;

    public static double StrokeForCounterRadius(double radius) => (radius - 7) / 1.8;

    public static double BlockSizeFor(double strength) => 2 + strength * 1.6;

    public static double BlurRadiusFor(double strength) => strength * 2;

    public static double StrengthForBlockSize(double block) => (block - 2) / 1.6;

    public static double StrengthForBlurRadius(double radius) => radius / 2;
}
