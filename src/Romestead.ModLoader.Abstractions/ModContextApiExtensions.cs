namespace Romestead.ModLoader;

public static class ModContextApiExtensions
{
    public static TApi GetApi<TApi>(this IModContext context) where TApi : class
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Apis.TryGet<TApi>(out var api) && api is not null)
        {
            return api;
        }

        throw new InvalidOperationException($"Mod API '{typeof(TApi).FullName}' is not registered.");
    }

    public static bool TryGetApi<TApi>(this IModContext context, out TApi? api) where TApi : class
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Apis.TryGet(out api);
    }
}

