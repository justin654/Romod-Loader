namespace Romestead.ModLoader;

public interface IValueOverrideRegistry
{
    IReadOnlyList<ValueOverrideDefinition> Pending { get; }
    void Register(ValueOverrideDefinition valueOverride);
}
