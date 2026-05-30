namespace Romestead.ModLoader;

/// <summary>
/// Where an equippable item is worn or wielded. Mirrors the game's
/// equipment slot values.
/// </summary>
public enum EquipmentSlot
{
    Invalid = 0,
    Helmet = 1,
    Armor = 2,
    Boots = 3,
    Trinket = 4,
    Weapon = 6,
    Offhand = 7,
    LightSource = 9,
    LumberAxe = 10,
    Pickaxe = 11,
    FishingRod = 12,
    Ammunition = 13,
    Back = 14
}

/// <summary>
/// Material an equippable item is made of. Mirrors the game's tiers
/// (cosmetic + sometimes used in formulas).
/// </summary>
public enum EquipmentMaterial
{
    Flint = 0,
    Copper = 1,
    Bronze = 2,
    Iron = 3,
    Silver = 4,
    Gold = 5,
    Steel = 6,
    Cheat = 7,
    Rusted = 8
}

/// <summary>
/// Preset weapon class. The loader maps each preset to the matching
/// vanilla <c>WeaponClass</c> static (skill, type, name).
/// </summary>
public enum WeaponClassPreset
{
    Sword,
    Spear,
    Crossbow,
    Shield,
    Arrow,
    SpellTome,
    Dagger,
    Sledgehammer,
    Bow,
    Fists,
    GrapplingHook,
    Javelin,
    Quiver
}

/// <summary>
/// Damage channel. Slashing/Piercing/Bludgeoning are physical;
/// Pyro/Chloro/Aqua/Cosmo/Necro are elemental and route through the
/// game's magic resistances.
/// </summary>
public enum DamageTypeId
{
    Slashing,
    Piercing,
    Bludgeoning,
    Pyro,
    Chloro,
    Aqua,
    Cosmo,
    Necro
}

/// <summary>
/// A range of damage for a single channel. Min and Max are floats
/// (the game samples uniformly within the range).
/// </summary>
public sealed class DamageRange
{
    public required DamageTypeId Type { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
}

/// <summary>
/// One stat modification applied while the item is equipped.
/// Matches the game's <c>StatModificationData</c> shape.
/// </summary>
public sealed class StatBonusDefinition
{
    public required string StatId { get; init; }
    public float Additive { get; init; }
    public float AdditiveMultiplier { get; init; }
    public float BaseMultiplier { get; init; }
    public float BonusMultiplier { get; init; }
    public float Multiplier { get; init; } = 1f;
}

/// <summary>
/// Combat fields for a weapon. Set on <see cref="EquipmentDefinition.Weapon"/>.
/// </summary>
public sealed class WeaponStatsDefinition
{
    public required WeaponClassPreset Class { get; init; }
    public List<DamageRange> Damage { get; init; } = [];
    public float SwingTimer { get; init; }
    public float BaseAttackRange { get; init; }
    public float BaseKnockback { get; init; }
    public float EnergyCost { get; init; }
    public float SpecialEnergyCost { get; init; }
    public float StunPower { get; init; }
    public float MovementFactor { get; init; } = 1f;

    /// <summary>
    /// Mana consumed per tap-cast of this weapon. Zero means the weapon
    /// does not use mana. Mana must be a registered stat
    /// (see <see cref="IStatRegistry"/>). Cost is checked + deducted at
    /// attack start; if the wielder lacks the required mana, the attack
    /// is blocked entirely. Client-only enforcement, matching how vanilla
    /// handles <see cref="EnergyCost"/>.
    /// </summary>
    public float ManaCost { get; init; }

    /// <summary>
    /// Mana consumed per charged / special cast (e.g. a SpellTome's hold
    /// attack). Same enforcement rules as <see cref="ManaCost"/>.
    /// </summary>
    public float SpecialManaCost { get; init; }

    /// <summary>
    /// Stat ID the weapon consumes for its <see cref="ManaCost"/> /
    /// <see cref="SpecialManaCost"/>. Defaults to <c>"Mana"</c>. Override
    /// if you've registered a different magical-resource stat.
    /// </summary>
    public string ManaStatId { get; init; } = "Mana";

