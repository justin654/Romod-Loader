namespace Romestead.ModLoader;

public sealed record ModCapabilityStatusInfo(
    string Id,
    ModCapabilityState State,
    string Summary);
