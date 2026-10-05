using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// The app's check box: a 16 px rounded square with a hairline, filled with the accent and a white vector check when
/// ticked, lavender on hover; the label in the body font. A drop-in CheckBox (same Checked / CheckedChanged), drawn
/// here instead of by Windows so it matches the buttons and cards.
/// </summary>
public class FancyCheck : CheckBox
{
    private bool _hover;
    private const int Box = 16, Gap = 8;

    public FancyCheck()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Padding = new Padding(0, 2, 0, 2);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(Box + (Text.Length > 0 ? Gap + text.Width : 0) + 2, Math.Max(Box, text.Height) + Padding.Vertical + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? Theme.Bg);

        int y = (Height - Box) / 2;
        var box = new Rectangle(1, y, Box - 1, Box - 1);
        using var path = FancyButton.Rounded(box, 4);
        bool on = Checked || CheckState == CheckState.Indeterminate;
        if (on)
        {
            var fill = !Enabled ? Theme.AccentDisabled : _hover ? Theme.AccentHover : Theme.Accent;
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            var inner = box; inner.Inflate(-2, -2);
            if (Checked) Glyphs.Draw(g, "check", inner, Color.White, 0.95f);
            else using (var p = new Pen(Color.White, 2f)) g.DrawLine(p, inner.X + 2, inner.Y + inner.Height / 2f, inner.Right - 2, inner.Y + inner.Height / 2f);
        }
        else
        {
            using var b = new SolidBrush(_hover && Enabled ? Theme.AccentTint : Theme.Elevated);
            g.FillPath(b, path);
            using var p = new Pen(!Enabled ? Theme.Border : _hover ? Theme.Accent : Theme.BorderStrong, 1f);
            g.DrawPath(p, path);
        }

        if (Text.Length > 0)
        {
            var fore = !Enabled ? Theme.TextMuted : ForeColor == SystemColors.ControlText || ForeColor == Color.Empty || ForeColor == Theme.TextSecondary ? Theme.Heading : ForeColor;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Box + Gap, 0, Width - Box - Gap, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (Focused && ShowFocusCues)
        {
            using var fp = new Pen(Color.FromArgb(140, Theme.Accent)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(fp, Box + Gap - 2, 1, Math.Max(2, Width - Box - Gap), Height - 3);
        }
    }
}
