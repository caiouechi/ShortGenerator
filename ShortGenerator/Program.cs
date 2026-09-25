using ShortGenerator.Forms;

namespace ShortGenerator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        var form = new MainForm();
        // Dev convenience: "ShortGenerator.exe --tab 3" opens on a given step (0-based).
        var args = Environment.GetCommandLineArgs();
        int tabArg = Array.IndexOf(args, "--tab");
        int openArg = Array.IndexOf(args, "--open");
        form.Shown += async (_, _) =>
        {
            // "--open <file>" loads a local video at startup (dev convenience), then "--tab N" selects a step.
            if (openArg >= 0 && openArg + 1 < args.Length && File.Exists(args[openArg + 1]))
                await form.OpenLocalFileAsync(args[openArg + 1]);
            if (tabArg >= 0 && tabArg + 1 < args.Length && int.TryParse(args[tabArg + 1], out var tab))
                form.SelectTab(tab);
        };
        Application.Run(form);
    }
}
