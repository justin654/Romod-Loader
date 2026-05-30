using Candide.CandideUI;
using Candide.CandideUI.Windows;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Opens the game's native crafting windows (<see cref="SecondaryCraftingWindow"/>) on behalf of mods
/// in response to <see cref="IModCraftingRegistry"/>. The native window binds itself to the local
/// player's inventory when shown, so mod-registered recipes for the requested stations appear with the
/// game's own look. Like the other hosts, the registry can be poked from any thread; the host only
/// flags itself dirty and reconciles on the gameplay update (UI thread).
/// </summary>
internal static class ModCraftingWindowHost
{
    private static readonly Dictionary<string, SecondaryCraftingWindow> _mounted = new(StringComparer.Ordinal);
    private static CandideDesktop? _desktop;
    private static bool _subscribed;
    private static volatile bool _dirty;

    public static void Attach(CandideDesktop desktop)
    {
        if (desktop is null)
        {
            return;
        }

        EnsureSubscribed();
        Pump(desktop);
    }

    public static void Detach()
    {
        if (_desktop is null)
        {
            return;
        }

        try
        {
            foreach (var window in _mounted.Values)
            {
                window.Close();
            }
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to close mod crafting windows on detach.", ex);
        }

        _mounted.Clear();
        _desktop = null;
    }

    public static void Pump(CandideDesktop desktop)
    {
        if (desktop is null)
        {
            return;
        }

        if (!ReferenceEquals(desktop, _desktop))
        {
            _mounted.Clear();
            _desktop = desktop;
            _dirty = true;
        }

        if (!_dirty)
        {
            return;
        }

        Reconcile();
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        ModRegistries.Crafting.Changed += () => _dirty = true;
    }

    private static void Reconcile()
    {
        var desktop = _desktop;
        if (desktop is null)
        {
            return;
        }

        _dirty = false;

        try
        {
            var active = ModRegistries.Crafting.ActiveStationWindows;
            var liveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instance in active)
            {
                liveIds.Add(instance.Id);
            }

            foreach (var id in _mounted.Keys.ToArray())
            {
                if (!liveIds.Contains(id))
                {
                    _mounted[id].Close();
                    _mounted.Remove(id);
                }
            }

            foreach (var instance in active)
            {
                if (_mounted.ContainsKey(instance.Id))
                {
                    continue;
                }

                var window = new SecondaryCraftingWindow(instance.StationIds.ToArray());
                var windowId = instance.Id;
                window.Closed += (_, _) => ModRegistries.Crafting.Close(windowId);
                window.Show(desktop, instance.X, instance.Y);

                _mounted[instance.Id] = window;
            }
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to open mod crafting windows.", ex);
        }
    }
}
