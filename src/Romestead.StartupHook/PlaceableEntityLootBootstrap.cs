using CandideServer;
using CandideServer.ServerManagers;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;
using Shared.Entity;

namespace Romestead.StartupHook;

[HarmonyPatch(typeof(LootServerManager), nameof(LootServerManager.DropGoodsAndInventory))]
internal static class PlaceableEntityLootBootstrap
{
    private static bool Prefix(EntityWrapper entity, int? owningPlayerId)
    {
        var placeable = FindPlaceable(entity.BaseGuid);
        if (placeable is null)
        {
            return true;
        }

        if (ServerGameState.Entities.TryGetValue(entity.Id, out var model) &&
            model.InventoryId.HasValue)
        {
            SharedContentBootstrap.Logger?.Warn(
                $"[placeable-loot] '{placeable.Id}' has an inventory; falling back to vanilla loot path.");
            return true;
        }

        WorldItemServerManager.SpawnNewItemAsWorldItem(
            placeable.Id,
            1,
            entity.Position + new Vector3(0f, 0f, 6f),
            Vector3.Zero,
            entity.WorldId,
            owningPlayerId,
            null,
            string.Empty,
            null);

        SharedContentBootstrap.Logger?.Info(
            $"[placeable-loot] Dropped custom placeable item '{placeable.Id}' for entity={entity.Id} base={entity.BaseGuid}.");
        return false;
    }

    private static ModPlaceableStation? FindPlaceable(Guid baseGuid)
    {
        foreach (var placeable in ModRegistries.Placeables.Pending)
        {
            if (placeable.DeriveDoodadGuid() == baseGuid)
            {
                return placeable;
            }
        }

        return null;
    }
}
