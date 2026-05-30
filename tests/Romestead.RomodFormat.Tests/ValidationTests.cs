using Romestead.RomodFormat;
using Romestead.RomodFormat.Package;
using Romestead.RomodFormat.Validation;
using Xunit;

namespace Romestead.RomodFormat.Tests;

public class ValidationTests
{
    private static string MinimalManifest(string id = "test.pkg") =>
        $"id = \"{id}\"\nname = \"Pkg\"\nversion = \"1.0.0\"\n";

    [Fact]
    public void Duplicate_item_ids_within_a_package_are_errors()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/a.item.toml",
                "id = \"weapon:dup\"\nname = \"A\"\ndescription = \"a\"\nicon = \"i\"\n"),
            ("content/b.item.toml",
                "id = \"weapon:dup\"\nname = \"B\"\ndescription = \"b\"\nicon = \"i\"\n"));

        var pipeline = new RomodPackagePipeline();
        var result = pipeline.Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors,
            d => d.Message.Contains("Duplicate") && d.Message.Contains("weapon:dup"));
    }

    [Fact]
    public void Duplicate_text_ids_within_a_package_are_errors()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/a.text.toml", "id = \"text:dup\"\ntext = \"A\"\n"),
            ("content/b.text.toml", "id = \"text:dup\"\ntext = \"B\"\n"));

        var pipeline = new RomodPackagePipeline();
        var result = pipeline.Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors,
            d => d.Message.Contains("Duplicate") && d.Message.Contains("text:dup"));
    }

    [Fact]
    public void Duplicate_aggro_tuning_ids_within_a_package_are_errors()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/a.aggro-tuning.toml", "id = \"aggro:dup\"\ntype = \"ThreatDecayMultiplier\"\nvalue = 2\n"),
            ("content/b.aggro-tuning.toml", "id = \"aggro:dup\"\ntype = \"LossRadiusMultiplier\"\nvalue = 0.5\n"));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors,
            d => d.Message.Contains("Duplicate") && d.Message.Contains("aggro:dup"));
    }

    [Fact]
    public void Crafting_station_missing_required_field_is_an_error()
    {
        // iconId omitted -> parser rejects the required field.
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/bench.crafting-station.toml", "id = \"embercraft\"\nname = \"Bench\"\n"));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackagePipeline().Run(archive, NullRomodLog.Instance));
        Assert.Contains("iconId", ex.Message);
    }

    [Fact]
    public void Placeable_with_missing_texture_asset_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/bench.placeable.toml",
                """
                id = "x.bench"
                stationId = "embercraft"
                displayName = "Bench"
                iconId = "icon:x"
                texture = "assets/placeables/missing.png"
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("missing.png"));
    }

    [Fact]
    public void Map_file_with_missing_source_asset_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/tweaks.map.toml",
                """
                [[files]]
                mapId = "maps/interiors_new/insula_1"
                source = "assets/maps/missing.tmx"
                format = "Tmx"
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("missing.tmx"));
    }

    [Fact]
    public void Map_with_no_aliases_or_files_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/empty.map.toml", "# nothing here\n"));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackagePipeline().Run(archive, NullRomodLog.Instance));
        Assert.Contains("at least one", ex.Message);
    }

    [Fact]
    public void Icon_with_missing_texture_asset_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/icon.icon.toml",
                "id = \"icon:x\"\ntexture = \"assets/icons/missing.png\"\n"));

        var pipeline = new RomodPackagePipeline();
        var result = pipeline.Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors,
            d => d.Message.Contains("missing.png"));
    }

    [Fact]
    public void Icon_with_present_texture_asset_is_valid()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/icon.icon.toml",
                "id = \"icon:x\"\ntexture = \"assets/icons/found.png\"\n"),
            ("assets/icons/found.png", "fake-png-bytes"));

        var pipeline = new RomodPackagePipeline();
        var result = pipeline.Run(archive, NullRomodLog.Instance);

        Assert.False(result.Validation.HasErrors);
    }

    [Fact]
    public void Recipe_with_no_ingredients_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/r.recipe.toml",
                "resultItemId = \"weapon:x\"\nstation = \"anvil\"\n"));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackagePipeline().Run(archive, NullRomodLog.Instance));
        Assert.Contains("ingredient", ex.Message);
    }

    [Fact]
    public void Weapon_with_zero_swing_timer_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:zero_swing"
                name = "Bad Sword"
                description = "should fail"
                icon = "i"

                [equipment]
                slot = "Weapon"
                displayId = "cdd:iron_sword"

                [equipment.weapon]
                class = "Sword"
                baseAttackRange = 26
                # swingTimer omitted -> defaults to 0

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 1
                max = 2
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);
        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("swingTimer"));
    }

    [Fact]
    public void Damage_with_min_greater_than_max_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:bad_damage"
                name = "Bad"
                description = "bad"
                icon = "i"

                [equipment]
                slot = "Weapon"
                displayId = "cdd:iron_sword"

                [equipment.weapon]
                class = "Sword"
                swingTimer = 0.5
                baseAttackRange = 26

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 10
                max = 5
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);
        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("min") && d.Message.Contains("max"));
    }

    [Fact]
    public void Weapon_with_custom_display_texture_is_valid_without_display_id()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:custom_display"
                name = "Custom"
                description = "custom"
                icon = "i"

                [equipment]
                slot = "Weapon"

                [equipment.display]
                id = "cdd:test:custom_display"

                [[equipment.display.fragments]]
                skinName = "TestCustomSword"
                texture = "assets/equipment/custom_sword.png"
                spriteWidth = 48
                spriteHeight = 48
                skinTag = 8
                spacTag = 8

                [equipment.weapon]
                class = "Sword"
                swingTimer = 0.5
                baseAttackRange = 26

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 1
                max = 2
                """),
            ("assets/equipment/custom_sword.png", "fake-png-bytes"));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.False(result.Validation.HasErrors);
        Assert.DoesNotContain(result.Validation.Warnings, d => d.Message.Contains("displayId"));
    }

    [Fact]
    public void Custom_display_with_missing_texture_asset_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:missing_display_texture"
                name = "Custom"
                description = "custom"
                icon = "i"

                [equipment]
                slot = "Weapon"

                [equipment.display]

                [[equipment.display.fragments]]
                skinName = "TestCustomSword"
                texture = "assets/equipment/missing.png"

                [equipment.weapon]
                class = "Sword"
                swingTimer = 0.5
                baseAttackRange = 26

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 1
                max = 2
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("missing.png"));
    }

    [Fact]
    public void Weapon_with_held_vfx_is_valid()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:ember_vfx"
                name = "Ember"
                description = "ember"
                icon = "i"

                [equipment]
                slot = "Weapon"
                displayId = "cdd:iron_sword"

                [equipment.heldVfx]
                particleEmitterId = "flame_small"
                particleOffsetZ = 14
                particleLineLength = 26
                particleSpawnFrequency = 0.025
                particleAmountSpawned = 1
                lightRadius = 58

                [equipment.weapon]
                class = "Sword"
                swingTimer = 0.5
                baseAttackRange = 26

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 1
                max = 2
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.False(result.Validation.HasErrors);
    }

    [Fact]
    public void Held_vfx_with_invalid_spawn_frequency_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                """
                id = "weapon:test:bad_vfx"
                name = "Bad"
                description = "bad"
                icon = "i"

                [equipment]
                slot = "Weapon"
                displayId = "cdd:iron_sword"

                [equipment.heldVfx]
                particleEmitterId = "flame_small"
                particleSpawnFrequency = 0

                [equipment.weapon]
                class = "Sword"
                swingTimer = 0.5
                baseAttackRange = 26

                [[equipment.weapon.damage]]
                type = "Slashing"
                min = 1
                max = 2
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);

        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("particleSpawnFrequency"));
    }

    [Fact]
    public void Stat_default_outside_min_max_range_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/x.stat.toml",
                """
                id = "BadStat"
                name = "Bad"
                minValue = 0
                maxValue = 10
                defaultValue = 50
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);
        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors, d => d.Message.Contains("defaultValue"));
    }

    [Fact]
    public void Stat_min_greater_than_max_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/x.stat.toml",
                """
                id = "BadStat"
                name = "Bad"
                minValue = 100
                maxValue = 5
                defaultValue = 50
                """));

        var result = new RomodPackagePipeline().Run(archive, NullRomodLog.Instance);
        Assert.True(result.Validation.HasErrors);
        Assert.Contains(result.Validation.Errors,
            d => d.Message.Contains("minValue") && d.Message.Contains("maxValue"));
    }

    [Fact]
    public void Recipe_ingredient_with_missing_item_id_is_an_error()
    {
        // Parser rejects missing required field with a thrown exception;
        // confirm the message surfaces the field name.
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/r.recipe.toml",
                """
                resultItemId = "weapon:test:thing"
                station = "anvil"

                [[ingredients]]
                amount = 1
                """));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackagePipeline().Run(archive, NullRomodLog.Instance));
        Assert.Contains("itemId", ex.Message);
    }

    [Fact]
    public void Archive_entry_with_path_traversal_is_rejected()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("../escape.toml", "id = \"oops\""));

        Assert.Throws<RomodFormatException>(() =>
            new RomodPackagePipeline().Run(archive, NullRomodLog.Instance));
    }
}
