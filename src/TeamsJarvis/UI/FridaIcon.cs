using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TeamsJarvis.UI;

/// <summary>
/// Logo de Frida.ai dibujado por código (sin depender de un .ico externo): un schnauzer
/// estilizado (cejas y barba características) sobre una teja redondeada morada. Se usa para el
/// icono de la bandeja y de las ventanas. Mantiene la API de <c>CreateIcon</c>/<c>CreateImageSource</c>.
/// </summary>
public static class FridaIcon
{
    private static readonly Color Brand = Color.FromArgb(0x62, 0x64, 0xA7);   // morado corporativo
    private static readonly Color BrandDk = Color.FromArgb(0x45, 0x47, 0x82);
    private static readonly Color Fur = Color.FromArgb(0xC9, 0xCC, 0xDA);     // "sal" (gris claro)
    private static readonly Color FurDk = Color.FromArgb(0x41, 0x44, 0x5A);   // "pimienta" (gris oscuro)
    private static readonly Color Beard = Color.FromArgb(0xEC, 0xED, 0xF4);   // barba (casi blanco)
    private static readonly Color Nose = Color.FromArgb(0x25, 0x25, 0x2C);

    public static Bitmap CreateBitmap(int size = 256)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float s = size / 256f;
        RectangleF R(float x, float y, float w, float h) => new(x * s, y * s, w * s, h * s);

        // Teja redondeada de marca (fondo).
        using (var path = RoundedRect(R(12, 12, 232, 232), 54 * s))
        using (var bg = new LinearGradientBrush(
                   new PointF(0, 0), new PointF(0, size), Brand, BrandDk))
            g.FillPath(bg, path);

        // Orejas (triángulos caídos, gris oscuro).
        using (var brush = new SolidBrush(FurDk))
        {
            g.FillPolygon(brush, [P(70, 56, s), P(116, 70, s), P(88, 112, s)]);
            g.FillPolygon(brush, [P(186, 56, s), P(140, 70, s), P(168, 112, s)]);
        }

        // Cara (gris claro).
        using (var path = RoundedRect(R(74, 72, 108, 116), 42 * s))
        using (var brush = new SolidBrush(Fur))
            g.FillPath(brush, path);

        // Barba característica (trapecio que se ensancha hacia abajo).
        using (var brush = new SolidBrush(Beard))
        {
            g.FillPolygon(brush, [P(96, 150, s), P(160, 150, s), P(182, 214, s), P(74, 214, s)]);
        }
        // Puente del hocico sobre la barba.
        using (var path = RoundedRect(R(108, 140, 40, 34), 14 * s))
        using (var brush = new SolidBrush(Fur))
            g.FillPath(brush, path);

        // Cejas pobladas (elipses oscuras).
        using (var brush = new SolidBrush(FurDk))
        {
            g.FillEllipse(brush, R(86, 104, 36, 18));
            g.FillEllipse(brush, R(134, 104, 36, 18));
        }

        // Ojos.
        using (var brush = new SolidBrush(Nose))
        {
            g.FillEllipse(brush, R(96, 122, 15, 15));
            g.FillEllipse(brush, R(145, 122, 15, 15));
        }

        // Nariz.
        using (var brush = new SolidBrush(Nose))
            g.FillEllipse(brush, R(110, 150, 36, 24));

        // Un par de trazos de barba para textura.
        using (var pen = new Pen(Color.FromArgb(120, FurDk), 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pen, 118 * s, 182 * s, 112 * s, 208 * s);
            g.DrawLine(pen, 138 * s, 182 * s, 144 * s, 208 * s);
            g.DrawLine(pen, 128 * s, 180 * s, 128 * s, 210 * s);
        }

        return bmp;
    }

    private static PointF P(float x, float y, float s) => new(x * s, y * s);

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Icono para el NotifyIcon del tray.</summary>
    public static Icon CreateIcon()
    {
        using var bmp = CreateBitmap(64);
        var hicon = bmp.GetHicon();
        return (Icon)Icon.FromHandle(hicon).Clone();
    }

    /// <summary>ImageSource para la propiedad Icon de la ventana WPF.</summary>
    public static System.Windows.Media.ImageSource CreateImageSource()
    {
        using var bmp = CreateBitmap(256);
        var hbitmap = bmp.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hbitmap);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
