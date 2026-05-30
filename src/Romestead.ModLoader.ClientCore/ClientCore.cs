using HarmonyLib;
using System.Reflection;
using CandideServer.Database;
using CandideServer.Entities.Modules;
using Candide.Database;
using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.Input;
using Candide.CandideUI.PauseMenuUi;
using Candide;
using Candide.GameModels.Managers;
using Candide.MainMenu;
using Candide.Save;
using Candide.Scene;
using Candide.World;
using CandideCreator.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Romestead.ModLoader;
using Romestead.StartupHook;
using Shared.Data;
using Shared.Data.DataModels;
using Shared.Entity;
using Shared.Entity.Aggro;
using Shared.Entity.Components;
using Shared.Models.Crafting;
using Shared.Models.Items;
using Shared.Models.Player;
using Shared.Text;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Client-only loader infrastructure. Installs Harmony patches for the client-side
/// integration points (UI, scenes, icons, skills, player classes, recipe filtering,
/// aggro tuning) and wires up the lifecycle + world map services. Loaded explicitly
/// by the StartupHook on client hosts — not discoverable as a mod.
/// </summary>
public static class ClientCore
{
    public static void Install(IModLogger logger, IModApiResolver apis)
    {
        CoreState.Logger = logger;
        CoreState.Lifecycle = ModRegistries.Lifecycle;
        ModUiSettingsHost.EnsureBuiltInPagesRegistered();
        if (!apis.TryGet<IWorldMapApi>(out var worldMapApi) || worldMapApi is not WorldMapApi worldMap)
        {
            throw new InvalidOperationException("IWorldMapApi is not backed by the expected WorldMapApi service.");
        }
        CoreState.WorldMap = worldMap;
        CoreState.WorldMap.Logger = logger;
        CoreState.WorldMap.RefreshReadyState();

        ManaTracker.Install(logger);
        ManaBarRenderer.Install(logger);

        var harmony = new Harmony("romestead.modloader.clientcore");
        PatchGroupInstaller.Install(logger, harmony, ClientCorePatchGroups.Create());
        CoreState.WorldMap.RefreshReadyState();
        // NormalState is internal — Harmony can't pick it up via [HarmonyPatch] type
        // arg, so wire its patches explicitly with TargetMethod() factories.

        logger.Info("Client core ready. Lifecycle + typed APIs + client content patches active.");
    }
}

internal static class CoreState
{
    public static IModLogger? Logger { get; set; }
    public static ModLifecycle? Lifecycle { get; set; }
    public static WorldMapApi? WorldMap { get; set; }
    public static bool IconsInjected { get; set; }
    public static bool SkillsInjected { get; set; }
    public static bool PlayerClassesInjected { get; set; }

    public static void InjectIcons(ref IEnumerable<IconData> newIcons)
    {
        if (IconsInjected)
        {
            return;
        }

        IconsInjected = true;
        var vanillaIcons = newIcons.ToList();
        var iconIdsInBatch = new HashSet<string>(vanillaIcons.Select(icon => icon.Id), StringComparer.Ordinal);
        var modIcons = new List<IconData>();

        foreach (var def in ModRegistries.Icons.Pending)
        {
            if (!def.ReplaceExisting &&
                (iconIdsInBatch.Contains(def.Id) || IconDataBase.GetIconOrNull(def.Id) is not null))
            {
                Logger?.Warn($"Icon {def.Id} already exists; skipping.");
                RegisterContentDiagnostic(
                    GetModIdForIcon(def.Id),
                    "Icon",
                    def.Id,
                    "SkippedDuplicate",
                    "An icon with this ID already exists in the vanilla batch or database.");
                continue;
            }

            try
            {
                modIcons.Add(CreateIconData(def));
                iconIdsInBatch.Add(def.Id);
                RegisterContentDiagnostic(
                    GetModIdForIcon(def.Id),
                    "Icon",
                    def.Id,
                    def.ReplaceExisting ? "InjectedIconOverride" : "InjectedIntoIconLoad",
                    $"Loaded texture from '{def.TexturePath}'.");
            }
            catch (Exception ex)
            {
                Logger?.Error($"Failed to load icon {def.Id} from {def.TexturePath}.", ex);
                RegisterContentDiagnostic(
                    GetModIdForIcon(def.Id),
                    "Icon",
                    def.Id,
                    "Failed",
                    ex.Message);
            }
        }

        if (modIcons.Count == 0)
        {
            return;
        }

        vanillaIcons.AddRange(modIcons);
        newIcons = vanillaIcons;
        Logger?.Info($"Injected {modIcons.Count} mod icon(s) into vanilla icon load.");
    }

