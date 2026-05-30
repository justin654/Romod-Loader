using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Content.Types;
using Romestead.RomodFormat.Package;

namespace Romestead.RomodFormat.Validation;

/// <summary>
/// Validates a single parsed package document. Checks duplicate IDs
/// within the package, missing asset references, and a few semantic
/// rules that are awkward to catch in the parser itself (e.g. icon
/// texture path resolution, weapon stat ranges).
///
/// Cross-package duplicate detection happens in the runtime loader,
/// after all packages and C# mods have been discovered.
/// </summary>
public sealed class RomodPackageValidator
{
    public RomodValidationResult Validate(RomodPackageDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<RomodValidationDiagnostic>();
        var packageId = document.Manifest.Id;
        var assetPaths = new HashSet<string>(
            document.Assets.Select(a => a.ArchiveRelativePath),
            StringComparer.OrdinalIgnoreCase);

        ValidateDuplicateIds(document, packageId, diagnostics);

        foreach (var entry in document.ContentEntries)
        {
            switch (entry.Model)
            {
                case IconTomlModel icon:
                    ValidateIcon(icon, entry, packageId, assetPaths, diagnostics);
                    break;
                case ItemTomlModel item:
                    ValidateItem(item, entry, packageId, assetPaths, diagnostics);
                    break;
                case RecipeTomlModel recipe:
                    ValidateRecipe(recipe, entry, packageId, diagnostics);
                    break;
                case StatTomlModel stat:
                    ValidateStat(stat, entry, packageId, diagnostics);
                    break;
                case SkillTomlModel skill:
                    ValidateSkill(skill, entry, packageId, diagnostics);
                    break;
                case PlaceableTomlModel placeable:
                    ValidatePlaceable(placeable, entry, packageId, assetPaths, diagnostics);
                    break;
                case MapTomlModel map:
                    ValidateMap(map, entry, packageId, assetPaths, diagnostics);
                    break;
            }
        }

        return new RomodValidationResult(diagnostics);
    }

