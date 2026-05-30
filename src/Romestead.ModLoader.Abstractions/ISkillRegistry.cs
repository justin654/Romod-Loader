namespace Romestead.ModLoader;

public interface ISkillRegistry
{
    IReadOnlyList<SkillDefinition> Pending { get; }

    void Register(SkillDefinition skill);
}
