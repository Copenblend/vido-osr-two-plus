// generate-icons.csx — Run with: dotnet script generate-icons.csx
// Generates all plugin icons using SkiaSharp
// Install: dotnet tool install -g dotnet-script (if not installed)
// Requires: #r "nuget: SkiaSharp, 2.88.9"

#r "nuget: SkiaSharp, 2.88.9"

using SkiaSharp;

var iconsDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Icons");
Directory.CreateDirectory(iconsDir);

// ── Helper: Save bitmap as PNG ───────────────────────────
void Save(SKBitmap bmp, string name)
{
    var path = Path.Combine(iconsDir, name);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.OpenWrite(path);
    data.SaveTo(fs);
    Console.WriteLine($"  Created: {path}");
}

// ── 1. Sidebar Icon (24x24) — Simplified fleshlight silhouette ──
void GenerateSidebarIcon()
{
    using var bmp = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);

    using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(204, 204, 204) }; // #CCCCCC

    // Rounded body (capsule shape)
    var bodyRect = new SKRect(7, 2, 17, 20);
    canvas.DrawRoundRect(bodyRect, 5, 5, paint);

    // Top rim (slightly wider)
    var rimRect = new SKRect(5, 1, 19, 6);
    canvas.DrawRoundRect(rimRect, 3, 3, paint);

    // Opening circle at top (dark cutout)
    using var cutout = new SKPaint { IsAntialias = true, Color = new SKColor(30, 30, 30) };
    canvas.DrawCircle(12, 3.5f, 3, cutout);

    // Bottom cap
    var capRect = new SKRect(8, 18, 16, 22);
    canvas.DrawRoundRect(capRect, 2, 2, paint);

    Save(bmp, "sidebar-icon.png");
}

// ── 2. Connect Icon (16x16) — Lightning bolt ─────────────
SKBitmap GenerateConnectBase()
{
    var bmp = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);

    using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(204, 204, 204), Style = SKPaintStyle.Fill };

    // Lightning bolt path
    using var path = new SKPath();
    path.MoveTo(9, 1);
    path.LineTo(4, 9);
    path.LineTo(7.5f, 9);
    path.LineTo(6, 15);
    path.LineTo(12, 7);
    path.LineTo(8.5f, 7);
    path.LineTo(9, 1);
    path.Close();

    canvas.DrawPath(path, paint);
    return bmp;
}

void GenerateConnectIcons()
{
    // Base icon (no dot)
    using var baseBmp = GenerateConnectBase();
    Save(baseBmp, "connect-icon.png");

    // With green dot (connected)
    using var greenBmp = GenerateConnectBase();
    using (var canvas = new SKCanvas(greenBmp))
    using (var dot = new SKPaint { IsAntialias = true, Color = new SKColor(0x14, 0xCC, 0x00) })
    {
        canvas.DrawCircle(12.5f, 12.5f, 3.5f, dot);
    }
    Save(greenBmp, "connect-dot-green.png");

    // With red dot (disconnected)
    using var redBmp = GenerateConnectBase();
    using (var canvas = new SKCanvas(redBmp))
    using (var dot = new SKPaint { IsAntialias = true, Color = new SKColor(0xCC, 0x33, 0x33) })
    {
        canvas.DrawCircle(12.5f, 12.5f, 3.5f, dot);
    }
    Save(redBmp, "connect-dot-red.png");
}

// ── 3. Funscript File Icons (16x16) — Waveform symbol ───
void GenerateFunscriptIcon(string name, SKColor color)
{
    using var bmp = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);

    // Document background (rounded rect)
    using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(50, 50, 50) };
    canvas.DrawRoundRect(new SKRect(2, 1, 14, 15), 2, 2, bg);

    // Waveform line
    using var wave = new SKPaint
    {
        IsAntialias = true,
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        StrokeCap = SKStrokeCap.Round
    };

    using var path = new SKPath();
    path.MoveTo(4, 10);
    path.LineTo(6, 4);
    path.LineTo(8, 12);
    path.LineTo(10, 5);
    path.LineTo(12, 10);
    canvas.DrawPath(path, wave);

    Save(bmp, name);
}

// ── Generate all ─────────────────────────────────────────
Console.WriteLine("Generating OSR2+ plugin icons...");
GenerateSidebarIcon();
GenerateConnectIcons();
GenerateFunscriptIcon("funscript-stroke.png", new SKColor(0x00, 0x7A, 0xCC)); // L0 #007ACC
GenerateFunscriptIcon("funscript-twist.png",  new SKColor(0xB8, 0x00, 0xCC)); // R0 #B800CC
GenerateFunscriptIcon("funscript-roll.png",   new SKColor(0xCC, 0x52, 0x00)); // R1 #CC5200
GenerateFunscriptIcon("funscript-pitch.png",  new SKColor(0x14, 0xCC, 0x00)); // R2 #14CC00
Console.WriteLine("Done! All icons generated.");
