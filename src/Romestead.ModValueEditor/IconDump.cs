using System.Text.Json;

namespace Romestead.ModValueEditor;

/// <summary>
/// Reads the icon set exported by the in-game Romestead.IconDumpMod. Real item
/// icons can't be resolved outside the running game (the icon database needs a
/// live GraphicsDevice), so the mod dumps them once into this folder and the
/// editor just loads the PNGs.
/// </summary>
internal sealed class IconDump
{
    private readonly Dictionary<string, string> _byId;

    private IconDump(Dictionary<string, string> byId) => _byId = byId;

    public static string DumpDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Romestead.ModValueEditor", "icon-dump");

    private static string ManifestPath => Path.Combine(DumpDir, "manifest.json");

    public bool Available => _byId.Count > 0;
    public int Count => _byId.Count;

    public static IconDump Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(ManifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath));
                if (doc.RootElement.TryGetProperty("icons", out var icons) &&
                    icons.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in icons.EnumerateObject())
                    {
                        var file = entry.Value.GetString();
                        if (string.IsNullOrEmpty(file)) continue;
                        var full = Path.Combine(DumpDir, file);
                        if (File.Exists(full)) map[entry.Name] = full;
                    }
                }
            }
        }
        catch
        {
            // A malformed dump just means "no icons available" — not fatal.
        }

        return new IconDump(map);
    }

    public string? TryGetPath(string? itemId) =>
        !string.IsNullOrEmpty(itemId) && _byId.TryGetValue(itemId, out var path) ? path : null;
}
