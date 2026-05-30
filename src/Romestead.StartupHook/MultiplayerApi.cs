using System.Reflection;
using Romestead.ModLoader;

namespace Romestead.StartupHook;

internal sealed class MultiplayerApi : IMultiplayerApi
{
    private static readonly Type? ConnectServiceType = Type.GetType("Candide.Multiplayer.Services.ConnectService, Romestead");
    private static readonly Type? NetworkManagerType = Type.GetType("Candide.Multiplayer.Network.NetworkManager, Romestead");
    private static readonly Type? LocalHostServerManagerType = Type.GetType("Candide.Multiplayer.Network.LocalHostServerManager, Romestead");
    private static readonly Type? BaseServerType = Type.GetType("CandideServer.Server.BaseServer, CandideServer");

    public bool IsMultiplayer
    {
        get
        {
            if (!TryGetConfiguration(out var config))
            {
                return false;
            }

            return !ReadBoolField(config, "Offline");
        }
    }

    public bool IsHost
    {
        get
        {
            if (!TryGetConfiguration(out var config) || ReadBoolField(config, "Offline"))
            {
                return false;
            }

            return ReadBoolField(config, "HostingInClient") ||
                ReadStaticBool(LocalHostServerManagerType, "StartedServer");
        }
    }

    public bool IsClient => IsMultiplayer && ReadStaticBool(NetworkManagerType, "ClientActive");

    public bool IsServerAuthority
    {
        get
        {
            var server = ReadStaticField(BaseServerType, "Instance");
            if (server is null || !ReadBoolField(server, "Running"))
            {
                return false;
            }

            if (!TryGetConfiguration(out var config))
            {
                return true;
            }

            if (ReadBoolField(config, "Offline") || ReadBoolField(config, "HostingInClient"))
            {
                return true;
            }

            return !ReadStaticBool(NetworkManagerType, "ClientActive");
        }
    }

    private static bool TryGetConfiguration(out object? config)
    {
        var state = ReadStaticField(ConnectServiceType, "State");
        config = ReadField(state, "Config");

        if (config is not null)
        {
            return true;
        }

        var server = ReadStaticField(BaseServerType, "Instance");
        config = ReadField(server, "Config");
        return config is not null;
    }

    private static object? ReadStaticField(Type? type, string fieldName)
    {
        if (type is null)
        {
            return null;
        }

        var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null);
    }

    private static object? ReadField(object? instance, string fieldName)
    {
        if (instance is null)
        {
            return null;
        }

        var field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(instance);
    }

    private static bool ReadStaticBool(Type? type, string fieldName)
    {
        var value = ReadStaticField(type, fieldName);
        return value is bool flag && flag;
    }

    private static bool ReadBoolField(object? instance, string fieldName)
    {
        var value = ReadField(instance, fieldName);
        return value is bool flag && flag;
    }
}
