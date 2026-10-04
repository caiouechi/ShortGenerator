using ShortGenerator.Forms.Controls;
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
    private readonly FancyButton _download = new() { Text = "Download", Width = 100 };
    private readonly FancyButton _downloadTranscribe = new() { Text = "Download + Transcribe", Width = 170 };
    private readonly FancyButton _openLocal = new() { Text = "Open local file...", Width = 130 };

    // library of downloaded videos (Video tab)
    private readonly ListView _library = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
    private readonly FancyButton _libraryRefresh = new() { Text = "Refresh", Width = 90 };
    private readonly FancyButton _libraryLoad = new() { Text = "Load selected", Width = 120, Enabled = false };
    private readonly FancyButton _libraryTranscribe = new() { Text = "Transcribe selected", Width = 170, Enabled = false };
    private readonly FancyButton _libraryOpenFolder = new() { Text = "Open downloads folder", Width = 160 };
    private readonly FancyButton _libraryDelete = new() { Text = "Delete", Width = 90, Enabled = false };

    // ---- tabs ----
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _tabVideo = new("1. Video");
    private readonly TabPage _tabTranscript = new("2. Transcript");
    private readonly TabPage _tabChatGpt = new("3. Ask ChatGPT");
    private readonly TabPage _tabSuggest = new("4. Short suggestions");
    private readonly TabPage _tabGenerate = new("5. Generate shorts");
    private readonly TabPage _tabEditor = new("6. Edit & preview");
    private readonly TabPage _tabPublish = new("7. Publish");
    private readonly PublishPanel _publishPanel = new();

    // editor tab
    private readonly ListView _editClips = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    private readonly ClipPlayer _player = new() { Dock = DockStyle.Fill };
    private readonly FancyButton _playPause = new() { Text = "Play", Width = 80 };
    private readonly TimelineBar _timeline = new() { Maximum = 1000, Dock = DockStyle.Fill, Height = 30 };
    private readonly Label _timeLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 10, 0, 0) };
    private readonly NumericUpDown _clipStart = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 80, Maximum = 100000 };
    private readonly NumericUpDown _clipEnd = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 80, Maximum = 100000 };
    private readonly FancyButton _renderPreview = new() { Text = "Render preview", Width = 160 };
    // render queue (Edit & preview, bottom right): Render preview adds the short here; one render runs at a time
    private readonly ListView _renders = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true };
    private readonly Label _rendersCounts = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 12, 0, 0) };
    private readonly Label _rendersEmpty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, BackColor = Theme.Elevated,
        Text = "No renders yet. Render preview adds the short here; while one renders you can select another short and queue it." };
    private readonly FancyButton _renderCancel = new() { Text = "Cancel", Width = 96, Enabled = false, Glyph = "\uE711" };
    private readonly FancyButton _renderClear = new() { Text = "Clear done", Width = 116, Enabled = false, Glyph = "\uE894" };
    // cover / thumbnail for the short
    private readonly PictureBox _coverPreview = new() { Width = 96, Height = 170, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(19, 24, 52), BorderStyle = BorderStyle.None };
    private readonly FancyButton _setCover = new() { Text = "Use this frame", Width = 170, Glyph = "" };
    private readonly FancyButton _pickCoverImage = new() { Text = "Choose image...", Width = 170 };
    // which language's cover the cover buttons work on (shown for shorts ticked for English)
    private readonly ComboBox _coverLang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private string? CoverLanguage => _editing?.TranslateEnglish == true && _coverLang.SelectedIndex == 1 ? "en" : null;
    private readonly FancyButton _clearCover = new() { Text = "Reset to default", Width = 170 };
    private readonly Label _coverInfo = new() { AutoSize = true, MaximumSize = new Size(170, 0), ForeColor = Color.DimGray };
    // cover helpers: copy the frame for ChatGPT, copy a thumbnail brief, or (developer only) let Higgsfield redraw it
    private readonly FancyButton _copyCover = new() { Text = "Copy image", Width = 150, Glyph = "\uE8C8" };
    private readonly FancyButton _copyCoverPrompt = new() { Text = "Copy thumbnail prompt", Width = 150 };
    private readonly FancyButton _generateCover = new() { Text = "Higgsfield cover", Width = 150, Glyph = "\uE8A9", Visible = HiggsfieldClient.IsConfigured || System.Diagnostics.Debugger.IsAttached };
    private readonly CheckBox _cameraMode = new() { Text = "Camera mode", AutoSize = false, Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter, Width = 110, Height = 28 };
    private readonly FancyButton _autoCamera = new() { Text = "Auto camera (faces)", Width = 185 };
    private readonly FancyButton _changeCamera = new() { Text = "Change camera", Width = 135 };
    private readonly FancyButton _removeFromSelection = new() { Text = "Remove from selected", Width = 180, Glyph = "\uE738" };
    // image layers of the short open in the editor
    private readonly ListView _layers = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    private readonly FancyButton _addLayer = new() { Text = "Add image...", Width = 120, Glyph = "\uE710" };
    private readonly FancyButton _pasteLayer = new() { Text = "Paste image", Width = 120, Glyph = "\uE77F" };
    // under the player: one click adds an image at the playhead; the label follows the time
    private readonly FancyButton _addLayerHere = new() { Text = "Image at 00:00.0", Width = 150, Glyph = "\uE91B" };
    private const double DefaultLayerSeconds = 5;
    /// <summary>Adds a moment of another video (the goal being talked about) over the short.</summary>
    private readonly FancyButton _addVideoLayer = new() { Text = "Add video", Width = 120 };
    private readonly FancyButton _removeLayer = new() { Text = "Remove", Width = 96, Enabled = false, Glyph = "\uE74D" };
    private readonly NumericUpDown _layerFrom = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 64, Maximum = 100000 };
    private readonly NumericUpDown _layerTo = new() { DecimalPlaces = 1, Increment = 0.5M, Width = 64, Maximum = 100000 };
    private readonly NumericUpDown _layerSize = new() { Minimum = 8, Maximum = 100, Width = 64 };
    private readonly NumericUpDown _layerRot = new() { Minimum = -180, Maximum = 180, Width = 64 };
    private readonly ComboBox _layerStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _layerAnim = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ContextMenuStrip _segMenu = new();
    // English: caption language in the editor and on Generate shorts
    /// <summary>The live captions follow the table column being edited: English while in the English column.</summary>
    private bool _previewEnglish;
    private readonly ComboBox _previewLang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Label _previewLangLabel = new() { Text = "Captions:", AutoSize = true, Margin = new Padding(12, 9, 4, 0) };
    private readonly FlowLayoutPanel _previewLangGroup = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly FancyButton _retranslate = new() { Text = "Translate again", Width = 140, Visible = false, Glyph = "\uF2B7" };
    private readonly ComboBox _captionLang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private bool _loadingLayer;
    // face analysis per short, kept for the session so "Change camera" can offer other framings instantly
    private readonly Dictionary<ShortSuggestion, FaceFramer.Analysis> _faces = new(ReferenceEqualityComparer.Instance);
    private readonly Label _keyframeHint = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 7, 0, 0) };
    private readonly ListView _keyframes = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    private readonly FancyButton _keyframeDelete = new() { Text = "Delete", Width = 70 };
    private readonly FancyButton _keyframeClear = new() { Text = "Clear all", Width = 80 };
    private double _playerTime;
    private readonly DataGridView _editSegments = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
    };
    private readonly FancyButton _segDelete = new() { Text = "Delete line", Width = 100 };
    private readonly FancyButton _segPlay = new() { Text = "Play from line", Width = 110 };
    private readonly Label _editorHint = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 4, 8, 4), ForeColor = Color.DimGray };
    private ShortSuggestion? _editing;
    private bool _timelineDragging;
    private bool _syncingTimeline;
    private bool _playerInitStarted;

    // chatgpt tab
    private readonly NumericUpDown _gptCount = new() { Minimum = 1, Maximum = 20, Value = 6, Width = 55 };
    private readonly CheckBox _gptAuto = new() { Text = "AI decides", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
    private readonly NumericUpDown _gptMin = new() { Minimum = 5, Maximum = 180, Value = 15, Width = 55 };
    private readonly NumericUpDown _gptMax = new() { Minimum = 10, Maximum = 180, Value = 60, Width = 55 };
    private readonly FancyButton _gptBuild = new() { Text = "Generate prompt", Width = 160 };
    private readonly FancyButton _gptCopy = new() { Text = "Copy prompt", Width = 140, Enabled = false };
    private readonly FancyButton _gptSave = new() { Text = "Save prompt .txt", Width = 150, Enabled = false };
    private readonly FancyButton _gptPaste = new() { Text = "Paste from clipboard", Width = 150 };
    private readonly FancyButton _gptImport = new() { Text = "Import suggestions", Width = 170 };
    private readonly TextBox _gptPrompt = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    private readonly TextBox _gptAnswer = new() { Multiline = true, MaxLength = 0, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, Dock = DockStyle.Fill,
        PlaceholderText = "Paste ChatGPT's JSON answer here, then click 'Import suggestions'." };

    // empty states (Higgsfield illustrations)
    private readonly EmptyState _emptyVideo = new("empty-video.png", "No video yet",
        "Paste a YouTube, Instagram or TikTok link above and click Download, pick one of your downloaded videos below, or open a local file.");
    private readonly EmptyState _emptyTranscript = new("empty-transcript.png", "No transcript yet",
        "Click Transcribe to run Whisper locally, or load an existing .srt / .vtt file. Turn on reactions to capture laughs and loud moments.");
    private readonly EmptyState _emptySuggest = new("empty-shorts.png", "No suggestions yet",
        "Ask ChatGPT with the copy / paste prompt, or Analyze with Claude. Each suggestion comes with the reasoning and a score out of 10.");

    // video tab
    private readonly Label _videoInfo = new() { AutoSize = true, Padding = new Padding(10), UseMnemonic = false };
    private readonly FancyButton _openFile = new() { Text = "Play video", Width = 110, Enabled = false };
    private readonly FancyButton _openFolder = new() { Text = "Open folder", Width = 110, Enabled = false };

    // transcript tab
    private readonly FancyButton _transcribe = new() { Text = "Transcribe", Width = 130 };
    private readonly ComboBox _whisperModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _language = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _detectReactions = new() { Text = "Detect laughs / reactions", AutoSize = true };
    private readonly FancyButton _loadTranscript = new() { Text = "Load transcript file...", Width = 150, Enabled = false };
    private readonly FancyButton _saveSrt = new() { Text = "Save .srt", Width = 90, Enabled = false };
    private readonly FancyButton _saveTxt = new() { Text = "Save .txt", Width = 90, Enabled = false };
    private readonly FancyButton _deleteTranscript = new() { Text = "Delete transcript", Width = 140, Enabled = false };
    private readonly FancyButton _deleteShort = new() { Text = "Delete short", Width = 120, Enabled = false };
    private readonly ListView _segments = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

    // suggestions tab
    private readonly FancyButton _analyze = new() { Text = "Analyze with Claude", Width = 185, Enabled = false };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 20, Width = 55 };
    private readonly CheckBox _autoCount = new() { Text = "AI decides", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
    private readonly NumericUpDown _minSec = new() { Minimum = 5, Maximum = 180, Width = 55 };
    private readonly NumericUpDown _maxSec = new() { Minimum = 10, Maximum = 180, Width = 55 };
    private readonly FancyButton _addClip = new() { Text = "Add custom clip", Width = 120, Enabled = false };
    private readonly FancyButton _editClip = new() { Text = "Adjust times", Width = 100, Enabled = false };
    private readonly FancyButton _removeClip = new() { Text = "Remove", Width = 80, Enabled = false };
    private readonly FancyButton _previewClip = new() { Text = "Preview clip", Width = 100, Enabled = false };
    private readonly ListView _suggestList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, GridLines = true, HideSelection = false };
    private readonly RichTextBox _suggestDetail = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(6), ForeColor = Color.DimGray };

    // generate tab
    private readonly CheckBox _addCaptions = new() { Text = "Burn captions into the video", Checked = true, AutoSize = true };
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _styleDesc = new() { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.DimGray };
    private readonly NumericUpDown _wordsPerCaption = new() { Minimum = 1, Maximum = 8, Value = 6, Width = 60 };
    private readonly NumericUpDown _fontSize = new() { Minimum = 0, Maximum = 200, Value = 0, Width = 60 };
    private readonly ComboBox _crop = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _burnHook = new() { Text = "Show the hook as a title at the start", AutoSize = true };
    private readonly CheckBox _includeReactions = new() { Text = "Show [laughs] tags in captions", AutoSize = true };
    private readonly CheckBox _autoCameraOpt = new() { Text = "Auto camera (follow faces)", Checked = true, AutoSize = true };
    private readonly ComboBox _look = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _lookDesc = new() { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.DimGray };
    private readonly TextBox _outputFolder = new() { Width = 230 };
    private readonly FancyButton _generate = new() { Text = "Generate selected shorts", Width = 190, Height = 34, Enabled = false, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    // hand-off from Suggestions to Generate shorts, and the queue of ticked shorts shown there
    private readonly FancyButton _continueToGenerate = new() { Text = "Continue with selected shorts", Width = 250, Height = 36, Enabled = false };
    private readonly FancyButton _backToSuggestions = new() { Text = "Change selection", Width = 150 };
    private readonly Label _selectionSummary = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
    private readonly ListView _queue = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false };
    private readonly Label _queueTitle = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, Text = "Shorts to generate" };
    private readonly CaptionPreview _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30) };
    private readonly ListView _results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

    // bottom
    private readonly BrandProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly BrandHeader _header = new();
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly FancyButton _cancel = new() { Text = "Cancel", Width = 80, Enabled = false };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(250, 250, 250) };

    public MainForm()
    {
        Text = "Galiluna Short Generator";
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1320, 840);
        BackColor = Theme.Bg;
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

    // ---- shell: side navigation + header + command bar + activity drawer ----
    private readonly SideNav _nav = new();
    private TableLayoutPanel _commandBar = null!;
    private readonly FancyButton _activityToggle = new() { Text = "Activity", Width = 96, Glyph = "" };
    private Panel _drawer = null!;
    private bool _drawerOpen;

    private static readonly (string Title, string Tagline, string Hint, string Glyph)[] Pages =
    {
        ("Video", "Bring in the source: paste a link, pick a downloaded video, or open a file.", "Source video", ""),
        ("Transcript", "Whisper runs locally. Turn on reactions to capture laughs and intensity.", "Whisper, reactions", ""),
        ("Ask ChatGPT", "Copy the prompt, paste the answer. No API key needed.", "Copy / paste flow", ""),
        ("Suggestions", "The moments most likely to travel, with the reasoning and a score.", "Pick your moments", ""),
        ("Generate shorts", "Framing, captions and camera. Render the shorts you ticked.", "Render the clips", ""),
        ("Edit & preview", "Play a short, fix words, place the caption, direct the camera.", "Fine-tune each short", ""),
        ("Publish", "Send the rendered shorts, with cover and per-network text, to the Instagram, TikTok and YouTube accounts connected on galiluna.", "Post with galiluna", ""),
    };

    private void BuildLayout()
    {
        // navigation
        foreach (var p in Pages) _nav.Add(p.Title, p.Hint, p.Glyph);
        _nav.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedIndex != _nav.SelectedIndex) _tabs.SelectedIndex = _nav.SelectedIndex; };

        _nav.SettingsClicked += (_, _) => OpenSettings();

        // command bar: the link box and the source actions. It belongs to step 1 only, so it is built here
        // and docked inside the Video page (see BuildVideoTab).
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 58, ColumnCount = 4, Padding = new Padding(0, 8, 0, 12), BackColor = Theme.Bg };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _url.Font = Theme.Body(10f);
        var urlField = Theme.WrapInput(_url);
        urlField.Dock = DockStyle.Fill;
        urlField.Margin = new Padding(0, 0, 12, 0);
        _url.GotFocus += (_, _) => urlField.Invalidate();
        _url.LostFocus += (_, _) => urlField.Invalidate();
        bar.Controls.Add(urlField, 0, 0);
        _download.Glyph = ""; _download.Width = 128; _download.Margin = new Padding(0, 1, 8, 0);
        _downloadTranscribe.Glyph = ""; _downloadTranscribe.Width = 200; _downloadTranscribe.Margin = new Padding(0, 1, 8, 0);
        _openLocal.Glyph = ""; _openLocal.Text = "Open file"; _openLocal.Width = 118; _openLocal.Margin = new Padding(0, 1, 0, 0);
        bar.Controls.Add(_download, 1, 0);
        bar.Controls.Add(_downloadTranscribe, 2, 0);
        bar.Controls.Add(_openLocal, 3, 0);
        _commandBar = bar;

        // pages (the tab strip is hidden; the side navigation drives it)
        _tabs.TabPages.AddRange(new[] { _tabVideo, _tabTranscript, _tabChatGpt, _tabSuggest, _tabGenerate, _tabEditor, _tabPublish });
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _nav.SelectedIndex = _tabs.SelectedIndex;
            var p = Pages[Math.Clamp(_tabs.SelectedIndex, 0, Pages.Length - 1)];
            _header.Set(p.Title, p.Tagline);
        };
        BuildVideoTab();
        BuildTranscriptTab();
        BuildChatGptTab();
        BuildSuggestTab();
        BuildEditorTab();
        BuildGenerateTab();
        BuildPublishTab();

        // activity drawer: status line always visible, log expands on demand
        _drawer = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(24, 6, 24, 6), BackColor = Theme.Elevated };
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, ColumnCount = 4, BackColor = Theme.Elevated };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _progress.Margin = new Padding(0, 11, 16, 0);
        _status.Font = Theme.Body(9f);
        statusRow.Controls.Add(_progress, 0, 0);
        statusRow.Controls.Add(_status, 1, 0);
        _cancel.Kind = ButtonKind.Danger; _cancel.Width = 90; _cancel.Height = 30; _cancel.Margin = new Padding(0, 0, 8, 0);
        _activityToggle.Height = 30;
        statusRow.Controls.Add(_cancel, 2, 0);
        statusRow.Controls.Add(_activityToggle, 3, 0);
        _log.Margin = new Padding(0, 8, 0, 0);
        _log.BorderStyle = BorderStyle.None;
        _drawer.Controls.Add(_log);
        _drawer.Controls.Add(statusRow);
        _log.Visible = false;
        _activityToggle.Click += (_, _) => ToggleDrawer();

        // The tab control paints a light frame we cannot theme, so it is positioned slightly outside its
        // host and the host clips the frame away.
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 12), BackColor = Theme.Bg };
        var clip = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _tabs.Dock = DockStyle.None;
        clip.Controls.Add(_tabs);
        clip.Resize += (_, _) => _tabs.SetBounds(-4, -4, clip.Width + 8, clip.Height + 8);
        content.Controls.Add(clip);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        right.Controls.Add(content);
        right.Controls.Add(_drawer);
        right.Controls.Add(_header);

        Controls.Add(right);
        Controls.Add(_nav);
        _header.Set(Pages[0].Title, Pages[0].Tagline);
    }

    /// <summary>
    /// SplitContainer clamps SplitterDistance to its (tiny) construction-time size, so the wanted distance
    /// is applied once the control has been laid out at its real size.
    /// </summary>
    private static void SplitWhenSized(SplitContainer split, int distance)
    {
        bool done = false;
        split.SizeChanged += (_, _) =>
        {
            int extent = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
            if (done || extent < distance + 80) return;
            done = true;
            split.SplitterDistance = distance;
        };
    }

    private void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
        _log.Visible = _drawerOpen;
        _drawer.Height = _drawerOpen ? 190 : 44;
        _activityToggle.Text = _drawerOpen ? "Hide" : "Activity";
    }

    /// <summary>Reflects progress in the side navigation: done steps get a check, the next step a glowing dot.</summary>
    private void UpdateNavStates()
    {
        bool video = _video is not null, transcript = _transcript is { Segments.Count: > 0 }, suggestions = _suggestions is { Shorts.Count: > 0 };
        bool generated = _generated.Count > 0;
        _nav.SetState(0, video ? StepState.Done : StepState.Ready);
        _nav.SetState(1, transcript ? StepState.Done : (video ? StepState.Ready : StepState.Pending));
        _nav.SetState(2, suggestions ? StepState.Done : (transcript ? StepState.Ready : StepState.Pending));
        _nav.SetState(3, suggestions ? StepState.Done : (transcript ? StepState.Ready : StepState.Pending));
        _nav.SetState(4, generated ? StepState.Done : (suggestions ? StepState.Ready : StepState.Pending));
        _nav.SetState(5, suggestions ? StepState.Ready : StepState.Pending);
        _nav.SetState(6, _generated.Any(g => g.Sent) ? StepState.Done : (generated ? StepState.Ready : StepState.Pending));
    }

    /// <summary>Step 7: publishing goes through galiluna's Shorts API; the panel owns the UI and
    /// this form only lends it the settings, the generated files and the project save.</summary>
    private void BuildPublishTab()
    {
        _publishPanel.ClientFactory = () => SettingsStore.CreateGaliLunaClient(_settings);
        _publishPanel.Files = () => _generated;
        _publishPanel.Saved = () => { SaveProject(); UpdateNavStates(); };
        _publishPanel.Log = Log;
        _publishPanel.OpenSettings = OpenSettings;
        _publishPanel.Delete = DeleteGenerated;
        _publishPanel.MakeCoverLeadCopy = (video, cover, ct) => _renderer.MakeCoverLeadCopyAsync(video, cover, ct);
        _publishPanel.OpenSignIn = () =>
        {
            using var dlg = new GaliLunaSignInForm(_settings);
            bool ok = dlg.ShowDialog(this) == DialogResult.OK;
            if (ok) Log("Signed in with galiluna.");
            return ok;
        };
        _tabPublish.Controls.Add(_publishPanel);
    }

    private void ApplyTheme()
    {
        foreach (var b in new[] { _download, _downloadTranscribe, _transcribe, _analyze, _generate, _continueToGenerate, _libraryTranscribe, _gptBuild, _gptCopy, _gptImport, _playPause, _renderPreview }) Theme.Primary(b);
        // the editor's white buttons carry the Higgsfield icon set (gradient buttons keep their white glyphs)
        foreach (var (b, icon) in new (FancyButton, string)[]
        {
            (_autoCamera, "auto-camera"), (_changeCamera, "change-camera"), (_setCover, "use-frame"), (_pickCoverImage, "choose-image"),
            (_clearCover, "reset"), (_copyCover, "copy-image"), (_copyCoverPrompt, "copy-prompt"), (_generateCover, "ai-magic"),
            (_keyframeDelete, "delete"), (_keyframeClear, "clear"), (_removeLayer, "delete"),
            (_addLayer, "choose-image"), (_pasteLayer, "copy-image"), (_segDelete, "delete"), (_retranslate, "ai-magic"),
            (_segPlay, "render"), (_renderClear, "clear"), (_addVideoLayer, "use-frame"),
        })
            b.Picture = Theme.Icon(icon);
        // icon + label must fit (never a truncated label); measured once every label is final
        Load += (_, _) => FitPictureButtons(this);
        if (Theme.Icon("camera-mode") is { } modeIcon)
        {
            _cameraMode.Image = new Bitmap(modeIcon, new Size(20, 20));
            _cameraMode.TextImageRelation = TextImageRelation.ImageBeforeText;
            _cameraMode.ImageAlign = ContentAlignment.MiddleCenter;
        }
        Theme.Apply(this);
        _videoInfo.Font = Theme.Body(10f);
        _videoInfo.ForeColor = Theme.TextSecondary;
        _log.Font = Theme.Mono();
        _log.BackColor = Theme.Bg;
        _log.ForeColor = Theme.TextSecondary;
        _status.ForeColor = Theme.TextMuted;
        _suggestDetail.Font = Theme.Body(9.5f);
        _gptPrompt.Font = Theme.Mono(9f);
        _gptAnswer.Font = Theme.Mono(9f);
        _summary.ForeColor = Theme.TextMuted;
        _summary.BackColor = Theme.Elevated;
        _preview.BackColor = Theme.DarkBg;
        _cameraMode.BackColor = Theme.Elevated;
        _cameraMode.ForeColor = Theme.TextSecondary;
        _cameraMode.FlatStyle = FlatStyle.Flat;
        _cameraMode.FlatAppearance.BorderColor = Theme.BorderStrong;
        _cameraMode.FlatAppearance.CheckedBackColor = Theme.Nebula;
        _generate.Font = Theme.Body(10f, FontStyle.Bold);
        _generate.Height = 36; _generate.Width = 210; _generate.Glyph = "";
        _continueToGenerate.Glyph = ""; _continueToGenerate.Font = Theme.Body(10f, FontStyle.Bold);
        _renderPreview.Glyph = "";
        _renderPreview.Glyph = "";
        _transcribe.Glyph = "";
        _analyze.Glyph = "";
        _gptCopy.Glyph = "";
        _changeCamera.Glyph = "\uE89E";
        _autoCamera.Glyph = "";
        _playPause.Glyph = "";
        UpdateNavStates();
    }

    private void BuildVideoTab()
    {
        // Top: current video details. Bottom: library of already downloaded videos.
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1 };
        SplitWhenSized(split, 200);

        var info = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_openFile);
        buttons.Controls.Add(_openFolder);
        _videoInfo.Text = "";
        info.Controls.Add(_emptyVideo);
        info.Controls.Add(_videoInfo);
        _emptyVideo.BringToFront();
        info.Controls.Add(buttons);
        split.Panel1.Controls.Add(info);

        var libBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        libBar.Controls.Add(new Label { Text = "Downloaded videos", AutoSize = true, Margin = new Padding(0, 7, 14, 0), Font = Theme.HeadingFont(9.5f) });
        libBar.Controls.Add(_libraryTranscribe);
        libBar.Controls.Add(_libraryLoad);
        libBar.Controls.Add(_libraryRefresh);
        libBar.Controls.Add(_libraryOpenFolder);
        libBar.Controls.Add(_libraryDelete);

        _library.Columns.Add("File", 520);
        _library.Columns.Add("Length", 80);
        _library.Columns.Add("Transcript", 90);
        _library.Columns.Add("Shorts", 70);
        _library.Columns.Add("Downloaded", 140);
        _library.Columns.Add("Size", 80);
        Theme.FillColumn(_library, 0);

        split.Panel2.Controls.Add(_library);
        split.Panel2.Controls.Add(libBar);
        _tabVideo.Controls.Add(split);
        _tabVideo.Controls.Add(_commandBar);
    }

    private void BuildTranscriptTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0), WrapContents = true };
        foreach (var m in Transcriber.ModelNames) _whisperModel.Items.Add(Transcriber.DescribeModel(m));
        _whisperModel.SelectedIndex = Math.Max(0, Array.IndexOf(Transcriber.ModelNames, _settings.WhisperModel));
        _language.Items.AddRange(LanguageOptions.DisplayNames);
        _language.SelectedIndex = LanguageOptions.IndexOfCode(_settings.WhisperLanguage);
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
        bar.Controls.Add(_deleteTranscript);

        _segments.Columns.Add("Start", 80);
        _segments.Columns.Add("End", 80);
        _segments.Columns.Add("Text", 820);
        _segments.Columns.Add("Peak dB", 70);
        Theme.FillColumn(_segments, 2);

        _tabTranscript.Controls.Add(_emptyTranscript);
        _tabTranscript.Controls.Add(_segments);
        _emptyTranscript.BringToFront();
        _tabTranscript.Controls.Add(bar);
    }

    private void BuildChatGptTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0), WrapContents = true };
        bar.Controls.Add(_gptBuild);
        bar.Controls.Add(new Label { Text = "Shorts:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_gptCount);
        // the AI picks how many shorts by default; the number only shows when it is unticked
        _gptAuto.Checked = true;
        _gptCount.Visible = false;
        _gptAuto.CheckedChanged += (_, _) => { _gptCount.Enabled = _gptCount.Visible = !_gptAuto.Checked; };
        bar.Controls.Add(_gptAuto);
        bar.Controls.Add(new Label { Text = "Length (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_gptMin);
        bar.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_gptMax);
        bar.Controls.Add(PostTargetBoxes());

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
        // Copy sits right under the prompt, mirroring "Import" under the answer: read on the left, act, paste on the right.
        var leftBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _gptCopy.Width = 160; _gptCopy.Margin = new Padding(8, 0, 0, 0);
        leftBar.Controls.Add(_gptCopy);
        leftBar.Controls.Add(_gptSave);
        left.Controls.Add(_gptPrompt);
        left.Controls.Add(leftBar);
        left.Controls.Add(new Label { Text = "Prompt for ChatGPT", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray });
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var rightBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        rightBar.Controls.Add(_gptImport);
        rightBar.Controls.Add(_gptPaste);
        right.Controls.Add(_gptAnswer);
        right.Controls.Add(rightBar);
        right.Controls.Add(new Label { Text = "ChatGPT's answer", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray });
        split.Panel2.Controls.Add(right);

        _tabChatGpt.Controls.Add(split);
        _tabChatGpt.Controls.Add(help);
        _tabChatGpt.Controls.Add(bar);
    }

    private void BuildSuggestTab()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0), WrapContents = true };
        _count.Value = Math.Clamp(_settings.SuggestionCount, 1, 20);
        _minSec.Value = Math.Clamp(_settings.MinShortSeconds, 5, 180);
        _maxSec.Value = Math.Clamp(_settings.MaxShortSeconds, 10, 180);
        bar.Controls.Add(_analyze);
        bar.Controls.Add(new Label { Text = "Shorts:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_count);
        _autoCount.Checked = true;
        _count.Visible = false;
        _autoCount.CheckedChanged += (_, _) => { _count.Enabled = _count.Visible = !_autoCount.Checked; };
        bar.Controls.Add(_autoCount);
        bar.Controls.Add(new Label { Text = "Length (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        bar.Controls.Add(_minSec);
        bar.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_maxSec);
        bar.Controls.Add(PostTargetBoxes());
        bar.Controls.Add(new Label { Text = "", Width = 20 });
        bar.Controls.Add(_previewClip);
        bar.Controls.Add(_editClip);
        bar.Controls.Add(_addClip);
        bar.Controls.Add(_removeClip);

        _suggestList.Columns.Add("Use", 52);
        _suggestList.Columns.Add("#", 30);
        _suggestList.Columns.Add("Title", 360);
        _suggestList.Columns.Add("Start", 70);
        _suggestList.Columns.Add("End", 70);
        _suggestList.Columns.Add("Length", 60);
        _suggestList.Columns.Add("Viral score", 95);
        _suggestList.Columns.Add("Emotion", 110);
        _suggestList.Columns.Add("English", 64);
        Theme.FillColumn(_suggestList, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        SplitWhenSized(split, 260);
        split.Panel1.Controls.Add(_emptySuggest);
        split.Panel1.Controls.Add(_suggestList);
        _emptySuggest.BringToFront();
        split.Panel2.Controls.Add(_suggestDetail);
        split.Panel2.Controls.Add(_summary);
        _suggestDetail.Text = "Get suggestions from '3. Ask ChatGPT' (copy/paste) or click 'Analyze with Claude' (API key). Select one to read why it could go viral.";

        // footer: tick the moments above, then move on with one clear primary action
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 2, Padding = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selectionSummary.Font = Theme.Body(9f);
        footer.Controls.Add(_selectionSummary, 0, 0);
        _continueToGenerate.Margin = Padding.Empty;
        footer.Controls.Add(_continueToGenerate, 1, 0);

        _tabSuggest.Controls.Add(split);
        _tabSuggest.Controls.Add(footer);
        _tabSuggest.Controls.Add(bar);
    }

    private void BuildEditorTab()
    {
        _editorHint.Text = "Click the video to play or pause. Drag the caption to place it, double-click it to fix the words, right-click it to edit its line in the table. Camera mode: drag the 9:16 box or scroll to zoom; auto framing resumes 4 s later. " +
                           "Edit the Text column to fix words. Style and framing come from Generate shorts.";

        // Three resizable columns: shorts + actions | player | transcript. Inside the left column the
        // shorts list, the actions and the keyframe timeline are separated by a draggable splitter too,
        // because a long list of shorts or of camera cuts needs room the fixed heights never gave it.
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 4, 6) };
        _editClips.Columns.Add("Short", 190);
        _editClips.Columns.Add("Length", 66);
        _editClips.Columns.Add("Score", 56);
        Theme.FillColumn(_editClips, 0);
        _keyframes.Columns.Add("At", 50);
        _keyframes.Columns.Add("What", 64);
        _keyframes.Columns.Add("Details", 190);
        Theme.FillColumn(_keyframes, 2);
        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        SplitWhenSized(leftSplit, 170);
        var clipsHost = new Panel { Dock = DockStyle.Fill };
        // no "Remove from selected" button: the Delete key removes the selected short from this list
        clipsHost.Controls.Add(_editClips);
        clipsHost.Controls.Add(new Label { Text = "Selected shorts  (Delete key removes one)", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, UseMnemonic = false });

        // actions for the current short: one primary, a row of two, the mode toggle, then the cover
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 8, 0, 4) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void Row(Control c, bool span)
        {
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            int r = actions.RowCount++;
            actions.Controls.Add(c, 0, r);
            if (span) actions.SetColumnSpan(c, 2);
        }
        _renderPreview.Height = 38; _renderPreview.Dock = DockStyle.Fill; _renderPreview.Margin = new Padding(0, 0, 0, 8);
        _autoCamera.Text = "Auto camera"; _autoCamera.Dock = DockStyle.Fill; _autoCamera.Margin = new Padding(0, 0, 4, 8);
        _changeCamera.Dock = DockStyle.Fill; _changeCamera.Margin = new Padding(4, 0, 0, 8);
        _cameraMode.Dock = DockStyle.Fill; _cameraMode.Height = 32; _cameraMode.Margin = new Padding(0, 0, 0, 12);
        actions.RowCount = 0;
        Row(_renderPreview, true);
        actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actions.Controls.Add(_autoCamera, 0, actions.RowCount);
        actions.Controls.Add(_changeCamera, 1, actions.RowCount++);
        Row(_cameraMode, true);
        var coverHead = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        coverHead.Controls.Add(new Label { Text = "Cover / thumbnail", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 8, 0) });
        _coverLang.Items.AddRange(new object[] { "Original", "English" });
        _coverLang.SelectedIndex = 0;
        _coverLang.Margin = new Padding(0, 2, 0, 0);
        coverHead.Controls.Add(_coverLang);
        Row(coverHead, true);
        var cover = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        cover.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cover.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _coverPreview.Width = 78; _coverPreview.Height = 138; _coverPreview.Margin = new Padding(0, 0, 10, 0);
        cover.Controls.Add(_coverPreview, 0, 0);
        var coverButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        _setCover.Width = _pickCoverImage.Width = _clearCover.Width = 150;
        _setCover.Margin = _pickCoverImage.Margin = _clearCover.Margin = _copyCover.Margin = _copyCoverPrompt.Margin = _generateCover.Margin = new Padding(0, 0, 0, 6);
        _coverInfo.MaximumSize = new Size(150, 0);
        coverButtons.Controls.Add(_setCover);
        coverButtons.Controls.Add(_pickCoverImage);
        coverButtons.Controls.Add(_clearCover);
        coverButtons.Controls.Add(_coverInfo);
        cover.Controls.Add(coverButtons, 1, 0);
        Row(cover, true);
        // make it viral: copy the frame and a brief for ChatGPT, or (developer only) let Higgsfield redraw it
        Row(new Label { Text = "Make it viral", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 0, 2) }, true);
        var viral = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        _copyCover.Text = "Copy image"; _copyCover.Width = 118;
        _copyCoverPrompt.Text = "Copy prompt"; _copyCoverPrompt.Width = 118; _copyCoverPrompt.Glyph = "";
        _generateCover.Text = "Higgsfield"; _generateCover.Width = 118;
        _copyCover.Margin = _copyCoverPrompt.Margin = _generateCover.Margin = new Padding(0, 0, 6, 6);
        viral.Controls.Add(_copyCover);
        viral.Controls.Add(_copyCoverPrompt);
        viral.Controls.Add(_generateCover);
        Row(viral, true);

        var kfHost = new Panel { Dock = DockStyle.Fill };
        var kfBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        kfBar.Controls.Add(_keyframeDelete);
        kfBar.Controls.Add(_keyframeClear);
        kfHost.Controls.Add(_keyframes);
        kfHost.Controls.Add(kfBar);
        kfHost.Controls.Add(new Label { Text = "Camera cuts and caption positions", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, UseMnemonic = false });
        // the actions scroll with the column when the window is short, so nothing is ever clipped
        var actionsScroll = new Panel { Dock = DockStyle.Top, AutoScroll = true, Height = 420 };
        actionsScroll.Controls.Add(actions);
        actions.SizeChanged += (_, _) => actionsScroll.Height = Math.Min(actions.Height + 4, 430);
        kfHost.Controls.Add(actionsScroll);
        leftSplit.Panel1.Controls.Add(clipsHost);
        leftSplit.Panel2.Controls.Add(kfHost);
        left.Controls.Add(leftSplit);

        // right: transcript lines of the clip
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 0, 6) };
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "Start", HeaderText = "Start", ReadOnly = true, FillWeight = 18 });
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "End", HeaderText = "End", ReadOnly = true, FillWeight = 18 });
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Original", FillWeight = 64 });
        _editSegments.Columns.Add(new DataGridViewTextBoxColumn { Name = "English", HeaderText = "English", FillWeight = 64, Visible = false });
        _editSegments.Columns["English"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        // the times stay readable however narrow the column gets; the text columns share the rest
        _editSegments.Columns["Start"]!.MinimumWidth = _editSegments.Columns["End"]!.MinimumWidth = 58;
        _editSegments.Columns["Text"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _editSegments.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        var segBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        segBar.Controls.Add(_segPlay);
        segBar.Controls.Add(_segDelete);
        // which captions the video preview shows; the English column stays visible either way
        _previewLang.Items.AddRange(new object[] { "Original", "English" });
        _previewLang.SelectedIndex = 0;
        _previewLang.Margin = new Padding(0, 5, 8, 0);
        _previewLangLabel.ForeColor = Theme.TextSecondary;
        // label and dropdown wrap together, never apart
        _previewLangGroup.Controls.Add(_previewLangLabel);
        _previewLangGroup.Controls.Add(_previewLang);
        _retranslate.Margin = new Padding(4, 0, 0, 0);
        segBar.Controls.Add(_retranslate);
        right.Controls.Add(_editSegments);
        right.Controls.Add(segBar);
        right.Controls.Add(new Label { Text = "Transcript of this short", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, UseMnemonic = false });

        // image layers: one row per illustration, its window, look and entrance
        var layersHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 0, 6) };
        _layers.Columns.Add("From", 58);
        _layers.Columns.Add("To", 58);
        _layers.Columns.Add("Image", 120);
        _layers.Columns.Add("Style", 72);
        Theme.FillColumn(_layers, 2);
        foreach (var st in ImageOverlay.Styles) _layerStyle.Items.Add(st.Name);
        foreach (var an in ImageOverlay.Animations) _layerAnim.Items.Add(an.Name);
        var props = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 8, 0, 0), Visible = false };
        _layerPropsPanel = props; // shown only while an image is selected, so an empty Images table leaves room for the renders
        props.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        props.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void Prop(int row, int col, string label, Control c)
        {
            props.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(col == 0 ? 0 : 10, 8, 6, 0) }, col * 2, row);
            c.Margin = new Padding(0, 4, 0, 2);
            if (c is ComboBox) c.Dock = DockStyle.Fill;
            props.Controls.Add(c, col * 2 + 1, row);
        }
        Prop(0, 0, "From (s)", _layerFrom); Prop(0, 1, "To (s)", _layerTo);
        Prop(1, 0, "Style", _layerStyle); Prop(1, 1, "Entrance", _layerAnim);
        Prop(2, 0, "Size %", _layerSize); Prop(2, 1, "Rotate", _layerRot);
        var layerBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(2, 8, 0, 0), WrapContents = false };
        layerBar.Controls.Add(_addLayer);
        layerBar.Controls.Add(_pasteLayer);
        layerBar.Controls.Add(_addVideoLayer);
        layerBar.Controls.Add(_removeLayer);
        _addLayer.Text = "Add"; _pasteLayer.Text = "Paste"; _addLayer.Width = 84; _pasteLayer.Width = 90; _removeLayer.Width = 100;
        foreach (Control c in new Control[] { layersHost, _layers })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) => e.Effect = DroppedImages(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += async (_, e) => await AddLayersAsync(DroppedImages(e.Data), CurrentClipTime());
        }
        layersHost.Controls.Add(_layers);
        layersHost.Controls.Add(props);
        layersHost.Controls.Add(layerBar);
        // AutoSize off + fixed height lets the hint wrap onto a second line instead of being cut off
        layersHost.Controls.Add(new Label { Text = "Images and video clips. Add video shows a moment of another video (double-click its row to change it); on the video, drag a full-screen clip to frame it and scroll to zoom.", Dock = DockStyle.Top, Height = 40, AutoSize = false, ForeColor = Color.DimGray, UseMnemonic = false });

        // render queue under the images
        _renders.Columns.Add("Short", 200);
        _renders.Columns.Add("Version", 150);
        _renders.Columns.Add("Status", 200);
        Theme.FillColumn(_renders, 2);
        var rendersHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 0, 6) };
        var rendersHead = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 6) };
        rendersHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rendersHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rendersHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rendersHead.Controls.Add(new Label { Text = "Renders and posts", AutoSize = true, Font = Theme.HeadingFont(10.5f), ForeColor = Theme.Heading, Margin = new Padding(0, 8, 10, 0) }, 0, 0);
        rendersHead.Controls.Add(_rendersCounts, 1, 0);
        // the buttons sit on their own line under the title, so a narrow column never makes them overlap it
        var rendersButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        rendersButtons.Controls.Add(_renderCancel);
        _renderClear.Margin = Padding.Empty;
        rendersButtons.Controls.Add(_renderClear);
        rendersHead.Controls.Add(rendersButtons, 0, 1);
        rendersHead.SetColumnSpan(rendersButtons, 3);
        var rendersBody = new Panel { Dock = DockStyle.Fill };
        rendersBody.Controls.Add(_renders);
        rendersBody.Controls.Add(_rendersEmpty);
        _rendersEmpty.BringToFront();
        rendersHost.Controls.Add(rendersBody);
        rendersHost.Controls.Add(rendersHead);
        var bottomSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        bool bottomSet = false;
        bottomSplit.SizeChanged += (_, _) =>
        {
            if (bottomSet || bottomSplit.Height < 240) return;
            bottomSet = true;
            bottomSplit.Panel1MinSize = 110; bottomSplit.Panel2MinSize = 110;
            bottomSplit.SplitterDistance = Math.Max(110, (int)(bottomSplit.Height * 0.5));
        };
        bottomSplit.Panel1.Controls.Add(layersHost);
        bottomSplit.Panel2.Controls.Add(rendersHost);
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        bool rightSet = false;
        rightSplit.SizeChanged += (_, _) =>
        {
            if (rightSet || rightSplit.Height < 500) return;
            rightSet = true;
            rightSplit.Panel1MinSize = 160; rightSplit.Panel2MinSize = 220;
            rightSplit.SplitterDistance = (int)(rightSplit.Height * 0.52);
        };
        rightSplit.Panel1.Controls.Add(right);
        rightSplit.Panel2.Controls.Add(bottomSplit);

        // center: player + controls
        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        // row 1: the scrub bar gets the full width; row 2: Play, "Image at <time>", Start / End (wraps when narrow)
        var controls = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _timeline.Margin = new Padding(0, 2, 8, 0);
        _timeLabel.Margin = new Padding(0, 8, 0, 0);
        controls.Controls.Add(_timeline, 0, 0);
        controls.Controls.Add(_timeLabel, 1, 0);
        var actionsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 2, 0, 0), Margin = Padding.Empty };
        _playPause.Width = 84; _playPause.Margin = new Padding(0, 3, 8, 3);
        // adding an illustration is one click from wherever the video is
        _addLayerHere.Margin = new Padding(0, 3, 16, 3); _addLayerHere.Height = _playPause.Height;
        actionsRow.Controls.Add(_playPause);
        // which captions the player shows sits next to Play, where it is used
        _previewLangGroup.Margin = new Padding(0, 3, 12, 3);
        actionsRow.Controls.Add(_previewLangGroup);
        actionsRow.Controls.Add(_addLayerHere);
        var trim = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        trim.Controls.Add(new Label { Text = "Start (s):", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        trim.Controls.Add(_clipStart);
        trim.Controls.Add(new Label { Text = "End (s):", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        trim.Controls.Add(_clipEnd);
        actionsRow.Controls.Add(trim);
        _keyframeHint.Margin = new Padding(12, 10, 0, 0);
        actionsRow.Controls.Add(_keyframeHint);
        controls.Controls.Add(actionsRow, 0, 1);
        controls.SetColumnSpan(actionsRow, 2);
        center.Controls.Add(_player);
        center.Controls.Add(controls);

        // columns: drag the splitters to give the list, the player or the transcript more room
        // Default proportions (left 15 %, player 45 %, transcript + images 40 %) follow the window, so maximizing
        // gives the tables the extra room. Once the user drags a splitter, that layout is kept.
        var columns = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
        columns.Panel1.Controls.Add(left);
        var inner = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        bool userMoved = false, sizing = false; _ = sizing;
        void Proportion()
        {
            if (userMoved || columns.Width < 900) return;
            sizing = true;
            try
            {
                columns.Panel1MinSize = 300;
                columns.SplitterDistance = Math.Max(310, Math.Min(380, (int)(columns.Width * 0.15)));
                if (inner.Width > 700)
                {
                    inner.Panel1MinSize = 360; inner.Panel2MinSize = 300;
                    int right = Math.Max(340, (int)(columns.Width * 0.40));
                    inner.SplitterDistance = Math.Max(inner.Panel1MinSize, inner.Width - right);
                }
            }
            catch (InvalidOperationException) { } // sizes not settled yet; the next resize applies it
            finally { sizing = false; }
        }
        columns.SizeChanged += (_, _) => Proportion();
        inner.SizeChanged += (_, _) => Proportion();
        // a real drag ends with a mouse release on the splitter bar (resizes also raise SplitterMoved)
        columns.MouseUp += (_, _) => userMoved = true;
        inner.MouseUp += (_, _) => userMoved = true;
        inner.Panel1.Controls.Add(center);
        inner.Panel2.Controls.Add(rightSplit);
        columns.Panel2.Controls.Add(inner);

        _tabEditor.Controls.Add(columns);
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
        _style.SelectedIndex = Math.Max(0, CaptionStyle.All.ToList().FindIndex(s => s.Id == "karaoke"));
        _crop.Items.AddRange(new object[] { "Vertical 9:16 - center crop", "Vertical 9:16 - blurred background", "Keep original aspect ratio" });
        _crop.SelectedIndex = 0;
        _outputFolder.Text = _settings.OutputFolder;

        var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var browse = new FancyButton { Text = "...", Width = 36 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : "" };
            if (d.ShowDialog(this) == DialogResult.OK) _outputFolder.Text = d.SelectedPath;
        };
        folderRow.Controls.Add(_outputFolder); folderRow.Controls.Add(browse);

        foreach (var l in VisualLook.All) _look.Items.Add(l.Name);
        _look.SelectedIndex = 0;
        _lookDesc.Text = VisualLook.All[0].Description;
        Add("Framing", _crop);
        Add("", _autoCameraOpt);
        Add("Look", _look);
        Add("", _lookDesc);
        Add("", _addCaptions);
        _captionLang.Items.AddRange(new object[] { "Original language", "English", "Both (two files per short)" });
        _captionLang.SelectedIndex = _settings.CaptionLanguage switch { "en" => 1, "both" => 2, _ => 0 };
        Add("Captions in", _captionLang);
        Add("Caption style", _style);
        Add("", _styleDesc);
        Add("Words per caption", _wordsPerCaption);
        Add("Font size", _fontSize);
        Add("", new Label { Text = "0 = style default. Sizes are for a 1080x1920 frame.", AutoSize = true, ForeColor = Color.DimGray });
        Add("", _burnHook);
        Add("", _includeReactions);
        Add("Output folder", folderRow);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330 };

        // top: what will be rendered (ticked in Suggestions; can still be unticked here), the caption
        // preview beside it, and the one primary action underneath
        var top = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _queue.Columns.Add("Render", 58);
        _queue.Columns.Add("#", 26);
        _queue.Columns.Add("Title", 200);
        _queue.Columns.Add("Range", 112);
        _queue.Columns.Add("Status", 130);
        // the title takes whatever width is left, so the range and status stay visible without scrolling
        Theme.FillColumn(_queue, 2);
        var queueHost = new Panel { Dock = DockStyle.Fill };
        var queueBar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 3, Padding = new Padding(0, 10, 0, 0) };
        queueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queueBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _backToSuggestions.Glyph = "\uE72B"; _backToSuggestions.Width = 170; _backToSuggestions.Height = 36; _backToSuggestions.Margin = Padding.Empty;
        _generate.Margin = Padding.Empty;
        queueBar.Controls.Add(_backToSuggestions, 0, 0);
        queueBar.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 0);
        queueBar.Controls.Add(_generate, 2, 0);
        queueHost.Controls.Add(_queue);
        queueHost.Controls.Add(queueBar);
        queueHost.Controls.Add(_queueTitle);
        var previewHost = new Panel { Dock = DockStyle.Right, Width = 230, Padding = new Padding(12, 0, 0, 0) };
        previewHost.Controls.Add(_preview);
        previewHost.Controls.Add(new Label { Text = "Caption preview", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray });
        top.Controls.Add(queueHost);
        top.Controls.Add(previewHost);
        right.Panel1.Controls.Add(top);

        _results.Columns.Add("Short", 300);
        _results.Columns.Add("Status", 90);
        _results.Columns.Add("File", 500);
        Theme.FillColumn(_results, 2);
        var resultsHost = new Panel { Dock = DockStyle.Fill };
        var resultsBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
        resultsBar.Controls.Add(_deleteShort);
        resultsHost.Controls.Add(_results);
        resultsHost.Controls.Add(resultsBar);
        resultsHost.Controls.Add(new Label { Text = "Generated files (double-click to play)", Dock = DockStyle.Top, Height = 26, ForeColor = Color.DimGray, Padding = new Padding(4, 0, 0, 0) });
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
        _library.SelectedIndexChanged += (_, _) => _libraryLoad.Enabled = _libraryTranscribe.Enabled = _libraryDelete.Enabled = _library.SelectedItems.Count > 0 && _cts is null;
        _libraryDelete.Click += (_, _) => DeleteLibraryVideo();
        _deleteTranscript.Click += (_, _) => DeleteTranscript();
        _deleteShort.Click += (_, _) => DeleteGeneratedShort();
        _results.SelectedIndexChanged += (_, _) => _deleteShort.Enabled = _results.SelectedItems.Count > 0 && _cts is null;
        _library.DoubleClick += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: false);
        _libraryLoad.Click += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: false);
        _libraryTranscribe.Click += async (_, _) => await LoadFromLibraryAsync(thenTranscribe: true);
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_tabs.SelectedTab == _tabVideo) RefreshLibrary();
            else if (_tabs.SelectedTab == _tabGenerate) RefreshQueue();
            else if (_tabs.SelectedTab == _tabEditor) await EnterEditorAsync();
            else if (_tabs.SelectedTab == _tabPublish) await _publishPanel.RefreshAsync();
            else await _player.PauseAsync();
        };

        // editor tab
        _editClips.SelectedIndexChanged += async (_, _) => await LoadClipInEditorAsync();
        _playPause.Click += async (_, _) => await _player.TogglePlayAsync();
        // the button shows what a click does: Play with the play icon while paused, Pause with the pause icon while playing
        _player.PlayingChanged += playing => { _playPause.Text = playing ? "Pause" : "Play"; _playPause.Glyph = playing ? "" : ""; };
        _player.TimeChanged += OnPlayerTime;
        _player.Status += s => Log("Player: " + s);
        _player.CaptionMoved += (x, y, t) => BeginInvoke(() => OnCaptionMoved(x, y, t));
        _player.CameraMoved += (x, y, z, t) => BeginInvoke(() => OnCameraMoved(x, y, z, t));
        _player.EditRequested += t => BeginInvoke(async () => { if (SegmentAt(t) is { } seg) await _player.BeginEditAsync(EnglishMode ? seg.English ?? seg.Text : seg.Text); });
        _player.TextEdited += (t, text) => BeginInvoke(() => OnCaptionTextEdited(t, text));
        _player.LineRequested += t => BeginInvoke(() => EditLineInGrid(t));
        _player.LayerSelected += id => BeginInvoke(() => SelectLayerRow(id));
        _player.LayerChanged += (id, x, y, size, rot) => BeginInvoke(() => OnLayerMovedOnVideo(id, x, y, size, rot));
        _addLayer.Click += async (_, _) => await PickAndAddLayerAsync(CurrentClipTime());
        _addVideoLayer.Click += async (_, _) => await AddVideoLayerAsync(CurrentClipTime());
        _layers.DoubleClick += async (_, _) => { if (SelectedLayer is { IsVideo: true } v) await EditVideoLayerAsync(v); };
        _player.VideoLayerChanged += (id, cx, cy, zoom, x, y, size) => BeginInvoke(() =>
        {
            var o = _editing?.Overlays.FirstOrDefault(l => l.Id == id);
            if (o is null) return;
            o.CropX = cx; o.CropY = cy; o.Zoom = zoom; o.X = x; o.Y = y; o.Size = size;
            SaveProject();
            RefreshLayerList(o.Id);
            _ = PushLayersAsync();
        });
        _addLayerHere.Click += async (_, _) => await PickAndAddLayerAsync(CurrentClipTime());
        _pasteLayer.Click += async (_, _) => await PasteLayerAsync();
        _removeLayer.Click += async (_, _) => await RemoveLayerAsync();
        _layers.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; await RemoveLayerAsync(); } };
        _layers.SelectedIndexChanged += async (_, _) => await OnLayerRowSelectedAsync();
        _layerFrom.ValueChanged += async (_, _) => await OnLayerPropsChangedAsync();
        _layerTo.ValueChanged += async (_, _) => await OnLayerPropsChangedAsync();
        _layerSize.ValueChanged += async (_, _) => await OnLayerPropsChangedAsync();
        _layerRot.ValueChanged += async (_, _) => await OnLayerPropsChangedAsync();
        _layerStyle.SelectedIndexChanged += async (_, _) => await OnLayerPropsChangedAsync();
        _layerAnim.SelectedIndexChanged += async (_, _) =>
        {
            await OnLayerPropsChangedAsync();
            if (!_loadingLayer && SelectedLayer is { } o) await ShowLayerEntranceAsync(o); // show the chosen effect once
        };
        // right-click a transcript line: add an image for exactly that moment
        _segMenu.Items.Add("Add image for this line...", null, async (_, _) =>
        {
            if (_editing is not null && _editSegments.CurrentRow?.Tag is TranscriptSegment line)
                await PickAndAddLayerAsync(Math.Max(0, line.Start - _editing.StartSeconds));
        });
        _editSegments.ContextMenuStrip = _segMenu;
        _editSegments.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _editSegments.CurrentCell = _editSegments.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; };
        _removeFromSelection.Click += (_, _) => RemoveEditingFromSelection();
        _editClips.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveEditingFromSelection(); } };
        _cameraMode.CheckedChanged += async (_, _) =>
        {
            _cameraMode.BackColor = _cameraMode.Checked ? Theme.Nebula : Theme.Elevated;
            _cameraMode.ForeColor = _cameraMode.Checked ? Color.White : Theme.Heading;
            if (_cameraMode.Checked) await _player.PauseAsync();
            await _player.SetCameraModeAsync(_cameraMode.Checked);
        };
        _autoCamera.Click += async (_, _) => await AutoCameraAsync();
        _changeCamera.Click += async (_, _) => await ChangeCameraAsync();
        _look.SelectedIndexChanged += async (_, _) =>
        {
            var look = VisualLook.All[Math.Max(0, _look.SelectedIndex)];
            _lookDesc.Text = look.Description;
            if (_player.IsReady) await _player.SetLookAsync(look.Css);
        };
        // one click on a camera cut or caption position jumps there
        _keyframes.SelectedIndexChanged += async (_, _) =>
        {
            if (_editing is not null && _keyframes.SelectedItems.Count > 0 && _keyframes.SelectedItems[0].Tag is IKeyframe k)
                await _player.SeekAsync(_editing.StartSeconds + k.Time);
        };
        _coverLang.SelectedIndexChanged += async (_, _) => await UpdateCoverPreviewAsync();
        _setCover.Click += async (_, _) =>
        {
            if (_editing is null) return;
            _editing.SetCover(CoverLanguage, RelativeTime, null);
            SaveProject();
            await UpdateCoverPreviewAsync();
        };
        _pickCoverImage.Click += async (_, _) =>
        {
            if (_editing is null) return;
            using var d = new OpenFileDialog { Title = "Choose a cover image", Filter = "Images|*.png;*.jpg;*.jpeg;*.webp|All files|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _editing.SetCover(CoverLanguage, null, d.FileName);
            SaveProject();
            await UpdateCoverPreviewAsync();
        };
        _copyCover.Click += (_, _) =>
        {
            if (_coverPreview.Image is null) { _status.Text = "No cover to copy yet."; return; }
            try { Clipboard.SetImage(_coverPreview.Image); _status.Text = "Cover image copied. Paste it into ChatGPT together with the thumbnail prompt."; }
            catch (Exception ex) { AppDialog.Show(this, ex.Message, "Copy failed"); }
        };
        _copyCoverPrompt.Click += (_, _) =>
        {
            if (_editing is null) return;
            try { Clipboard.SetText(ThumbnailBrief.Build(CoverBriefShort(_editing), CoverBriefLanguage, ThumbnailBrief.HumanPlaceholder)); _status.Text = "Thumbnail prompt copied. Paste it into ChatGPT with the cover image and fill in the last line."; }
            catch (Exception ex) { AppDialog.Show(this, ex.Message, "Copy failed"); }
        };
        _generateCover.Click += async (_, _) => await GenerateCoverWithHiggsfieldAsync();
        _clearCover.Click += async (_, _) =>
        {
            if (_editing is null) return;
            _editing.SetCover(CoverLanguage, null, null); // the English cover goes back to the original's
            SaveProject();
            await UpdateCoverPreviewAsync();
        };
        _keyframes.DoubleClick += async (_, _) =>
        {
            if (_editing is not null && _keyframes.SelectedItems.Count > 0 && _keyframes.SelectedItems[0].Tag is IKeyframe k)
                await _player.SeekAsync(_editing.StartSeconds + k.Time);
        };
        _keyframeDelete.Click += async (_, _) => await DeleteKeyframeAsync();
        _keyframeClear.Click += async (_, _) =>
        {
            if (_editing is null) return;
            if (!AppDialog.Confirm(this, "Clear camera and captions?", "All camera cuts and caption positions of this short are removed.", "Clear all", danger: true)) return;
            _editing.Camera.Clear(); _editing.CaptionPositions.Clear();
            await PushKeyframesAsync();
        };
        _timeline.MouseDown += (_, _) => _timelineDragging = true;
        _timeline.MouseUp += async (_, _) => { _timelineDragging = false; await SeekFromTimelineAsync(); };
        _timeline.Scroll += async (_, _) => { if (_timelineDragging) await SeekFromTimelineAsync(); };
        _clipStart.ValueChanged += async (_, _) => await ApplyClipTimesAsync();
        _clipEnd.ValueChanged += async (_, _) => await ApplyClipTimesAsync();
        _renderPreview.Click += async (_, _) => await RenderEditorPreviewAsync();
        _renders.SelectedIndexChanged += (_, _) => UpdateRenders();
        _renderCancel.Click += (_, _) =>
        {
            foreach (var j in SelectedRenders().ToList()) CancelRender(j);
            foreach (var p in SelectedPosts().ToList()) CancelPost(p);
        };
        _renderClear.Click += (_, _) =>
        {
            // a render leaves with its posts, once none of them is still on its way
            foreach (var j in _renderJobs.Where(j => !j.IsOpen && j.Posts.All(p => !p.IsOpen)).ToList())
            {
                _renderJobs.Remove(j);
                _renders.Items.Remove(j.Row);
                foreach (var p in j.Posts) _renders.Items.Remove(p.Row);
            }
            UpdateRenders();
        };
        _publishPanel.JobStatusChanged += OnPublishJobStatus;
        _renders.DoubleClick += (_, _) =>
        {
            if (SelectedRenders().FirstOrDefault() is { State: "done" } j)
                foreach (var g in _generated.Where(g => (g.ShortTitle ?? g.Title) == j.Short.Title && File.Exists(g.Path) && g.When >= j.Started
                                                        && j.Langs.Contains(g.Language == "en" ? "en" : "original"))) OpenPath(g.Path);
        };
        _editSegments.CellEndEdit += (_, e) => OnSegmentEdited(e.RowIndex, e.ColumnIndex);
        _editSegments.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 2) await PlayFromRowAsync(e.RowIndex); };
        // one click on a transcript line jumps the player there (double-click plays from it)
        _editSegments.CellClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _editSegments.Rows[e.RowIndex].Tag is TranscriptSegment seg && _player.IsReady) await _player.SeekAsync(seg.Start);
        };
        _segPlay.Click += async (_, _) => { if (_editSegments.CurrentRow is not null) await PlayFromRowAsync(_editSegments.CurrentRow.Index); };
        _segDelete.Click += (_, _) => DeleteSegmentRow();
        Shown += (_, _) => RefreshLibrary();
        // every modal window (dialogs, file pickers, confirmations) parks the editor's browser player until it closes
        Application.EnterThreadModal += (_, _) => _player.Park(true);
        Application.LeaveThreadModal += (_, _) => _player.Park(false);
        // posts scheduled before the app closed come back (and missed ones are offered)
        Shown += async (_, _) => { try { await _publishPanel.RestoreScheduledAsync(); } catch (Exception ex) { Log("Could not restore scheduled posts: " + ex.Message); } };
        FormClosing += (_, e) =>
        {
            int n = _publishPanel.ScheduledCount;
            if (n == 0 || e.CloseReason != CloseReason.UserClosing) return;
            if (!AppDialog.Confirm(this, "Posts are scheduled", $"{n} scheduled post{(n == 1 ? "" : "s")} only go out while Short Generator is open. They are kept: next time it opens, any that came due are offered to publish.",
                    "Close anyway", "Keep it open", kind: AppDialog.Kind.Warning)) e.Cancel = true;
        };
        _cancel.Click += (_, _) => _cts?.Cancel();

        _openFile.Click += (_, _) => { if (_video is not null) OpenPath(_video.FilePath); };
        _openFolder.Click += (_, _) => { if (_video is not null) OpenPath(Path.GetDirectoryName(_video.FilePath)!); };

        _transcribe.Click += async (_, _) => await TranscribeAsync();
        _loadTranscript.Click += (_, _) => LoadTranscriptFile();
        _saveSrt.Click += (_, _) => SaveTranscript(true);
        _saveTxt.Click += (_, _) => SaveTranscript(false);
        _segments.DoubleClick += (_, _) => PreviewSelectedSegment();

        _gptBuild.Click += (_, _) => BuildChatGptPrompt();
        _gptCopy.Click += (_, _) => { try { Clipboard.SetText(_gptPrompt.Text); _status.Text = "Prompt copied to clipboard. Paste it into ChatGPT."; } catch (Exception ex) { AppDialog.Show(this, ex.Message, "Copy failed"); } };
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
        // one click on the English cell ticks / unticks the English version of that short
        _suggestList.MouseClick += (_, e) =>
        {
            var hit = _suggestList.HitTest(e.Location);
            if (hit.Item?.Tag is not ShortSuggestion s || hit.SubItem is null) return;
            if (hit.Item.SubItems.IndexOf(hit.SubItem) != _suggestList.Columns.Count - 1) return;
            s.TranslateEnglish = !s.TranslateEnglish;
            hit.SubItem.Text = s.TranslateEnglish ? "\u2611" : "\u2610";
            _suggestList.Invalidate(hit.SubItem.Bounds);
            SaveProject();
            UpdateGenerateEnabled();
            if (ReferenceEquals(s, _editing)) UpdateEnglishColumn();
        };
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
        _continueToGenerate.Click += (_, _) => { if (EnsureEnglish(TickedShorts().Where(s => s.TranslateEnglish).ToList(), force: false)) SelectTab(4); };
        _previewLang.SelectedIndexChanged += async (_, _) =>
        {
            bool en = _editing?.TranslateEnglish == true && _previewLang.SelectedIndex == 1;
            if (en == _previewEnglish) return;
            _previewEnglish = en;
            await PushCaptionsAsync();
            if (en && _editing is not null && SegmentsOf(_editing).All(x => string.IsNullOrWhiteSpace(x.English)))
                _status.Text = "No English lines yet for this short: click 'Translate again'.";
        };
        _retranslate.Click += async (_, _) => await RetranslateEditingAsync();
        _captionLang.SelectedIndexChanged += (_, _) =>
        {
            _settings.CaptionLanguage = _captionLang.SelectedIndex switch { 1 => "en", 2 => "both", _ => "original" };
            try { SettingsStore.Save(_settings); } catch { }
            RefreshQueue();
        };
        _backToSuggestions.Click += (_, _) => SelectTab(3);
        _queue.ItemChecked += (_, e) =>
        {
            if (_populatingQueue || e.Item.Tag is not ShortSuggestion s || s.Selected == e.Item.Checked) return;
            s.Selected = e.Item.Checked;
            e.Item.ForeColor = s.Selected ? Theme.Text : Theme.TextMuted;
            e.Item.SubItems[4].Text = s.Selected && e.Item.SubItems[4].Text == "" ? "queued" : (!s.Selected && e.Item.SubItems[4].Text == "queued" ? "" : e.Item.SubItems[4].Text);
            SaveProject();
            // mirror the tick in the Suggestions list without re-entering
            _populatingSuggestions = true;
            foreach (ListViewItem it in _suggestList.Items) if (ReferenceEquals(it.Tag, s)) it.Checked = s.Selected;
            _populatingSuggestions = false;
            UpdateGenerateEnabled();
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
        using var dlg = new SettingsForm(_settings)
        {
            DescribeLocalFiles = () => DescribeLocal(ScanLocalFiles()),
            CleanLocalFiles = owner => CleanLocalFiles(owner),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings = SettingsStore.Load();
            RebuildServices();
            _whisperModel.SelectedIndex = Math.Max(0, Array.IndexOf(Transcriber.ModelNames, _settings.WhisperModel));
            _language.SelectedIndex = LanguageOptions.IndexOfCode(_settings.WhisperLanguage);
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
            AppDialog.Show(this, "Please paste a valid http(s) link.", "Invalid link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_tools.YtDlpPath is null)
        {
            if (!AppDialog.Confirm(this, "Download yt-dlp?", "yt-dlp downloads videos from links. It is not installed yet; it goes into the tools folder.", "Download")) return;
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
                    !AppDialog.Confirm(this, "Transcribe again?", "This video already has a transcript. Transcribing again replaces it.", "Transcribe again", "Keep transcript"))
                {
                    _tabs.SelectedTab = _tabTranscript;
                    return;
                }
                await TranscribeCoreAsync(ct);
            }
        });
    }

    // ------------------------------------------------------------------ deleting

    /// <summary>Deletes the selected downloaded video with its project file, transcript files and generated shorts.</summary>
    private void DeleteLibraryVideo()
    {
        if (_library.SelectedItems.Count == 0 || _library.SelectedItems[0].Tag is not string path) return;
        var name = Path.GetFileNameWithoutExtension(path);
        var generated = TryLoadProject(path, out var project) ? project.Generated?.Where(g => File.Exists(g.Path)).ToList() ?? new() : new();
        var removes = new List<string> { "The video file", "Its transcript and project data" };
        if (generated.Count > 0) removes.Add($"{generated.Count} generated short{(generated.Count == 1 ? "" : "s")} made from it, with covers");
        if (!AppDialog.Confirm(this, "Delete this video?", $"\"{name}\" will be removed from this computer:", "Delete video", danger: true,
                details: removes, notes: new[] { "This cannot be undone." })) return;

        bool current = _video is not null && string.Equals(_video.FilePath, path, StringComparison.OrdinalIgnoreCase);
        if (current) { _ = _player.PauseAsync(); ClearVideo(); }
        int removed = 0;
        foreach (var g in generated) { removed += TryDelete(g.Path) ? 1 : 0; TryDelete(g.CoverPath); }
        // companions saved next to the video: project file, transcripts, covers, yt-dlp metadata
        var dir = Path.GetDirectoryName(path)!;
        foreach (var f in Directory.EnumerateFiles(dir, name + ".*"))
            if (!string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
        TryDelete(path);
        Log($"Deleted \"{name}\" with its project data" + (removed > 0 ? $" and {removed} generated short(s)." : "."));
        RefreshLibrary();
    }

    /// <summary>Forgets the transcript of the current video (and deletes .srt / .vtt / .txt files saved next to it).</summary>
    private void DeleteTranscript()
    {
        if (_video is null || _transcript is null) return;
        var name = Path.GetFileNameWithoutExtension(_video.FilePath);
        var dir = Path.GetDirectoryName(_video.FilePath)!;
        var companions = new[] { ".srt", ".vtt", ".txt" }.Select(ext => Path.Combine(dir, name + ext)).Where(File.Exists).ToList();
        if (!AppDialog.Confirm(this, "Delete the transcript?", "The transcript of this video is forgotten. Suggestions and shorts already made stay as they are.",
                "Delete transcript", danger: true,
                details: companions.Count > 0 ? companions.Select(f => "Also deletes " + Path.GetFileName(f)).ToList() : null)) return;
        foreach (var f in companions) TryDelete(f);
        _transcript = null;
        ShowTranscript();
        SaveProject();
        UpdateNavStates();
        RefreshLibrary();
        Log("Transcript deleted.");
    }

    /// <summary>Deletes the generated short selected in the results list, with its cover, and forgets it in the project.</summary>
    private void DeleteGeneratedShort()
    {
        if (_results.SelectedItems.Count == 0) return;
        var item = _results.SelectedItems[0];
        var path = item.Tag as string;
        var file = _generated.LastOrDefault(g => g.Path == path);
        if (file is null) { TryDelete(path); _results.Items.Remove(item); return; }
        if (DeleteGenerated(file)) _results.Items.Remove(item);
    }

    /// <summary>Asks, then deletes a rendered short with its cover and forgets it in the project. Used by the results list and the Publish page.</summary>
    private bool DeleteGenerated(GeneratedFile file)
    {
        var published = file.Publications.Any(p => p.Status is "published" or "drafted");
        // everything this row produced: the video, the cover exported next to it, and the thumbnail image the short
        // uses in this language (generated or picked), unless another short or language still uses that image
        var suggestion = _suggestions?.Shorts.FirstOrDefault(s => s.Title == (file.ShortTitle ?? file.Title));
        var lang = file.Language == "en" ? "en" : null;
        string? thumbnail = suggestion?.CoverFor(lang).Image;
        if (thumbnail is not null && (!File.Exists(thumbnail) || string.Equals(thumbnail, file.CoverPath, StringComparison.OrdinalIgnoreCase))) thumbnail = null;
        if (thumbnail is not null && _suggestions!.Shorts.Any(s =>
                (!ReferenceEquals(s, suggestion) || lang == "en") && string.Equals(s.CoverImage, thumbnail, StringComparison.OrdinalIgnoreCase)
                || (!ReferenceEquals(s, suggestion) || lang != "en") && string.Equals(s.CoverImageEn, thumbnail, StringComparison.OrdinalIgnoreCase)))
            thumbnail = null; // shared with another short or the other language: keep it
        var files = new List<string> { file.Path };
        if (file.CoverPath is { } cover && File.Exists(cover)) files.Add(cover);
        if (thumbnail is not null) files.Add(thumbnail);
        var details = files.Where(File.Exists).Select(f => $"{Path.GetFileName(f)}\n{Path.GetDirectoryName(f)}").ToList();

        var notes = new List<string>();
        if (published) notes.Add("It was already published: the posts stay online, only the local files and the record are removed.");
        if (thumbnail is not null) notes.Add("The short goes back to a frame of the video as its cover; you can pick or generate a new one in Edit & preview.");
        notes.Add("This cannot be undone.");
        if (!AppDialog.Confirm(this, "Delete this short?", $"\"{file.Title}\" and everything made for it are deleted from this computer:", "Delete short",
                danger: true, details: details, notes: notes)) return false;
        if (_player is not null) _ = _player.PauseAsync(); // a preview may hold the file open
        foreach (var f in files) TryDelete(f);
        if (thumbnail is not null && suggestion is not null)
        {
            if (lang == "en") suggestion.CoverImageEn = null; else suggestion.CoverImage = null;
        }
        _generated.Remove(file);
        foreach (ListViewItem it in _results.Items) if (it.Tag as string == file.Path) { _results.Items.Remove(it); break; }
        SaveProject();
        _publishPanel.RefreshList();
        RefreshQueue();
        UpdateNavStates();
        Log($"Deleted short \"{file.Title}\".");
        return true;
    }

    private bool TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (Exception ex) { Log($"Could not delete {Path.GetFileName(path)}: {ex.Message}"); return false; }
    }

    /// <summary>Back to the empty state, as if no video had been opened.</summary>
    private void ClearVideo()
    {
        _video = null; _transcript = null; _suggestions = null; _editing = null;
        _generated = new List<GeneratedFile>();
        _segments.Items.Clear(); _suggestList.Items.Clear(); _results.Items.Clear(); _editClips.Items.Clear(); _keyframes.Items.Clear(); _editSegments.Rows.Clear();
        _suggestDetail.Text = ""; _summary.Text = ""; _videoInfo.Text = ""; _gptPrompt.Text = "";
        _emptyVideo.Visible = _emptyTranscript.Visible = _emptySuggest.Visible = true;
        _settings.LastVideoPath = null;
        try { SettingsStore.Save(_settings); } catch { }
        ShowTranscript();
        ShowSuggestions();
        _publishPanel.RefreshList();
        UpdateNavStates();
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

        _emptyVideo.Visible = false;
        _emptyTranscript.Visible = true;
        _emptySuggest.Visible = true;
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
                int cleaned = Transcriber.CleanSpeakerMarks(_transcript); // "-" speaker marks and wrapping quotes from older runs
                ShowTranscript();
                Log(removed > 0 ? $"Restored saved transcript and removed {removed} repeated segments." : "Restored saved transcript.");
                if (removed > 0 || cleaned > 0) SaveProject();
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
            AppDialog.Show(this,
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
        var language = LanguageOptions.CodeAt(_language.SelectedIndex);

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
        _saveSrt.Enabled = _saveTxt.Enabled = _deleteTranscript.Enabled = _transcript is { Segments.Count: > 0 };
        _emptyTranscript.Visible = _transcript is not { Segments.Count: > 0 };
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
            Transcriber.CleanSpeakerMarks(t);
            _transcript = t;
            ShowTranscript();
            SaveProject();
            RefreshLibrary();
            Log($"Loaded transcript from {d.FileName}: {t.Segments.Count} lines.");
            _tabs.SelectedTab = _tabChatGpt;
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, ex.Message, "Could not load transcript", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    // ---- which networks the AI writes post text for (YouTube title/description/keywords, TikTok and Instagram captions) ----
    private readonly List<(CheckBox Box, string Key)> _targetBoxes = new();
    private bool _syncingTargets;

    /// <summary>Three synced checkboxes (YouTube / TikTok / Instagram). Several bars can host their own copy.</summary>
    private FlowLayoutPanel PostTargetBoxes()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        // the label travels with its checkboxes, so a wrapping toolbar never separates them
        panel.Controls.Add(new Label { Text = "Post text for:", AutoSize = true, Margin = new Padding(18, 7, 4, 0) });
        foreach (var (key, label, checkedNow) in new[]
        {
            ("youtube", "YouTube", _settings.PostTextYouTube),
            ("tiktok", "TikTok", _settings.PostTextTikTok),
            ("instagram", "Instagram", _settings.PostTextInstagram),
        })
        {
            var box = new CheckBox { Text = label, AutoSize = true, Checked = checkedNow, Margin = new Padding(0, 8, 10, 0) };
            var k = key;
            box.CheckedChanged += (_, _) =>
            {
                if (_syncingTargets) return;
                _syncingTargets = true;
                foreach (var (other, otherKey) in _targetBoxes) if (otherKey == k && !ReferenceEquals(other, box)) other.Checked = box.Checked;
                _syncingTargets = false;
                switch (k)
                {
                    case "youtube": _settings.PostTextYouTube = box.Checked; break;
                    case "tiktok": _settings.PostTextTikTok = box.Checked; break;
                    default: _settings.PostTextInstagram = box.Checked; break;
                }
                try { SettingsStore.Save(_settings); } catch { }
            };
            _targetBoxes.Add((box, key));
            panel.Controls.Add(box);
        }
        return panel;
    }

    private PostTargets CurrentTargets => new(_settings.PostTextYouTube, _settings.PostTextTikTok, _settings.PostTextInstagram);

    private void BuildChatGptPrompt()
    {
        if (_video is null || _transcript is not { Segments.Count: > 0 })
        {
            AppDialog.Show(this, "Transcribe the video first. The prompt includes the timestamped transcript.", "No transcript", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _tabs.SelectedTab = _tabTranscript;
            return;
        }
        int max = (int)Math.Max(_gptMax.Value, _gptMin.Value + 5);
        int count = _gptAuto.Checked ? 0 : (int)_gptCount.Value;
        _gptPrompt.Text = ChatGptExchange.BuildPrompt(_video, _transcript, count, (int)_gptMin.Value, max, CurrentTargets);
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
        if (_video is null) { AppDialog.Show(this, "Load a video first.", "No video"); return; }
        SuggestionResponse parsed;
        try
        {
            parsed = ChatGptExchange.ParseResponse(_gptAnswer.Text);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, "Could not read the answer: " + ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        RefreshEditorClipList();
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
                var item = new ListViewItem(new[] { s.Title, $"{s.Duration:F0}s", s.ViralityScore > 0 ? $"{s.ViralityScore}/10" : "-" }) { Tag = s };
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
        _previewEnglish = s.TranslateEnglish && _previewLang.SelectedIndex == 1;
        UpdateEnglishColumn();
        s.MigrateLegacyCaptionPosition();
        _playerTime = s.StartSeconds;
        if (_cameraMode.Checked) _cameraMode.Checked = false;
        if (_player.IsReady)
        {
            await _player.LoadAsync(_video.FilePath, s.StartSeconds, s.EndSeconds);
            await PushCaptionsAsync();
        }
        await PushKeyframesAsync();
        if (_player.IsReady) await _player.SetLookAsync(VisualLook.All[Math.Max(0, _look.SelectedIndex)].Css);
        RefreshLayerList(null);
        await PushLayersAsync();
        UpdateTimeLabel(s.StartSeconds);
        _ = UpdateCoverPreviewAsync();
        // Auto camera is the default: a short that was never framed gets face framing as soon as it opens.
        if (s.Camera.Count == 0 && _autoCameraOpt.Checked && _crop.SelectedIndex == 0 && FaceFramer.IsSupported && _cts is null)
            await AutoCameraAsync(silent: true);
    }

    /// <summary>Renders the cover frame for the current short into a temp file and shows it in the editor.</summary>
    private async Task UpdateCoverPreviewAsync()
    {
        if (_video is null || _editing is null) { _coverPreview.Image = null; _coverInfo.Text = ""; return; }
        var s = _editing;
        var lang = CoverLanguage;
        var (time, image) = s.CoverFor(lang);
        _coverInfo.Text = (lang == "en" && !s.HasEnglishCover ? "Same as original: " : "") +
                          (image is not null ? "Custom image" : time is { } t ? $"Frame at {Fmt(t)}" : "Default: 1 s into the clip");
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "shortgen_covers", $"{Guid.NewGuid():N}.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            var opts = ReadOptions();
            opts.CaptionLanguage = lang ?? "original";
            var path = await _renderer.ExportCoverAsync(_video, s, opts, tmp, CancellationToken.None);
            if (path is null || !ReferenceEquals(_editing, s) || CoverLanguage != lang) return; // switched short or language meanwhile
            var img = Theme.ReadImage(path); // a copy: the file is deleted next, and the stream must not outlive it
            var old = _coverPreview.Image;
            _coverPreview.Image = img;
            old?.Dispose();
            try { File.Delete(path); } catch { }
        }
        catch (Exception ex) { Log("Cover preview failed: " + ex.Message); }
    }

    /// <summary>Dev convenience: shows the Higgsfield cover dialog for the short open in the editor without calling the API.</summary>
    public void ShowHiggsfieldDialogPreview()
    {
        var s = _editing ?? _suggestions?.Shorts.FirstOrDefault();
        if (s is null) return;
        using var dlg = new HiggsfieldCoverDialog(s, ThumbnailLanguage, _coverPreview.Image is { } img ? (Image)img.Clone() : null, "xai/grok-imagine-image-2.0");
        dlg.ShowDialog(this);
    }

    /// <summary>Language for the thumbnail headline: the transcript language when chosen, otherwise a neutral phrase.</summary>
    /// <summary>The headline language of the cover being made: English for the English cover.</summary>
    private string CoverBriefLanguage => CoverLanguage == "en" ? "English" : ThumbnailLanguage;

    /// <summary>For the English cover, the brief uses the English title and the first English line as the hook.</summary>
    private ShortSuggestion CoverBriefShort(ShortSuggestion s)
    {
        if (CoverLanguage != "en") return s;
        var firstEn = SegmentsOf(s).Select(x => x.English).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new ShortSuggestion
        {
            Title = s.English is { Title.Length: > 0 } e ? e.Title : s.Title,
            Hook = firstEn ?? s.Hook, Emotion = s.Emotion, WhyViral = s.WhyViral,
            SuggestedCaption = s.English?.Caption ?? s.SuggestedCaption, StartSeconds = s.StartSeconds, EndSeconds = s.EndSeconds,
        };
    }

    /// <summary>The transcript's language by name ("Portuguese"), for prompts; falls back to the Whisper choice.</summary>
    private string SourceLanguageName
    {
        get
        {
            var code = _transcript?.Language;
            var known = LanguageOptions.All.FirstOrDefault(o => o.Code != "auto" && string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase));
            if (known is not null) return known.Name;
            if (!string.IsNullOrWhiteSpace(code) && code != "auto")
                try { return new System.Globalization.CultureInfo(code).EnglishName; } catch { }
            return ThumbnailLanguage;
        }
    }

    private string ThumbnailLanguage => _language.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(_language.Text) ? _language.Text : "the language spoken in the video";

    /// <summary>Developer-only: asks what the thumbnail should be, then has Higgsfield redraw the cover (with or without the frame as reference).</summary>
    private async Task GenerateCoverWithHiggsfieldAsync()
    {
        if (_video is null || _editing is null) return;
        using var client = HiggsfieldClient.FromEnvironment();
        if (client is null)
        {
            AppDialog.Show(this, "Set HF_API_KEY (and HF_API_SECRET if your key has one) in the environment, then restart the app.", "Higgsfield");
            return;
        }
        var s = _editing;
        string prompt; bool useReference;
        var coverLang = CoverLanguage;
        using (var dlg = new HiggsfieldCoverDialog(CoverBriefShort(s), CoverBriefLanguage, _coverPreview.Image is { } img ? (Image)img.Clone() : null, client.Model))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            prompt = dlg.Prompt; useReference = dlg.UseReference;
        }
        await RunBusyAsync("Higgsfield cover", async ct =>
        {
            string? reference = null, frame = null;
            if (useReference)
            {
                var tmp = Path.Combine(Path.GetTempPath(), "shortgen_covers", $"{Guid.NewGuid():N}.mp4");
                Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                var fo = ReadOptions(); fo.CaptionLanguage = coverLang ?? "original";
                frame = await _renderer.ExportCoverAsync(_video, s, fo, tmp, ct) ?? throw new InvalidOperationException("The cover frame could not be exported.");
                _status.Text = "Uploading the frame to Higgsfield...";
                reference = await client.UploadAsync(frame, ct);
            }
            _status.Text = $"Higgsfield ({client.Model}) is drawing the cover...";
            var url = await client.GenerateImageAsync(prompt, reference, new Progress<string>(m => { Log(m); _status.Text = m; }), ct);
            var outPath = Path.Combine(Path.GetDirectoryName(_video.FilePath)!, "covers", SafeFolder(s.Title) + (coverLang == "en" ? " (EN)" : "") + ".higgsfield.jpg");
            await client.DownloadAsync(url, outPath, ct);
            if (frame is not null) { try { File.Delete(frame); } catch { } }
            s.SetCover(coverLang, null, outPath);
            SaveProject();
            Log($"Higgsfield cover saved to {outPath}");
            if (ReferenceEquals(_editing, s)) await UpdateCoverPreviewAsync();
        });
    }

    /// <summary>Current position relative to the clip start, rounded to 0.1 s.</summary>
    private double RelativeTime => _editing is null ? 0 : Math.Round(Math.Max(0, _playerTime - _editing.StartSeconds), 1);

    /// <summary>
    /// Clip-relative time of an edit made at this absolute video time. Floored (not rounded) to 0.01 s so the new
    /// keyframe starts at or before the frame on screen: a rounded-up time left the previous cut active until the
    /// playhead caught up, which looked like the change only applied "on the next frame".
    /// </summary>
    private double EditTime(double absolute) => _editing is null ? 0 : Math.Max(0, Math.Floor((absolute - _editing.StartSeconds) * 100) / 100);

    /// <summary>How long a manual camera change holds before the automatic framing takes over again.</summary>
    private const double ManualCameraHold = 4.0;

    private void OnCaptionMoved(double x, double y, double t)
    {
        if (_editing is null) return;
        Keyframes.Upsert(_editing.CaptionPositions, new CaptionKeyframe { Time = EditTime(t), X = x, Y = y });
        _ = PushKeyframesAsync();
    }

    private void OnCameraMoved(double x, double y, double zoom, double t)
    {
        if (_editing is null) return;
        var s = _editing;
        double at = EditTime(t);
        // the automatic framing that was in charge here, to hand back to after the manual change
        var auto = s.Camera.Where(k => k.Source is "auto" or "resume" && k.Time <= at + 0.001).OrderBy(k => k.Time).LastOrDefault();
        // an earlier "resume" inside this hold window belonged to a previous drag and would cut back too early
        s.Camera.RemoveAll(k => k.Source == "resume" && k.Time > at && k.Time <= at + ManualCameraHold + 0.01);
        Keyframes.Upsert(s.Camera, new CameraKeyframe { Time = at, X = x, Y = y, Zoom = zoom, Source = "manual" });
        double back = Math.Round(at + ManualCameraHold, 2);
        bool nextCutSoon = s.Camera.Any(k => k.Time > at + 0.001 && k.Time <= back);
        if (auto is not null && !nextCutSoon && back < s.Duration - 1)
            s.Camera.Add(new CameraKeyframe { Time = back, X = auto.X, Y = auto.Y, Zoom = auto.Zoom, Source = "resume" });
        s.Camera.Sort((a, b) => a.Time.CompareTo(b.Time));
        _ = PushKeyframesAsync();
    }

    /// <summary>The transcript line shown at this absolute video time (or the last one that started before it).</summary>
    private TranscriptSegment? SegmentAt(double t)
    {
        if (_editing is null) return null;
        var segs = SegmentsOf(_editing);
        return segs.FirstOrDefault(x => t >= x.Start && t < x.End) ?? segs.LastOrDefault(x => x.Start <= t);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.V) && _tabs.SelectedTab == _tabEditor && _editing is not null
            && ActiveControl is not TextBoxBase && !_editSegments.IsCurrentCellInEditMode && ClipboardHasImage())
        {
            _ = PasteLayerAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ image layers

    private ImageOverlay? SelectedLayer => _layers.SelectedItems.Count == 1 ? _layers.SelectedItems[0].Tag as ImageOverlay : null;

    private void RefreshLayerList(string? selectId)
    {
        selectId ??= SelectedLayer?.Id;
        _refreshingLayers = true; // re-selecting the row here must not seek or replay the entrance
        _layers.BeginUpdate();
        _layers.Items.Clear();
        if (_editing is not null)
            foreach (var o in _editing.Overlays.OrderBy(o => o.Start))
            {
                var item = new ListViewItem(new[] { $"{o.Start:F1}s", $"{o.End:F1}s",
                    o.IsVideo ? $"🎬 {Path.GetFileNameWithoutExtension(o.Path)} from {Fmt(o.SourceStart)}" : Path.GetFileName(o.Path),
                    o.IsVideo ? (o.FullFrame ? "Full screen" : "Inset " + (ImageOverlay.InsetShapes.FirstOrDefault(x => Math.Abs(x.Aspect - o.Aspect) < 0.01).Name ?? "")) : ImageOverlay.StyleName(o.Style) }) { Tag = o };
                if (!File.Exists(o.Path)) item.ForeColor = Theme.Danger;
                if (o.Id == selectId) item.Selected = true;
                _layers.Items.Add(item);
            }
        _layers.EndUpdate();
        _refreshingLayers = false;
        ShowLayerProps(SelectedLayer);
        RefreshKeyframeList(); // image marks on the scrub bar
    }

    private TableLayoutPanel? _layerPropsPanel;

    private void ShowLayerProps(ImageOverlay? o)
    {
        _loadingLayer = true;
        if (_layerPropsPanel is not null) _layerPropsPanel.Visible = o is not null;
        foreach (Control c in new Control[] { _layerFrom, _layerTo, _layerSize, _layerRot, _layerStyle, _layerAnim, _removeLayer }) c.Enabled = o is not null;
        if (o is { IsVideo: true })
        {
            _layerRot.Enabled = _layerStyle.Enabled = _layerAnim.Enabled = false;
            _layerSize.Enabled = !o.FullFrame;
        }
        if (o is not null)
        {
            _layerFrom.Value = (decimal)Math.Round(Math.Max(0, o.Start), 1);
            _layerTo.Value = (decimal)Math.Round(Math.Max(0, o.End), 1);
            _layerSize.Value = (decimal)Math.Clamp(Math.Round(o.Size), 8, 100);
            _layerRot.Value = (decimal)Math.Clamp(Math.Round(o.Rotation), -180, 180);
            _layerStyle.SelectedIndex = Math.Max(0, Array.FindIndex(ImageOverlay.Styles, x => x.Id == o.Style));
            _layerAnim.SelectedIndex = Math.Max(0, Array.FindIndex(ImageOverlay.Animations, x => x.Id == o.Animation));
        }
        _loadingLayer = false;
    }

    private async Task PushLayersAsync()
    {
        if (_editing is null || !_player.IsReady) return;
        await _player.SetLayersAsync(_editing.Overlays, SelectedLayer?.Id);
    }

    /// <summary>The playhead inside the clip, unrounded (the frame on screen).</summary>
    private double CurrentClipTime() => _editing is null ? 0 : Math.Clamp(_playerTime - _editing.StartSeconds, 0, _editing.Duration);

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    private static List<string> DroppedImages(IDataObject? data) =>
        data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
            : new List<string>();

    private static bool ClipboardHasImage()
    {
        try { return Clipboard.ContainsImage() || (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().Any(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))); }
        catch { return false; }
    }

    private async Task PickAndAddLayerAsync(double start)
    {
        if (_editing is null) return;
        await _player.PauseAsync();
        using var d = new OpenFileDialog { Title = "Choose an image to show on the short", Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|All files|*.*", Multiselect = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await AddLayersAsync(d.FileNames, start);
    }

    /// <summary>
    /// Adds images starting at <paramref name="start"/> (clip time), 5 s each; several images play one after the
    /// other. Near the end of the clip the window slides back so the image still gets its full 5 s.
    /// </summary>
    private async Task AddLayersAsync(IReadOnlyList<string> paths, double start)
    {
        if (_editing is null || paths.Count == 0) return;
        var s = _editing;
        await _player.PauseAsync();
        ImageOverlay? first = null;
        double at = start;
        foreach (var path in paths)
        {
            double len = Math.Min(DefaultLayerSeconds, s.Duration);
            double a = Math.Min(at, Math.Max(0, s.Duration - len));
            var o = new ImageOverlay { Path = path, Start = Math.Round(a, 2), End = Math.Round(Math.Min(s.Duration, a + len), 2) };
            s.Overlays.Add(o);
            first ??= o;
            at = o.End;
        }
        SaveProject();
        RefreshLayerList(first!.Id);
        await PushLayersAsync();
        await ShowLayerEntranceAsync(first);
        _status.Text = paths.Count == 1
            ? $"Image shown {Fmt(first.Start)} to {Fmt(first.End)}. Drag it on the video to place it; change From / To to adjust the timing."
            : $"{paths.Count} images added, 5 s each, from {Fmt(first.Start)}.";
    }


    /// <summary>
    /// Picks another video (the downloads folder first), then the moment and its framing, and shows it over the short
    /// from the playhead for as long as the chosen clip. The short keeps its own sound and captions.
    /// </summary>
    private async Task AddVideoLayerAsync(double start)
    {
        if (_editing is null || _video is null) return;
        await _player.PauseAsync();
        using var pick = new OpenFileDialog
        {
            Title = "Choose the video with the moment to show",
            Filter = "Videos|*.mp4;*.mov;*.mkv;*.webm;*.m4v;*.avi|All files|*.*",
            InitialDirectory = Directory.Exists(_settings.DownloadFolder) ? _settings.DownloadFolder : "",
        };
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        var s = _editing;
        double room = Math.Max(0.5, s.Duration - Math.Min(start, s.Duration - 0.5));
        // new clips start as a small vertical window near the top of the short; full screen is one click away
        using var dlg = new VideoClipDialog(_ffmpeg, pick.FileName, 0, Math.Min(5, room), false, 50, 50, 1, maxLength: room,
            aspect: 9.0 / 16, shortFrame: ShortFrameProvider(s, Math.Min(start, Math.Max(0, s.Duration - 0.5))), x: 50, y: 22, size: 32);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double a = Math.Min(start, Math.Max(0, s.Duration - dlg.Length));
        var o = new ImageOverlay
        {
            Kind = "video", Path = pick.FileName, SourceStart = Math.Round(dlg.SourceStart, 2),
            Start = Math.Round(a, 2), End = Math.Round(Math.Min(s.Duration, a + dlg.Length), 2),
            FullFrame = dlg.FullFrame, CropX = dlg.CropX, CropY = dlg.CropY, Zoom = dlg.Zoom, Aspect = dlg.Aspect,
            X = dlg.X, Y = dlg.Y, Size = dlg.WindowWidth, Style = "card", Animation = "none",
        };
        s.Overlays.Add(o);
        SaveProject();
        RefreshLayerList(o.Id);
        await PushLayersAsync();
        await _player.SeekAsync(s.StartSeconds + o.Start + 0.05);
        _status.Text = $"Clip shown {Fmt(o.Start)} to {Fmt(o.End)}. " + (o.FullFrame ? "Drag it on the video to frame it, scroll to zoom." : "Drag it to place it, scroll to resize.") + " Double-click its row to pick another moment.";
    }

    /// <summary>A frame of the short at <paramref name="t"/>, framed like the render, for the clip dialog's "On the short" view.</summary>
    private Func<CancellationToken, Task<Bitmap?>>? ShortFrameProvider(ShortSuggestion s, double t)
    {
        if (_video is null) return null;
        var video = _video;
        var opts = ReadOptions();
        return async ct =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"shortgen_frame_{Guid.NewGuid():N}.jpg");
            try
            {
                await _renderer.ExportShortFrameAsync(video, s, opts, t, tmp, 540, ct);
                return File.Exists(tmp) ? Theme.ReadImage(tmp) : null;
            }
            catch (Exception ex) { AppLog.Error("short frame", ex); return null; }
            finally { try { File.Delete(tmp); } catch { } }
        };
    }

    /// <summary>Opens the clip dialog again on an existing clip: another moment, length or framing.</summary>
    private async Task EditVideoLayerAsync(ImageOverlay o)
    {
        if (_editing is null || !File.Exists(o.Path)) return;
        await _player.PauseAsync();
        var s = _editing;
        double room = Math.Max(0.5, s.Duration - o.Start);
        using var dlg = new VideoClipDialog(_ffmpeg, o.Path, o.SourceStart, o.End - o.Start, o.FullFrame, o.CropX, o.CropY, o.Zoom, maxLength: room,
            aspect: o.Aspect, shortFrame: ShortFrameProvider(s, o.Start), x: o.X, y: o.Y, size: o.Size);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        o.SourceStart = Math.Round(dlg.SourceStart, 2);
        o.End = Math.Round(Math.Min(s.Duration, o.Start + dlg.Length), 2);
        o.FullFrame = dlg.FullFrame; o.CropX = dlg.CropX; o.CropY = dlg.CropY; o.Zoom = dlg.Zoom; o.Aspect = dlg.Aspect;
        o.X = dlg.X; o.Y = dlg.Y; o.Size = dlg.WindowWidth;
        SaveProject();
        RefreshLayerList(o.Id);
        await PushLayersAsync();
        await _player.SeekAsync(s.StartSeconds + o.Start + 0.05);
    }

    /// <summary>Pastes an image copied from a browser or an app (or a copied image file) at the playhead.</summary>
    private async Task PasteLayerAsync()
    {
        if (_editing is null || _video is null) return;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
                if (files.Count > 0) { await AddLayersAsync(files, CurrentClipTime()); return; }
            }
            if (!Clipboard.ContainsImage()) { _status.Text = "The clipboard has no image. Copy a picture (right-click > Copy image in a browser) and paste again."; return; }
            using var img = Clipboard.GetImage();
            if (img is null) return;
            // pasted pictures live next to the video so the project keeps working after a restart
            var dir = Path.Combine(Path.GetDirectoryName(_video.FilePath)!, "images");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{SafeFolder(_editing.Title)} {DateTime.Now:yyyyMMdd-HHmmss}.png");
            img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            await AddLayersAsync(new[] { path }, CurrentClipTime());
        }
        catch (Exception ex) { AppDialog.Show(this, ex.Message, "Paste image"); }
    }

    private async Task RemoveLayerAsync()
    {
        if (_editing is null || SelectedLayer is not { } o) return;
        _editing.Overlays.Remove(o);
        SaveProject();
        RefreshLayerList(null);
        await PushLayersAsync();
    }

    /// <summary>True while the selection is being mirrored from a click on the video (the page already knows it).</summary>
    private bool _layerSelectFromVideo, _refreshingLayers;

    private async Task OnLayerRowSelectedAsync()
    {
        ShowLayerProps(SelectedLayer);
        if (_layerSelectFromVideo || _refreshingLayers) return; // pushing now would rebuild the layer under the user's pointer
        await PushLayersAsync();
        if (_editing is not null && SelectedLayer is { } o) await ShowLayerEntranceAsync(o);
    }

    /// <summary>Jumps to the layer's start and plays its entrance once, so its timing and effect can be judged.</summary>
    private async Task ShowLayerEntranceAsync(ImageOverlay o)
    {
        if (_editing is null || !_player.IsReady) return;
        await _player.PauseAsync();
        await _player.SeekAsync(_editing.StartSeconds + o.Start + 0.05);
        await _player.PreviewLayerAsync(o.Id);
    }

    private async Task OnLayerPropsChangedAsync()
    {
        if (_loadingLayer || _editing is null || SelectedLayer is not { } o) return;
        o.Start = (double)_layerFrom.Value;
        o.End = Math.Max(o.Start + 0.5, (double)_layerTo.Value);
        o.Size = (double)_layerSize.Value;
        if (o.IsVideo)
        {
            // a clip keeps its own look; only its window and (inset) size change here
            SaveProject();
            RefreshLayerList(o.Id);
            await PushLayersAsync();
            return;
        }
        o.Rotation = (double)_layerRot.Value;
        o.Style = ImageOverlay.Styles[Math.Max(0, _layerStyle.SelectedIndex)].Id;
        o.Animation = ImageOverlay.Animations[Math.Max(0, _layerAnim.SelectedIndex)].Id;
        SaveProject();
        RefreshLayerList(o.Id);
        await PushLayersAsync();
    }

    private void SelectLayerRow(string id)
    {
        _layerSelectFromVideo = true;
        try { foreach (ListViewItem it in _layers.Items) it.Selected = it.Tag is ImageOverlay o && o.Id == id; }
        finally { _layerSelectFromVideo = false; }
    }

    private void OnLayerMovedOnVideo(string id, double x, double y, double size, double rot)
    {
        var o = _editing?.Overlays.FirstOrDefault(l => l.Id == id);
        if (o is null) return;
        o.X = x; o.Y = y; o.Size = size; o.Rotation = rot;
        SaveProject();
        RefreshLayerList(o.Id);
        _ = PushLayersAsync();
    }

    /// <summary>Right-click on the caption: select its line in the transcript table and open the Text cell for typing.</summary>
    private void EditLineInGrid(double t)
    {
        var seg = SegmentAt(t);
        if (seg is null) return;
        foreach (DataGridViewRow row in _editSegments.Rows)
        {
            if (!ReferenceEquals(row.Tag, seg)) continue;
            _editSegments.ClearSelection();
            row.Selected = true;
            _editSegments.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2);
            _editSegments.CurrentCell = row.Cells[EnglishMode ? "English" : "Text"];
            _editSegments.Focus();
            _editSegments.BeginEdit(false); // caret at the end, nothing selected, so typing does not wipe the line
            break;
        }
    }

    /// <summary>The caption was edited on the video: update that transcript line everywhere.</summary>
    private void OnCaptionTextEdited(double t, string text)
    {
        var seg = SegmentAt(t);
        if (seg is null || string.IsNullOrWhiteSpace(text)) return;
        if (EnglishMode)
        {
            if (text == seg.English) return;
            seg.English = text; seg.EnglishFrom = seg.Text;
            foreach (DataGridViewRow row in _editSegments.Rows)
                if (ReferenceEquals(row.Tag, seg)) { row.Cells["English"].Value = text; MarkEnglishCell(row); }
            SaveProject();
            _ = PushCaptionsAsync();
            Log("English caption line updated from the video.");
            return;
        }
        if (text == seg.Text) return;
        seg.Text = text;
        foreach (DataGridViewRow row in _editSegments.Rows)
            if (ReferenceEquals(row.Tag, seg)) row.Cells["Text"].Value = text;
        ShowTranscript();
        SaveProject();
        _ = PushCaptionsAsync();
        Log("Caption line updated from the video.");
    }

    /// <summary>Unticks the short open in the editor; it goes back to the Suggestions list, not to the bin.</summary>
    private void RemoveEditingFromSelection()
    {
        if (_editClips.SelectedItems.Count == 0 || _editClips.SelectedItems[0].Tag is not ShortSuggestion s) return;
        int index = _editClips.SelectedIndices[0];
        _ = _player.PauseAsync();
        s.Selected = false;
        _populatingSuggestions = true;
        foreach (ListViewItem it in _suggestList.Items) if (ReferenceEquals(it.Tag, s)) it.Checked = false;
        _populatingSuggestions = false;
        SaveProject();
        UpdateGenerateEnabled();
        if (ReferenceEquals(_editing, s)) _editing = null;
        RefreshEditorClipList();
        if (_editClips.Items.Count > 0)
        {
            foreach (ListViewItem it in _editClips.SelectedItems) it.Selected = false;
            _editClips.Items[Math.Min(index, _editClips.Items.Count - 1)].Selected = true;
        }
        _status.Text = $"\"{s.Title}\" removed from the selected shorts. Tick it again in Suggestions to bring it back.";
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
                .OrderBy(r => r.Item1).ThenBy(r => r.Item2);
            foreach (var r in rows)
            {
                var item = new ListViewItem(new[] { Fmt(r.Item1), r.Item2, r.Item3 }) { Tag = r.Item4 };
                item.ForeColor = r.Item2 == "Camera" ? Theme.PurpleDeep : Theme.TextSecondary;
                _keyframes.Items.Add(item);
            }
        }
        _keyframes.EndUpdate();
        int cams = _editing?.Camera.Count ?? 0, caps = _editing?.CaptionPositions.Count ?? 0;
        _keyframeHint.Text = _editing is null ? "" : $"{cams} camera cut{(cams == 1 ? "" : "s")}, {caps} caption pos.";
        // edits show on the scrub bar: camera cuts in violet, caption positions in blue
        if (_editing is not null && _editing.Duration > 0)
            _timeline.SetMarks(_editing.Camera.Where(k => k.Time > 0.05).Select(k => new TimelineBar.Mark(k.Time / _editing.Duration, Theme.Nebula, $"Camera cut at {Fmt(k.Time)} ({k.Source})"))
                .Concat(_editing.CaptionPositions.Select(k => new TimelineBar.Mark(k.Time / _editing.Duration, Theme.CosmicBlue, $"Caption moved at {Fmt(k.Time)}")))
                .Concat(_editing.Overlays.Select(o => new TimelineBar.Mark(Math.Clamp(o.Start / _editing.Duration, 0, 1), Theme.Success, $"Image {Path.GetFileName(o.Path)} {Fmt(o.Start)} - {Fmt(o.End)}"))));
        else _timeline.SetMarks(Array.Empty<TimelineBar.Mark>());
    }

    private async Task DeleteKeyframeAsync()
    {
        if (_editing is null || _keyframes.SelectedItems.Count == 0 || _keyframes.SelectedItems[0].Tag is not IKeyframe k) return;
        if (k is CameraKeyframe ck) _editing.Camera.Remove(ck);
        else if (k is CaptionKeyframe pk) _editing.CaptionPositions.Remove(pk);
        await PushKeyframesAsync();
    }

    /// <summary>Runs face detection on the current short and replaces its camera cuts.</summary>
    private async Task AutoCameraAsync(bool silent = false)
    {
        if (_video is null || _editing is null) return;
        if (!FaceFramer.IsSupported)
        {
            if (!silent) AppDialog.Show(this, "Face detection is not available on this Windows build. Use Camera mode to place the camera manually.", "Auto camera");
            return;
        }
        var s = _editing;
        if (!silent && s.Camera.Any(k => k.Source is "manual" or "alt") &&
            !AppDialog.Confirm(this, "Replace your camera cuts?", "This short has camera cuts you changed. Automatic face framing replaces them.", "Use auto camera", "Keep mine")) return;

        await RunBusyAsync("Detecting faces", async ct =>
        {
            var analysis = await new FaceFramer(_ffmpeg).AnalyzeAsync(_video, s, ProgressReporter(), new Progress<string>(Log), ct);
            _faces[s] = analysis;
            s.Camera = analysis.Cuts.Select(k => new CameraKeyframe { Time = k.Time, X = k.X, Y = k.Y, Zoom = k.Zoom, Source = k.Source }).ToList();
            if (ReferenceEquals(_editing, s)) await PushKeyframesAsync(); else SaveProject();
        });
    }

    /// <summary>
    /// Moves the camera to the next best place to look (another person, both, the wide shot), cycling on each
    /// click. With Camera mode off the whole cut under the playhead changes; with Camera mode on a new cut starts
    /// at the playhead, so only the scene from here to the next cut changes.
    /// </summary>
    private async Task ChangeCameraAsync()
    {
        if (_video is null || _editing is null) return;
        var s = _editing;
        if (!_faces.TryGetValue(s, out var analysis))
        {
            if (!FaceFramer.IsSupported) { AppDialog.Show(this, "Face detection is not available on this Windows build. Use Camera mode to drag the camera instead.", "Change camera"); return; }
            await RunBusyAsync("Detecting faces", async ct =>
            {
                _faces[s] = await new FaceFramer(_ffmpeg).AnalyzeAsync(_video, s, ProgressReporter(), new Progress<string>(Log), ct);
            });
            if (!_faces.TryGetValue(s, out analysis)) return;
        }

        double rel = RelativeTime;
        var cuts = s.Camera.OrderBy(k => k.Time).ToList();
        var current = cuts.LastOrDefault(k => k.Time <= rel + 0.001);
        if (current is null)
        {
            current = new CameraKeyframe { Time = 0, X = 50, Y = 50, Zoom = 1, Source = "auto" };
            s.Camera.Add(current);
            cuts = s.Camera.OrderBy(k => k.Time).ToList();
        }
        // Camera mode on: the change starts here and leaves everything before the playhead untouched, so a new
        // cut is inserted at the current time. Camera mode off: the whole cut under the playhead changes.
        if (_cameraMode.Checked && rel > current.Time + 0.3)
        {
            var split = new CameraKeyframe { Time = Math.Round(rel, 2), X = current.X, Y = current.Y, Zoom = current.Zoom, Source = current.Source };
            s.Camera.Add(split);
            current = split;
            cuts = s.Camera.OrderBy(k => k.Time).ToList();
        }
        var next = cuts.FirstOrDefault(k => k.Time > current.Time);
        double from = current.Time, to = next?.Time ?? s.Duration;

        var options = FaceFramer.Alternatives(analysis, from, to);
        if (options.Count == 0) return;
        // where are we now in that list? then step to the next one
        int at = options.FindIndex(o => Math.Abs(o.Key.X - current.X) < 6 && Math.Abs(o.Key.Zoom - current.Zoom) < 0.15);
        var pick = options[(at + 1) % options.Count];
        current.X = pick.Key.X; current.Y = pick.Key.Y; current.Zoom = pick.Key.Zoom; current.Source = "alt";
        await PushKeyframesAsync();
        await _player.SeekAsync(s.StartSeconds + from);
        _status.Text = $"Camera {Fmt(from)} - {Fmt(to)}: now on {pick.Label} ({(at + 1) % options.Count + 1} of {options.Count}). Click again for the next option.";
    }

    private void FillSegmentGrid(ShortSuggestion s)
    {
        _editSegments.Rows.Clear();
        foreach (var seg in SegmentsOf(s))
        {
            int row = _editSegments.Rows.Add(Fmt(seg.Start), Fmt(seg.End), seg.Text, seg.English ?? "");
            _editSegments.Rows[row].Tag = seg;
            MarkEnglishCell(_editSegments.Rows[row]);
        }
    }

    private async Task PushCaptionsAsync()
    {
        if (_editing is null || !_player.IsReady) return;
        var opts = ReadOptions();
        var slice = _transcript?.Slice(_editing.StartSeconds, _editing.EndSeconds, english: EnglishMode) ?? new List<TranscriptSegment>();
        await _player.SetCaptionsAsync(slice, _editing.StartSeconds, CaptionStyle.Get(opts.CaptionStyleId), opts.FontSize, opts.WordsPerCaption,
            opts.CropMode, opts.AddCaptions, opts.IncludeReactions);
    }

    private void OnPlayerTime(double t)
    {
        if (_editing is null) return;
        if (InvokeRequired) { BeginInvoke(() => OnPlayerTime(t)); return; }
        _playerTime = t;
        UpdateTimeLabel(t);
        var label = $"Image at {Fmt(CurrentClipTime())}";
        if (_addLayerHere.Text != label) _addLayerHere.Text = label;
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
                var color = active ? Theme.SelectionBg : Theme.Elevated;
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

    private void OnSegmentEdited(int rowIndex, int columnIndex = -1)
    {
        if (rowIndex < 0 || _editSegments.Rows[rowIndex].Tag is not TranscriptSegment seg) return;
        var row = _editSegments.Rows[rowIndex];
        if (columnIndex >= 0 && _editSegments.Columns[columnIndex].Name == "English")
        {
            var en = (row.Cells["English"].Value?.ToString() ?? "").Trim();
            if (en == (seg.English ?? "")) return;
            seg.English = en.Length > 0 ? en : null;
            seg.EnglishFrom = seg.Text; // a hand-written English line matches the current original
            MarkEnglishCell(row);
            SaveProject();
            _ = PushCaptionsAsync();
            Log("English line updated.");
            return;
        }
        var newText = (row.Cells["Text"].Value?.ToString() ?? "").Trim();
        if (newText == seg.Text) return;
        seg.Text = newText;
        MarkEnglishCell(row);  // the English line may now be out of date
        ShowTranscript();      // keep the Transcript tab in sync
        SaveProject();
        _ = PushCaptionsAsync();
        Log("Transcript line updated.");
    }

    /// <summary>English cells whose original changed after translating are tinted, with a tip to translate again.</summary>
    private void MarkEnglishCell(DataGridViewRow row)
    {
        if (row.Tag is not TranscriptSegment seg) return;
        var cell = row.Cells["English"];
        cell.Style.BackColor = seg.EnglishOutdated ? ColorTranslator.FromHtml("#FFF4E0") : Color.Empty;
        cell.ToolTipText = seg.EnglishOutdated ? "The original line changed after it was translated. Use 'Translate again' or edit this line." :
                           string.IsNullOrWhiteSpace(seg.English) ? "No English yet: the original line is used." : "";
    }

    private bool EnglishMode => _previewEnglish;

    /// <summary>The English column and "Translate again" belong to shorts ticked for English in Suggestions.</summary>
    private void UpdateEnglishColumn()
    {
        bool on = _editing?.TranslateEnglish == true;
        _editSegments.Columns["English"]!.Visible = on;
        _retranslate.Visible = on;
        _previewLangGroup.Visible = on;
        _coverLang.Visible = on;
        if (!on && _coverLang.SelectedIndex != 0) _coverLang.SelectedIndex = 0;
        if (!on && _previewLang.SelectedIndex != 0) _previewLang.SelectedIndex = 0; // also resets _previewEnglish
        if (!on) _previewEnglish = false;
    }

    /// <summary>Translates the lines of the short in the editor that are missing or out of date (all lines when everything is current).</summary>
    /// <summary>
    /// Translates every line of the transcript table as it is now (with your edits), and fills every English row
    /// with the answer. Uncommitted typing in the table is committed first so it is part of what gets sent.
    /// </summary>
    private async Task RetranslateEditingAsync()
    {
        if (_editing is null || _transcript is null) return;
        if (_editSegments.IsCurrentCellInEditMode) _editSegments.EndEdit();
        if (EnsureEnglish(new[] { _editing }, force: true)) { FillSegmentGrid(_editing); await PushCaptionsAsync(); }
    }

    /// <summary>Dev convenience: shows the translation dialog for the ticked shorts without applying anything.</summary>
    /// <summary>Dev convenience ("--settings"): the real Settings dialog, wired like the side nav opens it.</summary>
    public void OpenSettingsPreview() => OpenSettings();

    public void ShowTranslateDialogPreview()
    {
        if (_transcript is null) return;
        var shorts = TickedShorts().Where(s => s.TranslateEnglish).ToList();
        var job = CaptionTranslator.Plan(shorts, _transcript, SourceLanguageName, forceLines: true);
        using var dlg = new TranslateDialog(job, shorts.Count, null);
        dlg.ShowDialog(this);
    }

    private List<ShortSuggestion> TickedShorts() => _suggestions?.Shorts.Where(s => s.Selected).ToList() ?? new List<ShortSuggestion>();

    /// <summary>
    /// Makes sure the given shorts have English captions and post text, translating only what is missing or out of
    /// date. Returns false only when the user cancels (Skip continues without English).
    /// </summary>
    private bool EnsureEnglish(IReadOnlyList<ShortSuggestion> shorts, bool force)
    {
        if (_transcript is null || shorts.Count == 0) return true;
        // Nothing missing or out of date: no dialog. Otherwise send every line of these shorts as they are now
        // (with the user's edits), so the answer fills the whole English column row by row.
        if (!force && CaptionTranslator.Plan(shorts, _transcript, SourceLanguageName).IsEmpty) return true;
        var job = CaptionTranslator.Plan(shorts, _transcript, SourceLanguageName, forceLines: true);
        if (job.IsEmpty) return true;
        var key = SettingsStore.GetApiKey(_settings);
        Func<CancellationToken, Task<string>>? claude = key is null ? null : ct => CaptionTranslator.TranslateWithClaudeAsync(key, _settings.ClaudeModel, job, ct);
        using var dlg = new TranslateDialog(job, shorts.Count, claude);
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.OK)
        {
            SaveProject();
            var (lines, posts, missing) = dlg.Applied;
            Log($"English: {lines} caption line(s) and {posts} post text(s) translated" + (missing > 0 ? $"; {missing} line(s) missing in the answer keep the original text." : "."));
            if (_editing is not null) FillSegmentGrid(_editing);
        }
        else Log("English skipped for now; those shorts render with the original captions until translated.");
        return true;
    }

    private void DeleteSegmentRow()
    {
        if (_transcript is null || _editSegments.CurrentRow?.Tag is not TranscriptSegment seg) return;
        if (!AppDialog.Confirm(this, "Delete this line?", "It is removed from the transcript and will not appear in captions.", "Delete line", danger: true)) return;
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
        await _player.PauseAsync();
        // which versions to render and, optionally, where each one is published once it is ready
        await _publishPanel.EnsureAccountsAsync();
        int missing = s.TranslateEnglish ? SegmentsOf(s).Count(x => string.IsNullOrWhiteSpace(x.English)) : 0;
        using var dlg = new RenderPublishDialog(s.Title, s.TranslateEnglish, missing, _publishPanel.Accounts, _publishPanel.TikTokHow, _settings.AutoPublish);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Languages.Count == 0) return;
        var langs = dlg.Languages;
        if (_publishPanel.Accounts is not null)
        {
            // remembered per version for next time (only the versions rendered now change)
            foreach (var (lang, dests) in dlg.Publish) _settings.AutoPublish[lang] = dests.Select(d => d.Key).ToList();
            SettingsStore.Save(_settings);
        }
        var publish = dlg.Publish.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        var publishAt = new Dictionary<string, DateTime?>(dlg.PublishAt);
        // Render preview makes the real files: saved in the shorts folder with their covers and post texts and
        // listed on Publish, one per language. It goes into the render queue, so another short can be queued
        // while this one renders; the videos open as each render finishes.
        EnqueueRender(s, langs, publish, publishAt);
    }

    // ------------------------------------------------------------------ render queue

    /// <summary>One short to render in the given caption languages. It renders the short as it is when its turn comes.</summary>
    private sealed class RenderJob
    {
        public required ShortSuggestion Short { get; init; }
        public required List<string> Langs { get; set; }
        /// <summary>Per version: the accounts it is published to once rendered ("publish when ready").</summary>
        public Dictionary<string, List<AutoDestination>> Publish { get; set; } = new();
        /// <summary>Per version: when its posts go out (null = as soon as it is rendered).</summary>
        public Dictionary<string, DateTime?> PublishAt { get; set; } = new();
        /// <summary>One row per post this render leads to, right under it.</summary>
        public List<RenderPost> Posts { get; } = new();
        public ListViewItem Row { get; set; } = null!;
        /// <summary>queued | running | done | failed | cancelled</summary>
        public string State { get; set; } = "queued";
        public DateTime Started { get; set; }
        public bool IsOpen => State is "queued" or "running";
    }

    /// <summary>A post a render leads to: waits for the render, then mirrors its row in the Publishing queue.</summary>
    private sealed class RenderPost
    {
        public required RenderJob Job { get; init; }
        public required string Lang { get; init; }
        public required AutoDestination Dest { get; init; }
        public ListViewItem Row { get; set; } = null!;
        /// <summary>The Publish queue's key once handed over (file|network|account).</summary>
        public string? Key { get; set; }
        /// <summary>waiting | scheduled | queued | running | done | failed | cancelled</summary>
        public string State { get; set; } = "waiting";
        public bool IsOpen => State is "waiting" or "scheduled" or "queued" or "running";
    }

    private static string NetworkName(string n) => n switch { "instagram" => "Instagram", "tiktok" => "TikTok", _ => "YouTube" };

    private void SetPostRow(RenderPost p, string status, Color color)
    {
        if (p.Row.ListView is null) return;
        p.Row.SubItems[2].Text = status;
        p.Row.ForeColor = color;
    }

    /// <summary>The post rows of a render, inserted right under it (rebuilt when the waiting render's plan changes).</summary>
    private void BuildPostRows(RenderJob job)
    {
        foreach (var old in job.Posts) _renders.Items.Remove(old.Row);
        job.Posts.Clear();
        int at = _renders.Items.IndexOf(job.Row) + 1;
        foreach (var lang in job.Langs)
            foreach (var d in job.Publish.GetValueOrDefault(lang) ?? new())
            {
                var when = job.PublishAt.GetValueOrDefault(lang);
                var post = new RenderPost { Job = job, Lang = lang, Dest = d };
                post.Row = new ListViewItem(new[] { "      ↳ post", $"{NetworkName(d.Network)} · {d.Label}", when is { } w ? $"After the render, scheduled for {w:ddd HH:mm}" : "After the render" })
                    { Tag = post, ForeColor = Theme.TextMuted };
                _renders.Items.Insert(at++, post.Row);
                job.Posts.Add(post);
            }
    }

    /// <summary>Publish queue rows report here, so the post rows under a render follow them.</summary>
    private void OnPublishJobStatus(string key, string status, Color color, string state)
    {
        foreach (var p in _renderJobs.SelectMany(j => j.Posts).Where(p => p.Key == key))
        {
            p.State = state switch { "done" => "done", "failed" => "failed", "cancelled" => "cancelled", "running" => "running", "scheduled" => "scheduled", _ => "queued" };
            SetPostRow(p, status, color);
        }
        UpdateRenders();
    }

    private readonly List<RenderJob> _renderJobs = new();
    private RenderJob? _currentRender;
    /// <summary>How the last RunBusyAsync ended: done | cancelled | failed.</summary>
    private string _lastBusyOutcome = "";
    /// <summary>The message of the last failed busy task, shown in the render row.</summary>
    private string _lastBusyError = "";

    private static string LangsText(IEnumerable<string> langs) => string.Join(" + ", langs.Select(l => l == "en" ? "English" : "Original"));

    /// <summary>"Original → @a · English → @b, TikTok" for the Captions column.</summary>
    private static string PlanText(RenderJob j) => string.Join("  ·  ", j.Langs.Select(l =>
        (l == "en" ? "English" : "Original") + (j.Publish.TryGetValue(l, out var d) && d.Count > 0
            ? " → " + string.Join(", ", d.Select(x => x.Network == "tiktok" ? "TikTok" : x.Label))
              + (j.PublishAt.TryGetValue(l, out var at) && at is { } t ? $" at {t:ddd HH:mm}" : "")
            : "")));

    /// <summary>Queues one row per caption version: the original and the English render each show their own progress.</summary>
    private void EnqueueRender(ShortSuggestion s, List<string> langs, Dictionary<string, List<AutoDestination>>? publish = null,
        Dictionary<string, DateTime?>? publishAt = null)
    {
        publish ??= new();
        publishAt ??= new();
        foreach (var lang in langs)
        {
            var dests = publish.TryGetValue(lang, out var d) ? d : new List<AutoDestination>();
            // this version of this short still waiting: update where it goes, never queue it twice
            if (_renderJobs.FirstOrDefault(j => j.State == "queued" && ReferenceEquals(j.Short, s) && j.Langs.SequenceEqual(new[] { lang })) is { } waiting)
            {
                waiting.Publish = new() { [lang] = dests };
                waiting.PublishAt = new() { [lang] = publishAt.GetValueOrDefault(lang) };
                BuildPostRows(waiting);
                Log($"\"{s.Title}\" ({LangsText(new[] { lang })}) is already waiting in the render queue.");
                continue;
            }
            var job = new RenderJob { Short = s, Langs = new List<string> { lang }, Publish = new() { [lang] = dests }, PublishAt = new() { [lang] = publishAt.GetValueOrDefault(lang) } };
            job.Row = new ListViewItem(new[] { s.Title, LangsText(job.Langs) + " video", "Waiting" }) { Tag = job, ForeColor = Theme.TextMuted };
            _renderJobs.Add(job);
            _renders.Items.Add(job.Row);
            BuildPostRows(job);
        }
        Log($"Render queued: \"{s.Title}\" ({LangsText(langs)}).");
        PumpRenders();
        UpdateRenders();
    }

    private void SetRenderRow(RenderJob j, string status, Color color)
    {
        if (j.Row.ListView is null) return;
        j.Row.SubItems[2].Text = status;
        j.Row.ForeColor = color;
    }

    /// <summary>Starts the next waiting render when nothing else runs (a transcription, a download or another render).</summary>
    private void PumpRenders()
    {
        if (_currentRender is not null || IsDisposed) return;
        var next = _renderJobs.FirstOrDefault(j => j.State == "queued");
        if (next is null) return;
        if (_cts is not null)
        {
            foreach (var j in _renderJobs.Where(j => j.State == "queued")) SetRenderRow(j, $"Waiting for {(_busyTitle.Length > 0 ? _busyTitle.ToLowerInvariant() : "the current task")}", Theme.TextMuted);
            return; // RunBusyAsync pumps again when it ends
        }
        _currentRender = next;
        next.State = "running";
        next.Started = DateTime.Now;
        SetRenderRow(next, "Starting...", Theme.Purple);
        _ = RunRenderAsync(next);
    }

    private async Task RunRenderAsync(RenderJob j)
    {
        UpdateRenders();
        try
        {
            if (_video is null || _suggestions is null || !_suggestions.Shorts.Contains(j.Short))
            {
                j.State = "failed";
                SetRenderRow(j, "Failed: the short is no longer in this project", Theme.Danger);
                return;
            }
            _lastBusyOutcome = ""; // GenerateAsync can stop before it starts (no transcript, say)
            _lastBusyError = "";
            await GenerateAsync(new[] { j.Short }, j.Langs, openVideos: false, fromQueue: true);
            var made = _generated.Count(g => (g.ShortTitle ?? g.Title) == j.Short.Title && g.When >= j.Started && File.Exists(g.Path)
                                             && j.Langs.Contains(g.Language == "en" ? "en" : "original"));
            if (_lastBusyOutcome == "cancelled") { j.State = "cancelled"; SetRenderRow(j, "Cancelled", Theme.Warning); PostsNotSent(j, "Not posted: the render was cancelled"); }
            else if (made > 0 && _lastBusyOutcome == "done")
            {
                j.State = "done";
                // "publish when ready": each new file goes to the accounts chosen for its version
                int posts = 0;
                foreach (var (lang, dests) in j.Publish)
                {
                    var file = _generated.LastOrDefault(g => (g.ShortTitle ?? g.Title) == j.Short.Title && g.When >= j.Started && File.Exists(g.Path)
                                                             && (g.Language == "en") == (lang == "en"));
                    // only the posts still wanted (a post row cancelled while rendering is left out)
                    var wanted = j.Posts.Where(p => p.Lang == lang && p.State == "waiting").ToList();
                    if (file is null) { foreach (var p in wanted) { p.State = "failed"; SetPostRow(p, "Not posted: the video was not found", Theme.Danger); } continue; }
                    var keys = _publishPanel.QueueAutomatic(file, wanted.Select(p => p.Dest).ToList(), j.PublishAt.GetValueOrDefault(lang));
                    foreach (var p in wanted)
                    {
                        var k = keys.FirstOrDefault(x => x.Dest == p.Dest);
                        if (k.Key is null) { p.State = "failed"; SetPostRow(p, "Not posted: already in the queue or not connected", Theme.Warning); continue; }
                        p.Key = k.Key;
                        p.State = k.Scheduled ? "scheduled" : "queued";
                        SetPostRow(p, k.Scheduled ? $"Scheduled for {j.PublishAt.GetValueOrDefault(lang):ddd HH:mm}" : "In the publishing queue", Theme.CosmicBlue);
                        posts++;
                    }
                }
                var when = j.Langs.Select(l => j.PublishAt.GetValueOrDefault(l)).FirstOrDefault(t => t is not null && t > DateTime.Now);
                SetRenderRow(j, "Done" + (posts > 0 ? $", {posts} post{(posts == 1 ? "" : "s")} " + (when is { } w ? $"scheduled for {w:ddd HH:mm}" : "publishing") + " (see Publish)" : ", listed on Publish") + ". Double-click to play.", Theme.Success);
            }
            else { j.State = "failed"; SetRenderRow(j, "Failed: " + (_lastBusyError.Length > 0 ? _lastBusyError : "see Activity for the details"), Theme.Danger); PostsNotSent(j, "Not posted: the render failed"); }
        }
        catch (Exception ex)
        {
            j.State = "failed";
            SetRenderRow(j, "Failed: " + ex.Message, Theme.Danger);
        }
        finally
        {
            _currentRender = null;
            UpdateRenders();
            PumpRenders();
        }
    }

    private void PostsNotSent(RenderJob j, string why)
    {
        foreach (var p in j.Posts.Where(p => p.State == "waiting")) { p.State = "cancelled"; SetPostRow(p, why, Theme.TextMuted); }
    }

    /// <summary>A post row: before the render it is dropped from the plan; after, its upload is cancelled on Publish.</summary>
    private void CancelPost(RenderPost p)
    {
        if (p.State == "waiting") { p.State = "cancelled"; SetPostRow(p, "Cancelled: it will not be posted", Theme.TextMuted); }
        else if (p.IsOpen && p.Key is not null) _publishPanel.CancelByKey(p.Key);
        UpdateRenders();
    }

    private void CancelRender(RenderJob j)
    {
        if (j.State == "queued") { j.State = "cancelled"; SetRenderRow(j, "Cancelled before it started", Theme.TextMuted); PostsNotSent(j, "Not posted: the render was cancelled"); }
        else if (j.State == "running" && ReferenceEquals(j, _currentRender)) { SetRenderRow(j, "Cancelling...", Theme.Warning); _cts?.Cancel(); }
        UpdateRenders();
    }

    private IEnumerable<RenderJob> SelectedRenders() => _renders.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<RenderJob>();
    private IEnumerable<RenderPost> SelectedPosts() => _renders.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<RenderPost>();

    private void UpdateRenders()
    {
        int running = _renderJobs.Count(j => j.State == "running"), waiting = _renderJobs.Count(j => j.State == "queued");
        int done = _renderJobs.Count(j => j.State == "done"), failed = _renderJobs.Count(j => j.State is "failed" or "cancelled");
        var parts = new List<string>();
        if (running > 0) parts.Add($"{running} rendering");
        if (waiting > 0) parts.Add($"{waiting} waiting");
        if (done > 0) parts.Add($"{done} done");
        if (failed > 0) parts.Add($"{failed} failed or cancelled");
        var posts = _renderJobs.SelectMany(j => j.Posts).ToList();
        int postsOpen = posts.Count(p => p.IsOpen), postsDone = posts.Count(p => p.State == "done");
        if (postsOpen > 0) parts.Add($"{postsOpen} post{(postsOpen == 1 ? "" : "s")} to go");
        if (postsDone > 0) parts.Add($"{postsDone} posted");
        _rendersCounts.Text = string.Join("  ·  ", parts);
        _rendersCounts.ForeColor = running + waiting + postsOpen > 0 ? Theme.Purple : failed > 0 ? Theme.Danger : Theme.TextMuted;
        _rendersEmpty.Visible = _renderJobs.Count == 0;
        _renderCancel.Enabled = SelectedRenders().Any(j => j.IsOpen) || SelectedPosts().Any(p => p.IsOpen);
        _renderClear.Enabled = _renderJobs.Any(j => !j.IsOpen && j.Posts.All(p => !p.IsOpen));
        // the button says what it will do: render now, or join the queue behind a running render
        _renderPreview.Text = running + waiting > 0 || _cts is not null ? "Add to render queue" : "Render preview";
    }

    /// <summary>Dev convenience: shows the Render preview language question for the short in the editor.</summary>
    public async void ShowPreviewLanguagesPreview()
    {
        var s = _editing ?? _suggestions?.Shorts.FirstOrDefault();
        if (s is null) return;
        await _publishPanel.EnsureAccountsAsync();
        using var dlg = new RenderPublishDialog(s.Title, s.TranslateEnglish, 0, _publishPanel.Accounts, _publishPanel.TikTokHow, _settings.AutoPublish);
        dlg.ShowDialog(this); // shown only, nothing is rendered
    }

    // ------------------------------------------------------------------ step 4: suggestions

    private async Task AnalyzeAsync()
    {
        if (_video is null || _transcript is null) return;
        var apiKey = SettingsStore.GetApiKey(_settings);
        if (apiKey is null)
        {
            AppDialog.Show(this, "Add your Anthropic API key in Settings first.", "API key required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenSettings();
            return;
        }

        int count = _autoCount.Checked ? 0 : (int)_count.Value;
        int min = (int)_minSec.Value, max = (int)Math.Max(_maxSec.Value, _minSec.Value + 5);
        await RunBusyAsync("Analyzing with Claude", async ct =>
        {
            var suggester = new ShortSuggester(apiKey, _settings.ClaudeModel);
            var custom = _suggestions?.Shorts.Where(s => s.Emotion == "custom").ToList() ?? new List<ShortSuggestion>();
            _suggestions = await suggester.SuggestAsync(_video, _transcript, count, min, max, CurrentTargets, new Progress<string>(Log), ct);
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
                    s.ViralityScore > 0 ? $"{s.ViralityScore}/10" : "-", s.Emotion, s.TranslateEnglish ? "\u2611" : "\u2610" })
                { Tag = s, Checked = s.Selected };
                _suggestList.Items.Add(item);
            }
            _summary.Text = string.IsNullOrWhiteSpace(_suggestions.VideoSummary) ? "" : "Video summary: " + _suggestions.VideoSummary;
        }
        _suggestList.EndUpdate();
        _populatingSuggestions = false;
        _emptySuggest.Visible = _suggestList.Items.Count == 0;
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
        void Net(string name, NetworkPost? p, bool hashtags)
        {
            if (p is null) return;
            H(name);
            var sbNet = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(p.Title)) sbNet.AppendLine("Title: " + p.Title);
            if (!string.IsNullOrWhiteSpace(p.Description)) sbNet.AppendLine(p.Description.Trim());
            if (p.Tags.Count > 0) sbNet.AppendLine((hashtags ? "" : "Keywords: ") + string.Join(hashtags ? " " : ", ", p.Tags.Select(t => hashtags ? "#" + t.TrimStart('#') : t)));
            P(sbNet.ToString().TrimEnd());
        }
        Net("YouTube", s.Youtube, false);
        Net("TikTok", s.Tiktok, true);
        Net("Instagram", s.Instagram, true);
        if (s.CoverImage is not null || s.CoverTime is not null)
        {
            H("Cover");
            P(s.CoverImage is not null ? $"Custom image: {Path.GetFileName(s.CoverImage)}" : $"Frame at {Fmt(s.CoverTime!.Value)} into the clip");
            if (s.HasEnglishCover) P("English: " + (s.CoverImageEn is not null ? $"custom image {Path.GetFileName(s.CoverImageEn)}" : $"frame at {Fmt(s.CoverTimeEn!.Value)}"));
        }
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
        Look = VisualLook.All[Math.Max(0, _look.SelectedIndex)].Id,
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
        int ticked = _suggestions?.Shorts.Count(s => s.Selected) ?? 0, total = _suggestions?.Shorts.Count ?? 0;
        bool can = _video is not null && ticked > 0 && _cts is null;
        _generate.Enabled = can;
        _generate.Text = ticked > 0 ? $"Generate {ticked} short{(ticked == 1 ? "" : "s")}" : "Generate shorts";
        _continueToGenerate.Enabled = can;
        _continueToGenerate.Text = ticked > 0 ? $"Continue with {ticked} short{(ticked == 1 ? "" : "s")}" : "Continue with selected shorts";
        _selectionSummary.Text = total == 0 ? "" : ticked == 0 ? "Tick the moments you want as shorts." : $"{ticked} of {total} moments ticked. Framing and captions come next.";
        _queueTitle.Text = ticked == 0 ? "Shorts to generate" : $"Shorts to generate ({ticked})";
        UpdateNavStates();
    }

    private bool _populatingQueue;

    /// <summary>The Generate step lists the shorts ticked in Suggestions, with what already rendered.</summary>
    private void RefreshQueue()
    {
        _populatingQueue = true;
        _queue.BeginUpdate();
        _queue.Items.Clear();
        if (_suggestions is not null)
        {
            int i = 1;
            foreach (var s in _suggestions.Shorts)
            {
                var file = _generated.LastOrDefault(g => (g.ShortTitle ?? g.Title) == s.Title && g.Language is null && File.Exists(g.Path));
                string status = file is null ? (s.Selected ? "queued" : "") : file.Sent ? "published" : "rendered";
                if (s.Selected && _settings.CaptionLanguage != "original" && s.TranslateEnglish && _transcript is not null)
                    status += SegmentsOf(s).Any(x => x.EnglishIsStale) || s.English is null ? " · EN to do" : " · EN ready";
                var item = new ListViewItem(new[] { "", (i++).ToString(), s.Title, $"{Fmt(s.StartSeconds)} - {Fmt(s.EndSeconds)}  ({s.Duration:F0}s)", status })
                { Tag = s, Checked = s.Selected };
                if (!s.Selected) item.ForeColor = Theme.TextMuted;
                _queue.Items.Add(item);
            }
        }
        _queue.EndUpdate();
        _populatingQueue = false;
    }

    /// <param name="only">Render just these shorts; null = all ticked shorts.</param>
    /// <param name="languages">Render exactly these caption languages ("original" / "en") instead of the Generate setting.</param>
    /// <param name="openVideos">Open the rendered videos afterwards (Render preview) instead of their folder.</param>
    /// <param name="fromQueue">A render from the render queue: nothing opens afterwards (no player, no Explorer) and a
    /// failure is reported in its row and the Activity log instead of an error box, which could land on top of a
    /// dialog the user has open.</param>
    private async Task GenerateAsync(IReadOnlyList<ShortSuggestion>? only = null, IReadOnlyList<string>? languages = null, bool openVideos = false, bool fromQueue = false)
    {
        if (_video is null || _suggestions is null) return;
        var selected = only?.ToList() ?? _suggestions.Shorts.Where(s => s.Selected).ToList();
        if (selected.Count == 0) return;
        var options = ReadOptions();
        if (options.AddCaptions && _transcript is null)
        {
            AppDialog.Show(this, "Captions need a transcript. Transcribe first or untick 'Burn captions'.", "No transcript");
            return;
        }

        var sub = Path.Combine(options.OutputFolder, SafeFolder(_video.Title));
        options.OutputFolder = sub;
        _results.Items.Clear();

        await RunBusyAsync("Generating shorts", quiet: fromQueue, work: async ct =>
        {
            int i = 1, ok = 0;
            var rendered = new List<string>();
            var mode = _settings.CaptionLanguage;
            foreach (var s in selected)
            {
                ct.ThrowIfCancellationRequested();
                // the languages this short is rendered in: English only when it was ticked for English
                var langs = new List<string>();
                if (languages is not null) langs.AddRange(languages.Where(l => l == "original" || (l == "en" && s.TranslateEnglish)));
                else
                {
                    if (mode != "en" || !s.TranslateEnglish) langs.Add("original");
                    if (mode != "original" && s.TranslateEnglish) langs.Add("en");
                    if (mode == "en" && !s.TranslateEnglish) Log($"\"{s.Title}\" is not ticked for English; rendering the original captions.");
                }
                // the file number is the short's place among the ticked shorts, so one short keeps its name
                int fileIndex = only is null ? i : Math.Max(1, _suggestions.Shorts.Where(x => x.Selected).ToList().IndexOf(s) + 1);
                foreach (var lang in langs)
                {
                bool en = lang == "en";
                var opt = ReadOptions();
                opt.OutputFolder = sub;
                opt.CaptionLanguage = lang;
                opt.FileSuffix = en ? " (EN)" : "";
                if (en && _transcript is not null)
                {
                    int missing = SegmentsOf(s).Count(x => string.IsNullOrWhiteSpace(x.English));
                    if (missing > 0) Log($"\"{s.Title}\" (EN): {missing} line(s) have no English and keep the original text.");
                }
                _busyTitle = $"Generating {i}/{selected.Count}: {s.Title}" + (en ? " (EN)" : "");
                _status.Text = _busyTitle + "...";
                var item = _results.Items.Add(new ListViewItem(new[] { s.Title + (en ? " (EN)" : ""), "rendering...", "" }));
                // an earlier render of this short in this language that was never published is replaced, so the
                // folder holds one file per short and language and the cover on disk is always the current one
                foreach (var old in _generated.Where(g => (g.ShortTitle ?? g.Title) == s.Title && g.Language == (en ? "en" : null) && !g.Sent).ToList())
                {
                    TryDelete(old.Path);
                    TryDelete(old.CoverPath);
                    _generated.Remove(old);
                }
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
                var r = await _renderer.RenderAsync(_video, _transcript, s, opt, fileIndex, ProgressReporter(), new Progress<string>(Log), ct);
                if (r.Success) rendered.Add(r.OutputPath);
                item.SubItems[1].Text = r.Success ? "done" : "failed";
                item.SubItems[2].Text = r.Success ? r.OutputPath : r.Error ?? "";
                item.Tag = r.OutputPath;
                if (r.Success)
                {
                    ok++;
                    var ep = en ? s.English : null;
                    _generated.Add(new GeneratedFile
                    {
                        Title = ep is { Title.Length: > 0 } ? ep.Title : s.Title, ShortTitle = s.Title, Language = en ? "en" : null,
                        Path = r.OutputPath, When = DateTime.Now,
                        // Carried along so the Publish step has the post text without finding the suggestion again;
                        // the English file carries the English texts.
                        Caption = WithCredit(ep?.Caption ?? s.SuggestedCaption, en), Hashtags = (ep?.Hashtags ?? s.Hashtags)?.ToList(),
                        Youtube = WithCredit(ep is null ? s.Youtube : ep.Youtube, en), Tiktok = WithCredit(ep is null ? s.Tiktok : ep.Tiktok, en),
                        Instagram = WithCredit(ep is null ? s.Instagram : ep.Instagram, en),
                        // Cover / thumbnail exported next to the video so publishing can send it along.
                        CoverPath = await _renderer.ExportCoverAsync(_video, s, opt, r.OutputPath, ct),
                        CoverTimeSeconds = ShortRenderer.CoverFrameTime(s, opt.CaptionLanguage),
                    });
                }
                }
                i++;
            }
            SaveProject();
            _publishPanel.RefreshList();
            RefreshQueue();
            Log($"Finished: {ok}/{selected.Count} shorts generated in {sub}" + (ok > 0 ? ". Open the Publish step to send them to your accounts." : ""));
            if (!fromQueue)
            {
                if (ok > 0 && !openVideos) OpenPath(sub);
                if (openVideos) foreach (var path in rendered) OpenPath(path);
            }
        });
    }

    /// <summary>Widens every button with an icon picture so its icon and label fit.</summary>
    private static void FitPictureButtons(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is FancyButton { Picture: not null } b && b.Dock != DockStyle.Fill)
                b.Width = Math.Max(b.Width, TextRenderer.MeasureText(b.Text, b.Font).Width + 24 + 8 + 30);
            FitPictureButtons(c);
        }
    }

    // ------------------------------------------------------------------ credit to the source

    /// <summary>
    /// "🎥 Credit: Diario AS (@DiarioAS)": the channel the clip comes from, added to every rendered short's post text so
    /// the source is always credited. "Créditos" for a Portuguese original. Null when the source has no known channel.
    /// </summary>
    private string? CreditLine(bool english)
    {
        if (_video is null) return null;
        var who = (_video.Channel ?? _video.Uploader)?.Trim();
        if (string.IsNullOrWhiteSpace(who)) return null;
        bool portuguese = !english && (_transcript?.Language?.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ?? false);
        var handle = _video.Handle is { } h && !who.Replace(" ", "").Contains(h.TrimStart('@'), StringComparison.OrdinalIgnoreCase) ? $" ({h})" : "";
        return $"🎥 {(portuguese ? "Créditos" : "Credit")}: {who}{handle}";
    }

    /// <summary>The text with the credit line under it, unless it already names the channel.</summary>
    private string WithCredit(string? text, bool english)
    {
        var credit = CreditLine(english);
        text ??= "";
        if (credit is null || (_video is { } v && text.Contains((v.Channel ?? v.Uploader).Trim(), StringComparison.OrdinalIgnoreCase))) return text;
        return text.Length == 0 ? credit : text.TrimEnd() + "\n\n" + credit;
    }

    private NetworkPost? WithCredit(NetworkPost? post, bool english) => post is null ? null
        : new NetworkPost { Title = post.Title, Description = WithCredit(post.Description, english), Tags = post.Tags.ToList() };

    // ------------------------------------------------------------------ helpers

    /// <param name="quiet">No error box on failure (the caller reports it, like the render queue's row).</param>
    private async Task RunBusyAsync(string title, Func<CancellationToken, Task> work, bool quiet = false)
    {
        if (_cts is not null) { AppDialog.Show(this, "Another operation is running. Cancel it first.", "Busy"); return; }
        _cts = new CancellationTokenSource();
        SetBusy(true, title);
        try
        {
            await work(_cts.Token);
            _status.Text = title + " - done";
            _lastBusyOutcome = "done";
        }
        catch (OperationCanceledException)
        {
            Log($"{title} cancelled.");
            _status.Text = title + " - cancelled";
            _lastBusyOutcome = "cancelled";
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            _status.Text = title + " - failed";
            _lastBusyOutcome = "failed";
            AppLog.Error(title, ex);
            _lastBusyError = ex.Message;
            if (!quiet) AppDialog.Show(this, ex.Message, title + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false, "");
            // a render waiting behind this task starts now (after the current call stack unwinds)
            if (_currentRender is null && _renderJobs.Any(j => j.State == "queued") && IsHandleCreated) BeginInvoke(PumpRenders);
            if (IsHandleCreated) BeginInvoke(UpdateRenders);
        }
    }

    private void SetBusy(bool busy, string title)
    {
        _cancel.Enabled = busy;
        _download.Enabled = _downloadTranscribe.Enabled = _openLocal.Enabled = !busy;
        _libraryRefresh.Enabled = _libraryOpenFolder.Enabled = !busy;
        _libraryDelete.Enabled = !busy && _library.SelectedItems.Count > 0;
        _deleteShort.Enabled = !busy && _results.SelectedItems.Count > 0;
        _libraryLoad.Enabled = _libraryTranscribe.Enabled = !busy && _library.SelectedItems.Count > 0;
        _transcribe.Enabled = !busy; // with no video loaded it offers to download + transcribe the link
        _loadTranscript.Enabled = !busy && _video is not null;
        _analyze.Enabled = !busy && _transcript is { Segments.Count: > 0 };
        _addClip.Enabled = !busy && _video is not null;
        _previewClip.Enabled = _editClip.Enabled = _removeClip.Enabled = !busy && _suggestList.SelectedItems.Count > 0;
        UpdateGenerateEnabled();
        // No wait cursor: long jobs run in the background and the rest of the app stays usable
        // (browse tabs, read the log, tweak caption options). The progress bar and status show activity.
        _busyTitle = busy ? title : "";
        _progress.Value = 0;
        if (busy) _status.Text = title + "...";
    }

    /// <summary>The current operation's label, so the progress reporter can show "Transcribing - 70%" next to the bar.</summary>
    private string _busyTitle = "";

    private IProgress<double> ProgressReporter() => new Progress<double>(p =>
    {
        int pct = (int)Math.Clamp(p * 100, 0, 100);
        _progress.Value = pct;
        _status.Text = _busyTitle.Length == 0 ? $"{pct}%" : $"{_busyTitle} - {pct}%";
        if (_currentRender is { State: "running" } r)
            SetRenderRow(r, $"Rendering {pct}%", Theme.Purple);
    });

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        AppLog.Write(message);
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

    /// <summary>
    /// Opens a file or folder with its default app. Never blocks with a dialog: if Windows cannot start the app
    /// ("Server execution failed" is common when the Media Player / Films &amp; TV app is slow to launch) it retries
    /// once, then shows the file selected in Explorer so it can be opened from there.
    /// </summary>
    private static void OpenPath(string path)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return; }
            catch (Exception) when (attempt == 0) { Thread.Sleep(800); }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                }
                catch { }
            }
        }
    }

    private void PlayRange(double start)
    {
        if (_video is null) return;
        // Most players ignore a start offset via the shell, so just open the file.
        OpenPath(_video.FilePath);
        Log($"Opened video. Segment starts at {Fmt(start)}.");
    }

    // ---- disk space: rendered shorts, covers, generated thumbnails, temporary files ----

    /// <summary>What "Delete rendered shorts and thumbnails" removes. Downloaded videos, transcripts and suggestions stay.</summary>
    /// <param name="Picked">Cover images the user picked for a short (made in ChatGPT, say), wherever they are: only
    /// the exact files a suggestion points at, named in the confirmation.</param>
    private sealed record LocalFiles(List<string> Shorts, List<string> Covers, List<string> Thumbnails, List<string> Picked, List<string> Temp, List<string> TempDirs, List<string> Projects)
    {
        public IEnumerable<string> AllFiles => Shorts.Concat(Covers).Concat(Thumbnails).Concat(Picked).Concat(Temp);
    }

    private static long SizeOf(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static long SizeOfDir(string dir) { try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(SizeOf); } catch { return 0; } }
    private static string Bytes(long b) => b >= 1024L * 1024 * 1024 ? $"{b / (1024.0 * 1024 * 1024):0.0} GB" : $"{Math.Max(b > 0 ? 1 : 0, b / (1024 * 1024))} MB";

    /// <summary>Every project next to the downloaded videos (and the open one), and what they and the shorts folders hold.</summary>
    private LocalFiles ScanLocalFiles()
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var shorts = new HashSet<string>(cmp); var covers = new HashSet<string>(cmp); var thumbs = new HashSet<string>(cmp); var picked = new HashSet<string>(cmp);
        var projects = new HashSet<string>(cmp);
        void AddProject(string p) { if (File.Exists(p)) projects.Add(p); }
        if (Directory.Exists(_settings.DownloadFolder))
            foreach (var p in Directory.EnumerateFiles(_settings.DownloadFolder, "*.shortgen.json", SearchOption.AllDirectories)) AddProject(p);
        if (_video is not null) AddProject(ProjectPath(_video.FilePath));

        foreach (var p in projects)
        {
            ProjectFile? project = null;
            try { project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(p)); } catch { }
            if (project is null) continue;
            foreach (var g in project.Generated ?? new())
            {
                if (File.Exists(g.Path)) shorts.Add(g.Path);
                if (g.CoverPath is { } c && File.Exists(c) && IsOurCover(c)) covers.Add(c);
            }
            foreach (var s in project.Suggestions?.Shorts ?? new())
                foreach (var img in new[] { s.CoverImage, s.CoverImageEn })
                    if (img is not null && File.Exists(img))
                        (img.EndsWith(".higgsfield.jpg", StringComparison.OrdinalIgnoreCase) ? thumbs : picked).Add(img);
            // every Higgsfield thumbnail saved next to the video, even ones no suggestion points at any more
            var coversDir = Path.Combine(Path.GetDirectoryName(p)!, "covers");
            if (Directory.Exists(coversDir))
                foreach (var f in Directory.EnumerateFiles(coversDir, "*.higgsfield.jpg")) thumbs.Add(f);
        }
        // Renders no project remembers any more: only inside the per-video folders this app creates in the Shorts
        // folder (Shorts\<video title>\...), never loose files in it, and never a folder picked on the Generate step,
        // which could be any folder of the user's.
        if (!string.IsNullOrWhiteSpace(_settings.OutputFolder) && Directory.Exists(_settings.OutputFolder))
            foreach (var sub in Directory.EnumerateDirectories(_settings.OutputFolder))
                foreach (var f in Directory.EnumerateFiles(sub))
                {
                    if (f.EndsWith(".cover.jpg", StringComparison.OrdinalIgnoreCase)) covers.Add(f);
                    else if (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) shorts.Add(f);
                }

        // temporary work: previews, cover frames, face analysis, TikTok copies
        var tmp = Path.GetTempPath();
        var temp = new List<string>(); var tempDirs = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(tmp, "shortgen_*")) temp.Add(f);
            foreach (var d in Directory.EnumerateDirectories(tmp, "shortgen_*")) tempDirs.Add(d);
            var own = Path.Combine(tmp, "ShortGenerator");
            if (Directory.Exists(own)) tempDirs.Add(own);
        }
        catch { }
        picked.ExceptWith(thumbs);
        return new LocalFiles(shorts.ToList(), covers.ToList(), thumbs.ToList(), picked.ToList(), temp, tempDirs, projects.ToList());
    }

    /// <summary>A cover this app exported (next to a rendered short); a cover picked from elsewhere is the user's own file.</summary>
    private static bool IsOurCover(string path) => path.EndsWith(".cover.jpg", StringComparison.OrdinalIgnoreCase);

    private static string DescribeLocal(LocalFiles f)
    {
        long total = f.AllFiles.Sum(SizeOf) + f.TempDirs.Sum(SizeOfDir);
        int thumbs = f.Thumbnails.Count + f.Picked.Count;
        if (f.Shorts.Count + f.Covers.Count + thumbs + f.Temp.Count + f.TempDirs.Count == 0) return "Nothing to delete.";
        return $"{f.Shorts.Count} rendered short{(f.Shorts.Count == 1 ? "" : "s")}, {f.Covers.Count} exported cover{(f.Covers.Count == 1 ? "" : "s")}, " +
               $"{thumbs} thumbnail image{(thumbs == 1 ? "" : "s")} and temporary files: {Bytes(total)}.";
    }

    /// <summary>Asks, deletes, and forgets the deleted files in every project so no list points at a missing file.</summary>
    private bool CleanLocalFiles(IWin32Window owner)
    {
        if (_publishPanel.IsPublishing)
        {
            AppDialog.Alert(owner, "Uploads still running", "Wait for the publishing queue to finish: it is still sending files from this computer.", AppDialog.Kind.Warning);
            return false;
        }
        if (_cts is not null)
        {
            AppDialog.Alert(owner, "Something is still running", "Wait for the current render or task to finish, or cancel it, then try again.", AppDialog.Kind.Warning);
            return false;
        }
        var f = ScanLocalFiles();
        long shortsBytes = f.Shorts.Sum(SizeOf), coverBytes = f.Covers.Sum(SizeOf) + f.Thumbnails.Sum(SizeOf) + f.Picked.Sum(SizeOf), tempBytes = f.Temp.Sum(SizeOf) + f.TempDirs.Sum(SizeOfDir);
        int thumbCount = f.Thumbnails.Count + f.Picked.Count;
        if (shortsBytes + coverBytes + tempBytes == 0 && f.Shorts.Count + f.Covers.Count + thumbCount == 0)
        {
            AppDialog.Alert(owner, "Nothing to delete", "There are no rendered shorts, covers or generated thumbnails on this computer.");
            return false;
        }
        var details = new List<string>
        {
            $"{f.Shorts.Count} rendered short{(f.Shorts.Count == 1 ? "" : "s")}\n{Bytes(shortsBytes)}, original and English versions",
            $"{f.Covers.Count} exported cover{(f.Covers.Count == 1 ? "" : "s")} and {thumbCount} thumbnail image{(thumbCount == 1 ? "" : "s")}\n{Bytes(coverBytes)}",
            $"Temporary files\n{Bytes(tempBytes)} of previews and work files",
        };
        if (f.Picked.Count > 0)
        {
            // images outside this app's folders: name them, so nobody loses a file by surprise
            var names = f.Picked.Take(5).Select(Path.GetFileName).ToList();
            var where = f.Picked.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? Path.GetDirectoryName(f.Picked[0]) : "several folders";
            details.Add($"Thumbnail images you picked, in {where}\n{string.Join(", ", names)}{(f.Picked.Count > 5 ? $" and {f.Picked.Count - 5} more" : "")}");
        }
        if (!AppDialog.Confirm(owner, "Delete rendered shorts and thumbnails?", "This frees disk space on this computer:", "Delete all", danger: true, details: details,
                notes: new[]
                {
                    "Downloaded videos, transcripts, suggestions and your edits stay: you can render any short again.",
                    "Published posts stay online. To publish a short again, render it again first.",
                    "This cannot be undone.",
                })) return false;

        if (_player is not null) _ = _player.PauseAsync(); // a preview may hold a rendered file open
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int failed = 0;
        foreach (var file in f.AllFiles) { if (TryDelete(file)) deleted.Add(file); else if (File.Exists(file)) failed++; }
        foreach (var d in f.TempDirs) { try { Directory.Delete(d, recursive: true); } catch { } }
        // empty per-video folders left in the shorts folders
        foreach (var sub in deleted.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            try { if (sub is not null && Directory.Exists(sub) && !Directory.EnumerateFileSystemEntries(sub).Any()
                      && string.Equals(Path.GetDirectoryName(sub), _settings.OutputFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) Directory.Delete(sub); } catch { }

        // forget them in every project: the open one in memory, the others on disk
        string? openProject = _video is null ? null : ProjectPath(_video.FilePath);
        foreach (var p in f.Projects)
        {
            if (openProject is not null && string.Equals(p, openProject, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(p));
                if (project is null) continue;
                project.Generated?.RemoveAll(g => !File.Exists(g.Path));
                foreach (var s in project.Suggestions?.Shorts ?? new()) ForgetDeletedCovers(s, deleted);
                File.WriteAllText(p, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log($"Could not update {Path.GetFileName(p)}: {ex.Message}"); }
        }
        if (_video is not null)
        {
            _generated.RemoveAll(g => !File.Exists(g.Path));
            foreach (var s in _suggestions?.Shorts ?? new()) ForgetDeletedCovers(s, deleted);
            SaveProject();
        }
        foreach (ListViewItem it in _results.Items.Cast<ListViewItem>().ToList())
            if (it.Tag is string path && !File.Exists(path)) _results.Items.Remove(it);
        _publishPanel.RefreshList();
        RefreshLibrary();

        long freed = shortsBytes + coverBytes + tempBytes;
        Log($"Disk clean-up: {deleted.Count} file(s) deleted, about {Bytes(freed)} freed" + (failed > 0 ? $"; {failed} in use and kept." : "."));
        AppDialog.Alert(owner, "Disk space freed", $"{deleted.Count} file{(deleted.Count == 1 ? "" : "s")} deleted, about {Bytes(freed)}." +
            (failed > 0 ? $" {failed} {(failed == 1 ? "file was" : "files were")} in use and kept; close any player showing them and try again." : ""), AppDialog.Kind.Success);
        return true;
    }

    /// <summary>A suggestion whose cover image was deleted goes back to a frame of the video.</summary>
    private static void ForgetDeletedCovers(ShortSuggestion s, HashSet<string> deleted)
    {
        if (s.CoverImage is { } a && deleted.Contains(a)) s.CoverImage = null;
        if (s.CoverImageEn is { } b && deleted.Contains(b)) s.CoverImageEn = null;
    }

    // ---- project persistence: <video>.shortgen.json next to the video ----

    private sealed class ProjectFile
    {
        public VideoInfo? Video { get; set; }
        public Transcript? Transcript { get; set; }
        public SuggestionResponse? Suggestions { get; set; }
        public List<GeneratedFile>? Generated { get; set; }
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
