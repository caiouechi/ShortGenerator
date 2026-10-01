using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ShortGenerator.Forms.Controls;

namespace ShortGenerator.Forms;

/// <summary>
/// The app's own confirm / alert dialog, replacing the grey Windows MessageBox: brand fonts and colours, a tinted
/// icon badge, the title as a heading, an optional details box (what is about to happen, one line per item), muted
/// notes, and buttons named after the action ("Publish", "Delete") rather than Yes / No. The window behind it dims
/// while it is open. <see cref="Show(IWin32Window?, string, string, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton)"/>
/// keeps MessageBox's signature so every existing call reads the same.
/// </summary>
public sealed class AppDialog : Form
{
    public enum Kind { Info, Question, Warning, Error, Success }

    private const int DialogWidth = 520;
    private readonly Kind _kind;
    private Point _dragFrom;

    private AppDialog(string title, string message, Kind kind, IReadOnlyList<string>? details, IReadOnlyList<string>? notes,
        IReadOnlyList<(string Text, DialogResult Result, ButtonKind Style)> buttons, int defaultIndex, DialogResult escapeResult)
    {
        _kind = kind;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Elevated;
        Font = Theme.Body();
        Text = string.IsNullOrWhiteSpace(title) ? "Galiluna Short Generator" : title;
        Padding = new Padding(1, 4, 1, 1); // the top 4 px hold the brand accent
        Width = DialogWidth;

        int textWidth = DialogWidth - 24 - 44 - 16 - 24;
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(24, 26, 24, 20), BackColor = Theme.Elevated };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var badge = new IconBadge(kind) { Margin = new Padding(0, 2, 16, 0) };
        root.Controls.Add(badge, 0, 0);
        root.SetRowSpan(badge, 4);

