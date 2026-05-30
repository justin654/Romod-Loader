namespace Romestead.ModLoader;

public interface IContentRegistry
{
    IItemRegistry Items { get; }
    IRecipeRegistry Recipes { get; }
    ITextRegistry Text { get; }
    IIconRegistry Icons { get; }
    ISkillRegistry Skills { get; }
    ISkillEffectRegistry SkillEffects { get; }
    IPlayerClassRegistry PlayerClasses { get; }
    IAggroTuningRegistry AggroTuning { get; }
    IStatRegistry Stats { get; }
    ICraftingStationRegistry CraftingStations { get; }
    IMapRegistry Maps { get; }
    IPlaceableRegistry Placeables { get; }
    IValueOverrideRegistry ValueOverrides { get; }
}
