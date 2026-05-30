using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record ItemTomlModel
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public string? DescriptionTextId { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public int MaxStackSize { get; init; } = 99;
    public int Tier { get; init; } = 1;
    public EquipmentTomlModel? Equipment { get; init; }
}

public sealed record EquipmentTomlModel
{
    public required string Slot { get; init; }
    public string? ExtraSlot { get; init; }
    public string Material { get; init; } = "Iron";
    public string? DisplayId { get; init; }
    public EquipmentDisplayTomlModel? Display { get; init; }
    public EquipmentHeldVfxTomlModel? HeldVfx { get; init; }
    public string? EntityAuraId { get; init; }
    public IReadOnlyList<string> ExtraEntityAuraIds { get; init; } = [];
    public IReadOnlyList<StatBonusTomlModel> StatBonuses { get; init; } = [];
    public WeaponTomlModel? Weapon { get; init; }
    public ShieldTomlModel? Shield { get; init; }
}

public sealed record EquipmentDisplayTomlModel
{
    public string? Id { get; init; }
    public IReadOnlyList<int> SpacTagsToHide { get; init; } = [];
    public IReadOnlyList<EquipmentDisplayFragmentTomlModel> Fragments { get; init; } = [];
}

public sealed record EquipmentDisplayFragmentTomlModel
{
    public int SkinTag { get; init; } = 8;
    public required string SkinName { get; init; }
    public string? Texture { get; init; }
    public int SpriteWidth { get; init; } = 48;
    public int SpriteHeight { get; init; } = 48;
    public int SpacTag { get; init; } = 8;
    public bool HideBaseSkin { get; init; } = true;
    public float Layer { get; init; } = 1f;
    public float DepthOffset { get; init; }
    public IReadOnlyList<EquipmentDisplayPaletteTomlModel> Palette { get; init; } = [];
}

public sealed record EquipmentDisplayPaletteTomlModel
{
    public required string PaletteId { get; init; }
    public int Row { get; init; }
}

public sealed record EquipmentHeldVfxTomlModel
{
    public string? ParticleEmitterId { get; init; }
    public bool RotateWithEntityDirection { get; init; } = true;
    public float ParticleOffsetX { get; init; }
    public float ParticleOffsetY { get; init; }
    public float ParticleOffsetZ { get; init; } = 14f;
    public float ParticleLineLength { get; init; } = 22f;
    public float ParticleLineWidth { get; init; } = 2f;
    public float ParticleLineHeight { get; init; } = 3f;
    public float ParticleLineAngleDegrees { get; init; }
    public float? ParticleSpawnFrequency { get; init; }
    public int? ParticleAmountSpawned { get; init; }
    public bool LightEnabled { get; init; } = true;
    public float LightOffsetX { get; init; } = 8f;
    public float LightOffsetY { get; init; }
    public float LightOffsetZ { get; init; } = 18f;
    public float LightRadius { get; init; } = 48f;
    public float LightIntensity { get; init; } = 1.2f;
    public float LightRed { get; init; } = 1f;
    public float LightGreen { get; init; } = 0.42f;
    public float LightBlue { get; init; } = 0.08f;
    public float LightDuration { get; init; } = 0.25f;
    public float LightFlickerAmount { get; init; } = 0.2f;
}

public sealed record StatBonusTomlModel
{
    public required string StatId { get; init; }
    public float Additive { get; init; }
    public float AdditiveMultiplier { get; init; }
    public float BaseMultiplier { get; init; }
    public float BonusMultiplier { get; init; }
    public float Multiplier { get; init; } = 1f;
}

public sealed record WeaponTomlModel
{
    public required string Class { get; init; }
    public IReadOnlyList<DamageRangeTomlModel> Damage { get; init; } = [];
    public float SwingTimer { get; init; }
    public float BaseAttackRange { get; init; }
    public float BaseKnockback { get; init; }
    public float StunPower { get; init; }
    public float EnergyCost { get; init; }
    public float SpecialEnergyCost { get; init; }
    public float MovementFactor { get; init; } = 1f;
    public float ManaCost { get; init; }
    public float SpecialManaCost { get; init; }
    public string ManaStatId { get; init; } = "Mana";
    public SpellTomeTomlModel? SpellTome { get; init; }
}

