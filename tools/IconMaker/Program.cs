using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// The MultiSound icon: a green rounded square (ToadDesign accent) with white headphones in front and a second,
// translucent pair behind — one sound, several headphones. Drawn on a 256-unit canvas and rendered per size.

var output = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/MultiSound.App/Assets"));
Directory.CreateDirectory(output);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var frames = sizes.Select(size => (size, png: Png(Render(size)))).ToList();
WriteIco(Path.Combine(output, "MultiSound.ico"), frames);
File.WriteAllBytes(Path.Combine(output, "MultiSound.png"), frames.Single(f => f.size == 256).png);
Console.WriteLine($"Wrote {Path.Combine(output, "MultiSound.ico")} ({string.Join(", ", sizes)} px)");

static BitmapSource Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
        Draw(dc, size);
        dc.Pop();
    }
    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    return bitmap;
}

static void Draw(DrawingContext dc, int size)
{
    var background = new LinearGradientBrush(Color.FromRgb(0x51, 0xCF, 0x66), Color.FromRgb(0x2B, 0x8A, 0x3E), new Point(0, 0), new Point(1, 1));
    dc.DrawRoundedRectangle(background, null, new Rect(8, 8, 240, 240), 56, 56);

    // Centre the pair of headphones (their union is offset down-left) and leave some air at the edges.
    var scale = size >= 32 ? 0.9 : 1.12;
    dc.PushTransform(new ScaleTransform(scale, scale, 128, 128));
    dc.PushTransform(new TranslateTransform(size >= 32 ? 4 : 0, size >= 32 ? -25 : -16));

    // Tiny sizes keep only the front pair, so the glyph stays readable.
    if (size >= 32)
    {
        dc.PushTransform(new TranslateTransform(42, -26));
        dc.PushTransform(new ScaleTransform(0.62, 0.62, 128, 150));
        Headphones(dc, new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), size);
        dc.Pop();
        dc.Pop();
        dc.PushTransform(new TranslateTransform(-18, 14));
    }
    else
    {
        dc.PushTransform(new TranslateTransform(0, 6));
    }
    Headphones(dc, Brushes.White, size);
    dc.Pop();
    dc.Pop();
    dc.Pop();
}

static void Headphones(DrawingContext dc, Brush brush, int size)
{
    var bandThickness = size <= 24 ? 26 : 20;
    var band = new Pen(brush, bandThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    dc.DrawGeometry(null, band, Geometry.Parse("M 62,150 A 66,66 0 0 1 194,150"));

    // Ear cups: a rounded pad on each side of the band's ends.
    dc.DrawRoundedRectangle(brush, null, new Rect(40, 132, 46, 78), 18, 18);
    dc.DrawRoundedRectangle(brush, null, new Rect(170, 132, 46, 78), 18, 18);
}

static byte[] Png(BitmapSource bitmap)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}

// ICO with PNG-compressed images (supported since Windows Vista).
static void WriteIco(string path, IReadOnlyList<(int size, byte[] png)> frames)
{
    using var file = new BinaryWriter(File.Create(path));
    file.Write((ushort)0);
    file.Write((ushort)1);
    file.Write((ushort)frames.Count);
    var offset = 6 + 16 * frames.Count;
    foreach (var (size, png) in frames)
    {
        file.Write((byte)(size >= 256 ? 0 : size));
        file.Write((byte)(size >= 256 ? 0 : size));
        file.Write((byte)0);
        file.Write((byte)0);
        file.Write((ushort)1);
        file.Write((ushort)32);
        file.Write(png.Length);
        file.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in frames) file.Write(png);
}
