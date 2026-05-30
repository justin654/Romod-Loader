using CandideServer.ServerManagers;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;
using Shared.Data;
using Shared.Data.Decorations;
using Shared.Models.Construction;
using Shared.Models.Stats;

namespace Romestead.StartupHook;

/// <summary>
/// Keeps custom placeables on the game's decoration-backed persistence path.
///
/// We intentionally do not change the client-facing construction shell that the
/// placeable item references. Instead, we inject a decoration record for each
/// custom placeable and route the server spawn path through
/// <see cref="DecorationsServerManager.TrySpawnNewDecorationConstruction"/> so
/// the world save contains the decoration instance the cold-load path expects.
/// </summary>
[HarmonyPatch]
internal static class PlaceableDecorationBootstrap
{
    private static bool _addDataReentry;
    private static bool _spawnRouting;
    private static readonly HashSet<string> InjectedDecorations = new(StringComparer.Ordinal);

    [HarmonyPatch(typeof(DecorationsDataBase), nameof(DecorationsDataBase.AddData))]
    [HarmonyPostfix]
    private static void AddDecorationDataPostfix()
    {
        if (_addDataReentry)
        {
            return;
        }

        var pending = ModRegistries.Placeables.Pending;
        if (pending.Count == 0)
        {
            return;
        }

        var toAdd = new List<DecorationData>();
        foreach (var placeable in pending)
        {
            if (InjectedDecorations.Contains(placeable.Id))
            {
                continue;
            }

            if (DecorationsDataBase.GetDecorationOrNull(placeable.DecorationId) is not null)
            {
                InjectedDecorations.Add(placeable.Id);
                continue;
            }

            toAdd.Add(new DecorationData
            {
                Id = placeable.DecorationId,
                SpawnedEntityGuid = placeable.DeriveDoodadGuid(),
                Radius = 0f,
                BuildingAppeal = 0f,
                CitizenStatBonuses = new Dictionary<string, StatModificationData>(StringComparer.Ordinal)
            });
            InjectedDecorations.Add(placeable.Id);
        }

        if (toAdd.Count == 0)
        {
            return;
        }

        _addDataReentry = true;
        try
        {
            DecorationsDataBase.AddData(toAdd);
        }
        finally
        {
            _addDataReentry = false;
        }
    }

    [HarmonyPatch(typeof(ConstructionsServerManager), nameof(ConstructionsServerManager.SpawnConstruction), new[]
    {
        typeof(ConstructionModel),
        typeof(Rectangle),
        typeof(Guid),
        typeof(Guid),
        typeof(Dictionary<string, string>),
        typeof(Guid?),
        typeof(Guid?),
        typeof(Guid?)
    })]
    [HarmonyPrefix]
    private static bool SpawnConstructionPrefix(
        ConstructionModel construction,
        Rectangle tileBounds,
        Guid worldId,
        Guid spawnId,
        Dictionary<string, string>? parameters,
        Guid? constructionSiteId,
        Guid? characterId,
        Guid? townId)
    {
        if (_spawnRouting)
        {
            return true;
        }

        var placeable = FindTrackedPlaceable(construction.Id);
        if (placeable is null)
        {
            return true;
        }

        if (DecorationsDataBase.GetDecorationOrNull(placeable.DecorationId) is null)
        {
            SharedContentBootstrap.Logger?.Warn(
                $"[placeable-bootstrap] Missing decoration data '{placeable.DecorationId}' for '{placeable.Id}'; falling back to vanilla entity spawn.");
            return true;
        }

        var mappedConstruction = ConstructionDataBase.GetConstructionOrNull(construction.Id) ?? construction;
        var originalSpawnedId = mappedConstruction.SpawnedId;
        var originalConstructionSpawnedId = construction.SpawnedId;

        try
        {
            _spawnRouting = true;
            mappedConstruction.SpawnedId = placeable.DecorationId;
            construction.SpawnedId = placeable.DecorationId;

            var ok = DecorationsServerManager.TrySpawnNewDecorationConstruction(
                construction.Id,
                spawnId,
                tileBounds,
                constructionSiteId,
                characterId);

            if (!ok)
            {
                SharedContentBootstrap.Logger?.Warn(
                    $"[placeable-bootstrap] Decoration spawn path rejected '{placeable.Id}' at {tileBounds} in world {worldId}; falling back to vanilla entity spawn.");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            SharedContentBootstrap.Logger?.Error(
                $"[placeable-bootstrap] Decoration spawn routing failed for '{placeable.Id}'; falling back to vanilla entity spawn.",
                ex);
            return true;
        }
        finally
        {
            mappedConstruction.SpawnedId = originalSpawnedId;
            construction.SpawnedId = originalConstructionSpawnedId;
            _spawnRouting = false;
        }
    }

    private static ModPlaceableStation? FindTrackedPlaceable(string constructionId)
    {
        foreach (var placeable in ModRegistries.Placeables.Pending)
        {
            if (string.Equals(placeable.ConstructionId, constructionId, StringComparison.Ordinal))
            {
                return placeable;
            }
        }

        return null;
    }
}
