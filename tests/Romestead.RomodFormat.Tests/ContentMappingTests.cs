using Romestead.RomodFormat;
using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Content.Types;
using Romestead.RomodFormat.Package;
using Xunit;

namespace Romestead.RomodFormat.Tests;

public class ContentMappingTests
{
    private static string MinimalManifest() => "id = \"x\"\nname = \"x\"\nversion = \"1.0.0\"\n";

    [Fact]
    public void Item_with_weapon_spelltome_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/wand.item.toml",
                """
                id = "weapon:x:wand"
                name = "Wand"
                description = "casts"
                icon = "i"
                tier = 3

                [equipment]
                slot = "Offhand"
                material = "Iron"

                [equipment.weapon]
                class = "SpellTome"
                energyCost = 20
                manaCost = 10

                [[equipment.weapon.damage]]
                type = "Pyro"
                min = 1
                max = 2

                [equipment.weapon.spellTome]
                spellId = "item:scroll:bolt:0"
                chargedSpellId = "item:scroll:shield:0"
                chargeTime = 1.5
                target = "Self"
                chargedTarget = "TargetGround"
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var entry = Assert.Single(doc.ContentEntries);
        var item = Assert.IsType<ItemTomlModel>(entry.Model);

        Assert.Equal("weapon:x:wand", item.Id);
        Assert.NotNull(item.Equipment);
        Assert.Equal("Offhand", item.Equipment!.Slot);
        Assert.NotNull(item.Equipment.Weapon);
        Assert.Equal("SpellTome", item.Equipment.Weapon!.Class);
        Assert.Equal(20f, item.Equipment.Weapon.EnergyCost);
        Assert.Equal(10f, item.Equipment.Weapon.ManaCost);
        Assert.NotNull(item.Equipment.Weapon.SpellTome);
        Assert.Equal("item:scroll:bolt:0", item.Equipment.Weapon.SpellTome!.SpellId);
        Assert.Equal("TargetGround", item.Equipment.Weapon.SpellTome.ChargedTarget);
        var damage = Assert.Single(item.Equipment.Weapon.Damage);
        Assert.Equal("Pyro", damage.Type);
    }

    [Fact]
    public void Recipe_with_multiple_ingredients_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/r.recipe.toml",
                """
                resultItemId = "weapon:x:wand"
                resultAmount = 2
                station = "anvil"

                [[ingredients]]
                itemId = "material:iron"
                amount = 3

