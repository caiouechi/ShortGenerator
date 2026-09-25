using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ShortGenerator.Models;

namespace ShortGenerator.Forms;

/// <summary>Draws an approximate preview of a caption style on a 9:16 mock frame (GDI+, no ffmpeg needed).</summary>
public sealed class CaptionPreview : Control
{
    private CaptionStyle _style = CaptionStyle.All[0];
    private int _fontSizeOverride;
    private string _sampleText = "This is how your captions look";
    private int _wordsPerCaption = 3;

    public CaptionPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public void Update(CaptionStyle style, int fontSizeOverride, int wordsPerCaption, string? sampleText = null)
    {
        _style = style;
        _fontSizeOverride = fontSizeOverride;
        _wordsPerCaption = Math.Max(1, wordsPerCaption);
        if (!string.IsNullOrWhiteSpace(sampleText)) _sampleText = sampleText;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);

        // 9:16 frame centred in the control
        int frameH = Height - 8;
        int frameW = (int)(frameH * 9 / 16.0);
        if (frameW > Width - 8) { frameW = Width - 8; frameH = (int)(frameW * 16 / 9.0); }
        var frame = new Rectangle((Width - frameW) / 2, (Height - frameH) / 2, frameW, frameH);

        using (var bg = new LinearGradientBrush(frame, Color.FromArgb(40, 44, 70), Color.FromArgb(90, 40, 60), 60f))
            g.FillRectangle(bg, frame);
        using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1)) g.DrawRectangle(pen, frame);

        // Fake "subject" so the layout reads as a video
        using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
            g.FillEllipse(b, frame.X + frame.Width * 0.3f, frame.Y + frame.Height * 0.22f, frame.Width * 0.4f, frame.Width * 0.4f);

        double scale = frame.Height / 1920.0;
        float fontPx = (float)((_fontSizeOverride > 0 ? _fontSizeOverride : _style.FontSize) * scale);
        if (fontPx < 6) fontPx = 6;

        var words = _sampleText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(_wordsPerCaption).ToArray();
        var text = string.Join(" ", words);
        if (_style.Uppercase) text = text.ToUpperInvariant();

        var fontStyle = (_style.Bold ? FontStyle.Bold : FontStyle.Regular) | (_style.Italic ? FontStyle.Italic : 0);
        using var font = CreateFont(_style.FontName, fontPx, fontStyle);
        var size = g.MeasureString(text, font);
        int marginV = (int)(_style.MarginV * scale);

        float y = _style.Alignment switch
        {
            8 => frame.Y + marginV,
            5 => frame.Y + (frame.Height - size.Height) / 2,
            _ => frame.Bottom - marginV - size.Height
        };
        float x = frame.X + (frame.Width - size.Width) / 2;

        if (_style.BorderStyle == 3)
        {
            var pad = 8f * (float)scale * 4;
            using var box = new SolidBrush(_style.BackColor);
            g.FillRectangle(box, x - pad, y - pad / 2, size.Width + pad * 2, size.Height + pad);
        }

        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, g.DpiY * font.SizeInPoints / 72f, new PointF(x, y), StringFormat.GenericDefault);

        if (_style.Shadow > 0)
        {
            using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            var m = new Matrix(); m.Translate((float)(_style.Shadow * scale) * 2, (float)(_style.Shadow * scale) * 2);
            using var sp = (GraphicsPath)path.Clone(); sp.Transform(m);
            g.FillPath(shadow, sp);
        }
        if (_style.Outline > 0 && _style.BorderStyle == 1)
        {
            using var pen = new Pen(_style.OutlineColor, (float)(_style.Outline * scale) * 2) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using (var fill = new SolidBrush(_style.PrimaryColor)) g.FillPath(fill, path);

        if (_style.Karaoke && words.Length > 0)
        {
            // Highlight the first word to show the "spoken word" colour.
            var first = _style.Uppercase ? words[0].ToUpperInvariant() : words[0];
            using var hp = new GraphicsPath();
            hp.AddString(first, font.FontFamily, (int)font.Style, g.DpiY * font.SizeInPoints / 72f, new PointF(x, y), StringFormat.GenericDefault);
            using var hb = new SolidBrush(_style.HighlightColor);
            g.FillPath(hb, hp);
        }
    }

    private static Font CreateFont(string family, float px, FontStyle style)
    {
        try { return new Font(family, px, style, GraphicsUnit.Pixel); }
        catch { return new Font("Arial", px, style, GraphicsUnit.Pixel); }
    }
}
