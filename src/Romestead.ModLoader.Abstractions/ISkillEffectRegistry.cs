namespace Romestead.ModLoader;

public interface ISkillEffectRegistry
{
    IReadOnlyList<SkillEffectDefinition> Pending { get; }

    void Register(SkillEffectDefinition effect);
}
