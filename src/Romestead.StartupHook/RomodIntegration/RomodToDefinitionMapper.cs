using Romestead.ModLoader;
using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Content.Types;
using Romestead.RomodFormat.Package;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Converts <c>Romestead.RomodFormat</c> TOML models into the
/// game-facing <c>Romestead.ModLoader.*Definition</c> types that the
/// existing loader bootstrap drains into the game's databases.
///
/// Lives in the StartupHook assembly (not in RomodFormat) so that
/// RomodFormat can stay free of Romestead.ModLoader.Abstractions and
/// be reused by the CLI on machines with no game install.
/// </summary>
internal static class RomodToDefinitionMapper
{
    public sealed record MappedMapAlias(string Original, string Replacement);

    public sealed record MappedMapFile(string MapId, string SourcePath, MapFileFormat Format);

    public sealed record Mapped(
        IReadOnlyList<ItemDefinition> Items,
        IReadOnlyList<RecipeDefinition> Recipes,
        IReadOnlyList<IconDefinition> Icons,
        IReadOnlyList<StatDefinition> Stats,
        IReadOnlyList<SkillDefinition> Skills,
        IReadOnlyList<SkillEffectDefinition> SkillEffects,
        IReadOnlyList<PlayerClassDefinition> PlayerClasses,
        IReadOnlyList<ValueOverrideDefinition> ValueOverrides,
        IReadOnlyList<TextDefinition> Texts,
        IReadOnlyList<AggroTuningDefinition> AggroTuning,
        IReadOnlyList<CraftingStationDefinition> CraftingStations,
        IReadOnlyList<ModPlaceableStation> Placeables,
        IReadOnlyList<MappedMapAlias> MapAliases,
        IReadOnlyList<MappedMapFile> MapFiles);

    public static Mapped Map(RomodPackageDocument document, string packageAssetCacheRoot)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(packageAssetCacheRoot);

        var items = new List<ItemDefinition>();
        var recipes = new List<RecipeDefinition>();
        var icons = new List<IconDefinition>();
        var stats = new List<StatDefinition>();
        var skills = new List<SkillDefinition>();
        var skillEffects = new List<SkillEffectDefinition>();
        var playerClasses = new List<PlayerClassDefinition>();
        var valueOverrides = new List<ValueOverrideDefinition>();
        var texts = new List<TextDefinition>();
        var aggroTuning = new List<AggroTuningDefinition>();
        var craftingStations = new List<CraftingStationDefinition>();
        var placeables = new List<ModPlaceableStation>();
        var mapAliases = new List<MappedMapAlias>();
        var mapFiles = new List<MappedMapFile>();

        foreach (var entry in document.ContentEntries)
        {
            switch (entry.Model)
            {
                case ItemTomlModel m: items.Add(MapItem(m, entry, document.Manifest.Id, packageAssetCacheRoot)); break;
                case RecipeTomlModel m: recipes.Add(MapRecipe(m)); break;
                case IconTomlModel m: icons.Add(MapIcon(m, packageAssetCacheRoot)); break;
                case StatTomlModel m: stats.Add(MapStat(m, entry, document.Manifest.Id)); break;
                case SkillTomlModel m: skills.Add(MapSkill(m)); break;
                case SkillEffectTomlModel m: skillEffects.Add(MapSkillEffect(m, entry, document.Manifest.Id)); break;
                case PlayerClassTomlModel m: playerClasses.Add(MapPlayerClass(m)); break;
                case ValueOverrideTomlModel m: valueOverrides.Add(MapValueOverride(m, entry, document.Manifest.Id)); break;
                case TextTomlModel m: texts.Add(MapText(m)); break;
                case AggroTuningTomlModel m: aggroTuning.Add(MapAggroTuning(m, entry, document.Manifest.Id)); break;
                case CraftingStationTomlModel m: craftingStations.Add(MapCraftingStation(m)); break;
                case PlaceableTomlModel m: placeables.Add(MapPlaceable(m, entry, document.Manifest.Id, packageAssetCacheRoot)); break;
                case MapTomlModel m: MapMap(m, entry, document.Manifest.Id, packageAssetCacheRoot, mapAliases, mapFiles); break;
                default:
                    throw new InvalidOperationException(
                        $"[{document.Manifest.Id}] Unmapped content kind {entry.Kind} for {entry.ArchiveRelativePath}.");
            }
        }

