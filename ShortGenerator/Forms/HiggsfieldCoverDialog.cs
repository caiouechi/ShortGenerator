using ShortGenerator.Forms.Controls;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// Asked before calling Higgsfield for a cover: use the current frame as the reference or not, and what the
/// person wants the thumbnail to be. The viral-creator persona and the 9:16 publishing specs are always part of
/// the prompt; the free text fills the "ideal thumbnail" line. The final prompt is shown before it is sent.
/// </summary>
public sealed class HiggsfieldCoverDialog : Form
{
    private readonly CheckBox _useReference = new() { Text = "Use the current cover frame as the reference", AutoSize = true, Checked = true };
    private readonly TextBox _wish = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 96, Dock = DockStyle.Top };
    private readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 190, Dock = DockStyle.Top };
    private readonly FancyButton _generate = new() { Text = "Generate cover", Width = 180, Height = 38, Glyph = "" };
    private readonly FancyButton _cancel = new() { Text = "Cancel", Width = 110, Height = 38 };
    private readonly PictureBox _thumb = new() { Width = 90, Height = 160, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(19, 24, 52) };
    private readonly ShortSuggestion _short;
    private readonly string _language;

    public bool UseReference => _useReference.Checked;
    public string Prompt => _preview.Text;

    public HiggsfieldCoverDialog(ShortSuggestion s, string language, Image? cover, string modelName)
    {
        _short = s; _language = language;
        Text = "Higgsfield cover";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 640);
        BackColor = Theme.Bg;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 18, 24, 18) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _thumb.Image = cover;
        _thumb.Margin = new Padding(0, 4, 16, 0);
        root.Controls.Add(_thumb, 0, 0);
        root.SetRowSpan(_thumb, 2);

        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = Padding.Empty };
        stack.Controls.Add(new Label { Text = s.Title, AutoSize = true, Font = Theme.HeadingFont(11f), ForeColor = Theme.Heading, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 2) });
        stack.Controls.Add(new Label { Text = $"Higgsfield ({modelName}) redraws the cover as a 9:16 thumbnail. The viral-creator brief and the publishing specs (1080x1920, headline in the upper third, bottom 20% free) are always included.", AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 12) });
        _useReference.Margin = new Padding(0, 0, 0, 2);
        stack.Controls.Add(_useReference);
        stack.Controls.Add(new Label { Text = "Ticked: the same person, face and scene are kept. Unticked: Higgsfield invents a fitting scene from the brief alone.", AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 12) });
        stack.Controls.Add(new Label { Text = "What do you want the thumbnail to be? (expression, headline words, colours, what to exaggerate)", AutoSize = true, ForeColor = Theme.TextSecondary, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 4) });
        _wish.Width = 560;
        _wish.Text = ThumbnailBrief.AutoIdeal(s);
        stack.Controls.Add(_wish);
        stack.Controls.Add(new Label { Text = "Prompt that will be sent", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 12, 0, 4) });
        _preview.Width = 560; _preview.WordWrap = true;
        _preview.Font = Theme.Mono(8.5f);
        stack.Controls.Add(_preview);
        root.Controls.Add(stack, 1, 0);
        root.SetRowSpan(stack, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(_generate);
        _cancel.Margin = new Padding(0, 0, 8, 0);
        buttons.Controls.Add(_cancel);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        _wish.TextChanged += (_, _) => Refresh();
        _useReference.CheckedChanged += (_, _) => Refresh();
        _generate.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = _generate; CancelButton = _cancel;

        Theme.Primary(_generate);
        Theme.Apply(this);
        Refresh();
    }

    private new void Refresh()
    {
        _preview.Text = ThumbnailBrief.Build(_short, _language, _wish.Text.Trim(), _useReference.Checked).Replace("\n", Environment.NewLine);
    }
}
