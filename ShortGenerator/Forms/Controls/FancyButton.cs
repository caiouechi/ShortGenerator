using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShortGenerator.Forms.Controls;

public enum ButtonKind { Primary, Ghost, Subtle, Danger }

/// <summary>
/// The app's button. One quiet language for every action: a soft bright purple fill for the one thing to do on a
/// page, white with a hairline border for everything else, a tint instead of a border for the quietest ones, and a
/// rose tint for destructive actions. Icons are thin vector strokes in the state colour (see <see cref="Glyphs"/>).
/// Hover lifts with a tint and a lavender border; pressing settles the fill; nothing glows, nothing shouts.
/// Drop-in replacement for Button (same events and properties).
/// </summary>
public class FancyButton : Button
{
    private bool _hover, _down;
    private ButtonKind _kind = ButtonKind.Ghost;
    private string? _glyph, _icon;

    public FancyButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Height = 36;
        Margin = new Padding(0, 0, 8, 0); // consistent 8 px gap between neighbouring buttons in toolbars
        Cursor = Cursors.Hand;
        Font = Theme.Body(9.5f, FontStyle.Bold);
        UseVisualStyleBackColor = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    /// <summary>Optional icon glyph (Segoe Fluent Icons code point) drawn before the text, when no vector icon is set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    /// <summary>The vector icon drawn before the text, one of <see cref="Glyphs.Names"/>; takes the state colour.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? IconName { get => _icon; set { _icon = value; Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 11;

    private Image? _picture;
    /// <summary>Optional bitmap icon drawn before the text (kept for pictures that are not in the vector set).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Picture { get => _picture; set { _picture = value; Invalidate(); } }

    /// <summary>Toggle buttons paint their "on" state like a selected chip.</summary>
    protected virtual bool IsOn => false;

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
        if (Parent is not null) g.Clear(Parent.BackColor);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded(rect, Radius);
        bool on = IsOn;

        Color fore;
        switch (_kind)
        {
            case ButtonKind.Primary:
            {
                if (!Enabled)
                {
                    using var dis = new SolidBrush(Theme.AccentDisabled);
                    g.FillPath(dis, path);
                    fore = Color.White;
                    break;
                }
                // a soft shadow under the button, a touch deeper on hover, so it floats rather than glows
                using (var shadowPath = Rounded(new Rectangle(1, _hover ? 3 : 2, Width - 3, Height - 2), Radius))
                using (var shadow = new SolidBrush(Color.FromArgb(_hover ? 70 : 45, Theme.Accent)))
                    g.FillPath(shadow, shadowPath);
                var top = _down ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                var bottom = _down ? Theme.AccentPressed : _hover ? Theme.Accent : Theme.AccentDeep;
                using (var grad = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), top, bottom, 90f))
                    g.FillPath(grad, path);
                using (var hi = new Pen(Color.FromArgb(_hover ? 70 : 45, 255, 255, 255)))
                    g.DrawPath(hi, path);
                fore = Color.White;
                break;
            }
            case ButtonKind.Danger:
            {
                // rose tint: clear about what it does, without a red slab
                var fill = !Enabled ? Theme.DangerTint : _down ? Theme.DangerTintStrong : _hover ? Theme.DangerTintStrong : Theme.DangerTint;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(_hover && Enabled ? Theme.Danger : Theme.DangerBorder)) g.DrawPath(p, path);
                fore = Enabled ? Theme.Danger : Color.FromArgb(120, Theme.Danger);
                break;
            }
            case ButtonKind.Subtle:
            {
                if (_hover || _down || on)
                {
                    using var b = new SolidBrush(_down || on ? Theme.AccentTintStrong : Theme.AccentTint);
                    g.FillPath(b, path);
                }
                fore = Enabled ? (_hover || _down || on ? Theme.AccentDeep : Theme.TextSecondary) : Theme.TextMuted;
                break;
            }
            default: // Ghost: white, hairline border; lavender tint and border on hover; a selected chip when on
            {
                var fill = on ? Theme.AccentTintStrong : _down ? Theme.AccentTintStrong : _hover ? Theme.AccentTint : Theme.Elevated;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                var edge = on ? Theme.Accent : _hover && Enabled ? Theme.AccentBorder : Theme.Border;
                using (var p = new Pen(edge, on ? 1.4f : 1f)) g.DrawPath(p, path);
                fore = Enabled ? (on || _hover || _down ? Theme.AccentDeep : Theme.Heading) : Theme.TextMuted;
                break;
            }
        }

        // layout: [icon] text, centred as one group
        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int iconW = 0;
        Font? glyphFont = null;
        int iconSize = Math.Clamp(Height - 16, 16, 20);
        bool hasText = !string.IsNullOrEmpty(Text);
        int gap = hasText ? 8 : 0;
        if (_icon is not null || _picture is not null) iconW = iconSize + gap;
        else if (!string.IsNullOrEmpty(_glyph))
        {
            glyphFont = Theme.IconFont(Font.Size + 1.5f);
            iconW = TextRenderer.MeasureText(g, _glyph, glyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + gap;
        }
        int total = (hasText ? textSize.Width : 0) + iconW;
        int x = Math.Max(6, (Width - total) / 2);
        var iconColor = Enabled ? (_kind == ButtonKind.Primary ? Color.White : _kind == ButtonKind.Danger ? Theme.Danger : (on || _hover || _down ? Theme.AccentDeep : Theme.Accent)) : Theme.TextMuted;
        if (_icon is not null)
        {
            Glyphs.Draw(g, _icon, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), iconColor);
            x += iconW;
        }
        else if (_picture is not null)
        {
            var dest = new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (Enabled) g.DrawImage(_picture, dest);
            else
            {
                using var faded = new System.Drawing.Imaging.ImageAttributes();
                faded.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
                g.DrawImage(_picture, dest, 0, 0, _picture.Width, _picture.Height, GraphicsUnit.Pixel, faded);
            }
            x += iconW;
        }
        else if (glyphFont is not null)
        {
            TextRenderer.DrawText(g, _glyph, glyphFont, new Rectangle(x, 0, iconW - gap, Height), iconColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += iconW;
            glyphFont.Dispose();
        }
        if (hasText)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - 4, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            using var fp = new Pen(Color.FromArgb(140, Theme.Accent)) { DashStyle = DashStyle.Dot };
            using var fpath = Rounded(new Rectangle(2, 2, Width - 5, Height - 5), Radius - 2);
            g.DrawPath(fp, fpath);
        }
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>
/// A button that stays pressed: a mode switch (Camera mode). Looks like a ghost button; when on, a lavender chip
/// with the purple border. Same Checked / CheckedChanged surface as a CheckBox, so callers need no other change.
/// </summary>
public sealed class FancyToggle : FancyButton
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override bool IsOn => _checked;

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }
}
