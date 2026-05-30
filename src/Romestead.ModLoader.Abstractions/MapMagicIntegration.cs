namespace Romestead.ModLoader;

/// <summary>
/// Integration point for the optional MapMagic mod. Set during mod <see cref="IRomesteadMod.Initialize"/>.
/// </summary>
public static class MapMagicIntegration
{
    public static IMapMagicEditorHost? Host { get; set; }
}
