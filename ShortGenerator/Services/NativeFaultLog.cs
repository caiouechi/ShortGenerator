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
        if (!on) return;
        _handler = Handler;
        AddVectoredExceptionHandler(0, _handler);
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
            AppLog.Write($"NATIVE exception 0x{code:X8} at 0x{address.ToInt64():X} in {name} (thread {Environment.CurrentManagedThreadId}).");
        }
        catch { }
        return 0; // EXCEPTION_CONTINUE_SEARCH: never changes what happens
    }
}
