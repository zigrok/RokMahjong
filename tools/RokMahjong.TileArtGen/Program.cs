using System.Text.Json;
using SkiaSharp;

const int CellWidth = 256;
const int CellHeight = 340;
const int FaceWidth = 208;
const int FaceHeight = 288;
const int FaceLeft = 18;
const int FaceTop = 18;
const int Columns = 6;
const int Rows = 7;
const int ReferenceScale = 4;

const float SideOffsetX = 14f;
const float SideOffsetY = 16f;
const float CornerRadius = 16f;

var root = FindRoot();
var outputDirectory = Path.Combine(root, "assets", "art");
var referenceDirectory = Path.Combine(outputDirectory, "reference");
var glyphDirectory = Path.Combine(outputDirectory, "glyphs");
var legacyFaceDirectory = Path.Combine(outputDirectory, "faces");
var fontPath = Path.Combine(root, "assets", "fonts", "NotoSerifCJKsc-Subset.otf");
Directory.CreateDirectory(outputDirectory);
Directory.CreateDirectory(referenceDirectory);

using var typeface = SKTypeface.FromFile(fontPath)
    ?? throw new InvalidOperationException($"Could not load tile font: {fontPath}");
using var bitmap = new SKBitmap(new SKImageInfo(Columns * CellWidth, Rows * CellHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
using var canvas = new SKCanvas(bitmap);
canvas.Clear(SKColors.Transparent);

var numerals = new[] { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
var windGlyphs = new[] { "東", "南", "西", "北" };
var red = new SKColor(164, 42, 45);
var green = new SKColor(31, 108, 68);
var blue = new SKColor(39, 77, 138);
var gold = new SKColor(184, 138, 52);

var faces = BuildFaces();
var withGenerated = 0;
for (var index = 0; index < faces.Count; index++)
{
    var face = faces[index];
    using var reference = RenderReference(face, typeface);
    WriteReference(reference, face);

    var glyph = LoadGlyphSource(glyphDirectory, legacyFaceDirectory, face, reference);
    if (!ReferenceEquals(glyph, reference)) withGenerated++;

    DrawTile(canvas, index, glyph);

    if (!ReferenceEquals(glyph, reference)) glyph.Dispose();
}

using (var image = SKImage.FromBitmap(bitmap))
using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
using (var stream = File.Create(Path.Combine(outputDirectory, "tile-atlas.png")))
    encoded.SaveTo(stream);

var metadata = new
{
    columns = Columns,
    rows = Rows,
    cellWidth = CellWidth,
    cellHeight = CellHeight,
    faceLeft = FaceLeft,
    faceTop = FaceTop,
    faceWidth = FaceWidth,
    faceHeight = FaceHeight,
    faceCount = faces.Count,
    faceOrder = faces.Select(face => face.Label).ToArray(),
};
File.WriteAllText(
    Path.Combine(outputDirectory, "tile-atlas.json"),
    JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Wrote {faces.Count} extruded tile faces to {outputDirectory} ({withGenerated} from generated glyphs or legacy faces).");
Console.WriteLine($"Wrote {faces.Count} body-less reference faces to {referenceDirectory}.");

static string FindRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "RokMahjong.slnx"))) return current.FullName;
        current = current.Parent;
    }
    return Directory.GetCurrentDirectory();
}

static List<TileFaceSpec> BuildFaces()
{
    var faces = new List<TileFaceSpec>(42);
    for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFaceSpec("Characters", rank, $"C{rank}"));
    for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFaceSpec("Bamboo", rank, $"B{rank}"));
    for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFaceSpec("Circles", rank, $"D{rank}"));
    var winds = new[] { "E", "S", "W", "N" };
    for (var i = 0; i < winds.Length; i++) faces.Add(new TileFaceSpec("Wind", i + 1, winds[i]));
    var dragons = new[] { "R", "G", "Wh" };
    for (var i = 0; i < dragons.Length; i++) faces.Add(new TileFaceSpec("Dragon", i + 1, dragons[i]));
    var flowers = new[] { "梅", "蘭", "菊", "竹" };
    for (var rank = 0; rank < flowers.Length; rank++)
        faces.Add(new TileFaceSpec("Flower", rank + 1, $"F{rank + 1}"));
    var seasons = new[] { "春", "夏", "秋", "冬" };
    for (var rank = 0; rank < seasons.Length; rank++)
        faces.Add(new TileFaceSpec("Season", rank + 1, $"Se{rank + 1}"));
    return faces;
}

