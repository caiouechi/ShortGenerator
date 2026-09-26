using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using ShortGenerator.Forms.Controls;

namespace ShortGenerator.Forms;

/// <summary>
/// Galiluna brand foundation (v1.1 guidelines) in its dark theme, translated to WinForms:
/// navy #0B1028, cosmic blue #295BFF, lunar purple #6A38FF, nebula #A855F7.
/// </summary>
public static class Theme
{
    // palette
    public static readonly Color Navy = ColorTranslator.FromHtml("#0B1028");
    public static readonly Color CosmicBlue = ColorTranslator.FromHtml("#295BFF");
    public static readonly Color Purple = ColorTranslator.FromHtml("#6A38FF");
    public static readonly Color PurpleHover = ColorTranslator.FromHtml("#7B4DFF");
    public static readonly Color PurpleDeep = ColorTranslator.FromHtml("#4523C7");
    public static readonly Color Nebula = ColorTranslator.FromHtml("#A855F7");
    public static readonly Color Lavender = ColorTranslator.FromHtml("#C4B5FD");

    // light surfaces (the app theme: this is a product for the public, so the work area is light)
    public static readonly Color Bg = ColorTranslator.FromHtml("#F6F7FC");
    public static readonly Color Elevated = Color.White;
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceStrong = ColorTranslator.FromHtml("#EEF0F8");
    public static readonly Color SurfaceSoft = ColorTranslator.FromHtml("#F4F1FF");
    public static readonly Color Border = ColorTranslator.FromHtml("#E7EAF5");
    public static readonly Color BorderStrong = ColorTranslator.FromHtml("#D8DDED");
    public static readonly Color BorderHover = ColorTranslator.FromHtml("#C9BCFF");
    public static readonly Color SelectionBg = ColorTranslator.FromHtml("#E9E3FF");
    public static readonly Color SelectionFg = ColorTranslator.FromHtml("#0B1028");

    // text
    public static readonly Color Text = ColorTranslator.FromHtml("#0B1028");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#3E4668");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#737B98");
    public static readonly Color Heading = ColorTranslator.FromHtml("#2E3A6E");
    public static readonly Color Link = ColorTranslator.FromHtml("#295BFF");

    // status
    public static readonly Color Success = ColorTranslator.FromHtml("#16794F");
    public static readonly Color Warning = ColorTranslator.FromHtml("#A66A00");
    public static readonly Color Danger = ColorTranslator.FromHtml("#C5283D");

    // the dark side navigation keeps the brand's night palette
    public static readonly Color DarkBg = ColorTranslator.FromHtml("#131834");
    public static readonly Color DarkElevated = ColorTranslator.FromHtml("#191F40");
    public static readonly Color DarkSurfaceStrong = ColorTranslator.FromHtml("#272F60");
    public static readonly Color DarkTextSecondary = ColorTranslator.FromHtml("#C7CBE0");
    public static readonly Color DarkTextMuted = ColorTranslator.FromHtml("#9298B3");

    private static string? _bodyFamily, _headingFamily, _iconFamily, _monoFamily;

    /// <summary>Inter for body, Poppins for headings when installed; Segoe UI otherwise.</summary>
    public static string BodyFamily => _bodyFamily ??= FirstInstalled("Inter", "Segoe UI Variable Text", "Segoe UI");
    public static string HeadingFamily => _headingFamily ??= FirstInstalled("Poppins", "Inter", "Segoe UI Variable Display", "Segoe UI");
    public static string IconFamily => _iconFamily ??= FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
    public static string MonoFamily => _monoFamily ??= FirstInstalled("Cascadia Mono", "Consolas");

    public static Font Body(float size = 9.5f, FontStyle style = FontStyle.Regular) => new(BodyFamily, size, style);
    public static Font HeadingFont(float size = 12f, FontStyle style = FontStyle.Bold) => new(HeadingFamily, size, style);
    public static Font IconFont(float size = 11f) => new(IconFamily, size, FontStyle.Regular);
    public static Font Mono(float size = 8.5f) => new(MonoFamily, size);

