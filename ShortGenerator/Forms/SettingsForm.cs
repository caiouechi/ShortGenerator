using System.Drawing.Drawing2D;
using ShortGenerator.Forms.Controls;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// Settings as a quiet page of cards: Account (who is signed in on galiluna and which networks are connected),
/// AI, Transcription, Suggestions, Folders, Tools and Storage. Each card has a title, one line that says what it is
/// for, and its fields; states are shown as small pills and dots, never as a sentence to decode.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _apiKey = new() { UseSystemPasswordChar = true, Width = 360 };
    private readonly FancyButton _apiKeyShow = new() { Text = "Show", Width = 70, Height = 34, Kind = ButtonKind.Subtle };
    private readonly StatusPill _apiKeyPill = new();
    private readonly TextBox _model = new() { Width = 360 };
    private readonly ComboBox _whisperModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _downloadFolder = new() { Width = 360 };
    private readonly TextBox _outputFolder = new() { Width = 360 };
    private readonly TextBox _toolsFolder = new() { Width = 360 };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 20, Width = 64 };
    private readonly NumericUpDown _minSec = new() { Minimum = 5, Maximum = 180, Width = 64 };
    private readonly NumericUpDown _maxSec = new() { Minimum = 10, Maximum = 180, Width = 64 };

    // tools: one row per tool with a dot
    private readonly ToolRow _ytDlp = new("yt-dlp", "downloads videos");
    private readonly ToolRow _ffmpeg = new("ffmpeg", "renders the shorts");
    private readonly ToolRow _ffprobe = new("ffprobe", "reads video details");
    private readonly Label _toolNote = new() { AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(520, 0), Visible = false };
    private readonly FancyButton _downloadTools = new() { Text = "Download missing tools", IconName = "download", Width = 210 };
    private readonly FancyButton _updateYtDlp = new() { Text = "Update yt-dlp", IconName = "reset", Width = 150 };

    // galiluna account
    private readonly StatusPill _glPill = new();
    private readonly Label _glWho = new() { AutoSize = true, Font = Theme.HeadingFont(10.5f), ForeColor = Theme.Heading };
    private readonly Label _glWhere = new() { AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly FlowLayoutPanel _glNetworks = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0, 10, 0, 0), MaximumSize = new Size(CardWidth - 44, 0), MinimumSize = new Size(CardWidth - 44, 0) };
    private readonly Label _glMessage = new() { AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(540, 0), Visible = false };
    private readonly FancyButton _glSignIn = new() { Text = "Sign in", IconName = "upload", Width = 160 };
    private readonly FancyButton _glRefresh = new() { Text = "Refresh", IconName = "reset", Width = 116 };
    private readonly FancyButton _glSignOut = new() { Text = "Sign out", Width = 104, Kind = ButtonKind.Subtle };

    // storage
    private readonly Label _diskStatus = new() { AutoSize = true, ForeColor = Theme.TextSecondary, MaximumSize = new Size(520, 0) };
    private readonly FancyButton _diskClean = new() { Text = "Delete rendered shorts and thumbnails", IconName = "trash", Kind = ButtonKind.Danger, Width = 300 };

    /// <summary>Describes what the clean-up would delete (MainForm knows the projects); null hides the section.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string>? DescribeLocalFiles { get; set; }
    /// <summary>Asks and deletes; returns false when the user said no or nothing could run.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<IWin32Window, bool>? CleanLocalFiles { get; set; }

    private readonly Panel _page = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(24, 18, 24, 8), BackColor = Theme.Bg };
    private const int CardWidth = 660 - 48 - 18;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(660, 820);
        BackColor = Theme.Bg;

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
        host.Controls.Add(_page);
        // one gap between cards: each card is followed by a thin spacer panel
        void Gap() => _page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Theme.Bg });

        // ---- Account
        var account = Section("Account", "Shorts are published through the Instagram, TikTok and YouTube accounts connected on galiluna. Sign in opens your browser; no password is typed here.");
        var who = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, BackColor = Theme.Elevated };
        who.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); who.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var mark = new OrgMark { Margin = new Padding(0, 2, 14, 0) };
        _glWhere.Margin = new Padding(0, 2, 0, 0);
        var names = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
        var headline = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
        _glWho.Margin = new Padding(0, 1, 10, 0); _glPill.Margin = new Padding(0, 2, 0, 0);
        headline.Controls.Add(_glWho); headline.Controls.Add(_glPill);
        names.Controls.Add(headline); names.Controls.Add(_glWhere);
        who.Controls.Add(mark, 0, 0); who.Controls.Add(names, 1, 0);
        AddRow(account, who);
        _glNetworks.BackColor = Theme.Elevated;
        AddRow(account, _glNetworks);
        _glMessage.Margin = new Padding(0, 8, 0, 0);
        AddRow(account, _glMessage);
        AddRow(account, Row(_glSignIn, _glRefresh, _glSignOut));
        _orgMark = mark;

        // ---- AI
        var ai = Section("AI", "Claude writes the suggestions, the post texts and the translations.");
        var keyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
        keyRow.Controls.Add(Theme.WrapInput(_apiKey, 34));
        _apiKeyShow.Margin = new Padding(8, 0, 8, 0); _apiKeyPill.Margin = new Padding(0, 6, 0, 0);
        keyRow.Controls.Add(_apiKeyShow); keyRow.Controls.Add(_apiKeyPill);
        AddRow(ai, Field("Anthropic API key", keyRow, "Stored encrypted for your Windows user. Empty uses the ANTHROPIC_API_KEY environment variable."));
        AddRow(ai, Field("Claude model", Theme.WrapInput(_model, 34)));

        // ---- Transcription
        var tr = Section("Transcription", "Whisper runs on this computer; larger models are more accurate and slower.");
        AddRow(tr, Field("Whisper model", _whisperModel));
        AddRow(tr, Field("Spoken language", _language, "Auto-detect can guess wrong on short or noisy clips; pick the language to force it."));

        // ---- Suggestions
        var sg = Section("Suggestions", "How many moments Claude proposes per video, and how long a short may be.");
        var lengths = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
        void Word(string t) => lengths.Controls.Add(new Label { Text = t, AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 7, 8, 0), BackColor = Theme.Elevated });
        Word("Up to"); _count.Margin = new Padding(0, 3, 8, 0); lengths.Controls.Add(_count);
        Word("shorts, each between"); _minSec.Margin = new Padding(0, 3, 8, 0); lengths.Controls.Add(_minSec);
        Word("and"); _maxSec.Margin = new Padding(0, 3, 8, 0); lengths.Controls.Add(_maxSec);
        Word("seconds");
        AddRow(sg, lengths);

        // ---- Folders
        var fo = Section("Folders", "Where downloads, rendered shorts and the helper tools live.");
        AddRow(fo, Field("Downloads", Browse(_downloadFolder)));
        AddRow(fo, Field("Shorts", Browse(_outputFolder)));
        AddRow(fo, Field("Tools", Browse(_toolsFolder)));

        // ---- Tools
        var tools = Section("Tools", "The three programs the app runs for you.");
        AddRow(tools, _ytDlp); AddRow(tools, _ffmpeg); AddRow(tools, _ffprobe);
        _toolNote.Margin = new Padding(0, 6, 0, 0);
        AddRow(tools, _toolNote);
        AddRow(tools, Row(_downloadTools, _updateYtDlp));

        // ---- Storage
        var st = Section("Storage", "Rendered shorts, exported covers, thumbnail images and temporary files on this computer. Downloads, transcripts and suggestions stay.");
        _diskStatus.Margin = new Padding(0, 0, 0, 2);
        AddRow(st, _diskStatus);
        AddRow(st, Row(_diskClean));

        // docked Top: the last one added sits at the top, so the order is reversed
        foreach (var c in new Control[] { st, tools, fo, sg, tr, ai, account }) { _page.Controls.Add(c); Gap(); }

        // footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Elevated, Padding = new Padding(24, 14, 24, 14) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Theme.Elevated };
        var ok = new FancyButton { Text = "Save", Width = 110, DialogResult = DialogResult.OK, Kind = ButtonKind.Primary, Margin = Padding.Empty };
        var cancel = new FancyButton { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 10, 0) };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons);
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(host);
        Controls.Add(footer);

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

        _apiKeyShow.Click += (_, _) => { _apiKey.UseSystemPasswordChar = !_apiKey.UseSystemPasswordChar; _apiKeyShow.Text = _apiKey.UseSystemPasswordChar ? "Show" : "Hide"; };
        _apiKey.TextChanged += (_, _) => ShowKeyState();
        _glSignIn.Click += async (_, _) =>
        {
            using var dlg = new GaliLunaSignInForm(_settings);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Accounts is { } a) ShowAccounts(a, _settings.GaliLunaBaseUrl);
            else await RefreshGaliLunaAsync();
        };
        _glRefresh.Click += async (_, _) => await RefreshGaliLunaAsync();
        _glSignOut.Click += async (_, _) => await SignOutGaliLunaAsync();
        _diskClean.Click += (_, _) => { if (CleanLocalFiles is null) return; CleanLocalFiles(this); ShowDiskStatus(); };
        _downloadTools.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => await loc.DownloadMissingToolsAsync(log, CancellationToken.None));
        _updateYtDlp.Click += async (_, _) => await RunToolActionAsync(async (loc, log) => log.Report(await loc.UpdateYtDlpAsync(CancellationToken.None)));
        _toolsFolder.TextChanged += (_, _) => RefreshToolStatus();
        ok.Click += (_, _) => ApplyToSettings();
        Shown += (_, _) => { ShowDiskStatus(); host.AutoScrollPosition = Point.Empty; ActiveControl = null; };

        Theme.Apply(this);
        ShowKeyState();
        RefreshToolStatus();
        ShowSignedOut();
        _ = RefreshGaliLunaAsync();
    }

    private OrgMark _orgMark = null!;

    // ------------------------------------------------------------------ building blocks

    /// <summary>A card with a title and a line saying what the section is for; rows stack below it.</summary>
    private static Card Section(string title, string about)
    {
        var card = new Card { Width = CardWidth, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(20, 18, 20, 18), Margin = Padding.Empty };
        // a spacer below each card, carried by the card itself (docked controls ignore Margin)
        var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Theme.Elevated, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(body);
        card.Tag = body;
        AddRow(card, new Label { Text = title, AutoSize = true, Font = Theme.HeadingFont(11f), ForeColor = Theme.Heading, Margin = new Padding(0, 0, 0, 2), BackColor = Theme.Elevated });
        AddRow(card, new Label { Text = about, AutoSize = true, Font = Theme.Body(8.5f), ForeColor = Theme.TextMuted, MaximumSize = new Size(CardWidth - 44, 0), Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Elevated });
        return card;
    }

    /// <summary>Adds one row to a section card.</summary>
    private static void AddRow(Card card, Control row)
    {
        var body = (TableLayoutPanel)card.Tag!;
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        body.Controls.Add(row, 0, body.RowCount++);
    }

    /// <summary>Label above the field, an optional note below, all left aligned.</summary>
    private static Control Field(string label, Control input, string? note = null)
    {
        var box = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 12), BackColor = Theme.Elevated };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Add(Control c) { box.RowStyles.Add(new RowStyle(SizeType.AutoSize)); c.Anchor = AnchorStyles.Left | AnchorStyles.Top; box.Controls.Add(c, 0, box.RowCount++); }
        Add(new Label { Text = label, AutoSize = true, Font = Theme.Body(8.5f, FontStyle.Bold), ForeColor = Theme.TextMuted, Margin = new Padding(0, 0, 0, 5), BackColor = Theme.Elevated });
        input.Margin = Padding.Empty;
        Add(input);
        if (note is not null) Add(new Label { Text = note, AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.Body(8.5f), MaximumSize = new Size(CardWidth - 44, 0), Margin = new Padding(0, 6, 0, 0), BackColor = Theme.Elevated });
        return box;
    }

    private static FlowLayoutPanel Row(params Control[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0), BackColor = Theme.Elevated };
        foreach (var c in items) { c.Margin = new Padding(0, 0, 8, 0); row.Controls.Add(c); }
        return row;
    }

    private Control Browse(TextBox tb)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
        var pick = new FancyButton { Text = "Change...", Width = 100, Height = 34, Margin = new Padding(8, 0, 0, 0) };
        pick.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(tb.Text) ? tb.Text : "" };
            if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
        };
        p.Controls.Add(Theme.WrapInput(tb, 34)); p.Controls.Add(pick);
        return p;
    }

    // ------------------------------------------------------------------ states

    private void ShowKeyState()
    {
        if (_apiKey.Text.Length > 0) _apiKeyPill.Set("Key set", StatusPill.Tone.Good);
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) _apiKeyPill.Set("From environment", StatusPill.Tone.Good);
        else _apiKeyPill.Set("Not set", StatusPill.Tone.Quiet);
    }

    private void RefreshToolStatus()
    {
        var loc = new ToolLocator(_toolsFolder.Text);
        _ytDlp.Set(loc.YtDlpPath); _ffmpeg.Set(loc.FfmpegPath); _ffprobe.Set(loc.FfprobePath);
        _downloadTools.Enabled = loc.YtDlpPath is null || loc.FfmpegPath is null || loc.FfprobePath is null;
    }

    private async Task RunToolActionAsync(Func<ToolLocator, IProgress<string>, Task> action)
    {
        _downloadTools.Enabled = _updateYtDlp.Enabled = false;
        UseWaitCursor = true;
        _toolNote.Visible = true;
        try
        {
            var loc = new ToolLocator(_toolsFolder.Text);
            var log = new Progress<string>(s => _toolNote.Text = s);
            await action(loc, log);
            RefreshToolStatus();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, ex.Message, "Tool download failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _updateYtDlp.Enabled = true;
            RefreshToolStatus();
        }
    }

    private void ShowDiskStatus()
    {
        if (DescribeLocalFiles is null) { _diskStatus.Text = ""; _diskClean.Enabled = false; return; }
        _diskStatus.Text = "Measuring...";
        try
        {
            var text = DescribeLocalFiles();
            _diskStatus.Text = text;
            _diskClean.Enabled = !text.StartsWith("Nothing", StringComparison.Ordinal);
        }
        catch (Exception ex) { _diskStatus.Text = ex.Message; _diskClean.Enabled = false; }
    }

    private void ShowSignedOut(string? message = null, StatusPill.Tone tone = StatusPill.Tone.Quiet)
    {
        _glWho.Text = "Not signed in";
        _glWhere.Text = "Sign in to publish from this app.";
        _glPill.Set(tone == StatusPill.Tone.Warn ? "Needs sign-in" : "Signed out", tone);
        _glNetworks.Controls.Clear(); _glNetworks.Visible = false;
        _glMessage.Text = message ?? ""; _glMessage.Visible = message is not null; _glMessage.ForeColor = tone == StatusPill.Tone.Bad ? Theme.Danger : Theme.TextMuted;
        _glSignIn.Kind = ButtonKind.Primary; _glSignIn.Text = "Sign in";
        _glRefresh.Enabled = false; _glSignOut.Enabled = SettingsStore.GetGaliLunaKey(_settings) is not null;
        _orgMark.Set("");
    }

    private void ShowAccounts(GaliLunaClient.Accounts a, string baseUrl)
    {
        _glWho.Text = string.IsNullOrWhiteSpace(a.Organization.Name) ? a.User.Email : a.Organization.Name;
        _glWhere.Text = a.User.Email + "   ·   " + baseUrl.Replace("https://", "").TrimEnd('/');
        _glPill.Set("Connected", StatusPill.Tone.Good);
        _orgMark.Set(_glWho.Text);
        _glNetworks.Controls.Clear();
        foreach (var ig in a.Instagram) _glNetworks.Controls.Add(new NetworkChip("Instagram", ig.Label));
        if (a.Tiktok is { } t) _glNetworks.Controls.Add(new NetworkChip("TikTok", t.Label));
        foreach (var ch in a.Youtube.Channels) _glNetworks.Controls.Add(new NetworkChip("YouTube", ch.Label));
        if (_glNetworks.Controls.Count == 0) _glNetworks.Controls.Add(new Label { Text = "No network connected yet. Connect Instagram, TikTok or YouTube on galiluna.com.", AutoSize = true, ForeColor = Theme.TextMuted, BackColor = Theme.Elevated });
        _glNetworks.Visible = true;
        _glMessage.Visible = false;
        _glSignIn.Kind = ButtonKind.Ghost; _glSignIn.Text = "Switch account";
        _glRefresh.Enabled = true; _glSignOut.Enabled = true;
    }

    /// <summary>Re-reads the accounts. A 401 means the key was revoked or replaced on galiluna: keep it and ask to sign in again.</summary>
    private async Task RefreshGaliLunaAsync()
    {
        var client = SettingsStore.CreateGaliLunaClient(_settings);
        if (client is null) { ShowSignedOut(); return; }
        _glRefresh.Enabled = false;
        _glPill.Set("Checking...", StatusPill.Tone.Quiet);
        try
        {
            using (client)
            {
                var accounts = await client.GetAccountsAsync(CancellationToken.None);
                ShowAccounts(accounts, client.BaseUrl);
            }
        }
        catch (GaliLunaClient.GaliLunaException ex) when (ex.StatusCode == 401)
        {
            ShowSignedOut("Your galiluna key was revoked or replaced. Sign in again.", StatusPill.Tone.Warn);
        }
        catch (Exception ex)
        {
            ShowSignedOut("galiluna could not be reached: " + ex.Message, StatusPill.Tone.Bad);
            _glRefresh.Enabled = true;
        }
    }

    private async Task SignOutGaliLunaAsync()
    {
        var client = SettingsStore.CreateGaliLunaClient(_settings);
        if (client is null) { ShowSignedOut(); return; }
        _glSignOut.Enabled = false;
        _glPill.Set("Signing out...", StatusPill.Tone.Quiet);
        string? note = null;
        try
        {
            using (client) await client.SignOutAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The key is forgotten locally either way; galiluna's Connected apps page can revoke it too.
            note = "galiluna could not be reached to revoke the key (" + ex.Message + "). Signed out on this computer.";
        }
        SettingsStore.SetGaliLunaKey(_settings, null);
        try { SettingsStore.Save(_settings); } catch { }
        ShowSignedOut(note, note is null ? StatusPill.Tone.Quiet : StatusPill.Tone.Warn);
        _glSignOut.Enabled = false;
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

    // ------------------------------------------------------------------ small parts

    /// <summary>A rounded chip with a dot and a word: Connected, Signed out, Key set.</summary>
    private sealed class StatusPill : Control
    {
        public enum Tone { Quiet, Good, Warn, Bad }
        private Tone _tone;
        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body(8f, FontStyle.Bold); Height = 22; Width = 90;
        }
        public void Set(string text, Tone tone) { Text = text; _tone = tone; Width = TextRenderer.MeasureText(text, Font).Width + 30; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Elevated);
            var (fill, dot, fore) = _tone switch
            {
                Tone.Good => (ColorTranslator.FromHtml("#EAF7F0"), Theme.Success, Theme.Success),
                Tone.Warn => (ColorTranslator.FromHtml("#FFF5E6"), Theme.Warning, Theme.Warning),
                Tone.Bad => (Theme.DangerTint, Theme.Danger, Theme.Danger),
                _ => (Theme.SurfaceStrong, Theme.TextMuted, Theme.TextSecondary),
            };
            using var path = FancyButton.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 11);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var d = new SolidBrush(dot)) g.FillEllipse(d, 9, (Height - 7) / 2f, 7, 7);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(21, 0, Width - 24, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>One connected destination: the network as a small label, the account as the text.</summary>
    private sealed class NetworkChip : Control
    {
        private readonly string _network, _label;
        public NetworkChip(string network, string label)
        {
            _network = network; _label = label;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body(9f, FontStyle.Bold); Height = 30; Margin = new Padding(0, 0, 8, 8);
            using var nf = Theme.Body(7.5f, FontStyle.Bold);
            Width = TextRenderer.MeasureText(network.ToUpperInvariant(), nf).Width + TextRenderer.MeasureText(label, Font).Width + 38;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Elevated);
            using var path = FancyButton.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
            using (var b = new SolidBrush(Theme.AccentTint)) g.FillPath(b, path);
            using (var p = new Pen(Theme.AccentBorder)) g.DrawPath(p, path);
            using var nf = Theme.Body(7.5f, FontStyle.Bold);
            int nw = TextRenderer.MeasureText(_network.ToUpperInvariant(), nf).Width;
            TextRenderer.DrawText(g, _network.ToUpperInvariant(), nf, new Rectangle(12, 0, nw + 2, Height), Theme.AccentDeep, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _label, Font, new Rectangle(12 + nw + 10, 0, Width - nw - 26, Height), Theme.Heading, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>The organisation's initial in a soft purple disc.</summary>
    private sealed class OrgMark : Control
    {
        private string _initial = "";
        public OrgMark()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(44, 44);
        }
        public void Set(string name) { _initial = name.Length > 0 ? name.Trim()[..1].ToUpperInvariant() : ""; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Elevated);
            using (var b = new SolidBrush(_initial.Length > 0 ? Theme.AccentTintStrong : Theme.SurfaceStrong)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            if (_initial.Length > 0)
            {
                using var f = Theme.HeadingFont(14f);
                TextRenderer.DrawText(g, _initial, f, ClientRectangle, Theme.AccentDeep, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                var inner = ClientRectangle; inner.Inflate(-12, -12);
                Glyphs.Draw(g, "upload", inner, Theme.TextMuted, 0.9f);
            }
        }
    }

    /// <summary>One tool: a dot (found / missing), its name, what it does, and where it was found.</summary>
    private sealed class ToolRow : Control
    {
        private readonly string _name, _role;
        private string? _path;
        public ToolRow(string name, string role)
        {
            _name = name; _role = role;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30; Width = 540; Margin = new Padding(0, 0, 0, 2);
        }
        public void Set(string? path) { _path = path; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Elevated);
            bool ok = _path is not null;
            using (var d = new SolidBrush(ok ? Theme.Success : Theme.Danger)) g.FillEllipse(d, 2, (Height - 8) / 2f, 8, 8);
            using var nf = Theme.Body(9.5f, FontStyle.Bold);
            using var rf = Theme.Body(9f);
            int x = 20;
            TextRenderer.DrawText(g, _name, nf, new Rectangle(x, 0, 80, Height), Theme.Heading, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += 84;
            TextRenderer.DrawText(g, _role, rf, new Rectangle(x, 0, 150, Height), Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += 156;
            string where = ok ? Path.GetFileName(Path.GetDirectoryName(_path!) ?? "") + "\\" + Path.GetFileName(_path!) : "not found";
            TextRenderer.DrawText(g, where, rf, new Rectangle(x, 0, Width - x - 4, Height), ok ? Theme.TextSecondary : Theme.Danger, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
        }
    }
}
