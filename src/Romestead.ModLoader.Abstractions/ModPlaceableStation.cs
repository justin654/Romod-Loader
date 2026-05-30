namespace Romestead.ModLoader;

/// <summary>
/// Which vanilla crafting bench to use as the data template for a custom
/// placeable station. The template supplies the entity-component layout and
/// the interaction controller that opens the crafting window; the mod only
/// overrides the art and the station flags. Pick the template whose
/// interaction behaviour matches what you want.
/// </summary>
public enum VanillaBenchTemplate
{
    /// <summary>The cauldron bench. Opens the standard crafting window, but uses cauldron-specific animated rendering.</summary>
    Cauldron = 0,

    /// <summary>The campfire bench.</summary>
    Campfire = 1,

    /// <summary>The war table bench. Static-rendering default that opens the standard crafting window.</summary>
    WarTable = 2,
}

/// <summary>
/// Declarative description of a placeable custom crafting bench: a world object
/// the player crafts, drops into the world, and interacts with (press E) to open
/// a crafting window scoped to a custom <see cref="StationId"/> — exactly like
/// vanilla benches, including the player inventory and top menu.
///
/// Under the hood this generates a cloned doodad/entity, a construction shell,
/// a placeable item, and a save-backed decoration record. The placed bench is
/// persisted via that decoration record and reconstructed after a cold restart.
/// Mods supply ids plus world-art settings; the brittle wiring stays inside the
/// loader.
/// </summary>
public sealed class ModPlaceableStation
{
    /// <summary>
    /// Stable, globally-unique id for this placeable (e.g.
    /// "romestead.new-items.embercraft-bench"). Also used as the placeable
    /// item id the player crafts and carries.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The crafting station id this bench opens (e.g. "embercraft"). Recipes
    /// whose <see cref="RecipeDefinition.Station"/> matches appear in the window.
    /// Register the station itself through the crafting-station registry.
    /// </summary>
    public required string StationId { get; init; }

    /// <summary>
    /// Display name for the placeable item and the in-world object.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional tooltip description for the generated placeable item. When
    /// omitted, the loader returns a short generated placement description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Icon id shown for the placeable item in the inventory. Use a vanilla
    /// icon id or one registered through the icon registry.
    /// </summary>
    public required string IconId { get; init; }

    /// <summary>
    /// Absolute or mod-relative path to a PNG used as the world object's art.
    /// Loaded at runtime as the doodad texture and sprite-sheet source.
    /// </summary>
    public required string TexturePath { get; init; }

    /// <summary>
    /// Optional single-frame sprite width for the in-world bench art. Defaults
    /// to the loaded texture width when left at <c>0</c>.
    /// </summary>
    public int SpriteWidth { get; init; }

    /// <summary>
    /// Optional single-frame sprite height for the in-world bench art.
    /// Defaults to the loaded texture height when left at <c>0</c>.
    /// </summary>
    public int SpriteHeight { get; init; }

    /// <summary>
    /// Optional X offset applied to the in-world sprite sheet. When omitted,
    /// custom placeable sprites use <c>0</c> instead of inheriting the cloned
    /// vanilla template's visual offset.
    /// </summary>
    public float? SpriteOffsetX { get; init; }

    /// <summary>
    /// Optional Y offset applied to the in-world sprite sheet. When omitted,
    /// custom placeable sprites use <c>-SpriteHeight / 2</c>, making the
    /// authoring pivot the sprite's bottom center.
    /// </summary>
    public float? SpriteOffsetY { get; init; }

    /// <summary>
    /// Optional world collision width for the placed bench. When omitted, the
    /// loader derives a compact footprint from the sprite size instead of
    /// inheriting the cloned vanilla station's collision.
    /// </summary>
    public float? CollisionWidth { get; init; }

    /// <summary>
    /// Optional world collision height for the placed bench. When omitted, the
    /// loader derives a compact footprint from the sprite size instead of
    /// inheriting the cloned vanilla station's collision.
    /// </summary>
    public float? CollisionHeight { get; init; }

    /// <summary>
    /// Optional X offset for the collision footprint. When omitted, the
    /// footprint is centered on the entity position.
    /// </summary>
    public float? CollisionOffsetX { get; init; }

    /// <summary>
    /// Optional Y offset for the collision footprint. When omitted, the
    /// footprint sits immediately above the sprite's bottom-center placement
    /// anchor.
    /// </summary>
    public float? CollisionOffsetY { get; init; }

    /// <summary>
    /// Vanilla bench whose entity-component layout and interaction controller
    /// are cloned. Defaults to <see cref="VanillaBenchTemplate.WarTable"/>
    /// because it uses the standard secondary crafting window without the
    /// cauldron's custom animated mesh render path.
    /// </summary>
    public VanillaBenchTemplate Template { get; init; } = VanillaBenchTemplate.WarTable;

    /// <summary>
    /// The construction id the generated placeable item spawns (e.g.
    /// "romestead.new-items.embercraft-bench:0"). Stable across client and
    /// server because it is derived purely from <see cref="Id"/>.
    /// </summary>
    public string ConstructionId => DeriveConstructionId(Id);

    /// <summary>
    /// Stable decoration id used for the save-backed world record behind the
    /// generated placeable. The place-construction item still targets
    /// <see cref="ConstructionId"/>, but persistence and cold-load rebuild use
    /// this decoration id.
    /// </summary>
    public string DecorationId => DeriveDecorationId(Id);

    /// <summary>
    /// Builds the construction id for a placeable id. The ":0" suffix mirrors
    /// the vanilla bench construction naming (e.g. "cauldron:0").
    /// </summary>
    public static string DeriveConstructionId(string placeableId) => $"{placeableId}:0";

    /// <summary>
    /// Builds the shared decoration id for a placeable id. This is the
    /// persistent world record that survives a full game restart and rebuilds
    /// the bench entity on load.
    /// </summary>
    public static string DeriveDecorationId(string placeableId) => $"{placeableId}:decoration";

    /// <summary>
    /// Derives a stable doodad <c>Guid</c> for a placeable id. Both the client
    /// (<c>DoodadDatabaseManager</c>) and the server (<c>ServerEntityDataManager</c>)
    /// must register the cloned bench entity under the SAME guid so that
    /// server-authoritative construction placement spawns an entity the client
    /// can render and interact with. Computed deterministically (MD5 of the id,
    /// RFC-4122 version/variant bits stamped) so it never collides with a
    /// random vanilla guid yet is identical in every process.
    /// </summary>
    public Guid DeriveDoodadGuid() => DeriveDoodadGuid(Id);

    /// <inheritdoc cref="DeriveDoodadGuid()"/>
    public static Guid DeriveDoodadGuid(string placeableId)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("romestead.placeable.doodad:" + placeableId));
        // Stamp version 4 + RFC-4122 variant so the guid is well-formed.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
