using System.Drawing.Drawing2D;
using System.Globalization;
using ShortGenerator.Forms.Controls;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// Picks the moment of another video to show over the short, and how it is framed. Frames come from ffmpeg as the
/// slider moves (no embedded browser here: a second WebView2 in a modal window crashed on some machines); "Start
/// here" / "End here" mark the clip and "Play the clip" plays a light preview of it. The framing box (9:16 for full
/// screen, the chosen shape for picture in picture) is dragged over the frame and the wheel zooms, exactly the crop
/// the render uses.
/// </summary>
public sealed class VideoClipDialog : Form
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double SourceStart { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Length { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FullFrame { get; private set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CropX { get; private set; } = 50;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double CropY { get; private set; } = 50;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Zoom { get; private set; } = 1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Aspect { get; private set; } = 9.0 / 16;
    /// <summary>Picture in picture: centre (percent of the short) and width (percent of the short's width) of the window.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double X { get; private set; } = 50;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Y { get; private set; } = 22;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double WindowWidth { get; private set; } = 32;

    private readonly Func<CancellationToken, Task<Bitmap?>>? _shortFrame;
    private readonly ShortView _short;

    private readonly FfmpegRunner _ffmpeg;
    private readonly string _path;
    private readonly FrameView _view;
    private readonly TrackBar _slider = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 1000, SmallChange = 10, LargeChange = 100 };
    private readonly Label _time = new() { AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(8, 8, 0, 0), Font = Theme.Mono(9.5f) };
    private readonly NumericUpDown _start = new() { DecimalPlaces = 1, Increment = 0.5M, Maximum = 100000, Width = 90 };
    private readonly NumericUpDown _end = new() { DecimalPlaces = 1, Increment = 0.5M, Maximum = 100000, Width = 90 };
    private readonly RadioButton _full = new() { Text = "Full screen (9:16)", AutoSize = true, Checked = true };
    private readonly RadioButton _inset = new() { Text = "Picture in picture", AutoSize = true };
    private readonly ComboBox _shape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Label _info = new() { AutoSize = true, ForeColor = Theme.TextMuted };
    private readonly FancyButton _play = new() { Text = "Play the clip", Width = 130, Glyph = "" };
    private readonly System.Windows.Forms.Timer _scrubTimer = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 125 };
    private readonly string _work = Path.Combine(Path.GetTempPath(), "shortgen_clip_" + Guid.NewGuid().ToString("N")[..8]);
    private CancellationTokenSource? _frameCts;
    private double _duration, _now;
    private List<Bitmap>? _playFrames;
    private int _playIndex;

    /// <param name="shortFrame">A frame of the short where the clip appears, to place the window on (null = no short preview).</param>
    public VideoClipDialog(FfmpegRunner ffmpeg, string path, double sourceStart, double length, bool fullFrame, double cropX, double cropY, double zoom,
        double maxLength, double aspect = 9.0 / 16, Func<CancellationToken, Task<Bitmap?>>? shortFrame = null, double x = 50, double y = 22, double size = 32)
    {
        _ffmpeg = ffmpeg; _path = path; _shortFrame = shortFrame;
        X = x; Y = y; WindowWidth = size;
        SourceStart = sourceStart; Length = length; FullFrame = fullFrame; CropX = cropX; CropY = cropY; Zoom = zoom; Aspect = aspect;
        Text = "Video clip: " + Path.GetFileNameWithoutExtension(path);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(1240, 780);
        MinimumSize = new System.Drawing.Size(900, 580);
        BackColor = Theme.Bg;
        _now = sourceStart;

        _view = new FrameView(this) { Dock = DockStyle.Fill };
        var head = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(16, 10, 16, 0), ForeColor = Theme.TextSecondary,
            Text = "Move the slider to the moment you want (arrow keys step 0.1 s, Shift+arrow 1 s), then click \"Start here\" and \"End here\". Drag the box to frame it and scroll to zoom. The short keeps its own sound and its captions on top.",
        };

        // scrub row: step buttons, slider, time
        var scrub = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, ColumnCount = 6, Padding = new Padding(12, 6, 12, 0), BackColor = Theme.SurfaceStrong };
        for (int i = 0; i < 2; i++) scrub.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scrub.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) scrub.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FancyButton Step(string text, double by) { var b = new FancyButton { Text = text, Width = 58, Height = 30, Margin = new Padding(0, 0, 4, 0) }; b.Click += (_, _) => SeekTo(_now + by); return b; }
        scrub.Controls.Add(Step("-1 s", -1), 0, 0);
        scrub.Controls.Add(Step("-0.1", -0.1), 1, 0);
        scrub.Controls.Add(_slider, 2, 0);
        scrub.Controls.Add(Step("+0.1", 0.1), 3, 0);
        scrub.Controls.Add(Step("+1 s", 1), 4, 0);
        scrub.Controls.Add(_time, 5, 0);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(14, 8, 14, 10), WrapContents = true, BackColor = Theme.SurfaceStrong };
        var startHere = new FancyButton { Text = "Start here", Width = 120, Glyph = "" };
        var endHere = new FancyButton { Text = "End here", Width = 110, Glyph = "" };
        Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(14, 9, 4, 0), ForeColor = Theme.TextSecondary };
        bar.Controls.Add(startHere);
        bar.Controls.Add(L("Start (s)")); bar.Controls.Add(_start);
        bar.Controls.Add(endHere);
        bar.Controls.Add(L("End (s)")); bar.Controls.Add(_end);
        bar.Controls.Add(_play);
        _full.Margin = new Padding(18, 8, 6, 0); _inset.Margin = new Padding(6, 8, 6, 0);
        bar.Controls.Add(_full); bar.Controls.Add(_inset);
        foreach (var (_, name) in ImageOverlay.InsetShapes) _shape.Items.Add(name);
        _shape.SelectedIndex = Math.Max(0, Array.FindIndex(ImageOverlay.InsetShapes, x => Math.Abs(x.Aspect - aspect) < 0.01));
        _shape.Margin = new Padding(4, 5, 6, 0);
        bar.Controls.Add(_shape);
        _info.Margin = new Padding(14, 9, 0, 0);
        bar.Controls.Add(_info);
        foreach (var b in new[] { startHere, endHere, _play }) b.Margin = new Padding(0, 2, 6, 2);

        var ok = new FancyButton { Text = "Use this clip", Width = 140, Height = 38 };
        var cancel = new FancyButton { Text = "Cancel", Width = 100, Height = 38 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 6, 14, 12), BackColor = Theme.SurfaceStrong };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);

        // right: the short at the moment the clip appears, with the clip on it (drag and scroll to place a window)
        _short = new ShortView(this) { Dock = DockStyle.Fill, BackColor = Theme.Navy };
        var shortHost = new Panel { Dock = DockStyle.Right, Width = 330, Padding = new Padding(10, 6, 10, 6), BackColor = Theme.SurfaceStrong };
        var shortHint = new Label
        {
            Dock = DockStyle.Bottom, Height = 46, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.TopCenter,
            Text = "Drag the window to place it, scroll to resize it.",
        };
        shortHost.Controls.Add(_short);
        shortHost.Controls.Add(shortHint);
        shortHost.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "On the short", Font = Theme.HeadingFont(10f), ForeColor = Theme.Heading });
        shortHost.Visible = shortFrame is not null;
        Controls.Add(_view);
        Controls.Add(shortHost);
        Controls.Add(head);
        Controls.Add(scrub);
        Controls.Add(bar);
        Controls.Add(buttons);
        Theme.Apply(this);
        Theme.Primary(ok);
        _view.BackColor = Theme.Navy;

        _start.Value = (decimal)Math.Max(0, Math.Round(sourceStart, 1));
        _end.Value = (decimal)Math.Max(0, Math.Round(sourceStart + length, 1));
        _full.Checked = fullFrame; _inset.Checked = !fullFrame;
        _shape.Enabled = !fullFrame;

        _slider.Scroll += (_, _) => { StopPlay(load: false); _now = _slider.Value / 1000.0 * _duration; ShowTime(); _scrubTimer.Stop(); _scrubTimer.Start(); };
        _scrubTimer.Tick += async (_, _) => { _scrubTimer.Stop(); await LoadFrameAsync(_now); };
        startHere.Click += (_, _) => { StopPlay(); _start.Value = Clamp(_now); if (_end.Value <= _start.Value) _end.Value = Clamp(_now + Math.Min(5, maxLength)); ShowInfo(); };
        endHere.Click += (_, _) => { StopPlay(); _end.Value = Clamp(Math.Max(_now, (double)_start.Value + 0.5)); ShowInfo(); };
        _play.Click += async (_, _) => await TogglePlayAsync();
        _playTimer.Tick += (_, _) =>
        {
            if (_playFrames is null || _playIndex >= _playFrames.Count) { StopPlay(); return; }
            _view.SetFrame(_playFrames[_playIndex++], keep: true);
        };
        _start.ValueChanged += (_, _) => ShowInfo();
        _end.ValueChanged += (_, _) => ShowInfo();
        _full.CheckedChanged += (_, _) => { _shape.Enabled = _inset.Checked; _view.Invalidate(); _short.Invalidate(); };
        _shape.SelectedIndexChanged += (_, _) => { _view.Invalidate(); _short.Invalidate(); };
        ok.Click += (_, _) =>
        {
            double a = (double)_start.Value, b = (double)_end.Value;
            if (b - a < 0.5) { AppDialog.Alert(this, "Pick the clip", "Mark where the clip starts and ends: it has to last at least half a second.", AppDialog.Kind.Warning); return; }
            SourceStart = a; Length = Math.Min(b - a, maxLength); FullFrame = _full.Checked;
            Aspect = ImageOverlay.InsetShapes[Math.Max(0, _shape.SelectedIndex)].Aspect;
            DialogResult = DialogResult.OK; Close();
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = cancel;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (ActiveControl is NumericUpDown or ComboBox) return;
            if (e.KeyCode == Keys.Left) { SeekTo(_now - (e.Shift ? 1 : 0.1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { SeekTo(_now + (e.Shift ? 1 : 0.1)); e.Handled = true; }
        };
        Load += async (_, _) => await InitAsync();
        FormClosed += (_, _) => { _playTimer.Stop(); _scrubTimer.Stop(); _frameCts?.Cancel(); DisposePlayFrames(); try { Directory.Delete(_work, true); } catch { } };
        ShowInfo();
    }

    /// <summary>The framing box's shape: 9:16 for full screen, the chosen window shape for picture in picture (0 = whole frame).</summary>
    private double BoxAspect => _full.Checked ? 9.0 / 16 : ImageOverlay.InsetShapes[Math.Max(0, _shape.SelectedIndex)].Aspect;

    private decimal Clamp(double t) => (decimal)Math.Round(Math.Clamp(t, 0, _duration > 0 ? _duration : 100000), 1);

    private void ShowInfo()
    {
        double len = (double)_end.Value - (double)_start.Value;
        _info.Text = len > 0 ? $"Clip: {len:0.0} s" : "Mark the start and the end";
    }

    private static string Fmt(double t) { var ts = TimeSpan.FromSeconds(Math.Max(0, t)); return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss\.f") : ts.ToString(@"mm\:ss\.f"); }
    private void ShowTime() => _time.Text = $"{Fmt(_now)} / {Fmt(_duration)}";

    private async Task InitAsync()
    {
        try
        {
            Directory.CreateDirectory(_work);
            var (w, h, d) = await _ffmpeg.ProbeAsync(_path, CancellationToken.None);
            if (IsDisposed) return;
            _duration = d;
            _view.SourceSize = new System.Drawing.Size(w, h);
            ShowTime();
            await LoadFrameAsync(_now);
            if (_shortFrame is not null)
            {
                var bmp = await _shortFrame(CancellationToken.None);
                if (!IsDisposed && bmp is not null) _short.SetBackground(bmp);
            }
        }
        catch (Exception ex) { AppLog.Error("clip dialog", ex); }
    }

    private void SeekTo(double t)
    {
        StopPlay(load: false);
        _now = Math.Clamp(t, 0, _duration > 0 ? _duration : Math.Max(0, t));
        if (_duration > 0) _slider.Value = Math.Clamp((int)Math.Round(_now / _duration * 1000), 0, 1000);
        ShowTime();
        _scrubTimer.Stop(); _scrubTimer.Start();
    }

    /// <summary>One frame at <paramref name="t"/> from ffmpeg (fast seek), shown when it arrives; a newer request cancels an older one.</summary>
    private async Task LoadFrameAsync(double t)
    {
        _frameCts?.Cancel();
        var cts = _frameCts = new CancellationTokenSource();
        var file = Path.Combine(_work, $"f{Guid.NewGuid():N}.jpg");
        try
        {
            if (_duration > 0) _slider.Value = Math.Clamp((int)Math.Round(t / _duration * 1000), 0, 1000);
            await _ffmpeg.RunAsync(new[] { "-y", "-ss", t.ToString("F3", CultureInfo.InvariantCulture), "-i", _path, "-frames:v", "1", "-vf", "scale=960:-2", "-q:v", "3", file }, null, null, 0, cts.Token);
            if (cts.IsCancellationRequested || IsDisposed || !File.Exists(file)) return;
            _view.SetFrame(Theme.ReadImage(file));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Error("clip frame", ex); }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>A light preview of the marked clip: frames at 8 per second, played in place.</summary>
    private async Task TogglePlayAsync()
    {
        if (_playTimer.Enabled) { StopPlay(); return; }
        double a = (double)_start.Value, b = (double)_end.Value;
        if (b - a < 0.2) return;
        _play.Enabled = false; _play.Text = "Preparing...";
        var dir = Path.Combine(_work, "play" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            await _ffmpeg.RunAsync(new[] { "-y", "-ss", a.ToString("F3", CultureInfo.InvariantCulture), "-t", (b - a).ToString("F3", CultureInfo.InvariantCulture), "-i", _path,
                "-vf", "fps=8,scale=640:-2", "-q:v", "5", Path.Combine(dir, "p%04d.jpg") }, null, null, 0, CancellationToken.None);
            if (IsDisposed) return;
            DisposePlayFrames();
            _playFrames = Directory.GetFiles(dir, "p*.jpg").OrderBy(f => f).Select(Theme.ReadImage).ToList();
            _playIndex = 0;
            _play.Text = "Stop"; _play.Glyph = "";
            _playTimer.Start();
        }
        catch (Exception ex) { AppLog.Error("clip preview", ex); _play.Text = "Play the clip"; }
        finally { _play.Enabled = true; try { Directory.Delete(dir, true); } catch { } }
    }

    private void StopPlay(bool load = true)
    {
        if (!_playTimer.Enabled && _playFrames is null) return;
        _playTimer.Stop();
        _play.Text = "Play the clip"; _play.Glyph = "";
        DisposePlayFrames();
        if (load) _ = LoadFrameAsync(_now);
    }

    private void DisposePlayFrames()
    {
        if (_playFrames is null) return;
        var frames = _playFrames; _playFrames = null;
        _view.ReleaseIfShown(frames);
        foreach (var f in frames) f.Dispose();
    }

    // ------------------------------------------------------------------ the frame with its framing box

    private sealed class FrameView : Control
    {
        private readonly VideoClipDialog _d;
        private Bitmap? _frame;
        private bool _frameOwned = true;
        private Point _dragFrom;
        private double _cx0, _cy0;
        private bool _dragging;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Size SourceSize { get; set; }

        public FrameView(VideoClipDialog d)
        {
            _d = d;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.SizeAll;
        }

        /// <param name="keep">A preview frame owned by the preview list (not disposed here).</param>
        public void SetFrame(Bitmap bmp, bool keep = false)
        {
            var old = _frame; bool oldOwned = _frameOwned;
            _frame = bmp; _frameOwned = !keep;
            if (old is not null && oldOwned && !ReferenceEquals(old, bmp)) old.Dispose();
            Invalidate();
        }

        /// <summary>The source frame on screen (the short view draws the clip from it).</summary>
        public Bitmap? Frame => _frame;

        protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); _d._short?.Invalidate(); }

        public void ReleaseIfShown(List<Bitmap> frames) { if (_frame is not null && frames.Contains(_frame)) { _frame = null; _frameOwned = true; Invalidate(); } }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _frameOwned) _frame?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Where the frame is drawn: letterboxed into the control.</summary>
        private RectangleF FrameRect()
        {
            var src = SourceSize.Width > 0 ? SourceSize : (_frame?.Size ?? Size.Empty);
            if (src.Width <= 0 || src.Height <= 0) return RectangleF.Empty;
            float s = Math.Min((float)Width / src.Width, (float)Height / src.Height);
            float w = src.Width * s, h = src.Height * s;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        /// <summary>The framing box in control pixels: the same rule as the render's crop (largest of that shape, / zoom).</summary>
        private RectangleF BoxRect(RectangleF fr)
        {
            double a = _d.BoxAspect > 0 ? _d.BoxAspect : fr.Width / fr.Height;
            double w, h;
            if (fr.Width / fr.Height > a) { h = fr.Height; w = h * a; } else { w = fr.Width; h = w / a; }
            double z = Math.Clamp(_d.Zoom, 1, 4);
            w /= z; h /= z;
            double cx = fr.X + _d.CropX / 100 * fr.Width, cy = fr.Y + _d.CropY / 100 * fr.Height;
            double x = Math.Clamp(cx - w / 2, fr.X, fr.Right - w), y = Math.Clamp(cy - h / 2, fr.Y, fr.Bottom - h);
            return new RectangleF((float)x, (float)y, (float)w, (float)h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var fr = FrameRect();
            if (fr.IsEmpty || _frame is null)
            {
                using var lf = Theme.Body(10f);
                TextRenderer.DrawText(g, "Loading the video...", lf, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (fr.IsEmpty) return;
            }
            if (_frame is not null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(_frame, fr);
            }
            var box = BoxRect(fr);
            using (var shade = new SolidBrush(Color.FromArgb(140, 11, 16, 40)))
            using (var outside = new Region(fr))
            {
                outside.Exclude(box);
                g.FillRegion(shade, outside);
            }
            using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            const string label = "what the viewer sees";
            using var f = Theme.Body(8.5f, FontStyle.Bold);
            var sz = TextRenderer.MeasureText(label, f);
            var lr = new Rectangle((int)box.X, (int)box.Y - sz.Height - 6, sz.Width + 10, sz.Height + 4);
            if (lr.Y < 0) lr.Y = (int)box.Y + 4;
            using (var b = new SolidBrush(Theme.Purple)) g.FillRectangle(b, lr);
            TextRenderer.DrawText(g, label, f, lr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _dragging = true; _dragFrom = e.Location; _cx0 = _d.CropX; _cy0 = _d.CropY;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var fr = FrameRect();
            if (fr.Width <= 0) return;
            _d.CropX = Math.Clamp(_cx0 + (e.X - _dragFrom.X) / fr.Width * 100, 0, 100);
            _d.CropY = Math.Clamp(_cy0 + (e.Y - _dragFrom.Y) / fr.Height * 100, 0, 100);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Capture = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _d.Zoom = Math.Clamp(_d.Zoom + (e.Delta > 0 ? 0.1 : -0.1), 1, 4);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); } // so the wheel reaches it
    }

    // ------------------------------------------------------------------ the short, with the clip on it

    /// <summary>
    /// A 9:16 frame of the short with the clip drawn as the render will: covering it (full screen) or as a window
    /// with a white border (picture in picture) that is dragged to place it and scrolled to shrink or grow it.
    /// </summary>
    private sealed class ShortView : Control
    {
        private readonly VideoClipDialog _d;
        private Bitmap? _bg;
        private bool _dragging;
        private Point _from;
        private double _x0, _y0;

        public ShortView(VideoClipDialog d)
        {
            _d = d;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public void SetBackground(Bitmap bmp) { var old = _bg; _bg = bmp; old?.Dispose(); Invalidate(); }

        protected override void Dispose(bool disposing) { if (disposing) _bg?.Dispose(); base.Dispose(disposing); }

        /// <summary>The 9:16 canvas inside the control.</summary>
        private RectangleF Canvas()
        {
            float h = Height, w = h * 9f / 16f;
            if (w > Width) { w = Width; h = w * 16f / 9f; }
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        /// <summary>The clip's window on the canvas (picture in picture): width = Size % of the short, height from the shape.</summary>
        private RectangleF Window(RectangleF c, double aspect)
        {
            float w = (float)(_d.WindowWidth / 100 * c.Width), h = (float)(w / aspect);
            float cx = c.X + (float)(_d.X / 100 * c.Width), cy = c.Y + (float)(_d.Y / 100 * c.Height);
            return new RectangleF(cx - w / 2, cy - h / 2, w, h);
        }

        private double ClipAspect()
        {
            var f = _d._view.Frame;
            double src = f is null ? 16.0 / 9 : (double)f.Width / f.Height;
            return _d.BoxAspect > 0 ? _d.BoxAspect : src;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            var c = Canvas();
            if (_bg is not null) g.DrawImage(_bg, c);
            else
            {
                using var dark = new SolidBrush(Color.FromArgb(25, 31, 64));
                g.FillRectangle(dark, c);
                using var lf = Theme.Body(9f);
                TextRenderer.DrawText(g, "Loading the short...", lf, Rectangle.Round(c), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            var frame = _d._view.Frame;
            if (frame is null) return;
            // the clip's crop in the frame's own pixels (same rule as the render)
            var o = new ImageOverlay { CropX = _d.CropX, CropY = _d.CropY, Zoom = _d.Zoom };
            if (_d._full.Checked)
            {
                var r = o.CropRect(frame.Width, frame.Height, 9.0 / 16);
                g.DrawImage(frame, c, new RectangleF(r.X, r.Y, r.W, r.H), GraphicsUnit.Pixel);
                return;
            }
            double a = ClipAspect();
            var cr = o.CropRect(frame.Width, frame.Height, a);
            var win = Window(c, a);
            float border = Math.Max(2, win.Width * 0.012f);
            using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, win);
            var inner = RectangleF.Inflate(win, -border, -border);
            var state = g.Save();
            g.SetClip(c);
            g.DrawImage(frame, inner, new RectangleF(cr.X, cr.Y, cr.W, cr.H), GraphicsUnit.Pixel);
            using (var sel = new Pen(Theme.Nebula, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(sel, win.X - 3, win.Y - 3, win.Width + 6, win.Height + 6);
            g.Restore(state);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left || _d._full.Checked) return;
            _dragging = true; _from = e.Location; _x0 = _d.X; _y0 = _d.Y;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _d._full.Checked ? Cursors.Default : Cursors.SizeAll;
            if (!_dragging) return;
            var c = Canvas();
            _d.X = Math.Clamp(_x0 + (e.X - _from.X) / c.Width * 100, 0, 100);
            _d.Y = Math.Clamp(_y0 + (e.Y - _from.Y) / c.Height * 100, 0, 100);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Capture = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_d._full.Checked) return;
            _d.WindowWidth = Math.Clamp(_d.WindowWidth + (e.Delta > 0 ? 2 : -2), 10, 100);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    }
}
