namespace Romestead.RomodFormat.Content;

/// <summary>
/// Lookup table from <see cref="RomodContentKind"/> to its parser. Default
/// instance is populated with the eight shipped parsers; tests and the CLI
/// can build their own with a subset.
/// </summary>
public sealed class RomodContentParserRegistry
{
    private readonly Dictionary<RomodContentKind, IRomodContentParser> _parsers = new();

    public static RomodContentParserRegistry CreateDefault()
    {
        var r = new RomodContentParserRegistry();
        r.Register(new Types.ItemTomlParser());
        r.Register(new Types.RecipeTomlParser());
        r.Register(new Types.IconTomlParser());
        r.Register(new Types.StatTomlParser());
        r.Register(new Types.SkillTomlParser());
        r.Register(new Types.SkillEffectTomlParser());
        r.Register(new Types.PlayerClassTomlParser());
        r.Register(new Types.ValueOverrideTomlParser());
        r.Register(new Types.TextTomlParser());
        r.Register(new Types.AggroTuningTomlParser());
        r.Register(new Types.CraftingStationTomlParser());
        r.Register(new Types.PlaceableTomlParser());
        r.Register(new Types.MapTomlParser());
        return r;
    }

    public void Register(IRomodContentParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        if (!_parsers.TryAdd(parser.Kind, parser))
        {
            throw new InvalidOperationException(
                $"A parser for content kind '{parser.Kind}' is already registered.");
        }
    }

    public bool TryGet(RomodContentKind kind, out IRomodContentParser parser)
    {
        return _parsers.TryGetValue(kind, out parser!);
    }

    public IEnumerable<RomodContentKind> KnownKinds => _parsers.Keys;
}