                [[ingredients]]
                itemId = "material:coal"
                amount = 1
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var entry = Assert.Single(doc.ContentEntries);
        var recipe = Assert.IsType<RecipeTomlModel>(entry.Model);
        Assert.Equal(2, recipe.ResultAmount);
        Assert.Equal(2, recipe.Ingredients.Count);
        Assert.Equal("anvil", recipe.Station);
    }

    [Fact]
    public void Percentage_stat_without_explicit_format_defaults_to_P0()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/x.stat.toml",
                """
                id = "CritChance"
                name = "Crit Chance"
                isPercentage = true
                minValue = 0
                maxValue = 1
                defaultValue = 0
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var stat = Assert.IsType<StatTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.True(stat.IsPercentage);
        Assert.Equal("P0", stat.StringFormat);
    }

    [Fact]
    public void Explicit_string_format_is_preserved_for_percentage_stat()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/x.stat.toml",
                """
                id = "CritChance"
                name = "Crit Chance"
                isPercentage = true
                stringFormat = "P2"
                minValue = 0
                maxValue = 1
                defaultValue = 0
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var stat = Assert.IsType<StatTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.Equal("P2", stat.StringFormat);
    }

    [Fact]
    public void Text_entry_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/lore.text.toml",
                """
                id = "item.x.lore"
                text = "Forged in the calderas of the old world."
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var text = Assert.IsType<TextTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.Equal("item.x.lore", text.Id);
        Assert.Equal("Forged in the calderas of the old world.", text.Text);
    }

    [Fact]
    public void Aggro_tuning_entry_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/leash.aggro-tuning.toml",
                """
                id = "x:max-leash"
                type = "MaxLossRadiusTiles"
                value = 20
                applyToBosses = false
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var tuning = Assert.IsType<AggroTuningTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.Equal("x:max-leash", tuning.Id);
        Assert.Equal("MaxLossRadiusTiles", tuning.Type);
        Assert.Equal(20f, tuning.Value);
        Assert.False(tuning.ApplyToBosses);
    }

    [Fact]
    public void Crafting_station_entry_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/bench.crafting-station.toml",
                """
                id = "embercraft"
                name = "Embercraft Bench"
                iconId = "icon:emberpack:ember_sword"
                """));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var station = Assert.IsType<CraftingStationTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.Equal("embercraft", station.Id);
        Assert.Equal("Embercraft Bench", station.Name);
        Assert.Equal("icon:emberpack:ember_sword", station.IconId);
    }

    [Fact]
    public void Placeable_entry_round_trips()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/bench.placeable.toml",
                """
                id = "x.bench"
                stationId = "embercraft"
                displayName = "Embercraft Bench"
                description = "A compact bench."
                iconId = "icon:x"
                texture = "assets/placeables/bench.png"
                spriteWidth = 32
                spriteHeight = 48
                collisionWidth = 28
                collisionHeight = 16
                template = "WarTable"
                """),
            ("assets/placeables/bench.png", "fake-png-bytes"));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var placeable = Assert.IsType<PlaceableTomlModel>(Assert.Single(doc.ContentEntries).Model);
        Assert.Equal("x.bench", placeable.Id);
        Assert.Equal("embercraft", placeable.StationId);
        Assert.Equal("assets/placeables/bench.png", placeable.Texture);
        Assert.Equal(32, placeable.SpriteWidth);
        Assert.Equal(48, placeable.SpriteHeight);
        Assert.Equal(28f, placeable.CollisionWidth);
        Assert.Equal("WarTable", placeable.Template);
    }

    [Fact]
    public void Map_aliases_and_files_round_trip()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/tweaks.map.toml",
                """
                [[aliases]]
                original = "maps/interiors_new/insula_1"
                replacement = "maps/dungeons/plains/plains_crypt_ruin"

                [[files]]
                mapId = "maps/interiors_new/insula_2"
                source = "assets/maps/insula_2.tmx"
                format = "Tmx"
                """),
            ("assets/maps/insula_2.tmx", "<map/>"));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var map = Assert.IsType<MapTomlModel>(Assert.Single(doc.ContentEntries).Model);
        var alias = Assert.Single(map.Aliases);
        Assert.Equal("maps/interiors_new/insula_1", alias.Original);
        Assert.Equal("maps/dungeons/plains/plains_crypt_ruin", alias.Replacement);
        var file = Assert.Single(map.Files);
        Assert.Equal("maps/interiors_new/insula_2", file.MapId);
        Assert.Equal("assets/maps/insula_2.tmx", file.Source);
        Assert.Equal("Tmx", file.Format);
    }

    [Fact]
    public void Asset_extractor_resolves_to_cache_path()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", "id = \"asset.test\"\nname = \"x\"\nversion = \"1.0.0\"\n"),
            ("assets/icons/sword.png", "fake-bytes"));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        var cacheRoot = Path.Combine(Path.GetTempPath(), "romod-tests-cache", Guid.NewGuid().ToString("N"));
        try
        {
            var extractedRoot = RomodAssetExtractor.Extract(doc, cacheRoot, NullRomodLog.Instance);
            var assetPath = RomodAssetExtractor.ResolveAssetPath(extractedRoot, "assets/icons/sword.png");
            Assert.True(File.Exists(assetPath), $"Expected extracted asset at {assetPath}");
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { }
        }
    }
}
