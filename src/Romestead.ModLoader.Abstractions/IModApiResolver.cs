namespace Romestead.ModLoader;

public interface IModApiResolver
{
    bool TryGet<TApi>(out TApi? api) where TApi : class;
}