    public static void InjectPlayerClasses(ref List<PlayerClassModel> playerClasses)
    {
        if (PlayerClassesInjected)
        {
            return;
        }

        PlayerClassesInjected = true;
        var classIdsInBatch = new HashSet<string>(playerClasses.Select(playerClass => playerClass.Id), StringComparer.Ordinal);
        var modClasses = new List<PlayerClassModel>();

        foreach (var def in ModRegistries.PlayerClasses.Pending)
        {
            if (classIdsInBatch.Contains(def.Id) || PlayerClassDataBase.GetPlayerClassOrNull(def.Id) is not null)
            {
                Logger?.Warn($"Player class {def.Id} already exists; skipping.");
                RegisterContentDiagnostic(
                    GetModIdForPlayerClass(def.Id),
                    "PlayerClass",
                    def.Id,
                    "SkippedDuplicate",
                    "A player class with this ID already exists in the vanilla batch or database.");
                continue;
            }

            modClasses.Add(CreatePlayerClassModel(def));
            classIdsInBatch.Add(def.Id);
            RegisterContentDiagnostic(
                GetModIdForPlayerClass(def.Id),
                "PlayerClass",
                def.Id,
                "InjectedIntoPlayerClassLoad",
                $"Added with bonus skill '{def.BonusSkill}'.");
        }

        if (modClasses.Count == 0)
        {
            return;
        }

        playerClasses.AddRange(modClasses);
        Logger?.Info($"Injected {modClasses.Count} mod player class(es) into vanilla player class load.");
    }

    public static void InjectSkills(ref List<SkillData> skills)
    {
        if (SkillsInjected)
        {
            return;
        }

        SkillsInjected = true;
        var skillIdsInBatch = new HashSet<string>(skills.Select(skill => skill.Id), StringComparer.Ordinal);
        var modSkills = new List<SkillData>();

        foreach (var def in ModRegistries.Skills.Pending)
        {
            if (skillIdsInBatch.Contains(def.Id) || SkillsDataBase.GetSkillOrNull(def.Id) is not null)
            {
                Logger?.Warn($"Skill {def.Id} already exists; skipping.");
                RegisterContentDiagnostic(
                    GetModIdForSkill(def.Id),
                    "Skill",
                    def.Id,
                    "SkippedDuplicate",
                    "A skill with this ID already exists in the vanilla batch or database.");
                continue;
            }

            modSkills.Add(CreateSkillData(def));
            skillIdsInBatch.Add(def.Id);
            RegisterContentDiagnostic(
                GetModIdForSkill(def.Id),
                "Skill",
                def.Id,
                "InjectedIntoSkillLoad",
                $"Added with icon '{def.Icon}'.");
        }

        if (modSkills.Count == 0)
        {
            return;
        }

        skills.AddRange(modSkills);
        Logger?.Info($"Injected {modSkills.Count} mod skill(s) into vanilla skill load.");
    }

