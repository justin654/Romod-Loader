using Romestead.RomodFormat.Internal;
using Tomlyn.Model;

namespace Romestead.RomodFormat.Content.Types;

public sealed record ValueOverrideTomlModel
{
    public IReadOnlyList<EntityHealthOverrideTomlModel> EntityHealth { get; init; } = [];
    public IReadOnlyList<ItemValueOverrideTomlModel> Items { get; init; } = [];
}

public sealed record EntityHealthOverrideTomlModel
{
    public required string BaseId { get; init; }
    public required float MaxHealth { get; init; }
}

public sealed record ItemValueOverrideTomlModel
{
    public required string Id { get; init; }
    public int? MaxStackSize { get; init; }
    public int? Tier { get; init; }
    public WeaponValueOverrideTomlModel? Weapon { get; init; }
}

public sealed record WeaponValueOverrideTomlModel
{
    public IReadOnlyList<DamageRangeTomlModel> Damage { get; init; } = [];
    public float? SwingTimer { get; init; }
    public float? BaseAttackRange { get; init; }
    public float? BaseKnockback { get; init; }
    public float? EnergyCost { get; init; }
    public float? SpecialEnergyCost { get; init; }
    public float? StunPower { get; init; }
    public float? MovementFactor { get; init; }
}

public sealed class ValueOverrideTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.ValueOverride;

    private static readonly HashSet<string> KnownRootKeys = new(StringComparer.Ordinal)
    {
        "entityHealth", "items"
    };

    private static readonly HashSet<string> KnownEntityHealthKeys = new(StringComparer.Ordinal)
    {
        "baseId", "maxHealth"
    };

    private static readonly HashSet<string> KnownItemKeys = new(StringComparer.Ordinal)
    {
        "id", "maxStackSize", "tier", "weapon"
    };

    private static readonly HashSet<string> KnownWeaponKeys = new(StringComparer.Ordinal)
    {
        "damage", "swingTimer", "baseAttackRange", "baseKnockback",
        "energyCost", "specialEnergyCost", "stunPower", "movementFactor"
    };

    private static readonly HashSet<string> KnownDamageKeys = new(StringComparer.Ordinal)
    {
        "type", "min", "max"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        TomlHelpers.WarnUnknownKeys(root, KnownRootKeys, context.ArchiveRelativePath, context.PackageId, log);
        return new ValueOverrideTomlModel
        {
            EntityHealth = ParseEntityHealth(root, context, log),
            Items = ParseItems(root, context, log)
        };
    }

    private static IReadOnlyList<EntityHealthOverrideTomlModel> ParseEntityHealth(
        TomlTable root,
        RomodContentParseContext context,
        IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(root, "entityHealth", context.ArchiveRelativePath);
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[entityHealth]]";
        var result = new List<EntityHealthOverrideTomlModel>(array.Count);
        foreach (var entry in array)
        {
            TomlHelpers.WarnUnknownKeys(entry, KnownEntityHealthKeys, src, context.PackageId, log);
            var maxHealth = TomlHelpers.GetFloatOrDefault(entry, "maxHealth", src, 0f);
            if (maxHealth <= 0f)
            {
                throw new RomodFormatException($"{src}: field 'maxHealth' must be greater than zero.");
            }

            result.Add(new EntityHealthOverrideTomlModel
            {
                BaseId = TomlHelpers.RequireString(entry, "baseId", src),
                MaxHealth = maxHealth
            });
        }

        return result;
    }

    private static IReadOnlyList<ItemValueOverrideTomlModel> ParseItems(
        TomlTable root,
        RomodContentParseContext context,
        IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(root, "items", context.ArchiveRelativePath);
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[items]]";
        var result = new List<ItemValueOverrideTomlModel>(array.Count);
        foreach (var entry in array)
        {
            TomlHelpers.WarnUnknownKeys(entry, KnownItemKeys, src, context.PackageId, log);
            result.Add(new ItemValueOverrideTomlModel
            {
                Id = TomlHelpers.RequireString(entry, "id", src),
                MaxStackSize = ReadPositiveIntOrNull(entry, "maxStackSize", src),
                Tier = TomlHelpers.GetIntOrNull(entry, "tier", src),
                Weapon = ParseWeapon(TomlHelpers.GetTableOrNull(entry, "weapon", src), context, log)
            });
        }

        return result;
    }

    private static WeaponValueOverrideTomlModel? ParseWeapon(
        TomlTable? table,
        RomodContentParseContext context,
        IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [items.weapon]";
        TomlHelpers.WarnUnknownKeys(table, KnownWeaponKeys, src, context.PackageId, log);
        return new WeaponValueOverrideTomlModel
        {
            Damage = ParseDamage(table, context, log),
            SwingTimer = TomlHelpers.GetFloatOrNull(table, "swingTimer", src),
            BaseAttackRange = TomlHelpers.GetFloatOrNull(table, "baseAttackRange", src),
            BaseKnockback = TomlHelpers.GetFloatOrNull(table, "baseKnockback", src),
            EnergyCost = TomlHelpers.GetFloatOrNull(table, "energyCost", src),
            SpecialEnergyCost = TomlHelpers.GetFloatOrNull(table, "specialEnergyCost", src),
            StunPower = TomlHelpers.GetFloatOrNull(table, "stunPower", src),
            MovementFactor = TomlHelpers.GetFloatOrNull(table, "movementFactor", src)
        };
    }

    private static IReadOnlyList<DamageRangeTomlModel> ParseDamage(
        TomlTable weaponTable,
        RomodContentParseContext context,
        IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(weaponTable, "damage", context.ArchiveRelativePath + " [items.weapon]");
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[items.weapon.damage]]";
        var result = new List<DamageRangeTomlModel>(array.Count);
        foreach (var entry in array)
        {
            TomlHelpers.WarnUnknownKeys(entry, KnownDamageKeys, src, context.PackageId, log);
            result.Add(new DamageRangeTomlModel
            {
                Type = TomlHelpers.RequireString(entry, "type", src),
                Min = TomlHelpers.GetFloatOrDefault(entry, "min", src, 0f),
                Max = TomlHelpers.GetFloatOrDefault(entry, "max", src, 0f)
            });
        }

        return result;
    }

    private static int? ReadPositiveIntOrNull(TomlTable table, string key, string source)
    {
        var value = TomlHelpers.GetIntOrNull(table, key, source);
        if (value is <= 0)
        {
            throw new RomodFormatException($"{source}: field '{key}' must be greater than zero.");
        }

        return value;
    }
}
