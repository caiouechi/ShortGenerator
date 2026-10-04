using System.Drawing.Drawing2D;
using System.Globalization;
using ShortGenerator.Forms.Controls;
using ShortGenerator.Models;
using ShortGenerator.Services;

namespace ShortGenerator.Forms;

/// <summary>
/// Draws the area of the source frame the auto camera may look at. Faces outside it (on a screen in the background,
/// say) are ignored, and with no face in sight the camera frames the area's centre. The box is dragged to move and
/// its corners and edges to resize; "Clear area" goes back to the whole frame.
/// </summary>
public sealed class FocusAreaDialog : Form
{
    /// <summary>The chosen area, or null when it was cleared (whole frame).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public FocusArea? Area { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ApplyToAll => _all.Checked;

    private readonly FfmpegRunner _ffmpeg;
    private readonly string _path;
    private readonly double _time;
    private readonly AreaView _view;
    private readonly CheckBox _all = new() { Text = "Use this area for all shorts of this video", AutoSize = true };
    private readonly string _work = Path.Combine(Path.GetTempPath(), "shortgen_focus_" + Guid.NewGuid().ToString("N")[..8]);

    public FocusAreaDialog(FfmpegRunner ffmpeg, string path, double sourceTime, FocusArea? current, int shortCount)
    {
        _ffmpeg = ffmpeg; _path = path; _time = sourceTime;
        Text = "Focus area: where the auto camera looks";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(760, 520);
        BackColor = Theme.Bg;

        _view = new AreaView(current is null ? new RectangleF(0, 0, 100, 100) : new RectangleF((float)current.X, (float)current.Y, (float)current.W, (float)current.H)) { Dock = DockStyle.Fill, BackColor = Theme.Navy };
        var head = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(16, 10, 16, 0), ForeColor = Theme.TextSecondary,
            Text = "Drag the box to move it and its corners or edges to resize it. The auto camera only looks for faces inside the box (so a screen in the background is left out) and frames the box's centre when it sees no face.",
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(14, 8, 14, 6), WrapContents = true, BackColor = Theme.SurfaceStrong };
        _all.Margin = new Padding(0, 8, 14, 0);
        _all.Visible = shortCount > 1;
        _all.Text = $"Use this area for all {shortCount} shorts of this video";
        bar.Controls.Add(_all);
        var use = new FancyButton { Text = "Use this area", Width = 140, Height = 38 };
        var clear = new FancyButton { Text = "Clear area (whole frame)", Width = 200, Height = 38 };
        var cancel = new FancyButton { Text = "Cancel", Width = 100, Height = 38 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 6, 14, 12), BackColor = Theme.SurfaceStrong };
        buttons.Controls.Add(use); buttons.Controls.Add(cancel); buttons.Controls.Add(clear);
        Controls.Add(_view);
        Controls.Add(head);
        Controls.Add(bar);
        Controls.Add(buttons);
        Theme.Apply(this);
        Theme.Primary(use);
        _view.BackColor = Theme.Navy;

        use.Click += (_, _) =>
        {
            var r = _view.Area;
            // the whole frame (or nearly) means no restriction
            Area = r.Width >= 98 && r.Height >= 98 ? null : new FocusArea { X = Math.Round(r.X, 1), Y = Math.Round(r.Y, 1), W = Math.Round(r.Width, 1), H = Math.Round(r.Height, 1) };
            DialogResult = DialogResult.OK; Close();
        };
        clear.Click += (_, _) => { Area = null; DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = cancel;
        Load += async (_, _) => await LoadFrameAsync();
        FormClosed += (_, _) => { try { Directory.Delete(_work, true); } catch { } };
    }

    private async Task LoadFrameAsync()
    {
        try
        {
            Directory.CreateDirectory(_work);
            var file = Path.Combine(_work, "frame.jpg");
            await _ffmpeg.RunAsync(new[] { "-y", "-ss", _time.ToString("F3", CultureInfo.InvariantCulture), "-i", _path, "-frames:v", "1", "-vf", "scale=960:-2", "-q:v", "3", file }, null, null, 0, CancellationToken.None);
            if (!IsDisposed && File.Exists(file)) _view.SetFrame(Theme.ReadImage(file));
        }
        catch (Exception ex) { AppLog.Error("focus area frame", ex); }
    }

    /// <summary>The frame with the focus box: move by dragging inside, resize by the eight handles.</summary>
    private sealed class AreaView : Control
    {
        private Bitmap? _frame;
        private RectangleF _area; // percent of the frame
        private int _mode = -1;   // -1 none, 0..7 handles (nw n ne e se s sw w), 8 move
        private PointF _from;
        private RectangleF _area0;
        private const float MinSize = 10;

        public RectangleF Area => _area;

        public AreaView(RectangleF area)
        {
            _area = area;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetFrame(Bitmap bmp) { _frame?.Dispose(); _frame = bmp; Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) _frame?.Dispose(); base.Dispose(disposing); }

        private RectangleF FrameRect()
        {
            var src = _frame?.Size ?? new Size(16, 9);
            float s = Math.Min((float)Width / src.Width, (float)Height / src.Height);
            float w = src.Width * s, h = src.Height * s;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        private RectangleF BoxPx(RectangleF fr) => new(fr.X + _area.X / 100 * fr.Width, fr.Y + _area.Y / 100 * fr.Height, _area.Width / 100 * fr.Width, _area.Height / 100 * fr.Height);

        private static PointF[] Handles(RectangleF b) => new[]
        {
            new PointF(b.Left, b.Top), new PointF(b.Left + b.Width / 2, b.Top), new PointF(b.Right, b.Top), new PointF(b.Right, b.Top + b.Height / 2),
            new PointF(b.Right, b.Bottom), new PointF(b.Left + b.Width / 2, b.Bottom), new PointF(b.Left, b.Bottom), new PointF(b.Left, b.Top + b.Height / 2),
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var fr = FrameRect();
            if (_frame is null)
            {
                using var lf = Theme.Body(10f);
                TextRenderer.DrawText(g, "Loading the frame...", lf, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_frame, fr);
            var box = BoxPx(fr);
            using (var shade = new SolidBrush(Color.FromArgb(150, 11, 16, 40)))
            using (var outside = new Region(fr)) { outside.Exclude(box); g.FillRegion(shade, outside); }
            using (var pen = new Pen(Theme.Success, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var h in Handles(box))
            {
                using var fill = new SolidBrush(Color.White);
                using var edge = new Pen(Theme.Success, 2);
                var r = new RectangleF(h.X - 6, h.Y - 6, 12, 12);
                g.FillEllipse(fill, r); g.DrawEllipse(edge, r);
            }
            const string label = "the auto camera looks here";
            using var f = Theme.Body(8.5f, FontStyle.Bold);
            var sz = TextRenderer.MeasureText(label, f);
            var lr = new Rectangle((int)box.X + 8, (int)box.Y + 8, sz.Width + 10, sz.Height + 4);
            using (var b = new SolidBrush(Theme.Success)) g.FillRectangle(b, lr);
            TextRenderer.DrawText(g, label, f, lr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private int HitTest(Point p)
        {
            var box = BoxPx(FrameRect());
            var hs = Handles(box);
            for (int i = 0; i < hs.Length; i++) if (Math.Abs(p.X - hs[i].X) <= 9 && Math.Abs(p.Y - hs[i].Y) <= 9) return i;
            return box.Contains(p) ? 8 : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mode < 0)
            {
                int h = HitTest(e.Location);
                Cursor = h switch { 0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, 3 or 7 => Cursors.SizeWE, 8 => Cursors.SizeAll, _ => Cursors.Default };
                return;
            }
            var fr = FrameRect();
            float dx = (e.X - _from.X) / fr.Width * 100, dy = (e.Y - _from.Y) / fr.Height * 100;
            var a = _area0;
            float left = a.Left, top = a.Top, right = a.Right, bottom = a.Bottom;
            if (_mode == 8) { left = Math.Clamp(a.Left + dx, 0, 100 - a.Width); top = Math.Clamp(a.Top + dy, 0, 100 - a.Height); right = left + a.Width; bottom = top + a.Height; }
            else
            {
                if (_mode is 0 or 6 or 7) left = Math.Clamp(a.Left + dx, 0, a.Right - MinSize);
                if (_mode is 2 or 3 or 4) right = Math.Clamp(a.Right + dx, a.Left + MinSize, 100);
                if (_mode is 0 or 1 or 2) top = Math.Clamp(a.Top + dy, 0, a.Bottom - MinSize);
                if (_mode is 4 or 5 or 6) bottom = Math.Clamp(a.Bottom + dy, a.Top + MinSize, 100);
            }
            _area = RectangleF.FromLTRB(left, top, right, bottom);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _mode = HitTest(e.Location);
            if (_mode < 0) return;
            _from = e.Location; _area0 = _area; Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _mode = -1; Capture = false; }
    }
}
