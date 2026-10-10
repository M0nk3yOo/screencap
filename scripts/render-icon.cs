#:package SkiaSharp
// Renders the app icon: assets/AppIcon.png (1024 px) and assets/AppIcon.ico (Windows).
// Usage: dotnet run scripts/render-icon.cs -- <assets folder>
using SkiaSharp;

var folder = args.Length > 0 ? args[0] : "assets";
Directory.CreateDirectory(folder);

byte[] Render(int size)
{
    var s = size / 1024f;
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(s);

    // macOS icon grid: 824 px tile with a soft shadow.
    var tile = new SKRoundRect(new SKRect(100, 100, 924, 924), 185);
    using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70),
               ImageFilter = SKImageFilter.CreateBlur(18, 18) })
    {
        canvas.Save();
        canvas.Translate(0, 12);
        canvas.DrawRoundRect(tile, shadow);
        canvas.Restore();
    }
    using (var fill = new SKPaint { IsAntialias = true })
    {
        fill.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 100), new SKPoint(0, 924),
            [new SKColor(0x34, 0xD3, 0x99), new SKColor(0x04, 0x78, 0x57)], SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(tile, fill);
    }

    // Viewfinder corners and a center dot.
    using var stroke = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke,
        StrokeWidth = 58, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    const float l = 290, t = 290, r = 734, b = 734, arm = 140;
    void Corner(float x, float y, float dx, float dy)
    {
        using var path = new SKPath();
        path.MoveTo(x, y + dy * arm);
        path.LineTo(x, y);
        path.LineTo(x + dx * arm, y);
        canvas.DrawPath(path, stroke);
    }
    Corner(l, t, 1, 1);
    Corner(r, t, -1, 1);
    Corner(l, b, 1, -1);
    Corner(r, b, -1, -1);
    using var dot = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawCircle(512, 512, 74, dot);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

File.WriteAllBytes(Path.Combine(folder, "AppIcon.png"), Render(1024));

// ICO with embedded PNGs (supported since Windows Vista).
int[] sizes = [16, 24, 32, 48, 64, 256];
var images = sizes.Select(Render).ToArray();
using var ico = new BinaryWriter(File.Create(Path.Combine(folder, "AppIcon.ico")));
ico.Write((short)0);
ico.Write((short)1);
ico.Write((short)sizes.Length);
var offset = 6 + 16 * sizes.Length;
for (var i = 0; i < sizes.Length; i++)
{
    ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    ico.Write((byte)0);
    ico.Write((byte)0);
    ico.Write((short)1);
    ico.Write((short)32);
    ico.Write(images[i].Length);
    ico.Write(offset);
    offset += images[i].Length;
}
foreach (var png in images) ico.Write(png);
Console.WriteLine($"Icons written to {folder}");