public sealed record ShieldTomlModel
{
    public float BlockStrength { get; init; }
    public float BlockArcSize { get; init; }
    public float StrongBlockArcSize { get; init; }
    public float EnterCost { get; init; }
}

public sealed record DamageRangeTomlModel
{
    public required string Type { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
}

public sealed record SpellTomeTomlModel
{
    public required string SpellId { get; init; }
    public string? ChargedSpellId { get; init; }
    public float ChargeTime { get; init; } = 1f;
    public string Target { get; init; } = "Self";
    public string ChargedTarget { get; init; } = "Self";
}

public sealed class ItemTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Item;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "nameTextId", "name", "descriptionTextId", "description",
        "icon", "maxStackSize", "tier", "equipment"
    };

    private static readonly HashSet<string> KnownEquipmentKeys = new(StringComparer.Ordinal)
    {
        "slot", "extraSlot", "material", "displayId", "display", "heldVfx",
        "entityAuraId", "extraEntityAuraIds",
        "statBonuses", "weapon", "shield"
    };

    private static readonly HashSet<string> KnownDisplayKeys = new(StringComparer.Ordinal)
    {
        "id", "spacTagsToHide", "fragments"
    };

    private static readonly HashSet<string> KnownDisplayFragmentKeys = new(StringComparer.Ordinal)
    {
        "skinTag", "skinName", "texture", "spriteWidth", "spriteHeight",
        "spacTag", "hideBaseSkin", "layer", "depthOffset", "palette"
    };

    private static readonly HashSet<string> KnownDisplayPaletteKeys = new(StringComparer.Ordinal)
    {
        "paletteId", "row"
    };

    private static readonly HashSet<string> KnownHeldVfxKeys = new(StringComparer.Ordinal)
    {
        "particleEmitterId", "rotateWithEntityDirection",
        "particleOffsetX", "particleOffsetY", "particleOffsetZ",
        "particleLineLength", "particleLineWidth", "particleLineHeight",
        "particleLineAngleDegrees", "particleSpawnFrequency", "particleAmountSpawned",
        "lightEnabled", "lightOffsetX", "lightOffsetY", "lightOffsetZ",
        "lightRadius", "lightIntensity", "lightRed", "lightGreen", "lightBlue",
        "lightDuration", "lightFlickerAmount"
    };

    private static readonly HashSet<string> KnownWeaponKeys = new(StringComparer.Ordinal)
    {
        "class", "damage", "swingTimer", "baseAttackRange", "baseKnockback",
        "stunPower", "energyCost", "specialEnergyCost", "movementFactor",
        "manaCost", "specialManaCost", "manaStatId", "spellTome"
    };

    private static readonly HashSet<string> KnownShieldKeys = new(StringComparer.Ordinal)
    {
        "blockStrength", "blockArcSize", "strongBlockArcSize", "enterCost"
    };

    private static readonly HashSet<string> KnownDamageKeys = new(StringComparer.Ordinal)
    {
        "type", "min", "max"
    };

    private static readonly HashSet<string> KnownStatBonusKeys = new(StringComparer.Ordinal)
    {
        "statId", "additive", "additiveMultiplier", "baseMultiplier", "bonusMultiplier", "multiplier"
    };

    private static readonly HashSet<string> KnownSpellTomeKeys = new(StringComparer.Ordinal)
    {
        "spellId", "chargedSpellId", "chargeTime", "target", "chargedTarget"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new ItemTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            NameTextId = TomlHelpers.GetStringOrNull(root, "nameTextId", src),
            Name = TomlHelpers.RequireString(root, "name", src),
            DescriptionTextId = TomlHelpers.GetStringOrNull(root, "descriptionTextId", src),
            Description = TomlHelpers.RequireString(root, "description", src),
            Icon = TomlHelpers.RequireString(root, "icon", src),
            MaxStackSize = TomlHelpers.GetIntOrDefault(root, "maxStackSize", src, 99, 1, int.MaxValue),
            Tier = TomlHelpers.GetIntOrDefault(root, "tier", src, 1, 0, 100),
            Equipment = ParseEquipment(TomlHelpers.GetTableOrNull(root, "equipment", src), context, log)
        };
    }

    private static EquipmentTomlModel? ParseEquipment(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment]";
        TomlHelpers.WarnUnknownKeys(table, KnownEquipmentKeys, src, context.PackageId, log);

        return new EquipmentTomlModel
        {
            Slot = TomlHelpers.RequireString(table, "slot", src),
            ExtraSlot = TomlHelpers.GetStringOrNull(table, "extraSlot", src),
            Material = TomlHelpers.GetStringOrNull(table, "material", src) ?? "Iron",
            DisplayId = TomlHelpers.GetStringOrNull(table, "displayId", src),
            Display = ParseDisplay(TomlHelpers.GetTableOrNull(table, "display", src), context, log),
            HeldVfx = ParseHeldVfx(TomlHelpers.GetTableOrNull(table, "heldVfx", src), context, log),
            EntityAuraId = TomlHelpers.GetStringOrNull(table, "entityAuraId", src),
            ExtraEntityAuraIds = TomlHelpers.GetStringArrayOrEmpty(table, "extraEntityAuraIds", src),
            StatBonuses = ParseStatBonuses(table, context, log),
            Weapon = ParseWeapon(TomlHelpers.GetTableOrNull(table, "weapon", src), context, log),
            Shield = ParseShield(TomlHelpers.GetTableOrNull(table, "shield", src), context, log)
        };
    }

    private static EquipmentDisplayTomlModel? ParseDisplay(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment.display]";
        TomlHelpers.WarnUnknownKeys(table, KnownDisplayKeys, src, context.PackageId, log);

        return new EquipmentDisplayTomlModel
        {
            Id = TomlHelpers.GetStringOrNull(table, "id", src),
            SpacTagsToHide = TomlHelpers.GetIntArrayOrEmpty(table, "spacTagsToHide", src),
            Fragments = ParseDisplayFragments(table, context, log)
        };
    }

    private static IReadOnlyList<EquipmentDisplayFragmentTomlModel> ParseDisplayFragments(
        TomlTable displayTable,
        RomodContentParseContext context,
        IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(displayTable, "fragments", context.ArchiveRelativePath + " [equipment.display]");
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[equipment.display.fragments]]";
        var list = new List<EquipmentDisplayFragmentTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownDisplayFragmentKeys, src, context.PackageId, log);
            list.Add(new EquipmentDisplayFragmentTomlModel
            {
                SkinTag = TomlHelpers.GetIntOrDefault(entry, "skinTag", src, 8),
                SkinName = TomlHelpers.RequireString(entry, "skinName", src),
                Texture = TomlHelpers.GetStringOrNull(entry, "texture", src),
                SpriteWidth = TomlHelpers.GetIntOrDefault(entry, "spriteWidth", src, 48, 1, 8192),
                SpriteHeight = TomlHelpers.GetIntOrDefault(entry, "spriteHeight", src, 48, 1, 8192),
                SpacTag = TomlHelpers.GetIntOrDefault(entry, "spacTag", src, 8),
                HideBaseSkin = TomlHelpers.GetBoolOrDefault(entry, "hideBaseSkin", src, true),
                Layer = TomlHelpers.GetFloatOrDefault(entry, "layer", src, 1f),
                DepthOffset = TomlHelpers.GetFloatOrDefault(entry, "depthOffset", src, 0f),
                Palette = ParseDisplayPalette(entry, context, log)
            });
        }

        return list;
    }

    private static IReadOnlyList<EquipmentDisplayPaletteTomlModel> ParseDisplayPalette(
        TomlTable fragmentTable,
        RomodContentParseContext context,
        IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(fragmentTable, "palette", context.ArchiveRelativePath + " [[equipment.display.fragments]]");
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[equipment.display.fragments.palette]]";
        var list = new List<EquipmentDisplayPaletteTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownDisplayPaletteKeys, src, context.PackageId, log);
            list.Add(new EquipmentDisplayPaletteTomlModel
            {
                PaletteId = TomlHelpers.RequireString(entry, "paletteId", src),
                Row = TomlHelpers.GetIntOrDefault(entry, "row", src, 0)
            });
        }

        return list;
    }

    private static EquipmentHeldVfxTomlModel? ParseHeldVfx(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment.heldVfx]";
        TomlHelpers.WarnUnknownKeys(table, KnownHeldVfxKeys, src, context.PackageId, log);

        return new EquipmentHeldVfxTomlModel
        {
            ParticleEmitterId = TomlHelpers.GetStringOrNull(table, "particleEmitterId", src),
            RotateWithEntityDirection = TomlHelpers.GetBoolOrDefault(table, "rotateWithEntityDirection", src, true),
            ParticleOffsetX = TomlHelpers.GetFloatOrDefault(table, "particleOffsetX", src, 0f),
            ParticleOffsetY = TomlHelpers.GetFloatOrDefault(table, "particleOffsetY", src, 0f),
            ParticleOffsetZ = TomlHelpers.GetFloatOrDefault(table, "particleOffsetZ", src, 14f),
            ParticleLineLength = TomlHelpers.GetFloatOrDefault(table, "particleLineLength", src, 22f),
            ParticleLineWidth = TomlHelpers.GetFloatOrDefault(table, "particleLineWidth", src, 2f),
            ParticleLineHeight = TomlHelpers.GetFloatOrDefault(table, "particleLineHeight", src, 3f),
            ParticleLineAngleDegrees = TomlHelpers.GetFloatOrDefault(table, "particleLineAngleDegrees", src, 0f),
            ParticleSpawnFrequency = TomlHelpers.GetFloatOrNull(table, "particleSpawnFrequency", src),
            ParticleAmountSpawned = TomlHelpers.GetIntOrNull(table, "particleAmountSpawned", src),
            LightEnabled = TomlHelpers.GetBoolOrDefault(table, "lightEnabled", src, true),
            LightOffsetX = TomlHelpers.GetFloatOrDefault(table, "lightOffsetX", src, 8f),
            LightOffsetY = TomlHelpers.GetFloatOrDefault(table, "lightOffsetY", src, 0f),
            LightOffsetZ = TomlHelpers.GetFloatOrDefault(table, "lightOffsetZ", src, 18f),
            LightRadius = TomlHelpers.GetFloatOrDefault(table, "lightRadius", src, 48f),
            LightIntensity = TomlHelpers.GetFloatOrDefault(table, "lightIntensity", src, 1.2f),
            LightRed = TomlHelpers.GetFloatOrDefault(table, "lightRed", src, 1f),
            LightGreen = TomlHelpers.GetFloatOrDefault(table, "lightGreen", src, 0.42f),
            LightBlue = TomlHelpers.GetFloatOrDefault(table, "lightBlue", src, 0.08f),
            LightDuration = TomlHelpers.GetFloatOrDefault(table, "lightDuration", src, 0.25f),
            LightFlickerAmount = TomlHelpers.GetFloatOrDefault(table, "lightFlickerAmount", src, 0.2f)
        };
    }

    private static IReadOnlyList<StatBonusTomlModel> ParseStatBonuses(
        TomlTable equipmentTable, RomodContentParseContext context, IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(equipmentTable, "statBonuses", context.ArchiveRelativePath + " [equipment]");
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[equipment.statBonuses]]";
        var list = new List<StatBonusTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownStatBonusKeys, src, context.PackageId, log);
            list.Add(new StatBonusTomlModel
            {
                StatId = TomlHelpers.RequireString(entry, "statId", src),
                Additive = TomlHelpers.GetFloatOrDefault(entry, "additive", src, 0f),
                AdditiveMultiplier = TomlHelpers.GetFloatOrDefault(entry, "additiveMultiplier", src, 0f),
                BaseMultiplier = TomlHelpers.GetFloatOrDefault(entry, "baseMultiplier", src, 0f),
                BonusMultiplier = TomlHelpers.GetFloatOrDefault(entry, "bonusMultiplier", src, 0f),
                Multiplier = TomlHelpers.GetFloatOrDefault(entry, "multiplier", src, 1f)
            });
        }
        return list;
    }

    private static WeaponTomlModel? ParseWeapon(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment.weapon]";
        TomlHelpers.WarnUnknownKeys(table, KnownWeaponKeys, src, context.PackageId, log);

        return new WeaponTomlModel
        {
            Class = TomlHelpers.RequireString(table, "class", src),
            Damage = ParseDamage(table, context, log),
            SwingTimer = TomlHelpers.GetFloatOrDefault(table, "swingTimer", src, 0f),
            BaseAttackRange = TomlHelpers.GetFloatOrDefault(table, "baseAttackRange", src, 0f),
            BaseKnockback = TomlHelpers.GetFloatOrDefault(table, "baseKnockback", src, 0f),
            StunPower = TomlHelpers.GetFloatOrDefault(table, "stunPower", src, 0f),
            EnergyCost = TomlHelpers.GetFloatOrDefault(table, "energyCost", src, 0f),
            SpecialEnergyCost = TomlHelpers.GetFloatOrDefault(table, "specialEnergyCost", src, 0f),
            MovementFactor = TomlHelpers.GetFloatOrDefault(table, "movementFactor", src, 1f),
            ManaCost = TomlHelpers.GetFloatOrDefault(table, "manaCost", src, 0f),
            SpecialManaCost = TomlHelpers.GetFloatOrDefault(table, "specialManaCost", src, 0f),
            ManaStatId = TomlHelpers.GetStringOrNull(table, "manaStatId", src) ?? "Mana",
            SpellTome = ParseSpellTome(TomlHelpers.GetTableOrNull(table, "spellTome", src), context, log)
        };
    }

    private static IReadOnlyList<DamageRangeTomlModel> ParseDamage(
        TomlTable weaponTable, RomodContentParseContext context, IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(weaponTable, "damage", context.ArchiveRelativePath + " [equipment.weapon]");
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[equipment.weapon.damage]]";
        var list = new List<DamageRangeTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownDamageKeys, src, context.PackageId, log);
            list.Add(new DamageRangeTomlModel
            {
                Type = TomlHelpers.RequireString(entry, "type", src),
                Min = TomlHelpers.GetFloatOrDefault(entry, "min", src, 0f),
                Max = TomlHelpers.GetFloatOrDefault(entry, "max", src, 0f)
            });
        }
        return list;
    }

    private static ShieldTomlModel? ParseShield(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment.shield]";
        TomlHelpers.WarnUnknownKeys(table, KnownShieldKeys, src, context.PackageId, log);

        return new ShieldTomlModel
        {
            BlockStrength = TomlHelpers.GetFloatOrDefault(table, "blockStrength", src, 0f),
            BlockArcSize = TomlHelpers.GetFloatOrDefault(table, "blockArcSize", src, 0f),
            StrongBlockArcSize = TomlHelpers.GetFloatOrDefault(table, "strongBlockArcSize", src, 0f),
            EnterCost = TomlHelpers.GetFloatOrDefault(table, "enterCost", src, 0f)
        };
    }

    private static SpellTomeTomlModel? ParseSpellTome(TomlTable? table, RomodContentParseContext context, IRomodLog log)
    {
        if (table is null)
        {
            return null;
        }

        var src = context.ArchiveRelativePath + " [equipment.weapon.spellTome]";
        TomlHelpers.WarnUnknownKeys(table, KnownSpellTomeKeys, src, context.PackageId, log);

        return new SpellTomeTomlModel
        {
            SpellId = TomlHelpers.RequireString(table, "spellId", src),
            ChargedSpellId = TomlHelpers.GetStringOrNull(table, "chargedSpellId", src),
            ChargeTime = TomlHelpers.GetFloatOrDefault(table, "chargeTime", src, 1f),
            Target = TomlHelpers.GetStringOrNull(table, "target", src) ?? "Self",
            ChargedTarget = TomlHelpers.GetStringOrNull(table, "chargedTarget", src) ?? "Self"
        };
    }
}