    /// <summary>
    /// Set to make the weapon cast a spell on use, like a vanilla scroll.
    /// Typically paired with <see cref="WeaponClassPreset.SpellTome"/> and
    /// <see cref="EquipmentSlot.Offhand"/>. The spell IDs must already exist
    /// in the game's spell database (custom mod spells are a future phase).
    /// </summary>
    public SpellTomeDefinition? SpellTome { get; init; }
}

/// <summary>
/// Where a cast spell aims. Matches the game's <c>Shared.Aura.Args.Target</c>.
/// </summary>
public enum SpellTarget
{
    Self = 0,
    Target = 1,
    TargetGround = 2,
    ProjectilePosition = 3
}

/// <summary>
/// Configures an offhand spell-casting weapon (or any weapon that should
/// fire a spell on use). The tap-cast fires immediately; the charged cast
/// fires after the user holds use for <see cref="ChargeTime"/> seconds.
/// Costs energy per cast (set on <see cref="WeaponStatsDefinition.EnergyCost"/>
/// and <see cref="WeaponStatsDefinition.SpecialEnergyCost"/>) but the
/// weapon itself is never consumed — same as a vanilla SpellTome scroll.
/// </summary>
public sealed class SpellTomeDefinition
{
    /// <summary>
    /// Spell ID fired on a tap (single click). Must exist in the game's
    /// <c>SpellDataBase</c> — point at a vanilla spell such as
    /// <c>item:scroll:bolt:3</c> (the Ember Scroll bolt).
    /// </summary>
    public required string SpellId { get; init; }

    /// <summary>
    /// Optional spell ID fired after holding use for <see cref="ChargeTime"/>
    /// seconds. Leave null for a tap-only weapon.
    /// </summary>
    public string? ChargedSpellId { get; init; }

    /// <summary>
    /// Seconds the user must hold use before the charged spell triggers.
    /// Ignored when <see cref="ChargedSpellId"/> is null.
    /// </summary>
    public float ChargeTime { get; init; } = 1f;

    public SpellTarget Target { get; init; } = SpellTarget.Self;
    public SpellTarget ChargedTarget { get; init; } = SpellTarget.Self;
}

/// <summary>
/// Defence fields for a shield. Set on <see cref="EquipmentDefinition.Shield"/>.
/// </summary>
public sealed class ShieldStatsDefinition
{
    public float BlockStrength { get; init; }
    public float BlockArcSize { get; init; }
    public float StrongBlockArcSize { get; init; }
    public float EnterCost { get; init; }
}

/// <summary>
/// Player skin tags used by Romestead's character display system.
/// Weapon-like held sprites normally use <see cref="Tool"/>; offhand-only
/// tools use <see cref="ToolOffhand"/> and shields use <see cref="Shield"/>.
/// </summary>
public static class EquipmentSkinTag
{
    public const int Tool = 8;
    public const int Shield = 9;
    public const int ToolOffhand = 11;
}

/// <summary>
/// SPAC tags that receive a player skin slice. For normal held weapons this
/// matches <see cref="EquipmentSkinTag.Tool"/>.
/// </summary>
public static class EquipmentSpacTag
{
    public const int Tool = 8;
    public const int Shield = 13;
    public const int ToolOffhand = 15;
}

/// <summary>
/// Palette row applied to a character-display fragment. Use an empty palette
/// list for full-color custom art.
/// </summary>
public sealed class EquipmentDisplayPaletteDefinition
{
    public required string PaletteId { get; init; }
    public int Row { get; init; }
}

/// <summary>
/// One visual fragment in an equipment display definition. A fragment can point
/// at an existing vanilla skin by leaving <see cref="TexturePath"/> null, or it
/// can register a custom sprite sheet by providing a texture path.
/// </summary>
public sealed class EquipmentDisplayFragmentDefinition
{
    /// <summary>
    /// Player skin tag referenced by <c>CharacterDisplayData</c>. Held swords,
    /// spears, daggers, bows, and hammers use <see cref="EquipmentSkinTag.Tool"/>.
    /// </summary>
    public int SkinTag { get; init; } = EquipmentSkinTag.Tool;

    /// <summary>
    /// Skin name referenced by <c>CharacterDisplayData</c>. For custom art this
    /// must be unique enough to avoid colliding with vanilla names like
    /// <c>Sword</c>, <c>Spear</c>, or <c>Bow</c>.
    /// </summary>
    public required string SkinName { get; init; }

    /// <summary>
    /// Optional PNG path for a custom player skin sheet. Held weapons should use
    /// the same layout as vanilla tool sheets: 48x48 frames in a 23x5 grid
    /// (1104x240 pixels). Leave null to reuse an existing vanilla skin name.
    /// </summary>
    public string? TexturePath { get; init; }

    /// <summary>
    /// Frame width for <see cref="TexturePath"/>. Vanilla held-tool sheets use 48.
    /// </summary>
    public int SpriteWidth { get; init; } = 48;

    /// <summary>
    /// Frame height for <see cref="TexturePath"/>. Vanilla held-tool sheets use 48.
    /// </summary>
    public int SpriteHeight { get; init; } = 48;

    /// <summary>
    /// SPAC tag to replace with this custom skin sheet. Normal held weapons use
    /// <see cref="EquipmentSpacTag.Tool"/>; shields use <see cref="EquipmentSpacTag.Shield"/>.
    /// </summary>
    public int SpacTag { get; init; } = EquipmentSpacTag.Tool;

    /// <summary>
    /// Whether this skin slice hides the base sprite for the same SPAC tag.
    /// Custom held weapons should usually leave this true.
    /// </summary>
    public bool HideBaseSkin { get; init; } = true;

