using Romestead.ModLoader;

namespace Romestead.DeaggroMod;

[ModManifest("romestead.deaggro", "Better Deaggro", "0.1.0", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class DeaggroMod : IRomesteadMod, IContentMod
{
    public void Initialize(IModContext context)
    {
        if (context.TryGetApi<IAggroTuningRegistry>(out _))
        {
            context.Logger.Info("Aggro tuning API available through the typed resolver.");
        }

        context.Logger.Info("Better Deaggro mod ready.");
    }

    public void RegisterContent(IContentRegistry registry)
    {
        registry.AggroTuning.Register(new AggroTuningDefinition
        {
            Id = "romestead.deaggro:max-leash",
            Type = AggroTuningType.MaxLossRadiusTiles,
            Value = 20f,
            ApplyToBosses = false
        });

        registry.AggroTuning.Register(new AggroTuningDefinition
        {
            Id = "romestead.deaggro:no-pack-chain",
            Type = AggroTuningType.DisableAllyChainAggro,
            ApplyToBosses = false
        });

        registry.AggroTuning.Register(new AggroTuningDefinition
        {
            Id = "romestead.deaggro:faster-decay",
            Type = AggroTuningType.ThreatDecayMultiplier,
            Value = 2f,
            ApplyToBosses = false
        });
    }
}
