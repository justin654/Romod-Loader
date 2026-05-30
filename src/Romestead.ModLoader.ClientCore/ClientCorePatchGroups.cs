using HarmonyLib;
using Romestead.ModLoader;
using Romestead.StartupHook;

namespace Romestead.ModLoader.ClientCore;

internal static class ClientCorePatchGroups
{
    internal static IReadOnlyList<PatchGroupDefinition> Create() =>
    [
        new(
            "client.content.placeables",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks custom placeable entity art refresh.",
                harmony,
                typeof(PlaceableEntityArtPatch),
                typeof(PlaceableEntityBaseArtPatch),
                typeof(PlaceableCauldronRenderPatch))),
        new(
            "client.content.icons",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes("Hooks icon injection.", harmony, typeof(IconDataBaseAddIconsPatch))),
        new(
            "client.content.equipment-display",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks custom equipment display and player skin injection.",
                harmony,
                typeof(CharacterDisplayDataBaseAddCharacterDisplayDataPatch),
                typeof(PlayerSkinManagerSetupPatch))),
        new(
            "client.content.skills",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks skill injection and experience effects.",
                harmony,
                typeof(SkillsDataBaseAddDataPatch),
                typeof(SkillsManagerAddExperienceToSkillTypePatch))),
        new(
            "client.content.player-classes",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes("Hooks player-class injection.", harmony, typeof(PlayerClassDataBaseAddPlayerClassPatch))),
        new(
            "client.content.text",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks mod text resolution.",
                harmony,
                typeof(StringDefinitionsGetStringPatch),
                typeof(StringDefinitionsTryGetStringPatch))),
        new(
            "client.content.recipes",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks station recipe filtering and fallback station lookup.",
                harmony,
                typeof(ItemRecipeManagerGetLocalPlayerRecipesForStationPatch),
                typeof(CraftingStationDataBaseGetCraftingStationOrMissingPatch))),
        new(
            "client.gameplay.aggro",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks aggro tuning patches.",
                harmony,
                typeof(CircularAggroFieldCheckIfLostTargetPatch),
                typeof(LineOfSightAggroFieldCheckIfLostTargetPatch),
                typeof(AggressiveComponentUpdatePatch),
                typeof(AggressiveComponentCheckIfAllyHasTargetPatch),
                typeof(AggressiveComponentUpdateThreatDecayPatch))),
        new(
            "client.maps.aliases",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks map alias/discovery patches.",
                harmony,
                typeof(WorldLoaderLoadWorldPatch),
                typeof(OldInteriorWorldHandlerLoadDiscoveryPatch))),
        new(
            "client.maps.io-redirects",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes(
                "Hooks map file IO redirects.",
                harmony,
                typeof(FileExistsRedirectPatch),
                typeof(FileOpenReadRedirectPatch),
                typeof(XmlDocumentLoadRedirectPatch))),
        new(
            "client.gameplay.mana",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(
                    harmony,
                    typeof(ManaTracker.LocalPlayerFlagsHasFlagPatch),
                    typeof(ManaTracker.WeaponAttackStateSetPatch),
                    typeof(ManaTracker.SpellTomeHoldingStateCheckCanEnterPatch),
                    typeof(ManaTracker.SpellTomeHoldingStateOnLeavePatch),
                    typeof(ManaTracker.EntitySystemUpdateAllPatch));
                return ManaTracker.InstallInternalStatePatches(harmony);
            }),
        new(
            "client.ui.mana-bar",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes("Hooks mana bar rendering.", harmony, typeof(ManaBarRenderer.HealthBarInternalDrawPatch))),
        new(
            "client.api.lifecycle",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(harmony, typeof(EngineLoadContentPatch));
                return PatchGroupExecutionResult.SuccessResult(
                    "Hooks game-ready lifecycle event.",
                    new CapabilityPatchState(ModCapabilityId.Lifecycle, ModCapabilityState.Available, "GameReady lifecycle hook installed."));
            },
            ModCapabilityId.Lifecycle),
        new(
            "client.api.scene",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(
                    harmony,
                    typeof(MainMenuLoadContentPatch),
                    typeof(LoadingScreenLoadContentPatch),
                    typeof(GameplayLoadContentPatch));
                return PatchGroupExecutionResult.SuccessResult(
                    "Hooks scene lifecycle notifications.",
                    new CapabilityPatchState(ModCapabilityId.Scene, ModCapabilityState.Available, "Scene change hooks installed."));
            },
            ModCapabilityId.Scene),
        new(
            "client.api.worldmap",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(
                    harmony,
                    typeof(ExteriorWorldHandlerSyncFullGameStatePatch),
                    typeof(ExteriorWorldHandlerUpdateWorldMapPatch));
                return WorldMapLoadCallbackPatchInstaller.Patch(harmony);
            },
            ModCapabilityId.WorldMap),
        new(
            "client.api.overlays",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(
                    harmony,
                    typeof(LoadingScreenUiShowUiPatch),
                    typeof(LoadingScreenUpdatePatch),
                    typeof(LoadingScreenDeactivatedPatch));
                return PatchGroupExecutionResult.SuccessResult(
                    "Hooks loading-screen overlay host.",
                    new CapabilityPatchState(ModCapabilityId.Overlays, ModCapabilityState.Available, "Overlay host hooks installed."));
            },
            ModCapabilityId.Overlays),
        new(
            "client.api.gameplay-desktop",
            ModLoaderHostKind.Client,
            static harmony =>
            {
                PatchGroupInstaller.PatchClasses(
                    harmony,
                    typeof(StandardModeEnterPatch),
                    typeof(StandardModeUpdatePatch),
                    typeof(StandardModeDrawInWorldUiPatch),
                    typeof(StandardModeLeavePatch),
                    typeof(LiveEditorNormalStateCheckMainAttackPatch),
                    typeof(LiveEditorNormalStateCheckAltAttackPatch),
                    typeof(LiveEditorDashStateCheckMainAttackPatch),
                    typeof(LiveEditorDashStateCheckAltAttackPatch));
                return PatchGroupExecutionResult.SuccessResult(
                    "Hooks gameplay desktop hosts for mod windows and crafting UI.",
                    new CapabilityPatchState(ModCapabilityId.Windows, ModCapabilityState.Available, "Gameplay desktop window host installed."),
                    new CapabilityPatchState(ModCapabilityId.CraftingUi, ModCapabilityState.Available, "Gameplay desktop crafting host installed."));
            }),
        new(
            "client.ui.pause-menu",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes("Hooks pause-menu settings integration.", harmony, typeof(PauseMenuWindowBuildUiPatch))),
        new(
            "client.debug.debug-wand",
            ModLoaderHostKind.Client,
            static harmony => PatchTypes("Hooks debug wand use interception.", harmony, typeof(ItemInstanceManagerTryUseItemPatch)))
    ];

    private static PatchGroupExecutionResult PatchTypes(string message, Harmony harmony, params Type[] patchTypes)
    {
        PatchGroupInstaller.PatchClasses(harmony, patchTypes);
        return PatchGroupExecutionResult.SuccessResult(message);
    }
}
