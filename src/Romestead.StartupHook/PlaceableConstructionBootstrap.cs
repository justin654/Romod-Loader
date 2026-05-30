using HarmonyLib;
using Romestead.ModLoader;
using Shared.Data;
using Shared.Models.Construction;
using Shared.Text;

namespace Romestead.StartupHook;

/// <summary>
/// Injects a construction for each registered placeable bench by cloning a
/// vanilla player-placeable construction shell (for placement/UI behaviour) and
/// re-pointing its <see cref="ConstructionModel.SpawnedId"/> at the placeable's
/// deterministic doodad guid. At runtime the server routes these custom
/// constructions through the synthesized decoration record for persistence, then
/// rebuilds the doodad/entity from that saved record after a cold restart.
///
/// This keeps the player-facing placement semantics close to vanilla while the
/// fragile persistence plumbing stays centralized in the loader.
///
/// We patch as a postfix and retry across <see cref="ConstructionDataBase.AddConstructions"/>
/// calls until the vanilla template is present in the DataMap, so we never depend
/// on which batch the template lands in. Re-entry is guarded because we add our
/// clones by calling AddConstructions again.
/// </summary>
[HarmonyPatch(typeof(ConstructionDataBase), nameof(ConstructionDataBase.AddConstructions))]
internal static class ConstructionDataBaseAddConstructionsPatch
{
    private static bool _reentry;
    private static readonly HashSet<string> _injected = new(StringComparer.Ordinal);

    private static void Postfix()
    {
        if (_reentry)
        {
            return;
        }

        var pending = ModRegistries.Placeables.Pending;
        if (pending.Count == 0)
        {
            return;
        }

        var toAdd = new List<ConstructionModel>();
        foreach (var p in pending)
        {
            if (_injected.Contains(p.Id))
            {
                continue;
            }

            if (ConstructionDataBase.GetConstructionOrNull(p.ConstructionId) is not null)
            {
                _injected.Add(p.Id);
                continue;
            }

            var templateId = TemplateConstructionId(p.Template);
            var template = ConstructionDataBase.GetConstructionOrNull(templateId);
            if (template is null)
            {
                // Vanilla template not loaded in this batch; try again next call.
                continue;
            }

            toAdd.Add(CloneConstruction(template, p));
            _injected.Add(p.Id);
            SharedContentBootstrap.Logger?.Info(
                $"[modloader] Placeable construction '{p.ConstructionId}' cloned from '{templateId}' type={template.Type} spawned='{template.SpawnedId}' -> type={template.Type} spawned='{p.DeriveDoodadGuid()}'.");
        }

        if (toAdd.Count == 0)
        {
            return;
        }

        _reentry = true;
        try
        {
            ConstructionDataBase.AddConstructions(toAdd);
        }
        finally
        {
            _reentry = false;
        }
    }

    private static string TemplateConstructionId(VanillaBenchTemplate template) => template switch
    {
        VanillaBenchTemplate.Campfire => "campfire:0",
        // The cauldron doodad is a good interaction/entity template, but
        // workbench:0 is the safer player-placeable construction shell.
        VanillaBenchTemplate.Cauldron => "workbench:0",
        // War table is a safer render/interaction template for custom stations,
        // but workbench:0 remains the normal player-placeable construction shell.
        VanillaBenchTemplate.WarTable => "workbench:0",
        _ => "workbench:0",
    };

    private static ConstructionModel CloneConstruction(ConstructionModel source, ModPlaceableStation p) => new()
    {
        Id = p.ConstructionId,
        Name = (StringId)$"{p.ConstructionId}*construction:name",
        Description = source.Description,
        RequiredConstructionProgress = source.RequiredConstructionProgress,
        IconId = p.IconId,
        PlaceSoundId = source.PlaceSoundId,
        ConstructSoundId = source.ConstructSoundId,
        BuildNearbyConstructions = source.BuildNearbyConstructions,
        OnlyOutside = source.OnlyOutside,
        UpgradeLevel = source.UpgradeLevel,
        UpgradeForId = source.UpgradeForId,
        TownDefenceValue = source.TownDefenceValue,
        SpawnInventory = source.SpawnInventory,
        SpawnInventoryParameters = source.SpawnInventoryParameters,
        Parameters = source.Parameters,
        Type = source.Type,
        ConstructionUiCategory = source.ConstructionUiCategory,
        SpawnedId = p.DeriveDoodadGuid().ToString(),
        SpawnOffset = source.SpawnOffset,
        RequiresTown = source.RequiresTown,
        ConstructionMaterialsRequirement = source.ConstructionMaterialsRequirement,
        ConstructionSpaceInfo = source.ConstructionSpaceInfo,
    };
}
