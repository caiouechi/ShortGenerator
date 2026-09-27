using System.ComponentModel;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// Step 7, "Publish": send rendered shorts to the Instagram and TikTok accounts connected on
/// galiluna. Left: the generated files with what happened to each. Right: which accounts, how
/// TikTok should post, and the caption that goes with the selected short. Everything network
/// related goes through <see cref="GaliLunaClient"/>; this control only asks, shows, and records.
/// </summary>
public sealed class PublishPanel : UserControl
{
    // wiring supplied by MainForm
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<GaliLunaClient?> ClientFactory { get; set; } = () => null;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyList<GeneratedFile>> Files { get; set; } = () => Array.Empty<GeneratedFile>();
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action Saved { get; set; } = () => { };
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string> Log { get; set; } = _ => { };
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action OpenSettings { get; set; } = () => { };
    /// <summary>Opens the "Sign in with galiluna" form; returns true when a key was stored.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<bool> OpenSignIn { get; set; } = () => false;

    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false, MultiSelect = false };
    private readonly Label _connection = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 6, 10, 0), ForeColor = Color.DimGray };
    private readonly LinkLabel _refreshLink = new() { Text = "Refresh", AutoSize = true, LinkColor = Theme.Link, ActiveLinkColor = Theme.PurpleDeep, VisitedLinkColor = Theme.Link, LinkBehavior = LinkBehavior.HoverUnderline };
    private readonly FancyButton _refresh = new() { Text = "Refresh accounts", Width = 150 };
    private readonly FancyButton _settings = new() { Text = "galiluna settings", Width = 150 };
    // shown instead of the account lists until the user is signed in
    private readonly Panel _gate = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly EmptyState _gateEmpty = new("empty-editor.png", "Sign in with galiluna to publish", "Your browser opens galiluna, you approve this app, and your connected Instagram, TikTok and YouTube accounts appear here. No password is typed into this app.");
    private readonly FancyButton _gateSignIn = new() { Text = "Sign in with galiluna", Width = 240, Height = 40, Glyph = "" };
    private Control? _content;
    private readonly FlowLayoutPanel _instagram = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private readonly FlowLayoutPanel _youtube = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private readonly ComboBox _youtubePrivacy = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Label _youtubeHint = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(300, 0) };
    private readonly ComboBox _tiktokMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _tiktokPrivacy = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Enabled = false };
    private readonly Label _tiktokHint = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(300, 0) };
    private readonly ComboBox _textFor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly Label _textForHint = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(300, 0) };
    private readonly PictureBox _cover = new() { Width = 72, Height = 128, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(19, 24, 52) };
    private readonly TextBox _title = new() { Width = 300 };
    private readonly TextBox _caption = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 300, Height = 120 };
    private readonly TextBox _hashtags = new() { Width = 300, PlaceholderText = "tag1 tag2 tag3 (without #)" };
    private readonly Label _titleLabel = new() { Text = "Title", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
    private readonly Label _captionLabel = new() { Text = "Caption", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
    private readonly Label _tagsLabel = new() { Text = "Hashtags", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
    private readonly Label _captionCount = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly FancyButton _publish = new() { Text = "Publish selected shorts", Width = 300, Enabled = false };
    private readonly Label _outcome = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(300, 0) };
    private readonly EmptyState _empty = new("empty-shorts.png", "Nothing to publish yet", "Generate shorts first. They show up here, ready to send to the accounts you connected on galiluna.");

    private GaliLunaClient.Accounts? _accounts;
    private readonly Dictionary<CheckBox, int> _instagramBoxes = new();
    private readonly Dictionary<CheckBox, int> _youtubeBoxes = new();
    private GeneratedFile? _editing;
    private bool _loadingCaption;

    public PublishPanel()
    {
        Dock = DockStyle.Fill;

        _list.Columns.Add("Short", 260);
        _list.Columns.Add("Rendered", 110);
        _list.Columns.Add("Published", 420);

        var listHost = new Panel { Dock = DockStyle.Fill };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_empty);
        listHost.Controls.Add(new Label { Text = "Tick the shorts to publish (double-click to play)", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, Padding = new Padding(4, 0, 0, 0) });
        _empty.BringToFront();

        var options = new TableLayoutPanel { Dock = DockStyle.Right, Width = 440, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Add(string label, Control c) => AddL(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 4) }, c);
        void AddL(Label label, Control c)
        {
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.Controls.Add(label, 0, row);
            c.Margin = new Padding(0, 5, 0, 3);
            options.Controls.Add(c, 1, row++);
        }
        var connRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        connRow.Controls.Add(_refresh); connRow.Controls.Add(_settings);
        Add("galiluna", connRow);
        Add("Instagram", _instagram);
        Add("YouTube", _youtube);
        Add("YouTube privacy", _youtubePrivacy);
        Add("", _youtubeHint);
        Add("TikTok", _tiktokMode);
        Add("Visibility", _tiktokPrivacy);
        Add("", _tiktokHint);
        Add("Cover", _cover);
        Add("Text for", _textFor);
        Add("", _textForHint);
        AddL(_titleLabel, _title);
        AddL(_captionLabel, _caption);
        Add("", _captionCount);
        AddL(_tagsLabel, _hashtags);
        Add("", _publish);
        Add("", _outcome);

        // signed-in content vs sign-in gate
        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(listHost);
        content.Controls.Add(options);
        _content = content;
        _gate.Controls.Add(_gateEmpty);
        var gateButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 20, 0, 0) };
        gateButtons.Controls.Add(_gateSignIn);
        _gate.Controls.Add(gateButtons);
        _gate.Resize += (_, _) => gateButtons.Padding = new Padding(Math.Max(0, (_gate.Width - _gateSignIn.Width) / 2), 20, 0, 0);
        Controls.Add(content);
        Controls.Add(_gate);
        // header: status line + Refresh link
        _connection.Controls.Add(_refreshLink);
        _refreshLink.Location = new Point(10, 24);
        _connection.Height = 48;
        Controls.Add(_connection);
        Theme.Primary(_gateSignIn);
        _gateSignIn.Click += async (_, _) => { if (OpenSignIn()) await LoadAccountsAsync(); };
        _refreshLink.LinkClicked += async (_, _) => await LoadAccountsAsync();

        _tiktokMode.Items.AddRange(new object[] { "Do not post to TikTok", "Send to my TikTok drafts (finish in the app)", "Post directly to TikTok" });
        _tiktokMode.SelectedIndex = 1;
        _tiktokMode.SelectedIndexChanged += (_, _) => { _tiktokPrivacy.Enabled = _tiktokMode.SelectedIndex == 2; UpdateHint(); };
        _refresh.Click += async (_, _) => await LoadAccountsAsync();
        _settings.Click += (_, _) => OpenSettings();
        _list.ItemChecked += (_, _) => UpdatePublishEnabled();
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is GeneratedFile f && File.Exists(f.Path)) TryOpen(f.Path); };
        _textFor.Items.AddRange(new object[] { "Same text for every network", "YouTube (title, description, keywords)", "TikTok (caption, hashtags)", "Instagram (caption, hashtags)" });
        _textFor.SelectedIndex = 0;
        _textFor.SelectedIndexChanged += (_, _) => ShowSelected();
        _caption.TextChanged += (_, _) =>
        {
            _captionCount.Text = $"{_caption.Text.Length} / {(CurrentNetwork == "tiktok" ? 2200 : CurrentNetwork == "youtube" ? 5000 : 2200)}";
            if (_loadingCaption || _editing is null) return;
            if (CurrentNetwork is null) _editing.Caption = _caption.Text; else EditingPost().Description = _caption.Text;
        };
        _hashtags.TextChanged += (_, _) =>
        {
            if (_loadingCaption || _editing is null) return;
            if (CurrentNetwork is null) _editing.Hashtags = SplitTags(_hashtags.Text); else EditingPost().Tags = SplitTags(_hashtags.Text);
        };
        _title.TextChanged += (_, _) =>
        {
            if (_loadingCaption || _editing is null) return;
            if (CurrentNetwork is null) _editing.Title = _title.Text; else EditingPost().Title = _title.Text;
        };
        _publish.Click += async (_, _) => await PublishAsync();
        Theme.Primary(_publish);
    }

    /// <summary>Called by MainForm when the tab is shown and after every generation.</summary>
    public async Task RefreshAsync()
    {
        RefreshList();
        if (_accounts is null) await LoadAccountsAsync();
    }

    public void RefreshList()
    {
        var selectedPath = _list.SelectedItems.Count == 1 ? (_list.SelectedItems[0].Tag as GeneratedFile)?.Path : null;
        _populating = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var f in Files())
        {
            var item = new ListViewItem(new[] { f.Title, File.Exists(f.Path) ? f.When.ToString("MMM d HH:mm") : "file missing", Describe(f) }) { Tag = f };
            item.Checked = !f.Sent && File.Exists(f.Path);
            if (!File.Exists(f.Path)) item.ForeColor = Color.Gray;
            _list.Items.Add(item);
            if (f.Path == selectedPath) item.Selected = true;
        }
        _list.EndUpdate();
        _populating = false;
        _empty.Visible = _list.Items.Count == 0;
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        UpdatePublishEnabled();
    }

    private bool _populating;

    private static string Describe(GeneratedFile f)
    {
        if (!f.Sent) return "not sent";
        if (f.Publications.Count == 0) return $"sent (#{f.GaliLunaShortId})";
        return string.Join("  ·  ", f.Publications.Select(p =>
            (p.Account ?? p.Network) + ": " + p.Status switch
            {
                "published" => "live",
                "drafted" => "in TikTok drafts",
                "processing" => "TikTok working...",
                _ => "failed",
            }));
    }

    private async Task LoadAccountsAsync()
    {
        var client = ClientFactory();
        _instagram.Controls.Clear(); _instagramBoxes.Clear();
        _youtube.Controls.Clear(); _youtubeBoxes.Clear();
        if (client is null)
        {
            _accounts = null;
            SetGate(true, "Not signed in.");
            UpdatePublishEnabled();
            return;
        }
        _refresh.Enabled = false;
        _connection.Text = "Asking galiluna which accounts are connected...";
        _connection.ForeColor = Color.DimGray;
        try
        {
            _accounts = await client.GetAccountsAsync(CancellationToken.None);
            SetGate(false, GaliLunaSignInForm.Summary(_accounts) + $"  ({client.BaseUrl})");
            _connection.ForeColor = Theme.Success;
            foreach (var a in _accounts.Instagram)
            {
                var box = new CheckBox { Text = a.Label, AutoSize = true, Checked = true, Margin = new Padding(0, 2, 0, 2) };
                box.CheckedChanged += (_, _) => UpdatePublishEnabled();
                _instagram.Controls.Add(box);
                _instagramBoxes[box] = a.Id;
            }
            if (_accounts.Instagram.Count == 0)
                _instagram.Controls.Add(new Label { Text = "No Instagram account connected on galiluna.", AutoSize = true, ForeColor = Color.DimGray });
            foreach (var c in _accounts.Youtube.Channels)
            {
                var box = new CheckBox { Text = c.Label, AutoSize = true, Checked = true, Margin = new Padding(0, 2, 0, 2) };
                box.CheckedChanged += (_, _) => UpdatePublishEnabled();
                _youtube.Controls.Add(box);
                _youtubeBoxes[box] = c.Id;
            }
            if (_accounts.Youtube.Channels.Count == 0)
                _youtube.Controls.Add(new Label { Text = "No YouTube channel connected on galiluna.", AutoSize = true, ForeColor = Color.DimGray });
            _youtubePrivacy.Items.Clear();
            foreach (var level in _accounts.Youtube.PrivacyLevels.Count > 0 ? _accounts.Youtube.PrivacyLevels : new List<string> { "public", "unlisted", "private" })
                _youtubePrivacy.Items.Add(level);
            _youtubePrivacy.SelectedIndex = 0;
            _youtubePrivacy.Enabled = _accounts.Youtube.Channels.Count > 0;
            _youtubeHint.Text = _accounts.Youtube.Channels.Count > 0 ? (_accounts.Youtube.Note ?? "") : "";
            _tiktokMode.Enabled = _accounts.Tiktok is not null;
            if (_accounts.Tiktok is null) _tiktokMode.SelectedIndex = 0;
            _tiktokPrivacy.Items.Clear();
            foreach (var level in _accounts.Tiktok?.PrivacyLevels ?? new List<string>()) _tiktokPrivacy.Items.Add(level);
            if (_tiktokPrivacy.Items.Count > 0) _tiktokPrivacy.SelectedIndex = 0;
            UpdateHint();
        }
        catch (GaliLunaClient.GaliLunaException ex) when (ex.StatusCode == 401)
        {
            // Key revoked or replaced on galiluna: keep it locally, but gate publishing until the user signs in again.
            _accounts = null;
            SetGate(true, "Your galiluna key was revoked or replaced. Sign in again.");
            _gateEmpty.Set("Sign in with galiluna again", "The key this app holds was revoked or replaced on galiluna. Sign in again to reconnect your accounts.");
            Log("galiluna: key rejected (401); sign in again.");
        }
        catch (Exception ex)
        {
            _accounts = null;
            SetGate(false, ex.Message);
            _connection.ForeColor = Theme.Danger;
            Log("galiluna: " + ex.Message);
        }
        finally
        {
            _refresh.Enabled = true;
            UpdatePublishEnabled();
        }
    }

    /// <summary>Shows either the sign-in gate or the account lists, with the status line above both.</summary>
    private void SetGate(bool gated, string status)
    {
        _connection.Text = status;
        _connection.ForeColor = gated ? Theme.Warning : Theme.TextMuted;
        _gate.Visible = gated;
        if (_content is not null) _content.Visible = !gated;
        _refreshLink.Visible = !gated;
        if (gated) _gate.BringToFront();
    }

    private void UpdateHint()
    {
        _tiktokHint.Text = _tiktokMode.SelectedIndex switch
        {
            1 => "The video lands in your TikTok inbox; you choose visibility and post it in the TikTok app. Works for every account.",
            2 => _accounts?.Tiktok is { DirectPostingAvailable: false }
                ? "Direct posting on this galiluna environment only reaches the profile as SELF_ONLY until TikTok clears it."
                : "Posted straight to the profile with the visibility above.",
            _ => "",
        };
    }

    /// <summary>null = the generic text; otherwise "youtube" | "tiktok" | "instagram".</summary>
    private string? CurrentNetwork => _textFor.SelectedIndex switch { 1 => "youtube", 2 => "tiktok", 3 => "instagram", _ => null };

    /// <summary>The per-network post being edited, created from the generic text on first edit.</summary>
    private NetworkPost EditingPost()
    {
        var f = _editing!;
        switch (CurrentNetwork)
        {
            case "youtube": return f.Youtube ??= f.PostFor("youtube");
            case "tiktok": return f.Tiktok ??= f.PostFor("tiktok");
            default: return f.Instagram ??= f.PostFor("instagram");
        }
    }

    private void ShowSelected()
    {
        _editing = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as GeneratedFile : null;
        _loadingCaption = true;
        var net = CurrentNetwork;
        bool yt = net == "youtube";
        _titleLabel.Text = yt ? "Title" : "Title";
        _captionLabel.Text = yt ? "Description" : "Caption";
        _tagsLabel.Text = yt ? "Keywords" : "Hashtags";
        _hashtags.PlaceholderText = yt ? "keyword one, keyword two" : "tag1 tag2 tag3 (without #)";
        if (_editing is null)
        {
            _title.Text = _caption.Text = _hashtags.Text = "";
            _textForHint.Text = "";
        }
        else if (net is null)
        {
            _title.Text = _editing.Title;
            _caption.Text = _editing.Caption ?? "";
            _hashtags.Text = _editing.Hashtags is { } tags ? string.Join(" ", tags) : "";
            var have = new[] { (_editing.Youtube, "YouTube"), (_editing.Tiktok, "TikTok"), (_editing.Instagram, "Instagram") }.Where(p => p.Item1 is not null).Select(p => p.Item2).ToList();
            _textForHint.Text = have.Count > 0
                ? $"This short also has tailored text for {string.Join(", ", have)}. Each network receives its own version; this generic text is the fallback."
                : "No per-network text yet. Tick YouTube / TikTok / Instagram on the Ask ChatGPT step to get tailored titles, descriptions and keywords.";
        }
        else
        {
            var p = net switch { "youtube" => _editing.Youtube, "tiktok" => _editing.Tiktok, _ => _editing.Instagram };
            var shown = p ?? _editing.PostFor(net);
            _title.Text = shown.Title;
            _caption.Text = shown.Description;
            _hashtags.Text = string.Join(yt ? ", " : " ", shown.Tags);
            _textForHint.Text = p is null ? "Showing the generic text; start typing to create a version just for this network." : "This text is used only on this network.";
        }
        LoadCover(_editing?.CoverPath);
        _loadingCaption = false;
        _outcome.Text = _editing is null ? "" : string.Join("\n", _editing.Publications.Where(p => !string.IsNullOrWhiteSpace(p.Error)).Select(p => $"{p.Account ?? p.Network}: {p.Error}"));
        _outcome.ForeColor = Theme.Danger;
    }

    private void LoadCover(string? path)
    {
        var old = _cover.Image;
        _cover.Image = null;
        old?.Dispose();
        if (path is null || !File.Exists(path)) return;
        try
        {
            using var fs = File.OpenRead(path);
            _cover.Image = Image.FromStream(fs);
        }
        catch { }
    }

    private void UpdatePublishEnabled()
    {
        if (_populating) return; // ItemChecked fires mid-population, when the collection can still hand out nulls
        var anyTicked = _list.Items.Cast<ListViewItem>().Any(i => i is { Checked: true });
        var anyTarget = _instagramBoxes.Keys.Any(b => b.Checked) || _youtubeBoxes.Keys.Any(b => b.Checked)
            || (_tiktokMode.SelectedIndex > 0 && _accounts?.Tiktok is not null);
        _publish.Enabled = _accounts is not null && anyTicked && anyTarget && !_busy;
    }

    private bool _busy;

    private async Task PublishAsync()
    {
        var client = ClientFactory();
        if (client is null || _accounts is null) return;
        var files = _list.Items.Cast<ListViewItem>().Where(i => i is { Checked: true }).Select(i => (ListViewItem: i, File: (GeneratedFile)i.Tag!)).ToList();
        var instagramIds = _instagramBoxes.Where(kv => kv.Key.Checked).Select(kv => kv.Value).ToList();
        var tiktokMode = _tiktokMode.SelectedIndex switch { 1 => "drafts", 2 => "direct", _ => "off" };
        var privacy = _tiktokPrivacy.SelectedItem as string;
        var youtubeIds = _youtubeBoxes.Where(kv => kv.Key.Checked).Select(kv => kv.Value).ToList();
        var youtubePrivacy = _youtubePrivacy.SelectedItem as string ?? "public";

        var already = files.Where(f => f.File.Sent).Select(f => f.File.Title).ToList();
        var where = string.Join(", ", instagramIds.Select(id => _accounts.Instagram.First(a => a.Id == id).Label)
            .Concat(youtubeIds.Select(id => "YouTube " + _accounts.Youtube.Channels.First(c => c.Id == id).Label + " (" + youtubePrivacy + ")"))
            .Concat(tiktokMode == "off" ? Array.Empty<string>() : new[] { tiktokMode == "drafts" ? "TikTok drafts" : "TikTok" }));
        var message = $"Publish {files.Count} short{(files.Count == 1 ? "" : "s")} to {where}?\n\nInstagram posts go live immediately and are visible to followers."
            + (already.Count > 0 ? $"\n\nAlready sent before and will be posted AGAIN: {string.Join(", ", already)}." : "");
        if (MessageBox.Show(this, message, "Publish with galiluna", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _busy = true; UpdatePublishEnabled();
        _outcome.ForeColor = Color.DimGray;
        int ok = 0;
        try
        {
            foreach (var (item, file) in files)
            {
                item.SubItems[2].Text = "uploading...";
                _outcome.Text = $"Uploading {file.Title}...";
                var progress = new Progress<double>(p => item.SubItems[2].Text = p < 1 ? $"uploading {(int)(p * 100)}%" : "publishing (this can take a few minutes)...");
                try
                {
                    // One POST carries one title/caption/hashtag set for every account, so when the short has
                    // tailored text per network it is sent once per network, each with only that network's targets.
                    var sends = new List<(string Label, NetworkPost Text, GaliLunaClient.SendOptions Options)>();
                    bool tailored = file.Youtube is not null || file.Tiktok is not null || file.Instagram is not null;
                    if (!tailored)
                        sends.Add(("all", new NetworkPost { Title = file.Title, Description = file.Caption ?? "", Tags = file.Hashtags ?? new List<string>() },
                            new GaliLunaClient.SendOptions(instagramIds, tiktokMode, privacy, false, youtubeIds, youtubePrivacy)));
                    else
                    {
                        if (instagramIds.Count > 0) sends.Add(("Instagram", file.PostFor("instagram"), new GaliLunaClient.SendOptions(instagramIds, "off", null, false, Array.Empty<int>(), youtubePrivacy)));
                        if (youtubeIds.Count > 0) sends.Add(("YouTube", file.PostFor("youtube"), new GaliLunaClient.SendOptions(Array.Empty<int>(), "off", null, false, youtubeIds, youtubePrivacy)));
                        if (tiktokMode != "off") sends.Add(("TikTok", file.PostFor("tiktok"), new GaliLunaClient.SendOptions(Array.Empty<int>(), tiktokMode, privacy, false, Array.Empty<int>(), youtubePrivacy)));
                    }

                    var publications = new List<GaliLunaClient.Publication>();
                    int? firstId = null;
                    foreach (var (label, text, options) in sends)
                    {
                        if (sends.Count > 1) item.SubItems[2].Text = $"sending to {label}...";
                        var result = await client.SendAsync(file.Path, text.Title, text.Description, text.Tags, options, progress, CancellationToken.None, file.CoverPath);
                        // Ask again while TikTok is still working, up to ~5 minutes.
                        for (int attempt = 0; attempt < 20 && result.AnyProcessing; attempt++)
                        {
                            item.SubItems[2].Text = "TikTok still working...";
                            await Task.Delay(TimeSpan.FromSeconds(15));
                            result = await client.GetShortAsync(result.Id, CancellationToken.None);
                        }
                        firstId ??= result.Id;
                        publications.AddRange(result.Publications);
                        Log($"galiluna #{result.Id} \"{text.Title}\" ({label}): " + string.Join("; ", result.Publications.Select(p => $"{p.Account ?? p.Network} {p.Status}{(p.Error is null ? "" : " - " + p.Error)}")));
                    }
                    Apply(file, firstId ?? 0, publications);
                    if (publications.Count > 0 && publications.All(p => p.Status is "published" or "drafted")) ok++;
                }
                catch (Exception ex)
                {
                    file.Publications = new List<PublishOutcome> { new() { Network = "galiluna", Status = "failed", Error = ex.Message } };
                    Log($"galiluna \"{file.Title}\": {ex.Message}");
                }
                item.SubItems[2].Text = Describe(file);
                item.Checked = false;
                Saved();
            }
            _outcome.ForeColor = ok == files.Count ? Theme.Success : Theme.Warning;
            _outcome.Text = $"{ok}/{files.Count} short{(files.Count == 1 ? "" : "s")} fully published. See the Published column for each account.";
        }
        finally
        {
            _busy = false;
            UpdatePublishEnabled();
            ShowSelected();
        }
    }

    private static void Apply(GeneratedFile file, int shortId, IEnumerable<GaliLunaClient.Publication> publications)
    {
        file.GaliLunaShortId = shortId;
        file.SentAt ??= DateTime.Now;
        file.Publications = publications.Select(p => new PublishOutcome
        {
            Network = p.Network, Account = p.Account, Status = p.Status, Permalink = p.Permalink, Error = p.Error,
        }).ToList();
    }

    private static List<string> SplitTags(string text) =>
        text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).Where(t => t.Length > 0).ToList();

    private static void TryOpen(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }
}
