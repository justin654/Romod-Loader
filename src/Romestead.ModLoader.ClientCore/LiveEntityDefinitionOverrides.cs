using System.Text.Json;
using CandideServer.ServerControllers;
using HarmonyLib;

namespace Romestead.ModLoader.ClientCore;

internal static class LiveEntityDefinitionOverrides
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static Dictionary<Guid, EntityHealthOverride>? _healthByBaseId;

    internal static bool TryGetMaxHealth(Guid baseId, out float maxHealth)
    {
        EnsureLoaded();
        lock (Sync)
        {
            if (_healthByBaseId!.TryGetValue(baseId, out var rule))
            {
                maxHealth = rule.MaxHealth;
                return true;
            }
        }

        maxHealth = 0f;
        return false;
    }

    internal static void SetMaxHealth(Guid baseId, float maxHealth, IModLogger? log)
    {
        if (baseId == Guid.Empty)
        {
            return;
        }

        EnsureLoaded();
        lock (Sync)
        {
            _healthByBaseId![baseId] = new EntityHealthOverride
            {
                BaseId = baseId,
                MaxHealth = Math.Max(1f, maxHealth)
            };
            SaveLocked(log);
        }
    }

    internal static bool Clear(Guid baseId, IModLogger? log)
    {
        EnsureLoaded();
        lock (Sync)
        {
            var removed = _healthByBaseId!.Remove(baseId);
            if (removed)
            {
                SaveLocked(log);
            }

            return removed;
        }
    }

    private static void EnsureLoaded()
    {
        if (_healthByBaseId is not null)
        {
            return;
        }

        lock (Sync)
        {
            if (_healthByBaseId is not null)
            {
                return;
            }

            _healthByBaseId = new Dictionary<Guid, EntityHealthOverride>();
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var rules = JsonSerializer.Deserialize<List<EntityHealthOverride>>(File.ReadAllText(path), JsonOptions) ?? [];
                foreach (var rule in rules)
                {
                    if (rule.BaseId != Guid.Empty && rule.MaxHealth > 0f)
                    {
                        _healthByBaseId[rule.BaseId] = rule;
                    }
                }

                CoreState.Logger?.Info($"[entity-definitions] loaded {_healthByBaseId.Count} health override(s).");
            }
            catch (Exception ex)
            {
                CoreState.Logger?.Warn($"[entity-definitions] failed to load '{path}': {ex.Message}");
            }
        }
    }

    private static void SaveLocked(IModLogger? log)
    {
        var path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rules = _healthByBaseId!
            .Values
            .OrderBy(rule => rule.BaseId)
            .ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(rules, JsonOptions));
        log?.Info($"[entity-definitions] saved {rules.Count} health override(s) to {path}.");
    }

    private static string GetConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "romestead_modding", "config", "live-entity-definition-overrides.json");

    private sealed class EntityHealthOverride
    {
        public Guid BaseId { get; init; }
        public float MaxHealth { get; init; }
    }
}

[HarmonyPatch(typeof(EntitySController), nameof(EntitySController.SpawnEntity))]
internal static class EntitySControllerSpawnEntityDefinitionPatch
{
    private static void Prefix(SpawnEntityArgs args)
    {
        if (args.MaxHealth.HasValue)
        {
            return;
        }

        if (LiveEntityDefinitionOverrides.TryGetMaxHealth(args.BaseId, out var maxHealth))
        {
            args.MaxHealth = maxHealth;
        }
    }
}