SKBitmap RenderReference(TileFaceSpec face, SKTypeface typeface)
{
    var reference = new SKBitmap(new SKImageInfo(
        FaceWidth * ReferenceScale,
        FaceHeight * ReferenceScale,
        SKColorType.Rgba8888,
        SKAlphaType.Premul));
    using var referenceCanvas = new SKCanvas(reference);
    referenceCanvas.Clear(SKColors.Transparent);
    referenceCanvas.Scale(ReferenceScale);
    referenceCanvas.DrawRect(new SKRect(0, 0, FaceWidth, FaceHeight), new SKPaint { Color = new SKColor(250, 243, 224) });
    var artRect = new SKRect(28, 24, FaceWidth - 28, FaceHeight - 24);
    referenceCanvas.Save();
    referenceCanvas.ClipRect(artRect);
    DrawFace(referenceCanvas, face, artRect, typeface);
    referenceCanvas.Restore();
    return reference;
}

void WriteReference(SKBitmap reference, TileFaceSpec face)
{
    using var image = SKImage.FromBitmap(reference);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(Path.Combine(referenceDirectory, $"{face.Label}.png"));
    encoded.SaveTo(stream);
}

static SKBitmap LoadGlyphSource(
    string glyphDirectory,
    string legacyFaceDirectory,
    TileFaceSpec face,
    SKBitmap reference)
{
    var glyphPath = Path.Combine(glyphDirectory, $"{face.Label}.png");
    if (File.Exists(glyphPath))
    {
        var glyph = DecodeUnpremultiplied(glyphPath);
        if (glyph is not null) return glyph;
    }

    var legacyPath = Path.Combine(legacyFaceDirectory, $"{face.Label}.png");
    if (File.Exists(legacyPath))
    {
        var legacy = CropInnerArt(legacyPath);
        if (legacy is not null) return legacy;
    }

    return reference;
}

