using System.Windows.Forms;

namespace Romestead.ModValueEditor;

internal static class Program
{
    public static string AppDataDir { get; } = ResolveAppDataDir();
    public static string CrashLogPath { get; } = Path.Combine(AppDataDir, "crash.log");

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => WriteCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static string ResolveAppDataDir()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, "Romestead.ModValueEditor");
        try { Directory.CreateDirectory(dir); } catch { dir = Path.GetTempPath(); }
        return dir;
    }

    private static void WriteCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:o}] {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}\r\n\r\n");
        }
        catch { /* best effort */ }
    }
}
