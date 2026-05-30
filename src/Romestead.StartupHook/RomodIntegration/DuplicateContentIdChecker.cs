using Romestead.ModLoader;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Tracks the (content-type, id) pairs registered so far and reports any
/// collision before the duplicate hits <see cref="IContentRegistry"/> —
/// where it would silently lose. Used by both the romod loading path and
/// the C# mod path so cross-source duplicates surface as errors.
///
/// Icon duplicates are allowed when the second registration has
/// <c>ReplaceExisting = true</c>, mirroring the existing icon DataBase
/// semantics.
/// </summary>
internal sealed class DuplicateContentIdChecker
{
    private readonly Dictionary<(string Kind, string Id), Source> _owned = new();

    public sealed record Source(string ModId, string FilePath);

    public sealed record Conflict(
        string Kind,
        string Id,
        Source First,
        Source Second);

    public bool TryClaim(string kind, string id, Source source, bool allowReplaceExisting, out Conflict? conflict)
    {
        var key = (kind, id);
        if (_owned.TryGetValue(key, out var existing))
        {
            if (allowReplaceExisting)
            {
                _owned[key] = source;
                conflict = null;
                return true;
            }

            conflict = new Conflict(kind, id, existing, source);
            return false;
        }

        _owned[key] = source;
        conflict = null;
        return true;
    }

    public static string FormatConflictMessage(Conflict c)
    {
        return $"Duplicate {c.Kind} id '{c.Id}'.{Environment.NewLine}" +
            $"Defined by:{Environment.NewLine}" +
            $"- {c.First.ModId} {c.First.FilePath}{Environment.NewLine}" +
            $"- {c.Second.ModId} {c.Second.FilePath}";
    }
}
