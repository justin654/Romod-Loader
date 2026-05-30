namespace Romestead.ModLoader;

public interface IModContext
{
    string GameRoot { get; }
    string ModRoot { get; }
    string ModDirectory { get; }
    IModLogger Logger { get; }
    IModApiResolver Apis { get; }

    /// <summary>
    /// Register new items with the game.
    /// </summary>
    IItemRegistry Items { get; }

    /// <summary>
    /// Register new crafting recipes with the game.
    /// </summary>
    IRecipeRegistry Recipes { get; }

    /// <summary>
    /// Register localized text entries with the game.
    /// </summary>
    ITextRegistry Text { get; }

    /// <summary>
    /// Register custom icon entries with the game.
    /// </summary>
    IIconRegistry Icons { get; }

    /// <summary>
    /// Register character skills with the game.
    /// </summary>
    ISkillRegistry Skills { get; }

    /// <summary>
    /// Register declarative effects for character skills.
    /// </summary>
    ISkillEffectRegistry SkillEffects { get; }

    /// <summary>
    /// Register character creation classes with the game.
    /// </summary>
    IPlayerClassRegistry PlayerClasses { get; }

    /// <summary>
    /// Register new entity / citizen / world stats with the game.
    /// </summary>
    IStatRegistry Stats { get; }

    /// <summary>
    /// Register overrides for existing vanilla/mod content values.
    /// </summary>
    IValueOverrideRegistry ValueOverrides { get; }

    /// <summary>
    /// Register custom crafting stations (benches) with the game so mod recipes
    /// can have their own station identity (name + icon) in crafting windows.
    /// </summary>
    ICraftingStationRegistry CraftingStations { get; }

    /// <summary>
    /// Register placeable world objects (e.g. custom crafting benches) with the game.
    /// </summary>
    IPlaceableRegistry Placeables { get; }

    /// <summary>
    /// Register enemy aggro / behavior tuning with the game.
    /// </summary>
    IAggroTuningRegistry AggroTuning { get; }

    /// <summary>
    /// Register map file redirects and aliases with the game.
    /// </summary>
    IMapRegistry Maps { get; }

    /// <summary>
    /// Register declarative settings pages for the in-game mod loader UI.
    /// </summary>
    IModUiRegistry Ui { get; }

    /// <summary>
    /// Show declarative overlays (HUD panels, loading notices) on top of the active scene.
    /// Client-only; accessing this on a dedicated server throws.
    /// </summary>
    IModOverlayRegistry Overlays { get; }

    /// <summary>
    /// Open draggable in-game windows (crafting panels, dialogs) on the active gameplay desktop.
    /// Client-only; accessing this on a dedicated server throws.
    /// </summary>
    IModWindowRegistry Windows { get; }

    /// <summary>
    /// Open the game's native crafting windows (campfire/workbench style) for crafting stations.
    /// Client-only; accessing this on a dedicated server throws.
    /// </summary>
    IModCraftingRegistry Crafting { get; }

    /// <summary>
    /// Subscribe to game lifecycle events (game ready, scene changed).
    /// </summary>
    IModLifecycle Lifecycle { get; }
}
