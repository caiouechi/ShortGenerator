using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ShortGenerator.Services;

/// <summary>
/// Writes what the user does to the log file: every click, tick, choice and dialog on every form of the app, in
/// order, so that when something goes wrong the log shows the exact steps that led there. Lines start with "UI"
/// and name the control by its text; dialogs are logged when they open and when they close, with their result.
/// Forms are found as they appear (a window hook sees every window of the UI thread), controls as they are added.
/// Typing is not logged, only that a text box was left with a new value; repeated identical lines within a second
/// are written once.
/// </summary>
public static class UiTrace
{
    private const int WM_SHOWWINDOW = 0x0018, WH_CALLWNDPROC = 4;
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static HookProc? _hook; // kept alive for the process lifetime
    private static IntPtr _hookHandle;
    private static readonly ConditionalWeakTable<Control, object> _traced = new();
    private static string _last = ""; private static DateTime _lastAt;

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    public static void Install()
    {
        _hook = (code, wParam, lParam) =>
        {
            // a hook procedure is called from native code: an exception escaping it would end the process
            try
            {
                if (code >= 0)
                {
                    // CWPSTRUCT: lParam, wParam, message, hwnd
                    int msg = Marshal.ReadInt32(lParam, 2 * IntPtr.Size);
                    if (msg == WM_SHOWWINDOW && Marshal.ReadIntPtr(lParam, IntPtr.Size) != IntPtr.Zero)
                    {
                        var hwnd = Marshal.ReadIntPtr(lParam, 2 * IntPtr.Size + 8);
                        if (Control.FromHandle(hwnd) is Form form) Attach(form);
                    }
                }
            }
            catch (Exception ex) { AppLog.Error("UiTrace hook", ex); }
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        };
        _hookHandle = SetWindowsHookExW(WH_CALLWNDPROC, _hook, IntPtr.Zero, GetCurrentThreadId());
    }

    /// <summary>One line in the log, "UI <what>"; the same line twice within a second is written once.</summary>
    public static void Log(string what)
    {
        try { LogCore(what); } catch { }
    }

    private static void LogCore(string what)
    {
        var now = DateTime.Now;
        if (what == _last && (now - _lastAt).TotalSeconds < 1) return;
        _last = what; _lastAt = now;
        AppLog.Write("UI " + what);
    }

    private static void Attach(Form form)
    {
        if (_traced.TryGetValue(form, out _)) return;
        _traced.Add(form, form);
        string title = Name(form);
        Log($"window shown: {title}" + (form.Modal ? " (modal)" : ""));
        form.FormClosed += (_, _) => Log($"window closed: {title} -> {form.DialogResult}");
        Trace(form, title);
    }

    private static void Trace(Control control, string window)
    {
        foreach (Control child in control.Controls) Hook(child, window);
        control.ControlAdded += (_, e) => Hook(e.Control, window);
    }

    private static void Hook(Control c, string window)
    {
        if (_traced.TryGetValue(c, out _)) return;
        _traced.Add(c, c);
        string In(string what) => $"{what} [{window}]";
        switch (c)
        {
            case CheckBox cb: cb.CheckedChanged += (_, _) => Log(In($"tick: {Name(cb)} = {(cb.Checked ? "on" : "off")}")); break;
            case RadioButton rb: rb.CheckedChanged += (_, _) => { if (rb.Checked) Log(In($"choose: {Name(rb)}")); }; break;
            case Button b: b.Click += (_, _) => Log(In($"click: {Name(b)}")); break;
            case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Log(In($"select: {Name(combo)} = {combo.SelectedItem}")); break;
            case NumericUpDown n: n.ValueChanged += (_, _) => Log(In($"value: {Name(n)} = {n.Value}")); break;
            case TrackBar t: t.ValueChanged += (_, _) => Log(In($"slide: {Name(t)} = {t.Value}")); break;
            case TabControl tabs: tabs.SelectedIndexChanged += (_, _) => Log(In($"tab: {tabs.SelectedTab?.Text}")); break;
            case ListView lv:
                lv.SelectedIndexChanged += (_, _) => { if (lv.SelectedItems.Count > 0) Log(In($"row: {Name(lv)} = \"{lv.SelectedItems[0].Text}\"")); };
                lv.DoubleClick += (_, _) => Log(In($"double-click: {Name(lv)}"));
                lv.KeyDown += (_, e) => { if (e.KeyCode is Keys.Delete or Keys.Enter) Log(In($"key: {e.KeyCode} in {Name(lv)}")); };
                break;
            case DataGridView grid:
                grid.CellClick += (_, e) => Log(In($"cell: {Name(grid)} row {e.RowIndex} col {e.ColumnIndex}"));
                grid.CellDoubleClick += (_, e) => Log(In($"cell double-click: {Name(grid)} row {e.RowIndex} col {e.ColumnIndex}"));
                grid.CellEndEdit += (_, e) => Log(In($"cell edited: {Name(grid)} row {e.RowIndex} col {e.ColumnIndex}"));
                break;
            case TextBoxBase tb:
                string? was = null;
                tb.Enter += (_, _) => was = tb.Text;
                tb.Leave += (_, _) => { if (was is not null && was != tb.Text) Log(In($"text: {Name(tb)} changed ({tb.Text.Length} chars)")); };
                break;
            case LinkLabel link: link.LinkClicked += (_, _) => Log(In($"link: {Name(link)}")); break;
            case PictureBox pic: pic.Click += (_, _) => Log(In($"click: {Name(pic)}")); break;
        }
        if (c is not TextBoxBase && c is not ComboBox && c.ContextMenuStrip is { } menu) HookMenu(menu, window);
        Trace(c, window);
    }

    private static void HookMenu(ToolStrip menu, string window)
    {
        if (_traced.TryGetValue(menu, out _)) return;
        _traced.Add(menu, menu);
        menu.ItemClicked += (_, e) => Log($"menu: {e.ClickedItem?.Text} [{window}]");
    }

    /// <summary>The control by its text, or its name, or its type: enough to find it on screen.</summary>
    private static string Name(Control c)
    {
        var text = c switch
        {
            TextBoxBase or NumericUpDown or ComboBox or ListView or DataGridView or TrackBar => c.Name,
            _ => c.Text,
        };
        // a field without a name is usually labelled by the control placed right before it ("Start (s)", "Size %")
        if (string.IsNullOrWhiteSpace(text) && c.Parent is { } parent)
        {
            int at = parent.Controls.IndexOf(c);
            if (at > 0 && parent.Controls[at - 1] is Label label && !string.IsNullOrWhiteSpace(label.Text)) text = label.Text.TrimEnd(':');
        }
        if (string.IsNullOrWhiteSpace(text) && c is ListView { Columns.Count: > 0 } list) text = "list " + list.Columns[0].Text;
        if (string.IsNullOrWhiteSpace(text) && c is DataGridView { Columns.Count: > 0 } g) text = "table " + g.Columns[0].HeaderText;
        if (string.IsNullOrWhiteSpace(text)) text = c.Name;
        if (string.IsNullOrWhiteSpace(text)) text = c.AccessibleName;
        if (string.IsNullOrWhiteSpace(text)) text = c.GetType().Name;
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 60 ? text[..60] + "..." : text;
    }
}
