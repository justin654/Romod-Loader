using Candide.GameModels;
using CandideServer.Models.Boss;
using Shared.Data;
using Shared.Entity;

namespace Romestead.ModLoader.ClientCore;

internal static class AggroBossDetection
{
    private static HashSet<Guid>? _bossEntityBaseIds;

    internal static bool IsBossEntity(EntityWrapper? entity)
    {
        if (entity is null || entity.Removed)
        {
            return false;
        }

        if (IsActiveBossFightEntity(entity))
        {
            return true;
        }

        if (GetBossEntityBaseIds().Contains(entity.BaseGuid))
        {
            return true;
        }

        var controllerTypeName = entity.Controller?.GetType().FullName;
        if (string.IsNullOrEmpty(controllerTypeName))
        {
            return false;
        }

        return controllerTypeName.Contains("BossController", StringComparison.OrdinalIgnoreCase) ||
               controllerTypeName.Contains("BossAi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveBossFightEntity(EntityWrapper entity)
    {
        var activeBossFights = GameState.ActiveBossFights;
        if (activeBossFights is null || activeBossFights.Count == 0)
        {
            return false;
        }

        foreach (BossFightModel fight in activeBossFights.Values)
        {
            if (fight.BossEntityId == entity.Id)
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<Guid> GetBossEntityBaseIds()
    {
        if (_bossEntityBaseIds is not null)
        {
            return _bossEntityBaseIds;
        }

        _bossEntityBaseIds = new HashSet<Guid>();
        foreach (var bossId in KnownBossContentIds)
        {
            var boss = BossDataBase.GetBossOrNull(bossId);
            if (boss is null || boss.BossEntityBaseId == Guid.Empty)
            {
                continue;
            }

            _bossEntityBaseIds.Add(boss.BossEntityBaseId);
        }

        return _bossEntityBaseIds;
    }

    private static readonly string[] KnownBossContentIds =
    [
        BossIds.Cyclops,
        BossIds.CyclopsEye,
        BossIds.DiInferi,
        BossIds.Medusa,
        BossIds.MinervasOwl,
        BossIds.SatyrBoss,
        BossIds.Talos,
        BossIds.VolcanicPhoenix,
        BossIds.Vulcan
    ];
}
