using HarmonyLib;
using Romestead.ModLoader;
using Shared.Aura.Args;
using Shared.Combat;
using Shared.Combat.Spells.Parameters;
using Shared.Data;
using Shared.Data.DataModels;
using Shared.Models.Crafting;
using Shared.Models.Items;
using Shared.Models.Stats;
using Shared.Text;

namespace Romestead.StartupHook;

/// <summary>
/// Shared content injection that must run on both the client and the dedicated server.
/// Currently handles <see cref="ItemDataBase.AddItems"/> and <see cref="ItemRecipeDataBase.AddItemRecipes"/>
/// using strongly-typed constructors — no reflection.
/// </summary>
public static class SharedContentBootstrap
{
    private static readonly object Sync = new();
    private static bool _installed;
    private static IModLogger? _logger;
    private static bool _itemsInjected;
    private static bool _recipesInjected;
    private static bool _statsInjected;
    private static bool _stationsInjected;
    private static IReadOnlyList<ItemRecipe> _injectedRecipes = [];
    private static readonly Dictionary<string, ManaCostInfo> _manaCosts = new(StringComparer.Ordinal);

    public static IReadOnlyList<ItemRecipe> InjectedRecipes => _injectedRecipes;
    internal static IModLogger? Logger => _logger;

    /// <summary>
    /// Built-in debug wand item id. A usable item the client intercepts
    /// (see <c>DebugWandHost</c>) to open the in-game debug menu. Synthesized
    /// here so it exists on both client and server at content-load time.
    /// </summary>
    public const string DebugWandItemId = "debug:wand";

    /// <summary>
    /// Look up the mana cost of an item by id. Returns null if the item is
    /// not modded or has no mana cost. Populated during item injection.
    /// </summary>
    public static ManaCostInfo? GetManaCost(string itemId) =>
        _manaCosts.TryGetValue(itemId, out var cost) ? cost : null;

    internal static void Install(HostKind hostKind, IModLogger logger)
    {
        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            _logger = logger;
        }

