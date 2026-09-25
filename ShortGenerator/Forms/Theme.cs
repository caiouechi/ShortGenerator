using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace ShortGenerator.Forms;

/// <summary>
/// Galiluna brand foundation (v1.1 guidelines) translated to WinForms:
/// navy #0B1028, cosmic blue #295BFF, lunar purple #6A38FF, nebula #A855F7.
/// Light theme surfaces for the work area, dark navy header with the light wordmark.
/// </summary>
public static class Theme
{
    // palette
    public static readonly Color Navy = ColorTranslator.FromHtml("#0B1028");
    public static readonly Color CosmicBlue = ColorTranslator.FromHtml("#295BFF");
    public static readonly Color Purple = ColorTranslator.FromHtml("#6A38FF");
    public static readonly Color PurpleHover = ColorTranslator.FromHtml("#5A2BE6");
    public static readonly Color PurpleDeep = ColorTranslator.FromHtml("#4523C7");
    public static readonly Color Nebula = ColorTranslator.FromHtml("#A855F7");

    // dark surfaces (header)
    public static readonly Color DarkBg = ColorTranslator.FromHtml("#131834");
    public static readonly Color DarkElevated = ColorTranslator.FromHtml("#191F40");
    public static readonly Color DarkSurfaceStrong = ColorTranslator.FromHtml("#272F60");
    public static readonly Color DarkTextSecondary = ColorTranslator.FromHtml("#C7CBE0");
    public static readonly Color DarkTextMuted = ColorTranslator.FromHtml("#9298B3");

    // light surfaces (work area)
    public static readonly Color Bg = ColorTranslator.FromHtml("#FAFBFF");
    public static readonly Color Elevated = Color.White;
    public static readonly Color SurfaceSoft = ColorTranslator.FromHtml("#F4F1FF");
    public static readonly Color Text = Navy;
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#3E4668");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#737B98");
    public static readonly Color Heading = ColorTranslator.FromHtml("#2E3A6E");
    public static readonly Color Border = ColorTranslator.FromHtml("#E7EAF5");
    public static readonly Color BorderStrong = ColorTranslator.FromHtml("#D8DDED");
    public static readonly Color BorderHover = ColorTranslator.FromHtml("#C9BCFF");
    public static readonly Color Success = ColorTranslator.FromHtml("#16794F");
    public static readonly Color Danger = ColorTranslator.FromHtml("#C5283D");

    private static string? _bodyFamily;
    private static string? _headingFamily;

    /// <summary>Inter for body, Poppins for headings when installed; Segoe UI otherwise.</summary>
    public static string BodyFamily => _bodyFamily ??= FirstInstalled("Inter", "Segoe UI");
    public static string HeadingFamily => _headingFamily ??= FirstInstalled("Poppins", "Inter", "Segoe UI");

    public static Font Body(float size = 9.5f, FontStyle style = FontStyle.Regular) => new(BodyFamily, size, style);
    public static Font HeadingFont(float size = 12f, FontStyle style = FontStyle.Bold) => new(HeadingFamily, size, style);
    public static Font Mono(float size = 8.5f) => new(FirstInstalled("Cascadia Mono", "Consolas"), size);

