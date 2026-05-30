using CandideServer.ServerControllers;
using HarmonyLib;
using Romestead.ModLoader;
using Shared.Combat;
using Shared.Data;
using Shared.Models.Items;

namespace Romestead.StartupHook;

internal static class ValueOverrideBootstrap
{
    internal static void ApplyItemOverrides(IModLogger? log)
    {
        var overrides = ModRegistries.ValueOverrides.Pending.SelectMany(v => v.Items).ToList();
        if (overrides.Count == 0)
        {
            return;
        }

        var applied = 0;
        foreach (var itemOverride in overrides)
        {
            if (ItemDataBase.GetItemDataOrNull(itemOverride.Id) is not { } item)
            {
                log?.Warn($"[value-overrides] Item '{itemOverride.Id}' not found; skipping override.");
                continue;
            }

            ApplyItemOverride(item, itemOverride, log);
            applied++;
        }

        log?.Info($"[value-overrides] Applied {applied} item value override(s).");
    }

    internal static void ApplySpawnOverride(SpawnEntityArgs args)
    {
        if (args.MaxHealth.HasValue)
        {
            return;
        }

        foreach (var rule in ModRegistries.ValueOverrides.Pending.SelectMany(v => v.EntityHealth))
        {
            if (rule.BaseId == args.BaseId)
            {
                args.MaxHealth = Math.Max(1f, rule.MaxHealth);
                return;
            }
        }
    }

    private static void ApplyItemOverride(ItemData item, ItemValueOverrideDefinition itemOverride, IModLogger? log)
    {
        if (itemOverride.MaxStackSize is { } maxStackSize)
        {
            item.MaxStackSize = maxStackSize;
        }

        if (itemOverride.Tier is { } tier)
        {
            item.Tier = tier;
        }

        if (itemOverride.Weapon is not { } weaponOverride)
        {
            return;
        }

        if (item.Equippable?.WeaponStats is not { } weapon)
        {
            log?.Warn($"[value-overrides] Item '{item.Id}' has no weapon stats; skipping weapon override.");
            return;
        }

        if (weaponOverride.SwingTimer is { } swingTimer) { weapon.SwingTimer = swingTimer; }
        if (weaponOverride.BaseAttackRange is { } range) { weapon.BaseAttackRange = range; }
        if (weaponOverride.BaseKnockback is { } knockback) { weapon.BaseKnockback = knockback; }
        if (weaponOverride.EnergyCost is { } energyCost) { weapon.EnergyCost = energyCost; }
        if (weaponOverride.SpecialEnergyCost is { } specialEnergyCost) { weapon.SpecialEnergyCost = specialEnergyCost; }
        if (weaponOverride.StunPower is { } stunPower) { weapon.StunPower = stunPower; }
        if (weaponOverride.MovementFactor is { } movementFactor) { weapon.MovementFactor = movementFactor; }

        foreach (var damage in weaponOverride.Damage)
        {
            var index = MapDamageTypeIndex(damage.Type);
            weapon.DamageRanges.MinDamage[index] = damage.Min;
            weapon.DamageRanges.MaxDamage[index] = damage.Max;
        }
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

[HarmonyPatch(typeof(ItemDataBase), nameof(ItemDataBase.AddItems))]
internal static class ItemDataBaseAddItemsValueOverridePatch
{
    private static void Postfix() => ValueOverrideBootstrap.ApplyItemOverrides(SharedContentBootstrap.Logger);
}

[HarmonyPatch(typeof(EntitySController), nameof(EntitySController.SpawnEntity))]
internal static class EntitySControllerSpawnEntityValueOverridePatch
{
    private static void Prefix(SpawnEntityArgs args) => ValueOverrideBootstrap.ApplySpawnOverride(args);
}
