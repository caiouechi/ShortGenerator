using System.ComponentModel;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// Step 7, "Publish". Left: the rendered shorts, tick the ones to send. Right: one card per network that is
/// actually connected on galiluna (Instagram, YouTube, TikTok), each with its accounts, its own post text for
/// the selected short and its own Publish button, plus "Publish to all" at the top. Networks that are not
/// connected are named in one muted line instead of showing dead controls. Everything network related goes
/// through <see cref="GaliLunaClient"/>; this control only asks, shows, and records.
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
    /// <summary>Deletes a rendered short (file, cover, project entry); MainForm owns the list and the confirmation.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<GeneratedFile, bool> Delete { get; set; } = _ => false;

    // header
    private readonly Label _connection = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 6, 10, 0), ForeColor = Color.DimGray };
    private readonly LinkLabel _refreshLink = new() { Text = "Refresh", AutoSize = true, LinkColor = Theme.Link, ActiveLinkColor = Theme.PurpleDeep, VisitedLinkColor = Theme.Link, LinkBehavior = LinkBehavior.HoverUnderline };

    // gate (not signed in)
    private readonly Panel _gate = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly EmptyState _gateEmpty = new("empty-editor.png", "Sign in with galiluna to publish", "Your browser opens galiluna, you approve this app, and your connected Instagram, TikTok and YouTube accounts appear here. No password is typed into this app.");
    private readonly FancyButton _gateSignIn = new() { Text = "Sign in with galiluna", Width = 240, Height = 40, Glyph = "" };

    // left: shorts
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false, MultiSelect = false };
    private readonly EmptyState _empty = new("empty-shorts.png", "Nothing to publish yet", "Generate shorts first. They show up here, ready to send to the accounts you connected on galiluna.");
    private readonly Label _listTitle = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, Padding = new Padding(4, 0, 0, 0), Text = "Tick the shorts to publish (double-click to play)" };
    private readonly FancyButton _deleteShort = new() { Text = "Delete short", Width = 120, Enabled = false, Glyph = "\uE74D" };
    private readonly FancyButton _openFolder = new() { Text = "Open folder", Width = 120, Glyph = "\uE8B7" };
    private readonly FancyButton _cancelPublish = new() { Text = "Cancel publishing", Width = 160, Height = 36, Visible = false, Glyph = "\uE711" };
    private CancellationTokenSource? _publishCts;

    // right: destinations
    private readonly Panel _right = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 4, 8) };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(600, 0) };
    private readonly FancyButton _publishAll = new() { Text = "Publish to all", Width = 170, Height = 36, Enabled = false, Glyph = "" };
    private readonly PictureBox _cover = new() { Width = 54, Height = 96, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(19, 24, 52) };
    private readonly Label _editingTitle = new() { AutoSize = true, MaximumSize = new Size(600, 0) };
    private readonly Label _editingHint = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(600, 0) };
    private readonly FancyButton _changeCover = new() { Text = "Change cover...", Width = 140, Glyph = "\uE8B9" };
    private readonly FlowLayoutPanel _cards = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Bg };
    private readonly Label _notConnected = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(600, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly List<NetworkCard> _networkCards = new();

    private GaliLunaClient.Accounts? _accounts;
    private GeneratedFile? _editing;
    private bool _populating;
    private Control? _content;

    public PublishPanel()
    {
        Dock = DockStyle.Fill;

        // left column
        _list.Columns.Add("Short", 220);
        _list.Columns.Add("Rendered", 100);
        _list.Columns.Add("Published", 300);
        Theme.FillColumn(_list, 2);
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 6, 0) };
        var listBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
        listBar.Controls.Add(_deleteShort);
        listBar.Controls.Add(_openFolder);
        listHost.Controls.Add(_list);
        listHost.Controls.Add(listBar);
        listHost.Controls.Add(_empty);
        listHost.Controls.Add(_listTitle);
        _empty.BringToFront();
        _split.Panel1.Controls.Add(listHost);

        // right column: header row, selected short, cards
        // title row with the one global action, then the summary on its own full-width line
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "Destinations", AutoSize = true, Font = Theme.HeadingFont(10.5f), ForeColor = Theme.Heading, Margin = new Padding(0, 10, 0, 2) }, 0, 0);
        var headerButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _cancelPublish.Margin = new Padding(0, 2, 8, 0); _cancelPublish.Kind = ButtonKind.Danger;
        _publishAll.Margin = new Padding(0, 2, 0, 0);
        headerButtons.Controls.Add(_cancelPublish);
        headerButtons.Controls.Add(_publishAll);
        header.Controls.Add(headerButtons, 1, 0);
        _summary.Margin = new Padding(0, 2, 0, 0);
        header.Controls.Add(_summary, 0, 1);
        header.SetColumnSpan(_summary, 2);

        var selected = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 10) };
        selected.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selected.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _cover.Margin = new Padding(0, 0, 12, 0);
        selected.Controls.Add(_cover, 0, 0);
        var selText = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _editingTitle.Font = Theme.HeadingFont(10f); _editingTitle.ForeColor = Theme.Heading;
        selText.Controls.Add(_editingTitle);
        selText.Controls.Add(_editingHint);
        _changeCover.Margin = new Padding(0, 6, 0, 0);
        selText.Controls.Add(_changeCover);
        _changeCover.Click += (_, _) =>
        {
            if (_editing is null) return;
            using var d = new OpenFileDialog { Title = "Choose the cover sent with this video", Filter = "Images|*.jpg;*.jpeg;*.png;*.webp|All files|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _editing.CoverPath = d.FileName;
            _editing.CoverTimeSeconds = null; // a picked image is no frame of the video
            Saved();
            LoadCover(_editing.CoverPath);
            Log($"Cover for \"{_editing.Title}\" set to {Path.GetFileName(d.FileName)}.");
        };
        selected.Controls.Add(selText, 1, 0);

        var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        body.Controls.Add(_cards);
        body.Controls.Add(_notConnected);
        // the right column stacks header, selected short and cards; cards stretch to the column width
        var stack = new Panel { Dock = DockStyle.Top, AutoSize = true };
        stack.Controls.Add(body);
        stack.Controls.Add(selected);
        stack.Controls.Add(header);
        _right.Controls.Add(stack);
        _right.Resize += (_, _) => FitCards();
        _split.Panel2.Controls.Add(_right);
        // min sizes and the first distance wait for a real width: at construction the control is tiny and rejects them
        bool splitSet = false;
        _split.SizeChanged += (_, _) =>
        {
            if (splitSet || _split.Width < 900) return;
            splitSet = true;
            _split.Panel1MinSize = 260;
            _split.Panel2MinSize = 420;
            _split.SplitterDistance = Math.Max(_split.Panel1MinSize, (int)(_split.Width * 0.42));
        };

        // signed-in content vs sign-in gate
        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_split);
        _content = content;
        _gate.Controls.Add(_gateEmpty);
        var gateButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 20, 0, 0) };
        gateButtons.Controls.Add(_gateSignIn);
        _gate.Controls.Add(gateButtons);
        _gate.Resize += (_, _) => gateButtons.Padding = new Padding(Math.Max(0, (_gate.Width - _gateSignIn.Width) / 2), 20, 0, 0);
        Controls.Add(content);
        Controls.Add(_gate);
        _connection.Controls.Add(_refreshLink);
        _refreshLink.Location = new Point(10, 24);
        Controls.Add(_connection);

        Theme.Primary(_gateSignIn);
        Theme.Primary(_publishAll);
        _gateSignIn.Click += async (_, _) => { if (OpenSignIn()) await LoadAccountsAsync(); };
        _refreshLink.LinkClicked += async (_, _) => await LoadAccountsAsync();
        _publishAll.Click += async (_, _) => await PublishAsync(_networkCards.Where(c => c.HasTarget).ToList());
        _list.ItemChecked += (_, _) => UpdateButtons();
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _deleteShort.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count != 1 || _list.SelectedItems[0].Tag is not GeneratedFile f) return;
            if (Delete(f)) RefreshList();
        };
        _openFolder.Click += (_, _) =>
        {
            var f = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as GeneratedFile : Files().FirstOrDefault();
            var dir = f is null ? null : Path.GetDirectoryName(f.Path);
            if (dir is not null && Directory.Exists(dir)) TryOpen(dir);
        };
        _cancelPublish.Click += (_, _) => { _publishCts?.Cancel(); _cancelPublish.Enabled = false; _cancelPublish.Text = "Cancelling..."; };
        _publishAll.Visible = true;
        _list.DoubleClick += (_, _) => { if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is GeneratedFile f && File.Exists(f.Path)) TryOpen(f.Path); };
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
            var item = new ListViewItem(new[] { (f.Language == "en" ? "[EN] " : "") + f.Title, File.Exists(f.Path) ? f.When.ToString("MMM d HH:mm") : "file missing", Describe(f) }) { Tag = f };
            item.Checked = !f.Sent && File.Exists(f.Path);
            if (!File.Exists(f.Path)) item.ForeColor = Color.Gray;
            _list.Items.Add(item);
            if (f.Path == selectedPath) item.Selected = true;
        }
        _list.EndUpdate();
        _populating = false;
        _empty.Visible = _list.Items.Count == 0;
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        UpdateButtons();
    }

    private static string NetworkName(string network) => network.ToLowerInvariant() switch { "instagram" => "Instagram", "tiktok" => "TikTok", "youtube" => "YouTube", _ => network };

    private static string Describe(GeneratedFile f)
    {
        if (!f.Sent) return "not sent";
        if (f.Publications.Count == 0) return $"sent (#{f.GaliLunaShortId})";
        return string.Join("  ·  ", f.Publications.Select(p =>
            (p.Account ?? NetworkName(p.Network)) + ": " + p.Status switch
            {
                "published" => "live",
                "drafted" => "in TikTok drafts",
                "processing" => "TikTok working...",
                _ => "failed",
            }));
    }

    // ------------------------------------------------------------------ accounts

    private async Task LoadAccountsAsync()
    {
        var client = ClientFactory();
        if (client is null)
        {
            _accounts = null;
            SetGate(true, "Not signed in.");
            BuildCards();
            return;
        }
        _connection.Text = "Asking galiluna which accounts are connected...";
        _connection.ForeColor = Color.DimGray;
        try
        {
            _accounts = await client.GetAccountsAsync(CancellationToken.None);
            SetGate(false, GaliLunaSignInForm.Summary(_accounts) + $"  ({client.BaseUrl})");
            _connection.ForeColor = Theme.Success;
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
            BuildCards();
            ShowSelected();
        }
    }

    /// <summary>Shows either the sign-in gate or the destinations, with the status line above both.</summary>
    private void SetGate(bool gated, string status)
    {
        _connection.Text = status;
        _connection.ForeColor = gated ? Theme.Warning : Theme.TextMuted;
        _gate.Visible = gated;
        if (_content is not null) _content.Visible = !gated;
        _refreshLink.Visible = !gated;
        if (gated) _gate.BringToFront();
    }

    /// <summary>One card per connected network; the others are named in a single muted line.</summary>
    private void BuildCards()
    {
        _cards.SuspendLayout();
        foreach (var c in _networkCards) c.Dispose();
        _networkCards.Clear();
        _cards.Controls.Clear();
        var missing = new List<string>();
        if (_accounts is not null)
        {
            if (_accounts.Instagram.Count > 0) _networkCards.Add(new NetworkCard("instagram", "Instagram", "Reels go live immediately.", _accounts, this));
            else missing.Add("Instagram");
            if (_accounts.Youtube.Channels.Count > 0) _networkCards.Add(new NetworkCard("youtube", "YouTube", _accounts.Youtube.Note ?? "Uploaded as a Short with the title, description and keywords below.", _accounts, this));
            else missing.Add("YouTube");
            if (_accounts.Tiktok is not null) _networkCards.Add(new NetworkCard("tiktok", "TikTok", $"Connected as {_accounts.Tiktok.Label}.", _accounts, this));
            else missing.Add("TikTok");
        }
        foreach (var c in _networkCards) _cards.Controls.Add(c);
        _notConnected.Text = _accounts is null ? "" : missing.Count == 0 ? "" :
            $"Not connected on galiluna: {string.Join(", ", missing)}. Connect {(missing.Count == 1 ? "it" : "them")} under Connections on galiluna, then click Refresh above.";
        _notConnected.Visible = _notConnected.Text.Length > 0;
        _cards.ResumeLayout();
        FitCards();
        UpdateButtons();
    }

    private void FitCards()
    {
        int w = Math.Max(380, _right.ClientSize.Width - _right.Padding.Horizontal - 4);
        foreach (var c in _networkCards) c.Width = w;
        _summary.MaximumSize = _notConnected.MaximumSize = new Size(w, 0);
        _editingTitle.MaximumSize = _editingHint.MaximumSize = new Size(Math.Max(200, w - 80), 0);
    }

    // ------------------------------------------------------------------ selected short

    private void ShowSelected()
    {
        _editing = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as GeneratedFile : null;
        if (_editing is null)
        {
            _editingTitle.Text = "";
            _editingHint.Text = _list.Items.Count == 0 ? "" : "Select a short on the left to edit its post text.";
        }
        else
        {
            _editingTitle.Text = _editing.Title;
            var tailored = new[] { (_editing.Youtube, "YouTube"), (_editing.Tiktok, "TikTok"), (_editing.Instagram, "Instagram") }.Where(p => p.Item1 is not null).Select(p => p.Item2).ToList();
            _editingHint.Text = tailored.Count > 0
                ? $"Tailored text for {string.Join(", ", tailored)}; the other networks use the generic caption. Edit any card below."
                : "All networks use the generic caption. Edit a card below to write a version just for that network.";
        }
        LoadCover(_editing?.CoverPath);
        foreach (var c in _networkCards) c.Show(_editing);
        UpdateButtons();
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

    private int TickedCount => _populating ? 0 : _list.Items.Cast<ListViewItem>().Count(i => i is { Checked: true });

    private void UpdateButtons()
    {
        if (_populating) return; // ItemChecked fires mid-population, when the collection can still hand out nulls
        int ticked = TickedCount;
        bool any = _accounts is not null && ticked > 0;
        // publishing runs in the background, so the buttons stay usable while uploads are in flight
        int destinations = 0, uploads = 0;
        foreach (var c in _networkCards)
        {
            int accounts = c.Sends().Count;
            uploads += ticked * accounts;
            if (accounts > 0) destinations++;
            c.SetEnabled(any && accounts > 0, ticked);
        }
        _publishAll.Enabled = any && uploads > 0;
        // a video still uploading cannot be deleted
        _deleteShort.Enabled = _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is GeneratedFile sf && !_inFlight.Any(k => k.StartsWith(sf.Path + "|", StringComparison.OrdinalIgnoreCase));
        _openFolder.Enabled = Files().Count > 0;
        _cancelPublish.Visible = _running > 0;
        _cancelPublish.Text = _running > 1 ? $"Cancel publishing ({_running})" : "Cancel publishing";
        _publishAll.Text = uploads > 1 ? $"Publish to all ({uploads})" : "Publish to all";
        _summary.Text = _accounts is null ? "" : ticked == 0 ? "Tick at least one short on the left." :
            uploads == 0 ? "Tick the accounts to publish to in a card below." :
            $"{ticked} video{(ticked == 1 ? "" : "s")} ticked, {uploads} upload{(uploads == 1 ? "" : "s")} to {destinations} network{(destinations == 1 ? "" : "s")}." +
            (_running > 0 ? $" Publishing in the background ({_running} running): you can tick another video and publish it meanwhile." : "");
    }

    // ------------------------------------------------------------------ publishing

    /// <summary>Uploads in flight, keyed file path | network | account, so the same video never goes twice to one account at once.</summary>
    private readonly HashSet<string> _inFlight = new();
    private int _running;

    /// <summary>
    /// Sends the ticked videos to the ticked accounts of the given networks. Everything is captured when the button is
    /// clicked (files, accounts, options, texts) and the upload runs in the background, so another video can be ticked
    /// and published to other accounts while this one is still uploading.
    /// </summary>
    private async Task PublishAsync(IReadOnlyList<NetworkCard> targets)
    {
        var client = ClientFactory();
        if (client is null || _accounts is null || targets.Count == 0) return;
        var files = _list.Items.Cast<ListViewItem>().Where(i => i is { Checked: true }).Select(i => (Item: i, File: (GeneratedFile)i.Tag!)).ToList();
        if (files.Count == 0) return;

        // snapshot of the job: later clicks and ticks never change what this one sends
        var jobs = new List<(ListViewItem Item, GeneratedFile File, NetworkCard Card, string Account, GaliLunaClient.SendOptions Options, NetworkPost Text, string Key)>();
        int skipped = 0;
        foreach (var (item, file) in files)
            foreach (var card in targets)
                foreach (var (account, options) in card.Sends())
                {
                    var key = $"{file.Path}|{card.Network}|{account}";
                    if (_inFlight.Contains(key)) { skipped++; continue; }
                    var t = file.PostFor(card.Network);
                    jobs.Add((item, file, card, account, options, new NetworkPost { Title = t.Title, Description = t.Description, Tags = t.Tags.ToList() }, key));
                }
        if (jobs.Count == 0)
        {
            MessageBox.Show(this, skipped > 0 ? "Those videos are already being sent to those accounts." : "Tick at least one account to publish to.", "Nothing to publish");
            return;
        }
        var plan = string.Join("\n", jobs.GroupBy(j => j.File).Select(g => $"  {(g.Key.Language == "en" ? "[EN] " : "")}{g.Key.Title}  ->  {string.Join(", ", g.Select(j => j.Account))}"));
        var again = jobs.Where(j => j.File.Publications.Any(p => string.Equals(p.Network, j.Card.Network, StringComparison.OrdinalIgnoreCase)
                                                                  && string.Equals(p.Account, j.Account, StringComparison.OrdinalIgnoreCase) && p.Status is "published" or "drafted"))
                        .Select(j => $"{j.File.Title} on {j.Account}").Distinct().ToList();
        var message = $"Publish?\n\n{plan}" + (jobs.Count > 1 ? $"\n\n{jobs.Count} uploads. They run in the background: you can keep working and publish other videos meanwhile." : "")
            + (jobs.Any(j => j.Card.Network == "instagram") ? "\n\nInstagram posts go live immediately and are visible to followers." : "")
            + (skipped > 0 ? $"\n\n{skipped} upload(s) already in progress are skipped." : "")
            + (again.Count > 0 ? $"\n\nAlready posted before and will be posted AGAIN: {string.Join(", ", again)}." : "");
        if (MessageBox.Show(this, message, "Publish with galiluna", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var j in jobs) _inFlight.Add(j.Key);
        _publishCts ??= new CancellationTokenSource();
        var ct = _publishCts.Token;
        _running++;
        _cancelPublish.Enabled = true;
        // the videos of this job are unticked right away, so the next one can be ticked and published at once
        _populating = true;
        foreach (var (item, _) in files) item.Checked = false;
        _populating = false;
        UpdateButtons();
        int ok = 0;
        try
        {
            foreach (var j in jobs)
            {
                if (ct.IsCancellationRequested) break;
                void Show(string state) { j.Item.SubItems[2].Text = $"{j.Card.Title} ({j.Account}): {state}"; j.Card.SetOutcome($"{j.Account}: {state}", Theme.TextMuted); }
                Show("uploading...");
                var progress = new Progress<double>(v => Show(v < 1 ? $"uploading {(int)(v * 100)}%" : "publishing (this can take a few minutes)..."));
                try
                {
                    var result = await client.SendAsync(j.File.Path, j.Text.Title, j.Text.Description, j.Text.Tags, j.Options, progress, ct, j.File.CoverPath, j.File.CoverTimeSeconds);
                    // Ask again while TikTok is still working, up to ~5 minutes.
                    for (int attempt = 0; attempt < 20 && result.AnyProcessing; attempt++)
                    {
                        Show("TikTok still working...");
                        await Task.Delay(TimeSpan.FromSeconds(15), ct);
                        result = await client.GetShortAsync(result.Id, ct);
                    }
                    Apply(j.File, j.Card.Network, j.Account, result.Id, result.Publications);
                    bool good = result.Publications.Count > 0 && result.Publications.All(x => x.Status is "published" or "drafted");
                    if (good) ok++;
                    j.Card.SetOutcome(string.Join("; ", result.Publications.Select(x => $"{x.Account ?? j.Account}: {StatusText(x.Status)}{(x.Error is null ? "" : " - " + x.Error)}")), good ? Theme.Success : Theme.Warning);
                    Log($"galiluna #{result.Id} \"{j.Text.Title}\" ({j.Card.Title}, {j.Account}): " + string.Join("; ", result.Publications.Select(x => $"{x.Account ?? x.Network} {x.Status}{(x.Error is null ? "" : " - " + x.Error)}")));
                }
                catch (OperationCanceledException)
                {
                    // Stopped before galiluna answered: if the upload had finished, galiluna may still post it.
                    j.Card.SetOutcome($"{j.Account}: cancelled (if the upload had finished, galiluna may still have posted it; check the account)", Theme.Warning);
                    Log($"galiluna \"{j.File.Title}\" ({j.Card.Title}, {j.Account}): cancelled by the user.");
                }
                catch (Exception ex)
                {
                    Apply(j.File, j.Card.Network, j.Account, null, new[] { new GaliLunaClient.Publication { Network = j.Card.Network, Account = j.Account, Status = "failed", Error = ex.Message } });
                    j.Card.SetOutcome($"{j.Account}: failed - {ex.Message}", Theme.Danger);
                    Log($"galiluna \"{j.File.Title}\" ({j.Card.Title}, {j.Account}): {ex.Message}");
                }
                finally
                {
                    _inFlight.Remove(j.Key);
                    if (!j.Item.ListView?.IsDisposed ?? false) j.Item.SubItems[2].Text = Describe(j.File);
                    Saved();
                }
            }
            Log($"Publishing finished: {ok}/{jobs.Count} upload(s) succeeded.");
        }
        finally
        {
            foreach (var j in jobs) _inFlight.Remove(j.Key);
            _running--;
            if (_running == 0) { _publishCts?.Dispose(); _publishCts = null; }
            UpdateButtons();
            if (_running == 0) ShowSelected();
        }
    }

    private static string StatusText(string status) => status switch { "published" => "live", "drafted" => "in drafts", "processing" => "working...", _ => "failed" };

    /// <summary>Records one account's outcome on the file, keeping what other accounts and networks reported earlier.</summary>
    private static void Apply(GeneratedFile file, string network, string account, int? shortId, IEnumerable<GaliLunaClient.Publication> publications)
    {
        if (shortId is not null) file.GaliLunaShortId ??= shortId;
        file.SentAt ??= DateTime.Now;
        var incomingAccounts = publications.Select(p => p.Account ?? account).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = file.Publications.Where(p => !(string.Equals(p.Network, network, StringComparison.OrdinalIgnoreCase)
                                                  && incomingAccounts.Contains(p.Account ?? account))).ToList();
        kept.AddRange(publications.Select(p => new PublishOutcome
        {
            Network = p.Network, Account = p.Account ?? account, Status = p.Status, Permalink = p.Permalink, Error = p.Error,
        }));
        file.Publications = kept;
    }

    private static List<string> SplitTags(string text) =>
        text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).Where(t => t.Length > 0).ToList();

    private static void TryOpen(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ------------------------------------------------------------------ network card

    /// <summary>
    /// One destination: its accounts (or mode for TikTok), the post text for the selected short on this network,
    /// and its own Publish button. Text edits go straight into the file's per-network post.
    /// </summary>
    private sealed class NetworkCard : Card
    {
        public string Network { get; }
        public new string Title { get; }
        private readonly PublishPanel _owner;
        private readonly GaliLunaClient.Accounts _accounts;
        private readonly Dictionary<CheckBox, int> _boxes = new();

        private readonly ComboBox _privacy = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        private readonly Label _modeHint = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly TextBox _title = new() { Dock = DockStyle.Top };
        private readonly TextBox _text = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 72, Dock = DockStyle.Top };
        private readonly TextBox _tags = new() { Dock = DockStyle.Top };
        private readonly Label _count = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly FancyButton _publish = new() { Width = 190, Height = 34, Enabled = false, Glyph = "" };
        private readonly Label _outcome = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly TableLayoutPanel _grid;
        private GeneratedFile? _file;
        private bool _loading;

        public NetworkCard(string network, string title, string subtitle, GaliLunaClient.Accounts accounts, PublishPanel owner)
        {
            Network = network; Title = title; _owner = owner; _accounts = accounts;
            base.Title = title; Subtitle = subtitle;
            Margin = new Padding(0, 0, 0, 12);
            bool yt = network == "youtube", tt = network == "tiktok";

            _grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, BackColor = Theme.Elevated };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            void Add(string label, Control c)
            {
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 4), ForeColor = Theme.TextSecondary }, 0, row);
                c.Margin = new Padding(0, 5, 0, 3);
                _grid.Controls.Add(c, 1, row++);
            }

            // accounts / mode
            if (tt)
            {
                var t = accounts.Tiktok!;
                _mode.Items.AddRange(new object[] { "Send to my TikTok drafts (finish in the app)", "Post directly to TikTok" });
                _mode.SelectedIndex = 0;
                foreach (var level in t.PrivacyLevels) _privacy.Items.Add(level);
                if (_privacy.Items.Count > 0) _privacy.SelectedIndex = 0;
                _privacy.Enabled = false;
                _mode.SelectedIndexChanged += (_, _) => { _privacy.Enabled = _mode.SelectedIndex == 1; UpdateModeHint(); owner.UpdateButtons(); };
                Add("How", _mode);
                Add("Visibility", _privacy);
                Add("", _modeHint);
                UpdateModeHint();
            }
            else
            {
                var list = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Elevated };
                foreach (var a in yt ? accounts.Youtube.Channels : accounts.Instagram)
                {
                    var box = new CheckBox { Text = a.Label, AutoSize = true, Checked = false, Margin = new Padding(0, 2, 0, 2) };
                    box.CheckedChanged += (_, _) => owner.UpdateButtons();
                    list.Controls.Add(box);
                    _boxes[box] = a.Id;
                }
                Add(yt ? "Channels" : "Accounts", list);
                if (yt)
                {
                    foreach (var level in accounts.Youtube.PrivacyLevels.Count > 0 ? accounts.Youtube.PrivacyLevels : new List<string> { "public", "unlisted", "private" }) _privacy.Items.Add(level);
                    _privacy.SelectedIndex = 0;
                    Add("Privacy", _privacy);
                }
            }

            // text for this network
            if (yt) Add("Title", _title);
            Add(yt ? "Description" : "Caption", _text);
            Add("", _count);
            _tags.PlaceholderText = yt ? "keyword one, keyword two" : "tag1 tag2 tag3 (without #)";
            Add(yt ? "Keywords" : "Hashtags", _tags);

            // action
            var actionRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Dock = DockStyle.Top, BackColor = Theme.Elevated };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _publish.Text = $"Publish to {title}";
            _publish.Margin = new Padding(0, 6, 12, 0);
            _outcome.Margin = new Padding(0, 14, 0, 0);
            actionRow.Controls.Add(_publish, 0, 0);
            actionRow.Controls.Add(_outcome, 1, 0);
            Add("", actionRow);
            Theme.Primary(_publish);
            _publish.Click += async (_, _) => await owner.PublishAsync(new[] { this });

            _text.TextChanged += (_, _) => { _count.Text = $"{_text.Text.Length} / {(yt ? 5000 : 2200)}"; if (!_loading && _file is not null) Post().Description = _text.Text; };
            _tags.TextChanged += (_, _) => { if (!_loading && _file is not null) Post().Tags = SplitTags(_tags.Text); };
            _title.TextChanged += (_, _) => { if (!_loading && _file is not null) Post().Title = _title.Text; };

            Controls.Add(_grid);
            _grid.SizeChanged += (_, _) => Height = _grid.Height + Padding.Vertical;
            Height = _grid.Height + Padding.Vertical;
            Theme.Apply(this);
            // the theme tints nested panels; inside a white card they must stay white
            foreach (Control c in _grid.Controls) if (c is FlowLayoutPanel or TableLayoutPanel) c.BackColor = Theme.Elevated;
            _grid.BackColor = Theme.Elevated;
            Resize += (_, _) => _outcome.MaximumSize = new Size(Math.Max(120, Width - 96 - 190 - 60), 0);
        }

        private NetworkPost Post()
        {
            var f = _file!;
            return Network switch
            {
                "youtube" => f.Youtube ??= f.PostFor("youtube"),
                "tiktok" => f.Tiktok ??= f.PostFor("tiktok"),
                _ => f.Instagram ??= f.PostFor("instagram"),
            };
        }

        private void UpdateModeHint()
        {
            _modeHint.Text = _mode.SelectedIndex == 1
                ? (_accounts.Tiktok is { DirectPostingAvailable: false } ? "Direct posting on this galiluna environment only reaches the profile as SELF_ONLY until TikTok clears it." : "Posted straight to the profile with the visibility above.")
                : "The video lands in your TikTok inbox; you choose visibility and post it in the TikTok app. Works for every account.";
        }

        public bool HasTarget => Network == "tiktok" || _boxes.Keys.Any(b => b.Checked);

        /// <summary>
        /// One request per ticked account (or one for TikTok), each with only that account as target, so the
        /// outcome of every account shows as soon as galiluna answers for it instead of after the whole batch.
        /// </summary>
        /// <summary>One request per ticked account (or one for TikTok), so each account's result shows as soon as it lands.</summary>
        public List<(string Account, GaliLunaClient.SendOptions Options)> Sends()
        {
            var none = Array.Empty<int>();
            var list = new List<(string, GaliLunaClient.SendOptions)>();
            switch (Network)
            {
                case "instagram":
                    foreach (var kv in _boxes.Where(kv => kv.Key.Checked))
                        list.Add((kv.Key.Text, new GaliLunaClient.SendOptions(new[] { kv.Value }, "off", null, false, none, "public")));
                    break;
                case "youtube":
                    foreach (var kv in _boxes.Where(kv => kv.Key.Checked))
                        list.Add((kv.Key.Text, new GaliLunaClient.SendOptions(none, "off", null, false, new[] { kv.Value }, _privacy.SelectedItem as string ?? "public")));
                    break;
                default:
                    list.Add((_accounts.Tiktok?.Label ?? "TikTok", new GaliLunaClient.SendOptions(none, _mode.SelectedIndex == 1 ? "direct" : "drafts", _privacy.SelectedItem as string, false, none, "public")));
                    break;
            }
            return list;
        }

        public string Describe() => Network switch
        {
            "instagram" => string.Join(", ", _boxes.Where(kv => kv.Key.Checked).Select(kv => kv.Key.Text)),
            "youtube" => "YouTube " + string.Join(", ", _boxes.Where(kv => kv.Key.Checked).Select(kv => kv.Key.Text)) + $" ({_privacy.SelectedItem})",
            _ => _mode.SelectedIndex == 1 ? "TikTok (direct)" : "TikTok drafts",
        };

        /// <summary>Shows the selected short's text for this network (its tailored version or the generic fallback).</summary>
        public void Show(GeneratedFile? file)
        {
            _file = file;
            _loading = true;
            if (file is null) { _title.Text = _text.Text = _tags.Text = ""; _outcome.Text = ""; }
            else
            {
                var tailored = Network switch { "youtube" => file.Youtube, "tiktok" => file.Tiktok, _ => file.Instagram };
                var post = tailored ?? file.PostFor(Network);
                _title.Text = post.Title;
                _text.Text = post.Description;
                _tags.Text = string.Join(Network == "youtube" ? ", " : " ", post.Tags);
                var mine = file.Publications.Where(p => string.Equals(p.Network, Network, StringComparison.OrdinalIgnoreCase)).ToList();
                _outcome.Text = mine.Count == 0 ? (tailored is null ? "Generic text. Start typing to write a version just for this network." : "Text written for this network.")
                    : string.Join("; ", mine.Select(p => $"{p.Account ?? Title}: {StatusText(p.Status)}{(p.Error is null ? "" : " - " + p.Error)}"));
                _outcome.ForeColor = mine.Count == 0 ? Theme.TextMuted : mine.All(p => p.Status is "published" or "drafted") ? Theme.Success : Theme.Danger;
            }
            _loading = false;
        }

        /// <param name="files">How many of the ticked videos this network will receive (after the language filter).</param>
        public void SetEnabled(bool enabled, int files) { _publish.Enabled = enabled; _publish.Text = files > 1 && enabled ? $"Publish {files} to {Title}" : $"Publish to {Title}"; }
        public void SetOutcome(string text, Color color) { _outcome.Text = text; _outcome.ForeColor = color; }
    }
}
