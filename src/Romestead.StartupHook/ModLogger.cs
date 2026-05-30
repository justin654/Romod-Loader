using Romestead.ModLoader;

namespace Romestead.StartupHook;

internal sealed class ModLogger : IModLogger
{
    private static readonly object FileLock = new();
    private static string? _logFilePath;

    private readonly string _prefix;

    public ModLogger(string prefix)
    {
        _prefix = prefix;
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var finalMessage = exception is null
            ? message
            : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", finalMessage);
    }

    public static void SetLogFile(string path)
    {
        _logFilePath = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{_prefix}] {message}";
        Console.WriteLine(line);

        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        lock (FileLock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }
}
