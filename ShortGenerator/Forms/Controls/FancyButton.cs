using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShortGenerator.Forms.Controls;

public enum ButtonKind { Primary, Ghost, Subtle, Danger }

/// <summary>
/// Owner-drawn button in the Galiluna language: rounded, gradient primary with a soft glow on hover,
/// quiet ghost / subtle variants for secondary actions, optional Segoe Fluent icon glyph.
/// Drop-in replacement for Button (same events and properties).
/// </summary>
public class FancyButton : Button
{
    private bool _hover, _down;
    private ButtonKind _kind = ButtonKind.Ghost;
    private string? _glyph;

    public FancyButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Height = 34;
        Cursor = Cursors.Hand;
        Font = Theme.Body(9.5f, FontStyle.Bold);
        UseVisualStyleBackColor = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    /// <summary>Optional icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets code point) drawn before the text.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // parent background (transparent look)
        if (Parent is not null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded(rect, Radius);

        switch (_kind)
        {
            case ButtonKind.Primary:
                if (!Enabled)
                {
                    using var dis = new SolidBrush(Color.FromArgb(70, Theme.Purple));
                    g.FillPath(dis, path);
                }
                else
                {
                    if (_hover && !_down)
                    {
                        // soft glow
                        using var glowPath = Rounded(new Rectangle(-2, -1, Width + 3, Height + 3), Radius + 2);
                        using var glow = new SolidBrush(Color.FromArgb(60, Theme.Nebula));
                        g.FillPath(glow, glowPath);
                    }
                    var c1 = _down ? Theme.PurpleDeep : Theme.CosmicBlue;
                    var c2 = _down ? Theme.Purple : (_hover ? Theme.Nebula : Theme.Purple);
                    using var grad = new LinearGradientBrush(rect, c1, c2, 20f);
                    g.FillPath(grad, path);
                    using var hi = new Pen(Color.FromArgb(_hover ? 90 : 50, 255, 255, 255));
                    g.DrawPath(hi, path);
                }
                break;

            case ButtonKind.Danger:
                using (var b = new SolidBrush(Enabled ? (_hover ? Color.FromArgb(230, 60, 80) : Theme.Danger) : Color.FromArgb(70, Theme.Danger))) g.FillPath(b, path);
                break;

            case ButtonKind.Subtle:
                using (var b = new SolidBrush(_down ? Theme.SurfaceStrong : (_hover ? Theme.Surface : Theme.Elevated))) g.FillPath(b, path);
                using (var p = new Pen(_hover ? Theme.BorderStrong : Theme.Border)) g.DrawPath(p, path);
                break;

            default: // Ghost
                if (_hover || _down)
                    using (var b = new SolidBrush(Color.FromArgb(_down ? 40 : 22, 255, 255, 255))) g.FillPath(b, path);
                using (var p = new Pen(_hover ? Theme.BorderHover : Theme.BorderStrong)) g.DrawPath(p, path);
                break;
        }

        var fore = _kind is ButtonKind.Primary or ButtonKind.Danger
            ? (Enabled ? Color.White : Color.FromArgb(200, 255, 255, 255))
            : (Enabled ? (_hover ? Color.White : Theme.TextSecondary) : Theme.TextMuted);

        // layout: [glyph] text, centered
        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int glyphW = 0;
        Font? glyphFont = null;
        if (!string.IsNullOrEmpty(_glyph))
        {
            glyphFont = Theme.IconFont(Font.Size + 2);
            glyphW = TextRenderer.MeasureText(g, _glyph, glyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + 8;
        }
        int total = textSize.Width + glyphW;
        int x = Math.Max(6, (Width - total) / 2);
        if (glyphFont is not null)
        {
            TextRenderer.DrawText(g, _glyph, glyphFont, new Rectangle(x, 0, glyphW - 8, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += glyphW;
            glyphFont.Dispose();
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - 4, Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            using var fp = new Pen(Color.FromArgb(120, Theme.Nebula)) { DashStyle = DashStyle.Dot };
            using var fpath = Rounded(new Rectangle(2, 2, Width - 5, Height - 5), Radius - 2);
            g.DrawPath(fp, fpath);
        }
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
