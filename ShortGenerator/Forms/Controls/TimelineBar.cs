using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// The editor's scrub bar: a rounded track with a gradient progress, a round thumb, and small marks where the
/// short was edited (camera cuts in violet, caption positions in blue) so the changes are visible at a glance.
/// Same surface as a TrackBar: Maximum, Value, Scroll, MouseDown / MouseUp.
/// </summary>
public sealed class TimelineBar : Control
{
    public readonly record struct Mark(double Fraction, Color Color, string Tooltip);

    private int _value;
    private readonly List<Mark> _marks = new();
    private bool _dragging;
    private readonly ToolTip _tip = new();

    public event EventHandler? Scroll;

    public TimelineBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Height = 30;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    [DefaultValue(1000)]
    public int Maximum { get; set; } = 1000;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set { var v = Math.Clamp(value, 0, Maximum); if (v != _value) { _value = v; Invalidate(); } }
    }

    public void SetMarks(IEnumerable<Mark> marks)
    {
        _marks.Clear();
        _marks.AddRange(marks.Where(m => m.Fraction >= 0 && m.Fraction <= 1));
        Invalidate();
    }

    private const int Pad = 10;
    private Rectangle Track => new(Pad, Height / 2 - 2, Math.Max(1, Width - Pad * 2), 4);
    private int XOf(double frac) => Track.X + (int)Math.Round(frac * Track.Width);

    private void SetFromMouse(int x)
    {
        double frac = Math.Clamp((x - Track.X) / (double)Track.Width, 0, 1);
        Value = (int)Math.Round(frac * Maximum);
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _dragging = true; SetFromMouse(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromMouse(e.X);
        else
        {
            // hover a mark to read what it is
            var hit = _marks.FirstOrDefault(m => Math.Abs(XOf(m.Fraction) - e.X) <= 5);
            var text = hit == default ? "" : hit.Tooltip;
            if (_tip.GetToolTip(this) != text) _tip.SetToolTip(this, text);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = Track;
        using (var path = FancyButton.Rounded(track, 2))
        using (var bg = new SolidBrush(Theme.BorderStrong)) g.FillPath(bg, path);

        int thumbX = XOf(Maximum > 0 ? _value / (double)Maximum : 0);
        var done = new Rectangle(track.X, track.Y, Math.Max(1, thumbX - track.X), track.Height);
        if (done.Width > 2)
        {
            using var dp = FancyButton.Rounded(done, 2);
            using var grad = new LinearGradientBrush(new Rectangle(track.X, track.Y, Math.Max(2, track.Width), track.Height), Theme.CosmicBlue, Theme.Nebula, 0f);
            g.FillPath(grad, dp);
        }

        // marks: a short vertical tick above the track with a tiny dot on top
        foreach (var m in _marks)
        {
            int x = XOf(m.Fraction);
            using var pen = new Pen(m.Color, 2f);
            g.DrawLine(pen, x, track.Y - 8, x, track.Bottom + 1);
            using var dot = new SolidBrush(m.Color);
            g.FillEllipse(dot, x - 3, track.Y - 13, 6, 6);
        }

        // thumb
        var thumb = new Rectangle(thumbX - 7, track.Y + track.Height / 2 - 7, 14, 14);
        using (var shadow = new SolidBrush(Color.FromArgb(40, Theme.Purple))) g.FillEllipse(shadow, thumbX - 9, thumb.Y - 1, 18, 18);
        using (var fill = new SolidBrush(Color.White)) g.FillEllipse(fill, thumb);
        using (var ring = new Pen(Theme.Purple, 2f)) g.DrawEllipse(ring, thumb);
    }
}
