using ShortGenerator.Forms.Controls;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// Shown when moving from Suggestions to Generate shorts with shorts ticked for English: translate now with Claude
/// (API key), or copy the prompt to ChatGPT and paste the answer back. "Skip for now" continues without English.
/// </summary>
public sealed class TranslateDialog : Form
{
    private readonly CaptionTranslator.Job _job;
    private readonly Func<CancellationToken, Task<string>>? _claude;
    private readonly TextBox _answer = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill, MaxLength = 0, PlaceholderText = "Paste ChatGPT's JSON answer here, then click Import." };
    private readonly FancyButton _useClaude = new() { Text = "Translate with Claude", Width = 210, Height = 38, Glyph = "" };
    private readonly FancyButton _copy = new() { Text = "Copy prompt for ChatGPT", Width = 210, Height = 34, Glyph = "" };
    private readonly FancyButton _paste = new() { Text = "Paste", Width = 90 };
    private readonly FancyButton _import = new() { Text = "Import", Width = 110 };
    private readonly FancyButton _skip = new() { Text = "Skip for now", Width = 130 };
    private readonly FancyButton _cancel = new() { Text = "Cancel", Width = 100 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 0, 0) };
    private CancellationTokenSource? _cts;

    /// <summary>What happened: translated lines / shorts and lines the answer left out.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public (int Lines, int Shorts, int Missing) Applied { get; private set; }

    public TranslateDialog(CaptionTranslator.Job job, int shortCount, Func<CancellationToken, Task<string>>? claude)
    {
        _job = job; _claude = claude;
        Text = "Translate to English";
        // Resizable: the answer box grows with the window, the text wraps to whatever width it has.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true; MinimizeBox = false;
        ShowIcon = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 580);
        MinimumSize = new Size(580, 480);
        BackColor = Theme.Bg;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(22, 18, 22, 16) };
        // one column exactly as wide as the window, never as wide as its widest child
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles[4] = new RowStyle(SizeType.Percent, 100);
        root.Controls.Add(new Label { Text = "English captions and post text", AutoSize = true, Font = Theme.HeadingFont(12f), ForeColor = Theme.Heading, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
        int n = job.Groups.Count;
        var what = $"{n} short{(n == 1 ? "" : "s")}, each with its whole transcript ({job.LineCount} caption line{(job.LineCount == 1 ? "" : "s")} in total) and its title, caption and hashtags" +
                   ". Only the shorts you are about to generate are translated; you can fix any English line later in Edit & preview.";
        var whatLabel = new Label { Text = what, AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 14), UseMnemonic = false };
        root.Controls.Add(whatLabel, 0, 1);

        var routes = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        _useClaude.Margin = new Padding(0, 0, 10, 0);
        if (claude is not null) routes.Controls.Add(_useClaude);
        routes.Controls.Add(_copy);
        root.Controls.Add(routes, 0, 2);
        var howLabel = new Label
        {
            Text = claude is not null ? "Claude translates in one step. Or copy the prompt to ChatGPT and paste its answer below."
                                      : "Copy the prompt, paste it into ChatGPT, then paste its answer below and click Import. (Add an Anthropic key in Settings to translate in one click.)",
            AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(howLabel, 0, 3);
        _answer.Font = Theme.Mono(9f);
        root.Controls.Add(_answer, 0, 4);

        // status on its own line under the answer, the buttons right-aligned below it (they wrap on a narrow window)
        root.Controls.Add(_status, 0, 5);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        foreach (var b in new[] { _cancel, _skip, _import, _paste }) { b.Margin = new Padding(8, 0, 0, 4); buttons.Controls.Add(b); }
        root.Controls.Add(buttons, 0, 6);
        Controls.Add(root);

        // labels wrap to the real width of the window (a fixed MaximumSize overflowed at 125 % scaling)
        void Wrap()
        {
            var w = Math.Max(200, root.ClientSize.Width - root.Padding.Horizontal);
            foreach (var l in new[] { whatLabel, howLabel, _status }) l.MaximumSize = new Size(w, 0);
        }
        root.SizeChanged += (_, _) => Wrap();
        Wrap();

        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(CaptionTranslator.BuildPrompt(_job)); SetStatus("Prompt copied. Paste it into ChatGPT, then paste the answer here.", Theme.TextMuted); }
            catch (Exception ex) { SetStatus(ex.Message, Theme.Danger); }
        };
        _paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) _answer.Text = Clipboard.GetText(); } catch { } };
        _import.Click += (_, _) => ApplyAnswer(_answer.Text);
        _useClaude.Click += async (_, _) => await RunClaudeAsync();
        _skip.Click += (_, _) => { DialogResult = DialogResult.Ignore; Close(); };
        _cancel.Click += (_, _) => { _cts?.Cancel(); DialogResult = DialogResult.Cancel; Close(); };
        FormClosing += (_, _) => _cts?.Cancel();

        Theme.Primary(claude is not null ? _useClaude : _copy);
        Theme.Primary(_import);
        Theme.Apply(this);
    }

    private async Task RunClaudeAsync()
    {
        if (_claude is null) return;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        SetStatus("Claude is translating...", Theme.TextMuted);
        try
        {
            var json = await _claude(_cts.Token);
            ApplyAnswer(json);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus("Claude: " + ex.Message, Theme.Danger); }
        finally { SetBusy(false); _cts?.Dispose(); _cts = null; }
    }

    private void ApplyAnswer(string raw)
    {
        try
        {
            var result = CaptionTranslator.Parse(raw);
            Applied = CaptionTranslator.Apply(_job, result);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { SetStatus("Could not read the answer: " + ex.Message, Theme.Danger); }
    }

    private void SetBusy(bool busy)
    {
        foreach (var b in new Control[] { _useClaude, _copy, _paste, _import, _skip, _answer }) b.Enabled = !busy;
    }

    private void SetStatus(string text, Color color) { _status.Text = text; _status.ForeColor = color; }
}
