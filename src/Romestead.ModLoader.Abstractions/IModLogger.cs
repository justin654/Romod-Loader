namespace Romestead.ModLoader;

public interface IModLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
