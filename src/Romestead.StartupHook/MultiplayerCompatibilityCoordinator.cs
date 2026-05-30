using System.Reflection;
using CandideServer.EventBus;
using CandideServer.Server;
using HarmonyLib;
using Romestead.ModLoader;

namespace Romestead.StartupHook;

internal static class MultiplayerCompatibilityCoordinator
{
    private const string SnapshotKey = "romestead.modloader:get-compatibility-snapshot";
    private static readonly Type? ConnectServiceType = Type.GetType("Candide.Multiplayer.Services.ConnectService, Romestead");
    private static readonly Type? NetworkManagerType = Type.GetType("Candide.Multiplayer.Network.NetworkManager, Romestead");
    private static readonly ModCompatibilitySnapshotRequest SnapshotRequest = new();
    private static readonly IMultiplayerApi Multiplayer = new MultiplayerApi();
    private static readonly object Sync = new();

    private static Harmony? _harmony;
    private static IModLogger? _logger;
    private static EventBusManager? _subscribedManager;
    private static bool _requestInFlight;
    private static bool _installed;

    public static void Install(HostKind hostKind, IModLogger logger)
    {
        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            _logger = logger;
            _harmony = new Harmony("romestead.startuphook.compatibility");
        }

        var harmony = _harmony;
        if (harmony is null)
        {
            return;
        }

        var diagnosticHostKind = hostKind == HostKind.Client
            ? ModLoaderHostKind.Client
            : ModLoaderHostKind.DedicatedServer;

        var groups = new List<PatchGroupDefinition>
        {
            new(
                "startup.compatibility.base-server",
                diagnosticHostKind,
                PatchBaseServerHooks)
        };

        if (hostKind == HostKind.Client)
        {
            groups.Add(new PatchGroupDefinition(
                "startup.compatibility.client-connect",
                diagnosticHostKind,
                PatchClientHooks));
        }

