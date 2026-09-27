using ShortGenerator.Forms.Controls;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _apiKey = new() { UseSystemPasswordChar = true, Width = 420 };
    private readonly TextBox _model = new() { Width = 420 };
    private readonly ComboBox _whisperModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _downloadFolder = new() { Width = 380 };
    private readonly TextBox _outputFolder = new() { Width = 380 };
    private readonly TextBox _toolsFolder = new() { Width = 380 };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 20, Width = 80 };
    private readonly NumericUpDown _minSec = new() { Minimum = 5, Maximum = 180, Width = 80 };
    private readonly NumericUpDown _maxSec = new() { Minimum = 10, Maximum = 180, Width = 80 };
    private readonly Label _toolStatus = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(520, 0) };
    private readonly FancyButton _downloadTools = new() { Text = "Download missing tools", AutoSize = true };
    private readonly FancyButton _updateYtDlp = new() { Text = "Update yt-dlp", AutoSize = true };
    // galiluna (Shorts API): environment, personal API key, test
    private readonly ComboBox _glEnv = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _glUrl = new() { Width = 420 };
    private readonly TextBox _glKey = new() { UseSystemPasswordChar = true, Width = 420, PlaceholderText = "glk_..." };
    private readonly FancyButton _glTest = new() { Text = "Test connection", AutoSize = true };
    private readonly Label _glStatus = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);
        ClientSize = new Size(620, 760);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        void Add(string label, Control c)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 4) }, 0, row);
            c.Margin = new Padding(0, 5, 0, 3);
            table.Controls.Add(c, 1, row++);
        }
        Control WithBrowse(TextBox tb)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var b = new FancyButton { Text = "...", Width = 36 };
            b.Click += (_, _) =>
            {
                using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(tb.Text) ? tb.Text : "" };
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
            };
            p.Controls.Add(tb); p.Controls.Add(b);
            return p;
        }

        Add("Anthropic API key", _apiKey);
        Add("", new Label { Text = "Stored encrypted with Windows DPAPI for your user. Leave empty to use the ANTHROPIC_API_KEY environment variable.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) });
        Add("Claude model", _model);
        Add("Whisper model", _whisperModel);
        Add("Spoken language", _language);
        Add("", new Label { Text = "Auto-detect can guess wrong on short or noisy clips. Pick English or Portuguese to force it.", AutoSize = true, ForeColor = Color.DimGray });
        Add("Suggestions", _count);
        Add("Min short (s)", _minSec);
        Add("Max short (s)", _maxSec);
        Add("Download folder", WithBrowse(_downloadFolder));
        Add("Shorts folder", WithBrowse(_outputFolder));
        Add("Tools folder", WithBrowse(_toolsFolder));

        var toolsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        toolsRow.Controls.Add(_downloadTools); toolsRow.Controls.Add(_updateYtDlp);
        Add("", toolsRow);
        Add("", _toolStatus);

        // ---- galiluna: publish shorts to the accounts connected on galiluna.com ----
        Add("", new Label { Text = "galiluna", AutoSize = true, Font = Theme.Body(10.5f), ForeColor = Theme.Heading, Margin = new Padding(0, 14, 0, 0) });
        Add("", new Label { Text = "Publish shorts to the Instagram and TikTok accounts connected on galiluna. Sign in to galiluna in your browser, open the account menu, Connected apps, create a key and paste it here. No password is stored.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) });
        Add("Environment", _glEnv);
        Add("Address", _glUrl);
        Add("API key", _glKey);
        var glRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        glRow.Controls.Add(_glTest);
        Add("", glRow);
        Add("", _glStatus);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40 };
        var ok = new FancyButton { Text = "Save", Width = 90, DialogResult = DialogResult.OK };
        var cancel = new FancyButton { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(table);
        Controls.Add(buttons);

        foreach (var m in Transcriber.ModelNames) _whisperModel.Items.Add(Transcriber.DescribeModel(m));

        _apiKey.Text = SettingsStore.GetApiKey(settings) is { } key && !string.IsNullOrEmpty(settings.ProtectedApiKey) ? key : "";
        _model.Text = settings.ClaudeModel;
        _whisperModel.SelectedIndex = Math.Max(0, Array.IndexOf(Transcriber.ModelNames, settings.WhisperModel));
        _language.Items.AddRange(LanguageOptions.DisplayNames);
        _language.SelectedIndex = LanguageOptions.IndexOfCode(settings.WhisperLanguage);
        _count.Value = Math.Clamp(settings.SuggestionCount, 1, 20);
        _minSec.Value = Math.Clamp(settings.MinShortSeconds, 5, 180);
        _maxSec.Value = Math.Clamp(settings.MaxShortSeconds, 10, 180);
        _downloadFolder.Text = settings.DownloadFolder;
        _outputFolder.Text = settings.OutputFolder;
        _toolsFolder.Text = settings.ToolsFolder;

        _glEnv.Items.AddRange(new object[] { "Production (galiluna.com)", "Development (torontodeveloper.ca)", "Custom address" });
        _glUrl.Text = string.IsNullOrWhiteSpace(settings.GaliLunaBaseUrl) ? GaliLunaClient.PrdBaseUrl : settings.GaliLunaBaseUrl;
        _glEnv.SelectedIndex = _glUrl.Text.TrimEnd('/') == GaliLunaClient.PrdBaseUrl ? 0 : _glUrl.Text.TrimEnd('/') == GaliLunaClient.DevBaseUrl ? 1 : 2;
        _glUrl.ReadOnly = _glEnv.SelectedIndex != 2;
        _glEnv.SelectedIndexChanged += (_, _) =>
        {
            if (_glEnv.SelectedIndex == 0) _glUrl.Text = GaliLunaClient.PrdBaseUrl;
            else if (_glEnv.SelectedIndex == 1) _glUrl.Text = GaliLunaClient.DevBaseUrl;
            _glUrl.ReadOnly = _glEnv.SelectedIndex != 2;
        };
        _glKey.Text = SettingsStore.GetGaliLunaKey(settings) ?? "";
        _glTest.Click += async (_, _) => await TestGaliLunaAsync();

        _downloadTools.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => await loc.DownloadMissingToolsAsync(log, CancellationToken.None));
        _updateYtDlp.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => log.Report(await loc.UpdateYtDlpAsync(CancellationToken.None)));
        _toolsFolder.TextChanged += (_, _) => RefreshToolStatus();
        ok.Click += (_, _) => ApplyToSettings();
        RefreshToolStatus();
        Theme.Primary(ok);
        Theme.Apply(this);
    }

    private void RefreshToolStatus()
    {
        var loc = new ToolLocator(_toolsFolder.Text);
        _toolStatus.Text =
            $"yt-dlp: {(loc.YtDlpPath ?? "NOT FOUND")}\n" +
            $"ffmpeg: {(loc.FfmpegPath ?? "NOT FOUND")}\n" +
            $"ffprobe: {(loc.FfprobePath ?? "NOT FOUND")}";
    }

    private async Task RunToolActionAsync(Func<ToolLocator, IProgress<string>, Task> action)
    {
        _downloadTools.Enabled = _updateYtDlp.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var loc = new ToolLocator(_toolsFolder.Text);
            var log = new Progress<string>(s => _toolStatus.Text = s);
            await action(loc, log);
            var last = _toolStatus.Text;
            RefreshToolStatus();
            _toolStatus.Text = last + "\n\n" + _toolStatus.Text;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Tool download failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _downloadTools.Enabled = _updateYtDlp.Enabled = true;
        }
    }

    private async Task TestGaliLunaAsync()
    {
        if (string.IsNullOrWhiteSpace(_glKey.Text)) { _glStatus.Text = "Paste the API key first."; _glStatus.ForeColor = Theme.Warning; return; }
        _glTest.Enabled = false;
        _glStatus.ForeColor = Color.DimGray;
        _glStatus.Text = "Contacting galiluna...";
        try
        {
            using var client = new GaliLunaClient(_glUrl.Text.Trim(), _glKey.Text);
            var accounts = await client.GetAccountsAsync(CancellationToken.None);
            _glStatus.Text = accounts.Summary;
            _glStatus.ForeColor = Theme.Success;
        }
        catch (Exception ex)
        {
            _glStatus.Text = ex.Message;
            _glStatus.ForeColor = Theme.Danger;
        }
        finally { _glTest.Enabled = true; }
    }

    private void ApplyToSettings()
    {
        SettingsStore.SetApiKey(_settings, _apiKey.Text);
        SettingsStore.SetGaliLunaKey(_settings, _glKey.Text);
        _settings.GaliLunaBaseUrl = _glUrl.Text.Trim().TrimEnd('/');
        _settings.ClaudeModel = _model.Text.Trim();
        _settings.WhisperModel = Transcriber.ModelNames[Math.Max(0, _whisperModel.SelectedIndex)];
        _settings.WhisperLanguage = LanguageOptions.CodeAt(_language.SelectedIndex);
        // DetectReactions is edited on the Transcript tab and carried over unchanged here.
        _settings.SuggestionCount = (int)_count.Value;
        _settings.MinShortSeconds = (int)_minSec.Value;
        _settings.MaxShortSeconds = (int)Math.Max(_maxSec.Value, _minSec.Value + 5);
        _settings.DownloadFolder = _downloadFolder.Text.Trim();
        _settings.OutputFolder = _outputFolder.Text.Trim();
        _settings.ToolsFolder = _toolsFolder.Text.Trim();
        SettingsStore.Save(_settings);
    }
}
