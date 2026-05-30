using Romestead.RomodFormat;
using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Content.Types;
using Romestead.RomodFormat.Package;
using Xunit;

namespace Romestead.RomodFormat.Tests;

public class PackageReaderTests
{
    private static string MinimalManifest(string id = "test.pkg", string version = "1.0.0", string? extra = null) =>
        $"id = \"{id}\"\nname = \"Test Pkg\"\nversion = \"{version}\"\n" + (extra ?? "");

    [Fact]
    public void Reads_a_valid_package()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                "id = \"weapon:test:sword\"\n" +
                "name = \"Sword\"\n" +
                "description = \"A sword.\"\n" +
                "icon = \"icon:test:sword\"\n"),
            ("content/sword.recipe.toml",
                "resultItemId = \"weapon:test:sword\"\nstation = \"anvil\"\n" +
                "[[ingredients]]\nitemId = \"material:iron_bar\"\namount = 5\n"));

        var log = new CollectingRomodLog();
        var doc = new RomodPackageReader().ReadFromFile(archive, log);

        Assert.Equal("test.pkg", doc.Manifest.Id);
        Assert.Equal(2, doc.ContentEntries.Count);
        Assert.Contains(doc.ContentEntries, e => e.Kind == RomodContentKind.Item);
        Assert.Contains(doc.ContentEntries, e => e.Kind == RomodContentKind.Recipe);
    }

    [Fact]
    public void Missing_manifest_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("content/sword.item.toml", "id = \"x\"\nname = \"x\"\ndescription = \"x\"\nicon = \"i\"\n"));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance));
        Assert.Contains("romestead.mod.toml", ex.Message);
    }

    [Fact]
    public void Invalid_toml_in_manifest_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", "id = this is not valid toml\n"));

        Assert.Throws<RomodFormatException>(() =>
            new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance));
    }

    [Fact]
    public void Missing_required_field_is_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", "id = \"x\"\nname = \"x\"\n")); // version missing

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Invalid_syncMode_lists_expected_values()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest(extra: "syncMode = \"BogusMode\"\n")));

        var ex = Assert.Throws<RomodFormatException>(() =>
            new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance));
        Assert.Contains("ClientOnly", ex.Message);
        Assert.Contains("RequiredOnClient", ex.Message);
    }

    [Fact]
    public void Unknown_field_in_item_is_a_warning_not_an_error()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/sword.item.toml",
                "id = \"weapon:test:sword\"\n" +
                "name = \"Sword\"\n" +
                "description = \"A sword.\"\n" +
                "icon = \"icon:test:sword\"\n" +
                "rarity = \"legendary\"\n")); // unknown field

        var log = new CollectingRomodLog();
        var doc = new RomodPackageReader().ReadFromFile(archive, log);

        Assert.Single(doc.ContentEntries);
        Assert.Contains(log.Messages, m => m.Level == "WARN" && m.Message.Contains("rarity"));
    }

    [Fact]
    public void Unknown_content_file_suffix_is_a_warning()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()),
            ("content/foo.aura.toml", "id = \"foo\"\n"));

        var log = new CollectingRomodLog();
        var doc = new RomodPackageReader().ReadFromFile(archive, log);

        Assert.Empty(doc.ContentEntries);
        Assert.Contains(log.Messages, m => m.Level == "WARN" && m.Message.Contains("aura"));
    }

    [Fact]
    public void Defaults_schemaVersion_to_1_when_missing()
    {
        var archive = PackageZipBuilder.CreateArchive(
            ("romestead.mod.toml", MinimalManifest()));

        var doc = new RomodPackageReader().ReadFromFile(archive, NullRomodLog.Instance);
        Assert.Equal(1, doc.Manifest.SchemaVersion);
        Assert.Equal(Romestead.RomodFormat.Manifest.RomodSyncMode.RequiredOnClient, doc.Manifest.SyncMode);
    }
}
