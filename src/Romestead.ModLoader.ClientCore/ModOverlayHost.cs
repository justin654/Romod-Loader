using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.PauseMenuUi;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Renders the declarative overlays registered through <see cref="IModOverlayRegistry"/> onto a
/// Candide desktop. The registry can be poked from any thread (mod content injection runs off the
/// main thread), so the host never mutates the desktop on the change event itself; it just flags
/// itself dirty and rebuilds during <see cref="Pump"/>, which callers invoke from the scene's
/// per-frame update on the UI thread.
/// </summary>
internal static class ModOverlayHost
{
    private const string OverlayTag = "romestead.modloader.overlay";

    private static readonly List<CandideUiElement> _mounted = new();
    private static CandideDesktop? _desktop;
    private static bool _subscribed;
    private static volatile bool _dirty;

    /// <summary>
    /// Binds the host to a desktop (typically from a scene's UI setup) and paints the current
    /// overlays immediately. Must be called on the UI thread.
    /// </summary>
    public static void Attach(CandideDesktop desktop)
    {
        if (desktop is null)
        {
            return;
        }

        EnsureSubscribed();
        _desktop = desktop;
        _dirty = true;
        Render();
    }

    /// <summary>Detaches from the current desktop when its scene tears down.</summary>
    public static void Detach()
    {
        RemoveMounted();
        _desktop = null;
    }

    /// <summary>Repaints if overlays changed since the last frame. Call from the UI thread each frame.</summary>
    public static void Pump()
    {
        if (!_dirty)
        {
            return;
        }

        Render();
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        ModRegistries.Overlays.OverlaysChanged += () => _dirty = true;
    }

    private static void Render()
    {
        var desktop = _desktop;
        if (desktop is null)
        {
            return;
        }

        _dirty = false;

        try
        {
            RemoveMounted();

            foreach (var overlay in ModRegistries.Overlays.ActiveOverlays)
            {
                var panel = BuildOverlayPanel(overlay);
                desktop.AddChild(panel, false);
                _mounted.Add(panel);
            }
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error("Failed to render mod overlays.", ex);
        }
    }

    private static void RemoveMounted()
    {
        var desktop = _desktop;
        foreach (var element in _mounted)
        {
            desktop?.RemoveChild(element);
        }

        _mounted.Clear();
    }

    private static CandideUiElement BuildOverlayPanel(ModOverlayInstance overlay)
    {
        ResolvePlacement(overlay.Placement, out var horizontal, out var vertical, out var margin);

        var panel = new CandideVerticalStackPanel
        {
            Tag = OverlayTag,
            Width = 460,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = margin
        };

        if (!string.IsNullOrWhiteSpace(overlay.Title))
        {
            panel.AddChild(ModUiRowRenderer.CreateLabel(overlay.Title!, CandideTextStyle.TitleBold, bottomMargin: 10), false);
        }

        var firstSection = true;
        foreach (var section in overlay.Sections)
        {
            if (!firstSection)
            {
                panel.AddChild(ModUiRowRenderer.CreateSeparator(), false);
            }

            firstSection = false;

            if (!string.IsNullOrWhiteSpace(section.Title))
            {
                panel.AddChild(ModUiRowRenderer.CreateLabel(section.Title, CandideTextStyle.MainBold, bottomMargin: 6), false);
            }

            foreach (var row in section.Rows)
            {
                ModUiRowRenderer.RenderRow(panel, row, CreateActionContext);
            }
        }

        return panel;
    }

    private static ModUiActionContext CreateActionContext()
    {
        return new ModUiActionContext
        {
            Logger = CoreState.Logger ?? throw new InvalidOperationException("Logger is not initialized."),
            NavigateToPage = _ => { },
            RefreshCurrentPage = () => _dirty = true
        };
    }

    private static void ResolvePlacement(
        ModOverlayPlacement placement,
        out HorizontalAlignment horizontal,
        out VerticalAlignment vertical,
        out CandideThickness margin)
    {
        switch (placement)
        {
            case ModOverlayPlacement.TopLeft:
                horizontal = HorizontalAlignment.Left;
                vertical = VerticalAlignment.Top;
                margin = new CandideThickness { Left = 24, Top = 24 };
                break;
            case ModOverlayPlacement.TopRight:
                horizontal = HorizontalAlignment.Right;
                vertical = VerticalAlignment.Top;
                margin = new CandideThickness { Right = 24, Top = 24 };
                break;
            case ModOverlayPlacement.BottomLeft:
                horizontal = HorizontalAlignment.Left;
                vertical = VerticalAlignment.Bottom;
                margin = new CandideThickness { Left = 24, Bottom = 24 };
                break;
            case ModOverlayPlacement.BottomRight:
                horizontal = HorizontalAlignment.Right;
                vertical = VerticalAlignment.Bottom;
                margin = new CandideThickness { Right = 24, Bottom = 24 };
                break;
            default:
                horizontal = HorizontalAlignment.Center;
                vertical = VerticalAlignment.Center;
                margin = new CandideThickness();
                break;
        }
    }
}
