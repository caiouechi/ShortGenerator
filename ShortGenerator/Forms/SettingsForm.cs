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
    // galiluna (Shorts API): signed-in status and sign in / refresh / sign out
    private readonly Label _glStatus = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) };
    private readonly FancyButton _glSignIn = new() { Text = "Sign in", Width = 120, Glyph = "" };
    private readonly FancyButton _glRefresh = new() { Text = "Refresh", Width = 110, Glyph = "" };
    private readonly FancyButton _glSignOut = new() { Text = "Sign out", Width = 110 };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);
        ClientSize = new Size(640, 780);

        // the table grows with its rows; the host scrolls when the screen is short
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
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
        Add("", new Label { Text = "Publish shorts to the Instagram, TikTok and YouTube accounts connected on galiluna. Sign in opens your browser; no password is typed into this app.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) });
        Add("Status", _glStatus);
        var glRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        glRow.Controls.Add(_glSignIn);
        glRow.Controls.Add(_glRefresh);
        glRow.Controls.Add(_glSignOut);
        Add("", glRow);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40 };
        var ok = new FancyButton { Text = "Save", Width = 90, DialogResult = DialogResult.OK };
        var cancel = new FancyButton { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        host.Controls.Add(table);
        Controls.Add(host);
        Controls.Add(buttons);
        buttons.Height = 52;
        buttons.Padding = new Padding(0, 10, 0, 0);

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

        _glSignIn.Click += async (_, _) =>
        {
            using var dlg = new GaliLunaSignInForm(_settings);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Accounts is { } a) ShowGaliLunaStatus(GaliLunaSignInForm.Summary(a) + $"  ({_settings.GaliLunaBaseUrl})", Theme.Success);
            else await RefreshGaliLunaAsync();
        };
        _glRefresh.Click += async (_, _) => await RefreshGaliLunaAsync();
        _glSignOut.Click += async (_, _) => await SignOutGaliLunaAsync();
        _ = RefreshGaliLunaAsync();

        _downloadTools.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => await loc.DownloadMissingToolsAsync(log, CancellationToken.None));
        _updateYtDlp.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => log.Report(await loc.UpdateYtDlpAsync(CancellationToken.None)));
        _toolsFolder.TextChanged += (_, _) => RefreshToolStatus();
        ok.Click += (_, _) => ApplyToSettings();
        RefreshToolStatus();
        Theme.Primary(ok);
        Theme.Primary(_glSignIn);
        Theme.Apply(this);
    }

    private void RefreshToolStatus()
    {
        var loc = new ToolLocator(_toolsFolder.Text);
        static string Short(string? p) => p is null ? "NOT FOUND" : Path.GetFileName(Path.GetDirectoryName(p) ?? "") + "\\" + Path.GetFileName(p);
        _toolStatus.Text = $"yt-dlp: {Short(loc.YtDlpPath)}   ffmpeg: {Short(loc.FfmpegPath)}   ffprobe: {Short(loc.FfprobePath)}";
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
            AppDialog.Show(this, ex.Message, "Tool download failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _downloadTools.Enabled = _updateYtDlp.Enabled = true;
        }
    }

    private void ShowGaliLunaStatus(string text, Color color)
    {
        _glStatus.Text = text;
        _glStatus.ForeColor = color;
        bool signedIn = SettingsStore.GetGaliLunaKey(_settings) is not null;
        _glRefresh.Enabled = signedIn;
        _glSignOut.Enabled = signedIn;
    }

    /// <summary>Re-reads the accounts. A 401 means the key was revoked or replaced on galiluna: keep it and ask to sign in again.</summary>
    private async Task RefreshGaliLunaAsync()
    {
        var client = SettingsStore.CreateGaliLunaClient(_settings);
        if (client is null) { ShowGaliLunaStatus("Not signed in.", Theme.TextMuted); return; }
        _glRefresh.Enabled = false;
        ShowGaliLunaStatus("Contacting galiluna...", Theme.TextMuted);
        try
        {
            using (client)
            {
                var accounts = await client.GetAccountsAsync(CancellationToken.None);
                ShowGaliLunaStatus(GaliLunaSignInForm.Summary(accounts) + $"  ({client.BaseUrl})", Theme.Success);
            }
        }
        catch (GaliLunaClient.GaliLunaException ex) when (ex.StatusCode == 401)
        {
            ShowGaliLunaStatus("Your galiluna key was revoked or replaced. Sign in again.", Theme.Warning);
        }
        catch (Exception ex)
        {
            ShowGaliLunaStatus(ex.Message, Theme.Danger);
        }
    }

    private async Task SignOutGaliLunaAsync()
    {
        var client = SettingsStore.CreateGaliLunaClient(_settings);
        if (client is null) { ShowGaliLunaStatus("Not signed in.", Theme.TextMuted); return; }
        _glSignOut.Enabled = false;
        ShowGaliLunaStatus("Signing out...", Theme.TextMuted);
        try
        {
            using (client) await client.SignOutAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The key is forgotten locally either way; galiluna's Connected apps page can revoke it too.
            ShowGaliLunaStatus("galiluna could not be reached to revoke the key (" + ex.Message + "). Signed out locally.", Theme.Warning);
        }
        SettingsStore.SetGaliLunaKey(_settings, null);
        try { SettingsStore.Save(_settings); } catch { }
        if (_glStatus.ForeColor != Theme.Warning) ShowGaliLunaStatus("Not signed in.", Theme.TextMuted);
        else { _glRefresh.Enabled = false; _glSignOut.Enabled = false; }
    }

    private void ApplyToSettings()
    {
        SettingsStore.SetApiKey(_settings, _apiKey.Text);
        // The galiluna key and address are saved by the sign-in form itself; nothing to copy here.
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