    private static string FirstInstalled(params string[] families)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return families.FirstOrDefault(names.Contains) ?? "Segoe UI";
    }

    // ------------------------------------------------------------ assets

    public static Image? LoadImage(string name)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (res is null) return null;
            using var s = asm.GetManifestResourceStream(res);
            return s is null ? null : Image.FromStream(s);
        }
        catch { return null; }
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

    public static void Primary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PurpleHover;
        b.FlatAppearance.MouseDownBackColor = PurpleDeep;
        b.BackColor = Purple;
        b.ForeColor = Color.White;
        b.Font = new Font(HeadingFamily, 9.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Tag = "styled"; // keep Apply() from also giving it the secondary look
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = b.Enabled ? Purple : ColorTranslator.FromHtml("#C4B5FD");
            b.ForeColor = Color.White; // always readable on the purple fill
        };
        if (!b.Enabled) b.BackColor = ColorTranslator.FromHtml("#C4B5FD");
        // WinForms paints disabled button text gray regardless of ForeColor; overdraw it in white.
        b.Paint += (_, e) =>
        {
            if (b.Enabled) return;
            using var fill = new SolidBrush(ColorTranslator.FromHtml("#C4B5FD"));
            e.Graphics.FillRectangle(fill, b.ClientRectangle);
            TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    public static void Secondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = BorderStrong;
        b.FlatAppearance.MouseOverBackColor = SurfaceSoft;
        b.FlatAppearance.MouseDownBackColor = ColorTranslator.FromHtml("#E9E3FF");
        b.BackColor = Elevated;
        b.ForeColor = Heading;
        b.Font = new Font(HeadingFamily, 9f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.EnabledChanged += (_, _) => b.ForeColor = b.Enabled ? Heading : TextMuted;
        if (!b.Enabled) b.ForeColor = TextMuted;
    }

    /// <summary>Applies light-theme colors and fonts to every control in the tree.</summary>
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
            case BrandHeader:
            case CaptionPreview:
            case ClipPlayer:
                return; // self-styled
            case DataGridView grid:
                grid.BackgroundColor = Elevated;
                grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceSoft;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Heading;
                grid.ColumnHeadersDefaultCellStyle.Font = new Font(HeadingFamily, 9f, FontStyle.Bold);
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = ColorTranslator.FromHtml("#E9E3FF");
                grid.DefaultCellStyle.SelectionForeColor = Text;
                return;
            case Button b when b.Tag is not "styled":
                Secondary(b);
                b.Tag = "styled";
                break;
            case TextBox tb:
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = tb.ReadOnly ? SurfaceSoft : Elevated;
                tb.ForeColor = Text;
                break;
            case RichTextBox rtb:
                rtb.BackColor = Elevated;
                rtb.ForeColor = TextSecondary;
                break;
            case ListView lv:
                lv.BackColor = Elevated;
                lv.ForeColor = Text;
                lv.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Elevated;
                cb.ForeColor = Text;
                break;
            case NumericUpDown nud:
                nud.BorderStyle = BorderStyle.FixedSingle;
                nud.BackColor = Elevated;
                nud.ForeColor = Text;
                break;
            case CheckBox chk:
                chk.ForeColor = TextSecondary;
                break;
            case Label lbl:
                if (lbl.ForeColor == SystemColors.ControlText || lbl.ForeColor == Color.Empty) lbl.ForeColor = TextSecondary;
                else if (lbl.ForeColor == Color.DimGray) lbl.ForeColor = TextMuted;
                break;
            case TabControl tc:
                StyleTabs(tc);
                break;
            case TabPage tp:
                tp.BackColor = Elevated;
                tp.UseVisualStyleBackColor = false;
                break;
            case SplitContainer sc:
                sc.BackColor = Border;
                sc.Panel1.BackColor = Elevated;
                sc.Panel2.BackColor = Elevated;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                if (c.BackColor == SystemColors.Control || c.BackColor == Color.Transparent) c.BackColor = c.Parent is TabPage ? Elevated : Bg;
                break;
        }
        foreach (Control child in c.Controls) ApplyRecursive(child);
    }

    private static void StyleTabs(TabControl tc)
    {
        tc.DrawMode = TabDrawMode.OwnerDrawFixed;
        tc.SizeMode = TabSizeMode.Fixed;
        tc.ItemSize = new Size(190, 34);
        tc.Padding = new Point(12, 4);
        tc.Font = new Font(HeadingFamily, 9.5f, FontStyle.Bold);
        tc.DrawItem += (s, e) =>
        {
            var page = tc.TabPages[e.Index];
            bool selected = tc.SelectedIndex == e.Index;
            var rect = e.Bounds;
            using var bg = new SolidBrush(selected ? Elevated : Bg);
            e.Graphics.FillRectangle(bg, rect);
            if (selected)
            {
                using var accent = new LinearGradientBrush(new Rectangle(rect.X, rect.Y, rect.Width, 3), CosmicBlue, Nebula, 0f);
                e.Graphics.FillRectangle(accent, rect.X, rect.Y, rect.Width, 3);
            }
            TextRenderer.DrawText(e.Graphics, page.Text, tc.Font, rect, selected ? Purple : TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        // Paint the strip background behind the tabs.
        tc.Paint += (s, e) =>
        {
            using var b = new SolidBrush(Bg);
            e.Graphics.FillRectangle(b, 0, 0, tc.Width, tc.ItemSize.Height + 2);
        };
    }
}

/// <summary>Dark navy header with the Galiluna light wordmark and the app name.</summary>
public sealed class BrandHeader : Control
{
    private readonly Image? _logo = Theme.LoadImage("galiluna-logo.png");
    private readonly Image? _mark = Theme.LoadImage("galiluna-icon.png");

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "Short Generator";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Tagline { get; set; } = "Download - transcribe - find the viral moments - cut captioned shorts";

    public BrandHeader()
    {
        Height = 72;
        Dock = DockStyle.Top;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Light header: the wordmark reads best on bright surfaces.
        using (var bg = new LinearGradientBrush(ClientRectangle, Color.White, Theme.SurfaceSoft, 0f))
            g.FillRectangle(bg, ClientRectangle);
        // Brand gradient hairline at the bottom, like the web navbar.
        using (var line = new LinearGradientBrush(new Rectangle(0, Height - 3, Width, 3), Theme.CosmicBlue, Theme.Nebula, 0f))
            g.FillRectangle(line, 0, Height - 3, Width, 3);

        int x = 16;
        if (_mark is not null)
        {
            int size = Height - 20;
            g.DrawImage(_mark, new Rectangle(x, 10, size, size));
            x += size + 8;
        }
        if (_logo is not null)
        {
            int h = 30;
            int w = (int)(_logo.Width * (h / (double)_logo.Height));
            g.DrawImage(_logo, new Rectangle(x, (Height - h) / 2 - 2, w, h));
            x += w + 14;
        }

        using var sep = new Pen(Theme.BorderStrong, 1);
        g.DrawLine(sep, x, 18, x, Height - 18);
        x += 14;

        using var titleFont = Theme.HeadingFont(13f);
        using var tagFont = Theme.Body(8.5f);
        TextRenderer.DrawText(g, Subtitle, titleFont, new Point(x, 14), Theme.Heading);
        TextRenderer.DrawText(g, Tagline, tagFont, new Point(x, 40), Theme.TextMuted);
    }
}

/// <summary>Flat progress bar with the Galiluna gradient.</summary>
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
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, (Height - 8) / 2, Width - 1, 8);
        using var trackBrush = new SolidBrush(Theme.SurfaceSoft);
        using var trackPen = new Pen(Theme.Border);
        using var trackPath = Rounded(track, 4);
        g.FillPath(trackBrush, trackPath);
        g.DrawPath(trackPen, trackPath);

        int w = (int)(track.Width * _value / 100.0);
        if (w < 8) return;
        var fill = new Rectangle(track.X, track.Y, w, track.Height);
        using var fillBrush = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), 1), Theme.CosmicBlue, Theme.Nebula, 0f);
        using var fillPath = Rounded(fill, 4);
        g.FillPath(fillBrush, fillPath);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
