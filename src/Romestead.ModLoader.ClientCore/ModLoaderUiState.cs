using System.Text.Json;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

internal static class ModLoaderUiState
{
    public static IReadOnlyList<LoadedModInfo> GetKnownMods()
    {
        var mods = new List<LoadedModInfo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ModRegistries.LoadedMods.Mods)
        {
            if (ids.Add(mod.Id))
            {
                mods.Add(mod);
            }
        }

        foreach (var mod in ModRegistries.Diagnostics.SkippedMods)
        {
            if (ids.Add(mod.Id))
            {
                mods.Add(new LoadedModInfo(mod.Id, mod.Name, mod.Version, mod.AssemblyPath, mod.SyncMode));
            }
        }

        foreach (var mod in ModRegistries.Diagnostics.FailedMods)
        {
            if (ids.Add(mod.Id))
            {
                mods.Add(new LoadedModInfo(mod.Id, mod.Name, mod.Version, mod.AssemblyPath, mod.SyncMode));
            }
        }

        return mods;
    }

    public static void WriteLoaderConfig(IEnumerable<string> disabledModIds)
    {
        try
        {
            var normalized = disabledModIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllText(
                ModRegistries.Diagnostics.ConfigPath,
                JsonSerializer.Serialize(
                    new
                    {
                        disabledMods = normalized
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            ModRegistries.Diagnostics.SetDisabledModIds(normalized);
            CoreState.Logger?.Info($"Updated mod config: {ModRegistries.Diagnostics.ConfigPath}");
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to update mod config.", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo("mods.json", ex.Message));
        }
    }
}
