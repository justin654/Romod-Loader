using System.Reflection;
using Candide.Entities.PlayerState;
using Candide.Entities.PlayerState.PlayerStates;
using Candide.GameModels;
using Candide.GameModels.Helpers;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;
using Romestead.StartupHook;
using Shared.Entity;
using Shared.Models.Items;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Client-side current-mana bookkeeping for modded magical weapons.
/// The game doesn't track a built-in "current Mana" pool — only the
/// max-mana stat we registered. This tracker keeps a per-entity current
/// value, initialises it to the entity's max stat on first read, and
/// clamps to [0, max] on writes. Refilled by <see cref="Refill"/> on
/// load. Client-only, matching how vanilla handles current Energy.
/// </summary>
internal static class ManaTracker
{
    private static readonly Dictionary<Guid, Dictionary<string, float>> _current = new();
    private static IModLogger? _logger;

    public static void Install(IModLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Manually wire patches that target internal types Harmony can't see via
    /// <c>[HarmonyPatch(typeof(...))]</c> at compile time.
    /// </summary>
    public static PatchGroupExecutionResult InstallInternalStatePatches(Harmony harmony)
    {
        var patched = 0;
        var attempted = 4;

        if (ApplyPatch(harmony, NormalStateCheckMainAttackPatch.TargetMethod(), nameof(NormalStateCheckMainAttackPatch), typeof(NormalStateCheckMainAttackPatch)))
        {
            patched++;
        }

        if (ApplyPatch(harmony, NormalStateCheckAltAttackPatch.TargetMethod(), nameof(NormalStateCheckAltAttackPatch), typeof(NormalStateCheckAltAttackPatch)))
        {
            patched++;
        }

        if (ApplyPatch(harmony, DashStateCheckMainAttackPatch.TargetMethod(), nameof(DashStateCheckMainAttackPatch), typeof(DashStateCheckMainAttackPatch)))
        {
            patched++;
        }

        if (ApplyPatch(harmony, DashStateCheckAltAttackPatch.TargetMethod(), nameof(DashStateCheckAltAttackPatch), typeof(DashStateCheckAltAttackPatch)))
        {
            patched++;
        }

        return patched == attempted
            ? PatchGroupExecutionResult.SuccessResult("Installed all mana-gate internal state patches.")
            : PatchGroupExecutionResult.FailureResult($"Installed {patched}/{attempted} mana-gate internal state patches.");
    }

    private static bool ApplyPatch(Harmony harmony, MethodBase? target, string label, Type patchType)
    {
        if (target is null)
        {
            _logger?.Warn($"[mana] Could not resolve target for {label}; mana gate inactive for that path.");
            return false;
        }
        var prefix = patchType.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        if (prefix is null)
        {
            _logger?.Warn($"[mana] Could not find Prefix method on {label}.");
            return false;
        }
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        return true;
    }

    /// <summary>
    /// Fallback base max mana applied when no item / aura grants the Mana
    /// stat. Mirrors how vanilla Energy starts at a base value before
    /// equipment bonuses stack on top — without this, modded magic
    /// characters spawn with 0 Mana because <see cref="StatDescription.DefaultValue"/>
    /// is not auto-propagated to <see cref="EntityWrapper.Stats"/> at spawn.
    /// </summary>
    private const float DefaultBaseMana = 100f;

    public static float GetMax(EntityWrapper entity, string statId)
    {
        var stat = entity.Stats.TryGet(statId, out var v) ? v : 0f;
        // For the canonical "Mana" stat, ensure a sensible base so magic
        // weapons are usable on a fresh character without item bonuses.
        if (statId == "Mana" && stat <= 0f)
        {
            return DefaultBaseMana;
        }
        return stat;
    }

    public static float GetCurrent(EntityWrapper entity, string statId)
    {
        var perEntity = GetOrCreatePerEntity(entity);
        if (!perEntity.TryGetValue(statId, out var value))
        {
            value = GetMax(entity, statId);
            perEntity[statId] = value;
        }
        return value;
    }

    public static bool TryConsume(EntityWrapper entity, string statId, float cost)
    {
        if (cost <= 0f) return true;
        var current = GetCurrent(entity, statId);
        if (current < cost) return false;
        SetCurrent(entity, statId, current - cost);
        return true;
    }

    public static void SetCurrent(EntityWrapper entity, string statId, float value)
    {
        var perEntity = GetOrCreatePerEntity(entity);
        var max = GetMax(entity, statId);
        perEntity[statId] = Math.Clamp(value, 0f, max);
    }

    private static Dictionary<string, float> GetOrCreatePerEntity(EntityWrapper entity)
    {
        var id = entity.Id;
        if (!_current.TryGetValue(id, out var perEntity))
        {
            perEntity = new Dictionary<string, float>(StringComparer.Ordinal);
            _current[id] = perEntity;
        }
        return perEntity;
    }

    private static EntityWrapper? GetLocalPlayerEntity() =>
        GameState.LocalPlayer?.Character?.Entity;

    /// <summary>
    /// Vanilla gates mainhand SpellTome use behind a player flag
    /// (<c>tome_equip_in_main_hand</c>) and the same pattern for crossbows
    /// (<c>crossbow_equip_in_main_hand</c>). Without the flag,
    /// <c>NormalState.CheckMainAttack</c> never invokes the SpellTome
    /// transition and the mainhand click does nothing. We unlock those
    /// flags client-side when the current mainhand item is a *modded*
    /// item — vanilla scrolls / crossbows still follow the vanilla
    /// progression to get the flag.
    /// </summary>
    [HarmonyPatch(typeof(LocalPlayerFlags), nameof(LocalPlayerFlags.HasFlag))]
    internal static class LocalPlayerFlagsHasFlagPatch
    {
        private static void Postfix(string id, ref bool __result)
        {
            if (__result) return; // already granted by vanilla, nothing to do.
            if (id != "tome_equip_in_main_hand" && id != "crossbow_equip_in_main_hand") return;

            var mainHand = PlayerEquipmentHelper.GetMainHandWeapon();
            if (mainHand?.Data?.Id is not { Length: > 0 } itemId) return;
            if (SharedContentBootstrap.GetManaCost(itemId) is null) return;

            // Modded magical mainhand weapon — unlock the gate.
            __result = true;
        }
    }

    [HarmonyPatch(typeof(WeaponAttackState), nameof(WeaponAttackState.Set))]
    internal static class WeaponAttackStateSetPatch
    {
        // Deducts mana once an attack actually proceeds past CheckMainAttack /
        // CheckAltAttack (which gate insufficient-mana clicks). Defensive
        // block: if somehow Set is reached with insufficient mana (e.g. the
        // upstream gate didn't fire), we also flip IsValid = false so the
        // downstream state machine treats the attack as invalid.
        private static bool Prefix(WeaponAttackState __instance, ItemInstanceModel __0, AttackButton __1)
        {
            if (__0?.Data?.Id is not { Length: > 0 } itemId) return true;
            var cost = SharedContentBootstrap.GetManaCost(itemId);
            if (cost is null) return true;

            var entity = GetLocalPlayerEntity();
            if (entity is null) return true;

            var required = cost.TapCost;
            if (required <= 0f) return true;

            if (!TryConsume(entity, cost.StatId, required))
            {
                if (__instance is not null) __instance.IsValid = false;
                _logger?.Info($"[mana] Blocked cast of {itemId}: need {required:F1} {cost.StatId}, have {GetCurrent(entity, cost.StatId):F1}.");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Vanilla <c>NormalState.CheckMainAttack</c> calls <c>WeaponAttackState.Set</c>
    /// and then unconditionally invokes the per-weapon-class transition (e.g.
    /// <c>_transitionSpellTomeHolding.Invoke()</c>) regardless of whether Set
    /// "succeeded" — Set returns void, so prefixing it to skip cost setup
    /// doesn't actually stop the attack. To gate the whole flow we prefix
    /// <c>CheckMainAttack</c> itself and return false when the equipped
    /// mainhand weapon is a modded magic weapon and the player can't afford
    /// the cast. CheckAltAttack mirrors the same logic for the offhand.
    /// </summary>
    // NormalState and DashState are internal — target them by AccessTools so
    // Harmony resolves at runtime, no compile-time visibility needed. Both
    // states have CheckMainAttack/CheckAltAttack and either can drive the
    // cast flow depending on whether the player is standing or moving.
    internal static class NormalStateCheckMainAttackPatch
    {
        internal static MethodBase? TargetMethod() =>
            AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.NormalState:CheckMainAttack");

        internal static bool Prefix(ref bool __result)
        {
            if (CanCastWithHand(PlayerEquipmentHelper.GetMainHandWeapon())) return true;
            __result = false;
            return false;
        }
    }

    internal static class NormalStateCheckAltAttackPatch
    {
        internal static MethodBase? TargetMethod() =>
            AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.NormalState:CheckAltAttack");

        internal static bool Prefix(ref bool __result)
        {
            if (CanCastWithHand(PlayerEquipmentHelper.GetOffHandWeapon())) return true;
            __result = false;
            return false;
        }
    }

    internal static class DashStateCheckMainAttackPatch
    {
        internal static MethodBase? TargetMethod() =>
            AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.DashState:CheckMainAttack");

        internal static bool Prefix(ref bool __result)
        {
            if (CanCastWithHand(PlayerEquipmentHelper.GetMainHandWeapon())) return true;
            __result = false;
            return false;
        }
    }

    internal static class DashStateCheckAltAttackPatch
    {
        internal static MethodBase? TargetMethod() =>
            AccessTools.Method("Candide.Entities.PlayerState.PlayerStates.DashState:CheckAltAttack");

        internal static bool Prefix(ref bool __result)
        {
            if (CanCastWithHand(PlayerEquipmentHelper.GetOffHandWeapon())) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Returns true if there's no mana gate to apply (no weapon, no mod cost,
    /// no entity, zero cost) OR the local player has enough mana to spend.
    /// Read-only: deduction happens later in <see cref="WeaponAttackStateSetPatch"/>.
    /// </summary>
    private static bool CanCastWithHand(ItemInstanceModel? weapon)
    {
        if (weapon?.Data?.Id is not { Length: > 0 } itemId) return true;
        var cost = SharedContentBootstrap.GetManaCost(itemId);
        if (cost is null) return true;
        var entity = GetLocalPlayerEntity();
        if (entity is null) return true;
        var required = cost.TapCost;
        if (required <= 0f) return true;
        return GetCurrent(entity, cost.StatId) >= required;
    }

    /// <summary>
    /// Plugs the charged-cast leak: <c>SpellTomeHoldingState.Update</c> deducts
    /// Energy directly and applies the spell aura, bypassing
    /// <c>WeaponAttackState.Set</c> entirely. We gate entry on
    /// <c>SpecialManaCost</c> via <c>CheckCanEnter</c> (so the player can't
    /// even start charging without mana) and deduct the cost in
    /// <c>OnLeave</c> when the state's private <c>_isFullAttack</c> flag is
    /// true (set immediately after the cast resolves in <c>Update</c> — false
    /// means the player released early and no spell fired).
    /// </summary>
    [HarmonyPatch(typeof(SpellTomeHoldingState), "CheckCanEnter")]
    internal static class SpellTomeHoldingStateCheckCanEnterPatch
    {
        private static bool Prefix(SpellTomeHoldingState __instance, ref bool __result)
        {
            if (!CanAffordChargedCast(__instance)) { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(SpellTomeHoldingState), "OnLeave")]
    internal static class SpellTomeHoldingStateOnLeavePatch
    {
        private static void Postfix(SpellTomeHoldingState __instance)
        {
            // _isFullAttack is private; flip if the cast resolved this hold.
            var isFullAttack = Traverse.Create(__instance).Field<bool>("_isFullAttack").Value;
            if (!isFullAttack) return;

            var weapon = GetActiveWeapon(__instance);
            if (weapon?.Data?.Id is not { Length: > 0 } itemId) return;
            var cost = SharedContentBootstrap.GetManaCost(itemId);
            if (cost is null || cost.SpecialCost <= 0f) return;

            var entity = GetLocalPlayerEntity();
            if (entity is null) return;

            TryConsume(entity, cost.StatId, cost.SpecialCost);
            _logger?.Info($"[mana] Charged cast of {itemId} consumed {cost.SpecialCost:F1} {cost.StatId}.");
        }
    }

    private static bool CanAffordChargedCast(SpellTomeHoldingState state)
    {
        var weapon = GetActiveWeapon(state);
        if (weapon?.Data?.Id is not { Length: > 0 } itemId) return true;
        var cost = SharedContentBootstrap.GetManaCost(itemId);
        if (cost is null || cost.SpecialCost <= 0f) return true;
        var entity = GetLocalPlayerEntity();
        if (entity is null) return true;
        return GetCurrent(entity, cost.StatId) >= cost.SpecialCost;
    }

    /// <summary>
    /// Read the in-use weapon from the state's PlayerController via Traverse —
    /// PlayerState.Controller and the WeaponAttackState chain are reachable
    /// at runtime even though their visibility makes compile-time access fragile.
    /// </summary>
    private static ItemInstanceModel? GetActiveWeapon(PlayerState state)
    {
        var controller = Traverse.Create(state).Field<PlayerController>("Controller").Value;
        return controller?.WeaponAttackState?.Item;
    }

    /// <summary>
    /// Per-frame Mana regen for the local player. Mirrors vanilla's
    /// EnergyRegeneration tick in <c>EntitySystem.UpdateAll</c>: looks up the
    /// regen rate on the stat system, advances current Mana by
    /// <c>regen * deltaTime</c>, clamps to the max stat value. Only ticks the
    /// local player — server doesn't load ClientCore, so this only runs where
    /// it should. Postfix runs after vanilla UpdateAll so any equipment / aura
    /// changes that frame have already applied to the stat values.
    /// </summary>
    [HarmonyPatch(typeof(EntitySystem), nameof(EntitySystem.UpdateAll))]
    internal static class EntitySystemUpdateAllPatch
    {
        private const string ManaStatId = "Mana";
        private const string ManaRegenStatId = "ManaRegeneration";

        private static void Postfix(GameTime gameTime)
        {
            var entity = Candide.GameModels.GameState.LocalPlayer?.Character?.Entity;
            if (entity is null) return;

            // ManaRegeneration is the per-second rate; gameTime.ElapsedGameTime
            // is the wall-clock delta for this frame.
            var regen = entity.Stats.TryGet(ManaRegenStatId, out var r) ? r : 0f;
            if (regen <= 0f) return;

            var max = GetMax(entity, ManaStatId);
            if (max <= 0f) return;

            var current = GetCurrent(entity, ManaStatId);
            if (current >= max) return; // already full

            var delta = (float)gameTime.ElapsedGameTime.TotalSeconds * regen;
            SetCurrent(entity, ManaStatId, current + delta);
        }
    }
}
