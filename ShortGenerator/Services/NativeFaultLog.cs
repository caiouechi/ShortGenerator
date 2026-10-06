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

    [DllImport("kernel32.dll")] private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);
    [DllImport("kernel32.dll")] private static extern void GetCurrentThreadStackLimits(out UIntPtr lowLimit, out UIntPtr highLimit);

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
                try { LogFirstChance(e.Exception); } catch { }
            };
        if (!on) return;
        _uiThread = GetCurrentThreadId();
        _handler = Handler;
        AddVectoredExceptionHandler(0, _handler);
    }

    private static void LogFirstChance(Exception ex)
    {
        {
            {
                // the kinds a debugger stops on are always written; the rest a few times per type, so routine handled
                // ones (cancellations, network drops) cannot push the interesting one out
                bool always = ex is System.Runtime.InteropServices.ExternalException || ex is AccessViolationException;
                if (!always && CountOf(ex.GetType().FullName!) > 5) return;
                string code = ex is System.Runtime.InteropServices.ExternalException ee ? $" (code 0x{ee.ErrorCode:X8})" : "";
                AppLog.Write($"FIRST-CHANCE {ex.GetType().FullName}{code}: {ex.Message}{Environment.NewLine}{ex.StackTrace ?? Environment.StackTrace}");
            }
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();
    private static int CountOf(string key) => _counts.AddOrUpdate(key, 1, (_, n) => n + 1);

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
            // only within this thread's stack: reading past its top is an access violation, which would end the process
            GetCurrentThreadStackLimits(out var low, out var high);
            if (rsp < (long)low || rsp >= (long)high) return "?";
            var parts = new List<string>();
            for (int i = 0; i < 4096; i++)
            {
                long slot = rsp + i * 8;
                if (slot + 8 > (long)high) break;
                IntPtr value = Marshal.ReadIntPtr(new IntPtr(slot));
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

    [ThreadStatic] private static bool t_inHandler;

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static uint _uiThread;

    private static int Handler(IntPtr pointers)
    {
        // Only the UI thread is watched: that is where a native raise becomes "External component has thrown an
        // exception". Worker threads of UI Automation and RPC raise and catch C++ exceptions of their own all the
        // time, and taking a managed stack trace there, in the middle of their unwinding, is not safe.
        bool ui = GetCurrentThreadId() == _uiThread;
        if (t_inHandler) return 0; // an exception raised while logging one (walking a stack can) is not logged again
        t_inHandler = true;
        try
        {
            var record = Marshal.ReadIntPtr(pointers);
            uint code = unchecked((uint)Marshal.ReadInt32(record));
            switch (code)
            {
                case 0xE0434352: // a managed throw (the first-chance log has it)
                case 0x406D1388: // thread naming
                case 0x40010006: case 0x4001000A: // OutputDebugString
                case 0x80000003: case 0x80000004: // breakpoint, single step (the debugger)
                    return 0;
                case 0xE06D7363 when !ui: // C++ exceptions are routine on the UI Automation and RPC worker threads
                    return 0;
            }
            // every other code is written a few times: C++ exceptions and RPC errors are handled inside their
            // component most of the time, but one that crosses a managed frame becomes an SEHException, so none is skipped
            if (CountOf($"native {code:X8}") > 6) return 0;
            // EXCEPTION_RECORD: code, flags, nested record, address
            var address = Marshal.ReadIntPtr(record, 8 + IntPtr.Size);
            string where = "no module (a stub or jitted code)";
            if (GetModuleHandleExW(FromAddress | UnchangedRefCount, address, out var module))
            {
                var name = new StringBuilder(260);
                GetModuleFileNameW(module, name, 260);
                where = Path.GetFileName(name.ToString());
            }
            string kind = code == 0xE06D7363 ? "C++ exception" : $"exception 0x{code:X8}";
            // on a worker thread only the module scan (plain memory reads inside that thread's stack): a managed stack
            // trace in the middle of another component's unwinding is not safe there
            AppLog.Write($"NATIVE {kind} at 0x{address.ToInt64():X} in {where} (thread {Environment.CurrentManagedThreadId}{(ui ? ", UI" : "")}). Stack: {ModuleStack(pointers)}" +
                         (ui ? Environment.NewLine + Environment.StackTrace : ""));
        }
        catch { }
        finally { t_inHandler = false; }
        return 0; // EXCEPTION_CONTINUE_SEARCH: never changes what happens
    }
}
