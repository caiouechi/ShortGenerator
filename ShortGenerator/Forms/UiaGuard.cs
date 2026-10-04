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
    private const int WM_GETOBJECT = 0x003D, UiaRootObjectId = -25, WH_CBT = 5, HCBT_CREATEWND = 3;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static HookProc? _hook; // kept alive for the process lifetime
    private static IntPtr _hookHandle;
    private static readonly ConditionalWeakTable<Control, Guard> _guarded = new();

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    /// <summary>Guards every form the UI thread creates from now on, and their controls.</summary>
    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("SHORTGEN_UIA") == "1") return;
        var sync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _hook = (code, wParam, lParam) =>
        {
            if (code == HCBT_CREATEWND)
            {
                var hwnd = wParam;
                // the control is wired to its handle once CreateWindow returns: look it up on the next message
                sync.Post(_ =>
                {
                    if (!IsWindow(hwnd)) return;
                    if (Control.FromHandle(hwnd) is Form form) Protect(form);
                }, null);
            }
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        };
        _hookHandle = SetWindowsHookExW(WH_CBT, _hook, IntPtr.Zero, GetCurrentThreadId());
    }

    /// <summary>Guards a control, the controls inside it, and any added later.</summary>
    public static void Protect(Control control)
    {
        if (_guarded.TryGetValue(control, out _)) return;
        var guard = new Guard();
        _guarded.Add(control, guard);
        if (control.IsHandleCreated) guard.AssignHandle(control.Handle);
        control.HandleCreated += (_, _) => guard.AssignHandle(control.Handle);
        control.HandleDestroyed += (_, _) => guard.ReleaseHandle();
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
