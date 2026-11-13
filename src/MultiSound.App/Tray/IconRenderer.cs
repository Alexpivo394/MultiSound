using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToadDesign;

namespace MultiSound.App.Tray;

/// <summary>Draws a ToadDesign icon into a Windows icon for the notification area.</summary>
internal static class IconRenderer
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x37, 0xB2, 0x4D));

    /// <remarks>The caller owns the icon and its handle; dispose it with <see cref="Destroy"/>.</remarks>
    public static System.Drawing.Icon Render(Symbol symbol, int size)
    {
        var icon = new ToadDesign.Icon { Symbol = symbol, Width = size, Height = size, Foreground = AccentBrush, StrokeThickness = 2.25 };
        icon.Measure(new Size(size, size));
        icon.Arrange(new Rect(0, 0, size, size));

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(icon);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;

        using var drawing = new System.Drawing.Bitmap(png);
        return System.Drawing.Icon.FromHandle(drawing.GetHicon());
    }

    public static void Destroy(System.Drawing.Icon icon)
    {
        var handle = icon.Handle;
        icon.Dispose();
        DestroyIcon(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
