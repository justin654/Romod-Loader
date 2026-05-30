using CandideCreator.Shared.Models;
using CandideCreator.Shared.Serialization;
using CandideServer.Entities;
using CandideServer.ServerControllers;
using HarmonyLib;
using Romestead.ModLoader;
using Shared.Entity;

namespace Romestead.StartupHook;

[HarmonyPatch]
internal static class PlaceableServerDoodadBootstrap
{
    private static readonly Dictionary<VanillaBenchTemplate, Guid> TemplateGuids = new()
    {
        [VanillaBenchTemplate.Cauldron] = Guid.Parse("e293bda6-4b78-4cb8-8287-d91b6a9129dc"),
        [VanillaBenchTemplate.Campfire] = Guid.Parse("5a3eea41-dfc3-49d7-ace6-65e511ba4b0f"),
        [VanillaBenchTemplate.WarTable] = Guid.Parse("afb970c9-276f-45cf-ae70-2bb9e26138e7"),
    };

    private static readonly object Sync = new();
    private static readonly HashSet<string> Injected = new(StringComparer.Ordinal);

    private static CandideReflection? _serverReflection;
    private static bool _reentry;

    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.SetBaseDoodadData))]
    [HarmonyPostfix]
    private static void SetBaseDoodadDataPostfix(BaseDoodad baseDoodad, EntitySystem data)
    {
        EnsureInjected("server-base-data-load", baseDoodad.Guid, baseDoodad, data);
    }

    [HarmonyPatch(typeof(EntitySController), nameof(EntitySController.OnWorldGameStateLoaded))]
    [HarmonyPrefix]
    private static void OnWorldGameStateLoadedPrefix()
    {
        // This closes the timing gap where the template entity data already exists
        // before the mod finishes registering its placeables.
        EnsureInjected("world-load");
    }

    [HarmonyPatch(typeof(ServerEntitySystemManager), nameof(ServerEntitySystemManager.BeforeWorldGameStateLoaded))]
    [HarmonyPrefix]
    private static void BeforeWorldGameStateLoadedPrefix()
    {
        // Cold-load entity models are already deserialized by this point, but the
        // server entity systems have not yet been cleared/rebuilt. Register mod
        // base data here so custom placeables survive the world-load reset path.
        EnsureInjected("server-entity-world-load");
    }

    internal static void EnsureInjected(
        string reason,
        Guid? justLoadedTemplateGuid = null,
        BaseDoodad? justLoadedBaseDoodad = null,
        EntitySystem? justLoadedSystem = null)
    {
        if (ModRegistries.Placeables.Pending.Count == 0)
        {
            return;
        }

        lock (Sync)
        {
            if (_reentry)
            {
                return;
            }

            _reentry = true;
        }

        try
        {
            foreach (var placeable in ModRegistries.Placeables.Pending)
            {
                if (Injected.Contains(placeable.Id))
                {
                    continue;
                }

                var newGuid = placeable.DeriveDoodadGuid();
                if (ServerEntityDataManager.TryGetEntityBaseData(newGuid, out _))
                {
                    Injected.Add(placeable.Id);
                    continue;
                }

                if (!TemplateGuids.TryGetValue(placeable.Template, out var templateGuid))
                {
                    SharedContentBootstrap.Logger?.Warn(
                        $"[placeable-bootstrap] Unknown template {placeable.Template} for '{placeable.Id}'; skipping server base-data registration.");
                    continue;
                }

                var sourceBaseDoodad = justLoadedTemplateGuid == templateGuid
                    ? justLoadedBaseDoodad
                    : null;
                var sourceSystem = justLoadedTemplateGuid == templateGuid
                    ? justLoadedSystem
                    : null;

                if (sourceSystem is null &&
                    (!ServerEntityDataManager.EntitySystems.TryGetValue(templateGuid, out sourceSystem) || sourceSystem is null))
                {
                    continue;
                }

                if (!ServerEntityDataManager.TryGetEntityBaseData(templateGuid, out var templateWrapper) || templateWrapper is null)
                {
                    SharedContentBootstrap.Logger?.Warn(
                        $"[placeable-bootstrap] Template wrapper {templateGuid} missing while registering '{placeable.Id}' during {reason}; will retry.");
                    continue;
                }

                try
                {
                    var cloneBaseDoodad = CloneBaseDoodad(sourceBaseDoodad, newGuid, placeable.Id);
                    var cloneSystem = new EntitySystem();
                    EntityCopyHelper.CopySystem(sourceSystem, cloneSystem, GetServerReflection());

                    var cloneWrapper = cloneSystem.GetWrapper(templateWrapper.Eid);
                    if (cloneWrapper is null)
                    {
                        cloneWrapper = cloneSystem.EntityIdMap.Values.FirstOrDefault();
                    }

                    if (cloneWrapper is null)
                    {
                        SharedContentBootstrap.Logger?.Warn(
                            $"[placeable-bootstrap] Cloned server entity system for '{placeable.Id}' has no root wrapper; cannot register during {reason}.");
                        continue;
                    }

                    PlaceableEntityCloneIdentity.Apply(
                        cloneSystem,
                        cloneWrapper,
                        placeable,
                        newGuid,
                        templateGuid,
                        SharedContentBootstrap.Logger);
                    ServerEntityDataManager.SetBaseDoodadData(cloneBaseDoodad, cloneSystem);
                    Injected.Add(placeable.Id);

                    SharedContentBootstrap.Logger?.Info(
                        $"[placeable-bootstrap] Registered server base data for '{placeable.Id}' from template '{placeable.Template}' during {reason}. base={newGuid} station={placeable.StationId}");
                }
                catch (Exception ex)
                {
                    SharedContentBootstrap.Logger?.Error(
                        $"[placeable-bootstrap] Failed registering server base data for '{placeable.Id}' during {reason}.",
                        ex);
                }
            }
        }
        finally
        {
            lock (Sync)
            {
                _reentry = false;
            }
        }
    }

    private static BaseDoodad CloneBaseDoodad(BaseDoodad? source, Guid newGuid, string savedName)
    {
        var clone = source?.Clone(true) ?? new BaseDoodad();
        clone.Guid = newGuid;

        try
        {
            clone.SavedName = savedName;
        }
        catch
        {
            // SavedName is cosmetic for this registration path.
        }

        return clone;
    }

    private static CandideReflection GetServerReflection()
    {
        if (_serverReflection is not null)
        {
            return _serverReflection;
        }

        var reflection = new CandideReflection();
        ServerEntityDataManager.ConfigureReflection(reflection);
        _serverReflection = reflection;
        return reflection;
    }
}