    private static void ValidateDuplicateIds(
        RomodPackageDocument document,
        string packageId,
        List<RomodValidationDiagnostic> diagnostics)
    {
        var idsByKind = new Dictionary<RomodContentKind, Dictionary<string, string>>();
        foreach (var entry in document.ContentEntries)
        {
            if (!TryGetEntryId(entry, out var id))
            {
                continue;
            }

            if (!idsByKind.TryGetValue(entry.Kind, out var seen))
            {
                seen = new Dictionary<string, string>(StringComparer.Ordinal);
                idsByKind[entry.Kind] = seen;
            }

            if (seen.TryGetValue(id, out var existingPath))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Duplicate {entry.Kind} id '{id}' (already defined in {existingPath})."));
            }
            else
            {
                seen[id] = entry.ArchiveRelativePath;
            }
        }
    }

    private static void ValidateIcon(
        IconTomlModel icon,
        RomodContentEntry entry,
        string packageId,
        HashSet<string> assetPaths,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (IsUnsafeArchivePath(icon.Texture))
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Icon '{icon.Id}' texture path '{icon.Texture}' is not allowed " +
                $"(absolute paths and '..' segments are rejected to prevent traversal)."));
            return;
        }

        if (!assetPaths.Contains(icon.Texture))
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Icon '{icon.Id}' references texture '{icon.Texture}' but no such file exists in the package."));
        }
    }

    private static void ValidateMap(
        MapTomlModel map,
        RomodContentEntry entry,
        string packageId,
        HashSet<string> assetPaths,
        List<RomodValidationDiagnostic> diagnostics)
    {
        foreach (var file in map.Files)
        {
            if (IsUnsafeArchivePath(file.Source))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Map file for '{file.MapId}' source path '{file.Source}' is not allowed " +
                    $"(absolute paths and '..' segments are rejected to prevent traversal)."));
                continue;
            }

            if (!assetPaths.Contains(file.Source))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Map file for '{file.MapId}' references source '{file.Source}' but no such file exists in the package."));
            }
        }
    }

    private static void ValidatePlaceable(
        PlaceableTomlModel placeable,
        RomodContentEntry entry,
        string packageId,
        HashSet<string> assetPaths,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (IsUnsafeArchivePath(placeable.Texture))
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Placeable '{placeable.Id}' texture path '{placeable.Texture}' is not allowed " +
                $"(absolute paths and '..' segments are rejected to prevent traversal)."));
            return;
        }

        if (!assetPaths.Contains(placeable.Texture))
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Placeable '{placeable.Id}' references texture '{placeable.Texture}' but no such file exists in the package."));
        }
    }

    private static void ValidateItem(
        ItemTomlModel item,
        RomodContentEntry entry,
        string packageId,
        HashSet<string> assetPaths,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (item.Equipment is null)
        {
            return;
        }

        var equipment = item.Equipment;
        ValidateEquipmentDisplay(item, equipment, entry, packageId, assetPaths, diagnostics);
        ValidateEquipmentHeldVfx(item, equipment, entry, packageId, diagnostics);
        if (equipment.Weapon is { } weapon)
        {
            // Equippable weapons need a DisplayId or they swing an invisible model.
            // This isn't fatal at the data-layer (you may want a debug placeholder),
            // but it's almost always a mistake.
            if (string.IsNullOrWhiteSpace(equipment.DisplayId) && equipment.Display is null)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Warning,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' has [equipment.weapon] but no displayId; " +
                    $"in-world it will render with no model. Set [equipment.displayId] (e.g. \"cdd:iron_sword\") " +
                    $"or [equipment.display]."));
            }

            if (weapon.SwingTimer <= 0f)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' weapon swingTimer must be > 0 (got {weapon.SwingTimer})."));
            }

            if (weapon.BaseAttackRange <= 0f)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' weapon baseAttackRange must be > 0 (got {weapon.BaseAttackRange})."));
            }

            if (weapon.MovementFactor <= 0f)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' weapon movementFactor must be > 0 (got {weapon.MovementFactor})."));
            }

            for (var i = 0; i < weapon.Damage.Count; i++)
            {
                var d = weapon.Damage[i];
                if (string.IsNullOrWhiteSpace(d.Type))
                {
                    diagnostics.Add(new RomodValidationDiagnostic(
                        RomodValidationSeverity.Error,
                        packageId,
                        entry.ArchiveRelativePath,
                        $"Item '{item.Id}' [[equipment.weapon.damage]][{i}] is missing 'type'."));
                }
                if (d.Min < 0f || d.Max < 0f)
                {
                    diagnostics.Add(new RomodValidationDiagnostic(
                        RomodValidationSeverity.Error,
                        packageId,
                        entry.ArchiveRelativePath,
                        $"Item '{item.Id}' [[equipment.weapon.damage]][{i}] has negative damage (min={d.Min}, max={d.Max})."));
                }
                if (d.Min > d.Max)
                {
                    diagnostics.Add(new RomodValidationDiagnostic(
                        RomodValidationSeverity.Error,
                        packageId,
                        entry.ArchiveRelativePath,
                        $"Item '{item.Id}' [[equipment.weapon.damage]][{i}] has min ({d.Min}) > max ({d.Max})."));
                }
            }

            if (weapon.SpellTome is { } spellTome)
            {
                if (string.IsNullOrWhiteSpace(spellTome.SpellId))
                {
                    diagnostics.Add(new RomodValidationDiagnostic(
                        RomodValidationSeverity.Error,
                        packageId,
                        entry.ArchiveRelativePath,
                        $"Item '{item.Id}' [equipment.weapon.spellTome] is missing 'spellId'."));
                }
                if (spellTome.ChargeTime <= 0f)
                {
                    diagnostics.Add(new RomodValidationDiagnostic(
                        RomodValidationSeverity.Error,
                        packageId,
                        entry.ArchiveRelativePath,
                        $"Item '{item.Id}' [equipment.weapon.spellTome] chargeTime must be > 0 (got {spellTome.ChargeTime})."));
                }
            }
        }
        else
        {
            // Armor/trinket/etc. (equippable but no weapon block) also needs a
            // displayId for the on-character mesh; without it the slot renders blank.
            if (string.IsNullOrWhiteSpace(equipment.DisplayId) && equipment.Display is null)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Warning,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' has [equipment] but no displayId; " +
                    $"the on-character mesh will not render. Set [equipment.displayId] or [equipment.display]."));
            }
        }
    }

    private static void ValidateEquipmentDisplay(
        ItemTomlModel item,
        EquipmentTomlModel equipment,
        RomodContentEntry entry,
        string packageId,
        HashSet<string> assetPaths,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (equipment.Display is not { } display)
        {
            return;
        }

        if (display.Fragments.Count == 0)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.display] must define at least one [[equipment.display.fragments]] entry."));
            return;
        }

        for (var i = 0; i < display.Fragments.Count; i++)
        {
            var fragment = display.Fragments[i];
            if (string.IsNullOrWhiteSpace(fragment.SkinName))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' [[equipment.display.fragments]][{i}] is missing skinName."));
            }

            if (fragment.Texture is null)
            {
                continue;
            }

            if (IsUnsafeArchivePath(fragment.Texture))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' display texture path '{fragment.Texture}' is not allowed " +
                    $"(absolute paths and '..' segments are rejected to prevent traversal)."));
                continue;
            }

            if (!assetPaths.Contains(fragment.Texture))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Item '{item.Id}' display fragment '{fragment.SkinName}' references texture '{fragment.Texture}' " +
                    $"but no such file exists in the package."));
            }
        }
    }

    private static void ValidateEquipmentHeldVfx(
        ItemTomlModel item,
        EquipmentTomlModel equipment,
        RomodContentEntry entry,
        string packageId,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (equipment.HeldVfx is not { } heldVfx)
        {
            return;
        }

        if (heldVfx.ParticleEmitterId is not null && string.IsNullOrWhiteSpace(heldVfx.ParticleEmitterId))
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.heldVfx] particleEmitterId cannot be blank."));
        }

        if (heldVfx.ParticleLineLength < 0f || heldVfx.ParticleLineWidth < 0f || heldVfx.ParticleLineHeight < 0f)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.heldVfx] particle line dimensions cannot be negative."));
        }

        if (heldVfx.ParticleSpawnFrequency is <= 0f)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.heldVfx] particleSpawnFrequency must be greater than zero."));
        }

        if (heldVfx.ParticleAmountSpawned is <= 0)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.heldVfx] particleAmountSpawned must be greater than zero."));
        }

        if (heldVfx.LightRadius < 0f || heldVfx.LightDuration <= 0f || heldVfx.LightFlickerAmount < 0f)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Item '{item.Id}' [equipment.heldVfx] lightRadius, lightDuration, and lightFlickerAmount are invalid."));
        }
    }

    private static void ValidateRecipe(
        RecipeTomlModel recipe,
        RomodContentEntry entry,
        string packageId,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (recipe.Ingredients.Count == 0)
        {
            // RecipeTomlParser already throws on empty ingredients, but mirror
            // the rule as a validator finding too so a model assembled by other
            // means (migrators, tests) still surfaces it.
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Recipe for '{recipe.ResultItemId}' has no ingredients."));
        }

        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            if (string.IsNullOrWhiteSpace(ingredient.ItemId))
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Recipe for '{recipe.ResultItemId}' [[ingredients]][{i}] has no itemId."));
            }
            if (ingredient.Amount <= 0)
            {
                diagnostics.Add(new RomodValidationDiagnostic(
                    RomodValidationSeverity.Error,
                    packageId,
                    entry.ArchiveRelativePath,
                    $"Recipe for '{recipe.ResultItemId}' [[ingredients]][{i}] amount must be > 0 (got {ingredient.Amount})."));
            }
        }
    }

    private static void ValidateStat(
        StatTomlModel stat,
        RomodContentEntry entry,
        string packageId,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (stat.MinValue > stat.MaxValue)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Stat '{stat.Id}' minValue ({stat.MinValue}) > maxValue ({stat.MaxValue})."));
        }
        else if (stat.DefaultValue < stat.MinValue || stat.DefaultValue > stat.MaxValue)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Stat '{stat.Id}' defaultValue ({stat.DefaultValue}) must be in " +
                $"[minValue, maxValue] = [{stat.MinValue}, {stat.MaxValue}]."));
        }
    }

    private static void ValidateSkill(
        SkillTomlModel skill,
        RomodContentEntry entry,
        string packageId,
        List<RomodValidationDiagnostic> diagnostics)
    {
        if (skill.ExperienceGainFactor <= 0f)
        {
            diagnostics.Add(new RomodValidationDiagnostic(
                RomodValidationSeverity.Error,
                packageId,
                entry.ArchiveRelativePath,
                $"Skill '{skill.Id}' experienceGainFactor must be > 0 (got {skill.ExperienceGainFactor})."));
        }
    }

    private static bool IsUnsafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            return true;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEntryId(RomodContentEntry entry, out string id)
    {
        switch (entry.Model)
        {
            case ItemTomlModel m: id = m.Id; return true;
            case IconTomlModel m: id = m.Id; return true;
            case StatTomlModel m: id = m.Id; return true;
            case SkillTomlModel m: id = m.Id; return true;
            case PlayerClassTomlModel m: id = m.Id; return true;
            case TextTomlModel m: id = m.Id; return true;
            case AggroTuningTomlModel m: id = m.Id; return true;
            case CraftingStationTomlModel m: id = m.Id; return true;
            case PlaceableTomlModel m: id = m.Id; return true;
            case RecipeTomlModel m: id = m.ResultItemId; return true;
            case SkillEffectTomlModel m:
                // skill effects have no unique id; use a composite key for duplicate detection.
                id = $"{m.SkillId}:{m.Type}:{m.TargetSkillId}";
                return true;
            default:
                id = "";
                return false;
        }
    }
}