    /// <summary>
    /// Layer order inside the target SPAC tag. Vanilla held swords use 1.
    /// </summary>
    public float Layer { get; init; } = 1f;

    /// <summary>
    /// Small depth offset used by some vanilla player skin slices. Leave at 0
    /// unless the sprite visibly sorts behind/in front of the wrong layer.
    /// </summary>
    public float DepthOffset { get; init; }

    /// <summary>
    /// Palette swaps applied by the character display system. Leave empty for
    /// full-color custom art loaded from <see cref="TexturePath"/>.
    /// </summary>
    public List<EquipmentDisplayPaletteDefinition> Palette { get; init; } = [];
}

/// <summary>
/// On-character visual definition for an equippable item. This registers the
/// game's display-data ID and, for fragments with a texture path, the player
/// skin slice that backs it.
/// </summary>
public sealed class EquipmentDisplayDefinition
{
    /// <summary>
    /// Optional explicit display ID. If omitted, the loader generates
    /// <c>cdd:mod:{itemId}</c> and assigns that to the item's DisplayId.
    /// </summary>
    public string? Id { get; init; }

    public List<EquipmentDisplayFragmentDefinition> Fragments { get; init; } = [];

    /// <summary>
    /// Optional SPAC tags hidden before fragments are applied. Usually empty for
    /// held weapons; useful for armor pieces that intentionally hide base skin.
    /// </summary>
    public List<int> SpacTagsToHide { get; init; } = [];
}

/// <summary>
/// Client-side visual effect attached to an equipped item. This is cosmetic
/// only; it is not synchronized or saved.
/// </summary>
public sealed class EquipmentHeldVfxDefinition
{
    /// <summary>
    /// Optional vanilla particle emitter ID to reuse, e.g. <c>flame_small</c>.
    /// Leave null to use only the attached light.
    /// </summary>
    public string? ParticleEmitterId { get; init; }

    /// <summary>
    /// Whether particle/light offsets rotate with the holder's facing direction.
    /// Held weapons usually want this true.
    /// </summary>
    public bool RotateWithEntityDirection { get; init; } = true;

    public float ParticleOffsetX { get; init; }
    public float ParticleOffsetY { get; init; }
    public float ParticleOffsetZ { get; init; } = 14f;

    /// <summary>
    /// Length of the particle spawn line in world units. Use roughly the blade
    /// length for weapon-edge effects.
    /// </summary>
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

public static class ModEquipmentDisplayIds
{
    public static string CreateForItem(string itemId) => $"cdd:mod:{itemId}";

    public static string? ResolveForItem(string itemId, EquipmentDefinition equipment)
    {
        if (!string.IsNullOrWhiteSpace(equipment.Display?.Id))
        {
            return equipment.Display.Id;
        }

        if (!string.IsNullOrWhiteSpace(equipment.DisplayId))
        {
            return equipment.DisplayId;
        }

        return equipment.Display is null ? null : CreateForItem(itemId);
    }
}

/// <summary>
/// Marks an <see cref="ItemDefinition"/> as equippable and carries the
/// per-slot data (stat bonuses, weapon stats, shield stats, auras).
/// Leave on <see cref="ItemDefinition.Equipment"/> as <c>null</c> for
/// non-equippable items.
/// </summary>
public sealed class EquipmentDefinition
{
    public required EquipmentSlot Slot { get; init; }
    public EquipmentSlot? ExtraSlot { get; init; }
    public EquipmentMaterial Material { get; init; } = EquipmentMaterial.Iron;
    public List<StatBonusDefinition> StatBonuses { get; init; } = [];
    public WeaponStatsDefinition? Weapon { get; init; }
    public ShieldStatsDefinition? Shield { get; init; }

    /// <summary>
    /// Aura ID applied to the wearer while equipped. Must already exist
    /// in the game's aura database (custom mod auras are not yet supported).
    /// </summary>
    public string? EntityAuraId { get; init; }

    /// <summary>
    /// Additional aura IDs applied alongside <see cref="EntityAuraId"/>.
    /// </summary>
    public List<string> ExtraEntityAuraIds { get; init; } = [];

    /// <summary>
    /// Optional override for the on-character display sprite ID. Leave
    /// null unless using a vanilla display id. Custom display definitions
    /// can omit this and let the loader generate <c>cdd:mod:{itemId}</c>.
    /// </summary>
    public string? DisplayId { get; init; }

    /// <summary>
    /// Optional custom on-character display data. Use this for custom held
    /// weapon/player equipment sprites.
    /// </summary>
    public EquipmentDisplayDefinition? Display { get; init; }

    /// <summary>
    /// Optional client-side cosmetic effect shown while the item is equipped.
    /// </summary>
    public EquipmentHeldVfxDefinition? HeldVfx { get; init; }
}
