namespace Romestead.ModLoader;

public sealed class SkillEffectDefinition
{
    public required string SkillId { get; init; }
    public required SkillEffectType Type { get; init; }
    public required string TargetSkillId { get; init; }
    public float ValuePerLevel { get; init; } = 0.05f;
}

public enum SkillEffectType
{
    ExperienceGainMultiplier
}
