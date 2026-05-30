using Candide.GameModels.Controllers;
using CandideServer.Entities;
using HarmonyLib;
using Shared.Entity;

namespace Romestead.ModLoader.ClientCore;

[HarmonyPatch(typeof(EntityController), nameof(EntityController.SpawnEntity))]
internal static class PlaceableEntityArtPatch
{
    private static void Postfix(ServerEntityModel model, EntityWrapper __result)
    {
        if (__result is null || CoreState.Logger is not { } log)
        {
            return;
        }

        ModPlaceableBenchHost.ApplyCustomArtToSpawnedEntity(model.BaseId, __result, log);
    }
}

[HarmonyPatch(typeof(EntityController), nameof(EntityController.SpawnEntityBase))]
internal static class PlaceableEntityBaseArtPatch
{
    private static void Postfix(Guid baseId, EntityWrapper __result)
    {
        if (__result is null || CoreState.Logger is not { } log)
        {
            return;
        }

        ModPlaceableBenchHost.ApplyCustomArtToSpawnedEntity(baseId, __result, log);
    }
}
