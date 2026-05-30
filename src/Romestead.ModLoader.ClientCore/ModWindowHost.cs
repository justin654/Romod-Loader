using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Containers;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Renders the draggable in-game windows registered through <see cref="IModWindowRegistry"/> onto the
/// active gameplay desktop. Mirrors <see cref="ModOverlayHost"/>: the registry may be poked from any
/// thread, so the host only flags itself dirty on change and reconciles during <see cref="Pump"/>,
/// which the gameplay mode manager's per-frame update drives on the UI thread.
///
/// Windows are reconciled (not torn down) across content updates so a window keeps its position and
/// drag state while its sections change in place.
/// </summary>
internal static class ModWindowHost
{
    private const int DefaultWidth = 360;
    private const int TitleBarHeight = 36;
    // Cap the content viewport so long windows (e.g. the debug teleport list) scroll
    // instead of running off the screen.
    private const int MaxContentHeight = 430;

    private sealed class Mounted
    {
        public required CandideWindow Window { get; init; }
        public object? SectionsToken { get; set; }
        public string? Title { get; set; }
    }

    private static readonly object _sync = new();
    private static readonly Dictionary<string, Mounted> _mounted = new(StringComparer.Ordinal);
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

    /// <summary>Closes all mod windows and unbinds when the gameplay mode tears down.</summary>
    public static void Detach()
    {
        var desktop = _desktop;
        if (desktop is null)
        {
            return;
        }

        try
        {
            foreach (var mounted in _mounted.Values)
            {
                mounted.Window.Close();
            }
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to close mod windows on detach.", ex);
        }

        _mounted.Clear();
        _desktop = null;
    }

    /// <summary>
    /// Binds to the supplied desktop (rebinding if it changed) and reconciles open windows when the
    /// registry changed since the last frame. Called from the gameplay update on the UI thread.
    /// </summary>
    public static void Pump(CandideDesktop desktop)
    {
        if (desktop is null)
        {
            return;
        }

        if (!ReferenceEquals(desktop, _desktop))
        {
            // The gameplay desktop was (re)created; drop stale mounts and repaint onto the new one.
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
        ModRegistries.Windows.WindowsChanged += () => _dirty = true;
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
            var active = ModRegistries.Windows.ActiveWindows;
            var liveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instance in active)
            {
                liveIds.Add(instance.Id);
            }

            foreach (var id in _mounted.Keys.ToArray())
            {
                if (!liveIds.Contains(id))
                {
                    _mounted[id].Window.Close();
                    _mounted.Remove(id);
                }
            }

            foreach (var instance in active)
            {
                if (_mounted.TryGetValue(instance.Id, out var existing))
                {
                    var sectionsChanged = !ReferenceEquals(existing.SectionsToken, instance.Sections);
                    var titleChanged = !string.Equals(existing.Title, instance.Title, StringComparison.Ordinal);
                    if (sectionsChanged || titleChanged)
                    {
                        existing.Window.SetChild(BuildContent(instance, existing.Window));
                        existing.SectionsToken = instance.Sections;
                        existing.Title = instance.Title;
                    }

                    continue;
                }

                var window = new CandideWindow(MapStyle(instance.Style));
                window.SetChild(BuildContent(instance, window));
                var windowId = instance.Id;
                window.Closed += (_, _) => ModRegistries.Windows.Close(windowId);
                window.Show(desktop, instance.X, instance.Y);

                _mounted[instance.Id] = new Mounted
                {
                    Window = window,
                    SectionsToken = instance.Sections,
                    Title = instance.Title
                };
            }
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to render mod windows.", ex);
        }
    }

    private static CandideUiElement BuildContent(ModWindowInstance instance, CandideWindow window)
    {
        var width = instance.Width ?? DefaultWidth;

        var root = new CandidePanel
        {
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var stack = new CandideVerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Left = 16, Top = 12, Right = 16, Bottom = 12 }
        };

        if (!string.IsNullOrWhiteSpace(instance.Title))
        {
            stack.AddChild(ModUiRowRenderer.CreateLabel(instance.Title!, CandideTextStyle.TitleBold, bottomMargin: 10), false);
        }

        var firstSection = true;
        foreach (var section in instance.Sections)
        {
            if (!firstSection)
            {
                stack.AddChild(ModUiRowRenderer.CreateSeparator(), false);
            }

            firstSection = false;

            if (!string.IsNullOrWhiteSpace(section.Title))
            {
                stack.AddChild(ModUiRowRenderer.CreateLabel(section.Title, CandideTextStyle.MainBold, bottomMargin: 6), false);
            }

            foreach (var row in section.Rows)
            {
                ModUiRowRenderer.RenderRow(stack, row, () => CreateActionContext(instance.Id));
            }
        }

        if (instance.ShowCloseButton)
        {
            stack.AddChild(ModUiRowRenderer.CreateSeparator(), false);
            stack.AddChild(
                ModUiRowRenderer.CreateButton("Close", () => ModRegistries.Windows.Close(instance.Id)),
                false);
        }

        // Wrap the content in a scroll viewer so long windows (e.g. the debug teleport list)
        // scroll within a bounded viewport instead of running off the bottom of the screen.
        // The viewer needs a bounding MaxHeight for ScrollYMaximum to become non-zero.
        var scroll = new CandideVerticalScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = MaxContentHeight,
            ShowVerticalScrollBar = true
        };
        scroll.SetChild(stack);
        root.AddChild(scroll, false);

        // A transparent drag strip across the top makes the window draggable by its title region,
        // matching how the game's built-in windows anchor their drag handle.
        root.AddChild(new CandideWindowAnchorDrag(window, new Rectangle(0, 0, width, TitleBarHeight)), false);

        return root;
    }

    private static ModUiActionContext CreateActionContext(string windowId) =>
        new()
        {
            Logger = CoreState.Logger ?? throw new InvalidOperationException("Logger is not initialized."),
            NavigateToPage = _ => { },
            RefreshCurrentPage = () => _dirty = true
        };

    private static WindowGraphics MapStyle(ModWindowStyle style) =>
        style switch
        {
            ModWindowStyle.Dark => WindowGraphics.Dark,
            ModWindowStyle.None => WindowGraphics.None,
            _ => WindowGraphics.Standard
        };
}
