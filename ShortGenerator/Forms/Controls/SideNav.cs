using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShortGenerator.Forms.Controls;

public enum StepState { Pending, Ready, Done }

/// <summary>
/// Left navigation with the workflow steps. Each step shows its number, a label, a one-line hint and a
/// state dot (pending / ready / done). Painted over the brand nebula artwork.
/// </summary>
public sealed class SideNav : Control
{
    public sealed class Item
    {
        public string Label { get; set; } = "";
        public string Hint { get; set; } = "";
        public string Glyph { get; set; } = "";
        public StepState State { get; set; } = StepState.Pending;
    }

    private readonly List<Item> _items = new();
    private int _selected;
    private int _hover = -1;
    private readonly Image? _bg = Theme.LoadImage("sidebar-bg.png");
    private readonly Image? _mark = Theme.LoadImage("galiluna-icon.png");
    private readonly Image? _logo = Theme.LoadImage("galiluna-logo.png");

    public event EventHandler? SelectedIndexChanged;
    /// <summary>Raised when the Settings entry in the footer is clicked.</summary>
    public event EventHandler? SettingsClicked;

    private const int ItemH = 54, ListTop = 104, Side = 12;

    public SideNav()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Width = 236;
        Dock = DockStyle.Left;
        Cursor = Cursors.Default;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Item> Items => _items;

    public Item Add(string label, string hint, string glyph)
    {
        var it = new Item { Label = label, Hint = hint, Glyph = glyph };
        _items.Add(it);
        Invalidate();
        return it;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _items.Count || value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetState(int index, StepState state)
    {
        if (index >= 0 && index < _items.Count) { _items[index].State = state; Invalidate(); }
    }

    private Rectangle ItemRect(int i) => new(Side, ListTop + i * (ItemH + 4), Width - Side * 2, ItemH);
    /// <summary>Footer entry (Settings): a utility, not a workflow step, so it sits apart at the bottom.</summary>
    private Rectangle FooterRect => new(Side, Height - 74, Width - Side * 2, 44);
    private const int FooterIndex = -2;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(e.Location)) { h = i; break; }
        if (h < 0 && FooterRect.Contains(e.Location)) h = FooterIndex;
        if (h != _hover) { _hover = h; Cursor = h != -1 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(e.Location)) { SelectedIndex = i; break; }
        if (FooterRect.Contains(e.Location)) SettingsClicked?.Invoke(this, EventArgs.Empty);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // artwork: bright white with a pastel aurora at the foot; cover-fit, anchored to the bottom
        using (var bg = new SolidBrush(Theme.Elevated)) g.FillRectangle(bg, ClientRectangle);
        if (_bg is not null)
        {
            double scale = Math.Max(Width / (double)_bg.Width, Height / (double)_bg.Height);
            int w = (int)(_bg.Width * scale), h = (int)(_bg.Height * scale);
            g.DrawImage(_bg, new Rectangle((Width - w) / 2, Height - h, w, h));
        }
        using (var edge = new Pen(Theme.Border)) g.DrawLine(edge, Width - 1, 0, Width - 1, Height);

