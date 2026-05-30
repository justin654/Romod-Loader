namespace Romestead.ModLoader;

public sealed class AggroTuningDefinition
{
    public required string Id { get; init; }
    public required AggroTuningType Type { get; init; }

    /// <summary>
    /// Meaning depends on <see cref="Type"/>:
    /// MaxLossRadiusTiles = tile count,
    /// LossRadiusMultiplier = multiplier on loss radius,
    /// ThreatDecayMultiplier = multiplier on threat decay rate.
    /// Ignored for DisableAllyChainAggro.
    /// </summary>
    public float Value { get; init; } = 1f;

    /// <summary>
    /// When false, this rule does not alter boss aggro unless explicitly enabled.
    /// </summary>
    public bool ApplyToBosses { get; init; }
}

public enum AggroTuningType
{
    /// <summary>Cap how far (in tiles) a target can be before enemies drop aggro.</summary>
    MaxLossRadiusTiles,

    /// <summary>Multiply the entity's configured loss radius. Values below 1 shorten leash range.</summary>
    LossRadiusMultiplier,

    /// <summary>Stop nearby allies from keeping this enemy aggro'd after you leave leash range.</summary>
    DisableAllyChainAggro,

    /// <summary>Multiply passive threat decay speed while out of combat.</summary>
    ThreatDecayMultiplier
}
