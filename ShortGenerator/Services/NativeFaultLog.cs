using System.Runtime.InteropServices;
using System.Text;

namespace ShortGenerator.Services;

/// <summary>
/// Diagnostics for "External component has thrown an exception" (SEHException): a native exception inside a window
/// message, whose source the .NET exception does not tell. While a debugger is attached (or SHORTGEN_NATIVE_LOG=1),
/// a vectored exception handler writes the exception code and the module it came from to the app log, so the next
/// one names its culprit. Exceptions the runtime raises for itself (managed throws, C++ exceptions, debugger
/// notifications, faults in jitted code) are skipped at once. It never handles anything: it only looks.
/// </summary>
public static class NativeFaultLog
{
    private delegate int VectoredHandler(IntPtr exceptionPointers);
    private static VectoredHandler? _handler;  // kept alive for the process lifetime
    private static int _logged;

    [DllImport("kernel32.dll")] private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);

    private const uint FromAddress = 0x4, UnchangedRefCount = 0x2;

    public static void InstallIfDebugging()
    {
        bool on = System.Diagnostics.Debugger.IsAttached || Environment.GetEnvironmentVariable("SHORTGEN_NATIVE_LOG") == "1";
        AppLog.Write($"Debugger attached: {System.Diagnostics.Debugger.IsAttached}." + (on ? " Native fault logging is on." : ""));
        // Under a debugger WinForms checks cross-thread calls and lets exceptions inside window messages escape
        // through user32 (they surface as SEHException 0xC000041D). SHORTGEN_STRICT=1 turns the same checks on
        // without a debugger, so the bug can be reproduced; first-chance managed exceptions are then logged too.
        bool strict = Environment.GetEnvironmentVariable("SHORTGEN_STRICT") == "1";
        if (strict) System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls = true;
        if (on || strict)
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                var ex = e.Exception;
                if (ex is OperationCanceledException || ex is System.IO.IOException) return; // expected and handled
                if (Interlocked.Increment(ref _managed) > 60) return;
                AppLog.Write($"FIRST-CHANCE {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{Environment.StackTrace}");
            };
        if (!on) return;
        _handler = Handler;
        AddVectoredExceptionHandler(0, _handler);
    }

    private static int _managed;

    /// <summary>
    /// The modules on the native stack at the time of the exception, innermost first, repeats collapsed: who raised,
    /// and who called them. The stack is scanned from the faulting frame's stack pointer for return addresses that
    /// fall inside a loaded module (a heuristic walk, which needs no symbols and cannot fail).
    /// </summary>
    private static string ModuleStack(IntPtr pointers)
    {
        try
        {
            var context = Marshal.ReadIntPtr(pointers, IntPtr.Size);
            if (context == IntPtr.Zero || IntPtr.Size != 8) return "?";
            long rsp = Marshal.ReadInt64(context, 0x98); // CONTEXT.Rsp on x64
            var parts = new List<string>();
            for (int i = 0; i < 4096; i++)
            {
                IntPtr slot = new(rsp + i * 8);
                IntPtr value;
                try { value = Marshal.ReadIntPtr(slot); } catch { break; }
                if (value == IntPtr.Zero || !GetModuleHandleExW(FromAddress | UnchangedRefCount, value, out var h)) continue;
                var sb = new StringBuilder(260);
                GetModuleFileNameW(h, sb, 260);
                string m = Path.GetFileName(sb.ToString());
                if (parts.Count == 0 || parts[^1] != m) parts.Add(m);
                if (parts.Count > 40) break;
            }
            return string.Join(" > ", parts);
        }
        catch { return "?"; }
    }

    private static int Handler(IntPtr pointers)
    {
        try
        {
            var record = Marshal.ReadIntPtr(pointers);
            uint code = unchecked((uint)Marshal.ReadInt32(record));
            switch (code)
            {
                case 0xE0434352: // a managed throw
                case 0xE06D7363: // a C++ exception (handled inside its component)
                case 0x406D1388: // thread naming
                case 0x40010006: case 0x4001000A: // OutputDebugString
                case 0x80000003: case 0x80000004: // breakpoint, single step (the debugger)
                case 0x000006BA: case 0x000006A6: case 0x000006D9: // RPC chatter
                    return 0;
            }
            // EXCEPTION_RECORD: code, flags, nested record, address
            var address = Marshal.ReadIntPtr(record, 8 + IntPtr.Size);
            // a fault in jitted code (a NullReferenceException, say) has no module: the runtime turns it into a managed exception
            if (!GetModuleHandleExW(FromAddress | UnchangedRefCount, address, out var module)) return 0;
            if (Interlocked.Increment(ref _logged) > 40) return 0; // enough to name the culprit
            var name = new StringBuilder(260);
            GetModuleFileNameW(module, name, 260);
            AppLog.Write($"NATIVE exception 0x{code:X8} at 0x{address.ToInt64():X} in {Path.GetFileName(name.ToString())} (thread {Environment.CurrentManagedThreadId}). Stack: {ModuleStack(pointers)}{Environment.NewLine}{Environment.StackTrace}");
        }
        catch { }
        return 0; // EXCEPTION_CONTINUE_SEARCH: never changes what happens
    }
}
