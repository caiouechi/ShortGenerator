using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ShortGenerator.Forms.Controls;

/// <summary>Rounded surface panel with an optional title row. Children dock inside the padded body.</summary>
public class Card : Panel
{
    private string _title = "";
    private string _subtitle = "";

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Elevated;
        Padding = new Padding(14, 14, 14, 14);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get => _title; set { _title = value; UpdatePadding(); Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get => _subtitle; set { _subtitle = value; UpdatePadding(); Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 14;

    private void UpdatePadding()
    {
        int top = 14 + (string.IsNullOrEmpty(_title) ? 0 : 26) + (string.IsNullOrEmpty(_subtitle) ? 0 : 18);
        Padding = new Padding(14, top, 14, 14);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = FancyButton.Rounded(rect, Radius);
        using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);

        int y = 12;
        if (!string.IsNullOrEmpty(_title))
        {
            using var f = Theme.HeadingFont(10.5f);
            TextRenderer.DrawText(g, _title, f, new Point(14, y), Theme.Heading, TextFormatFlags.NoPrefix);
            y += 24;
        }
        if (!string.IsNullOrEmpty(_subtitle))
        {
            using var f = Theme.Body(8.5f);
            TextRenderer.DrawText(g, _subtitle, f, new Rectangle(14, y, Width - 28, 18), Theme.TextMuted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
