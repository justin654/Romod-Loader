using Candide.Database;
using Candide.GameModels;
using Candide.GameModels.Helpers;
using Candide.Graphics.VFX;
using Candide.Particles;
using CandideCreator.Shared.Models.Particle.SpawnArea;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;
using Shared.Entity;
using Shared.Models.Items;

namespace Romestead.ModLoader.ClientCore;

internal static class ModEquipmentHeldVfxHost
{
    private static readonly Dictionary<string, ActiveHeldVfx> Active = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissingEmitterWarnings = new(StringComparer.Ordinal);
    private static Dictionary<string, EquipmentHeldVfxDefinition>? _definitionsByItemId;
    private static int _generation;
    private static float _time;

    public static void Update(GameTime gameTime)
    {
        var definitions = GetDefinitionsByItemId();
        if (definitions.Count == 0)
        {
            return;
        }

        var entity = GameState.LocalPlayer?.Character?.Entity;
        if (entity is null || entity.Removed)
        {
            PruneAll();
            return;
        }

        _generation++;
        var elapsed = Math.Min(0.1f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _time += elapsed;

        try
        {
            ApplyIfEquipped(entity, PlayerEquipmentHelper.GetMainHandWeapon(), "main", definitions, elapsed);
            ApplyIfEquipped(entity, PlayerEquipmentHelper.GetOffHandWeapon(), "offhand", definitions, elapsed);
            PruneInactive();
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("[held-vfx] Failed to update held equipment VFX.", ex);
        }
    }

    public static void Clear() => PruneAll();

    private static void ApplyIfEquipped(
        EntityWrapper entity,
        ItemInstanceModel? item,
        string hand,
        IReadOnlyDictionary<string, EquipmentHeldVfxDefinition> definitions,
        float elapsed)
    {
        if (item?.Data?.Id is not { Length: > 0 } itemId ||
            !definitions.TryGetValue(itemId, out var definition))
        {
            return;
        }

        var key = $"{entity.Id}:{hand}:{itemId}";
        if (!Active.TryGetValue(key, out var active))
        {
            active = new ActiveHeldVfx(itemId);
            Active[key] = active;
        }

        active.LastSeenGeneration = _generation;
        var directionAngle = definition.RotateWithEntityDirection ? EntityDirectionToAngle(entity.Direction) : 0f;
        UpdateParticles(active, entity, definition, directionAngle, elapsed);
        UpdateLight(key, entity, definition, directionAngle);
    }

    private static void UpdateParticles(
        ActiveHeldVfx active,
        EntityWrapper entity,
        EquipmentHeldVfxDefinition definition,
        float direction,
        float dt)
    {
        if (string.IsNullOrWhiteSpace(definition.ParticleEmitterId))
        {
            return;
        }

        if (active.Emitter is null || !string.Equals(active.EmitterId, definition.ParticleEmitterId, StringComparison.Ordinal))
        {
            active.Emitter = CreateEmitter(active.ItemId, definition);
            active.EmitterId = definition.ParticleEmitterId;
        }

        if (active.Emitter is not { } emitter)
        {
            return;
        }

        emitter.Finished = false;
        emitter.EmitterLifetime = -1f;
        emitter.Position = entity.Position + ResolveOffset(
            definition.ParticleOffsetX,
            definition.ParticleOffsetY,
            definition.ParticleOffsetZ,
            direction,
            definition.RotateWithEntityDirection);
        emitter.SpawnArea = new LineSpawnArea
        {
            Length = Math.Max(0f, definition.ParticleLineLength),
            Width = Math.Max(0f, definition.ParticleLineWidth),
            Height = Math.Max(0f, definition.ParticleLineHeight),
            Angle = direction + MathHelper.ToRadians(definition.ParticleLineAngleDegrees)
        };

        if (definition.ParticleSpawnFrequency is { } spawnFrequency)
        {
            emitter.TimeBetweenSpawns = spawnFrequency;
        }

        if (definition.ParticleAmountSpawned is { } amountSpawned)
        {
            emitter.AmountSpawned = amountSpawned;
        }

        emitter.Update(dt);
    }

    private static ParticleEmitter? CreateEmitter(string itemId, EquipmentHeldVfxDefinition definition)
    {
        var data = ParticleEmitterDataBase.GetParticleEmitterDataOrNull(definition.ParticleEmitterId!);
        if (data is null)
        {
            var warningKey = $"{itemId}:{definition.ParticleEmitterId}";
            if (MissingEmitterWarnings.Add(warningKey))
            {
                CoreState.Logger?.Warn($"[held-vfx] Particle emitter '{definition.ParticleEmitterId}' was not found for '{itemId}'. Light effect will still run.");
            }

            return null;
        }

        var emitter = new ParticleEmitter(data)
        {
            EmitterLifetime = -1f
        };
        return emitter;
    }

    private static void UpdateLight(
        string key,
        EntityWrapper entity,
        EquipmentHeldVfxDefinition definition,
        float direction)
    {
        if (!definition.LightEnabled || definition.LightRadius <= 0f)
        {
            return;
        }

        var flicker = ResolveFlicker(key, definition.LightFlickerAmount);
        var color = new Vector3(definition.LightRed, definition.LightGreen, definition.LightBlue)
            * Math.Max(0f, definition.LightIntensity)
            * flicker;

        var args = new VfxLightSourceArgs
        {
            Color = color,
            Radius = definition.LightRadius * flicker,
            Duration = Math.Max(0.05f, definition.LightDuration),
            IgnoreDuration = false,
            Fade = false,
            EntityShadows = false,
            TerrainShadows = false,
            Attenuation = new Vector3(1f, 1024f, 64f),
            Offset = ResolveOffset(
                definition.LightOffsetX,
                definition.LightOffsetY,
                definition.LightOffsetZ,
                direction,
                definition.RotateWithEntityDirection)
        };

        var light = VfxPlayer.PlayLightSource(args, entity, "romestead.modloader.held-vfx:" + key);
        light.Args = args;
        light.Time = 0f;
        light.Finished = false;
    }

    private static float ResolveFlicker(string key, float amount)
    {
        if (amount <= 0f)
        {
            return 1f;
        }

        var seed = (StableHash(key) % 1000) / 1000f;
        var wave = (MathF.Sin((_time + seed) * 21f) + MathF.Sin((_time + seed * 1.7f) * 9f)) * 0.25f + 0.5f;
        return Math.Max(0f, 1f - amount + (wave * amount * 2f));
    }

    private static Vector3 ResolveOffset(float x, float y, float z, float direction, bool rotate)
    {
        if (!rotate)
        {
            return new Vector3(x, y, z);
        }

        var cos = MathF.Cos(direction);
        var sin = MathF.Sin(direction);
        return new Vector3(
            (x * cos) - (y * sin),
            (x * sin) + (y * cos),
            z);
    }

    private static float EntityDirectionToAngle(float direction) => direction * MathHelper.PiOver2;

    private static void PruneInactive()
    {
        var inactive = Active
            .Where(pair => pair.Value.LastSeenGeneration != _generation)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in inactive)
        {
            Active.Remove(key);
        }
    }

    private static void PruneAll()
    {
        Active.Clear();
    }

    private static IReadOnlyDictionary<string, EquipmentHeldVfxDefinition> GetDefinitionsByItemId()
    {
        if (_definitionsByItemId is not null)
        {
            return _definitionsByItemId;
        }

        _definitionsByItemId = ModRegistries.Items.Pending
            .Where(item => item.Equipment?.HeldVfx is not null)
            .ToDictionary(
                item => item.Id,
                item => item.Equipment!.HeldVfx!,
                StringComparer.Ordinal);

        return _definitionsByItemId;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value)
            {
                hash = (hash * 31) + c;
            }

            return hash & int.MaxValue;
        }
    }

    private sealed class ActiveHeldVfx(string itemId)
    {
        public string ItemId { get; } = itemId;
        public ParticleEmitter? Emitter { get; set; }
        public string? EmitterId { get; set; }
        public int LastSeenGeneration { get; set; }
    }
}