        int row = 0;
        if (!string.IsNullOrWhiteSpace(title))
        {
            root.Controls.Add(new Label
            {
                Text = title, AutoSize = true, MaximumSize = new Size(textWidth, 0), Font = Theme.HeadingFont(12.5f), ForeColor = Theme.Heading,
                Margin = new Padding(0, 0, 0, 6), UseMnemonic = false,
            }, 1, row++);
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            root.Controls.Add(new Label
            {
                Text = message.Trim(), AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = Theme.TextSecondary,
                Margin = new Padding(0, 0, 0, 10), UseMnemonic = false,
            }, 1, row++);
        }
        if (details is { Count: > 0 })
        {
            var box = new DetailsBox(details, textWidth) { Margin = new Padding(0, 2, 0, 10) };
            root.Controls.Add(box, 1, row++);
        }
        if (notes is { Count: > 0 })
        {
            var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 4), BackColor = Theme.Elevated };
            foreach (var n in notes)
            {
                bool strong = n.StartsWith("!", StringComparison.Ordinal);
                flow.Controls.Add(new Label
                {
                    Text = (strong ? n[1..] : n).Trim(), AutoSize = true, MaximumSize = new Size(textWidth, 0), UseMnemonic = false,
                    ForeColor = strong ? Theme.Warning : Theme.TextMuted, Font = strong ? Theme.Body(9f, FontStyle.Bold) : Theme.Body(9f),
                    Margin = new Padding(0, 0, 0, 4),
                });
            }
            root.Controls.Add(flow, 1, row++);
        }
        for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // button bar on a soft strip, primary action last (rightmost)
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(24, 14, 24, 14), BackColor = Theme.Bg,
        };
        FancyButton? defaultButton = null;
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            var (text, result, style) = buttons[i];
            var b = new FancyButton { Text = text, Kind = style, Height = 38, Margin = new Padding(10, 0, 0, 0) };
            b.Width = Math.Max(96, TextRenderer.MeasureText(text, b.Font).Width + 44);
            b.Click += (_, _) => { DialogResult = result; Close(); };
            bar.Controls.Add(b);
            if (i == defaultIndex) defaultButton = b;
        }

        var close = new Label
        {
            Text = "", Font = Theme.IconFont(9f), ForeColor = Theme.TextMuted, AutoSize = false, Size = new Size(32, 32),
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, BackColor = Theme.Elevated,
        };
        close.Click += (_, _) => { DialogResult = escapeResult; Close(); };
        close.MouseEnter += (_, _) => close.ForeColor = Theme.Text;
        close.MouseLeave += (_, _) => close.ForeColor = Theme.TextMuted;

        Controls.Add(bar);
        Controls.Add(root);
        Controls.Add(close);
        close.BringToFront();

        Load += (_, _) =>
        {
            ClientSize = new Size(DialogWidth, root.PreferredSize.Height + bar.PreferredSize.Height + 6);
            close.Location = new Point(ClientSize.Width - close.Width - 8, 10);
            defaultButton?.Focus();
            if (Owner is { } o && StartPosition == FormStartPosition.CenterParent)
                Location = new Point(o.Left + (o.Width - Width) / 2, o.Top + Math.Max(40, (o.Height - Height) / 2));
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = escapeResult; Close(); }
            else if (e.KeyCode == Keys.Enter && defaultButton is not null) { defaultButton.PerformClick(); e.Handled = true; }
        };

        // drag the window by any empty area
        foreach (var c in new Control[] { this, root })
        {
            c.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) _dragFrom = e.Location; };
            c.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - _dragFrom.X, Location.Y + e.Y - _dragFrom.Y); };
        }
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; /* CS_DROPSHADOW */ return cp; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: rounded corners on a borderless window
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        // brand accent along the top edge, tinted by the kind for warnings and errors
        var (a, b) = _kind switch
        {
            Kind.Warning => (Theme.Warning, ColorTranslator.FromHtml("#E0A100")),
            Kind.Error => (Theme.Danger, ColorTranslator.FromHtml("#E5484D")),
            _ => (Theme.CosmicBlue, Theme.Purple),
        };
        using var accent = new LinearGradientBrush(new Rectangle(0, 0, Width, 4), a, b, LinearGradientMode.Horizontal);
        g.FillRectangle(accent, 0, 0, Width, 4);
        using var pen = new Pen(Theme.BorderStrong);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // ------------------------------------------------------------------ API

    /// <summary>Drop-in for MessageBox.Show: same arguments, the app's look.</summary>
    public static DialogResult Show(IWin32Window? owner, string text, string caption = "", MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        var kind = icon switch
        {
            MessageBoxIcon.Question => Kind.Question,
            MessageBoxIcon.Warning => Kind.Warning,
            MessageBoxIcon.Error => Kind.Error,
            _ => buttons is MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel or MessageBoxButtons.OKCancel ? Kind.Question : Kind.Info,
        };
        // a warning question that defaults to "No" is a destructive confirmation: its "Yes" is red
        var yesStyle = kind == Kind.Warning && defaultButton == MessageBoxDefaultButton.Button2 ? ButtonKind.Danger : ButtonKind.Primary;
        var list = buttons switch
        {
            MessageBoxButtons.YesNo => new List<(string, DialogResult, ButtonKind)> { ("No", DialogResult.No, ButtonKind.Ghost), ("Yes", DialogResult.Yes, yesStyle) },
            MessageBoxButtons.YesNoCancel => new() { ("Cancel", DialogResult.Cancel, ButtonKind.Subtle), ("No", DialogResult.No, ButtonKind.Ghost), ("Yes", DialogResult.Yes, yesStyle) },
            MessageBoxButtons.OKCancel => new() { ("Cancel", DialogResult.Cancel, ButtonKind.Ghost), ("OK", DialogResult.OK, ButtonKind.Primary) },
            MessageBoxButtons.RetryCancel => new() { ("Cancel", DialogResult.Cancel, ButtonKind.Ghost), ("Retry", DialogResult.Retry, ButtonKind.Primary) },
            _ => new() { ("OK", DialogResult.OK, ButtonKind.Primary) },
        };
        // MessageBox counts buttons left to right as Windows draws them (Yes = Button1); our lists are left to right too
        int def = buttons switch
        {
            MessageBoxButtons.YesNo => defaultButton == MessageBoxDefaultButton.Button2 ? 0 : 1,
            MessageBoxButtons.YesNoCancel => defaultButton switch { MessageBoxDefaultButton.Button2 => 1, MessageBoxDefaultButton.Button3 => 0, _ => 2 },
            MessageBoxButtons.OKCancel or MessageBoxButtons.RetryCancel => defaultButton == MessageBoxDefaultButton.Button2 ? 0 : 1,
            _ => 0,
        };
        var escape = buttons switch
        {
            MessageBoxButtons.YesNo => DialogResult.No,
            MessageBoxButtons.OK => DialogResult.OK,
            _ => DialogResult.Cancel,
        };
        // titles stay short headings; the old "Something failed" captions read fine as such
        return Run(owner, caption, text, kind, null, null, list, def, escape);
    }

    public static DialogResult Show(string text, string caption = "", MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        => Show(null, text, caption, buttons, icon, defaultButton);

    /// <summary>A confirmation with a named action: true when the user clicked <paramref name="confirm"/>.</summary>
    /// <param name="details">Shown in a soft box, one line each (what is about to happen).</param>
    /// <param name="notes">Muted lines under it; a note starting with "!" is a warning.</param>
    public static bool Confirm(IWin32Window? owner, string title, string message, string confirm, string cancel = "Cancel",
        bool danger = false, IReadOnlyList<string>? details = null, IReadOnlyList<string>? notes = null, Kind kind = Kind.Question)
    {
        var list = new List<(string, DialogResult, ButtonKind)>
        {
            (cancel, DialogResult.Cancel, ButtonKind.Ghost),
            (confirm, DialogResult.OK, danger ? ButtonKind.Danger : ButtonKind.Primary),
        };
        return Run(owner, title, message, danger && kind == Kind.Question ? Kind.Warning : kind, details, notes, list, danger ? 0 : 1, DialogResult.Cancel) == DialogResult.OK;
    }

    /// <summary>An alert with one OK button.</summary>
    public static void Alert(IWin32Window? owner, string title, string message, Kind kind = Kind.Info, IReadOnlyList<string>? details = null)
        => Run(owner, title, message, kind, details, null, new List<(string, DialogResult, ButtonKind)> { ("OK", DialogResult.OK, ButtonKind.Primary) }, 0, DialogResult.OK);

    private static DialogResult Run(IWin32Window? owner, string title, string message, Kind kind, IReadOnlyList<string>? details, IReadOnlyList<string>? notes,
        List<(string, DialogResult, ButtonKind)> buttons, int defaultIndex, DialogResult escape)
    {
        var ownerForm = (owner as Control)?.FindForm() ?? Form.ActiveForm;
        using var dlg = new AppDialog(title, message, kind, details, notes, buttons, defaultIndex, escape) { Icon = Theme.AppIcon };
        // dim the window behind, the way the web app greys the page behind a modal
        Form? shade = null;
        if (ownerForm is { Visible: true, WindowState: not FormWindowState.Minimized } && ownerForm is not AppDialog)
        {
            shade = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                BackColor = Theme.Navy, Opacity = 0.32, Bounds = ownerForm.Bounds,
            };
            shade.Show(ownerForm);
        }
        try
        {
            var result = dlg.ShowDialog((IWin32Window?)shade ?? ownerForm);
            return result == DialogResult.None ? escape : result;
        }
        finally
        {
            shade?.Close();
            shade?.Dispose();
            ownerForm?.Activate();
        }
    }

    // ------------------------------------------------------------------ parts

    /// <summary>Round tinted badge with the kind's glyph.</summary>
    private sealed class IconBadge : Control
    {
        private readonly Kind _kind;
        public IconBadge(Kind kind)
        {
            _kind = kind;
            Size = new Size(44, 44);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Elevated;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var (fill, fg, glyph) = _kind switch
            {
                Kind.Warning => (ColorTranslator.FromHtml("#FFF4DC"), Theme.Warning, ""),
                Kind.Error => (ColorTranslator.FromHtml("#FDECEC"), Theme.Danger, ""),
                Kind.Success => (ColorTranslator.FromHtml("#E5F6EE"), Theme.Success, ""),
                Kind.Question => (Theme.SurfaceSoft, Theme.Purple, ""),
                _ => (ColorTranslator.FromHtml("#EAF0FF"), Theme.CosmicBlue, ""),
            };
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            using var f = Theme.IconFont(15f);
            TextRenderer.DrawText(g, glyph, f, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Soft rounded box listing what is about to happen, one line each.</summary>
    private sealed class DetailsBox : Panel
    {
        public DetailsBox(IReadOnlyList<string> lines, int width)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.SurfaceSoft;
            Padding = new Padding(12, 10, 12, 8);
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.SurfaceSoft };
            int h = 0, max = width - Padding.Horizontal - 20;
            foreach (var line in lines)
            {
                // "first line\nmore" = the item in bold, its details muted underneath
                var parts = line.Split('\n', 2);
                var head = new Label
                {
                    Text = parts[0].Trim(), AutoSize = true, MaximumSize = new Size(max, 0), ForeColor = Theme.Text, UseMnemonic = false,
                    Font = parts.Length > 1 ? Theme.Body(9.5f, FontStyle.Bold) : Theme.Body(9.5f),
                    Margin = new Padding(0, 0, 0, parts.Length > 1 ? 1 : 8), BackColor = Theme.SurfaceSoft,
                };
                flow.Controls.Add(head);
                h += head.GetPreferredSize(new Size(max, 0)).Height + head.Margin.Vertical;
                if (parts.Length > 1)
                {
                    var sub = new Label
                    {
                        Text = parts[1].Trim(), AutoSize = true, MaximumSize = new Size(max, 0), ForeColor = Theme.TextMuted, UseMnemonic = false,
                        Font = Theme.Body(9f), Margin = new Padding(0, 0, 0, 8), BackColor = Theme.SurfaceSoft,
                    };
                    flow.Controls.Add(sub);
                    h += sub.GetPreferredSize(new Size(max, 0)).Height + sub.Margin.Vertical;
                }
            }
            Controls.Add(flow);
            Size = new Size(width, Math.Min(h, 260) + Padding.Vertical);
            flow.AutoScroll = h > 260;
            // clip to the rounded shape so no square corner shows
            SizeChanged += (_, _) => { using var gp = FancyButton.Rounded(new Rectangle(0, 0, Width, Height), 10); Region = new Region(gp); };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Theme.Elevated)) g.FillRectangle(bg, ClientRectangle);
            using var path = FancyButton.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
    }
}
