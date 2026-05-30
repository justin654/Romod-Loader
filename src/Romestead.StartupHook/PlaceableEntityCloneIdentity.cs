using Romestead.ModLoader;
using CandideCreator.Shared.Collision;
using Shared.Entity;
using Shared.Models.DropTables;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Romestead.StartupHook;

internal static class PlaceableEntityCloneIdentity
{
    internal static void Apply(
        EntitySystem system,
        EntityWrapper root,
        ModPlaceableStation placeable,
        Guid newGuid,
        Guid templateGuid,
        IModLogger? log)
    {
        root.BaseGuid = newGuid;
        root.CraftingStationFlags = new[] { placeable.StationId };
        root.UsesSpac = false;
        root.DropTable = CreateCustomPickupDropTable(placeable.Id);
        ApplyCustomCollision(root, placeable);

        foreach (var wrapper in GetCloneWrappers(system, root))
        {
            if (wrapper.Eid == root.Eid || wrapper.BaseGuid == templateGuid)
            {
                wrapper.BaseGuid = newGuid;
            }

            wrapper.UsesSpac = false;
        }

        log?.Info(
            $"[placeable-bootstrap] '{placeable.Id}': clone identity patched base={templateGuid}->{newGuid} drop={placeable.Id} station={placeable.StationId}.");
    }

    private static IEnumerable<EntityWrapper> GetCloneWrappers(EntitySystem system, EntityWrapper root)
    {
        yield return root;

        List<EntityWrapper>? wrappers = null;
        try
        {
            wrappers = system.GetEntityWrappersList();
        }
        catch
        {
            // Cloned data can be pre-world-load; patching the root wrapper is sufficient.
        }

        if (wrappers is null)
        {
            yield break;
        }

        foreach (var wrapper in wrappers)
        {
            if (wrapper is not null && wrapper.Eid != root.Eid)
            {
                yield return wrapper;
            }
        }
    }

    private static DropTableSystem CreateCustomPickupDropTable(string itemId) =>
        CreateDropTable(itemId);

    private static void ApplyCustomCollision(EntityWrapper wrapper, ModPlaceableStation p)
    {
        try
        {
            var spriteWidth = p.SpriteWidth > 0 ? p.SpriteWidth : 32;
            var spriteHeight = p.SpriteHeight > 0 ? p.SpriteHeight : 48;
            var width = p.CollisionWidth ?? Math.Clamp(spriteWidth, 16f, 64f);
            var height = p.CollisionHeight ?? Math.Clamp(spriteHeight * 0.33f, 10f, 24f);
            var offsetX = p.CollisionOffsetX ?? (-width / 2f);
            var offsetY = p.CollisionOffsetY ?? -height;

            wrapper.Shape = new CollisionRectangle(width, height, offsetX, offsetY);
            wrapper.System.CollisionComponent[wrapper.Eid].AdditionalCollisionShape = null;
            wrapper.System.CollisionComponent[wrapper.Eid].AdditionalCollisionShapeOffset = Vector3.Zero;
        }
        catch
        {
            // Older or partial entity systems may not have the optional collision fields populated.
        }
    }

    private static DropTableSystem CreateDropTable(string itemId)
    {
        var table = (DropTableSystem)RuntimeHelpers.GetUninitializedObject(typeof(DropTableSystem));
        SetField(table, "_itemDrops", new[] { new ItemDrop(1f, 1, 1, itemId, string.Empty, null!, string.Empty) });
        SetField(table, "_dropTableSlots", new Dictionary<int, DropTableSlot>());
        return table;
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }
}
