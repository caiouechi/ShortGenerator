using ShortGenerator.Models;

namespace ShortGenerator.Forms;

/// <summary>Small dialog to add a custom clip or adjust the start/end of a suggested one.</summary>
public sealed class ClipTimesDialog : Form
{
    private readonly TextBox _title = new() { Width = 320 };
    private readonly NumericUpDown _start = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 100 };
    private readonly NumericUpDown _end = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 100 };
    private readonly Label _duration = new() { AutoSize = true, ForeColor = Color.DimGray };

    public ClipTimesDialog(ShortSuggestion s, double videoDuration)
    {
        Text = "Clip times";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 190);

        decimal max = (decimal)Math.Max(1, videoDuration > 0 ? videoDuration : 36000);
        _start.Maximum = max; _end.Maximum = max;
        _title.Text = s.Title;
        _start.Value = (decimal)Math.Clamp(s.StartSeconds, 0, (double)max);
        _end.Value = (decimal)Math.Clamp(s.EndSeconds, 0, (double)max);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Title", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 0);
        table.Controls.Add(_title, 1, 0);
        table.Controls.Add(new Label { Text = "Start (s)", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 1);
        table.Controls.Add(_start, 1, 1);
        table.Controls.Add(new Label { Text = "End (s)", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 2);
        table.Controls.Add(_end, 1, 2);
        table.Controls.Add(_duration, 1, 3);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) };
        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(table); Controls.Add(buttons);

        void Refresh() => _duration.Text = $"Duration: {(_end.Value - _start.Value):F1} s";
        _start.ValueChanged += (_, _) => Refresh();
        _end.ValueChanged += (_, _) => Refresh();
        Refresh();
        Theme.Primary(ok);
        Theme.Apply(this);

        ok.Click += (_, e) =>
        {
            if (_end.Value <= _start.Value + 1)
            {
                MessageBox.Show(this, "End must be at least 1 second after start.", "Invalid times");
                DialogResult = DialogResult.None;
                return;
            }
            s.Title = string.IsNullOrWhiteSpace(_title.Text) ? "Custom clip" : _title.Text.Trim();
            s.StartSeconds = (double)_start.Value;
            s.EndSeconds = (double)_end.Value;
        };
    }
}
