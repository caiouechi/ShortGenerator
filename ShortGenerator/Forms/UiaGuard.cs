using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ShortGenerator.Forms;

/// <summary>
/// Keeps UI Automation clients from stopping the app under a debugger. WinForms answers WM_GETOBJECT for UI
/// Automation by calling UiaReturnRawElementProvider, which calls out over COM in the middle of an input-synchronous
/// message; COM raises RPC_E_CANTCALLOUT_ININPUTSYNCCALL (0x8001010D) inside and recovers on its own, but with a
/// debugger attached the raise surfaces as "External component has thrown an exception" (Visual Studio itself is a
/// UI Automation client, and asks every window of the app when a dialog opens). This guard declines the UI Automation
/// request on every window the app creates, so clients use the MSAA path instead, which has no such call-out.
/// SHORTGEN_UIA=1 leaves UI Automation on. The browser's own windows are left alone: WebView2 answers their
/// accessibility queries with the same call-out, but entirely in native code, where a debugger ignores it; a managed
/// window procedure in front of theirs would turn the C++ exceptions WebView2 throws and catches internally into
/// "External component has thrown an exception" (seen in Visual Studio).
/// </summary>
internal static class UiaGuard
{
    private const int WM_GETOBJECT = 0x003D, UiaRootObjectId = -25, WH_CALLWNDPROC = 4;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static HookProc? _hook; // kept alive for the process lifetime
    private static IntPtr _hookHandle;
    private static readonly ConditionalWeakTable<Control, Guard> _guarded = new();

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Guards every control of the UI thread from its first UI Automation request on. The hook sees each message
    /// sent to a window of this thread before its window procedure does; on a UI Automation root request for a
    /// control that is not guarded yet, the guard is put in place right there, so that very request is declined.
    /// (Guarding on window creation instead would leave a gap: a client can ask before the thread's message loop
    /// gets to run anything posted.) Drop-downs and other windows outside a form are covered the same way.
    /// </summary>
    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("SHORTGEN_UIA") == "1") return;
        _hook = (code, wParam, lParam) =>
        {
            // a hook procedure is called from native code: an exception escaping it would end the process
            try
            {
                if (code >= 0)
                {
                    // CWPSTRUCT: lParam, wParam, message, hwnd
                    int msg = Marshal.ReadInt32(lParam, 2 * IntPtr.Size);
                    if (msg == WM_GETOBJECT && unchecked((int)(long)Marshal.ReadIntPtr(lParam)) == UiaRootObjectId)
                    {
                        var hwnd = Marshal.ReadIntPtr(lParam, 2 * IntPtr.Size + 8);
                        if (Control.FromHandle(hwnd) is { } control) Protect(control);
                    }
                }
            }
            catch (Exception ex) { Services.AppLog.Error("UiaGuard hook", ex); }
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        };
        _hookHandle = SetWindowsHookExW(WH_CALLWNDPROC, _hook, IntPtr.Zero, GetCurrentThreadId());
    }

    /// <summary>Guards a control, the controls inside it, and any added later.</summary>
    public static void Protect(Control control)
    {
        if (_guarded.TryGetValue(control, out _)) return;
        var guard = new Guard();
        _guarded.Add(control, guard);
        // the guard may be asked for while the handle is still being created (a client asks as soon as the window
        // exists), so HandleCreated can follow for the same handle: never assign twice
        void Assign() { if (guard.Handle != control.Handle) { if (guard.Handle != IntPtr.Zero) guard.ReleaseHandle(); guard.AssignHandle(control.Handle); } }
        if (control.IsHandleCreated) Assign();
        control.HandleCreated += (_, _) => Assign();
        control.HandleDestroyed += (_, _) => { if (guard.Handle != IntPtr.Zero) guard.ReleaseHandle(); };
        control.ControlAdded += (_, e) => Protect(e.Control);
        foreach (Control child in control.Controls) Protect(child);
    }

    /// <summary>Sits in front of the control's own window procedure and declines the UI Automation root request.</summary>
    private sealed class Guard : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETOBJECT && unchecked((int)(long)m.LParam) == UiaRootObjectId) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
    }
}
