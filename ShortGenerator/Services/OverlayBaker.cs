using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Pre-draws an image layer at its final pixel size with its style (photo frame, rounded card, circle, plain)
/// and rotation baked in, as a transparent PNG. ffmpeg then only composites that small still image during the
/// layer's window, which keeps rendering cheap however fancy the style is.
/// </summary>
public static class OverlayBaker
{
    /// <summary>Writes the styled PNG and returns its pixel size.</summary>
    public static (int Width, int Height) Bake(ImageOverlay o, int frameWidth, string outPng)
    {
        using var src = Load(o.Path);
        int target = Math.Max(40, (int)Math.Round(frameWidth * Math.Clamp(o.Size, 5, 100) / 100.0));
        using var styled = o.Style switch
        {
            "frame" => Frame(src, target),
            "card" => Card(src, target),
            "circle" => Circle(src, target),
            _ => Plain(src, target),
        };
        using var final = Math.Abs(o.Rotation) > 0.1 ? Rotate(styled, (float)o.Rotation) : new Bitmap(styled);
        final.Save(outPng, ImageFormat.Png);
        return (final.Width, final.Height);
    }

    /// <summary>Loads without locking the file (Image.FromFile keeps it open until disposed).</summary>
    private static Bitmap Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs);
        return new Bitmap(img);
    }

    private static Graphics G(Bitmap b)
    {
        var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        return g;
    }

    private static Bitmap Plain(Bitmap src, int w)
    {
        int h = Math.Max(1, (int)Math.Round(src.Height * (w / (double)src.Width)));
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = G(b);
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return b;
    }

    /// <summary>Polaroid-like: white border, deeper at the bottom, soft shadow.</summary>
    private static Bitmap Frame(Bitmap src, int w)
    {
        int border = (int)Math.Round(w * 0.045), bottom = (int)Math.Round(w * 0.14);
        int iw = w - 2 * border, ih = Math.Max(1, (int)Math.Round(src.Height * (iw / (double)src.Width)));
        int h = ih + border + bottom, m = Margin(w);
        var b = new Bitmap(w + 2 * m, h + 2 * m, PixelFormat.Format32bppArgb);
        using var g = G(b);
        var paper = new Rectangle(m, m, w, h);
        Shadow(g, paper, 6);
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, paper);
        g.DrawImage(src, new Rectangle(m + border, m + border, iw, ih));
        return b;
    }

    /// <summary>Rounded corners, thin white edge, soft shadow.</summary>
    private static Bitmap Card(Bitmap src, int w)
    {
        int h = Math.Max(1, (int)Math.Round(src.Height * (w / (double)src.Width)));
        int m = Margin(w), r = Math.Max(6, (int)Math.Round(w * 0.07)), edge = Math.Max(2, (int)Math.Round(w * 0.012));
        var b = new Bitmap(w + 2 * m, h + 2 * m, PixelFormat.Format32bppArgb);
        using var g = G(b);
        var rect = new Rectangle(m, m, w, h);
        Shadow(g, rect, r);
        using (var path = Rounded(rect, r))
        {
            g.SetClip(path);
            g.DrawImage(src, rect);
            g.ResetClip();
            using var pen = new Pen(Color.White, edge);
            g.DrawPath(pen, path);
        }
        return b;
    }

    /// <summary>Centre square crop in a circle with a white ring: the classic "who is this" portrait.</summary>
    private static Bitmap Circle(Bitmap src, int w)
    {
        int side = Math.Min(src.Width, src.Height);
        var crop = new Rectangle((src.Width - side) / 2, Math.Max(0, (src.Height - side) / 3), side, side);
        int m = Margin(w), ring = Math.Max(3, (int)Math.Round(w * 0.03));
        var b = new Bitmap(w + 2 * m, w + 2 * m, PixelFormat.Format32bppArgb);
        using var g = G(b);
        var rect = new Rectangle(m, m, w, w);
        for (int i = 6; i >= 1; i--)
        {
            using var p = new GraphicsPath();
            p.AddEllipse(Rectangle.Inflate(new Rectangle(rect.X, rect.Y + m / 3, rect.Width, rect.Height), i * m / 8, i * m / 8));
            using var sb = new SolidBrush(Color.FromArgb(10, 0, 0, 0));
            g.FillPath(sb, p);
        }
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            g.SetClip(path);
            g.DrawImage(src, rect, crop, GraphicsUnit.Pixel);
            g.ResetClip();
        }
        using (var pen = new Pen(Color.White, ring)) g.DrawEllipse(pen, Rectangle.Inflate(rect, -ring / 2, -ring / 2));
        return b;
    }

    private static int Margin(int w) => Math.Max(8, (int)Math.Round(w * 0.06));

    /// <summary>Soft drop shadow made of a few translucent, growing rounded rectangles (no blur filter needed).</summary>
    private static void Shadow(Graphics g, Rectangle rect, int radius)
    {
        int m = Math.Max(4, rect.Width / 30);
        var down = new Rectangle(rect.X, rect.Y + m, rect.Width, rect.Height);
        for (int i = 6; i >= 1; i--)
        {
            var r = Rectangle.Inflate(down, i * m / 3, i * m / 3);
            using var path = Rounded(r, radius + i * m / 3);
            using var sb = new SolidBrush(Color.FromArgb(10, 0, 0, 0));
            g.FillPath(sb, path);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Rotates clockwise by the given degrees on a transparent canvas large enough to hold the result.</summary>
    private static Bitmap Rotate(Bitmap b, float degrees)
    {
        double rad = degrees * Math.PI / 180;
        double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));
        int nw = (int)Math.Ceiling(b.Width * c + b.Height * s), nh = (int)Math.Ceiling(b.Width * s + b.Height * c);
        var r = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
        using var g = G(r);
        g.TranslateTransform(nw / 2f, nh / 2f);
        g.RotateTransform(degrees);
        g.DrawImage(b, -b.Width / 2f, -b.Height / 2f, b.Width, b.Height);
        return r;
    }
}
