using Romestead.ModLoader;

namespace Romestead.StartupHook;

internal sealed class ModApiRegistry : IModApiResolver
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<TApi>(TApi api) where TApi : class
    {
        ArgumentNullException.ThrowIfNull(api);
        _services[typeof(TApi)] = api;
    }

    public bool TryGet<TApi>(out TApi? api) where TApi : class
    {
        if (_services.TryGetValue(typeof(TApi), out var service))
        {
            api = service as TApi;
            return api is not null;
        }

        api = null;
        return false;
    }

    public IReadOnlyList<string> GetRegisteredApiNames() =>
        _services.Keys
            .Select(type => type.Name)
            .ToArray();
}
