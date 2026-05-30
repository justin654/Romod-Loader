using Tomlyn;
using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Manifest;

/// <summary>
/// Reads <c>romestead.mod.toml</c> into a <see cref="RomodManifest"/>.
/// </summary>
public static class RomodManifestParser
{
    private const string ManifestFileName = "romestead.mod.toml";

    private static readonly HashSet<string> KnownTopLevelKeys = new(StringComparer.Ordinal)
    {
        "id",
        "name",
        "version",
        "schemaVersion",
        "syncMode",
        "author",
        "description",
        "homepage",
        "dependencies"
    };

    public static RomodManifest Parse(string tomlText, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(tomlText);
        ArgumentNullException.ThrowIfNull(log);

        TomlTable root;
        try
        {
            root = Toml.ToModel(tomlText, sourcePath: ManifestFileName);
        }
        catch (Exception ex)
        {
            throw new RomodFormatException($"Failed to parse {ManifestFileName}: {ex.Message}", ex);
        }

        var id = TomlHelpers.RequireString(root, "id", ManifestFileName);
        var name = TomlHelpers.RequireString(root, "name", ManifestFileName);
        var version = TomlHelpers.RequireString(root, "version", ManifestFileName);

        var schemaVersion = TomlHelpers.GetIntOrDefault(root, "schemaVersion", ManifestFileName, Schema.RomodSchema.CurrentVersion);
        if (schemaVersion < 1)
        {
            throw new RomodFormatException($"[{id}] schemaVersion must be >= 1 (got {schemaVersion}).");
        }

        var syncMode = ParseSyncMode(root, id);

        var author = TomlHelpers.GetStringOrNull(root, "author", ManifestFileName);
        var description = TomlHelpers.GetStringOrNull(root, "description", ManifestFileName);
        var homepage = TomlHelpers.GetStringOrNull(root, "homepage", ManifestFileName);

        var dependencies = ParseDependencies(root, id);

        WarnUnknownTopLevelKeys(root, id, log);

        return new RomodManifest
        {
            Id = id,
            Name = name,
            Version = version,
            SchemaVersion = schemaVersion,
            SyncMode = syncMode,
            Author = author,
            Description = description,
            Homepage = homepage,
            Dependencies = dependencies
        };
    }

    private static RomodSyncMode ParseSyncMode(TomlTable root, string id)
    {
        var raw = TomlHelpers.GetStringOrNull(root, "syncMode", ManifestFileName);
        if (raw is null)
        {
            return RomodSyncMode.RequiredOnClient;
        }

        if (Enum.TryParse<RomodSyncMode>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var expected = string.Join(", ", Enum.GetNames<RomodSyncMode>());
        throw new RomodFormatException(
            $"[{id}] Invalid syncMode '{raw}' in {ManifestFileName}. Expected one of: {expected}.");
    }

    private static IReadOnlyList<RomodDependencyRequirement> ParseDependencies(TomlTable root, string id)
    {
        if (!root.TryGetValue("dependencies", out var raw))
        {
            return [];
        }

        if (raw is not TomlTable table)
        {
            throw new RomodFormatException(
                $"[{id}] [dependencies] in {ManifestFileName} must be a table " +
                $"of \"id\" = \"version-spec\" entries.");
        }

        var list = new List<RomodDependencyRequirement>(table.Count);
        foreach (var (key, valueObj) in table)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new RomodFormatException($"[{id}] Empty dependency id in {ManifestFileName}.");
            }

            if (valueObj is not string valueStr)
            {
                throw new RomodFormatException(
                    $"[{id}] Dependency '{key}' in {ManifestFileName} must be a string spec " +
                    $"(e.g. \">=1.0.0\" or \"*\").");
            }

            var minVersion = ParseDependencySpec(valueStr.Trim(), id, key);
            list.Add(new RomodDependencyRequirement(key, minVersion));
        }

        return list;
    }

    private static string? ParseDependencySpec(string spec, string id, string depId)
    {
        if (spec.Length == 0 || spec == "*")
        {
            return null;
        }

        if (spec.StartsWith(">=", StringComparison.Ordinal))
        {
            var version = spec[2..].Trim();
            if (version.Length == 0)
            {
                throw new RomodFormatException(
                    $"[{id}] Dependency '{depId}' has empty version after '>=' in {ManifestFileName}.");
            }

            return version;
        }

        // Bare version, treat as >=
        if (char.IsDigit(spec[0]))
        {
            return spec;
        }

        throw new RomodFormatException(
            $"[{id}] Unsupported dependency spec '{spec}' for '{depId}' in {ManifestFileName}. " +
            $"Supported forms: \"*\", \">=X.Y.Z\", or bare \"X.Y.Z\".");
    }

    private static void WarnUnknownTopLevelKeys(TomlTable root, string id, IRomodLog log)
    {
        foreach (var (key, _) in root)
        {
            if (!KnownTopLevelKeys.Contains(key))
            {
                log.Warn($"[{id}] Unknown field '{key}' in {ManifestFileName}. Ignoring.");
            }
        }
    }
}
