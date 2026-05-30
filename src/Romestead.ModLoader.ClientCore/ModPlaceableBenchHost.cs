using System.Reflection;
using System.Runtime.CompilerServices;
using Candide;
using Candide.Database.Doodad;
using Candide.Graphics;
using CandideCreator.Shared.Collision;
using CandideCreator.Shared.Models;
using Microsoft.Xna.Framework;
using CandideServer.Entities;
using Microsoft.Xna.Framework.Graphics;
using Romestead.ModLoader;
using Shared.Entity;
using Shared.Models.DropTables;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Client-side host for placeable custom crafting benches (see
/// <see cref="ModPlaceableStation"/>).
///
/// Approach A — in-memory clone. At gameplay time, once the live entity tables
/// are populated, this clones the chosen vanilla bench's <see cref="EntitySystem"/>
/// (the ECS form of the doodad — sprite, collision, interaction controller,
/// Furniture component), overrides its <c>CraftingStationFlags</c> to the custom
/// station, and registers the clone under a deterministic
/// <see cref="ModPlaceableStation.DeriveDoodadGuid()"/> guid.
///
/// It registers into BOTH the client manager (<see cref="DoodadDatabaseManager"/>,
/// which renders/interacts) and the in-process server manager
/// (<see cref="ServerEntityDataManager"/>, which is authoritative for construction
/// placement and spawns the entity). In singleplayer both live in this process, so
/// registering both here makes the place-and-craft loop work. (A dedicated server
/// registers its own copy from <c>Romestead.StartupHook</c>.)
///
/// The matching construction (that spawns this guid) and the placeable item (that
/// casts the place-construction spell) are injected as shared data from the startup
/// hook so they exist on both client and server at content-load time.
/// </summary>
internal static class ModPlaceableBenchHost
{
    // Vanilla bench doodad guids + their db record names (captured from the
    // runtime diagnostic dump). The clone template per VanillaBenchTemplate.
    private static readonly Dictionary<VanillaBenchTemplate, (Guid Guid, string Name)> Templates = new()
    {
        [VanillaBenchTemplate.Cauldron] = (Guid.Parse("e293bda6-4b78-4cb8-8287-d91b6a9129dc"), "cauldron"),
        [VanillaBenchTemplate.Campfire] = (Guid.Parse("5a3eea41-dfc3-49d7-ace6-65e511ba4b0f"), "campfire"),
        [VanillaBenchTemplate.WarTable] = (Guid.Parse("afb970c9-276f-45cf-ae70-2bb9e26138e7"), "war_table"),
    };

    private static readonly HashSet<string> _clientIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _serverIds = new(StringComparer.Ordinal);
    private static readonly HashSet<Guid> _liveArtAppliedEntityIds = [];
    private static bool _done;
    private static int _attempts;

    // ~10s at 60fps before we give up waiting for the entity tables to populate.
    private const int MaxAttempts = 600;

    // Loaded custom textures keyed by placeable id (loaded once, reused across retries).
    private static readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-frame entry point (called from the standard-mode update postfix).
    /// Idempotent: registers each placeable into each manager exactly once, then
    /// stops doing work. Named for the historical diagnostic call site.
    /// </summary>
    internal static void DumpDiagnosticsOnce(IModLogger log)
    {
        if (_done)
        {
            return;
        }

        var pending = ModRegistries.Placeables.Pending;
        if (pending.Count == 0)
        {
            _done = true;
            return;
        }

        try
        {
            TryRegisterAll(pending, log);
        }
        catch (Exception ex)
        {
            _done = true;
            log.Error("[placeable] bench registration loop threw; disabling.", ex);
        }
    }

    internal static void RefreshLiveEntityArt(IModLogger log)
    {
        try
        {
            var system = Candide.GameModels.GameState.EntitySystem;
            if (system is null)
            {
                return;
            }

            foreach (var wrapper in system.GetEntityWrappersList())
            {
                if (wrapper is null)
                {
                    continue;
                }

                var placeable = FindPlaceable(wrapper.BaseGuid);
                if (placeable is null)
                {
                    continue;
                }

                if (_liveArtAppliedEntityIds.Add(wrapper.Id))
                {
                    ApplyCustomArtToLiveWrapper(placeable, wrapper, log);
                }

                AnimateCustomArtFrame(placeable, wrapper);
                QueueCustomImmediateSprite(wrapper);
            }
        }
        catch (Exception ex)
        {
            log.Error("[placeable-art] live entity refresh threw (non-fatal).", ex);
        }
    }

    internal static void ApplyCustomArtToSpawnedEntity(Guid baseGuid, EntityWrapper wrapper, IModLogger log)
    {
        try
        {
            var placeable = FindPlaceable(baseGuid);
            if (placeable is null)
            {
                return;
            }

            wrapper.BaseGuid = baseGuid;
            NormalizeCustomRenderState(wrapper);

            if (!_liveArtAppliedEntityIds.Add(wrapper.Id))
            {
                NormalizeCustomRenderState(wrapper);
                return;
            }

            ApplyCustomArtToLiveWrapper(placeable, wrapper, log);
        }
        catch (Exception ex)
        {
            log.Error("[placeable-art] spawned entity art swap threw (non-fatal).", ex);
        }
    }

    private static void TryRegisterAll(IReadOnlyList<ModPlaceableStation> pending, IModLogger log)
    {
        var clientReady = (DoodadDatabaseManager.EntitySystems?.Count ?? 0) > 0;
        var serverReady = (ServerEntityDataManager.EntitySystems?.Count ?? 0) > 0;

        if (!clientReady && !serverReady)
        {
            if (++_attempts > MaxAttempts)
            {
                _done = true;
                log.Warn("[placeable] entity tables never populated; giving up on bench registration.");
            }

            return;
        }

        foreach (var p in pending)
        {
            if (!Templates.TryGetValue(p.Template, out var template))
            {
                log.Warn($"[placeable] '{p.Id}': unknown template {p.Template}; skipping.");
                _clientIds.Add(p.Id);
                _serverIds.Add(p.Id);
                continue;
            }

            var newGuid = p.DeriveDoodadGuid();
            var baseDoodad = BuildBaseDoodad(p, newGuid, template.Name, log);
            if (baseDoodad is null)
            {
                // No template index doodad yet (db still loading); retry next frame.
                continue;
            }

            if (clientReady && !_clientIds.Contains(p.Id))
            {
                if (RegisterInto(
                        "client", p, newGuid, template.Guid, baseDoodad, log,
                        g => DoodadDatabaseManager.TryGetEntityBaseData(g, out var ew) ? ew : null,
                        (bd, sys) => DoodadDatabaseManager.SetBaseDoodadData(bd, sys)))
                {
                    _clientIds.Add(p.Id);
                }
            }

            if (serverReady && !_serverIds.Contains(p.Id))
            {
                if (RegisterInto(
                        "server", p, newGuid, template.Guid, baseDoodad, log,
                        g => ServerEntityDataManager.TryGetEntityBaseData(g, out var ew) ? ew : null,
                        (bd, sys) => ServerEntityDataManager.SetBaseDoodadData(bd, sys)))
                {
                    _serverIds.Add(p.Id);
                }
            }
        }

        if (_clientIds.Count >= pending.Count && _serverIds.Count >= pending.Count)
        {
            _done = true;
            log.Info("[placeable] all benches registered (client + server).");
        }
    }

    /// <summary>
    /// Clones the template entity system into <paramref name="tryGet"/>'s manager,
    /// overrides the crafting station flags, and registers it under the new guid.
    /// Returns true when the registration is complete (success OR a hard error we
    /// should not retry); false to retry next frame (template not loaded yet).
    /// </summary>
    private static bool RegisterInto(
        string side,
        ModPlaceableStation p,
        Guid newGuid,
        Guid templateGuid,
        BaseDoodad baseDoodad,
        IModLogger log,
        Func<Guid, EntityWrapper?> tryGet,
        Action<BaseDoodad, EntitySystem> setData)
    {
        try
        {
            if (tryGet(newGuid) is not null)
            {
                log.Info($"[placeable] {side}: '{p.Id}' already registered (guid={newGuid}).");
                return true;
            }

            var templateWrapper = tryGet(templateGuid);
            if (templateWrapper?.System is null)
            {
                log.Warn($"[placeable] {side}: template {p.Template} ({templateGuid}) not present yet for '{p.Id}'; will retry.");
                return false;
            }

            var system = new EntitySystem();
            EntityCopyHelper.CopySystem(templateWrapper.System, system, Globals.Reflection);

            var wrapper = system.GetWrapper(templateWrapper.Eid);
            if (wrapper is null)
            {
                log.Warn($"[placeable] {side}: cloned template {p.Template} ({templateGuid}) did not produce wrapper eid={templateWrapper.Eid} for '{p.Id}'; will retry.");
                return false;
            }

            ApplyCloneIdentity(system, wrapper, p, newGuid, templateGuid, log);

            // Custom art is client-only: the server registers the same logical
            // doodad/entity, but only the client has a GraphicsDevice and render data.
            if (side == "client")
            {
                ApplyCustomArt(p, wrapper, baseDoodad, log);
            }

            setData(baseDoodad, system);

            var check = tryGet(newGuid);
            var flags = check?.CraftingStationFlags;
            var flagsStr = flags is null ? "null" : "[" + string.Join(", ", flags) + "]";
            log.Info($"[placeable] {side}: registered '{p.Id}' guid={newGuid} eid={check?.Eid} station={p.StationId} flags={flagsStr} verify={check is not null}");
            return check is not null;
        }
        catch (Exception ex)
        {
            log.Error($"[placeable] {side}: registering '{p.Id}' threw; not retrying.", ex);
            return true;
        }
    }

    /// <summary>
    /// Clones the vanilla bench's lightweight index <see cref="BaseDoodad"/> and
    /// stamps the deterministic guid so <c>SetBaseDoodadData</c> keys it correctly.
    /// The cloned entity system carries the live sprite sheet, while the base
    /// doodad keeps the texture/sizing metadata the renderer consults when the
    /// entity is spawned from saved world state.
    /// </summary>
    private static BaseDoodad? BuildBaseDoodad(ModPlaceableStation p, Guid newGuid, string templateName, IModLogger log)
    {
        var databases = DoodadDatabaseManager.Databases;
        if (databases is null)
        {
            return null;
        }

        foreach (var (_, db) in databases)
        {
            if (db is null)
            {
                continue;
            }

            if (db.TryGetDoodad("player/crafting", templateName, out var doodad) && doodad is not null)
            {
                var clone = doodad.Clone(true);
                clone.Guid = newGuid;
                try
                {
                    clone.SavedName = p.Id;
                }
                catch
                {
                    // SavedName is cosmetic; ignore if the setter rejects it.
                }

                return clone;
            }
        }

        log.Warn($"[placeable] could not find template doodad '{templateName}' to clone for '{p.Id}'; will retry.");
        return null;
    }

    private static void ApplyCloneIdentity(
        EntitySystem system,
        EntityWrapper root,
        ModPlaceableStation p,
        Guid newGuid,
        Guid templateGuid,
        IModLogger log)
    {
        root.BaseGuid = newGuid;
        root.CraftingStationFlags = new[] { p.StationId };
        root.UsesSpac = false;
        root.DropTable = CreateCustomPickupDropTable(p.Id);
        ApplyCustomCollision(root, p);

        foreach (var wrapper in GetCloneWrappers(system, root))
        {
            if (wrapper.Eid == root.Eid || wrapper.BaseGuid == templateGuid)
            {
                wrapper.BaseGuid = newGuid;
            }

            wrapper.UsesSpac = false;
        }

        log.Info($"[placeable] '{p.Id}': clone identity patched base={templateGuid}->{newGuid} drop={p.Id} station={p.StationId}.");
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
            // Some cloned systems are not fully initialized yet; the root wrapper is enough.
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

    private static DropTableSystem CreateDropTable(string itemId)
    {
        var table = (DropTableSystem)RuntimeHelpers.GetUninitializedObject(typeof(DropTableSystem));
        SetField(table, "_itemDrops", new[] { new ItemDrop(1f, 1, 1, itemId, string.Empty, null!, string.Empty) });
        SetField(table, "_dropTableSlots", new Dictionary<int, DropTableSlot>());
        return table;
    }

    /// <summary>
     /// Re-points the cloned bench entity at the mod's custom art
    /// (<see cref="ModPlaceableStation.TexturePath"/>).
    /// </summary>
    private static void ApplyCustomArt(ModPlaceableStation p, EntityWrapper wrapper, BaseDoodad baseDoodad, IModLogger log)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(p.TexturePath))
            {
                return;
            }

            var tex = LoadTexture(p.Id, p.TexturePath, log);
            if (tex is null)
            {
                return;
            }

            var spriteWidth = p.SpriteWidth > 0 ? p.SpriteWidth : tex.Width;
            var spriteHeight = p.SpriteHeight > 0 ? p.SpriteHeight : tex.Height;

            SwapDoodadTexture(p, baseDoodad, tex, spriteWidth, spriteHeight, log);
            ApplyCustomArtToWrapper(p, wrapper, tex, spriteWidth, spriteHeight, RenderMode.PreviewTemplate, log);
        }
        catch (Exception ex)
        {
            log.Error($"[placeable-art] '{p.Id}': art swap threw (non-fatal).", ex);
        }
    }

    private static void ApplyCustomArtToLiveWrapper(ModPlaceableStation p, EntityWrapper wrapper, IModLogger log)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(p.TexturePath))
            {
                return;
            }

            var tex = LoadTexture(p.Id, p.TexturePath, log);
            if (tex is null)
            {
                return;
            }

            var spriteWidth = p.SpriteWidth > 0 ? p.SpriteWidth : tex.Width;
            var spriteHeight = p.SpriteHeight > 0 ? p.SpriteHeight : tex.Height;
            ApplyCustomCollision(wrapper, p);
            ApplyCustomArtToWrapper(p, wrapper, tex, spriteWidth, spriteHeight, RenderMode.LiveImmediateOnly, log);
        }
        catch (Exception ex)
        {
            log.Error($"[placeable-art] '{p.Id}': live wrapper art swap threw (non-fatal).", ex);
        }
    }

    private static void ApplyCustomArtToWrapper(
        ModPlaceableStation p,
        EntityWrapper wrapper,
        Texture2D tex,
        int spriteWidth,
        int spriteHeight,
        RenderMode renderMode,
        IModLogger log)
    {
        var sheetProp = typeof(EntityWrapper).GetProperty("SpriteSheet", BindingFlags.Public | BindingFlags.Instance);
        if (sheetProp is null)
        {
            log.Warn($"[placeable-art] '{p.Id}': EntityWrapper has no SpriteSheet property; skipping sprite swap.");
            return;
        }

        var current = sheetProp.GetValue(wrapper);
        if (current is null)
        {
            log.Warn($"[placeable-art] '{p.Id}': wrapper SpriteSheet is null; skipping sprite swap.");
            return;
        }

        var sheetType = current.GetType();
        var fresh = RuntimeHelpers.GetUninitializedObject(sheetType);
        CopyPublicFields(current, fresh);

        SetMember(fresh, "Texture", tex, log);
        SetMember(fresh, "NormalMap", GetMember(current, "NormalMap") ?? tex, log);
        SetMember(fresh, "MetalMap", GetMember(current, "MetalMap") ?? tex, log);
        var columns = Math.Max(1, tex.Width / Math.Max(1, spriteWidth));
        var rows = Math.Max(1, tex.Height / Math.Max(1, spriteHeight));
        SetMember(fresh, "ColumnCount", columns, log);
        SetMember(fresh, "RowCount", rows, log);
        SetMember(fresh, "SpriteWidth", spriteWidth, log);
        SetMember(fresh, "SpriteHeight", spriteHeight, log);

        var resolvedOffset = ResolveSpriteOffset(p, spriteHeight);
        SetMember(fresh, "Offset", resolvedOffset, log);

        if (!sheetProp.CanWrite)
        {
            log.Warn($"[placeable-art] '{p.Id}': SpriteSheet property has no setter; cannot apply swap.");
            return;
        }

        sheetProp.SetValue(wrapper, fresh);
        if (renderMode == RenderMode.LiveImmediateOnly)
        {
            NormalizeCustomRenderState(wrapper);
        }
        else
        {
            NormalizePreviewRenderState(wrapper, spriteWidth, spriteHeight, resolvedOffset, log);
        }

        var frameProp = typeof(EntityWrapper).GetProperty("Frame", BindingFlags.Public | BindingFlags.Instance);
        if (frameProp?.CanWrite == true)
        {
            try { frameProp.SetValue(wrapper, 0); } catch { }
        }

        log.Info($"[placeable-art] '{p.Id}': applied custom art {spriteWidth}x{spriteHeight} to entity {wrapper.Id} ({renderMode}).");
    }

    private enum RenderMode
    {
        PreviewTemplate,
        LiveImmediateOnly
    }

    private static void AnimateCustomArtFrame(ModPlaceableStation p, EntityWrapper wrapper)
    {
        var sheet = wrapper.SpriteSheet;
        if (sheet is null)
        {
            return;
        }

        var frameCount = Math.Max(1, sheet.ColumnCount * sheet.RowCount);
        if (frameCount <= 1)
        {
            return;
        }

        var frame = (int)((Environment.TickCount64 / 180) % frameCount);
        wrapper.Frame = frame;
    }

    private static void QueueCustomImmediateSprite(EntityWrapper wrapper)
    {
        var sheet = wrapper.SpriteSheet;
        if (sheet is null)
        {
            return;
        }

        DeferredRenderer.AddImmediateSprite(new ImmediateSprite
        {
            Position = wrapper.Position,
            Texture = sheet.Texture,
            NormalMap = sheet.NormalMap,
            MetalMap = sheet.MetalMap,
            SpriteSheetOffset = sheet.Offset,
            Frame = sheet.GetFrame(wrapper.Frame),
            MeshOffset = Vector3.Zero,
            Depth = wrapper.DepthOffset,
            Old = true,
            FlashColor = wrapper.FlashColor,
            Highlight = wrapper.Highlight,
            NoShadows = true
        });
    }

    private static void NormalizeCustomRenderState(EntityWrapper wrapper)
    {
        wrapper.Visible = true;
        wrapper.Render3D = false;
        wrapper.UsesSpac = false;
        wrapper.HasFull3DMesh = false;
        wrapper.MeshOffset = Vector3.Zero;
        wrapper.DepthOffset = 0f;
        wrapper.RotateSprite = false;
        wrapper.UsePositionBasedUvs = false;
        wrapper.HasNoShadow = true;
    }

    private static Vector2 ResolveSpriteOffset(ModPlaceableStation p, int spriteHeight) =>
        new(p.SpriteOffsetX ?? 0f, p.SpriteOffsetY ?? (-Math.Max(1, spriteHeight) / 2f));

    private static void NormalizePreviewRenderState(
        EntityWrapper wrapper,
        int spriteWidth,
        int spriteHeight,
        Vector2 spriteOffset,
        IModLogger log)
    {
        wrapper.Visible = true;
        wrapper.Render3D = true;
        wrapper.UsesSpac = false;
        wrapper.HasFull3DMesh = false;
        wrapper.EntityMesh = CreateSpriteQuadMesh(spriteWidth, spriteHeight, log);
        // The live placed sprite uses DeferredRenderer's old/simple sprite path,
        // which anchors the plane at Position.Y + frame.Height / 2 in world-Z.
        // Apply the same anchor and sprite-sheet offset to the preview mesh so
        // ghosting and placement share a bottom-center authoring pivot.
        wrapper.MeshOffset = new Vector3(spriteOffset.X, 0f, (spriteHeight / 2f) + spriteOffset.Y);
        wrapper.DepthOffset = 0f;
        wrapper.RotateSprite = false;
        wrapper.UsePositionBasedUvs = true;
        wrapper.HasNoShadow = true;
    }

    private static IndexedMesh? CreateSpriteQuadMesh(int spriteWidth, int spriteHeight, IModLogger log)
    {
        var graphicsDevice = Globals.GraphicsDevice;
        if (graphicsDevice is null)
        {
            log.Warn("[placeable-art] no GraphicsDevice available; preview will use sprite-only rendering.");
            return null;
        }

        var width = Math.Max(1, spriteWidth);
        var height = Math.Max(1, spriteHeight);
        var halfWidth = width / 2f;

        var mesh = new IndexedMesh
        {
            Verts =
            [
                new Vector3(-halfWidth, 0f, 0f),
                new Vector3(halfWidth, 0f, 0f),
                new Vector3(halfWidth, height, 0f),
                new Vector3(-halfWidth, height, 0f)
            ],
            Uvs =
            [
                new Vector2(0f, height),
                new Vector2(width, height),
                new Vector2(width, 0f),
                new Vector2(0f, 0f)
            ],
            Surfaces = [],
            Faces = []
        };

        var face1 = new IndexedFace(0, 1, 2, mesh);
        var face2 = new IndexedFace(0, 2, 3, mesh);
        mesh.Faces.Add(face1);
        mesh.Faces.Add(face2);
        mesh.Surfaces.Add(new IndexedSurface([face1, face2], new[] { 0, 1, 2, 3 }, mesh));
        mesh.GenerateBuffers(graphicsDevice);
        return mesh;
    }

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

    /// <summary>
    /// Replaces the doodad's render texture with the mod's custom art. The doodad's
    /// <c>Texture</c> is a <c>TextureInformation</c> wrapper (inner <c>Texture</c>
    /// Texture2D + content <c>Path</c> + <c>BypassedContent</c> flag). We set the
    /// inner Texture2D and flip <c>BypassedContent</c> so the content manager keeps
    /// our texture instead of reloading the vanilla one from <c>Path</c>.
    /// </summary>
    private static void SwapDoodadTexture(
        ModPlaceableStation p,
        BaseDoodad baseDoodad,
        Texture2D tex,
        int spriteWidth,
        int spriteHeight,
        IModLogger log)
    {
        try
        {
            object? texInfo = baseDoodad.Texture;
            if (texInfo is null)
            {
                log.Warn($"[placeable-art] '{p.Id}': BaseDoodad.Texture (TextureInformation) is null; cannot swap.");
                return;
            }

            var ti = texInfo.GetType();

            var texField = ti.GetField("Texture", BindingFlags.Public | BindingFlags.Instance);
            if (texField is null || !typeof(Texture2D).IsAssignableFrom(texField.FieldType))
            {
                log.Warn($"[placeable-art] '{p.Id}': TextureInformation has no Texture2D 'Texture' field; cannot swap.");
                return;
            }

            texField.SetValue(texInfo, tex);
            baseDoodad.SpriteWidth = spriteWidth;
            baseDoodad.SpriteHeight = spriteHeight;
            baseDoodad.Frame = 0;

            // Keep our texture instead of reloading the vanilla asset from Path.
            var bypass = ti.GetField("BypassedContent", BindingFlags.Public | BindingFlags.Instance);
            if (bypass is not null)
            {
                try { bypass.SetValue(texInfo, true); }
                catch (Exception ex) { log.Info($"[placeable-art] '{p.Id}': set BypassedContent failed: {ex.Message}"); }
            }

            log.Info($"[placeable-art] '{p.Id}': doodad texture swapped ({tex.Width}x{tex.Height}) with sprite {spriteWidth}x{spriteHeight}.");
        }
        catch (Exception ex)
        {
            log.Error($"[placeable-art] '{p.Id}': doodad texture swap threw.", ex);
        }
    }

    /// <summary>
    /// Loads a PNG from disk into a <see cref="Texture2D"/>, cached per placeable id.
    /// </summary>
    private static Texture2D? LoadTexture(string id, string path, IModLogger log)
    {
        if (_textureCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        Texture2D? tex = null;
        try
        {
            if (!File.Exists(path))
            {
                log.Warn($"[placeable-art] '{id}': texture file not found at '{path}'.");
            }
            else if (Globals.GraphicsDevice is null)
            {
                log.Warn($"[placeable-art] '{id}': no GraphicsDevice available; cannot load texture.");
            }
            else
            {
                using var fs = File.OpenRead(path);
                tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
                log.Info($"[placeable-art] '{id}': loaded texture {tex.Width}x{tex.Height} from '{path}'.");
            }
        }
        catch (Exception ex)
        {
            log.Error($"[placeable-art] '{id}': loading texture from '{path}' threw.", ex);
        }

        _textureCache[id] = tex;
        return tex;
    }

    private static void CopyPublicFields(object source, object target)
    {
        foreach (var f in source.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                f.SetValue(target, f.GetValue(source));
            }
            catch
            {
                // Skip fields that won't round-trip; the swap overrides the ones that matter.
            }
        }
    }

    private static void SetMember(object obj, string name, object? value, IModLogger log)
    {
        var t = obj.GetType();
        try
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f is not null)
            {
                f.SetValue(obj, value);
                return;
            }

            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p?.CanWrite == true)
            {
                p.SetValue(obj, value);
            }
        }
        catch (Exception ex)
        {
            log.Info($"[placeable-art]   could not set {name}: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static object? GetMember(object obj, string name)
    {
        var t = obj.GetType();
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f is not null)
        {
            return f.GetValue(obj);
        }

        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p?.CanRead == true
            ? p.GetValue(obj)
            : null;
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }

    private static ModPlaceableStation? FindPlaceable(Guid baseGuid)
    {
        foreach (var placeable in ModRegistries.Placeables.Pending)
        {
            if (placeable.DeriveDoodadGuid() == baseGuid)
            {
                return placeable;
            }
        }

        return null;
    }

    internal static bool IsCustomPlaceable(Guid baseGuid) => FindPlaceable(baseGuid) is not null;
}