    private static string FirstInstalled(params string[] families)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return families.FirstOrDefault(names.Contains) ?? "Segoe UI";
    }

    // ------------------------------------------------------------ assets

    private static readonly Dictionary<string, Image?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static Image? LoadImage(string name)
    {
        if (ImageCache.TryGetValue(name, out var cached)) return cached;
        Image? img = null;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (res is not null)
            {
                using var s = asm.GetManifestResourceStream(res);
                if (s is not null) img = Image.FromStream(s);
            }
        }
        catch { img = null; }
        ImageCache[name] = img;
        return img;
    }

    public static Icon? AppIcon
    {
        get
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
                if (res is null) return null;
                using var s = asm.GetManifestResourceStream(res);
                return s is null ? null : new Icon(s);
            }
            catch { return null; }
        }
    }

    // ------------------------------------------------------------ styling helpers

    /// <summary>Wraps a single-line TextBox in a rounded, bordered field so it matches the buttons.</summary>
    public static Panel WrapInput(TextBox tb, int height = 34)
    {
        var host = new RoundedField { Height = height, Padding = new Padding(12, 0, 12, 0) };
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = Elevated;
        tb.ForeColor = Text;
        tb.Dock = DockStyle.None;
        tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        host.Controls.Add(tb);
        void Place() { tb.Left = host.Padding.Left; tb.Width = host.Width - host.Padding.Horizontal; tb.Top = (host.Height - tb.Height) / 2; }
        host.Resize += (_, _) => Place();
        Place();
        return host;
    }

    private sealed class RoundedField : Panel
    {
        public RoundedField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Elevated;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent?.BackColor ?? Bg)) e.Graphics.FillRectangle(bg, ClientRectangle);
            using var path = FancyButton.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using var fill = new SolidBrush(Elevated);
            e.Graphics.FillPath(fill, path);
            bool focused = Controls.Count > 0 && Controls[0].Focused;
            using var pen = new Pen(focused ? Purple : BorderStrong, focused ? 1.5f : 1f);
            e.Graphics.DrawPath(pen, path);
        }
    }

    public static void Primary(Button b) { if (b is FancyButton f) f.Kind = ButtonKind.Primary; }
    public static void Secondary(Button b) { if (b is FancyButton f) f.Kind = ButtonKind.Ghost; }
    public static void Subtle(Button b) { if (b is FancyButton f) f.Kind = ButtonKind.Subtle; }

    /// <summary>Applies the dark theme to every control in the tree.</summary>
    public static void Apply(Control root)
    {
        if (root is Form f)
        {
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.Font = Body();
            f.Icon = AppIcon ?? f.Icon;
        }
        foreach (Control c in root.Controls) ApplyRecursive(c);
    }

    private static void ApplyRecursive(Control c)
    {
        switch (c)
        {
            case BrandHeader or CaptionPreview or ClipPlayer or SideNav or BrandProgressBar or FancyButton or Card:
                break;
            case Button b:
                // plain buttons that were not converted: give them the ghost look
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = BorderStrong;
                b.BackColor = Elevated;
                b.ForeColor = TextSecondary;
                break;
            case TextBox tb:
                if (tb.Parent is RoundedField) { tb.ForeColor = Text; break; } // already framed by the field
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = tb.ReadOnly ? SurfaceSoft : Surface;
                tb.ForeColor = Text;
                break;
            case RichTextBox rtb:
                rtb.BackColor = Elevated;
                rtb.ForeColor = TextSecondary;
                rtb.BorderStyle = BorderStyle.None;
                break;
            case ListView lv:
                StyleListView(lv);
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Surface;
                cb.ForeColor = Text;
                break;
            case NumericUpDown nud:
                nud.BorderStyle = BorderStyle.FixedSingle;
                nud.BackColor = Surface;
                nud.ForeColor = Text;
                break;
            case CheckBox chk:
                chk.ForeColor = TextSecondary;
                chk.FlatStyle = FlatStyle.Flat;
                break;
            case TrackBar tb:
                tb.BackColor = Elevated;
                break;
            case DataGridView grid:
                grid.BackgroundColor = Elevated;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceStrong;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
                grid.ColumnHeadersDefaultCellStyle.Font = new Font(HeadingFamily, 9f, FontStyle.Bold);
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceStrong;
                grid.DefaultCellStyle.BackColor = Elevated;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = SelectionBg;
                grid.DefaultCellStyle.SelectionForeColor = SelectionFg;
                grid.RowsDefaultCellStyle.BackColor = Elevated;
                grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceSoft;
                return;
            case Label lbl:
                if (lbl.ForeColor == SystemColors.ControlText || lbl.ForeColor == Color.Empty) lbl.ForeColor = TextSecondary;
                else if (lbl.ForeColor == Color.DimGray) lbl.ForeColor = TextMuted;
                break;
            case TabControl tc:
                HideTabStrip(tc);
                break;
            case TabPage tp:
                tp.BackColor = Bg;
                tp.UseVisualStyleBackColor = false;
                tp.Padding = Padding.Empty;
                break;
            case SplitContainer sc:
                sc.BackColor = Bg;
                sc.Panel1.BackColor = Bg;
                sc.Panel2.BackColor = Bg;
                sc.SplitterWidth = 8;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                if (c.BackColor == SystemColors.Control || c.BackColor == Color.Transparent || c.BackColor == Color.White || c.BackColor == ColorTranslator.FromHtml("#FAFBFF"))
                    c.BackColor = c.Parent is Card ? c.Parent.BackColor : Bg;
                break;
        }
        foreach (Control child in c.Controls) ApplyRecursive(child);
    }

    /// <summary>The tab control is only a page host: the SideNav drives it, so the strip is hidden.</summary>
    private static void HideTabStrip(TabControl tc)
    {
        tc.Appearance = TabAppearance.FlatButtons;
        tc.ItemSize = new Size(0, 1);
        tc.SizeMode = TabSizeMode.Fixed;
        tc.Multiline = false;
        tc.BackColor = Bg;
    }

    /// <summary>Dark list view with owner-drawn headers (system headers stay light otherwise).</summary>
    public static void StyleListView(ListView lv)
    {
        lv.BackColor = Elevated;
        lv.ForeColor = Text;
        lv.BorderStyle = BorderStyle.None;
        lv.OwnerDraw = true;
        lv.GridLines = false;
        lv.DrawColumnHeader -= LvHeader;
        lv.DrawColumnHeader += LvHeader;
        lv.DrawItem -= LvItem;
        lv.DrawItem += LvItem;
        lv.DrawSubItem -= LvSubItem;
        lv.DrawSubItem += LvSubItem;
        lv.Font = Body(9f);
    }

    private static void LvHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(SurfaceStrong);
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var line = new Pen(Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        using var f = new Font(HeadingFamily, 8.5f, FontStyle.Bold);
        var r = e.Bounds; r.Inflate(-6, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", f, r, TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static void LvItem(object? sender, DrawListViewItemEventArgs e)
    {
        if (sender is ListView { View: View.Details }) { e.DrawDefault = false; return; } // handled per sub item
        e.DrawDefault = true;
    }

    private static void LvSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView lv || e.Item is null || e.SubItem is null) return;
        bool selected = e.Item.Selected;
        var rowBg = selected ? SelectionBg : (e.ItemIndex % 2 == 1 ? ColorTranslator.FromHtml("#FAFAFD") : Elevated);
        using (var bg = new SolidBrush(rowBg)) e.Graphics.FillRectangle(bg, e.Bounds);

        int textX = e.Bounds.X + 6;
        if (e.ColumnIndex == 0 && lv.CheckBoxes)
        {
            var box = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + (e.Bounds.Height - 14) / 2, 14, 14);
            using var path = FancyButton.Rounded(box, 3);
            if (e.Item.Checked)
            {
                using var fill = new LinearGradientBrush(box, CosmicBlue, Nebula, 45f);
                e.Graphics.FillPath(fill, path);
                using var cf = IconFont(8f);
                TextRenderer.DrawText(e.Graphics, "", cf, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                using var pen = new Pen(BorderHover);
                e.Graphics.DrawPath(pen, path);
            }
            textX = box.Right + 6;
        }
        var fore = e.SubItem.ForeColor == SystemColors.WindowText || e.SubItem.ForeColor == Color.Empty || e.SubItem.ForeColor == Color.White ? Text : e.SubItem.ForeColor;
        var font = e.SubItem.Font ?? lv.Font;
        var tr = new Rectangle(textX, e.Bounds.Y, e.Bounds.Right - textX - 4, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, tr, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Slim brand header: nebula artwork, page title and subtitle. Sits above the content area.</summary>
public sealed class BrandHeader : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "Video";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Tagline { get; set; } = "";

    public BrandHeader()
    {
        Height = 74;
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(string title, string tagline) { Subtitle = title; Tagline = tagline; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // light header with a faint aurora tint on the left, echoing the sidebar artwork
        using (var bg = new SolidBrush(Theme.Elevated)) g.FillRectangle(bg, ClientRectangle);
        using (var tint = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Height), Color.FromArgb(60, Theme.Purple), Color.FromArgb(0, Theme.CosmicBlue), 0f))
        {
            var blend = new ColorBlend(3) { Colors = new[] { Color.FromArgb(60, Theme.Purple), Color.FromArgb(18, Theme.CosmicBlue), Color.FromArgb(0, Theme.CosmicBlue) }, Positions = new[] { 0f, 0.55f, 1f } };
            tint.InterpolationColors = blend;
            g.FillRectangle(tint, ClientRectangle);
        }
        using (var line = new LinearGradientBrush(new Rectangle(0, Height - 2, Math.Max(1, Width), 2), Color.FromArgb(200, Theme.CosmicBlue), Color.FromArgb(0, Theme.Nebula), 0f))
            g.FillRectangle(line, 0, Height - 2, Width, 2);

        using var titleFont = Theme.HeadingFont(15f);
        using var tagFont = Theme.Body(9f);
        TextRenderer.DrawText(g, Subtitle, titleFont, new Point(24, 16), Theme.Heading, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Tagline, tagFont, new Rectangle(24, 44, Width - 48, 20), Theme.TextMuted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Flat progress bar with the Galiluna gradient (and an indeterminate shimmer when Value is unknown).</summary>
public sealed class BrandProgressBar : Control
{
    private int _value;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public BrandProgressBar()
    {
        Height = 10;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        var track = new Rectangle(0, (Height - 6) / 2, Width - 1, 6);
        using var trackBrush = new SolidBrush(Theme.SurfaceStrong);
        using var trackPath = FancyButton.Rounded(track, 3);
        g.FillPath(trackBrush, trackPath);

        int w = (int)(track.Width * _value / 100.0);
        if (w < 6) return;
        var fill = new Rectangle(track.X, track.Y, w, track.Height);
        using var fillBrush = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), 1), Theme.CosmicBlue, Theme.Nebula, 0f);
        using var fillPath = FancyButton.Rounded(fill, 3);
        g.FillPath(fillBrush, fillPath);
    }
}
