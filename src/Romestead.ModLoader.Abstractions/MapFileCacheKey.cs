using System.Text.RegularExpressions;

namespace Romestead.ModLoader;

/// <summary>
/// Validates normalized map ids and builds safe relative map file paths (preserves folder structure).
/// </summary>
public static partial class MapFileCacheKey
{
    private static readonly HashSet<string> ReservedSegmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    public static bool TryValidateNormalizedMapId(string normalizedMapId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(normalizedMapId))
        {
            error = "map id is empty";
            return false;
        }

        if (normalizedMapId.Contains('\\', StringComparison.Ordinal))
        {
            error = "map id must use forward slashes";
            return false;
        }

        if (normalizedMapId.StartsWith("/", StringComparison.Ordinal) ||
            normalizedMapId.EndsWith("/", StringComparison.Ordinal))
        {
            error = "map id must not start or end with '/'";
            return false;
        }

        var segments = normalizedMapId.Split('/');
        foreach (var segment in segments)
        {
            if (!IsValidSegment(segment, out error))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryBuildCacheRelativePath(
        string normalizedMapId,
        string extension,
        out string relativePath,
        out string? error)
    {
        relativePath = string.Empty;
        if (!TryValidateNormalizedMapId(normalizedMapId, out error))
        {
            return false;
        }

        if (extension is not ".tmx" and not ".cmx")
        {
            error = $"unsupported map file extension '{extension}'";
            return false;
        }

        relativePath = normalizedMapId + extension;
        return true;
    }

    private static bool IsValidSegment(string segment, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(segment))
        {
            error = "map id contains an empty path segment";
            return false;
        }

        if (segment is "." or "..")
        {
            error = "map id must not contain '.' or '..' segments";
            return false;
        }

        if (segment.Length > 120)
        {
            error = "map id segment is too long";
            return false;
        }

        if (!ValidSegmentRegex().IsMatch(segment))
        {
            error = $"map id segment '{segment}' contains invalid characters (use a-z, 0-9, _, -, .)";
            return false;
        }

        if (ReservedSegmentNames.Contains(segment))
        {
            error = $"map id segment '{segment}' is reserved on Windows";
            return false;
        }

        if (segment.EndsWith(".", StringComparison.Ordinal) ||
            segment.EndsWith(" ", StringComparison.Ordinal))
        {
            error = "map id segment must not end with '.' or space";
            return false;
        }

        return true;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex ValidSegmentRegex();
}
