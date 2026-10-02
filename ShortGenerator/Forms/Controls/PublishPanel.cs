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
    /// <summary>Makes a copy of the video that opens with the cover image (TikTok takes no cover image, only a frame time).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string, CancellationToken, Task<string>>? MakeCoverLeadCopy { get; set; }

    // header
    private readonly Label _connection = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 6, 10, 0), ForeColor = Color.DimGray };
    private readonly LinkLabel _refreshLink = new() { Text = "Refresh", AutoSize = true, LinkColor = Theme.Link, ActiveLinkColor = Theme.PurpleDeep, VisitedLinkColor = Theme.Link, LinkBehavior = LinkBehavior.HoverUnderline };

    // gate (not signed in)
    private readonly Panel _gate = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly EmptyState _gateEmpty = new("empty-editor.png", "Sign in with galiluna to publish", "Your browser opens galiluna, you approve this app, and your connected Instagram, TikTok and YouTube accounts appear here. No password is typed into this app.");
    private readonly FancyButton _gateSignIn = new() { Text = "Sign in with galiluna", Width = 240, Height = 40, Glyph = "" };

    // left: shorts
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = false, HideSelection = false, MultiSelect = false };
    private readonly EmptyState _empty = new("empty-shorts.png", "Nothing to publish yet", "Generate shorts first. They show up here, ready to send to the accounts you connected on galiluna.");
    private readonly Label _listTitle = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, Padding = new Padding(4, 0, 0, 0), Text = "Select the short to publish (double-click to play)" };
    private readonly FancyButton _deleteShort = new() { Text = "Delete short", Width = 120, Enabled = false, Glyph = "\uE74D" };
    private readonly FancyButton _openFolder = new() { Text = "Open folder", Width = 120, Glyph = "\uE8B7" };
    /// <summary>Deletes the videos and covers galiluna keeps for publishing; nothing there is ever deleted without it.</summary>
    private readonly FancyButton _clearStorage = new() { Text = "Clear galiluna storage", Width = 200, Enabled = false, Visible = false, Glyph = "\uE74D" };
    private GaliLunaClient.StorageInfo? _storage;
    private readonly FancyButton _cancelPublish = new() { Text = "Cancel all", Width = 150, Height = 36, Visible = false, Glyph = "\uE711" };

    // left, under the shorts: the publishing queue (what is running, waiting, done)
    private readonly SplitContainer _leftSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly ListView _queue = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
    private readonly Label _queueTitle = new() { Text = "Publishing", AutoSize = true, Font = Theme.HeadingFont(10.5f), ForeColor = Theme.Heading, Margin = new Padding(0, 8, 10, 0) };
    private readonly Label _queueCounts = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 12, 0, 0) };
    private readonly Label _queueEmpty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, BackColor = Theme.Elevated,
        Text = "Nothing is publishing. Every upload you start shows here: waiting, uploading, live or failed." };
    private readonly FancyButton _cancelJob = new() { Text = "Cancel selected", Width = 150, Enabled = false, Glyph = "\uE711" };
    private readonly FancyButton _clearDone = new() { Text = "Clear finished", Width = 140, Enabled = false, Glyph = "\uE894" };

    // right: destinations
    private readonly Panel _right = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 4, 8) };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(600, 0) };
    private readonly FancyButton _publishAll = new() { Text = "Queue all", Width = 170, Height = 36, Enabled = false, Glyph = "" };
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
        var listBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0), WrapContents = true };
        foreach (var b in new[] { _deleteShort, _openFolder, _clearStorage }) b.Margin = new Padding(0, 0, 8, 6);
        listBar.Controls.Add(_deleteShort);
        listBar.Controls.Add(_openFolder);
        listBar.Controls.Add(_clearStorage);
        listHost.Controls.Add(_list);
        listHost.Controls.Add(listBar);
        listHost.Controls.Add(_empty);
        listHost.Controls.Add(_listTitle);
        _empty.BringToFront();

        // queue under the shorts
        _queue.Columns.Add("Short", 200);
        _queue.Columns.Add("Destination", 170);
        _queue.Columns.Add("Status", 240);
        Theme.FillColumn(_queue, 2);
        var queueHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 6, 0) };
        var queueHead = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 6) };
        queueHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queueHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queueHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queueHead.Controls.Add(_queueTitle, 0, 0);
        queueHead.Controls.Add(_queueCounts, 1, 0);
        var queueButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        queueButtons.Controls.Add(_cancelJob);
        _clearDone.Margin = Padding.Empty;
        queueButtons.Controls.Add(_clearDone);
        queueHead.Controls.Add(queueButtons, 2, 0);
        var queueBody = new Panel { Dock = DockStyle.Fill };
        queueBody.Controls.Add(_queue);
        queueBody.Controls.Add(_queueEmpty);
        _queueEmpty.BringToFront();
        queueHost.Controls.Add(queueBody);
        queueHost.Controls.Add(queueHead);
        _leftSplit.Panel1.Controls.Add(listHost);
        _leftSplit.Panel2.Controls.Add(queueHost);
        bool leftSet = false;
        _leftSplit.SizeChanged += (_, _) =>
        {
            if (leftSet || _leftSplit.Height < 420) return;
            leftSet = true;
            _leftSplit.Panel1MinSize = 160;
            _leftSplit.Panel2MinSize = 150;
            _leftSplit.SplitterDistance = (int)(_leftSplit.Height * 0.55);
        };
        _split.Panel1.Controls.Add(_leftSplit);

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
        _cancelPublish.Click += (_, _) =>
        {
            var open = _jobs.Where(j => j.IsOpen).ToList();
            if (open.Count == 0) return;
            if (!AppDialog.Confirm(this, "Cancel all publishing?", $"{open.Count} upload{(open.Count == 1 ? "" : "s")} still waiting or running will stop.", "Cancel all", "Keep publishing", danger: true,
                    notes: new[] { "An upload galiluna already received may still be posted. Check the account." })) return;
            foreach (var j in open) CancelJob(j);
        };
        _cancelJob.Click += (_, _) =>
        {
            foreach (var j in SelectedJobs().Where(j => j.IsOpen).ToList()) CancelJob(j);
        };
        _clearDone.Click += (_, _) =>
        {
            foreach (var j in _jobs.Where(j => !j.IsOpen).ToList()) { _jobs.Remove(j); _queue.Items.Remove(j.Row); }
            UpdateQueue();
        };
        _queue.SelectedIndexChanged += (_, _) => UpdateQueue();
        _clearStorage.Click += async (_, _) => await ClearStorageAsync();
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
            _ = RefreshStorageAsync();
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

    /// <summary>The one short every publish button acts on: the selected row, when its file still exists.</summary>
    private (ListViewItem Item, GeneratedFile File)? Current =>
        !_populating && _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is GeneratedFile f && File.Exists(f.Path)
            ? (_list.SelectedItems[0], f) : null;

    private void UpdateButtons()
    {
        if (_populating) return; // ItemChecked fires mid-population, when the collection can still hand out nulls
        var current = Current;
        bool any = _accounts is not null && current is not null;
        // publishing runs in the background, so the buttons stay usable while uploads are in flight
        int destinations = 0, uploads = 0;
        foreach (var c in _networkCards)
        {
            int accounts = c.Sends().Count;
            uploads += any ? accounts : 0;
            if (accounts > 0) destinations++;
            c.SetEnabled(any && accounts > 0, 1);
        }
        _publishAll.Enabled = any && uploads > 0;
        // a video still uploading cannot be deleted
        _deleteShort.Enabled = _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is GeneratedFile sf && !_inFlight.Any(k => k.StartsWith(sf.Path + "|", StringComparison.OrdinalIgnoreCase));
        _openFolder.Enabled = Files().Count > 0;
        int open = _jobs.Count(j => j.IsOpen);
        _cancelPublish.Visible = open > 0;
        _cancelPublish.Text = open > 1 ? $"Cancel all ({open})" : "Cancel all";
        _publishAll.Text = uploads > 1 ? $"Queue all ({uploads})" : "Queue all";
        _summary.Text = _accounts is null ? "" : current is null ? "Select a short on the left." :
            uploads == 0 ? "Tick the accounts to publish to in a card below." :
            $"Queue all sends this short to {uploads} destination{(uploads == 1 ? "" : "s")} on {destinations} network{(destinations == 1 ? "" : "s")}." +
            (open > 0 ? $" {open} upload{(open == 1 ? "" : "s")} in the queue on the left: you can select another short and queue it meanwhile." : "");
    }

    // ------------------------------------------------------------------ publishing

    /// <summary>Uploads waiting or running, keyed file path | network | account, so the same video never goes twice to one account at once.</summary>
    private readonly HashSet<string> _inFlight = new();
    /// <summary>Every upload started in this session, in the order it was queued; finished ones stay until "Clear finished".</summary>
    private readonly List<PublishJob> _jobs = new();
    /// <summary>Uploads that run at the same time; the rest wait their turn. galiluna holds each request for minutes.</summary>
    private const int MaxParallel = 2;
    private int _running;

    /// <summary>One video to one account: everything it sends is captured when it is queued.</summary>
    private sealed class PublishJob
    {
        public required ListViewItem Item { get; init; }
        public required GeneratedFile File { get; init; }
        public required NetworkCard Card { get; init; }
        public required string Account { get; init; }
        public required GaliLunaClient.SendOptions Options { get; init; }
        public required NetworkPost Text { get; init; }
        public required string Key { get; init; }
        public ListViewItem Row { get; set; } = null!;
        public CancellationTokenSource Cts { get; } = new();
        /// <summary>queued | running | done | failed | cancelled</summary>
        public string State { get; set; } = "queued";
        public bool IsOpen => State is "queued" or "running";
    }

    /// <summary>
    /// Queues the selected short for the ticked accounts of the given networks. Nothing waits for the upload: the jobs
    /// go into the queue on the left, run two at a time, and another short can be selected and queued meanwhile.
    /// </summary>
    private Task PublishAsync(IReadOnlyList<NetworkCard> targets)
    {
        var client = ClientFactory();
        if (client is null || _accounts is null || targets.Count == 0) return Task.CompletedTask;
        if (Current is not { } current) return Task.CompletedTask;
        var files = new[] { current };

        var jobs = new List<PublishJob>();
        int skipped = 0;
        foreach (var (item, file) in files)
            foreach (var card in targets)
                foreach (var (account, options) in card.Sends())
                {
                    var key = $"{file.Path}|{card.Network}|{account}";
                    if (_inFlight.Contains(key)) { skipped++; continue; }
                    var t = file.PostFor(card.Network);
                    jobs.Add(new PublishJob
                    {
                        Item = item, File = file, Card = card, Account = account, Options = options, Key = key,
                        Text = new NetworkPost { Title = t.Title, Description = t.Description, Tags = t.Tags.ToList() },
                    });
                }
        if (jobs.Count == 0)
        {
            AppDialog.Alert(this, "Nothing to publish", skipped > 0 ? "This short is already in the queue for those accounts." : "Tick at least one account to publish to.");
            return Task.CompletedTask;
        }

        var details = jobs.GroupBy(j => j.File).Select(g =>
            $"{(g.Key.Language == "en" ? "[EN] " : "")}{g.Key.Title}\n      to {string.Join(", ", g.Select(j => j.Card.Network == "tiktok" ? $"TikTok {j.Account} ({(j.Options.TikTokMode == "direct" ? "direct" : "drafts")})" : $"{j.Card.Title} {j.Account}"))}").ToList();
        var again = jobs.Where(j => j.File.Publications.Any(p => string.Equals(p.Network, j.Card.Network, StringComparison.OrdinalIgnoreCase)
                                                                  && string.Equals(p.Account, j.Account, StringComparison.OrdinalIgnoreCase) && p.Status is "published" or "drafted"))
                        .Select(j => $"{j.File.Title} on {j.Account}").Distinct().ToList();
        var notes = new List<string>();
        if (jobs.Any(j => j.Card.Network == "instagram" || j.Options.TikTokMode == "direct" || j.Card.Network == "youtube"))
            notes.Add("Posts go live right away and are visible to followers.");
        notes.Add("Uploads run in the queue on the left; you can keep working and publish other videos meanwhile.");
        if (skipped > 0) notes.Add($"{skipped} upload{(skipped == 1 ? "" : "s")} already in the queue {(skipped == 1 ? "is" : "are")} skipped.");
        if (again.Count > 0) notes.Add("!Already posted before, will be posted again: " + string.Join(", ", again) + ".");
        var title = jobs.Count == 1 ? "Publish this short?" : $"Publish {jobs.Count} uploads?";
        if (!AppDialog.Confirm(this, title, "galiluna sends the video, its cover and its text to:", jobs.Count == 1 ? "Publish" : $"Publish {jobs.Count}",
                details: details, notes: notes)) return Task.CompletedTask;

        foreach (var j in jobs)
        {
            _inFlight.Add(j.Key);
            j.Row = new ListViewItem(new[] { (j.File.Language == "en" ? "[EN] " : "") + j.File.Title, $"{j.Card.Title} · {j.Account}", "Waiting" }) { Tag = j, ForeColor = Theme.TextMuted };
            _jobs.Add(j);
            _queue.Items.Add(j.Row);
            j.Item.SubItems[2].Text = $"{j.Card.Title} ({j.Account}): waiting in the queue";
        }
        Log($"Queued {jobs.Count} upload(s).");
        Pump(client);
        UpdateQueue();
        UpdateButtons();
        return Task.CompletedTask;
    }

    /// <summary>Starts waiting jobs while fewer than <see cref="MaxParallel"/> run.</summary>
    private void Pump(GaliLunaClient client)
    {
        while (_running < MaxParallel && _jobs.FirstOrDefault(j => j.State == "queued") is { } next)
        {
            next.State = "running";
            _running++;
            _ = RunJobAsync(client, next);
        }
    }

    private void SetJob(PublishJob j, string status, Color color)
    {
        if (j.Row.ListView is not null) { j.Row.SubItems[2].Text = status; j.Row.ForeColor = color; }
        if (!j.Item.ListView?.IsDisposed ?? false) j.Item.SubItems[2].Text = $"{j.Card.Title} ({j.Account}): {status}";
        j.Card.SetOutcome($"{j.Account}: {status}", color == Theme.TextMuted ? Theme.TextMuted : color);
    }

    private async Task RunJobAsync(GaliLunaClient client, PublishJob j)
    {
        var ct = j.Cts.Token;
        UpdateQueue();
        string? tempCopy = null;
        try
        {
            // TikTok can only show a frame of the video as its cover. When the cover is an image (a custom one, or a
            // short rendered before frame times were kept), TikTok gets a copy that opens with it for a tenth of a
            // second, and its cover points there. Drafts get the same copy: TikTok suggests the first frame.
            string videoPath = j.File.Path;
            double? coverTime = j.File.CoverTimeSeconds;
            if (j.Card.Network == "tiktok" && coverTime is null && MakeCoverLeadCopy is not null && j.File.CoverPath is { } cover && File.Exists(cover))
            {
                SetJob(j, "Adding the cover for TikTok...", Theme.Purple);
                try
                {
                    tempCopy = await MakeCoverLeadCopy(j.File.Path, cover, ct);
                    videoPath = tempCopy;
                    coverTime = ShortRenderer.CoverLeadSeconds / 2;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log($"TikTok cover for \"{j.File.Title}\" could not be added ({ex.Message}); sending the video without it.");
                }
            }
            SetJob(j, "Uploading...", Theme.Purple);
            var progress = new Progress<double>(v => { if (j.State == "running") SetJob(j, v < 1 ? $"Uploading {(int)(v * 100)}%" : "Publishing (can take a few minutes)...", Theme.Purple); });
            var result = await client.SendAsync(videoPath, j.Text.Title, j.Text.Description, j.Text.Tags, j.Options, progress, ct, j.File.CoverPath, coverTime);
            // Ask again while TikTok is still working, up to ~5 minutes.
            for (int attempt = 0; attempt < 20 && result.AnyProcessing; attempt++)
            {
                SetJob(j, "TikTok still processing...", Theme.Purple);
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                result = await client.GetShortAsync(result.Id, ct);
            }
            Apply(j.File, j.Card.Network, j.Account, result.Id, result.Publications);
            bool good = result.Publications.Count > 0 && result.Publications.All(x => x.Status is "published" or "drafted");
            j.State = good ? "done" : "failed";
            var text = string.Join("; ", result.Publications.Select(x => $"{StatusText(x.Status)}{(x.Error is null ? "" : " - " + x.Error)}"));
            SetJob(j, good ? (text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : "Done") : "Failed: " + text, good ? Theme.Success : Theme.Danger);
            Log($"galiluna #{result.Id} \"{j.Text.Title}\" ({j.Card.Title}, {j.Account}): " + string.Join("; ", result.Publications.Select(x => $"{x.Account ?? x.Network} {x.Status}{(x.Error is null ? "" : " - " + x.Error)}")));
        }
        catch (OperationCanceledException)
        {
            // Stopped before galiluna answered: if the upload had finished, galiluna may still post it.
            j.State = "cancelled";
            SetJob(j, "Cancelled (if the upload had finished it may still be posted)", Theme.Warning);
            Log($"galiluna \"{j.File.Title}\" ({j.Card.Title}, {j.Account}): cancelled by the user.");
        }
        catch (Exception ex)
        {
            j.State = "failed";
            Apply(j.File, j.Card.Network, j.Account, null, new[] { new GaliLunaClient.Publication { Network = j.Card.Network, Account = j.Account, Status = "failed", Error = ex.Message } });
            SetJob(j, "Failed: " + ex.Message, Theme.Danger);
            Log($"galiluna \"{j.File.Title}\" ({j.Card.Title}, {j.Account}): {ex.Message}");
        }
        finally
        {
            if (tempCopy is not null) { try { Directory.Delete(Path.GetDirectoryName(tempCopy)!, recursive: true); } catch { } }
            _running--;
            _inFlight.Remove(j.Key);
            if (!j.Item.ListView?.IsDisposed ?? false) j.Item.SubItems[2].Text = Describe(j.File);
            Saved();
            Pump(client);
            UpdateQueue();
            UpdateButtons();
            if (_running == 0 && ReferenceEquals(_editing, j.File)) ShowSelected();
            if (_running == 0) _ = RefreshStorageAsync();
        }
    }

    private void CancelJob(PublishJob j)
    {
        if (j.State == "queued")
        {
            // never started: nothing reached galiluna
            j.State = "cancelled";
            _inFlight.Remove(j.Key);
            SetJob(j, "Cancelled before it started", Theme.TextMuted);
            if (!j.Item.ListView?.IsDisposed ?? false) j.Item.SubItems[2].Text = Describe(j.File);
        }
        else if (j.State == "running")
        {
            SetJob(j, "Cancelling...", Theme.Warning);
            j.Cts.Cancel();
        }
        UpdateQueue();
        UpdateButtons();
    }

    // ------------------------------------------------------------------ galiluna storage

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB" : $"{Math.Max(1, bytes / (1024 * 1024))} MB";

    /// <summary>Asks galiluna what it still stores; the button hides on a galiluna that does not offer it yet.</summary>
    private async Task RefreshStorageAsync()
    {
        var client = ClientFactory();
        if (client is null) { _storage = null; UpdateStorageButton(); return; }
        try { _storage = await client.GetStorageAsync(CancellationToken.None); }
        catch { _storage = null; }
        UpdateStorageButton();
    }

    private void UpdateStorageButton()
    {
        if (IsDisposed) return;
        _clearStorage.Visible = _storage is not null;
        if (_storage is null) return;
        int stored = _storage.Shorts;
        _clearStorage.Text = stored == 0 ? "galiluna storage is empty" : $"Clear galiluna storage ({stored} · {Size(_storage.Bytes)})";
        _clearStorage.Width = TextRenderer.MeasureText(_clearStorage.Text, _clearStorage.Font).Width + 56;
        // never while this app is still uploading: those files are being fetched by the networks
        _clearStorage.Enabled = stored > _storage.Busy && !_jobs.Any(j => j.IsOpen);
    }

    private async Task ClearStorageAsync()
    {
        var client = ClientFactory();
        if (client is null || _storage is null) return;
        if (_jobs.Any(j => j.IsOpen))
        {
            AppDialog.Alert(this, "Uploads still running", "Wait for the queue to finish: the networks are still fetching those videos from galiluna.", AppDialog.Kind.Warning);
            return;
        }
        int clearable = _storage.Shorts - _storage.Busy;
        var details = new List<string> { $"{clearable} video{(clearable == 1 ? "" : "s")} with {(clearable == 1 ? "its cover" : "their covers")}\nabout {Size(_storage.Bytes)} on galiluna's server" };
        var notes = new List<string>
        {
            "The posts stay online: Instagram, TikTok and YouTube keep their own copy.",
            "Your files on this computer are not touched, and publishing a short again uploads it again.",
        };
        if (_storage.Busy > 0) notes.Add($"{_storage.Busy} short{(_storage.Busy == 1 ? "" : "s")} that may still be publishing {(_storage.Busy == 1 ? "is" : "are")} kept.");
        if (!AppDialog.Confirm(this, "Clear galiluna storage?", "Deletes the short videos and covers galiluna keeps for publishing:", "Clear storage",
                danger: true, details: details, notes: notes)) return;
        _clearStorage.Enabled = false;
        _clearStorage.Text = "Clearing...";
        try
        {
            var r = await client.ClearStorageAsync(CancellationToken.None);
            Log($"galiluna storage cleared: {r.Removed} short(s), {Size(r.Bytes)}" + (r.Kept > 0 ? $"; {r.Kept} kept (still publishing)." : "."));
            AppDialog.Alert(this, "Storage cleared", $"{r.Removed} video{(r.Removed == 1 ? "" : "s")} and cover{(r.Removed == 1 ? "" : "s")} deleted from galiluna, about {Size(r.Bytes)}." +
                (r.Kept > 0 ? $" {r.Kept} still publishing {(r.Kept == 1 ? "was" : "were")} kept." : ""), AppDialog.Kind.Success);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, ex.Message, "Could not clear storage", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        await RefreshStorageAsync();
    }

    /// <summary>True while an upload waits or runs: the files on disk must stay until it is done.</summary>
    public bool IsPublishing => _jobs.Any(j => j.IsOpen);

    private IEnumerable<PublishJob> SelectedJobs() => _queue.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<PublishJob>();

    /// <summary>Counts in the queue header and the queue's buttons.</summary>
    private void UpdateQueue()
    {
        int running = _jobs.Count(j => j.State == "running"), waiting = _jobs.Count(j => j.State == "queued");
        int done = _jobs.Count(j => j.State == "done"), failed = _jobs.Count(j => j.State is "failed" or "cancelled");
        var parts = new List<string>();
        if (running > 0) parts.Add($"{running} running");
        if (waiting > 0) parts.Add($"{waiting} waiting");
        if (done > 0) parts.Add($"{done} done");
        if (failed > 0) parts.Add($"{failed} failed or cancelled");
        _queueCounts.Text = string.Join("  ·  ", parts);
        _queueCounts.ForeColor = running + waiting > 0 ? Theme.Purple : failed > 0 ? Theme.Danger : Theme.TextMuted;
        _queueEmpty.Visible = _jobs.Count == 0;
        _cancelJob.Enabled = SelectedJobs().Any(j => j.IsOpen);
        UpdateStorageButton();
        _clearDone.Enabled = _jobs.Any(j => !j.IsOpen);
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
                // direct posting is the default; drafts stay one click away
                _mode.SelectedIndex = 1;
                foreach (var level in t.PrivacyLevels) _privacy.Items.Add(level);
                var pub = t.PrivacyLevels.IndexOf("PUBLIC_TO_EVERYONE");
                if (_privacy.Items.Count > 0) _privacy.SelectedIndex = pub >= 0 ? pub : 0;
                _privacy.Enabled = true;
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