        PatchGroupInstaller.Install(logger, harmony, groups);
    }

    private static PatchGroupExecutionResult PatchBaseServerHooks(Harmony harmony)
    {
        var runMethod = AccessTools.Method(typeof(BaseServer), nameof(BaseServer.Run));
        var stopMethod = AccessTools.Method(typeof(BaseServer), nameof(BaseServer.Stop));
        if (runMethod is null || stopMethod is null)
        {
            return PatchGroupExecutionResult.FailureResult("BaseServer.Run or BaseServer.Stop could not be resolved.");
        }

        harmony.Patch(
            runMethod,
            postfix: new HarmonyMethod(typeof(MultiplayerCompatibilityCoordinator), nameof(OnBaseServerRunPostfix)));
        harmony.Patch(
            stopMethod,
            postfix: new HarmonyMethod(typeof(MultiplayerCompatibilityCoordinator), nameof(OnBaseServerStopPostfix)));
        return PatchGroupExecutionResult.SuccessResult("Hooks BaseServer run/stop for compatibility snapshots.");
    }

    private static PatchGroupExecutionResult PatchClientHooks(Harmony harmony)
    {
        if (ConnectServiceType is null)
        {
            return PatchGroupExecutionResult.FailureResult("ConnectService is unavailable.");
        }

        var loggedInMethod = AccessTools.Method(ConnectServiceType, "LoggedIn");
        var resetStateMethod = AccessTools.Method(ConnectServiceType, "ResetState");
        if (loggedInMethod is null || resetStateMethod is null)
        {
            return PatchGroupExecutionResult.FailureResult("ConnectService.LoggedIn or ConnectService.ResetState could not be resolved.");
        }

        harmony.Patch(
            loggedInMethod,
            postfix: new HarmonyMethod(typeof(MultiplayerCompatibilityCoordinator), nameof(OnClientLoggedInPostfix)));
        harmony.Patch(
            resetStateMethod,
            postfix: new HarmonyMethod(typeof(MultiplayerCompatibilityCoordinator), nameof(OnClientResetStatePostfix)));
        return PatchGroupExecutionResult.SuccessResult("Hooks client connect/reset state for compatibility snapshot requests.");
    }

    public static void OnBaseServerRunPostfix()
    {
        EnsureHostSubscription();
    }

    public static void OnBaseServerStopPostfix()
    {
        Reset(clearSubscription: true);
    }

    public static void OnClientLoggedInPostfix()
    {
        RequestHostSnapshot();
    }

    public static void OnClientResetStatePostfix()
    {
        Reset(clearSubscription: false);
        ModRegistries.Diagnostics.ClearRemoteCompatibilityResult();
    }

    private static void EnsureHostSubscription()
    {
        var manager = BaseServer.Instance?.NetworkEventBusManager;
        if (manager is null || ReferenceEquals(_subscribedManager, manager))
        {
            return;
        }

        manager.Subscribe<ModCompatibilitySnapshotRequest>(SnapshotKey, OnSnapshotRequested);
        _subscribedManager = manager;
        _logger?.Info("[modloader] Registered host compatibility snapshot responder.");
    }

    private static void Reset(bool clearSubscription)
    {
        if (clearSubscription)
        {
            _subscribedManager = null;
        }

        _requestInFlight = false;
    }

    private static void RequestHostSnapshot()
    {
        if (_requestInFlight || !Multiplayer.IsMultiplayer || !Multiplayer.IsClient)
        {
            return;
        }

        var localReport = ModRegistries.Diagnostics.LocalCompatibilityReport;
        if (localReport is null)
        {
            _logger?.Warn("[modloader] Cannot request host compatibility snapshot because the local report is not ready.");
            return;
        }

        var getMethod = NetworkManagerType?
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Get" && method.IsGenericMethodDefinition && method.GetParameters().Length == 6);
        if (getMethod is null)
        {
            _logger?.Warn("[modloader] Compatibility snapshot request skipped because NetworkManager.Get<T>() was not found.");
            return;
        }

        _requestInFlight = true;
        _logger?.Info("[modloader] Requesting host compatibility snapshot.");
        getMethod
            .MakeGenericMethod(typeof(ModCompatibilitySnapshot))
            .Invoke(
                null,
                [
                    SnapshotKey,
                    SnapshotRequest,
                    (Action<ModCompatibilitySnapshot>)OnHostSnapshotReceived,
                    10f,
                    (Action<byte>)OnSnapshotError,
                    true
                ]);
    }

    private static void OnSnapshotRequested(ModCompatibilitySnapshotRequest _, EventBusArgs args)
    {
        var server = BaseServer.Instance;
        if (server is null)
        {
            _logger?.Warn("[modloader] Received a compatibility snapshot request before BaseServer.Instance was available.");
            return;
        }

        var localReport = ModRegistries.Diagnostics.LocalCompatibilityReport ?? new ModCompatibilityReport([]);
        var snapshot = ModCompatibilitySnapshot.FromReport("host", localReport);
        server.SendGetResponseMessage(args, snapshot, false);
    }

    private static void OnHostSnapshotReceived(ModCompatibilitySnapshot snapshot)
    {
        _requestInFlight = false;
        snapshot ??= new ModCompatibilitySnapshot();
        snapshot.Source ??= "host";
        snapshot.Entries ??= [];

        _logger?.Info(
            $"[modloader] Received compatibility snapshot from {snapshot.Source} with {snapshot.Entries.Count} entr{(snapshot.Entries.Count == 1 ? "y" : "ies")}.");

        LogSnapshot(snapshot);

        var localReport = ModRegistries.Diagnostics.LocalCompatibilityReport;
        if (localReport is null)
        {
            _logger?.Warn("[modloader] Host compatibility snapshot arrived before the local compatibility report was ready.");
            return;
        }

        var comparison = ModCompatibilityComparer.CompareLocalToRemote(localReport, snapshot);
        ModRegistries.Diagnostics.SetRemoteCompatibilityResult(snapshot, comparison);

        if (comparison.Compatible)
        {
            _logger?.Info($"[modloader] Local vs {snapshot.Source} compatibility: compatible.");
            return;
        }

        var lines = comparison.Issues.Select(issue => $"- {issue.Message}");
        _logger?.Warn(
            $"[modloader] Local vs {snapshot.Source} compatibility found {comparison.Issues.Count} issue(s):{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }

    private static void OnSnapshotError(byte errorType)
    {
        _requestInFlight = false;
        _logger?.Warn($"[modloader] Failed to fetch host compatibility snapshot. ErrorType={errorType}.");
    }

    private static void LogSnapshot(ModCompatibilitySnapshot snapshot)
    {
        if (snapshot.Entries.Count == 0)
        {
            _logger?.Info($"[modloader] {snapshot.Source} compatibility snapshot: <none>");
            return;
        }

        var lines = snapshot.Entries
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"- {entry.Id} v{entry.Version} SyncMode={entry.SyncMode} LoadState={entry.LoadState} Present={entry.Present}");
        _logger?.Info(
            $"[modloader] {snapshot.Source} compatibility snapshot:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }
}

public sealed class ModCompatibilitySnapshotRequest
{
    public string Requester = "romestead.modloader";
}