        return new Mapped(items, recipes, icons, stats, skills, skillEffects, playerClasses, valueOverrides, texts, aggroTuning, craftingStations, placeables, mapAliases, mapFiles);
    }

    private static ItemDefinition MapItem(ItemTomlModel m, RomodContentEntry entry, string packageId, string packageAssetCacheRoot) => new()
    {
        Id = m.Id,
        NameTextId = m.NameTextId,
        Name = m.Name,
        DescriptionTextId = m.DescriptionTextId,
        Description = m.Description,
        Icon = m.Icon,
        MaxStackSize = m.MaxStackSize,
        Tier = m.Tier,
        Equipment = m.Equipment is null ? null : MapEquipment(m.Equipment, entry, packageId, packageAssetCacheRoot)
    };

    private static EquipmentDefinition MapEquipment(EquipmentTomlModel m, RomodContentEntry entry, string packageId, string packageAssetCacheRoot) => new()
    {
        Slot = ParseEnum<EquipmentSlot>(m.Slot, "slot", entry, packageId),
        ExtraSlot = m.ExtraSlot is null ? null : ParseEnum<EquipmentSlot>(m.ExtraSlot, "extraSlot", entry, packageId),
        Material = ParseEnum<EquipmentMaterial>(m.Material, "material", entry, packageId),
        StatBonuses = m.StatBonuses.Select(MapStatBonus).ToList(),
        Weapon = m.Weapon is null ? null : MapWeapon(m.Weapon, entry, packageId),
        Shield = m.Shield is null ? null : MapShield(m.Shield),
        EntityAuraId = m.EntityAuraId,
        ExtraEntityAuraIds = m.ExtraEntityAuraIds.ToList(),
        DisplayId = m.DisplayId,
        Display = m.Display is null ? null : MapDisplay(m.Display, packageAssetCacheRoot),
        HeldVfx = m.HeldVfx is null ? null : MapHeldVfx(m.HeldVfx)
    };

    private static EquipmentDisplayDefinition MapDisplay(EquipmentDisplayTomlModel m, string packageAssetCacheRoot) => new()
    {
        Id = m.Id,
        SpacTagsToHide = m.SpacTagsToHide.ToList(),
        Fragments = m.Fragments.Select(f => MapDisplayFragment(f, packageAssetCacheRoot)).ToList()
    };

    private static EquipmentDisplayFragmentDefinition MapDisplayFragment(
        EquipmentDisplayFragmentTomlModel m,
        string packageAssetCacheRoot) => new()
    {
        SkinTag = m.SkinTag,
        SkinName = m.SkinName,
        TexturePath = m.Texture is null
            ? null
            : RomodAssetExtractor.ResolveAssetPath(packageAssetCacheRoot, m.Texture),
        SpriteWidth = m.SpriteWidth,
        SpriteHeight = m.SpriteHeight,
        SpacTag = m.SpacTag,
        HideBaseSkin = m.HideBaseSkin,
        Layer = m.Layer,
        DepthOffset = m.DepthOffset,
        Palette = m.Palette
            .Select(p => new EquipmentDisplayPaletteDefinition { PaletteId = p.PaletteId, Row = p.Row })
            .ToList()
    };

    private static EquipmentHeldVfxDefinition MapHeldVfx(EquipmentHeldVfxTomlModel m) => new()
    {
        ParticleEmitterId = m.ParticleEmitterId,
        RotateWithEntityDirection = m.RotateWithEntityDirection,
        ParticleOffsetX = m.ParticleOffsetX,
        ParticleOffsetY = m.ParticleOffsetY,
        ParticleOffsetZ = m.ParticleOffsetZ,
        ParticleLineLength = m.ParticleLineLength,
        ParticleLineWidth = m.ParticleLineWidth,
        ParticleLineHeight = m.ParticleLineHeight,
        ParticleLineAngleDegrees = m.ParticleLineAngleDegrees,
        ParticleSpawnFrequency = m.ParticleSpawnFrequency,
        ParticleAmountSpawned = m.ParticleAmountSpawned,
        LightEnabled = m.LightEnabled,
        LightOffsetX = m.LightOffsetX,
        LightOffsetY = m.LightOffsetY,
        LightOffsetZ = m.LightOffsetZ,
        LightRadius = m.LightRadius,
        LightIntensity = m.LightIntensity,
        LightRed = m.LightRed,
        LightGreen = m.LightGreen,
        LightBlue = m.LightBlue,
        LightDuration = m.LightDuration,
        LightFlickerAmount = m.LightFlickerAmount
    };

    private static StatBonusDefinition MapStatBonus(StatBonusTomlModel m) => new()
    {
        StatId = m.StatId,
        Additive = m.Additive,
        AdditiveMultiplier = m.AdditiveMultiplier,
        BaseMultiplier = m.BaseMultiplier,
        BonusMultiplier = m.BonusMultiplier,
        Multiplier = m.Multiplier
    };

    private static WeaponStatsDefinition MapWeapon(WeaponTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        Class = ParseEnum<WeaponClassPreset>(m.Class, "class", entry, packageId),
        Damage = m.Damage.Select(d => MapDamage(d, entry, packageId)).ToList(),
        SwingTimer = m.SwingTimer,
        BaseAttackRange = m.BaseAttackRange,
        BaseKnockback = m.BaseKnockback,
        EnergyCost = m.EnergyCost,
        SpecialEnergyCost = m.SpecialEnergyCost,
        StunPower = m.StunPower,
        MovementFactor = m.MovementFactor,
        ManaCost = m.ManaCost,
        SpecialManaCost = m.SpecialManaCost,
        ManaStatId = m.ManaStatId,
        SpellTome = m.SpellTome is null ? null : MapSpellTome(m.SpellTome, entry, packageId)
    };

    private static DamageRange MapDamage(DamageRangeTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        Type = ParseEnum<DamageTypeId>(m.Type, "type", entry, packageId),
        Min = m.Min,
        Max = m.Max
    };

    private static SpellTomeDefinition MapSpellTome(SpellTomeTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        SpellId = m.SpellId,
        ChargedSpellId = m.ChargedSpellId,
        ChargeTime = m.ChargeTime,
        Target = ParseEnum<SpellTarget>(m.Target, "target", entry, packageId),
        ChargedTarget = ParseEnum<SpellTarget>(m.ChargedTarget, "chargedTarget", entry, packageId)
    };

    private static ShieldStatsDefinition MapShield(ShieldTomlModel m) => new()
    {
        BlockStrength = m.BlockStrength,
        BlockArcSize = m.BlockArcSize,
        StrongBlockArcSize = m.StrongBlockArcSize,
        EnterCost = m.EnterCost
    };

    private static RecipeDefinition MapRecipe(RecipeTomlModel m) => new()
    {
        ResultItemId = m.ResultItemId,
        ResultAmount = m.ResultAmount,
        Station = m.Station,
        Ingredients = m.Ingredients.Select(i => new RecipeIngredient(i.ItemId, i.Amount)).ToList()
    };

    private static IconDefinition MapIcon(IconTomlModel m, string packageAssetCacheRoot)
    {
        // texture is a forward-slash path relative to the package root;
        // resolve to the extracted asset on disk so the icon DataBase can load it.
        var absoluteTexture = Romestead.RomodFormat.Package.RomodAssetExtractor.ResolveAssetPath(
            packageAssetCacheRoot, m.Texture);

        return new IconDefinition
        {
            Id = m.Id,
            TexturePath = absoluteTexture,
            SpriteWidth = m.SpriteWidth,
            SpriteHeight = m.SpriteHeight,
            Frame = m.Frame,
            ReplaceExisting = m.ReplaceExisting
        };
    }

    private static StatDefinition MapStat(StatTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        Id = m.Id,
        NameTextId = m.NameTextId,
        Name = m.Name,
        DescriptionTextId = m.DescriptionTextId,
        Description = m.Description,
        Icon = m.Icon,
        Type = ParseEnum<ModStatType>(m.Type, "type", entry, packageId),
        Flags = ParseFlags<ModStatFlags>(m.Flags, "flags", entry, packageId),
        StringFormat = m.StringFormat,
        MinValue = m.MinValue,
        MaxValue = m.MaxValue,
        DefaultValue = m.DefaultValue,
        IsPercentage = m.IsPercentage,
        IsNegativeStat = m.IsNegativeStat
    };

    private static TextDefinition MapText(TextTomlModel m) => new()
    {
        Id = m.Id,
        Text = m.Text
    };

    private static AggroTuningDefinition MapAggroTuning(AggroTuningTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        Id = m.Id,
        Type = ParseEnum<AggroTuningType>(m.Type, "type", entry, packageId),
        Value = m.Value,
        ApplyToBosses = m.ApplyToBosses
    };

    private static void MapMap(
        MapTomlModel m,
        RomodContentEntry entry,
        string packageId,
        string packageAssetCacheRoot,
        List<MappedMapAlias> aliases,
        List<MappedMapFile> files)
    {
        foreach (var alias in m.Aliases)
        {
            aliases.Add(new MappedMapAlias(alias.Original, alias.Replacement));
        }

        foreach (var file in m.Files)
        {
            files.Add(new MappedMapFile(
                file.MapId,
                Romestead.RomodFormat.Package.RomodAssetExtractor.ResolveAssetPath(packageAssetCacheRoot, file.Source),
                ParseEnum<MapFileFormat>(file.Format, "format", entry, packageId)));
        }
    }

    private static CraftingStationDefinition MapCraftingStation(CraftingStationTomlModel m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        IconId = m.IconId
    };

    private static ModPlaceableStation MapPlaceable(PlaceableTomlModel m, RomodContentEntry entry, string packageId, string packageAssetCacheRoot) => new()
    {
        Id = m.Id,
        StationId = m.StationId,
        DisplayName = m.DisplayName,
        Description = m.Description,
        IconId = m.IconId,
        TexturePath = Romestead.RomodFormat.Package.RomodAssetExtractor.ResolveAssetPath(packageAssetCacheRoot, m.Texture),
        SpriteWidth = m.SpriteWidth,
        SpriteHeight = m.SpriteHeight,
        SpriteOffsetX = m.SpriteOffsetX,
        SpriteOffsetY = m.SpriteOffsetY,
        CollisionWidth = m.CollisionWidth,
        CollisionHeight = m.CollisionHeight,
        CollisionOffsetX = m.CollisionOffsetX,
        CollisionOffsetY = m.CollisionOffsetY,
        Template = ParseEnum<VanillaBenchTemplate>(m.Template, "template", entry, packageId)
    };

    private static SkillDefinition MapSkill(SkillTomlModel m) => new()
    {
        Id = m.Id,
        NameTextId = m.NameTextId,
        Name = m.Name,
        DescriptionTextId = m.DescriptionTextId,
        Description = m.Description,
        Icon = m.Icon,
        Value = m.Value,
        ExperienceGainFactor = m.ExperienceGainFactor
    };

    private static SkillEffectDefinition MapSkillEffect(SkillEffectTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        SkillId = m.SkillId,
        Type = ParseEnum<SkillEffectType>(m.Type, "type", entry, packageId),
        TargetSkillId = m.TargetSkillId,
        ValuePerLevel = m.ValuePerLevel
    };

    private static PlayerClassDefinition MapPlayerClass(PlayerClassTomlModel m) => new()
    {
        Id = m.Id,
        NameTextId = m.NameTextId,
        Name = m.Name,
        BonusSkill = m.BonusSkill,
        SkillBonuses = m.SkillBonuses.Select(b => new SkillBonusDefinition(b.SkillId, b.Level)).ToList(),
        StartingClothes = m.StartingClothes.ToList(),
        StartingInventory = m.StartingInventory.Select(i => new RecipeIngredient(i.ItemId, i.Amount)).ToList(),
        StartingFavourPoints = m.StartingFavourPoints
    };

    private static ValueOverrideDefinition MapValueOverride(ValueOverrideTomlModel m, RomodContentEntry entry, string packageId) => new()
    {
        EntityHealth = m.EntityHealth.Select(e => new EntityHealthOverrideDefinition
        {
            BaseId = ParseGuid(e.BaseId, "baseId", entry, packageId),
            MaxHealth = e.MaxHealth
        }).ToList(),
        Items = m.Items.Select(i => new ItemValueOverrideDefinition
        {
            Id = i.Id,
            MaxStackSize = i.MaxStackSize,
            Tier = i.Tier,
            Weapon = i.Weapon is null ? null : new WeaponValueOverrideDefinition
            {
                Damage = i.Weapon.Damage.Select(d => MapDamage(d, entry, packageId)).ToList(),
                SwingTimer = i.Weapon.SwingTimer,
                BaseAttackRange = i.Weapon.BaseAttackRange,
                BaseKnockback = i.Weapon.BaseKnockback,
                EnergyCost = i.Weapon.EnergyCost,
                SpecialEnergyCost = i.Weapon.SpecialEnergyCost,
                StunPower = i.Weapon.StunPower,
                MovementFactor = i.Weapon.MovementFactor
            }
        }).ToList()
    };

    /// <summary>
    /// Reject numeric strings and undefined values up front — <see cref="Enum.TryParse{T}"/>
    /// otherwise accepts them silently, turning a typo into a value that looks
    /// fine until something downstream blows up far from the source line.
    /// </summary>
    private static TEnum ParseEnum<TEnum>(string raw, string field, RomodContentEntry entry, string packageId)
        where TEnum : struct, Enum
    {
        var trimmed = raw?.Trim() ?? "";
        if (trimmed.Length == 0 || IsAllDigits(trimmed) ||
            !Enum.TryParse<TEnum>(trimmed, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(typeof(TEnum), parsed))
        {
            var expected = string.Join(", ", Enum.GetNames<TEnum>());
            throw new Romestead.RomodFormat.RomodFormatException(
                $"[{packageId}] {entry.ArchiveRelativePath}: invalid value '{raw}' for '{field}'. " +
                $"Expected one of: {expected}.");
        }

        return parsed;
    }

    private static Guid ParseGuid(string raw, string field, RomodContentEntry entry, string packageId)
    {
        if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        throw new Romestead.RomodFormat.RomodFormatException(
            $"[{packageId}] {entry.ArchiveRelativePath}: invalid GUID '{raw}' for '{field}'.");
    }

    /// <summary>
    /// Validates each comma-separated token against the enum names before
    /// folding them with bitwise OR. <see cref="Enum.TryParse{T}"/> on its own
    /// happily accepts numeric tokens like "5,Foo" or undefined integer flag
    /// combinations — neither is what an author meant to type.
    /// </summary>
    private static TEnum ParseFlags<TEnum>(string raw, string field, RomodContentEntry entry, string packageId)
        where TEnum : struct, Enum
    {
        var trimmed = raw?.Trim() ?? "";
        var expected = string.Join(", ", Enum.GetNames<TEnum>());
        if (trimmed.Length == 0)
        {
            throw new Romestead.RomodFormat.RomodFormatException(
                $"[{packageId}] {entry.ArchiveRelativePath}: empty flag value for '{field}'. " +
                $"Use comma-separated names, e.g. 'Additive, Multiplier'. Known flags: {expected}.");
        }

        var knownNames = new HashSet<string>(Enum.GetNames<TEnum>(), StringComparer.OrdinalIgnoreCase);
        var tokens = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new Romestead.RomodFormat.RomodFormatException(
                $"[{packageId}] {entry.ArchiveRelativePath}: empty flag value for '{field}'. " +
                $"Known flags: {expected}.");
        }

        foreach (var token in tokens)
        {
            if (IsAllDigits(token) || !knownNames.Contains(token))
            {
                throw new Romestead.RomodFormat.RomodFormatException(
                    $"[{packageId}] {entry.ArchiveRelativePath}: invalid flag token '{token}' in '{field}'. " +
                    $"Use comma-separated names, e.g. 'Additive, Multiplier'. Known flags: {expected}.");
            }
        }

        // All tokens are valid names; safe to delegate to Enum.Parse for the OR fold.
        return (TEnum)Enum.Parse(typeof(TEnum), trimmed, ignoreCase: true);
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        var i = (s[0] == '+' || s[0] == '-') ? 1 : 0;
        if (i == s.Length)
        {
            return false;
        }

        for (; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static MultiplayerSyncMode MapSyncMode(Romestead.RomodFormat.Manifest.RomodSyncMode mode) => mode switch
    {
        Romestead.RomodFormat.Manifest.RomodSyncMode.ClientOnly => MultiplayerSyncMode.ClientOnly,
        Romestead.RomodFormat.Manifest.RomodSyncMode.ServerOnly => MultiplayerSyncMode.ServerOnly,
        Romestead.RomodFormat.Manifest.RomodSyncMode.RequiredOnClient => MultiplayerSyncMode.RequiredOnClient,
        Romestead.RomodFormat.Manifest.RomodSyncMode.Incompatible => MultiplayerSyncMode.Incompatible,
        _ => throw new Romestead.RomodFormat.RomodFormatException(
            $"Unknown RomodSyncMode '{mode}'. This indicates a new sync mode " +
            $"was added without updating RomodToDefinitionMapper.MapSyncMode.")
    };
}