        // brand: mark and wordmark on the plain surface, a tracked product label underneath. No card, no glow:
        // the quiet background carries the logo by itself.
        int bx = Side + 8, by = 22;
        if (_mark is not null) { g.DrawImage(_mark, new Rectangle(bx, by, 34, 34)); bx += 42; }
        if (_logo is not null)
        {
            int lh = 22, lw = (int)(_logo.Width * (lh / (double)_logo.Height));
            g.DrawImage(_logo, new Rectangle(bx, by + 6, Math.Min(lw, Width - bx - Side - 8), lh));
        }
        using (var f = Theme.Body(7f))
        {
            const string label = "SHORT GENERATOR";
            int lx = Side + 8, ly = by + 44;
            foreach (var ch in label)
            {
                var text = ch.ToString();
                TextRenderer.DrawText(g, text, f, new Point(lx, ly), Theme.TextMuted, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                lx += TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width + 3;
            }
        }

        // items: a small step badge, the label and a hint. Selection is a soft tint with a thin gradient
        // accent; nothing else competes with the text.
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = ItemRect(i);
            bool sel = i == _selected, hov = i == _hover;
            if (sel || hov)
            {
                using var path = FancyButton.Rounded(r, 10);
                if (sel)
                {
                    // a lifted white card with a whisper of shadow, then the brand accent
                    using var shadow = FancyButton.Rounded(new Rectangle(r.X, r.Y + 2, r.Width, r.Height), 10);
                    using var sb = new SolidBrush(Color.FromArgb(18, Theme.Purple));
                    g.FillPath(sb, shadow);
                    using var fill = new SolidBrush(Color.White);
                    g.FillPath(fill, path);
                    using var rim = new Pen(Theme.Border);
                    g.DrawPath(rim, path);
                }
                else
                {
                    using var fill = new SolidBrush(Theme.SurfaceSoft);
                    g.FillPath(fill, path);
                }
                if (sel)
                {
                    using var accent = new LinearGradientBrush(new Rectangle(r.X, r.Y, 3, r.Height), Theme.CosmicBlue, Theme.Nebula, 90f);
                    using var bar = FancyButton.Rounded(new Rectangle(r.X, r.Y + 14, 3, r.Height - 28), 1);
                    g.FillPath(accent, bar);
                }
            }

            var badge = new Rectangle(r.X + 16, r.Y + (r.Height - 24) / 2, 24, 24);
            DrawBadge(g, badge, it.State, sel, (i + 1).ToString());

            int tx = badge.Right + 14;
            using var lf = Theme.HeadingFont(9.5f);
            using var hf = Theme.Body(7.5f);
            var labelColor = sel ? Theme.Heading : it.State == StepState.Pending ? Theme.TextMuted : Theme.TextSecondary;
            TextRenderer.DrawText(g, it.Label, lf, new Rectangle(tx, r.Y + 9, r.Right - tx - 12, 20), labelColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, it.Hint, hf, new Rectangle(tx, r.Y + 29, r.Right - tx - 12, 16), Color.FromArgb(sel ? 255 : 190, Theme.TextMuted), TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // footer: Settings, drawn like an item, separated by a hairline
        var fr = FooterRect;
        using (var sep = new Pen(Theme.Border)) g.DrawLine(sep, Side + 8, fr.Y - 12, Width - Side - 8, fr.Y - 12);
        if (_hover == FooterIndex)
        {
            using var fpath = FancyButton.Rounded(fr, 10);
            using var fill = new SolidBrush(Theme.SurfaceSoft);
            g.FillPath(fill, fpath);
        }
        var gear = new Rectangle(fr.X + 16, fr.Y + (fr.Height - 24) / 2, 24, 24);
        using (var ring = new Pen(_hover == FooterIndex ? Theme.Purple : Theme.BorderStrong, 1f)) g.DrawEllipse(ring, gear);
        using (var gf = Theme.IconFont(9.5f))
            TextRenderer.DrawText(g, "", gf, gear, _hover == FooterIndex ? Theme.PurpleDeep : Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var sf = Theme.HeadingFont(9.5f))
            TextRenderer.DrawText(g, "Settings", sf, new Rectangle(gear.Right + 14, fr.Y, fr.Right - gear.Right - 24, fr.Height),
                _hover == FooterIndex ? Theme.PurpleDeep : Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        using (var ff = Theme.Body(7f))
            TextRenderer.DrawText(g, "galiluna.com", ff, new Rectangle(0, Height - 22, Width, 16), Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    /// <summary>Done: gradient disc with a check. Ready: thin violet ring. Pending: faint ring. Numbers stay small.</summary>
    private static void DrawBadge(Graphics g, Rectangle badge, StepState state, bool selected, string number)
    {
        if (state == StepState.Done)
        {
            using var cb = new LinearGradientBrush(badge, Theme.CosmicBlue, Theme.Nebula, 45f);
            g.FillEllipse(cb, badge);
            using var check = Theme.IconFont(8.5f);
            TextRenderer.DrawText(g, "", check, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        var ringColor = state == StepState.Ready ? Theme.Purple : selected ? Theme.BorderStrong : Theme.Border;
        using var ring = new Pen(ringColor, state == StepState.Ready ? 1.5f : 1f);
        g.DrawEllipse(ring, badge);
        using var nf = Theme.HeadingFont(8.5f);
        var numColor = selected ? Theme.Heading : state == StepState.Ready ? Theme.PurpleDeep : Theme.TextMuted;
        TextRenderer.DrawText(g, number, nf, badge, numColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
