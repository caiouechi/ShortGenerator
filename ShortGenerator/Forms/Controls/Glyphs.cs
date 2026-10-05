using System.Drawing.Drawing2D;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// The app's icon set, drawn as thin rounded strokes on a 24-unit grid and scaled to the box asked for. Vector
/// icons stay crisp at any DPI and take the colour of their state (brand purple at rest, white on a filled button,
/// muted when disabled), which is what keeps the toolbars quiet and consistent.
/// </summary>
public static class Glyphs
{
    public static readonly string[] Names =
    {
        "camera", "camera-auto", "camera-switch", "focus", "frame", "image", "image-add", "reset", "copy", "copy-text", "sparkles",
        "trash", "clear", "play", "pause", "split", "video", "plus", "paste", "upload", "check", "settings", "download", "text", "translate", "wand",
    };

    public static void Draw(Graphics g, string name, Rectangle box, Color color, float strokeScale = 1f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = Math.Min(box.Width, box.Height) / 24f;
        g.TranslateTransform(box.X + (box.Width - 24 * s) / 2, box.Y + (box.Height - 24 * s) / 2);
        g.ScaleTransform(s, s);
        using var pen = new Pen(color, 1.75f * strokeScale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        switch (name)
        {
            case "camera": Camera(g, pen); break;
            case "camera-auto": Camera(g, pen); Spark(g, fill, 19f, 5f, 2.6f); break;
            case "camera-switch":
                g.DrawArc(pen, 5, 5, 14, 14, 200, 150); g.DrawArc(pen, 5, 5, 14, 14, 20, 150);
                Arrow(g, pen, 18.5f, 6.5f, 180); Arrow(g, pen, 5.5f, 17.5f, 0);
                break;
            case "focus":
                Corner(g, pen, 4, 4, 1, 1); Corner(g, pen, 20, 4, -1, 1); Corner(g, pen, 4, 20, 1, -1); Corner(g, pen, 20, 20, -1, -1);
                g.FillEllipse(fill, 10f, 10f, 4f, 4f);
                break;
            case "frame":
                Rounded(g, pen, 4, 5, 16, 14, 2.5f);
                g.DrawLine(pen, 7, 16, 11, 11.5f); g.DrawLine(pen, 11, 11.5f, 14, 14.5f); g.DrawLine(pen, 14, 14.5f, 17, 12);
                break;
            case "image":
                Rounded(g, pen, 4, 5, 16, 14, 2.5f);
                g.FillEllipse(fill, 8f, 8f, 3f, 3f);
                g.DrawLine(pen, 6, 17, 12, 11); g.DrawLine(pen, 12, 11, 18, 17);
                break;
            case "image-add":
                Rounded(g, pen, 4, 6, 13, 13, 2.5f);
                g.DrawLine(pen, 6, 17, 10.5f, 12.5f); g.DrawLine(pen, 10.5f, 12.5f, 15, 17);
                g.DrawLine(pen, 19, 3, 19, 9); g.DrawLine(pen, 16, 6, 22, 6);
                break;
            case "reset":
                g.DrawArc(pen, 5, 5, 14, 14, 300, 300);
                Arrow(g, pen, 16.5f, 4.5f, 170);
                break;
            case "copy":
                Rounded(g, pen, 9, 9, 11, 11, 2.5f);
                g.DrawLine(pen, 6, 15, 5, 15); g.DrawArc(pen, 4, 4, 4, 4, 180, 90); g.DrawLine(pen, 6, 4, 13, 4); g.DrawArc(pen, 11, 4, 4, 4, 270, 90); g.DrawLine(pen, 15, 6, 15, 6.5f);
                g.DrawLine(pen, 4, 6, 4, 13); g.DrawArc(pen, 4, 11, 4, 4, 90, 90);
                break;
            case "copy-text":
                Rounded(g, pen, 9, 9, 11, 11, 2.5f);
                g.DrawLine(pen, 12, 13, 17, 13); g.DrawLine(pen, 12, 16, 17, 16);
                g.DrawLine(pen, 4, 6, 4, 13); g.DrawArc(pen, 4, 11, 4, 4, 90, 90); g.DrawArc(pen, 4, 4, 4, 4, 180, 90); g.DrawLine(pen, 6, 4, 13, 4); g.DrawArc(pen, 11, 4, 4, 4, 270, 90);
                break;
            case "sparkles":
                Spark(g, fill, 10f, 11f, 6f); Spark(g, fill, 18.5f, 5.5f, 2.6f); Spark(g, fill, 18f, 17.5f, 2.2f);
                break;
            case "wand":
                g.DrawLine(pen, 5, 19, 15, 9);
                Spark(g, fill, 17.5f, 6.5f, 3.4f); Spark(g, fill, 11f, 5f, 1.8f); Spark(g, fill, 19.5f, 13f, 1.6f);
                break;
            case "trash":
                g.DrawLine(pen, 5, 7, 19, 7); g.DrawLine(pen, 10, 7, 10, 5); g.DrawLine(pen, 10, 5, 14, 5); g.DrawLine(pen, 14, 5, 14, 7);
                g.DrawLine(pen, 7, 7, 8, 19); g.DrawLine(pen, 8, 19, 16, 19); g.DrawLine(pen, 16, 19, 17, 7);
                g.DrawLine(pen, 10.5f, 10.5f, 10.8f, 16); g.DrawLine(pen, 13.5f, 10.5f, 13.2f, 16);
                break;
            case "clear":
                g.DrawEllipse(pen, 4, 4, 16, 16);
                g.DrawLine(pen, 9, 9, 15, 15); g.DrawLine(pen, 15, 9, 9, 15);
                break;
            case "play":
                using (var p = new GraphicsPath())
                {
                    p.AddPolygon(new[] { new PointF(8, 5), new PointF(19, 12), new PointF(8, 19) });
                    g.FillPath(fill, p); g.DrawPath(pen, p);
                }
                break;
            case "pause":
                using (var p = new Pen(color, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) { g.DrawLine(p, 8.5f, 6, 8.5f, 18); g.DrawLine(p, 15.5f, 6, 15.5f, 18); }
                break;
            case "split":
                Rounded(g, pen, 4, 4, 16, 6.5f, 2f); Rounded(g, pen, 4, 13.5f, 16, 6.5f, 2f);
                break;
            case "video":
                Rounded(g, pen, 3.5f, 6, 12, 12, 2.5f);
                g.DrawLine(pen, 15.5f, 10, 20.5f, 7.5f); g.DrawLine(pen, 20.5f, 7.5f, 20.5f, 16.5f); g.DrawLine(pen, 20.5f, 16.5f, 15.5f, 14);
                break;
            case "plus":
                g.DrawLine(pen, 12, 5, 12, 19); g.DrawLine(pen, 5, 12, 19, 12);
                break;
            case "paste":
                Rounded(g, pen, 5, 5, 14, 16, 2.5f);
                Rounded(g, pen, 9, 3, 6, 4, 1.5f);
                g.DrawLine(pen, 9, 12, 15, 12); g.DrawLine(pen, 9, 15.5f, 13, 15.5f);
                break;
            case "upload":
                g.DrawLine(pen, 12, 16, 12, 5); g.DrawLine(pen, 7.5f, 9.5f, 12, 5); g.DrawLine(pen, 16.5f, 9.5f, 12, 5);
                g.DrawLine(pen, 5, 19, 19, 19);
                break;
            case "download":
                g.DrawLine(pen, 12, 4, 12, 15); g.DrawLine(pen, 7.5f, 10.5f, 12, 15); g.DrawLine(pen, 16.5f, 10.5f, 12, 15);
                g.DrawLine(pen, 5, 19, 19, 19);
                break;
            case "check":
                using (var p = new Pen(color, 2.2f * strokeScale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                { g.DrawLine(p, 5.5f, 12.5f, 10, 17); g.DrawLine(p, 10, 17, 18.5f, 7.5f); }
                break;
            case "settings":
                g.DrawEllipse(pen, 9, 9, 6, 6);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, 12 + (float)(Math.Cos(a) * 7.2), 12 + (float)(Math.Sin(a) * 7.2), 12 + (float)(Math.Cos(a) * 9.5), 12 + (float)(Math.Sin(a) * 9.5));
                }
                break;
            case "text":
                g.DrawLine(pen, 5, 7, 19, 7); g.DrawLine(pen, 5, 12, 15, 12); g.DrawLine(pen, 5, 17, 11, 17);
                break;
            case "translate":
                g.DrawLine(pen, 4, 7, 13, 7); g.DrawLine(pen, 8.5f, 4.5f, 8.5f, 7); g.DrawLine(pen, 11, 7, 6, 14.5f); g.DrawLine(pen, 6, 7, 11, 14);
                g.DrawLine(pen, 13, 20, 17, 11); g.DrawLine(pen, 17, 11, 21, 20); g.DrawLine(pen, 14.5f, 17, 19.5f, 17);
                break;
            default:
                g.DrawEllipse(pen, 7, 7, 10, 10);
                break;
        }
        g.Restore(state);
    }

    private static void Camera(Graphics g, Pen pen)
    {
        // body with a raised top for the viewfinder, lens in the middle
        using var p = new GraphicsPath();
        p.AddLine(4, 9, 8, 9); p.AddLine(8, 9, 9.5f, 6.5f); p.AddLine(9.5f, 6.5f, 14.5f, 6.5f); p.AddLine(14.5f, 6.5f, 16, 9); p.AddLine(16, 9, 20, 9);
        p.AddArc(17.5f, 9, 2.5f, 2.5f, 270, 90); p.AddLine(20, 11.25f, 20, 17.5f); p.AddArc(17.5f, 15.5f, 2.5f, 2.5f, 0, 90);
        p.AddLine(18.75f, 18, 5.25f, 18); p.AddArc(4, 15.5f, 2.5f, 2.5f, 90, 90); p.AddLine(4, 16.75f, 4, 10.25f); p.AddArc(4, 9, 2.5f, 2.5f, 180, 90);
        p.CloseFigure();
        g.DrawPath(pen, p);
        g.DrawEllipse(pen, 9.25f, 10.25f, 5.5f, 5.5f);
    }

    /// <summary>A four-point star, the "automatic" and "AI" marker.</summary>
    private static void Spark(Graphics g, Brush fill, float cx, float cy, float r)
    {
        using var p = new GraphicsPath();
        float k = r * 0.28f;
        p.AddPolygon(new[]
        {
            new PointF(cx, cy - r), new PointF(cx + k, cy - k), new PointF(cx + r, cy), new PointF(cx + k, cy + k),
            new PointF(cx, cy + r), new PointF(cx - k, cy + k), new PointF(cx - r, cy), new PointF(cx - k, cy - k),
        });
        g.FillPath(fill, p);
    }

    private static void Arrow(Graphics g, Pen pen, float x, float y, float angleDeg)
    {
        double a = angleDeg * Math.PI / 180;
        float dx = (float)Math.Cos(a) * 3.2f, dy = (float)Math.Sin(a) * 3.2f;
        g.DrawLine(pen, x, y, x + dx, y + dy);
        g.DrawLine(pen, x, y, x - dy, y + dx);
    }

    private static void Corner(Graphics g, Pen pen, float x, float y, int sx, int sy)
    {
        g.DrawLine(pen, x, y, x + 4 * sx, y);
        g.DrawLine(pen, x, y, x, y + 4 * sy);
    }

    private static void Rounded(Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90); p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        g.DrawPath(pen, p);
    }
}
