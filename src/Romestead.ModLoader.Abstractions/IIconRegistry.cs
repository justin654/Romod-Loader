namespace Romestead.ModLoader;

public interface IIconRegistry
{
    IReadOnlyList<IconDefinition> Pending { get; }

    void Register(IconDefinition icon);
}
