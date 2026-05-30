using Tomlyn.Model;

namespace Romestead.RomodFormat.Internal;

/// <summary>
/// Tiny boring helpers around <c>TomlTable</c>. Every error here is wrapped
/// in a <see cref="RomodFormatException"/> with the source path included so
/// the package author knows which file to edit.
/// </summary>
internal static class TomlHelpers
{
    public static string RequireString(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw))
        {
            throw new RomodFormatException($"{source}: required field '{key}' is missing.");
        }

        if (raw is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        throw new RomodFormatException($"{source}: required field '{key}' must be a non-empty string.");
    }

    public static string? GetStringOrNull(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is string s)
        {
            return string.IsNullOrEmpty(s) ? null : s;
        }

        throw new RomodFormatException($"{source}: field '{key}' must be a string.");
    }

    public static int GetIntOrDefault(TomlTable table, string key, string source, int defaultValue)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return raw switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new RomodFormatException($"{source}: field '{key}' must be an integer.")
        };
    }

    public static int? GetIntOrNull(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new RomodFormatException($"{source}: field '{key}' must be an integer.")
        };
    }

    public static int GetIntOrDefault(TomlTable table, string key, string source, int defaultValue, int min, int max)
    {
        var value = GetIntOrDefault(table, key, source, defaultValue);
        if (value < min || value > max)
        {
            throw new RomodFormatException($"{source}: field '{key}' must be in [{min}, {max}] (got {value}).");
        }

        return value;
    }

    public static float GetFloatOrDefault(TomlTable table, string key, string source, float defaultValue)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return raw switch
        {
            double d => (float)d,
            float f => f,
            long l => l,
            int i => i,
            _ => throw new RomodFormatException($"{source}: field '{key}' must be a number.")
        };
    }

    public static float? GetFloatOrNull(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            double d => (float)d,
            float f => f,
            long l => l,
            int i => i,
            _ => throw new RomodFormatException($"{source}: field '{key}' must be a number.")
        };
    }

    public static bool GetBoolOrDefault(TomlTable table, string key, string source, bool defaultValue)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return raw switch
        {
            bool b => b,
            _ => throw new RomodFormatException($"{source}: field '{key}' must be a boolean.")
        };
    }

    public static TomlTable? GetTableOrNull(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is TomlTable inner)
        {
            return inner;
        }

        throw new RomodFormatException($"{source}: field '{key}' must be a table.");
    }

    public static TomlTableArray? GetTableArrayOrNull(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is TomlTableArray array)
        {
            return array;
        }

        throw new RomodFormatException($"{source}: field '{key}' must be an array of tables ([[{key}]]).");
    }

    public static IReadOnlyList<string> GetStringArrayOrEmpty(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is TomlArray array)
        {
            var result = new string[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not string s)
                {
                    throw new RomodFormatException($"{source}: field '{key}[{i}]' must be a string.");
                }

                result[i] = s;
            }

            return result;
        }

        throw new RomodFormatException($"{source}: field '{key}' must be an array of strings.");
    }

    public static IReadOnlyList<int> GetIntArrayOrEmpty(TomlTable table, string key, string source)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is TomlArray array)
        {
            var result = new int[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = array[i] switch
                {
                    long l => checked((int)l),
                    int value => value,
                    _ => throw new RomodFormatException($"{source}: field '{key}[{i}]' must be an integer.")
                };
            }

            return result;
        }

        throw new RomodFormatException($"{source}: field '{key}' must be an array of integers.");
    }

    public static TEnum ParseEnum<TEnum>(string value, string fieldName, string source) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var expected = string.Join(", ", Enum.GetNames<TEnum>());
        throw new RomodFormatException(
            $"{source}: field '{fieldName}' has invalid value '{value}'. Expected one of: {expected}.");
    }

    public static void WarnUnknownKeys(
        TomlTable table,
        HashSet<string> knownKeys,
        string source,
        string packageId,
        IRomodLog log)
    {
        foreach (var (key, _) in table)
        {
            if (!knownKeys.Contains(key))
            {
                log.Warn($"[{packageId}] Unknown field '{key}' in {source}. Ignoring.");
            }
        }
    }
}
