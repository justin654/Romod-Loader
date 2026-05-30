namespace Romestead.RomodFormat;

/// <summary>
/// Minimal logging sink used by the package format code so it does not have to
/// take a dependency on Romestead.ModLoader.IModLogger. Both the runtime
/// loader (which wraps a real <c>IModLogger</c>) and the CLI tool (which
/// wraps stdout/stderr) supply their own implementation.
/// </summary>
public interface IRomodLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>
/// Sink that drops every message. Useful for tests and for the
/// validate-without-noise path.
/// </summary>
public sealed class NullRomodLog : IRomodLog
{
    public static readonly NullRomodLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}

/// <summary>
/// Buffers messages in memory; used by the CLI for ordered output and by
/// validation to capture all diagnostics.
/// </summary>
public sealed class CollectingRomodLog : IRomodLog
{
    private readonly List<(string Level, string Message)> _messages = new();
    public IReadOnlyList<(string Level, string Message)> Messages => _messages;
    public void Info(string message) => _messages.Add(("INFO", message));
    public void Warn(string message) => _messages.Add(("WARN", message));
    public void Error(string message) => _messages.Add(("ERROR", message));
}