        var harmony = new Harmony("romestead.startuphook.shared-content");
        var diagnosticHostKind = hostKind == HostKind.Client
            ? ModLoaderHostKind.Client
            : ModLoaderHostKind.DedicatedServer;
        PatchGroupInstaller.Install(
            logger,
            harmony,
            [
                new PatchGroupDefinition(
                    "shared.content.items",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(harmony, typeof(ItemDataBaseAddItemsPatch));
                        return PatchGroupExecutionResult.SuccessResult("Hooks ItemDataBase.AddItems.");
                    }),
                new PatchGroupDefinition(
                    "shared.content.recipes",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(harmony, typeof(ItemRecipeDataBaseAddItemRecipesPatch));
                        return PatchGroupExecutionResult.SuccessResult("Hooks ItemRecipeDataBase.AddItemRecipes.");
                    }),
                new PatchGroupDefinition(
                    "shared.content.stats",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(harmony, typeof(EntityStatsDataBaseAddStatDefinitionsPatch));
                        return PatchGroupExecutionResult.SuccessResult("Hooks EntityStatsDataBase.AddStatDefinitions.");
                    }),
                new PatchGroupDefinition(
                    "shared.content.crafting-stations",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(harmony, typeof(CraftingStationDataBaseAddCraftingStationsPatch));
                        return PatchGroupExecutionResult.SuccessResult("Hooks CraftingStationDataBase.AddCraftingStations.");
                    }),
                new PatchGroupDefinition(
                    "shared.content.placeables",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(
                            harmony,
                            typeof(ConstructionDataBaseAddConstructionsPatch),
                            typeof(PlaceableDecorationBootstrap),
                            typeof(PlaceableEntityLootBootstrap),
                            typeof(PlaceableServerDoodadBootstrap));
                        return PatchGroupExecutionResult.SuccessResult("Hooks placeable construction, entity identity, and persistence support.");
                    }),
                new PatchGroupDefinition(
                    "shared.content.value-overrides",
                    diagnosticHostKind,
                    static harmony =>
                    {
                        PatchGroupInstaller.PatchClasses(
                            harmony,
                            typeof(ItemDataBaseAddItemsValueOverridePatch),
                            typeof(EntitySControllerSpawnEntityValueOverridePatch));
                        return PatchGroupExecutionResult.SuccessResult("Hooks .romod value overrides for existing items and entity spawn defaults.");
                    })
            ]);
    }

    internal static void InjectStats(ref List<StatDescription> stats)
    {
        if (_statsInjected)
        {
            return;
        }

        _statsInjected = true;
        var ids = new HashSet<string>(stats.Select(s => s.Id), StringComparer.Ordinal);
        var injectedCount = 0;

        foreach (var def in ModRegistries.Stats.Pending)
        {
            if (ids.Contains(def.Id))
            {
                _logger?.Warn($"[modloader] Stat {def.Id} already exists; skipping.");
                continue;
            }

            stats.Add(new StatDescription
            {
                Id = def.Id,
                Name = (StringId)(def.NameTextId ?? def.Name),
                Description = (StringId)(def.DescriptionTextId ?? def.Description),
                Icon = def.Icon,
                Flags = (Shared.Models.Stats.StatFlags)(int)def.Flags,
                StringFormat = def.StringFormat,
                MaxValue = def.MaxValue,
                MinValue = def.MinValue,
                DefaultValue = def.DefaultValue,
                IsPercentage = def.IsPercentage,
                IsNegativeStat = def.IsNegativeStat,
                StatType = (Shared.Models.Stats.StatType)(int)def.Type
            });
            ids.Add(def.Id);
            injectedCount++;
        }

        if (injectedCount > 0)
        {
            _logger?.Info($"[modloader] Injected {injectedCount} mod stat(s) into shared stat load.");
        }
    }

    internal static void InjectCraftingStations(ref List<CraftingStationData> stations)
    {
        if (_stationsInjected)
        {
            return;
        }

        _stationsInjected = true;
        var ids = new HashSet<string>(stations.Select(s => s.Id), StringComparer.Ordinal);
        var injectedCount = 0;

        foreach (var def in ModRegistries.CraftingStations.Pending)
        {
            if (ids.Contains(def.Id))
            {
                _logger?.Warn($"[modloader] Crafting station {def.Id} already exists; skipping.");
                continue;
            }

            // AddCraftingStations overwrites Name with the text key "{id}*crafting_station:name",
            // so the Name we set here is just to satisfy the required member; we register the
            // display name under that key below.
            stations.Add(new CraftingStationData
            {
                Id = def.Id,
                IconId = def.IconId,
                Name = (StringId)$"{def.Id}*crafting_station:name"
            });
            ModRegistries.Text.Register(new TextDefinition
            {
                Id = $"{def.Id}*crafting_station:name",
                Text = def.Name
            });
            ids.Add(def.Id);
            injectedCount++;
        }

        if (injectedCount > 0)
        {
            _logger?.Info($"[modloader] Injected {injectedCount} mod crafting station(s) into shared station load.");
        }
    }

    internal static void InjectItems(ref IEnumerable<ItemData> itemData)
    {
        if (_itemsInjected)
        {
            return;
        }

        _itemsInjected = true;
        var vanillaItems = itemData.ToList();
        var itemIdsInBatch = new HashSet<string>(vanillaItems.Select(item => item.Id), StringComparer.Ordinal);
        var modItems = new List<ItemData>();

        foreach (var def in ModRegistries.Items.Pending)
        {
            if (itemIdsInBatch.Contains(def.Id) || ItemDataBase.GetItemDataOrNull(def.Id) is not null)
            {
                _logger?.Warn($"[modloader] Item {def.Id} already exists; skipping.");
                continue;
            }

            var item = new ItemData
            {
                Id = def.Id,
                Icon = def.Icon,
                MaxStackSize = def.MaxStackSize,
                Tier = def.Tier
            };
            item.Name = (StringId)(def.NameTextId ?? def.Name);
            item.Description = (StringId)(def.DescriptionTextId ?? def.Description);
            if (def.Equipment is { } equipment)
            {
                item.Equippable = EquipmentMapping.MapEquipment(def.Id, equipment);
                if (equipment.Weapon is { } weapon && (weapon.ManaCost > 0f || weapon.SpecialManaCost > 0f))
                {
                    _manaCosts[def.Id] = new ManaCostInfo(weapon.ManaCost, weapon.SpecialManaCost, weapon.ManaStatId);
                }
            }

            modItems.Add(item);
            itemIdsInBatch.Add(def.Id);
        }

        // Placeable benches: synthesize a usable "placeable:" item by cloning the
        // vanilla place-construction item template so we inherit the exact spell
        // wiring, then re-point it at our generated construction.
        modItems.AddRange(BuildPlaceableItems(vanillaItems, itemIdsInBatch));

        // Built-in debug wand: a usable item the client intercepts to open the
        // debug menu (teleport / heal / god mode / fly).
        var debugWand = BuildDebugWandItem(vanillaItems, itemIdsInBatch);
        if (debugWand is not null)
        {
            modItems.Add(debugWand);
        }

        if (modItems.Count == 0)
        {
            return;
        }

        vanillaItems.AddRange(modItems);
        itemData = vanillaItems;
        _logger?.Info($"[modloader] Injected {modItems.Count} mod item(s) into shared item load.");
    }

    private static List<ItemData> BuildPlaceableItems(List<ItemData> vanillaItems, HashSet<string> itemIdsInBatch)
    {
        var result = new List<ItemData>();
        var pending = ModRegistries.Placeables.Pending;
        if (pending.Count == 0)
        {
            return result;
        }

        foreach (var p in pending)
        {
            if (itemIdsInBatch.Contains(p.Id) || ItemDataBase.GetItemDataOrNull(p.Id) is not null)
            {
                _logger?.Warn($"[modloader] Placeable item {p.Id} already exists; skipping.");
                continue;
            }

            var templateItemId = GetPlaceableItemTemplateId(p.Template);
            var template = vanillaItems.FirstOrDefault(i => string.Equals(i.Id, templateItemId, StringComparison.Ordinal));
            if (template is null)
            {
                _logger?.Warn($"[modloader] Placeable item template '{templateItemId}' not found in item batch; cannot synthesize '{p.Id}'.");
                continue;
            }

            var item = ClonePlaceableItem(template, p);
            if (item is null)
            {
                continue;
            }

            result.Add(item);
            itemIdsInBatch.Add(p.Id);
            _logger?.Info($"[modloader] Synthesized placeable item '{p.Id}' -> construction '{p.ConstructionId}'.");
        }

        return result;
    }

    private static string GetPlaceableItemTemplateId(VanillaBenchTemplate template) => template switch
    {
        VanillaBenchTemplate.Campfire => "placeable:campfire",
        VanillaBenchTemplate.Cauldron => "placeable:workbench",
        VanillaBenchTemplate.WarTable => "placeable:workbench",
        _ => "placeable:workbench",
    };

    private static ItemData? ClonePlaceableItem(ItemData template, ModPlaceableStation p)
    {
        if (template.Usable is null)
        {
            _logger?.Warn($"[modloader] Placeable template '{template.Id}' has no UsableItem; cannot synthesize '{p.Id}'.");
            return null;
        }

        var templateArgs = template.Usable.SpellTypeArgs as PlaceConstructionSpellArgs;
        var maxDistance = templateArgs?.MaxPlacementDistance;

        var item = new ItemData
        {
            Id = p.Id,
            Icon = p.IconId,
            MaxStackSize = template.MaxStackSize,
            Tier = template.Tier
        };
        item.Name = (StringId)$"{p.Id}*item:name";
        item.Description = (StringId)$"{p.Id}*item:description";
        item.Flags = template.Flags;
        item.NameFormatKey = template.NameFormatKey;
        item.Tradable = template.Tradable;
        item.Unique = template.Unique;

        var usable = new UsableItem
        {
            SpellId = template.Usable.SpellId,
            Type = template.Usable.Type,
            Cooldown = template.Usable.Cooldown,
            UsesMax = template.Usable.UsesMax,
            SpellTypeArgs = new PlaceConstructionSpellArgs
            {
                ConstructionId = p.ConstructionId,
                MaxPlacementDistance = maxDistance ?? default
            }
        };
        item.Usable = usable;
        return item;
    }

    /// <summary>
    /// Synthesizes the built-in debug wand. It carries a <see cref="UsableItem"/>
    /// so the inventory right-click and hotbar paths treat it as usable; the
    /// client's <c>DebugWandHost</c> intercepts <c>TryUseItem</c> for this id and
    /// opens the debug menu instead of casting. The referenced spell
    /// (<c>spell:repair</c>) only has to resolve so the right-click gate fires —
    /// it is never actually cast because the interception returns early.
    /// </summary>
    private static ItemData? BuildDebugWandItem(List<ItemData> vanillaItems, HashSet<string> itemIdsInBatch)
    {
        if (itemIdsInBatch.Contains(DebugWandItemId) || ItemDataBase.GetItemDataOrNull(DebugWandItemId) is not null)
        {
            return null;
        }

        // Borrow a real, present icon so the wand renders. Prefer a usable item's
        // icon (tool-like); fall back to any item with an icon.
        var icon = vanillaItems.FirstOrDefault(i => i.Usable is not null && !string.IsNullOrEmpty(i.Icon))?.Icon
                   ?? vanillaItems.FirstOrDefault(i => !string.IsNullOrEmpty(i.Icon))?.Icon
                   ?? string.Empty;

        var item = new ItemData
        {
            Id = DebugWandItemId,
            Icon = icon,
            MaxStackSize = 1,
            Tier = 1
        };
        item.Name = (StringId)"Debug Wand";
        item.Description = (StringId)"Use to open the debug menu (teleport, heal, god mode, fly).";
        item.Usable = new UsableItem
        {
            SpellId = "spell:repair",
            Type = UsableItem.UsableType.Healing,
            Cooldown = 0f,
            UsesMax = 0
        };

        itemIdsInBatch.Add(DebugWandItemId);
        _logger?.Info($"[modloader] Synthesized built-in debug wand item '{DebugWandItemId}'.");
        return item;
    }

    internal static void InjectRecipes(ref IEnumerable<ItemRecipe> itemRecipes)
    {
        if (_recipesInjected)
        {
            return;
        }

        _recipesInjected = true;
        var vanillaRecipes = itemRecipes.ToList();
        var recipeIdsInBatch = new HashSet<string>(vanillaRecipes.Select(recipe => recipe.Id), StringComparer.Ordinal);
        var modRecipes = new List<ItemRecipe>();

        foreach (var def in ModRegistries.Recipes.Pending)
        {
            if (recipeIdsInBatch.Contains(def.ResultItemId) || ItemRecipeDataBase.GetItemRecipeOrNull(def.ResultItemId) is not null)
            {
                _logger?.Warn($"[modloader] Recipe {def.ResultItemId} already exists; skipping.");
                continue;
            }

            var amounts = new ItemAmount[def.Ingredients.Count];
            for (var i = 0; i < def.Ingredients.Count; i++)
            {
                amounts[i] = new ItemAmount
                {
                    ItemId = def.Ingredients[i].ItemId,
                    Amount = def.Ingredients[i].Amount
                };
            }

            modRecipes.Add(new ItemRecipe
            {
                Id = def.ResultItemId,
                CraftedItemId = def.ResultItemId,
                CraftedAmount = def.ResultAmount,
                RequiredCraftingStation = def.Station,
                ItemsAmounts = amounts
            });
            recipeIdsInBatch.Add(def.ResultItemId);
        }

        if (modRecipes.Count == 0)
        {
            return;
        }

        vanillaRecipes.AddRange(modRecipes);
        itemRecipes = vanillaRecipes;
        _injectedRecipes = modRecipes;
        _logger?.Info($"[modloader] Injected {modRecipes.Count} mod recipe(s) into shared recipe load.");
    }
}

