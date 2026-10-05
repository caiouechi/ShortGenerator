using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShortGenerator.Forms.Controls;

/// <summary>
/// Gives the stock text fields, number fields and drop-downs the same quiet chrome as the rest of the app, without
/// replacing them: a hairline border in the theme colour (the accent while focused) instead of the black Windows
/// frame, and for drop-downs a clean arrow area. It sits in front of the control's window procedure and repaints the
/// frame after Windows has drawn the control.
/// </summary>
public static class FieldChrome
{
    private const int WM_NCPAINT = 0x0085, WM_PAINT = 0x000F, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_LBUTTONDOWN = 0x0201;
    /// <summary>Width of the spinner column drawn for number fields.</summary>
    public const int SpinWidth = 16;

    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    private const uint RDW_FRAME = 0x0400, RDW_INVALIDATE = 0x0001, RDW_UPDATENOW = 0x0100;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Chrome> _attached = new();

    public static void Attach(Control c)
    {
        if (_attached.TryGetValue(c, out _)) return;
        var chrome = new Chrome(c);
        _attached.Add(c, chrome);
        if (c.IsHandleCreated) chrome.AssignHandle(c.Handle);
        c.HandleCreated += (_, _) => { if (chrome.Handle != c.Handle) { if (chrome.Handle != IntPtr.Zero) chrome.ReleaseHandle(); chrome.AssignHandle(c.Handle); } };
        c.HandleDestroyed += (_, _) => { if (chrome.Handle != IntPtr.Zero) chrome.ReleaseHandle(); };
        // the frame follows focus and enabled state
        c.GotFocus += (_, _) => chrome.Refresh();
        c.LostFocus += (_, _) => chrome.Refresh();
        c.EnabledChanged += (_, _) => chrome.Refresh();
        if (c is NumericUpDown nud)
        {
            // the inner text box is what takes the focus; the Windows spinner buttons are hidden and drawn by the chrome
            foreach (Control inner in nud.Controls)
            {
                inner.GotFocus += (_, _) => chrome.Refresh(); inner.LostFocus += (_, _) => chrome.Refresh();
                if (inner.GetType().Name == "UpDownButtons") inner.Visible = false;
                else
                {
                    // the inner text box paints on its own schedule (focus, caret, selection) and can clip the spinner
                    // column: after each of its paints the frame is drawn again
                    var follower = new Follower(chrome);
                    if (inner.IsHandleCreated) follower.AssignHandle(inner.Handle);
                    inner.HandleCreated += (_, _) => { if (follower.Handle != inner.Handle) { if (follower.Handle != IntPtr.Zero) follower.ReleaseHandle(); follower.AssignHandle(inner.Handle); } };
                    inner.HandleDestroyed += (_, _) => { if (follower.Handle != IntPtr.Zero) follower.ReleaseHandle(); };
                    chrome.Keep(follower);
                }
            }
        }
    }

    /// <summary>Repaints the owner's frame after a child window has painted.</summary>
    private sealed class Follower : NativeWindow
    {
        private readonly Chrome _owner;
        public Follower(Chrome owner) => _owner = owner;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT) _owner.PaintFrameNow();
        }
    }

    private sealed class Chrome : NativeWindow
    {
        private readonly Control _c;
        private readonly List<NativeWindow> _kept = new();
        public Chrome(Control c) => _c = c;
        public void Keep(NativeWindow w) => _kept.Add(w);
        public void PaintFrameNow() => PaintFrame();

        public void Refresh()
        {
            if (Handle == IntPtr.Zero) return;
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME | RDW_INVALIDATE | RDW_UPDATENOW);
        }

        private bool HasFocus => _c.Focused || (_c is NumericUpDown n && n.Controls.Cast<Control>().Any(x => x.Focused)) || (_c is ComboBox cb && cb.Focused);

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case WM_NCPAINT:
                case WM_PAINT when _c is ComboBox or NumericUpDown:
                    PaintFrame();
                    break;
                case WM_LBUTTONDOWN when _c is NumericUpDown nud:
                {
                    int x = unchecked((short)(long)m.LParam), y = unchecked((short)((long)m.LParam >> 16));
                    if (x >= _c.Width - SpinWidth - 1) { if (y < _c.Height / 2) nud.UpButton(); else nud.DownButton(); nud.Invalidate(); }
                    break;
                }
                case WM_SETFOCUS:
                case WM_KILLFOCUS:
                    PaintFrame();
                    break;
            }
        }

        private void PaintFrame()
        {
            if (Handle == IntPtr.Zero || !_c.IsHandleCreated) return;
            var hdc = GetWindowDC(Handle);
            if (hdc == IntPtr.Zero) return;
            try
            {
                using var g = Graphics.FromHdc(hdc);
                int w = _c.Width, h = _c.Height;
                var edge = !_c.Enabled ? Theme.Border : HasFocus ? Theme.Accent : Theme.BorderStrong;
                if (_c is ComboBox cb)
                {
                    // a flat combo paints its own grey frame and a boxed arrow: cover both with ours
                    var bg = cb.Enabled ? cb.BackColor : Theme.SurfaceStrong;
                    int aw = SystemInformation.VerticalScrollBarWidth + 6; // the flat button is a little wider than a scroll bar
                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, w - aw - 1, 1, aw, h - 2);
                    using var pen = new Pen(edge);
                    g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                    // a thin chevron
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using var cp = new Pen(cb.Enabled ? Theme.TextSecondary : Theme.TextMuted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    float cx = w - aw / 2f - 1, cy = h / 2f;
                    g.DrawLine(cp, cx - 4, cy - 2, cx, cy + 2); g.DrawLine(cp, cx, cy + 2, cx + 4, cy - 2);
                }
                else if (_c is NumericUpDown nud)
                {
                    // the frame, and two thin chevrons where the Windows spinner was
                    using (var b = new SolidBrush(nud.Enabled ? nud.BackColor : Theme.SurfaceStrong)) g.FillRectangle(b, w - SpinWidth - 1, 1, SpinWidth, h - 2);
                    using var pen = new Pen(edge);
                    g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using var cp = new Pen(nud.Enabled ? Theme.TextSecondary : Theme.TextMuted, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    float cx = w - SpinWidth / 2f - 1, up = h * 0.33f, dn = h * 0.67f;
                    g.DrawLine(cp, cx - 3, up + 1.5f, cx, up - 1.5f); g.DrawLine(cp, cx, up - 1.5f, cx + 3, up + 1.5f);
                    g.DrawLine(cp, cx - 3, dn - 1.5f, cx, dn + 1.5f); g.DrawLine(cp, cx, dn + 1.5f, cx + 3, dn - 1.5f);
                }
                else
                {
                    using var pen = new Pen(edge);
                    g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                    if (HasFocus) { using var inner = new Pen(Color.FromArgb(70, Theme.Accent)); g.DrawRectangle(inner, 1, 1, w - 3, h - 3); }
                }
            }
            finally { ReleaseDC(Handle, hdc); }
        }
    }
}
