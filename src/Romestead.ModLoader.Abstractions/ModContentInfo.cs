namespace Romestead.ModLoader;

public sealed record ModContentInfo(
    string ModId,
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<string> RecipeIds,
    IReadOnlyList<string> TextIds,
    IReadOnlyList<string> IconIds,
    IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> SkillEffectIds,
    IReadOnlyList<string> PlayerClassIds,
    IReadOnlyList<string> AggroTuningIds);
