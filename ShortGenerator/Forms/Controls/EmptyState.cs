using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShortGenerator.Forms.Controls;

/// <summary>Centered illustration with a title and a hint, shown when a page has nothing to display yet.</summary>
public sealed class EmptyState : Control
{
    private Image? _image;

    public EmptyState(string imageName, string title, string hint)
    {
        _image = Theme.LoadImage(imageName);
        Title = title;
        Hint = hint;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Elevated;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; }

    public void Set(string title, string hint) { Title = title; Hint = hint; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

        int imgSize = Math.Clamp(Math.Min(Width, Height) / 3, 90, 220);
        int totalH = imgSize + 70;
        int y = Math.Max(10, (Height - totalH) / 2);
        if (_image is not null)
        {
            // soft glow behind the illustration
            var glowRect = new Rectangle((Width - imgSize) / 2 - imgSize / 4, y - imgSize / 6, imgSize + imgSize / 2, imgSize + imgSize / 3);
            using var glowPath = new GraphicsPath(); glowPath.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(glowPath) { CenterColor = Color.FromArgb(70, Theme.Purple), SurroundColors = new[] { Color.FromArgb(0, Theme.Purple) } };
            g.FillPath(glow, glowPath);
            g.DrawImage(_image, new Rectangle((Width - imgSize) / 2, y, imgSize, imgSize));
        }
        y += imgSize + 14;
        using var tf = Theme.HeadingFont(12f);
        using var hf = Theme.Body(9f);
        TextRenderer.DrawText(g, Title, tf, new Rectangle(20, y, Width - 40, 24), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Hint, hf, new Rectangle(Math.Max(20, Width / 6), y + 28, Width - Math.Max(40, Width / 3), 44), Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
