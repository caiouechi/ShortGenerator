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
    private readonly Image? _logo = Theme.LoadImage("galiluna-logo-light.png");

    public event EventHandler? SelectedIndexChanged;

    private const int HeaderH = 84, ItemH = 62, ListTop = 100, Side = 12;

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

    private Rectangle ItemRect(int i) => new(Side, ListTop + i * (ItemH + 6), Width - Side * 2, ItemH);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(e.Location)) { h = i; break; }
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(e.Location)) { SelectedIndex = i; break; }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // artwork, cover-fit, with a darkening overlay so text stays readable
        using (var bg = new SolidBrush(Theme.DarkBg)) g.FillRectangle(bg, ClientRectangle);
        if (_bg is not null)
        {
            double scale = Math.Max(Width / (double)_bg.Width, Height / (double)_bg.Height);
            int w = (int)(_bg.Width * scale), h = (int)(_bg.Height * scale);
            g.DrawImage(_bg, new Rectangle((Width - w) / 2, Height - h, w, h));
            using var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(120, Theme.DarkBg), Color.FromArgb(40, Theme.DarkBg), 90f);
            g.FillRectangle(shade, ClientRectangle);
        }
        using (var edge = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(edge, Width - 1, 0, Width - 1, Height);

        // brand
        int x = Side + 4, y = 18;
        if (_mark is not null) { g.DrawImage(_mark, new Rectangle(x, y, 40, 40)); x += 48; }
        if (_logo is not null)
        {
            int lh = 26, lw = (int)(_logo.Width * (lh / (double)_logo.Height));
            g.DrawImage(_logo, new Rectangle(x, y + 7, Math.Min(lw, Width - x - Side), lh));
        }
        using (var f = Theme.Body(8f))
            TextRenderer.DrawText(g, "SHORT GENERATOR", f, new Point(Side + 6, 66), Color.FromArgb(200, Theme.DarkTextMuted), TextFormatFlags.NoPrefix);

        // items
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = ItemRect(i);
            bool sel = i == _selected, hov = i == _hover;
            using var path = FancyButton.Rounded(r, 12);
            if (sel)
            {
                using var fill = new SolidBrush(Color.FromArgb(170, Theme.DarkSurfaceStrong));
                g.FillPath(fill, path);
                using var accent = new LinearGradientBrush(new Rectangle(r.X, r.Y, 4, r.Height), Theme.CosmicBlue, Theme.Nebula, 90f);
                using var bar = FancyButton.Rounded(new Rectangle(r.X, r.Y + 12, 4, r.Height - 24), 2);
                g.FillPath(accent, bar);
                using var border = new Pen(Color.FromArgb(70, 255, 255, 255));
                g.DrawPath(border, path);
            }
            else if (hov)
            {
                using var fill = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
                g.FillPath(fill, path);
            }

            // step circle
            var circle = new Rectangle(r.X + 14, r.Y + (r.Height - 30) / 2, 30, 30);
            if (it.State == StepState.Done)
            {
                using var cb = new LinearGradientBrush(circle, Theme.CosmicBlue, Theme.Nebula, 45f);
                g.FillEllipse(cb, circle);
                using var check = Theme.IconFont(11f);
                TextRenderer.DrawText(g, "", check, circle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                using var cb = new SolidBrush(Color.FromArgb(sel ? 90 : 40, 255, 255, 255));
                g.FillEllipse(cb, circle);
                using var cp = new Pen(Color.FromArgb(sel ? 200 : 110, 255, 255, 255), 1.2f);
                g.DrawEllipse(cp, circle);
                using var nf = Theme.HeadingFont(10f);
                TextRenderer.DrawText(g, (i + 1).ToString(), nf, circle, sel ? Color.White : Theme.DarkTextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // texts
            int tx = circle.Right + 12;
            using var lf = Theme.HeadingFont(10f);
            using var hf = Theme.Body(8f);
            TextRenderer.DrawText(g, it.Label, lf, new Rectangle(tx, r.Y + 12, r.Right - tx - 22, 20), sel ? Color.White : Theme.DarkTextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, it.Hint, hf, new Rectangle(tx, r.Y + 33, r.Right - tx - 22, 18), Color.FromArgb(sel ? 230 : 170, Theme.DarkTextMuted), TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // state dot
            var dotColor = it.State switch { StepState.Done => ColorTranslator.FromHtml("#34D399"), StepState.Ready => Theme.Nebula, _ => Color.FromArgb(90, 255, 255, 255) };
            using var dot = new SolidBrush(dotColor);
            g.FillEllipse(dot, r.Right - 16, r.Y + r.Height / 2 - 4, 8, 8);
        }

        // footer hint
        using (var ff = Theme.Body(7.5f))
            TextRenderer.DrawText(g, "galiluna.com", ff, new Rectangle(0, Height - 26, Width, 18), Color.FromArgb(150, Theme.DarkTextMuted), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }
}