[HarmonyPatch(typeof(ItemDataBase), nameof(ItemDataBase.AddItems))]
internal static class ItemDataBaseAddItemsPatch
{
    private static void Prefix(ref IEnumerable<ItemData> __0) => SharedContentBootstrap.InjectItems(ref __0);
}

internal static class EquipmentMapping
{
    public static EquippableItem MapEquipment(string itemId, EquipmentDefinition def)
    {
        var equippable = new EquippableItem
        {
            EquipmentType = (EquipmentType)(int)def.Slot,
            ExtraEquipmentType = def.ExtraSlot is { } extra ? (EquipmentType)(int)extra : null,
            Material = (Shared.Models.Items.EquipmentMaterial)(int)def.Material,
            StatBonuses = MapStatBonuses(def.StatBonuses),
            WeaponStats = def.Weapon is { } w ? MapWeaponStats(w) : default,
            ShieldStats = def.Shield is { } s ? MapShieldStats(s) : default,
            EntityAuraId = def.EntityAuraId,
            ExtraEntityAuraIds = def.ExtraEntityAuraIds.ToList(),
            DisplayId = ModEquipmentDisplayIds.ResolveForItem(itemId, def)
        };
        return equippable;
    }

    private static Dictionary<string, StatModificationData> MapStatBonuses(IEnumerable<StatBonusDefinition> bonuses)
    {
        var result = new Dictionary<string, StatModificationData>(StringComparer.Ordinal);
        foreach (var bonus in bonuses)
        {
            result[bonus.StatId] = new StatModificationData
            {
                Additive = bonus.Additive,
                AdditiveMultiplier = bonus.AdditiveMultiplier,
                BaseMultiplier = bonus.BaseMultiplier,
                BonusMultiplier = bonus.BonusMultiplier,
                Multiplier = bonus.Multiplier
            };
        }
        return result;
    }

