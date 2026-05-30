namespace Romestead.ModLoader;

public interface ITextRegistry
{
    IReadOnlyList<TextDefinition> Pending { get; }

    void Register(TextDefinition text);
}
