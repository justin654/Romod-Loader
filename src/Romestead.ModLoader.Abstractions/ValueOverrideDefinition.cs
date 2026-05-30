namespace Romestead.ModLoader;

public sealed class ValueOverrideDefinition
{
    public List<EntityHealthOverrideDefinition> EntityHealth { get; init; } = [];
    public List<ItemValueOverrideDefinition> Items { get; init; } = [];
}

public sealed class EntityHealthOverrideDefinition
{
    public required Guid BaseId { get; init; }
    public required float MaxHealth { get; init; }
}

public sealed class ItemValueOverrideDefinition
{
    public required string Id { get; init; }
    public int? MaxStackSize { get; init; }
    public int? Tier { get; init; }
    public WeaponValueOverrideDefinition? Weapon { get; init; }
}

public sealed class WeaponValueOverrideDefinition
{
    public List<DamageRange> Damage { get; init; } = [];
    public float? SwingTimer { get; init; }
    public float? BaseAttackRange { get; init; }
    public float? BaseKnockback { get; init; }
    public float? EnergyCost { get; init; }
    public float? SpecialEnergyCost { get; init; }
    public float? StunPower { get; init; }
    public float? MovementFactor { get; init; }
}