    private static WeaponStats MapWeaponStats(WeaponStatsDefinition def) => new()
    {
        Class = MapWeaponClass(def.Class),
        DamageRanges = new DamageTypeRanges
        {
            MinDamage = BuildDamageArray(def.Damage, r => r.Min),
            MaxDamage = BuildDamageArray(def.Damage, r => r.Max)
        },
        SwingTimer = def.SwingTimer,
        BaseAttackRange = def.BaseAttackRange,
        BaseKnockback = def.BaseKnockback,
        EnergyCost = def.EnergyCost,
        SpecialEnergyCost = def.SpecialEnergyCost,
        StunPower = def.StunPower,
        MovementFactor = def.MovementFactor,
        SpellTomeArgs = def.SpellTome is { } tome ? MapSpellTome(tome) : null
    };

    private static SpellTomeArgs MapSpellTome(SpellTomeDefinition def) => new()
    {
        SpellId = def.SpellId,
        ChargedSpellId = def.ChargedSpellId ?? string.Empty,
        ChargeTime = def.ChargeTime,
        Target = (Target)(int)def.Target,
        ChargedTarget = (Target)(int)def.ChargedTarget
    };

    private static ShieldStats MapShieldStats(ShieldStatsDefinition def) => new()
    {
        BlockStrength = def.BlockStrength,
        BlockArcSize = def.BlockArcSize,
        StrongBlockArcSize = def.StrongBlockArcSize,
        EnterCost = def.EnterCost
    };

