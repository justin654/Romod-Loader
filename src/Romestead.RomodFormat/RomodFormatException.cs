namespace Romestead.RomodFormat;

/// <summary>
/// Thrown by <see cref="Package.RomodPackageReader"/> and the validation
/// pipeline for any error the package author can fix. The message is meant
/// to be shown to humans and always includes the package id plus the
/// offending file path inside the archive when possible.
/// </summary>
public sealed class RomodFormatException : Exception
{
    public RomodFormatException(string message) : base(message) { }
    public RomodFormatException(string message, Exception inner) : base(message, inner) { }
}
