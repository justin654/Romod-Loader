namespace Romestead.ModLoader;

public interface IAggroTuningRegistry
{
    IReadOnlyList<AggroTuningDefinition> Pending { get; }

    void Register(AggroTuningDefinition tuning);
}
