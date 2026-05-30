using System.Diagnostics;
using System.Reflection;
using Romestead.ModLoader;

namespace Romestead.StartupHook;

public sealed class WorldMapApi : IWorldMapApi
{
    private static readonly Type? GameStateType = Type.GetType("Candide.GameModels.GameState, Romestead");
    private static readonly Type? ExteriorWorldHandlerType = Type.GetType("Candide.World.ExteriorWorldHandler, Romestead");
    private static readonly Type? ServerGameStateType = Type.GetType("CandideServer.ServerGameState, CandideServer");

    private readonly object _sync = new();
    private bool _revealEnabled;
    private bool _forceRevealRequested;
    private bool _pendingReveal;
    private int _lastWidth;
    private int _lastHeight;
    private volatile bool _isReady;

    public bool IsReady =>
        ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap) &&
        _isReady;
    public IModLogger? Logger { get; set; }

    public void RevealAll(bool force = false)
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap))
        {
            Logger?.Warn("[worldmap] RevealAll requested while the world-map capability is unavailable; ignoring.");
            _isReady = false;
            return;
        }

        lock (_sync)
        {
            _revealEnabled = true;
            _pendingReveal = true;
            _forceRevealRequested |= force;
        }

        TryRevealPending();
    }

    public void MarkPending()
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap))
        {
            _isReady = false;
            return;
        }

        lock (_sync)
        {
            // Only arm a reveal if a mod has actually opted in via RevealAll. Without this
            // gate the full-map reveal runs on every game-state sync (each map transition),
            // doing a multi-second reflection pass over millions of tiles for no visible
            // benefit when no full-map mod is active.
            if (!_revealEnabled)
            {
                return;
            }

            _pendingReveal = true;
        }

        _isReady = false;
    }

    public void MarkPendingAfterSaveLoad()
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap))
        {
            _isReady = false;
            return;
        }

        Logger?.Info("World map save load finished; scheduling full reveal on the main thread.");
        MarkPending();
    }

    public void TryRevealPending()
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap))
        {
            _isReady = false;
            return;
        }

        bool pending;
        bool force;

        lock (_sync)
        {
            pending = _pendingReveal;
            force = _forceRevealRequested;
        }

        if (!pending)
        {
            RefreshReadyState();
            return;
        }

        if (!TryRevealAll(refreshVisuals: true, force))
        {
            return;
        }

        lock (_sync)
        {
            _pendingReveal = false;
            _forceRevealRequested = false;
        }
    }

    public void RefreshReadyState()
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.WorldMap))
        {
            _isReady = false;
            return;
        }

        _isReady = TryGetMapState(logFailures: false, out _, out _, out _, out _);
    }

    private bool TryRevealAll(bool refreshVisuals, bool force)
    {
        if (!TryGetMapState(logFailures: true, out var mappedTiles, out var worldTiles, out var width, out var height))
        {
            _isReady = false;
            return false;
        }

        _isReady = true;

        if (!force &&
            width == _lastWidth &&
            height == _lastHeight &&
            IsFullyMapped(mappedTiles, width, height))
        {
            return true;
        }

        var stopwatch = Stopwatch.StartNew();

        CopyWorldTilesFromServerIfAvailable(worldTiles, width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                mappedTiles[x, y] = true;
            }
        }

        if (refreshVisuals)
        {
            RefreshMapVisuals(width, height);
        }

        _lastWidth = width;
        _lastHeight = height;

        stopwatch.Stop();
        Logger?.Info($"Revealed full map ({width}x{height}) in {stopwatch.ElapsedMilliseconds} ms.");
        return true;
    }

    private bool TryGetMapState(
        bool logFailures,
        out bool[,] mappedTiles,
        out Array worldTiles,
        out int width,
        out int height)
    {
        mappedTiles = ReadStaticMember(GameStateType, "MappedTiles") as bool[,]
            ?? new bool[0, 0];
        worldTiles = ReadStaticMember(GameStateType, "WorldTiles") as Array
            ?? Array.CreateInstance(typeof(object), 0, 0);
        width = 0;
        height = 0;

        if (mappedTiles.Length == 0 || worldTiles.Length == 0)
        {
            if (logFailures)
            {
                Logger?.Info("Map arrays are not ready; full map reveal delayed.");
            }

            return false;
        }

        width = mappedTiles.GetLength(0);
        height = mappedTiles.GetLength(1);

        if (width == 0 || height == 0)
        {
            if (logFailures)
            {
                Logger?.Warn($"MappedTiles has invalid dimensions: {width}x{height}.");
            }

            return false;
        }

        if (worldTiles.Rank != 2 || worldTiles.GetLength(0) != width || worldTiles.GetLength(1) != height)
        {
            if (logFailures)
            {
                Logger?.Warn("WorldTiles dimensions do not match MappedTiles; full map reveal delayed.");
            }

            return false;
        }

        return true;
    }

    private static bool IsFullyMapped(bool[,] mappedTiles, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mappedTiles[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void CopyWorldTilesFromServerIfAvailable(Array destination, int width, int height)
    {
        var serverRaw = ReadStaticMember(ServerGameStateType, "WorldTiles");
        if (serverRaw is null)
        {
            return;
        }

        if (serverRaw is not Array source)
        {
            Logger?.Warn("Server world tiles are not available in a compatible layout.");
            return;
        }

        if (source.Rank != 2 || source.GetLength(0) != width || source.GetLength(1) != height)
        {
            Logger?.Warn(
                $"Server world tile size {source.GetLength(0)}x{source.GetLength(1)} does not match client map {width}x{height}.");
            return;
        }

        // Resolve the Ground/Structure member accessors once from the tile element type rather
        // than per tile. Across a full 4096x4096 map that is the difference between two reflection
        // lookups and ~33M of them.
        var tileType = source.GetType().GetElementType();
        var blankChecker = TileBlankChecker.ForType(tileType);

        var copied = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var destinationTile = destination.GetValue(x, y);
                if (destinationTile is not null && !blankChecker.IsBlank(destinationTile))
                {
                    continue;
                }

                var serverTile = source.GetValue(x, y);
                if (serverTile is null || blankChecker.IsBlank(serverTile))
                {
                    continue;
                }

                destination.SetValue(serverTile, x, y);
                copied++;
            }
        }

        if (copied > 0)
        {
            Logger?.Info($"Copied {copied} world tiles from the host world state.");
        }
    }

    private sealed class TileBlankChecker
    {
        private readonly Func<object, object?>? _readGround;
        private readonly Func<object, object?>? _readStructure;

        private TileBlankChecker(Func<object, object?>? readGround, Func<object, object?>? readStructure)
        {
            _readGround = readGround;
            _readStructure = readStructure;
        }

        public static TileBlankChecker ForType(Type? tileType)
        {
            return new TileBlankChecker(BuildAccessor(tileType, "Ground"), BuildAccessor(tileType, "Structure"));
        }

        public bool IsBlank(object tile)
        {
            var ground = _readGround?.Invoke(tile)?.ToString();
            var structure = _readStructure?.Invoke(tile)?.ToString();
            return string.Equals(ground, "None", StringComparison.Ordinal) &&
                string.Equals(structure, "None", StringComparison.Ordinal);
        }

        private static Func<object, object?>? BuildAccessor(Type? tileType, string memberName)
        {
            if (tileType is null)
            {
                return null;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (tileType.GetField(memberName, flags) is { } field)
            {
                return field.GetValue;
            }

            if (tileType.GetProperty(memberName, flags) is { } property)
            {
                return property.GetValue;
            }

            return null;
        }
    }

    private void RefreshMapVisuals(int width, int height)
    {
        var worldMap = ReadStaticMember(ExteriorWorldHandlerType, "WorldMap");
        if (worldMap is not null)
        {
            InvokeInstanceMethod(worldMap, "QueueUpdateWholeMap");
            InvokeInstanceMethod(worldMap, "UpdateWholeRectangle", 0, 0, width, height);
        }

        var fogMap = ReadStaticMember(ExteriorWorldHandlerType, "FogOfWarWorldMap");
        if (fogMap is not null)
        {
            InvokeInstanceMethod(fogMap, "QueueUpdateWholeMap");
            InvokeInstanceMethod(fogMap, "UpdateWholeRectangle", 0, 0, width, height);
        }

        Logger?.Info("Requested world map and fog-of-war visual refresh.");
    }

    private static object? ReadStaticMember(Type? type, string memberName)
    {
        if (type is null)
        {
            return null;
        }

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        return type.GetField(memberName, flags)?.GetValue(null) ??
            type.GetProperty(memberName, flags)?.GetValue(null);
    }

    private static void InvokeInstanceMethod(object instance, string methodName, params object[] args)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        instance.GetType().GetMethod(methodName, flags)?.Invoke(instance, args);
    }
}