    public static void ApplySkillExperienceEffects(PlayerCharacterModel playerCharacter, string skillId, ref float experience)
    {
        if (experience <= 0 || ModRegistries.SkillEffects.Pending.Count == 0)
        {
            return;
        }

        var multiplier = 1.0f;
        foreach (var effect in ModRegistries.SkillEffects.Pending)
        {
            if (effect.Type != SkillEffectType.ExperienceGainMultiplier ||
                !string.Equals(effect.TargetSkillId, skillId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!playerCharacter.Skills.CharacterSkills.TryGetValue(effect.SkillId, out var sourceSkill) ||
                sourceSkill.Level <= 0)
            {
                continue;
            }

            multiplier += sourceSkill.Level * effect.ValuePerLevel;
            RegisterContentDiagnostic(
                GetModIdForSkillEffect(effect),
                "SkillEffect",
                GetSkillEffectDiagnosticId(effect),
                "Applied",
                $"Multiplied {skillId} experience by +{sourceSkill.Level * effect.ValuePerLevel:P0}.");
        }

        experience *= multiplier;
    }

    public static bool EvaluateTargetLost(IAggroField field, Vector2 position, Vector2 targetPosition, bool vanillaLost, EntityWrapper? entity)
    {
        var tuning = ModRegistries.AggroTuning.Pending;
        if (tuning.Count == 0 ||
            !tuning.Any(entry => entry.Type is AggroTuningType.MaxLossRadiusTiles or AggroTuningType.LossRadiusMultiplier))
        {
            return vanillaLost;
        }

        if (AggroBossDetection.IsBossEntity(entity) &&
            !tuning.Any(entry =>
                entry.ApplyToBosses &&
                entry.Type is AggroTuningType.MaxLossRadiusTiles or AggroTuningType.LossRadiusMultiplier))
        {
            return vanillaLost;
        }

        var distanceSquared = Vector2.DistanceSquared(position, targetPosition);
        var lossRadiusSquared = GetConfiguredLossRadiusSquared(field);

        foreach (var entry in tuning)
        {
            if (!ShouldApplyTuning(entry, entity))
            {
                continue;
            }

            switch (entry.Type)
            {
                case AggroTuningType.LossRadiusMultiplier when entry.Value > 0f:
                    lossRadiusSquared *= entry.Value;
                    break;
                case AggroTuningType.MaxLossRadiusTiles when entry.Value > 0f:
                {
                    var cappedRadiusSquared = TilesToWorldUnitsSquared(entry.Value);
                    lossRadiusSquared = lossRadiusSquared <= 0f
                        ? cappedRadiusSquared
                        : Math.Min(lossRadiusSquared, cappedRadiusSquared);
                    break;
                }
            }
        }

        return distanceSquared > lossRadiusSquared;
    }

    public static bool ShouldDisableAllyChainAggro(EntityWrapper? entity) =>
        ModRegistries.AggroTuning.Pending.Any(entry =>
            entry.Type == AggroTuningType.DisableAllyChainAggro &&
            ShouldApplyTuning(entry, entity));

    public static float ApplyThreatDecayMultiplier(float decayRate, EntityWrapper? entity)
    {
        if (ModRegistries.AggroTuning.Pending.Count == 0)
        {
            return decayRate;
        }

        var multiplier = 1f;
        foreach (var tuning in ModRegistries.AggroTuning.Pending)
        {
            if (tuning.Type != AggroTuningType.ThreatDecayMultiplier ||
                tuning.Value <= 0f ||
                !ShouldApplyTuning(tuning, entity))
            {
                continue;
            }

            multiplier *= tuning.Value;
        }

        return decayRate * multiplier;
    }

    private static bool ShouldApplyTuning(AggroTuningDefinition tuning, EntityWrapper? entity) =>
        !AggroBossDetection.IsBossEntity(entity) || tuning.ApplyToBosses;

    private static float GetConfiguredLossRadiusSquared(IAggroField field) =>
        field switch
        {
            CircularAggroField circular => circular.LossRadiusSquare,
            LineOfSightCheckingCircularAggroField lineOfSight => lineOfSight.LossRadiusSquare,
            _ => float.MaxValue
        };

    private static float TilesToWorldUnitsSquared(float tiles)
    {
        var tileSize = 16f;
        try
        {
            var config = Candide.GameModels.GameState.Config;
            if (config.TileSize.X > 0)
            {
                tileSize = config.TileSize.X;
            }
        }
        catch
        {
            // Gameplay config may not be ready yet; fall back to the default tile size.
        }

        var worldUnits = tiles * tileSize;
        return worldUnits * worldUnits;
    }

    public static void IncludeModRecipesForStation(string[]? stationIds, ref IEnumerable<ItemRecipe> recipes)
    {
        var injectedRecipes = SharedContentBootstrap.InjectedRecipes;
        if (injectedRecipes.Count == 0 || stationIds is null || stationIds.Length == 0)
        {
            return;
        }

        var visibleRecipes = recipes.ToList();
        var visibleIds = new HashSet<string>(visibleRecipes.Select(recipe => recipe.Id), StringComparer.Ordinal);
        var addedCount = 0;

        foreach (var recipe in injectedRecipes)
        {
            if (!IsRecipeForStation(recipe, stationIds) || !visibleIds.Add(recipe.Id))
            {
                continue;
            }

            visibleRecipes.Add(recipe);
            addedCount++;
            RegisterContentDiagnostic(
                GetModIdForRecipe(recipe.Id),
                "Recipe",
                recipe.Id,
                "VisibleInStationList",
                $"Added to station recipe result for: {string.Join(", ", stationIds)}.");
        }

        if (addedCount == 0)
        {
            return;
        }

        recipes = visibleRecipes;
        Logger?.Info($"Added {addedCount} mod recipe(s) to station recipe list: {string.Join(", ", stationIds)}.");
    }

    private static bool IsRecipeForStation(ItemRecipe recipe, string[] stationIds)
    {
        if (string.IsNullOrWhiteSpace(recipe.RequiredCraftingStation))
        {
            return true;
        }

        return stationIds.Contains(recipe.RequiredCraftingStation, StringComparer.Ordinal);
    }

    private static string GetModIdForItem(string itemId)
    {
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.ItemIds.Contains(itemId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    private static string GetModIdForPlaceable(ModPlaceableStation placeable) =>
        GetModIdForItem(placeable.Id);

    private static string GetModIdForRecipe(string recipeId)
    {
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.RecipeIds.Contains(recipeId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    private static string GetModIdForText(string textId)
    {
        var explicitTextOwner = ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.TextIds.Contains(textId, StringComparer.Ordinal))?.ModId;
        if (explicitTextOwner is not null)
        {
            return explicitTextOwner;
        }

        var item = ModRegistries.Items.Pending.FirstOrDefault(item =>
            string.Equals(item.NameTextId ?? item.Name, textId, StringComparison.Ordinal) ||
            string.Equals(item.DescriptionTextId ?? item.Description, textId, StringComparison.Ordinal));
        if (item is not null)
        {
            return GetModIdForItem(item.Id);
        }

        var placeable = ModRegistries.Placeables.Pending.FirstOrDefault(placeable =>
            IsPlaceableNameKey(placeable, textId) ||
            IsPlaceableDescriptionKey(placeable, textId));
        return placeable is null ? "<unknown>" : GetModIdForPlaceable(placeable);
    }

    private static string GetModIdForIcon(string iconId)
    {
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.IconIds.Contains(iconId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    private static string GetModIdForSkill(string skillId)
    {
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.SkillIds.Contains(skillId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    private static string GetModIdForSkillEffect(SkillEffectDefinition effect)
    {
        var effectId = GetSkillEffectDiagnosticId(effect);
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.SkillEffectIds.Contains(effectId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    private static string GetSkillEffectDiagnosticId(SkillEffectDefinition effect) =>
        $"{effect.SkillId}:{effect.Type}:{effect.TargetSkillId}";

    private static string GetModIdForPlayerClass(string playerClassId)
    {
        return ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.PlayerClassIds.Contains(playerClassId, StringComparer.Ordinal))?.ModId
            ?? "<unknown>";
    }

    public static bool TryGetText(string key, out string text, out string modId)
    {
        foreach (var def in ModRegistries.Text.Pending.Reverse())
        {
            if (string.Equals(def.Id, key, StringComparison.Ordinal))
            {
                text = def.Text;
                modId = GetModIdForText(def.Id);
                return true;
            }
        }

        foreach (var def in ModRegistries.Items.Pending)
        {
            if (string.Equals(def.NameTextId ?? def.Name, key, StringComparison.Ordinal) ||
                string.Equals($"{def.Id}*item:name", key, StringComparison.Ordinal))
            {
                text = def.Name;
                modId = GetModIdForItem(def.Id);
                return true;
            }

            if (string.Equals(def.DescriptionTextId ?? def.Description, key, StringComparison.Ordinal) ||
                string.Equals($"{def.Id}*item:description", key, StringComparison.Ordinal))
            {
                text = def.Description;
                modId = GetModIdForItem(def.Id);
                return true;
            }
        }

        foreach (var placeable in ModRegistries.Placeables.Pending)
        {
            if (IsPlaceableNameKey(placeable, key))
            {
                text = placeable.DisplayName;
                modId = GetModIdForPlaceable(placeable);
                return true;
            }

            if (IsPlaceableDescriptionKey(placeable, key))
            {
                text = GetPlaceableDescription(placeable);
                modId = GetModIdForPlaceable(placeable);
                return true;
            }
        }

        foreach (var def in ModRegistries.PlayerClasses.Pending)
        {
            if (string.Equals(def.NameTextId ?? def.Name, key, StringComparison.Ordinal) ||
                string.Equals($"{def.Id}*player_class:name", key, StringComparison.Ordinal))
            {
                text = def.Name;
                modId = GetModIdForPlayerClass(def.Id);
                return true;
            }
        }

        foreach (var def in ModRegistries.Skills.Pending)
        {
            if (string.Equals(def.NameTextId ?? def.Name, key, StringComparison.Ordinal) ||
                string.Equals($"{def.Id}*skills:name", key, StringComparison.Ordinal))
            {
                text = def.Name;
                modId = GetModIdForSkill(def.Id);
                return true;
            }

            if (string.Equals(def.DescriptionTextId ?? def.Description, key, StringComparison.Ordinal) ||
                string.Equals($"{def.Id}*skills:description", key, StringComparison.Ordinal))
            {
                text = def.Description;
                modId = GetModIdForSkill(def.Id);
                return true;
            }
        }

        text = "";
        modId = "<unknown>";
        return false;
    }

    private static bool IsPlaceableNameKey(ModPlaceableStation placeable, string key) =>
        string.Equals(placeable.DisplayName, key, StringComparison.Ordinal) ||
        string.Equals($"{placeable.Id}*item:name", key, StringComparison.Ordinal) ||
        string.Equals($"{placeable.ConstructionId}*construction:name", key, StringComparison.Ordinal);

    private static bool IsPlaceableDescriptionKey(ModPlaceableStation placeable, string key) =>
        string.Equals(placeable.Description, key, StringComparison.Ordinal) ||
        string.Equals($"{placeable.Id}*item:description", key, StringComparison.Ordinal) ||
        string.Equals($"{placeable.ConstructionId}*construction:description", key, StringComparison.Ordinal);

    private static string GetPlaceableDescription(ModPlaceableStation placeable) =>
        string.IsNullOrWhiteSpace(placeable.Description)
            ? $"Place this to craft at the {placeable.DisplayName}."
            : placeable.Description;

    private static SkillData CreateSkillData(SkillDefinition def)
    {
        return new SkillData
        {
            Id = def.Id,
            Name = (StringId)(def.NameTextId ?? def.Name),
            Description = (StringId)(def.DescriptionTextId ?? def.Description),
            Icon = def.Icon,
            Value = def.Value,
            ExperienceGainFactor = def.ExperienceGainFactor
        };
    }

    private static PlayerClassModel CreatePlayerClassModel(PlayerClassDefinition def)
    {
        return new PlayerClassModel
        {
            Id = def.Id,
            DisplayName = (StringId)(def.NameTextId ?? def.Name),
            BonusSkill = def.BonusSkill,
            SkillBonuses = def.SkillBonuses.Select(bonus => (bonus.SkillId, bonus.Level)).ToList(),
            StartingClothes = def.StartingClothes.ToList(),
            StartingInventory = def.StartingInventory
                .Select(item => new ItemAmount { ItemId = item.ItemId, Amount = item.Amount })
                .ToList(),
            StartingFavourPoints = def.StartingFavourPoints
        };
    }

    private static IconData CreateIconData(IconDefinition def)
    {
        var filePath = Path.GetFullPath(def.TexturePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Icon texture file does not exist.", filePath);
        }

        var contentPath = string.IsNullOrWhiteSpace(def.ContentPath)
            ? $"romestead_modding/icons/{def.Id}"
            : def.ContentPath;
        var texture = LoadModIconTexture(filePath, contentPath);
        var spriteWidth = def.SpriteWidth > 0 ? def.SpriteWidth : texture.Width;
        var spriteHeight = def.SpriteHeight > 0 ? def.SpriteHeight : texture.Height;
        var spriteSheet = CandideCreator.Shared.Content.SpriteSheet(texture, spriteWidth, spriteHeight, Vector2.Zero);
        var icon = new Icon
        {
            SpriteSheet = spriteSheet,
            Frame = def.Frame
        };

        return new IconData
        {
            Id = def.Id,
            Variations = new Dictionary<IconFlag, Icon>
            {
                [IconFlag.Small] = icon,
                [IconFlag.Medium] = icon,
                [IconFlag.MediumOutline] = icon,
                [IconFlag.Large] = icon,
                [IconFlag.Huge] = icon
            }
        };
    }

    private static Texture2D LoadModIconTexture(string filePath, string contentPath)
    {
        var graphicsDevice = ResolveGraphicsDevice();
        if (graphicsDevice is null)
        {
            throw new InvalidOperationException("Graphics device is not available yet for mod icon loading.");
        }

        var texture = Texture2D.FromFile(graphicsDevice, filePath);
        texture.Name = contentPath;
        CandideCreator.Shared.Content.DirectContentMap[contentPath] = texture;
        return texture;
    }

    private static GraphicsDevice? ResolveGraphicsDevice()
    {
        if (Globals.GraphicsDevice is not null)
        {
            return Globals.GraphicsDevice;
        }

        var gameSceneGame = AccessTools.Field(typeof(GameSceneManager), "GameSceneGame")?.GetValue(null);
        return gameSceneGame is Game game ? game.GraphicsDevice : null;
    }

    private static void RegisterContentDiagnostic(string modId, string contentType, string contentId, string status, string detail)
    {
        ModRegistries.Diagnostics.RegisterContentDiagnostic(new ContentDiagnosticInfo(
            modId,
            contentType,
            contentId,
            status,
            detail));
    }
}

[HarmonyPatch(typeof(Candide.CandideEngine), "LoadContent")]
internal static class EngineLoadContentPatch
{
    private static void Postfix()
    {
        try
        {
            CoreState.Lifecycle?.RaiseGameReady();
            CoreState.WorldMap?.RefreshReadyState();
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("A GameReady handler threw.", ex);
        }
    }
}

[HarmonyPatch(typeof(IconDataBase), nameof(IconDataBase.AddIcons))]
internal static class IconDataBaseAddIconsPatch
{
    private static void Prefix(ref IEnumerable<IconData> __0) => CoreState.InjectIcons(ref __0);
}

[HarmonyPatch(typeof(SkillsDataBase), nameof(SkillsDataBase.AddData))]
internal static class SkillsDataBaseAddDataPatch
{
    private static void Prefix(ref List<SkillData> __0) => CoreState.InjectSkills(ref __0);
}

[HarmonyPatch(typeof(SkillsManager), nameof(SkillsManager.AddExperienceToSkillType))]
internal static class SkillsManagerAddExperienceToSkillTypePatch
{
    private static void Prefix(PlayerCharacterModel playerCharacter, string skillId, ref float experience) =>
        CoreState.ApplySkillExperienceEffects(playerCharacter, skillId, ref experience);
}

[HarmonyPatch(typeof(CircularAggroField), nameof(CircularAggroField.CheckIfLostTarget))]
internal static class CircularAggroFieldCheckIfLostTargetPatch
{
    private static void Postfix(CircularAggroField __instance, Vector2 position, Vector2 targetPosition, ref bool __result) =>
        __result = CoreState.EvaluateTargetLost(
            __instance,
            position,
            targetPosition,
            __result,
            AggressiveComponentUpdateContext.CurrentEntity);
}

[HarmonyPatch(typeof(LineOfSightCheckingCircularAggroField), nameof(LineOfSightCheckingCircularAggroField.CheckIfLostTarget))]
internal static class LineOfSightAggroFieldCheckIfLostTargetPatch
{
    private static void Postfix(LineOfSightCheckingCircularAggroField __instance, Vector2 position, Vector2 targetPosition, ref bool __result) =>
        __result = CoreState.EvaluateTargetLost(
            __instance,
            position,
            targetPosition,
            __result,
            AggressiveComponentUpdateContext.CurrentEntity);
}

[HarmonyPatch(typeof(AggressiveComponent), nameof(AggressiveComponent.Update))]
internal static class AggressiveComponentUpdatePatch
{
    private static void Prefix(EntityWrapper entity) => AggressiveComponentUpdateContext.Begin(entity);

    private static void Postfix() => AggressiveComponentUpdateContext.End();
}

internal static class AggressiveComponentUpdateContext
{
    [ThreadStatic]
    private static EntityWrapper? _currentEntity;

    internal static EntityWrapper? CurrentEntity => _currentEntity;

    internal static void Begin(EntityWrapper entity) => _currentEntity = entity;

    internal static void End() => _currentEntity = null;
}

[HarmonyPatch(typeof(AggressiveComponent))]
internal static class AggressiveComponentCheckIfAllyHasTargetPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(AggressiveComponent), "CheckIfAllyHasTarget");

    private static bool Prefix(EntityWrapper wrapper, ref bool __result)
    {
        if (!CoreState.ShouldDisableAllyChainAggro(wrapper))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(AggressiveComponent))]
internal static class AggressiveComponentUpdateThreatDecayPatch
{
    [ThreadStatic]
    private static float _savedThreatDecay;

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(AggressiveComponent), "UpdateThreatDecay");

    private static void Prefix(AggressiveComponent __instance)
    {
        _savedThreatDecay = __instance.ThreatDecay;
        __instance.ThreatDecay = CoreState.ApplyThreatDecayMultiplier(
            _savedThreatDecay,
            AggressiveComponentUpdateContext.CurrentEntity);
    }

    private static void Postfix(AggressiveComponent __instance) =>
        __instance.ThreatDecay = _savedThreatDecay;
}

[HarmonyPatch(typeof(PlayerClassDataBase), nameof(PlayerClassDataBase.AddPlayerClass))]
internal static class PlayerClassDataBaseAddPlayerClassPatch
{
    private static void Prefix(ref List<PlayerClassModel> __0) => CoreState.InjectPlayerClasses(ref __0);
}

[HarmonyPatch(typeof(StringDefinitions), nameof(StringDefinitions.GetString))]
internal static class StringDefinitionsGetStringPatch
{
    private static bool Prefix(string key, ref string __result)
    {
        if (!CoreState.TryGetText(key, out var text, out var modId))
        {
            return true;
        }

        __result = text;
        ModRegistries.Diagnostics.RegisterContentDiagnostic(new ContentDiagnosticInfo(
            modId,
            "Text",
            key,
            "Resolved",
            "Resolved by mod loader text registry."));
        return false;
    }
}

[HarmonyPatch(typeof(StringDefinitions), nameof(StringDefinitions.TryGetString))]
internal static class StringDefinitionsTryGetStringPatch
{
    private static bool Prefix(string key, ref string val, ref bool __result)
    {
        if (!CoreState.TryGetText(key, out var text, out var modId))
        {
            return true;
        }

        val = text;
        __result = true;
        ModRegistries.Diagnostics.RegisterContentDiagnostic(new ContentDiagnosticInfo(
            modId,
            "Text",
            key,
            "Resolved",
            "Resolved by mod loader text registry."));
        return false;
    }
}

[HarmonyPatch(typeof(ItemRecipeManager), nameof(ItemRecipeManager.GetLocalPlayerRecipesForStation))]
internal static class ItemRecipeManagerGetLocalPlayerRecipesForStationPatch
{
    private static void Postfix(string[] stationIds, ref IEnumerable<ItemRecipe> __result) =>
        CoreState.IncludeModRecipesForStation(stationIds, ref __result);
}

// Fallback so a mod station's crafting-window header resolves to its name + icon even if
// the shared AddCraftingStations injection did not populate DataMap on this host.
[HarmonyPatch(typeof(CraftingStationDataBase), nameof(CraftingStationDataBase.GetCraftingStationOrMissing))]
internal static class CraftingStationDataBaseGetCraftingStationOrMissingPatch
{
    private static void Postfix(string id, ref CraftingStationData __result)
    {
        if (__result is not null && !ReferenceEquals(__result, CraftingStationDataBase.MissingCraftingStationData))
        {
            return;
        }

        foreach (var def in ModRegistries.CraftingStations.Pending)
        {
            if (string.Equals(def.Id, id, StringComparison.Ordinal))
            {
                __result = new CraftingStationData
                {
                    Id = def.Id,
                    IconId = def.IconId,
                    Name = (StringId)def.Name
                };
                return;
            }
        }
    }
}

[HarmonyPatch(typeof(ExteriorWorldHandler), nameof(ExteriorWorldHandler.SyncFullGameState))]
internal static class ExteriorWorldHandlerSyncFullGameStatePatch
{
    private static void Postfix()
    {
        CoreState.WorldMap?.Logger?.Info("ExteriorWorldHandler.SyncFullGameState observed; full map reveal pending.");
        CoreState.WorldMap?.MarkPending();
    }
}

[HarmonyPatch(typeof(ExteriorWorldHandler))]
internal static class ExteriorWorldHandlerUpdateWorldMapPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(ExteriorWorldHandler), "UpdateWorldMap");

    private static void Postfix() => CoreState.WorldMap?.TryRevealPending();
}

internal static class WorldMapLoadCallbackPatchInstaller
{
    internal static PatchGroupExecutionResult Patch(Harmony harmony)
    {
        var callback = typeof(WorldMapSaveManager)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            .FirstOrDefault(method =>
                method.Name.Contains("TryLoadInWorldMap", StringComparison.Ordinal));

        if (callback is null)
        {
            return PatchGroupExecutionResult.SuccessResult(
                "Could not find the world map load callback; reveal may be delayed until gameplay.",
                new CapabilityPatchState(
                    ModCapabilityId.WorldMap,
                    ModCapabilityState.Degraded,
                    "World map load callback not found; reveal falls back to gameplay refresh."));
        }

        harmony.Patch(
            callback,
            postfix: new HarmonyMethod(typeof(WorldMapLoadedPatch), nameof(WorldMapLoadedPatch.Postfix)));

        return PatchGroupExecutionResult.SuccessResult(
            $"Patched world map load callback: {callback.DeclaringType?.FullName}.{callback.Name}",
            new CapabilityPatchState(
                ModCapabilityId.WorldMap,
                ModCapabilityState.Available,
                "World map callbacks and reveal hooks installed."));
    }
}

internal static class WorldMapLoadedPatch
{
    internal static void Postfix() => CoreState.WorldMap?.MarkPendingAfterSaveLoad();
}

internal static class SceneRaise
{
    public static void Raise(SceneInfo info)
    {
        try
        {
            CoreState.Lifecycle?.RaiseSceneChanged(info);
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error($"A SceneChanged handler threw for {info.Name}.", ex);
        }
    }
}

[HarmonyPatch(typeof(PauseMenuWindow), "BuildUi")]
internal static class PauseMenuWindowBuildUiPatch
{
    private const string SidebarButtonTagPrefix = "romestead.modloader.settings-button:";

    private static void Postfix(PauseMenuWindow __instance)
    {
        try
        {
            var listView = AccessTools.Field(typeof(PauseMenuWindow), "_listView")
                .GetValue(__instance) as CandideListView;
            if (listView is null)
            {
                return;
            }

            foreach (var entry in ModUiSettingsHost.BuildSidebarEntries())
            {
                var button = CreateSidebarButton(entry, __instance.SettingsPanel);
                listView.AddItem(button, false);
            }

            CoreState.Logger?.Info("Added mod loader sidebar entries to pause menu.");
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to add mod loader sidebar entries to pause menu.", ex);
        }
    }

    private static CandideRadioListItem CreateSidebarButton(ModSidebarEntryDefinition entry, SettingsMainPanel settingsPanel)
    {
        var button = new CandideRadioListItem(entry.Icon ?? "scroll:red", IconFlag.Small, 92)
        {
            Tag = SidebarButtonTagPrefix + entry.Id,
            Text = entry.Title,
            Padding = new CandideThickness(2, 2, 6, -3),
            Margin = new CandideThickness(1, 0, 1, 2),
            OnToggledSoundEvent = "event:/interface/main menu/ui_mainmenu_click"
        };
        button.OnToggledOnAction = () => ModUiSettingsHost.ShowPage(settingsPanel, entry.TargetPageId);
        return button;
    }
}

internal static class ModLoaderSettingsUi
{
    public static void Show(SettingsMainPanel settingsPanel)
    {
        ModUiSettingsHost.ShowPage(settingsPanel, ModUiSettingsHost.RootPageId);
    }

    public static void ShowDetails(SettingsMainPanel settingsPanel, string modId)
    {
        ModUiSettingsHost.ShowPage(settingsPanel, ModUiSettingsHost.GetModDetailPageId(modId));
    }

    public static void ShowLog(SettingsMainPanel settingsPanel)
    {
        ModUiSettingsHost.ShowPage(settingsPanel, ModUiSettingsHost.LogPageId);
    }
}

[HarmonyPatch(typeof(Candide.MainMenu.MainMenu), nameof(Candide.MainMenu.MainMenu.LoadContent))]
internal static class MainMenuLoadContentPatch
{
    private const string ModLoadedLabelTag = "romestead.modloader.loaded-label";

    private static void Postfix(MainMenu __instance)
    {
        MainMenuOverlay.AddModLoadedLabel(__instance.Desktop);
        SceneRaise.Raise(SceneInfo.MainMenu);
    }

    private static class MainMenuOverlay
    {
        public static void AddModLoadedLabel(CandideDesktop desktop)
        {
            try
            {
                if (desktop.ChildrenEnumerable.Any(child => child.Tag == ModLoadedLabelTag))
                {
                    return;
                }

                var label = new CandideTextLabel
                {
                    Tag = ModLoadedLabelTag,
                    Text = "Mod loader active",
                    TextStyle = CandideTextStyle.MainSmall,
                    TextColor = Color.LightGreen,
                    ShadowColor = Color.Black,
                    Width = 320,
                    Height = 24,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new CandideThickness
                    {
                        Right = 16,
                        Bottom = 42
                    }
                };

                desktop.AddChild(label, false);
                CoreState.Logger?.Info("Added mod loader status text to main menu.");
            }
            catch (Exception ex)
            {
                CoreState.Logger?.Error("Failed to add mod loader status text to main menu.", ex);
            }
        }
    }
}

[HarmonyPatch(typeof(Candide.Scenes.LoadingScreen.LoadingScreen), nameof(Candide.Scenes.LoadingScreen.LoadingScreen.LoadContent))]
internal static class LoadingScreenLoadContentPatch
{
    private static void Postfix() => SceneRaise.Raise(SceneInfo.LoadingScreen);
}

// The loading screen pumps its own frames on the UI thread while CandideEngine loads in the
// background, so mounting the overlay host here lets loader-owned overlays animate during load.
[HarmonyPatch(typeof(Candide.Scenes.LoadingScreen.LoadingScreenUi), nameof(Candide.Scenes.LoadingScreen.LoadingScreenUi.ShowUi))]
internal static class LoadingScreenUiShowUiPatch
{
    private static void Postfix(CandideDesktop desktop)
    {
        ModOverlayHost.Attach(desktop);
        LoadingModsScreen.Show();
    }
}

[HarmonyPatch(typeof(Candide.Scenes.LoadingScreen.LoadingScreen), nameof(Candide.Scenes.LoadingScreen.LoadingScreen.Update))]
internal static class LoadingScreenUpdatePatch
{
    private static void Postfix() => ModOverlayHost.Pump();
}

[HarmonyPatch(typeof(Candide.Scenes.LoadingScreen.LoadingScreen), "OnSceneDeactivated")]
internal static class LoadingScreenDeactivatedPatch
{
    private static void Postfix()
    {
        LoadingModsScreen.Hide();
        ModOverlayHost.Detach();
    }
}

/// <summary>
/// The loader's first overlay consumer: a "Loading Mods" panel shown on the loading screen that
/// lists the active mods. Drives the overlay through the same public <see cref="IModOverlayRegistry"/>
/// a mod would use, so it doubles as the reference implementation.
/// </summary>
internal static class LoadingModsScreen
{
    private const string OverlayId = "romestead.modloader.loading-mods";
    private static IModOverlayHandle? _handle;

    public static void Show()
    {
        try
        {
            _handle = ModRegistries.Overlays.Show(new ModOverlayDefinition
            {
                Id = OverlayId,
                Title = "Loading Mods",
                Placement = ModOverlayPlacement.Center,
                Sections = BuildSections()
            });
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to show the Loading Mods overlay.", ex);
        }
    }

    public static void Hide()
    {
        _handle?.Hide();
        _handle = null;
    }

    private static IReadOnlyList<ModSection> BuildSections()
    {
        var mods = ModRegistries.LoadedMods.Mods;
        var rows = new List<ModUiRow>
        {
            new ModProgressRow { Label = $"{mods.Count} mod(s) active", Fraction = null },
            new ModListRow
            {
                Label = "Loaded mods",
                Values = mods.Select(mod => $"{mod.Name} {mod.Version}").ToArray(),
                EmptyText = "No mods loaded"
            }
        };

        return [new ModSection { Title = "", Rows = rows }];
    }
}

[HarmonyPatch(typeof(Candide.Scene.GameSceneGame), "LoadContent")]
internal static class GameplayLoadContentPatch
{
    private static void Postfix() => SceneRaise.Raise(SceneInfo.Gameplay);
}

[HarmonyPatch(typeof(Candide.PlayerMode.StandardModeManager), "OnEntering")]
internal static class StandardModeEnterPatch
{
    private static void Postfix(Candide.PlayerMode.StandardModeManager __instance)
    {
        ModWindowHost.Attach(__instance.CandideDesktop);
        ModCraftingWindowHost.Attach(__instance.CandideDesktop);
    }
}

[HarmonyPatch(typeof(Candide.PlayerMode.StandardModeManager), "Update")]
internal static class StandardModeUpdatePatch
{
    private static void Prefix(Candide.PlayerMode.StandardModeManager __instance, GameTime gameTime)
    {
        if (CoreState.Logger is { } log)
        {
            MapMagicIntegration.Host?.UpdateWorldEditor(log, __instance.IsMouseOverGui, __instance.CandideDesktop.DesktopWidth);
        }
    }

    private static void Postfix(Candide.PlayerMode.StandardModeManager __instance, GameTime gameTime)
    {
        ModWindowHost.Pump(__instance.CandideDesktop);
        ModCraftingWindowHost.Pump(__instance.CandideDesktop);
        ModEquipmentHeldVfxHost.Update(gameTime);
        if (CoreState.Logger is { } log)
        {
            ModPlaceableBenchHost.DumpDiagnosticsOnce(log);
            ModPlaceableBenchHost.RefreshLiveEntityArt(log);
            DebugWandHost.EnsureGranted(log);
        }
    }
}

[HarmonyPatch(typeof(Candide.PlayerMode.StandardModeManager), "DrawInWorldUi")]
internal static class StandardModeDrawInWorldUiPatch
{
    private static void Postfix(SpriteBatch batch)
    {
        MapMagicIntegration.Host?.DrawInWorldUi(batch);
    }
}

[HarmonyPatch(typeof(Candide.PlayerMode.StandardModeManager), "OnLeaving")]
internal static class StandardModeLeavePatch
{
    private static void Postfix()
    {
        ModWindowHost.Detach();
        ModCraftingWindowHost.Detach();
        ModEquipmentHeldVfxHost.Clear();
        MapMagicIntegration.Host?.DisableWorldEditor(CoreState.Logger);
    }
}

internal static class LiveEditorNormalStateCheckMainAttackPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.NormalState:CheckMainAttack");

    private static bool Prefix(ref bool __result) => AllowAttack(ref __result);

    internal static bool AllowAttack(ref bool result)
    {
        if (MapMagicIntegration.Host?.Active != true)
        {
            return true;
        }

        result = false;
        return false;
    }
}

internal static class LiveEditorNormalStateCheckAltAttackPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.NormalState:CheckAltAttack");

    private static bool Prefix(ref bool __result) => LiveEditorNormalStateCheckMainAttackPatch.AllowAttack(ref __result);
}

internal static class LiveEditorDashStateCheckMainAttackPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.DashState:CheckMainAttack");

    private static bool Prefix(ref bool __result) => LiveEditorNormalStateCheckMainAttackPatch.AllowAttack(ref __result);
}

internal static class LiveEditorDashStateCheckAltAttackPatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.DashState:CheckAltAttack");

    private static bool Prefix(ref bool __result) => LiveEditorNormalStateCheckMainAttackPatch.AllowAttack(ref __result);
}