    private static WeaponClass MapWeaponClass(WeaponClassPreset preset) => preset switch
    {
        WeaponClassPreset.Sword => WeaponClass.Sword,
        WeaponClassPreset.Spear => WeaponClass.Spear,
        WeaponClassPreset.Crossbow => WeaponClass.Crossbow,
        WeaponClassPreset.Shield => WeaponClass.Shield,
        WeaponClassPreset.Arrow => WeaponClass.Arrow,
        WeaponClassPreset.SpellTome => WeaponClass.SpellTome,
        WeaponClassPreset.Dagger => WeaponClass.Dagger,
        WeaponClassPreset.Sledgehammer => WeaponClass.Sledgehammer,
        WeaponClassPreset.Bow => WeaponClass.Bow,
        WeaponClassPreset.Fists => WeaponClass.Fists,
        WeaponClassPreset.GrapplingHook => WeaponClass.GrapplingHook,
        WeaponClassPreset.Javelin => WeaponClass.Javelin,
        WeaponClassPreset.Quiver => WeaponClass.Quiver,
        _ => WeaponClass.Undefined
    };

    private static DamageTypesArray BuildDamageArray(IReadOnlyList<DamageRange> ranges, Func<DamageRange, float> selector)
    {
        var arr = new float[DamageTypes.MaxTypes];
        foreach (var range in ranges)
        {
            arr[MapDamageTypeIndex(range.Type)] = selector(range);
        }
        return new DamageTypesArray(arr);
    }

    private static int MapDamageTypeIndex(DamageTypeId id) => id switch
    {
        DamageTypeId.Slashing => DamageTypes.Slashing,
        DamageTypeId.Piercing => DamageTypes.Piercing,
        DamageTypeId.Bludgeoning => DamageTypes.Bludgeoning,
        DamageTypeId.Pyro => DamageTypes.Pyro,
        DamageTypeId.Chloro => DamageTypes.Chloro,
        DamageTypeId.Aqua => DamageTypes.Aqua,
        DamageTypeId.Cosmo => DamageTypes.Cosmo,
        DamageTypeId.Necro => DamageTypes.Necro,
        _ => DamageTypes.Slashing
    };
}

[HarmonyPatch(typeof(ItemRecipeDataBase), nameof(ItemRecipeDataBase.AddItemRecipes))]
internal static class ItemRecipeDataBaseAddItemRecipesPatch
{
    private static void Prefix(ref IEnumerable<ItemRecipe> __0) => SharedContentBootstrap.InjectRecipes(ref __0);
}

[HarmonyPatch(typeof(EntityStatsDataBase), nameof(EntityStatsDataBase.AddStatDefinitions))]
internal static class EntityStatsDataBaseAddStatDefinitionsPatch
{
    private static void Prefix(ref List<StatDescription> __0) => SharedContentBootstrap.InjectStats(ref __0);
}

[HarmonyPatch(typeof(CraftingStationDataBase), nameof(CraftingStationDataBase.AddCraftingStations))]
internal static class CraftingStationDataBaseAddCraftingStationsPatch
{
    private static void Prefix(ref List<CraftingStationData> __0) => SharedContentBootstrap.InjectCraftingStations(ref __0);
}

/// <summary>
/// Per-item mana cost record. Populated during item injection so client-side
/// cast handlers can look up cost by item id without re-reading the mod definitions.
/// </summary>
public sealed record ManaCostInfo(float TapCost, float SpecialCost, string StatId);
