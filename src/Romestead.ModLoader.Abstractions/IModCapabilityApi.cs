namespace Romestead.ModLoader;

public interface IModCapabilityApi
{
    IReadOnlyList<ModCapabilityStatusInfo> Capabilities { get; }

    bool TryGetCapability(string capabilityId, out ModCapabilityStatusInfo capability);

    bool IsAvailable(string capabilityId);
}
