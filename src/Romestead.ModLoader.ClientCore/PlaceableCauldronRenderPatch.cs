using Candide.Entities.Controllers.Other;
using HarmonyLib;
using Shared.Entity.Base;

namespace Romestead.ModLoader.ClientCore;

[HarmonyPatch(typeof(CauldronController), nameof(CauldronController.BeforeRender))]
internal static class PlaceableCauldronRenderPatch
{
    private static bool Prefix(CauldronController __instance)
    {
        var entity = ((AbstractController)__instance).Entity;
        return entity is null || !ModPlaceableBenchHost.IsCustomPlaceable(entity.BaseGuid);
    }
}
