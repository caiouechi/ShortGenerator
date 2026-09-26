using System.Diagnostics;
using System.Text.Json;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

public sealed class MainForm : Form
{
    // ---- services / state ----
    private AppSettings _settings = SettingsStore.Load();
    private ToolLocator _tools = null!;
    private FfmpegRunner _ffmpeg = null!;
    private VideoDownloader _downloader = null!;
    private Transcriber _transcriber = null!;
    private ShortRenderer _renderer = null!;

    private VideoInfo? _video;
    private Transcript? _transcript;
    private SuggestionResponse? _suggestions;
    private CancellationTokenSource? _cts;

    // ---- top bar ----
    private readonly TextBox _url = new() { PlaceholderText = "Paste a YouTube / Instagram / TikTok link (video, short or reel)...", Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button _download = new() { Text = "Download", Width = 100 };
    private readonly Button _downloadTranscribe = new() { Text = "Download + Transcribe", Width = 170 };
    private readonly Button _openLocal = new() { Text = "Open local file...", Width = 130 };

    // library of downloaded videos (Video tab)
    private readonly ListView _library = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
    private readonly Button _libraryRefresh = new() { Text = "Refresh", Width = 90 };
    private readonly Button _libraryLoad = new() { Text = "Load selected", Width = 120, Enabled = false };
    private readonly Button _libraryTranscribe = new() { Text = "Transcribe selected", Width = 150, Enabled = false };
    private readonly Button _libraryOpenFolder = new() { Text = "Open downloads folder", Width = 160 };
    private readonly Button _settingsBtn = new() { Text = "Settings", Width = 90 };

    // ---- tabs ----
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _tabVideo = new("1. Video");
    private readonly TabPage _tabTranscript = new("2. Transcript");
    private readonly TabPage _tabChatGpt = new("3. Ask ChatGPT");
    private readonly TabPage _tabSuggest = new("4. Short suggestions");
    private readonly TabPage _tabGenerate = new("5. Generate shorts");
    private readonly TabPage _tabEditor = new("6. Edit & preview");

    // editor tab
    private readonly ListView _editClips = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    private readonly ClipPlayer _player = new() { Dock = DockStyle.Fill };
    private readonly Button _playPause = new() { Text = "Play", Width = 80 };
    private readonly TrackBar _timeline = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = 30 };
    private readonly Label _timeLabel = new() { AutoSize = false, Width = 130, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown _clipStart = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 80, Maximum = 100000 };
    private readonly NumericUpDown _clipEnd = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 80, Maximum = 100000 };
    private readonly Button _renderPreview = new() { Text = "Render preview", Width = 130 };
    private readonly Button _generateOne = new() { Text = "Generate this short", Width = 150 };
    private readonly CheckBox _cameraMode = new() { Text = "Camera mode", AutoSize = false, Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter, Width = 110, Height = 28 };
    private readonly Button _autoCamera = new() { Text = "Auto camera (faces)", Width = 150 };
    private readonly Label _keyframeHint = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 7, 0, 0) };
    private readonly ListView _keyframes = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    private readonly ListView _stickers = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, HideSelection = false };
    private readonly ImageList _stickerImages = new() { ImageSize = new Size(56, 56), ColorDepth = ColorDepth.Depth32Bit };
    private readonly ComboBox _stickerAnim = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly NumericUpDown _stickerDuration = new() { Minimum = 0.5M, Maximum = 30, DecimalPlaces = 1, Increment = 0.5M, Value = 2.5M, Width = 55 };
    private readonly Button _stickerAdd = new() { Text = "Add at current time", Width = 140 };
    private readonly Button _stickerAuto = new() { Text = "Auto from reactions", Width = 140 };
    private readonly Button _stickerFolder = new() { Text = "Folder", Width = 60 };
    private readonly Button _keyframeDelete = new() { Text = "Delete", Width = 70 };
    private readonly Button _keyframeClear = new() { Text = "Clear all", Width = 80 };
    private double _playerTime;
    private readonly DataGridView _editSegments = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
    };
    private readonly Button _segDelete = new() { Text = "Delete line", Width = 100 };
    private readonly Button _segPlay = new() { Text = "Play from line", Width = 110 };
    private readonly Label _editorHint = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 4, 8, 4), ForeColor = Color.DimGray };
    private ShortSuggestion? _editing;
    private bool _timelineDragging;
    private bool _syncingTimeline;
    private bool _playerInitStarted;

    // chatgpt tab
    private readonly NumericUpDown _gptCount = new() { Minimum = 1, Maximum = 20, Value = 6, Width = 55 };
    private readonly NumericUpDown _gptMin = new() { Minimum = 5, Maximum = 180, Value = 15, Width = 55 };
    private readonly NumericUpDown _gptMax = new() { Minimum = 10, Maximum = 180, Value = 60, Width = 55 };
    private readonly Button _gptBuild = new() { Text = "Generate prompt", Width = 140 };
    private readonly Button _gptCopy = new() { Text = "Copy prompt", Width = 110, Enabled = false };
    private readonly Button _gptSave = new() { Text = "Save prompt .txt", Width = 130, Enabled = false };
    private readonly Button _gptPaste = new() { Text = "Paste from clipboard", Width = 150 };
    private readonly Button _gptImport = new() { Text = "Import suggestions", Width = 150 };
    private readonly TextBox _gptPrompt = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    private readonly TextBox _gptAnswer = new() { Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, Dock = DockStyle.Fill,
        PlaceholderText = "Paste ChatGPT's JSON answer here, then click 'Import suggestions'." };

    // video tab
    private readonly Label _videoInfo = new() { AutoSize = true, Padding = new Padding(10), UseMnemonic = false };
    private readonly Button _openFile = new() { Text = "Play video", Width = 110, Enabled = false };
    private readonly Button _openFolder = new() { Text = "Open folder", Width = 110, Enabled = false };

    // transcript tab
    private readonly Button _transcribe = new() { Text = "Transcribe", Width = 110 };
    private readonly ComboBox _whisperModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _language = new() { Width = 60 };
    private readonly CheckBox _detectReactions = new() { Text = "Detect laughs / reactions", AutoSize = true };
    private readonly Button _loadTranscript = new() { Text = "Load transcript file...", Width = 150, Enabled = false };
    private readonly Button _saveSrt = new() { Text = "Save .srt", Width = 90, Enabled = false };
    private readonly Button _saveTxt = new() { Text = "Save .txt", Width = 90, Enabled = false };
    private readonly ListView _segments = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

    // suggestions tab
    private readonly Button _analyze = new() { Text = "Analyze with Claude", Width = 150, Enabled = false };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 20, Width = 55 };
    private readonly NumericUpDown _minSec = new() { Minimum = 5, Maximum = 180, Width = 55 };
    private readonly NumericUpDown _maxSec = new() { Minimum = 10, Maximum = 180, Width = 55 };
    private readonly Button _addClip = new() { Text = "Add custom clip", Width = 120, Enabled = false };
    private readonly Button _editClip = new() { Text = "Adjust times", Width = 100, Enabled = false };
    private readonly Button _removeClip = new() { Text = "Remove", Width = 80, Enabled = false };
    private readonly Button _previewClip = new() { Text = "Preview clip", Width = 100, Enabled = false };
    private readonly ListView _suggestList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, GridLines = true, HideSelection = false };
    private readonly RichTextBox _suggestDetail = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(6), ForeColor = Color.DimGray };

    // generate tab
    private readonly CheckBox _addCaptions = new() { Text = "Burn captions into the video", Checked = true, AutoSize = true };
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _styleDesc = new() { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.DimGray };
    private readonly NumericUpDown _wordsPerCaption = new() { Minimum = 1, Maximum = 8, Value = 3, Width = 60 };
    private readonly NumericUpDown _fontSize = new() { Minimum = 0, Maximum = 200, Value = 0, Width = 60 };
    private readonly ComboBox _crop = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _burnHook = new() { Text = "Show the hook as a title at the start", AutoSize = true };
    private readonly CheckBox _includeReactions = new() { Text = "Show laughs / reactions in captions, e.g. [laughs]", AutoSize = true };
    private readonly CheckBox _autoCameraOpt = new() { Text = "Auto camera: follow faces (vertical crop)", Checked = true, AutoSize = true };
    private readonly TextBox _outputFolder = new() { Width = 230 };
    private readonly Button _generate = new() { Text = "Generate selected shorts", Width = 190, Height = 34, Enabled = false, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    private readonly CaptionPreview _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30) };
    private readonly ListView _results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

    // bottom
    private readonly BrandProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly BrandHeader _header = new();
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 80, Enabled = false };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(250, 250, 250) };

    public MainForm()
    {
        Text = "Galiluna Short Generator";
        MinimumSize = new Size(1000, 720);
        ClientSize = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.Body();

        RebuildServices();
        BuildLayout();
        WireEvents();
        ApplyTheme();
        Log("Ready. Paste a link and click Download, or open a local video file.");
        CheckTools();
    }

    public void SelectTab(int index)
    {
        try
        {
            if (index >= 0 && index < _tabs.TabPages.Count) _tabs.SelectedIndex = index;
        }
        catch (Exception ex) { Log("Could not switch tab: " + ex); }
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        // top bar
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 6, Padding = new Padding(8, 8, 8, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Video link:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
        top.Controls.Add(_url, 1, 0);
        top.Controls.Add(_download, 2, 0);
        top.Controls.Add(_downloadTranscribe, 3, 0);
        top.Controls.Add(_openLocal, 4, 0);
        top.Controls.Add(_settingsBtn, 5, 0);

        // tabs
        _tabs.TabPages.AddRange(new[] { _tabVideo, _tabTranscript, _tabChatGpt, _tabSuggest, _tabGenerate, _tabEditor });
        BuildVideoTab();
        BuildTranscriptTab();
        BuildChatGptTab();
        BuildSuggestTab();
        BuildEditorTab();
        BuildGenerateTab();

        // bottom
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 140, ColumnCount = 3, RowCount = 2, Padding = new Padding(8, 0, 8, 6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.Controls.Add(_progress, 0, 0);
        bottom.Controls.Add(_status, 1, 0);
        bottom.Controls.Add(_cancel, 2, 0);
        bottom.Controls.Add(_log, 0, 1);
        bottom.SetColumnSpan(_log, 3);

        var tabHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        tabHost.Controls.Add(_tabs);

        Controls.Add(tabHost);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_header);
    }

    private void ApplyTheme()
    {
        foreach (var b in new[] { _download, _downloadTranscribe, _transcribe, _analyze, _generate, _libraryTranscribe, _gptBuild, _gptImport, _playPause, _generateOne }) Theme.Primary(b);
        Theme.Apply(this);
        _videoInfo.Font = Theme.Mono(9.5f);
        _videoInfo.ForeColor = Theme.TextSecondary;
        _log.Font = Theme.Mono();
        _log.BackColor = Theme.SurfaceSoft;
        _log.ForeColor = Theme.TextSecondary;
        _status.ForeColor = Theme.TextMuted;
        _suggestDetail.Font = Theme.Body(9.5f);
        _gptPrompt.Font = Theme.Mono(9f);
        _gptAnswer.Font = Theme.Mono(9f);
        _gptAnswer.BackColor = Theme.Elevated;
        _summary.ForeColor = Theme.TextMuted;
        _summary.BackColor = Theme.SurfaceSoft;
        _preview.BackColor = Theme.DarkBg;
    }

    private void BuildVideoTab()
    {
        // Top: current video details. Bottom: library of already downloaded videos.
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 165 };

        var info = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_openFile);
        buttons.Controls.Add(_openFolder);
        _videoInfo.Text = "No video loaded yet.\n\nPaste a link above and click Download (or Download + Transcribe), pick a downloaded video from the library below, or open a local file.\nSupported: YouTube videos & Shorts, Instagram Reels / posts, TikTok, and any other site yt-dlp knows.";
        info.Controls.Add(_videoInfo);
        info.Controls.Add(buttons);
        split.Panel1.Controls.Add(info);

        var libBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 5, 6, 0), WrapContents = false };
        libBar.Controls.Add(new Label { Text = "Downloaded videos", AutoSize = true, Margin = new Padding(0, 7, 14, 0), Font = Theme.HeadingFont(9.5f) });
        libBar.Controls.Add(_libraryTranscribe);
        libBar.Controls.Add(_libraryLoad);
        libBar.Controls.Add(_libraryRefresh);
        libBar.Controls.Add(_libraryOpenFolder);

        _library.Columns.Add("File", 520);
        _library.Columns.Add("Length", 80);
        _library.Columns.Add("Transcript", 90);
        _library.Columns.Add("Shorts", 70);
        _library.Columns.Add("Downloaded", 140);
        _library.Columns.Add("Size", 80);

        split.Panel2.Controls.Add(_library);
        split.Panel2.Controls.Add(libBar);
        _tabVideo.Controls.Add(split);
    }

    private void BuildTranscriptTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 5, 6, 0), WrapContents = false };
        foreach (var m in Transcriber.ModelNames) _whisperModel.Items.Add(Transcriber.DescribeModel(m));
        _whisperModel.SelectedIndex = Math.Max(0, Array.IndexOf(Transcriber.ModelNames, _settings.WhisperModel));
        _language.Text = _settings.WhisperLanguage;
        bar.Controls.Add(_transcribe);
        bar.Controls.Add(new Label { Text = "Whisper model:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_whisperModel);
        bar.Controls.Add(new Label { Text = "Language:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_language);
        _detectReactions.Checked = _settings.DetectReactions;
        _detectReactions.Margin = new Padding(14, 7, 4, 0);
        bar.Controls.Add(_detectReactions);
        bar.Controls.Add(new Label { Text = "", Width = 20 });
        bar.Controls.Add(_loadTranscript);
        bar.Controls.Add(_saveSrt);
        bar.Controls.Add(_saveTxt);

        _segments.Columns.Add("Start", 80);
        _segments.Columns.Add("End", 80);
        _segments.Columns.Add("Text", 820);
        _segments.Columns.Add("Peak dB", 70);

        _tabTranscript.Controls.Add(_segments);
        _tabTranscript.Controls.Add(bar);
    }

    private void BuildChatGptTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 5, 6, 0), WrapContents = false };
        bar.Controls.Add(_gptBuild);
        bar.Controls.Add(new Label { Text = "Shorts:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_gptCount);
        bar.Controls.Add(new Label { Text = "Length (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_gptMin);
        bar.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_gptMax);
        bar.Controls.Add(new Label { Text = "", Width = 20 });
        bar.Controls.Add(_gptCopy);
        bar.Controls.Add(_gptSave);

        var help = new Label
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 4, 8, 4), ForeColor = Color.DimGray,
            Text = "No API key needed. 1) Generate the prompt and copy it.  2) Paste it into ChatGPT (or any assistant) and send.  " +
                   "3) Paste the JSON answer on the right and click Import. The clips, the reasoning and the /10 score appear in '4. Short suggestions'."
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        // The control has no real width yet (min sizes and distance would be rejected), so configure it once laid out.
        bool splitSet = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitSet || split.Width < 700) return;
            splitSet = true;
            split.Panel1MinSize = 250;
            split.Panel2MinSize = 330;
            split.SplitterDistance = split.Width / 2;
        };

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        left.Controls.Add(_gptPrompt);
        left.Controls.Add(new Label { Text = "Prompt for ChatGPT", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var rightBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        rightBar.Controls.Add(_gptImport);
        rightBar.Controls.Add(_gptPaste);
        right.Controls.Add(_gptAnswer);
        right.Controls.Add(rightBar);
        right.Controls.Add(new Label { Text = "ChatGPT's answer", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
        split.Panel2.Controls.Add(right);

        _tabChatGpt.Controls.Add(split);
        _tabChatGpt.Controls.Add(help);
        _tabChatGpt.Controls.Add(bar);
    }

    private void BuildSuggestTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 5, 6, 0), WrapContents = false };
        _count.Value = Math.Clamp(_settings.SuggestionCount, 1, 20);
        _minSec.Value = Math.Clamp(_settings.MinShortSeconds, 5, 180);
        _maxSec.Value = Math.Clamp(_settings.MaxShortSeconds, 10, 180);
        bar.Controls.Add(_analyze);
        bar.Controls.Add(new Label { Text = "Shorts:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_count);
        bar.Controls.Add(new Label { Text = "Length (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_minSec);
        bar.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_maxSec);
        bar.Controls.Add(new Label { Text = "", Width = 20 });
        bar.Controls.Add(_previewClip);
        bar.Controls.Add(_editClip);
        bar.Controls.Add(_addClip);
        bar.Controls.Add(_removeClip);

        _suggestList.Columns.Add("Use", 40);
        _suggestList.Columns.Add("#", 30);
        _suggestList.Columns.Add("Title", 360);
        _suggestList.Columns.Add("Start", 70);
        _suggestList.Columns.Add("End", 70);
        _suggestList.Columns.Add("Length", 60);
        _suggestList.Columns.Add("Viral score", 95);
        _suggestList.Columns.Add("Emotion", 110);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        split.Panel1.Controls.Add(_suggestList);
        split.Panel2.Controls.Add(_suggestDetail);
        split.Panel2.Controls.Add(_summary);
        _suggestDetail.Text = "Get suggestions from '3. Ask ChatGPT' (copy/paste) or click 'Analyze with Claude' (API key). Select one to read why it could go viral.";

        _tabSuggest.Controls.Add(split);
        _tabSuggest.Controls.Add(bar);
    }

    private void BuildEditorTab()
    {
        _editorHint.Text = "Pick a short on the left. Drag the caption on the video to place it from the current time on. 'Camera mode' shows the whole frame: drag the 9:16 box and scroll to zoom to add a camera cut at the current time. " +
                           "'Auto camera' follows faces. Fix wrong words in the Text column (F2 or start typing). Style, framing and words-per-caption come from '5. Generate shorts'.";

        // left: list of selected shorts + timeline of camera cuts and caption positions
        var left = new Panel { Dock = DockStyle.Left, Width = 290, Padding = new Padding(6) };
        _editClips.Columns.Add("Short", 170);
        _editClips.Columns.Add("Range", 100);
        _keyframes.Columns.Add("At", 50);
        _keyframes.Columns.Add("What", 60);
        _keyframes.Columns.Add("Details", 160);
        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 120 };
        var clipsHost = new Panel { Dock = DockStyle.Fill };
        clipsHost.Controls.Add(_editClips);
        clipsHost.Controls.Add(new Label { Text = "Selected shorts", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
        var kfHost = new Panel { Dock = DockStyle.Fill };
        var kfBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };
        kfBar.Controls.Add(_keyframeDelete);
        kfBar.Controls.Add(_keyframeClear);
        kfHost.Controls.Add(_keyframes);
        kfHost.Controls.Add(kfBar);
        kfHost.Controls.Add(new Label { Text = "Camera cuts and caption positions (double-click to jump)", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, UseMnemonic = false });
        leftSplit.Panel1.Controls.Add(clipsHost);
        leftSplit.Panel2.Controls.Add(kfHost);
        left.Controls.Add(leftSplit);

        // right: transcript lines of the clip
        var right = new Panel { Dock = DockStyle.Right, Width = 420, Padding = new Padding(6) };
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "Start", HeaderText = "Start", ReadOnly = true, FillWeight = 18 });
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "End", HeaderText = "End", ReadOnly = true, FillWeight = 18 });
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Text (editable)", FillWeight = 64 });
        _editSegments.Columns["Text"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _editSegments.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        var segBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        segBar.Controls.Add(_segPlay);
        segBar.Controls.Add(_segDelete);
        var segHost = new Panel { Dock = DockStyle.Fill };
        segHost.Controls.Add(_editSegments);
        segHost.Controls.Add(segBar);
        segHost.Controls.Add(new Label { Text = "Transcript of this short", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });

        // stickers gallery
        _stickers.LargeImageList = _stickerImages;
        _stickerAnim.Items.AddRange(new object[] { "pop", "float", "shake", "none" });
        _stickerAnim.SelectedIndex = 0;
        var stBar1 = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };
        stBar1.Controls.Add(_stickerAdd);
        stBar1.Controls.Add(_stickerAuto);
        stBar1.Controls.Add(_stickerFolder);
        var stBar2 = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false };
        stBar2.Controls.Add(new Label { Text = "Animation:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        stBar2.Controls.Add(_stickerAnim);
        stBar2.Controls.Add(new Label { Text = "Seconds:", AutoSize = true, Margin = new Padding(10, 7, 4, 0) });
        stBar2.Controls.Add(_stickerDuration);
        var stHost = new Panel { Dock = DockStyle.Fill };
        stHost.Controls.Add(_stickers);
        stHost.Controls.Add(stBar2);
        stHost.Controls.Add(stBar1);
        stHost.Controls.Add(new Label { Text = "Stickers (double-click to add, then drag it on the video; scroll to resize)", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });

        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 215 };
        rightSplit.Panel1.Controls.Add(segHost);
        rightSplit.Panel2.Controls.Add(stHost);
        right.Controls.Add(rightSplit);

        // center: player + controls
        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var controls = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 112, ColumnCount = 3, RowCount = 3 };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        controls.Controls.Add(_playPause, 0, 0);
        controls.Controls.Add(_timeline, 1, 0);
        controls.Controls.Add(_timeLabel, 2, 0);
        var trim = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        trim.Controls.Add(new Label { Text = "Start (s):", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        trim.Controls.Add(_clipStart);
        trim.Controls.Add(new Label { Text = "End (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        trim.Controls.Add(_clipEnd);
        trim.Controls.Add(new Label { Text = "", Width = 16 });
        trim.Controls.Add(_renderPreview);
        trim.Controls.Add(_generateOne);
        controls.Controls.Add(trim, 0, 1);
        controls.SetColumnSpan(trim, 3);
        var camRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        camRow.Controls.Add(_cameraMode);
        camRow.Controls.Add(_autoCamera);
        camRow.Controls.Add(_keyframeHint);
        controls.Controls.Add(camRow, 0, 2);
        controls.SetColumnSpan(camRow, 3);
        center.Controls.Add(_player);
        center.Controls.Add(controls);

        _tabEditor.Controls.Add(center);
        _tabEditor.Controls.Add(right);
        _tabEditor.Controls.Add(left);
        _tabEditor.Controls.Add(_editorHint);
    }

    private void BuildGenerateTab()
    {
        var options = new TableLayoutPanel { Dock = DockStyle.Left, Width = 410, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Add(string label, Control c)
        {
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 4) }, 0, row);
            c.Margin = new Padding(0, 5, 0, 3);
            options.Controls.Add(c, 1, row++);
        }

        foreach (var s in CaptionStyle.All) _style.Items.Add(s.Name);
        _style.SelectedIndex = Math.Max(0, CaptionStyle.All.ToList().FindIndex(s => s.Id == "bold-pop"));
        _crop.Items.AddRange(new object[] { "Vertical 9:16 - center crop", "Vertical 9:16 - blurred background", "Keep original aspect ratio" });
        _crop.SelectedIndex = 0;
        _outputFolder.Text = _settings.OutputFolder;

        var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var browse = new Button { Text = "...", Width = 32 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : "" };
            if (d.ShowDialog(this) == DialogResult.OK) _outputFolder.Text = d.SelectedPath;
        };
        folderRow.Controls.Add(_outputFolder); folderRow.Controls.Add(browse);

        Add("Framing", _crop);
        Add("", _autoCameraOpt);
        Add("", _addCaptions);
        Add("Caption style", _style);
        Add("", _styleDesc);
        Add("Words per caption", _wordsPerCaption);
        Add("Font size", _fontSize);
        Add("", new Label { Text = "0 = style default. Sizes are for a 1080x1920 frame.", AutoSize = true, ForeColor = Color.DimGray });
        Add("", _burnHook);
        Add("", _includeReactions);
        Add("Output folder", folderRow);
        Add("", _generate);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330 };
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        previewHost.Controls.Add(_preview);
        previewHost.Controls.Add(new Label { Text = "Caption preview (approximate)", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
        right.Panel1.Controls.Add(previewHost);

        _results.Columns.Add("Short", 300);
        _results.Columns.Add("Status", 90);
        _results.Columns.Add("File", 500);
        var resultsHost = new Panel { Dock = DockStyle.Fill };
        resultsHost.Controls.Add(_results);
        resultsHost.Controls.Add(new Label { Text = "Generated files (double-click to play)", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Padding = new Padding(4, 0, 0, 0) });
        right.Panel2.Controls.Add(resultsHost);

        _tabGenerate.Controls.Add(right);
        _tabGenerate.Controls.Add(options);
        RefreshPreview();
    }

    // ------------------------------------------------------------------ events

    private void WireEvents()
    {
        _download.Click += async (_, _) => await DownloadAsync(thenTranscribe: false);
        _downloadTranscribe.Click += async (_, _) => await DownloadAsync(thenTranscribe: true);
        _url.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await DownloadAsync(thenTranscribe: false); } };
        _openLocal.Click += async (_, _) => await OpenLocalAsync();

        _libraryRefresh.Click += (_, _) => RefreshLibrary();
        _libraryOpenFolder.Click += (_, _) => { Directory.CreateDirectory(_settings.DownloadFolder); OpenPath(_settings.DownloadFolder); };
        _library.SelectedIndexChanged += (_, _) => _libraryLoad.Enabled = _libraryTranscribe.Enabled = _library.SelectedItems.Count > 0 && _cts is null;
        _library.DoubleClick += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: false);
        _libraryLoad.Click += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: false);
        _libraryTranscribe.Click += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: true);
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_tabs.SelectedTab == _tabVideo) RefreshLibrary();
            else if (_tabs.SelectedTab == _tabEditor) await EnterEditorAsync();
            else await _player.PauseAsync();
        };

        // editor tab
        _editClips.SelectedIndexChanged += async (_, _) => await LoadClipInEditorAsync();
        _playPause.Click += async (_, _) => await _player.TogglePlayAsync();
        _player.PlayingChanged += playing => _playPause.Text = playing ? "Pause" : "Play";
        _player.TimeChanged += OnPlayerTime;
        _player.Status += s => Log("Player: " + s);
        _player.CaptionMoved += (x, y) => BeginInvoke(() => OnCaptionMoved(x, y));
        _player.CameraMoved += (x, y, z) => BeginInvoke(() => OnCameraMoved(x, y, z));
        _player.OverlayMoved += (id, x, y, size) => BeginInvoke(() => OnOverlayMoved(id, x, y, size));
        _stickers.DoubleClick += async (_, _) => await AddStickerAsync();
        _stickerAdd.Click += async (_, _) => await AddStickerAsync();
        _stickerAuto.Click += async (_, _) => await AutoStickersAsync();
        _stickerFolder.Click += (_, _) => { StickerLibrary.EnsureSeeded(); OpenPath(StickerLibrary.Folder); };
        _cameraMode.CheckedChanged += async (_, _) =>
        {
            _cameraMode.BackColor = _cameraMode.Checked ? Theme.Nebula : Theme.Elevated;
            _cameraMode.ForeColor = _cameraMode.Checked ? Color.White : Theme.Heading;
            if (_cameraMode.Checked) await _player.PauseAsync();
            await _player.SetCameraModeAsync(_cameraMode.Checked);
        };
        _autoCamera.Click += async (_, _) => await AutoCameraAsync();
        _keyframes.DoubleClick += async (_, _) =>
        {
            if (_editing is not null && _keyframes.SelectedItems.Count > 0 && _keyframes.SelectedItems[0].Tag is IKeyframe k)
                await _player.SeekAsync(_editing.StartSeconds + k.Time);
        };
        _keyframeDelete.Click += async (_, _) => await DeleteKeyframeAsync();
        _keyframeClear.Click += async (_, _) =>
        {
            if (_editing is null) return;
            if (MessageBox.Show(this, "Remove all camera cuts, caption positions and stickers of this short?", "Clear all", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _editing.Camera.Clear(); _editing.CaptionPositions.Clear(); _editing.Overlays.Clear();
            await PushKeyframesAsync();
        };
        _timeline.MouseDown += (_, _) => _timelineDragging = true;
        _timeline.MouseUp += async (_, _) => { _timelineDragging = false; await SeekFromTimelineAsync(); };
        _timeline.Scroll += async (_, _) => { if (_timelineDragging) await SeekFromTimelineAsync(); };
        _clipStart.ValueChanged += async (_, _) => await ApplyClipTimesAsync();
        _clipEnd.ValueChanged += async (_, _) => await ApplyClipTimesAsync();
        _renderPreview.Click += async (_, _) => await RenderEditorPreviewAsync();
        _editSegments.CellEndEdit += (_, e) => OnSegmentEdited(e.RowIndex);
        _editSegments.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 2) await PlayFromRowAsync(e.RowIndex); };
        _segPlay.Click += async (_, _) => { if (_editSegments.CurrentRow is not null) await PlayFromRowAsync(_editSegments.CurrentRow.Index); };
        _segDelete.Click += (_, _) => DeleteSegmentRow();
        Shown += (_, _) => RefreshLibrary();
        _settingsBtn.Click += (_, _) => OpenSettings();
        _cancel.Click += (_, _) => _cts?.Cancel();

        _openFile.Click += (_, _) => { if (_video is not null) OpenPath(_video.FilePath); };
        _openFolder.Click += (_, _) => { if (_video is not null) OpenPath(Path.GetDirectoryName(_video.FilePath)!); };

        _transcribe.Click += async (_, _) => await TranscribeAsync();
        _loadTranscript.Click += (_, _) => LoadTranscriptFile();
        _saveSrt.Click += (_, _) => SaveTranscript(true);
        _saveTxt.Click += (_, _) => SaveTranscript(false);
        _segments.DoubleClick += (_, _) => PreviewSelectedSegment();

        _gptBuild.Click += (_, _) => BuildChatGptPrompt();
        _gptCopy.Click += (_, _) => { try { Clipboard.SetText(_gptPrompt.Text); _status.Text = "Prompt copied to clipboard. Paste it into ChatGPT."; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed"); } };
        _gptSave.Click += (_, _) => SaveChatGptPrompt();
        _gptPaste.Click += (_, _) => { try { if (Clipboard.ContainsText()) _gptAnswer.Text = Clipboard.GetText(); } catch { } };
        _gptImport.Click += (_, _) => ImportChatGptAnswer();

        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _suggestList.SelectedIndexChanged += (_, _) => ShowSuggestionDetail();
        _suggestList.ItemChecked += (_, e) =>
        {
            if (e.Item.Tag is ShortSuggestion s && s.Selected != e.Item.Checked)
            {
                s.Selected = e.Item.Checked;
                if (!_populatingSuggestions) SaveProject(); // ticks survive a restart
            }
            UpdateGenerateEnabled();
        };
        _suggestList.DoubleClick += (_, _) => EditSelectedClip();
        _editClip.Click += (_, _) => EditSelectedClip();
        _addClip.Click += (_, _) => AddCustomClip();
        _removeClip.Click += (_, _) => RemoveSelectedClip();
        _previewClip.Click += async (_, _) => await PreviewSelectedClipAsync();

        _style.SelectedIndexChanged += (_, _) => RefreshPreview();
        _wordsPerCaption.ValueChanged += (_, _) => RefreshPreview();
        _fontSize.ValueChanged += (_, _) => RefreshPreview();
        _addCaptions.CheckedChanged += (_, _) => { _style.Enabled = _wordsPerCaption.Enabled = _fontSize.Enabled = _burnHook.Enabled = _includeReactions.Enabled = _addCaptions.Checked; RefreshPreview(); };
        _includeReactions.CheckedChanged += (_, _) => RefreshPreview();
        _crop.SelectedIndexChanged += (_, _) => RefreshPreview();
        _generate.Click += async (_, _) => await GenerateAsync();
        _generateOne.Click += async (_, _) =>
        {
            if (_editing is null) { MessageBox.Show(this, "Pick a short first.", "Generate"); return; }
            await _player.PauseAsync();
            var s = _editing;
            await GenerateAsync(new[] { s });
        };
        _results.DoubleClick += (_, _) => { if (_results.SelectedItems.Count > 0 && _results.SelectedItems[0].Tag is string p && File.Exists(p)) OpenPath(p); };
    }

    private void RebuildServices()
    {
        _tools = new ToolLocator(_settings.ToolsFolder);
        _ffmpeg = new FfmpegRunner(_tools);
        _downloader = new VideoDownloader(_tools, _ffmpeg);
        _transcriber = new Transcriber(_ffmpeg, Path.Combine(SettingsStore.AppDataFolder, "whisper-models"));
        _renderer = new ShortRenderer(_ffmpeg);
    }

    private void CheckTools()
    {
        var missing = new List<string>();
        if (_tools.YtDlpPath is null) missing.Add("yt-dlp");
        if (_tools.FfmpegPath is null) missing.Add("ffmpeg");
        if (missing.Count > 0)
            Log($"Missing tools: {string.Join(", ", missing)}. Open Settings and click 'Download missing tools'.");
        else
            Log($"Tools OK. yt-dlp: {_tools.YtDlpPath}  |  ffmpeg: {_tools.FfmpegPath}");
        if (SettingsStore.GetApiKey(_settings) is null)
            Log("No Anthropic API key configured. Set it in Settings before using 'Analyze with Claude'.");
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings = SettingsStore.Load();
            RebuildServices();
            _whisperModel.SelectedIndex = Math.Max(0, Array.IndexOf(Transcriber.ModelNames, _settings.WhisperModel));
            _language.Text = _settings.WhisperLanguage;
            _count.Value = Math.Clamp(_settings.SuggestionCount, 1, 20);
            _minSec.Value = Math.Clamp(_settings.MinShortSeconds, 5, 180);
            _maxSec.Value = Math.Clamp(_settings.MaxShortSeconds, 10, 180);
            if (string.IsNullOrWhiteSpace(_outputFolder.Text)) _outputFolder.Text = _settings.OutputFolder;
            CheckTools();
        }
    }

    // ------------------------------------------------------------------ step 1: download

    /// <summary>Downloads the link in the URL box. With <paramref name="thenTranscribe"/> it continues straight into Whisper.</summary>
    private async Task DownloadAsync(bool thenTranscribe)
    {
        var url = _url.Text.Trim();
        if (!VideoDownloader.LooksLikeUrl(url))
        {
            MessageBox.Show(this, "Please paste a valid http(s) link.", "Invalid link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_tools.YtDlpPath is null)
        {
            if (MessageBox.Show(this, "yt-dlp.exe is not installed yet. Download it now into the tools folder?", "Missing yt-dlp",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunBusyAsync("Downloading tools", async ct => await _tools.DownloadMissingToolsAsync(new Progress<string>(Log), ct));
            if (_tools.YtDlpPath is null) return;
        }

        await RunBusyAsync(thenTranscribe ? "Downloading + transcribing" : "Downloading video", async ct =>
        {
            var info = await _downloader.DownloadAsync(url, _settings.DownloadFolder, ProgressReporter(), new Progress<string>(Log), ct);
            SetVideo(info);
            RefreshLibrary();
            if (thenTranscribe) await TranscribeCoreAsync(ct);
        });
    }

    private async Task OpenLocalAsync()
    {
        using var d = new OpenFileDialog
        {
            Title = "Open a video file",
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v|All files|*.*"
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await OpenLocalFileAsync(d.FileName);
    }

    public async Task OpenLocalFileAsync(string path)
    {
        await RunBusyAsync("Reading video", async ct =>
        {
            var info = await _downloader.LoadLocalAsync(path, ct);
            SetVideo(info);
        });
    }

    // ---- library of downloaded videos ----

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v" };

    private void RefreshLibrary()
    {
        var selectedPath = _library.SelectedItems.Count > 0 ? _library.SelectedItems[0].Tag as string : null;
        _library.BeginUpdate();
        _library.Items.Clear();
        try
        {
            if (Directory.Exists(_settings.DownloadFolder))
            {
                var files = new DirectoryInfo(_settings.DownloadFolder).EnumerateFiles()
                    .Where(f => VideoExtensions.Contains(f.Extension))
                    .OrderByDescending(f => f.LastWriteTime);
                foreach (var f in files)
                {
                    var project = TryLoadProject(f.FullName, out var p) ? p : null;
                    bool hasTranscript = project?.Transcript is { Segments.Count: > 0 };
                    int shorts = project?.Suggestions?.Shorts.Count ?? 0;
                    double duration = project?.Video?.DurationSeconds ?? 0;
                    var item = new ListViewItem(new[]
                    {
                        Path.GetFileNameWithoutExtension(f.Name),
                        duration > 0 ? Fmt(duration) : "",
                        hasTranscript ? "yes" : "no",
                        shorts > 0 ? shorts.ToString() : "",
                        f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        $"{f.Length / 1024.0 / 1024.0:F0} MB"
                    }) { Tag = f.FullName };
                    if (hasTranscript) item.ForeColor = Theme.Text; else item.ForeColor = Theme.TextSecondary;
                    if (f.FullName == selectedPath || (_video is not null && f.FullName == _video.FilePath)) item.Selected = true;
                    _library.Items.Add(item);
                }
            }
        }
        catch (Exception ex) { Log("Could not read the downloads folder: " + ex.Message); }
        _library.EndUpdate();
        _libraryLoad.Enabled = _libraryTranscribe.Enabled = _library.SelectedItems.Count > 0 && _cts is null;
        ProbeMissingDurationsAsync();
    }

    /// <summary>Fills in the Length column for library files that have no saved project (runs ffprobe in the background).</summary>
    private async void ProbeMissingDurationsAsync()
    {
        var pending = _library.Items.Cast<ListViewItem>().Where(i => i.SubItems[1].Text == "" && i.Tag is string).ToList();
        foreach (var item in pending)
        {
            try
            {
                var path = (string)item.Tag!;
                var probe = await _ffmpeg.ProbeAsync(path, CancellationToken.None);
                if (item.ListView is not null && probe.Duration > 0) item.SubItems[1].Text = Fmt(probe.Duration);
            }
            catch { /* best effort */ }
        }
    }

    private async Task LoadFromLibraryAsync(bool thenTranscribe)
    {
        if (_library.SelectedItems.Count == 0 || _library.SelectedItems[0].Tag is not string path) return;
        if (!File.Exists(path)) { RefreshLibrary(); return; }

        await RunBusyAsync(thenTranscribe ? "Transcribing" : "Reading video", async ct =>
        {
            if (_video?.FilePath != path)
            {
                var info = await _downloader.LoadLocalAsync(path, ct);
                if (TryLoadProject(path, out var p) && p.Video is not null)
                {
                    // Keep the original title / source / creator from the download.
                    info.Title = p.Video.Title; info.Uploader = p.Video.Uploader; info.Url = p.Video.Url; info.Source = p.Video.Source;
                }
                SetVideo(info);
            }
            if (thenTranscribe)
            {
                if (_transcript is { Segments.Count: > 0 } &&
                    MessageBox.Show(this, "This video already has a transcript. Transcribe it again?", "Transcript exists",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    _tabs.SelectedTab = _tabTranscript;
                    return;
                }
                await TranscribeCoreAsync(ct);
            }
        });
    }

    private void SetVideo(VideoInfo info)
    {
        _video = info;
        _transcript = null;
        _suggestions = null;
        _editing = null;
        _segments.Items.Clear();
        _suggestList.Items.Clear();
        _results.Items.Clear();
        _editClips.Items.Clear();
        _keyframes.Items.Clear();
        _editSegments.Rows.Clear();
        _suggestDetail.Text = "";
        _summary.Text = "";

        // Remember this video so the next start reopens it with everything that was saved for it.
        _settings.LastVideoPath = info.FilePath;
        try { SettingsStore.Save(_settings); } catch { }

        _generated = new List<GeneratedFile>();
        bool hasProject = TryLoadProject(info.FilePath, out var project);
        // A locally opened file keeps the title / creator / source recorded when it was downloaded.
        if (hasProject && project.Video is { } pv && info.Source == VideoSource.LocalFile && !string.IsNullOrWhiteSpace(pv.Title))
        {
            info.Title = pv.Title; info.Uploader = pv.Uploader; info.Url = pv.Url; info.Source = pv.Source;
        }

        var dur = TimeSpan.FromSeconds(info.DurationSeconds);
        _videoInfo.Text =
            $"Title:       {info.Title}\n" +
            $"Creator:     {(string.IsNullOrWhiteSpace(info.Uploader) ? "-" : info.Uploader)}\n" +
            $"Source:      {info.Source}\n" +
            $"Duration:    {(int)dur.TotalMinutes:00}:{dur.Seconds:00}\n" +
            $"Resolution:  {(info.Width > 0 ? $"{info.Width}x{info.Height} ({(info.IsVertical ? "vertical" : "landscape")})" : "unknown")}\n" +
            $"File:        {info.FilePath}\n\n" +
            "Next: go to '2. Transcript' and click Transcribe, then pick '3. Ask ChatGPT' (copy/paste, free) or 'Analyze with Claude' (API key).";
        _openFile.Enabled = _openFolder.Enabled = true;
        _loadTranscript.Enabled = true;
        _addClip.Enabled = true;
        _analyze.Enabled = false;
        _saveSrt.Enabled = _saveTxt.Enabled = false;

        // Restore a previous session for this file (transcript, suggestions, generated files) if one exists.
        if (hasProject)
        {
            if (project.Transcript is { Segments.Count: > 0 })
            {
                _transcript = project.Transcript;
                // Transcripts saved by earlier runs may still contain a Whisper repetition loop; clean on load.
                int removed = Transcriber.RemoveRepetitionLoops(_transcript);
                ShowTranscript();
                Log(removed > 0 ? $"Restored saved transcript and removed {removed} repeated segments." : "Restored saved transcript.");
                if (removed > 0) SaveProject();
            }
            if (project.Suggestions is { Shorts.Count: > 0 })
            {
                _suggestions = project.Suggestions;
                foreach (var s in _suggestions.Shorts) s.MigrateLegacyCaptionPosition();
                ShowSuggestions();
                Log("Restored saved suggestions.");
            }
            _generated = project.Generated ?? new List<GeneratedFile>();
            if (project.Generated is { Count: > 0 })
            {
                int shown = 0;
                foreach (var g in project.Generated.Where(g => File.Exists(g.Path)))
                {
                    _results.Items.Add(new ListViewItem(new[] { g.Title, "done", g.Path }) { Tag = g.Path });
                    shown++;
                }
                if (shown > 0) Log($"Restored {shown} generated short(s).");
            }
        }
        UpdateGenerateEnabled();
        _tabs.SelectedTab = _transcript is null ? _tabVideo : (_suggestions is null ? _tabSuggest : _tabGenerate);
    }

    /// <summary>Reopens the video used last time, if it still exists.</summary>
    public async Task ReopenLastVideoAsync()
    {
        var last = _settings.LastVideoPath;
        if (string.IsNullOrWhiteSpace(last) || !File.Exists(last)) return;
        Log($"Reopening last video: {Path.GetFileName(last)}");
        await OpenLocalFileAsync(last);
    }

    // ------------------------------------------------------------------ step 2: transcript

    private async Task TranscribeAsync()
    {
        if (_video is null)
        {
            // No video loaded yet: transcribe straight from the link if there is one.
            if (VideoDownloader.LooksLikeUrl(_url.Text))
            {
                await DownloadAsync(thenTranscribe: true);
                return;
            }
            MessageBox.Show(this,
                "Load a video first: paste a link and click 'Download + Transcribe', pick one from the downloaded videos list, or open a local file.",
                "No video", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _tabs.SelectedTab = _tabVideo;
            return;
        }
        await RunBusyAsync("Transcribing", TranscribeCoreAsync);
    }

    /// <summary>Runs Whisper on the loaded video. Must be called inside RunBusyAsync.</summary>
    private async Task TranscribeCoreAsync(CancellationToken ct)
    {
        if (_video is null) return;
        var model = Transcriber.ModelNames[Math.Max(0, _whisperModel.SelectedIndex)];
        var language = string.IsNullOrWhiteSpace(_language.Text) ? "auto" : _language.Text.Trim();

        _tabs.SelectedTab = _tabTranscript;
        _settings.DetectReactions = _detectReactions.Checked;
        _transcript = await _transcriber.TranscribeAsync(_video, model, language, _detectReactions.Checked, ProgressReporter(), new Progress<string>(Log), ct);
        ShowTranscript();
        SaveProject();
        RefreshLibrary();
        _tabs.SelectedTab = _tabChatGpt;
    }

    private void ShowTranscript()
    {
        _segments.BeginUpdate();
        _segments.Items.Clear();
        if (_transcript is not null)
        {
            foreach (var s in _transcript.Segments)
            {
                var item = new ListViewItem(new[] { Fmt(s.Start), Fmt(s.End), s.Text.Trim(), s.Loudness is { } l ? $"+{l:F0}" : "" }) { Tag = s };
                if (TranscriptEvents.ContainsIntense(s.Text)) { item.ForeColor = Theme.Danger; item.Font = new Font(_segments.Font, FontStyle.Bold); }
                else if (TranscriptEvents.ContainsReaction(s.Text)) item.ForeColor = Theme.Purple;
                _segments.Items.Add(item);
            }
        }
        _segments.EndUpdate();
        _saveSrt.Enabled = _saveTxt.Enabled = _transcript is { Segments.Count: > 0 };
        _analyze.Enabled = _transcript is { Segments.Count: > 0 };
    }

    /// <summary>Uses an existing .srt / .vtt / .json transcript instead of running Whisper.</summary>
    private void LoadTranscriptFile()
    {
        if (_video is null) return;
        using var d = new OpenFileDialog
        {
            Title = "Load an existing transcript",
            Filter = TranscriptImporter.FileFilter,
            InitialDirectory = Path.GetDirectoryName(_video.FilePath)
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var t = TranscriptImporter.Load(d.FileName);
            if (!_detectReactions.Checked)
                foreach (var s in t.Segments) s.Text = TranscriptEvents.Strip(s.Text);
            t.Segments = t.Segments.Where(s => s.Text.Trim().Length > 0).ToList();
            Transcriber.RemoveRepetitionLoops(t);
            _transcript = t;
            ShowTranscript();
            SaveProject();
            RefreshLibrary();
            Log($"Loaded transcript from {d.FileName}: {t.Segments.Count} lines.");
            _tabs.SelectedTab = _tabChatGpt;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not load transcript", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveTranscript(bool srt)
    {
        if (_transcript is null || _video is null) return;
        using var d = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(_video.FilePath) + (srt ? ".srt" : ".txt"),
            Filter = srt ? "SubRip subtitles|*.srt" : "Text|*.txt",
            InitialDirectory = Path.GetDirectoryName(_video.FilePath)
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(d.FileName, srt ? _transcript.ToSrt() : _transcript.ToPlainText());
        Log($"Transcript saved to {d.FileName}");
    }

    private void PreviewSelectedSegment()
    {
        if (_video is null || _segments.SelectedItems.Count == 0 || _segments.SelectedItems[0].Tag is not TranscriptSegment s) return;
        PlayRange(s.Start);
    }

    // ------------------------------------------------------------------ step 3: ChatGPT (manual copy / paste)

    private void BuildChatGptPrompt()
    {
        if (_video is null || _transcript is not { Segments.Count: > 0 })
        {
            MessageBox.Show(this, "Transcribe the video first. The prompt includes the timestamped transcript.", "No transcript", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _tabs.SelectedTab = _tabTranscript;
            return;
        }
        int max = (int)Math.Max(_gptMax.Value, _gptMin.Value + 5);
        _gptPrompt.Text = ChatGptExchange.BuildPrompt(_video, _transcript, (int)_gptCount.Value, (int)_gptMin.Value, max);
        _gptCopy.Enabled = _gptSave.Enabled = true;
        _status.Text = $"Prompt ready ({_gptPrompt.Text.Length:N0} characters). Copy it and paste into ChatGPT.";
    }

    private void SaveChatGptPrompt()
    {
        if (_video is null || _gptPrompt.Text.Length == 0) return;
        using var d = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(_video.FilePath) + " - chatgpt prompt.txt",
            Filter = "Text|*.txt",
            InitialDirectory = Path.GetDirectoryName(_video.FilePath)
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(d.FileName, _gptPrompt.Text);
        Log($"Prompt saved to {d.FileName}");
    }

    private void ImportChatGptAnswer()
    {
        if (_video is null) { MessageBox.Show(this, "Load a video first.", "No video"); return; }
        SuggestionResponse parsed;
        try
        {
            parsed = ChatGptExchange.ParseResponse(_gptAnswer.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read the answer: " + ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var s in parsed.Shorts)
        {
            if (_video.DurationSeconds > 0)
            {
                s.StartSeconds = Math.Clamp(s.StartSeconds, 0, _video.DurationSeconds);
                s.EndSeconds = Math.Clamp(s.EndSeconds, s.StartSeconds + 1, _video.DurationSeconds);
            }
            if (_transcript is not null) ShortSuggester.SnapToSegments(s, _transcript);
        }
        parsed.Shorts = parsed.Shorts.Where(s => s.Duration >= 3).OrderByDescending(s => s.ViralityScore).ToList();
        if (!string.IsNullOrWhiteSpace(parsed.VideoSummary)) parsed.VideoSummary = "[ChatGPT] " + parsed.VideoSummary;

        // keep manually added clips
        var custom = _suggestions?.Shorts.Where(s => s.Emotion == "custom").ToList() ?? new List<ShortSuggestion>();
        parsed.Shorts.AddRange(custom);
        _suggestions = parsed;
        ShowSuggestions();
        SaveProject();
        Log($"Imported {parsed.Shorts.Count - custom.Count} suggestions from ChatGPT.");
        _tabs.SelectedTab = _tabSuggest;
    }

    // ------------------------------------------------------------------ step 5: edit & preview

    private async Task EnterEditorAsync()
    {
        if (!_playerInitStarted)
        {
            _playerInitStarted = true;
            if (!await _player.InitAsync()) Log("In-app player unavailable (WebView2). Use 'Render preview clip' instead.");
        }
        RefreshStickerGallery();
        RefreshEditorClipList();
    }

    private void RefreshStickerGallery()
    {
        _stickers.BeginUpdate();
        _stickers.Items.Clear();
        _stickerImages.Images.Clear();
        foreach (var (name, path) in StickerLibrary.List())
        {
            try
            {
                using var img = Image.FromFile(path);
                _stickerImages.Images.Add(name, new Bitmap(img, _stickerImages.ImageSize));
                _stickers.Items.Add(new ListViewItem(name, name) { Tag = name });
            }
            catch { /* unreadable image: skip */ }
        }
        _stickers.EndUpdate();
    }

    private async Task AddStickerAsync()
    {
        if (_editing is null) { MessageBox.Show(this, "Pick a short first.", "Stickers"); return; }
        if (_stickers.SelectedItems.Count == 0 || _stickers.SelectedItems[0].Tag is not string name)
        {
            MessageBox.Show(this, "Select a sticker in the gallery first.", "Stickers");
            return;
        }
        var ov = new OverlayItem
        {
            Time = RelativeTime, Duration = (double)_stickerDuration.Value, File = name,
            Animation = _stickerAnim.SelectedItem?.ToString() ?? "pop"
        };
        // stagger if another sticker is already showing at this time
        if (_editing.Overlays.Any(o => o.Time <= ov.Time && ov.Time < o.End && Math.Abs(o.X - ov.X) < 10)) ov.X = 22;
        _editing.Overlays.Add(ov);
        await PushKeyframesAsync();
        await _player.PauseAsync();
    }

    private async Task AutoStickersAsync()
    {
        if (_editing is null || _transcript is null) return;
        var slice = _transcript.Slice(_editing.StartSeconds, _editing.EndSeconds);
        var proposed = StickerLibrary.Suggest(_editing, slice);
        if (proposed.Count == 0)
        {
            MessageBox.Show(this, "No laughs / reactions tagged inside this short, so nothing to place automatically. Transcribe with 'Detect laughs / reactions' on, or add stickers by hand.", "Auto stickers");
            return;
        }
        if (_editing.Overlays.Count > 0 &&
            MessageBox.Show(this, $"Add {proposed.Count} sticker(s) from the reaction tags? Existing stickers are kept.", "Auto stickers",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _editing.Overlays.AddRange(proposed);
        await PushKeyframesAsync();
        Log($"Added {proposed.Count} sticker(s) from reaction tags.");
    }

    private void OnOverlayMoved(string id, double x, double y, double size)
    {
        var ov = _editing?.Overlays.FirstOrDefault(o => o.Id == id);
        if (ov is null) return;
        ov.X = x; ov.Y = y; ov.Size = size;
        RefreshKeyframeList();
        SaveProject();
    }

    private void RefreshEditorClipList()
    {
        var current = _editing;
        _editClips.BeginUpdate();
        _editClips.Items.Clear();
        if (_suggestions is not null)
        {
            foreach (var s in _suggestions.Shorts.Where(s => s.Selected))
            {
                var item = new ListViewItem(new[] { s.Title, $"{Fmt(s.StartSeconds)} - {Fmt(s.EndSeconds)}" }) { Tag = s };
                if (ReferenceEquals(s, current)) item.Selected = true;
                _editClips.Items.Add(item);
            }
        }
        _editClips.EndUpdate();
        if (_editClips.Items.Count > 0 && _editClips.SelectedItems.Count == 0) _editClips.Items[0].Selected = true;
        if (_editClips.Items.Count == 0)
        {
            _editing = null;
            _editSegments.Rows.Clear();
            _editorHint.Text = "No shorts selected yet. Tick the ones you want in '4. Short suggestions' and come back here.";
        }
    }

    /// <summary>Original (not copied) transcript segments overlapping the clip, so edits change the real transcript.</summary>
    private List<TranscriptSegment> SegmentsOf(ShortSuggestion s) =>
        _transcript?.Segments.Where(x => x.End > s.StartSeconds && x.Start < s.EndSeconds).ToList() ?? new List<TranscriptSegment>();

    private async Task LoadClipInEditorAsync()
    {
        if (_video is null || _editClips.SelectedItems.Count == 0 || _editClips.SelectedItems[0].Tag is not ShortSuggestion s) return;
        _editing = s;

        _syncingTimeline = true;
        _clipStart.Maximum = _clipEnd.Maximum = (decimal)Math.Max(1, _video.DurationSeconds > 0 ? _video.DurationSeconds : 100000);
        _clipStart.Value = (decimal)Math.Clamp(s.StartSeconds, 0, (double)_clipStart.Maximum);
        _clipEnd.Value = (decimal)Math.Clamp(s.EndSeconds, 0, (double)_clipEnd.Maximum);
        _syncingTimeline = false;

        FillSegmentGrid(s);
        s.MigrateLegacyCaptionPosition();
        _playerTime = s.StartSeconds;
        if (_cameraMode.Checked) _cameraMode.Checked = false;
        if (_player.IsReady)
        {
            await _player.LoadAsync(_video.FilePath, s.StartSeconds, s.EndSeconds);
            await PushCaptionsAsync();
        }
        await PushKeyframesAsync();
        UpdateTimeLabel(s.StartSeconds);
    }

    /// <summary>Current position relative to the clip start, rounded to 0.1 s.</summary>
    private double RelativeTime => _editing is null ? 0 : Math.Round(Math.Max(0, _playerTime - _editing.StartSeconds), 1);

    private void OnCaptionMoved(double x, double y)
    {
        if (_editing is null) return;
        Keyframes.Upsert(_editing.CaptionPositions, new CaptionKeyframe { Time = RelativeTime, X = x, Y = y });
        _ = PushKeyframesAsync();
    }

    private void OnCameraMoved(double x, double y, double zoom)
    {
        if (_editing is null) return;
        Keyframes.Upsert(_editing.Camera, new CameraKeyframe { Time = RelativeTime, X = x, Y = y, Zoom = zoom, Source = "manual" });
        _ = PushKeyframesAsync();
    }

    /// <summary>Sends camera cuts and caption positions to the player, refreshes the list, saves.</summary>
    private async Task PushKeyframesAsync()
    {
        if (_editing is null) return;
        RefreshKeyframeList();
        SaveProject();
        if (_player.IsReady)
        {
            await _player.SetCameraAsync(_editing.Camera);
            await _player.SetCaptionPositionsAsync(_editing.CaptionPositions);
            await _player.SetOverlaysAsync(_editing.Overlays);
        }
    }

    private void RefreshKeyframeList()
    {
        _keyframes.BeginUpdate();
        _keyframes.Items.Clear();
        if (_editing is not null)
        {
            var rows = _editing.Camera.Select(k => (k.Time, "Camera", $"{k.X:F0}% / {k.Y:F0}%  zoom {k.Zoom:F2}x  ({k.Source})", (IKeyframe)k))
                .Concat(_editing.CaptionPositions.Select(k => (k.Time, "Caption", $"{k.X:F0}% / {k.Y:F0}%", (IKeyframe)k)))
                .Concat(_editing.Overlays.Select(o => (o.Time, "Sticker", $"{o.File}  {o.Duration:F1}s  {o.Animation}  at {o.X:F0}% / {o.Y:F0}%", (IKeyframe)o)))
                .OrderBy(r => r.Item1).ThenBy(r => r.Item2);
            foreach (var r in rows)
            {
                var item = new ListViewItem(new[] { Fmt(r.Item1), r.Item2, r.Item3 }) { Tag = r.Item4 };
                item.ForeColor = r.Item2 switch { "Camera" => Theme.PurpleDeep, "Sticker" => Theme.Nebula, _ => Theme.TextSecondary };
                _keyframes.Items.Add(item);
            }
        }
        _keyframes.EndUpdate();
        int cams = _editing?.Camera.Count ?? 0, caps = _editing?.CaptionPositions.Count ?? 0, ovs = _editing?.Overlays.Count ?? 0;
        _keyframeHint.Text = _editing is null ? "" : $"{cams} camera cut{(cams == 1 ? "" : "s")}, {caps} caption pos., {ovs} sticker{(ovs == 1 ? "" : "s")}";
    }

    private async Task DeleteKeyframeAsync()
    {
        if (_editing is null || _keyframes.SelectedItems.Count == 0 || _keyframes.SelectedItems[0].Tag is not IKeyframe k) return;
        if (k is CameraKeyframe ck) _editing.Camera.Remove(ck);
        else if (k is CaptionKeyframe pk) _editing.CaptionPositions.Remove(pk);
        else if (k is OverlayItem ov) _editing.Overlays.Remove(ov);
        await PushKeyframesAsync();
    }

    /// <summary>Runs face detection on the current short and replaces its camera cuts.</summary>
    private async Task AutoCameraAsync()
    {
        if (_video is null || _editing is null) return;
        if (!FaceFramer.IsSupported)
        {
            MessageBox.Show(this, "Face detection is not available on this Windows build. Use Camera mode to place the camera manually.", "Auto camera");
            return;
        }
        var s = _editing;
        if (s.Camera.Any(k => k.Source == "manual") &&
            MessageBox.Show(this, "This short has manual camera cuts. Replace them with automatic face framing?", "Auto camera",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        await RunBusyAsync("Detecting faces", async ct =>
        {
            var cuts = await new FaceFramer(_ffmpeg).DetectAsync(_video, s, ProgressReporter(), new Progress<string>(Log), ct);
            s.Camera = cuts;
            if (ReferenceEquals(_editing, s)) await PushKeyframesAsync(); else SaveProject();
        });
    }

    private void FillSegmentGrid(ShortSuggestion s)
    {
        _editSegments.Rows.Clear();
        foreach (var seg in SegmentsOf(s))
        {
            int row = _editSegments.Rows.Add(Fmt(seg.Start), Fmt(seg.End), seg.Text);
            _editSegments.Rows[row].Tag = seg;
        }
    }

    private async Task PushCaptionsAsync()
    {
        if (_editing is null || !_player.IsReady) return;
        var opts = ReadOptions();
        var slice = _transcript?.Slice(_editing.StartSeconds, _editing.EndSeconds) ?? new List<TranscriptSegment>();
        await _player.SetCaptionsAsync(slice, _editing.StartSeconds, CaptionStyle.Get(opts.CaptionStyleId), opts.FontSize, opts.WordsPerCaption,
            opts.CropMode, opts.AddCaptions, opts.IncludeReactions);
    }

    private void OnPlayerTime(double t)
    {
        if (_editing is null) return;
        if (InvokeRequired) { BeginInvoke(() => OnPlayerTime(t)); return; }
        _playerTime = t;
        UpdateTimeLabel(t);
        if (!_timelineDragging)
        {
            _syncingTimeline = true;
            double frac = _editing.Duration > 0 ? (t - _editing.StartSeconds) / _editing.Duration : 0;
            _timeline.Value = (int)Math.Clamp(frac * 1000, 0, 1000);
            _syncingTimeline = false;
        }
        // highlight the transcript line being spoken
        foreach (DataGridViewRow row in _editSegments.Rows)
        {
            if (row.Tag is TranscriptSegment seg)
            {
                bool active = t >= seg.Start && t < seg.End;
                var color = active ? Theme.SurfaceSoft : Color.White;
                if (row.DefaultCellStyle.BackColor != color) row.DefaultCellStyle.BackColor = color;
            }
        }
    }

    private void UpdateTimeLabel(double t)
    {
        if (_editing is null) return;
        _timeLabel.Text = $"{Fmt(Math.Max(0, t - _editing.StartSeconds))} / {Fmt(_editing.Duration)}";
    }

    private async Task SeekFromTimelineAsync()
    {
        if (_editing is null || _syncingTimeline) return;
        double t = _editing.StartSeconds + _editing.Duration * _timeline.Value / 1000.0;
        await _player.SeekAsync(t);
    }

    private async Task ApplyClipTimesAsync()
    {
        if (_editing is null || _syncingTimeline) return;
        double start = (double)_clipStart.Value, end = (double)_clipEnd.Value;
        if (end <= start + 1) return;
        _editing.StartSeconds = start;
        _editing.EndSeconds = end;
        FillSegmentGrid(_editing);
        RefreshEditorClipList();
        ShowSuggestions();
        SaveProject();
        if (_player.IsReady)
        {
            await _player.SetRangeAsync(start, end);
            await PushCaptionsAsync();
        }
    }

    private void OnSegmentEdited(int rowIndex)
    {
        if (rowIndex < 0 || _editSegments.Rows[rowIndex].Tag is not TranscriptSegment seg) return;
        var newText = (_editSegments.Rows[rowIndex].Cells["Text"].Value?.ToString() ?? "").Trim();
        if (newText == seg.Text) return;
        seg.Text = newText;
        ShowTranscript();      // keep the Transcript tab in sync
        SaveProject();
        _ = PushCaptionsAsync();
        Log("Transcript line updated.");
    }

    private void DeleteSegmentRow()
    {
        if (_transcript is null || _editSegments.CurrentRow?.Tag is not TranscriptSegment seg) return;
        if (MessageBox.Show(this, "Remove this line from the transcript? It will not appear in captions.", "Delete line",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _transcript.Segments.Remove(seg);
        if (_editing is not null) FillSegmentGrid(_editing);
        ShowTranscript();
        SaveProject();
        _ = PushCaptionsAsync();
    }

    private async Task PlayFromRowAsync(int rowIndex)
    {
        if (rowIndex < 0 || _editSegments.Rows[rowIndex].Tag is not TranscriptSegment seg || !_player.IsReady) return;
        await _player.SeekAsync(seg.Start);
        await _player.PlayAsync();
    }

    /// <summary>Renders a low-resolution captioned MP4 of the current short and opens it in the default player.</summary>
    private async Task RenderEditorPreviewAsync()
    {
        if (_video is null || _editing is null) return;
        var s = _editing;
        var opts = ReadOptions();
        opts.OutputFolder = Path.Combine(Path.GetTempPath(), "shortgen_previews");
        await RunBusyAsync("Rendering preview", async ct =>
        {
            var r = await _renderer.RenderAsync(_video, _transcript, s, opts, 0, ProgressReporter(), new Progress<string>(Log), ct);
            if (r.Success) OpenPath(r.OutputPath);
            else throw new InvalidOperationException(r.Error ?? "Preview failed.");
        });
    }

    // ------------------------------------------------------------------ step 4: suggestions

    private async Task AnalyzeAsync()
    {
        if (_video is null || _transcript is null) return;
        var apiKey = SettingsStore.GetApiKey(_settings);
        if (apiKey is null)
        {
            MessageBox.Show(this, "Add your Anthropic API key in Settings first.", "API key required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenSettings();
            return;
        }

        int count = (int)_count.Value, min = (int)_minSec.Value, max = (int)Math.Max(_maxSec.Value, _minSec.Value + 5);
        await RunBusyAsync("Analyzing with Claude", async ct =>
        {
            var suggester = new ShortSuggester(apiKey, _settings.ClaudeModel);
            var custom = _suggestions?.Shorts.Where(s => s.Emotion == "custom").ToList() ?? new List<ShortSuggestion>();
            _suggestions = await suggester.SuggestAsync(_video, _transcript, count, min, max, new Progress<string>(Log), ct);
            _suggestions.Shorts.AddRange(custom);
            ShowSuggestions();
            SaveProject();
        });
    }

    private void ShowSuggestions()
    {
        _populatingSuggestions = true;
        _suggestList.BeginUpdate();
        _suggestList.Items.Clear();
        if (_suggestions is not null)
        {
            int i = 1;
            foreach (var s in _suggestions.Shorts)
            {
                var item = new ListViewItem(new[] { "", (i++).ToString(), s.Title, Fmt(s.StartSeconds), Fmt(s.EndSeconds), $"{s.Duration:F0}s",
                    s.ViralityScore > 0 ? $"{s.ViralityScore}/10" : "-", s.Emotion })
                { Tag = s, Checked = s.Selected };
                _suggestList.Items.Add(item);
            }
            _summary.Text = string.IsNullOrWhiteSpace(_suggestions.VideoSummary) ? "" : "Video summary: " + _suggestions.VideoSummary;
        }
        _suggestList.EndUpdate();
        _populatingSuggestions = false;
        if (_suggestList.Items.Count > 0) _suggestList.Items[0].Selected = true;
        UpdateGenerateEnabled();
    }

    private bool _populatingSuggestions;

    private void ShowSuggestionDetail()
    {
        bool has = _suggestList.SelectedItems.Count > 0;
        _editClip.Enabled = _removeClip.Enabled = _previewClip.Enabled = has;
        if (!has || _suggestList.SelectedItems[0].Tag is not ShortSuggestion s) return;

        _suggestDetail.Clear();
        void H(string text) { _suggestDetail.SelectionFont = new Font(_suggestDetail.Font, FontStyle.Bold); _suggestDetail.AppendText(text + "\n"); _suggestDetail.SelectionFont = _suggestDetail.Font; }
        void P(string text) { _suggestDetail.AppendText(text + "\n\n"); }

        H(s.Title);
        P($"{Fmt(s.StartSeconds)} - {Fmt(s.EndSeconds)}  ({s.Duration:F0}s)   Viral score: {(s.ViralityScore > 0 ? $"{s.ViralityScore}/10" : "-")}   Emotion: {s.Emotion}");
        if (!string.IsNullOrWhiteSpace(s.Hook)) { H("Hook"); P(s.Hook); }
        if (!string.IsNullOrWhiteSpace(s.WhyViral)) { H("Why this could go viral"); P(s.WhyViral); }
        if (!string.IsNullOrWhiteSpace(s.SuggestedCaption)) { H("Suggested post caption"); P(s.SuggestedCaption); }
        if (s.Hashtags.Count > 0) { H("Hashtags"); P(string.Join(" ", s.Hashtags.Select(h => h.StartsWith('#') ? h : "#" + h))); }
        if (_transcript is not null)
        {
            H("Transcript of this clip");
            P(string.Join(" ", _transcript.Slice(s.StartSeconds, s.EndSeconds).Select(x => x.Text)));
        }
        _suggestDetail.SelectionStart = 0;
        _suggestDetail.ScrollToCaret();
    }

    private void EditSelectedClip()
    {
        if (_video is null || _suggestList.SelectedItems.Count == 0 || _suggestList.SelectedItems[0].Tag is not ShortSuggestion s) return;
        using var dlg = new ClipTimesDialog(s, _video.DurationSeconds);
        if (dlg.ShowDialog(this) == DialogResult.OK) { ShowSuggestions(); SaveProject(); }
    }

    private void AddCustomClip()
    {
        if (_video is null) return;
        _suggestions ??= new SuggestionResponse();
        var s = new ShortSuggestion
        {
            Title = "Custom clip", StartSeconds = 0, EndSeconds = Math.Min(30, Math.Max(2, _video.DurationSeconds)),
            Emotion = "custom", WhyViral = "Manually selected clip."
        };
        using var dlg = new ClipTimesDialog(s, _video.DurationSeconds);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _suggestions.Shorts.Add(s);
        ShowSuggestions();
        SaveProject();
    }

    private void RemoveSelectedClip()
    {
        if (_suggestions is null || _suggestList.SelectedItems.Count == 0 || _suggestList.SelectedItems[0].Tag is not ShortSuggestion s) return;
        _suggestions.Shorts.Remove(s);
        ShowSuggestions();
        SaveProject();
    }

    private async Task PreviewSelectedClipAsync()
    {
        if (_video is null || _suggestList.SelectedItems.Count == 0 || _suggestList.SelectedItems[0].Tag is not ShortSuggestion s) return;
        // Quick low-res, no-caption cut so the user can check the moment before rendering.
        var tmp = Path.Combine(Path.GetTempPath(), $"shortgen_preview_{Guid.NewGuid():N}.mp4");
        await RunBusyAsync("Rendering preview", async ct =>
        {
            await _ffmpeg.RunAsync(new[]
            {
                "-y", "-ss", s.StartSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                "-to", s.EndSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                "-i", _video.FilePath, "-vf", "scale=-2:480", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-c:a", "aac", tmp
            }, null, ProgressReporter(), s.Duration, ct);
            OpenPath(tmp);
        });
    }

    // ------------------------------------------------------------------ step 4: generate

    private GenerateOptions ReadOptions() => new()
    {
        AddCaptions = _addCaptions.Checked,
        CaptionStyleId = CaptionStyle.All[Math.Max(0, _style.SelectedIndex)].Id,
        WordsPerCaption = (int)_wordsPerCaption.Value,
        FontSize = (int)_fontSize.Value,
        CropMode = (CropMode)_crop.SelectedIndex,
        BurnTitleHook = _burnHook.Checked,
        IncludeReactions = _includeReactions.Checked,
        AutoCamera = _autoCameraOpt.Checked,
        OutputFolder = string.IsNullOrWhiteSpace(_outputFolder.Text) ? _settings.OutputFolder : _outputFolder.Text.Trim()
    };

    private void RefreshPreview()
    {
        var style = CaptionStyle.All[Math.Max(0, _style.SelectedIndex)];
        _styleDesc.Text = style.Description;
        var sample = _transcript?.Segments.FirstOrDefault(s => s.Text.Split(' ').Length >= 3)?.Text;
        _preview.Update(style, (int)_fontSize.Value, (int)_wordsPerCaption.Value, sample);
        _preview.Visible = _addCaptions.Checked;
        _ = PushCaptionsAsync(); // keep the editor's live preview in sync with the caption options
    }

    private void UpdateGenerateEnabled()
    {
        _generate.Enabled = _video is not null && _suggestions is not null && _suggestions.Shorts.Any(s => s.Selected) && _cts is null;
    }

    /// <param name="only">Render just these shorts (from the editor's "Generate this short"); null = all ticked shorts.</param>
    private async Task GenerateAsync(IReadOnlyList<ShortSuggestion>? only = null)
    {
        if (_video is null || _suggestions is null) return;
        var selected = only?.ToList() ?? _suggestions.Shorts.Where(s => s.Selected).ToList();
        if (selected.Count == 0) return;
        var options = ReadOptions();
        if (options.AddCaptions && _transcript is null)
        {
            MessageBox.Show(this, "Captions need a transcript. Transcribe first or untick 'Burn captions'.", "No transcript");
            return;
        }

        var sub = Path.Combine(options.OutputFolder, SafeFolder(_video.Title));
        options.OutputFolder = sub;
        _results.Items.Clear();

        await RunBusyAsync("Generating shorts", async ct =>
        {
            int i = 1, ok = 0;
            foreach (var s in selected)
            {
                ct.ThrowIfCancellationRequested();
                _status.Text = $"Generating {i}/{selected.Count}: {s.Title}";
                var item = _results.Items.Add(new ListViewItem(new[] { s.Title, "rendering...", "" }));
                if (options.CropMode == CropMode.VerticalCrop && options.AutoCamera && s.Camera.Count == 0 && FaceFramer.IsSupported)
                {
                    item.SubItems[1].Text = "faces...";
                    try
                    {
                        s.Camera = await new FaceFramer(_ffmpeg).DetectAsync(_video, s, ProgressReporter(), new Progress<string>(Log), ct);
                        SaveProject();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Log($"Auto camera failed for \"{s.Title}\" ({ex.Message}); using a centered crop.");
                    }
                    item.SubItems[1].Text = "rendering...";
                }
                var r = await _renderer.RenderAsync(_video, _transcript, s, options, i, ProgressReporter(), new Progress<string>(Log), ct);
                item.SubItems[1].Text = r.Success ? "done" : "failed";
                item.SubItems[2].Text = r.Success ? r.OutputPath : r.Error ?? "";
                item.Tag = r.OutputPath;
                if (r.Success)
                {
                    ok++;
                    _generated.Add(new GeneratedFile { Title = s.Title, Path = r.OutputPath, When = DateTime.Now });
                }
                i++;
            }
            SaveProject();
            Log($"Finished: {ok}/{selected.Count} shorts generated in {sub}");
            if (ok > 0) OpenPath(sub);
        });
    }

    // ------------------------------------------------------------------ helpers

    private async Task RunBusyAsync(string title, Func<CancellationToken, Task> work)
    {
        if (_cts is not null) { MessageBox.Show(this, "Another operation is running. Cancel it first.", "Busy"); return; }
        _cts = new CancellationTokenSource();
        SetBusy(true, title);
        try
        {
            await work(_cts.Token);
            _status.Text = title + " - done";
        }
        catch (OperationCanceledException)
        {
            Log($"{title} cancelled.");
            _status.Text = title + " - cancelled";
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            _status.Text = title + " - failed";
            MessageBox.Show(this, ex.Message, title + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false, "");
        }
    }

    private void SetBusy(bool busy, string title)
    {
        _cancel.Enabled = busy;
        _download.Enabled = _downloadTranscribe.Enabled = _openLocal.Enabled = _settingsBtn.Enabled = !busy;
        _libraryRefresh.Enabled = _libraryOpenFolder.Enabled = !busy;
        _libraryLoad.Enabled = _libraryTranscribe.Enabled = !busy && _library.SelectedItems.Count > 0;
        _transcribe.Enabled = !busy; // with no video loaded it offers to download + transcribe the link
        _loadTranscript.Enabled = !busy && _video is not null;
        _analyze.Enabled = !busy && _transcript is { Segments.Count: > 0 };
        _addClip.Enabled = !busy && _video is not null;
        _previewClip.Enabled = _editClip.Enabled = _removeClip.Enabled = !busy && _suggestList.SelectedItems.Count > 0;
        UpdateGenerateEnabled();
        // No wait cursor: long jobs run in the background and the rest of the app stays usable
        // (browse tabs, read the log, tweak caption options). The progress bar and status show activity.
        _progress.Value = 0;
        if (busy) _status.Text = title + "...";
    }

    private IProgress<double> ProgressReporter() => new Progress<double>(p =>
    {
        _progress.Value = (int)Math.Clamp(p * 100, 0, 100);
    });

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string Fmt(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
    }

    private static string SafeFolder(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (cleaned.Length > 60) cleaned = cleaned[..60].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "video" : cleaned;
    }

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void PlayRange(double start)
    {
        if (_video is null) return;
        // Most players ignore a start offset via the shell, so just open the file.
        OpenPath(_video.FilePath);
        Log($"Opened video. Segment starts at {Fmt(start)}.");
    }

    // ---- project persistence: <video>.shortgen.json next to the video ----

    private sealed class ProjectFile
    {
        public VideoInfo? Video { get; set; }
        public Transcript? Transcript { get; set; }
        public SuggestionResponse? Suggestions { get; set; }
        public List<GeneratedFile>? Generated { get; set; }
    }

    private sealed class GeneratedFile
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime When { get; set; }
    }

    private List<GeneratedFile> _generated = new();

    private static string ProjectPath(string videoPath) => Path.ChangeExtension(videoPath, ".shortgen.json");

    private void SaveProject()
    {
        if (_video is null) return;
        try
        {
            var p = new ProjectFile { Video = _video, Transcript = _transcript, Suggestions = _suggestions, Generated = _generated };
            File.WriteAllText(ProjectPath(_video.FilePath), JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log("Could not save project file: " + ex.Message); }
    }

    private static bool TryLoadProject(string videoPath, out ProjectFile project)
    {
        project = new ProjectFile();
        try
        {
            var path = ProjectPath(videoPath);
            if (!File.Exists(path)) return false;
            project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path)) ?? new ProjectFile();
            return true;
        }
        catch { return false; }
    }
}
