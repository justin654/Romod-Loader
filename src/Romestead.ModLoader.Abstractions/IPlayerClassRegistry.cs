namespace Romestead.ModLoader;

public interface IPlayerClassRegistry
{
    IReadOnlyList<PlayerClassDefinition> Pending { get; }

    void Register(PlayerClassDefinition playerClass);
}