static SKBitmap? DecodeUnpremultiplied(string path)
{
    using var source = SKBitmap.Decode(path);
    if (source is null) return null;
    var converted = new SKBitmap(new SKImageInfo(
        source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var convertedCanvas = new SKCanvas(converted);
    convertedCanvas.Clear(SKColors.Transparent);
    using var paint = new SKPaint { IsAntialias = true };
    convertedCanvas.DrawBitmap(
        source,
        new SKRect(0, 0, source.Width, source.Height),
        new SKRect(0, 0, source.Width, source.Height),
        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        paint);
    return converted;
}

static SKBitmap? CropInnerArt(string path)
{
    using var source = SKBitmap.Decode(path);
    if (source is null) return null;
    var left = source.Width * 28f / FaceWidth;
    var top = source.Height * 24f / FaceHeight;
    var right = source.Width * (FaceWidth - 28f) / FaceWidth;
    var bottom = source.Height * (FaceHeight - 24f) / FaceHeight;
    var width = Math.Max(1, (int)MathF.Round(right - left));
    var height = Math.Max(1, (int)MathF.Round(bottom - top));
    var cropped = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var croppedCanvas = new SKCanvas(cropped);
    croppedCanvas.Clear(SKColors.Transparent);
    using var paint = new SKPaint { IsAntialias = true };
    croppedCanvas.DrawBitmap(
        source,
        new SKRect(left, top, right, bottom),
        new SKRect(0, 0, width, height),
        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        paint);
    return cropped;
}

void DrawTile(SKCanvas canvas, int index, SKBitmap texture)
{
    var column = index % Columns;
    var row = index / Columns;
    // The logical tile footprint is the full face rect. The top face is inset
    // inside it and the extrusion fills the right/bottom bands, so neighbouring
    // tiles can never cover the side.
    var footprint = new SKRect(FaceLeft, FaceTop, FaceLeft + FaceWidth, FaceTop + FaceHeight);
    var face = new SKRect(
        footprint.Left,
        footprint.Top,
        footprint.Right - SideOffsetX,
        footprint.Bottom - SideOffsetY);

    canvas.Save();
    canvas.Translate(column * CellWidth, row * CellHeight);

    // Soft contact shadow.
    using (var shadow = new SKPaint
           {
               Color = new SKColor(0, 0, 0, 55),
               IsAntialias = true,
               MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7),
           })
        canvas.DrawRoundRect(
            new SKRect(footprint.Left + 4, footprint.Top + 7, footprint.Right + 3, footprint.Bottom + 3),
            CornerRadius,
            CornerRadius,
            shadow);

    // The right and bottom sides: an axis-aligned extrusion inside the
    // footprint, no perspective.
    using (var sideShader = SKShader.CreateLinearGradient(
               new SKPoint(0, footprint.Top),
               new SKPoint(0, footprint.Bottom),
               new[] { new SKColor(238, 224, 192), new SKColor(206, 186, 146) },
               null,
               SKShaderTileMode.Clamp))
    using (var sidePaint = new SKPaint { Shader = sideShader, IsAntialias = true })
        canvas.DrawRoundRect(footprint, CornerRadius, CornerRadius, sidePaint);

    using (var sideEdge = new SKPaint
           {
               Color = new SKColor(184, 160, 116),
               Style = SKPaintStyle.Stroke,
               StrokeWidth = 1.2f,
               IsAntialias = true,
           })
        canvas.DrawRoundRect(footprint, CornerRadius, CornerRadius, sideEdge);

    // The top face is a perfect rectangle: vertical and horizontal edges only.
    using (var faceShader = SKShader.CreateLinearGradient(
               new SKPoint(0, face.Top),
               new SKPoint(0, face.Bottom),
               new[] { new SKColor(255, 253, 246), new SKColor(246, 236, 214) },
               null,
               SKShaderTileMode.Clamp))
    using (var facePaint = new SKPaint { Shader = faceShader, IsAntialias = true })
        canvas.DrawRoundRect(face, CornerRadius, CornerRadius, facePaint);

    canvas.Save();
    canvas.ClipRoundRect(new SKRoundRect(face, CornerRadius, CornerRadius), SKClipOperation.Intersect, true);
    using (var texturePaint = new SKPaint { IsAntialias = true })
        canvas.DrawBitmap(
            texture,
            new SKRect(0, 0, texture.Width, texture.Height),
            face,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            texturePaint);
    canvas.Restore();

    // Inner highlight and the thin gold rule around the face.
    var inner = new SKRect(face.Left + 2, face.Top + 2, face.Right - 2, face.Bottom - 2);
    using (var highlight = new SKPaint
           {
               Color = new SKColor(255, 255, 255, 150),
               Style = SKPaintStyle.Stroke,
               StrokeWidth = 1.4f,
               IsAntialias = true,
           })
        canvas.DrawRoundRect(inner, CornerRadius - 2, CornerRadius - 2, highlight);

    using (var border = new SKPaint
           {
               Color = new SKColor(201, 169, 106),
               Style = SKPaintStyle.Stroke,
               StrokeWidth = 1.6f,
               IsAntialias = true,
           })
        canvas.DrawRoundRect(face, CornerRadius, CornerRadius, border);

    canvas.Restore();
}

void DrawFace(SKCanvas canvas, TileFaceSpec face, SKRect art, SKTypeface typeface)
{
    switch (face.Group)
    {
        case "Characters":
            DrawCharacter(canvas, face.Rank, art, typeface);
            break;
        case "Bamboo":
            DrawBamboo(canvas, face.Rank, art);
            break;
        case "Circles":
            DrawCircles(canvas, face.Rank, art);
            break;
        case "Wind":
            DrawWind(canvas, face.Rank, art, typeface);
            break;
        case "Dragon":
            DrawDragon(canvas, face.Rank, art, typeface);
            break;
        case "Flower":
            DrawFlower(canvas, face.Rank, art, typeface);
            break;
        case "Season":
            DrawSeason(canvas, face.Rank, art, typeface);
            break;
    }
}

void DrawCharacter(SKCanvas canvas, int rank, SKRect art, SKTypeface typeface)
{
    var center = art.MidX;
    DrawText(canvas, numerals[rank - 1], center, art.Top + 92, 92, red, typeface);
    DrawText(canvas, "萬", center, art.Bottom - 62, 82, red, typeface);
    using var line = new SKPaint { Color = new SKColor(164, 42, 45, 90), StrokeWidth = 2, IsAntialias = true };
    canvas.DrawLine(art.Left + 28, art.Top + 128, art.Right - 28, art.Top + 128, line);
}

void DrawBamboo(SKCanvas canvas, int rank, SKRect art)
{
    if (rank == 1)
    {
        DrawBambooStick(canvas, art.MidX, art.MidY + 4, 44, 172, green);
        DrawLeaf(canvas, art.MidX - 36, art.MidY - 46, 30, 14, new SKColor(44, 126, 72));
        DrawLeaf(canvas, art.MidX + 36, art.MidY - 22, 30, 14, new SKColor(44, 126, 72));
        return;
    }

    var (columns, rows) = rank switch
    {
        2 => (1, 2),
        3 => (2, 2),
        4 => (2, 2),
        5 => (2, 2),
        6 => (2, 3),
        7 => (2, 3),
        8 => (2, 4),
        _ => (3, 3),
    };
    var positions = GridPositions(art, columns, rows, 44, 50);
    if (rank == 3) positions = new[] { (art.MidX, art.Top + 62), (art.Left + 56, art.Bottom - 58), (art.Right - 56, art.Bottom - 58) };
    if (rank == 5) positions = new[] { (art.Left + 56, art.Top + 56), (art.Right - 56, art.Top + 56), (art.MidX, art.MidY), (art.Left + 56, art.Bottom - 56), (art.Right - 56, art.Bottom - 56) };
    if (rank == 7) positions = new[] { (art.MidX, art.Top + 42), (art.Left + 56, art.Top + 110), (art.Right - 56, art.Top + 110), (art.Left + 56, art.Bottom - 102), (art.Right - 56, art.Bottom - 102), (art.Left + 56, art.Bottom - 42), (art.Right - 56, art.Bottom - 42) };
    if (rank == 9) positions = GridPositions(art, 3, 3, 52, 56);

    var index = 0;
    foreach (var (x, y) in positions)
    {
        DrawBambooStick(canvas, x, y, 24, rank >= 8 ? 42 : 48, index % 3 == 0 ? new SKColor(31, 108, 68) : new SKColor(44, 126, 72));
        index++;
    }
}

static void DrawBambooStick(SKCanvas canvas, float x, float y, float width, float height, SKColor color)
{
    var rect = new SKRect(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
    using var paint = new SKPaint { Color = color, IsAntialias = true };
    canvas.DrawRoundRect(rect, width / 2, width / 2, paint);
    using var highlight = new SKPaint
    {
        Color = new SKColor(255, 255, 255, 100),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2,
        IsAntialias = true,
    };
    canvas.DrawLine(rect.Left + width * 0.28f, rect.Top + 8, rect.Left + width * 0.28f, rect.Bottom - 8, highlight);
    using var joint = new SKPaint { Color = new SKColor(19, 78, 48), StrokeWidth = 2, IsAntialias = true };
    canvas.DrawLine(rect.Left, rect.Top + height * 0.34f, rect.Right, rect.Top + height * 0.34f, joint);
    canvas.DrawLine(rect.Left, rect.Top + height * 0.67f, rect.Right, rect.Top + height * 0.67f, joint);
    DrawLeaf(canvas, x + width * 0.3f, rect.Top - 1, width * 0.8f, width * 0.35f, color);
}

static void DrawLeaf(SKCanvas canvas, float x, float y, float width, float height, SKColor color)
{
    using var paint = new SKPaint { Color = color, IsAntialias = true };
    canvas.Save();
    canvas.RotateDegrees(-28, x, y);
    canvas.DrawOval(new SKRect(x - width / 2, y - height / 2, x + width / 2, y + height / 2), paint);
    canvas.Restore();
}

void DrawCircles(SKCanvas canvas, int rank, SKRect art)
{
    if (rank == 1)
    {
        DrawDot(canvas, art.MidX, art.MidY, 64, blue);
        return;
    }

    var positions = rank switch
    {
        2 => new[] { (art.MidX, art.Top + 72), (art.MidX, art.Bottom - 72) },
        3 => new[] { (art.Left + 52, art.Top + 62), (art.MidX, art.MidY), (art.Right - 52, art.Bottom - 62) },
        4 => GridPositions(art, 2, 2, 104, 104),
        5 => new[] { (art.Left + 52, art.Top + 56), (art.Right - 52, art.Top + 56), (art.MidX, art.MidY), (art.Left + 52, art.Bottom - 56), (art.Right - 52, art.Bottom - 56) },
        6 => GridPositions(art, 2, 3, 104, 76),
        7 => new[] { (art.MidX, art.Top + 42) }.Concat(GridPositions(new SKRect(art.Left, art.Top + 82, art.Right, art.Bottom), 2, 3, 104, 66)).ToArray(),
        8 => GridPositions(art, 2, 4, 104, 58),
        _ => GridPositions(art, 3, 3, 76, 72),
    };

    var colors = new[] { blue, red, green };
    var radius = rank >= 7 ? 20 : 24;
    for (var i = 0; i < positions.Length; i++)
        DrawDot(canvas, positions[i].Item1, positions[i].Item2, radius, colors[i % colors.Length]);
}

static void DrawDot(SKCanvas canvas, float x, float y, float radius, SKColor color)
{
    using var outer = new SKPaint { Color = color, IsAntialias = true };
    canvas.DrawCircle(x, y, radius, outer);
    using var ring = new SKPaint { Color = new SKColor(255, 255, 255, 210), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
    canvas.DrawCircle(x, y, radius * 0.78f, ring);
    using var center = new SKPaint { Color = new SKColor(250, 246, 230), IsAntialias = true };
    canvas.DrawCircle(x, y, radius * 0.58f, center);
    using var highlight = new SKPaint { Color = new SKColor(255, 255, 255, 150), IsAntialias = true };
    canvas.DrawCircle(x - radius * 0.22f, y - radius * 0.24f, radius * 0.16f, highlight);
}

void DrawWind(SKCanvas canvas, int rank, SKRect art, SKTypeface typeface)
{
    DrawText(canvas, windGlyphs[rank - 1], art.MidX, art.MidY, 144, blue, typeface);
    using var mark = new SKPaint { Color = rank == 1 ? red : gold, IsAntialias = true };
    canvas.DrawRoundRect(new SKRect(art.Left + 14, art.Top + 14, art.Left + 44, art.Top + 44), 4, 4, mark);
}

void DrawDragon(SKCanvas canvas, int rank, SKRect art, SKTypeface typeface)
{
    if (rank == 1)
    {
        DrawText(canvas, "中", art.MidX, art.MidY, 152, red, typeface);
        return;
    }
    if (rank == 2)
    {
        DrawText(canvas, "發", art.MidX, art.MidY, 148, green, typeface);
        return;
    }

    var frame = new SKRect(art.Left + 24, art.Top + 24, art.Right - 24, art.Bottom - 24);
    using var outer = new SKPaint { Color = blue, Style = SKPaintStyle.Stroke, StrokeWidth = 14, IsAntialias = true };
    canvas.DrawRoundRect(frame, 8, 8, outer);
    using var inner = new SKPaint { Color = new SKColor(255, 255, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true };
    canvas.DrawRoundRect(new SKRect(frame.Left + 20, frame.Top + 20, frame.Right - 20, frame.Bottom - 20), 6, 6, inner);
    using var dot = new SKPaint { Color = blue, IsAntialias = true };
    canvas.DrawCircle(art.MidX, art.MidY, 16, dot);
}

void DrawFlower(SKCanvas canvas, int rank, SKRect art, SKTypeface typeface)
{
    var glyphs = new[] { "梅", "蘭", "菊", "竹" };
    var colors = new[]
    {
        new SKColor(190, 74, 120),
        new SKColor(132, 74, 160),
        new SKColor(196, 138, 34),
        new SKColor(46, 116, 70),
    };
    var color = colors[rank - 1];
    DrawText(canvas, glyphs[rank - 1], art.MidX, art.Top + 100, 96, color, typeface);

    var center = new SKPoint(art.MidX, art.Bottom - 64);
    if (rank == 4)
    {
        using var stem = new SKPaint { Color = color, StrokeWidth = 6, IsAntialias = true };
        canvas.DrawLine(center.X, center.Y + 34, center.X, center.Y - 42, stem);
        DrawLeaf(canvas, center.X - 26, center.Y - 12, 44, 20, color);
        DrawLeaf(canvas, center.X + 26, center.Y + 8, 44, 20, color);
        return;
    }

    var petals = rank == 3 ? 8 : 5;
    using var petal = new SKPaint { Color = color, IsAntialias = true };
    for (var i = 0; i < petals; i++)
    {
        var angle = i * MathF.PI * 2f / petals;
        var x = center.X + MathF.Cos(angle) * (rank == 3 ? 34 : 30);
        var y = center.Y + MathF.Sin(angle) * (rank == 3 ? 34 : 30);
        canvas.DrawCircle(x, y, rank == 3 ? 15 : 20, petal);
    }
    using var core = new SKPaint { Color = new SKColor(240, 188, 68), IsAntialias = true };
    canvas.DrawCircle(center, 16, core);
}

void DrawSeason(SKCanvas canvas, int rank, SKRect art, SKTypeface typeface)
{
    var glyphs = new[] { "春", "夏", "秋", "冬" };
    var colors = new[]
    {
        new SKColor(46, 126, 72),
        new SKColor(196, 120, 32),
        new SKColor(174, 92, 42),
        new SKColor(60, 104, 158),
    };
    var color = colors[rank - 1];
    DrawText(canvas, glyphs[rank - 1], art.MidX, art.Top + 100, 96, color, typeface);

    var center = new SKPoint(art.MidX, art.Bottom - 66);
    using var paint = new SKPaint { Color = color, IsAntialias = true };
    if (rank == 1)
    {
        using var stem = new SKPaint { Color = color, StrokeWidth = 6, IsAntialias = true };
        canvas.DrawLine(center.X, center.Y + 36, center.X, center.Y - 38, stem);
        DrawLeaf(canvas, center.X - 26, center.Y - 12, 44, 20, color);
        DrawLeaf(canvas, center.X + 26, center.Y + 8, 44, 20, color);
    }
    else if (rank == 2)
    {
        canvas.DrawCircle(center, 28, paint);
        using var ray = new SKPaint { Color = color, StrokeWidth = 5, IsAntialias = true };
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI * 2f / 8f;
            canvas.DrawLine(
                center.X + MathF.Cos(angle) * 38,
                center.Y + MathF.Sin(angle) * 38,
                center.X + MathF.Cos(angle) * 54,
                center.Y + MathF.Sin(angle) * 54,
                ray);
        }
    }
    else if (rank == 3)
    {
        canvas.Save();
        canvas.RotateDegrees(-24, center.X, center.Y);
        canvas.DrawOval(new SKRect(center.X - 40, center.Y - 22, center.X + 40, center.Y + 22), paint);
        canvas.Restore();
        using var vein = new SKPaint { Color = new SKColor(255, 244, 214, 180), StrokeWidth = 3, IsAntialias = true };
        canvas.DrawLine(center.X - 28, center.Y + 2, center.X + 28, center.Y - 2, vein);
    }
    else
    {
        using var flake = new SKPaint { Color = color, StrokeWidth = 6, IsAntialias = true };
        for (var i = 0; i < 6; i++)
        {
            var angle = i * MathF.PI / 3f;
            canvas.DrawLine(center.X, center.Y, center.X + MathF.Cos(angle) * 46, center.Y + MathF.Sin(angle) * 46, flake);
        }
        using var core = new SKPaint { Color = new SKColor(245, 250, 255), IsAntialias = true };
        canvas.DrawCircle(center, 14, core);
    }
}

static (float, float)[] GridPositions(SKRect art, int columns, int rows, float stepX, float stepY)
{
    var result = new List<(float, float)>(columns * rows);
    var totalWidth = (columns - 1) * stepX;
    var totalHeight = (rows - 1) * stepY;
    var left = art.MidX - totalWidth / 2f;
    var top = art.MidY - totalHeight / 2f;
    for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            result.Add((left + column * stepX, top + row * stepY));
    return result.ToArray();
}

static void DrawText(SKCanvas canvas, string text, float x, float centerY, float size, SKColor color, SKTypeface typeface)
{
    using var font = new SKFont(typeface, size);
    using var paint = new SKPaint { Color = color, IsAntialias = true };
    var metrics = font.Metrics;
    var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2f;
    canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, paint);
}

internal sealed record TileFaceSpec(string Group, int Rank, string Label);
