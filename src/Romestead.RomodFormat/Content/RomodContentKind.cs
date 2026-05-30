namespace Romestead.RomodFormat.Content;

/// <summary>
/// The supported content-file kinds, identified by the extension pattern
/// before <c>.toml</c>. Adding a new kind here is a one-line change; you
/// then register a parser via <see cref="RomodContentParserRegistry.Register"/>
/// and a mapper in the runtime loader. The string value is the suffix
/// AFTER the dot (so <c>*.item.toml</c> → "item").
/// </summary>
public enum RomodContentKind
{
    Item,
    Recipe,
    Icon,
    Stat,
    Skill,
    SkillEffect,
    PlayerClass,
    ValueOverride,
    Text,
    AggroTuning,
    CraftingStation,
    Placeable,
    Map
}

public static class RomodContentKindExtensions
{
    public static string ToFileSuffix(this RomodContentKind kind) => kind switch
    {
        RomodContentKind.Item => "item",
        RomodContentKind.Recipe => "recipe",
        RomodContentKind.Icon => "icon",
        RomodContentKind.Stat => "stat",
        RomodContentKind.Skill => "skill",
        RomodContentKind.SkillEffect => "skill-effect",
        RomodContentKind.PlayerClass => "player-class",
        RomodContentKind.ValueOverride => "value-override",
        RomodContentKind.Text => "text",
        RomodContentKind.AggroTuning => "aggro-tuning",
        RomodContentKind.CraftingStation => "crafting-station",
        RomodContentKind.Placeable => "placeable",
        RomodContentKind.Map => "map",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static bool TryFromFileSuffix(string suffix, out RomodContentKind kind)
    {
        switch (suffix)
        {
            case "item": kind = RomodContentKind.Item; return true;
            case "recipe": kind = RomodContentKind.Recipe; return true;
            case "icon": kind = RomodContentKind.Icon; return true;
            case "stat": kind = RomodContentKind.Stat; return true;
            case "skill": kind = RomodContentKind.Skill; return true;
            case "skill-effect": kind = RomodContentKind.SkillEffect; return true;
            case "player-class": kind = RomodContentKind.PlayerClass; return true;
            case "value-override": kind = RomodContentKind.ValueOverride; return true;
            case "text": kind = RomodContentKind.Text; return true;
            case "aggro-tuning": kind = RomodContentKind.AggroTuning; return true;
            case "crafting-station": kind = RomodContentKind.CraftingStation; return true;
            case "placeable": kind = RomodContentKind.Placeable; return true;
            case "map": kind = RomodContentKind.Map; return true;
            default: kind = default; return false;
        }
    }
}
