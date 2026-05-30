using System.Reflection;
using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.UserControls;
using CandideCreator.Shared.Graphics;
using FontStashSharp;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Draws a blue Mana bar directly under the vanilla HP bar by postfixing
/// <c>HealthBar.InternalDraw</c>. Reuses the vanilla HP-bar sprites tinted
/// with a blue colour, and reads current/max Mana from
/// <see cref="ManaTracker"/>. Bar is hidden when the local player has no
/// Mana stat (so non-magical playthroughs aren't affected visually).
/// </summary>
internal static class ManaBarRenderer
{
    private const string ManaStatId = "Mana";
    private const string BackgroundSprite = "media/interface/hpbar_background3";
    private const string FillSprite = "media/interface/hpbar_hpbar3";
    private static readonly Color ManaTint = new(80, 140, 255);
    private static readonly Color BackgroundTint = Color.White;

    private static SpriteSheet? _background;
    private static SpriteSheet? _fill;
    private static IModLogger? _logger;
    private static bool _loggedFirstDraw;
    private static bool _loggedFirstFire;
    private static SpriteFontBase? _hoverFont;
    private static bool _hoverFontFetchFailed;

    public static void Install(IModLogger logger)
    {
        _logger = logger;
    }

    // InternalDraw is non-public on HealthBar, so target by name string rather than nameof().
    [HarmonyPatch(typeof(HealthBar), "InternalDraw")]
    internal static class HealthBarInternalDrawPatch
    {
        private static void Postfix(HealthBar __instance, UiRenderContext context)
        {
            try
            {
                Draw(__instance, context);
            }
            catch (Exception ex)
            {
                _logger?.Error("[mana-bar] Draw threw.", ex);
            }
        }
    }

    private static void Draw(HealthBar healthBar, UiRenderContext context)
    {
        if (!_loggedFirstFire)
        {
            _loggedFirstFire = true;
            _logger?.Info("[mana-bar] HealthBar.InternalDraw postfix is firing.");
        }

        var entity = Candide.GameModels.GameState.LocalPlayer?.Character?.Entity;
        if (entity is null)
        {
            if (!_loggedFirstDraw)
            {
                _loggedFirstDraw = true;
                _logger?.Info("[mana-bar] No local player entity yet; bar hidden.");
            }
            return;
        }

        var max = ManaTracker.GetMax(entity, ManaStatId);
        var current = Math.Max(0f, ManaTracker.GetCurrent(entity, ManaStatId));
        var ratio = max > 0f ? Math.Min(1f, current / max) : 0f;

        var bg = _background ??= CandideCreator.Shared.Content.SpriteSheet(BackgroundSprite);
        var fill = _fill ??= CandideCreator.Shared.Content.SpriteSheet(FillSprite);

        var scale = (float)CandideUiSystem.Scale;
        var bounds = healthBar.ActualBounds;
        // Place the bar directly below the HP bar — same X edge, Y just past the
        // HP bar's bottom. ActualBounds is the post-layout pixel rect already
        // scaled to the UI system's scale, so we don't add scale to the offset.
        var basePos = new Vector2(bounds.X, bounds.Bottom);

        if (!_loggedFirstDraw)
        {
            _loggedFirstDraw = true;
            _logger?.Info(
                $"[mana-bar] First draw: max={max:F1} current={current:F1} ratio={ratio:F2} " +
                $"scale={scale} bounds={bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height} " +
                $"basePos={basePos.X:F0},{basePos.Y:F0} " +
                $"bgSprite={bg.SpriteWidth}x{bg.SpriteHeight} fillSprite={fill.SpriteWidth}x{fill.SpriteHeight}");
        }

        if (max <= 0f) return;

        // Bar rectangle in screen-space pixels — used both for the background
        // draw position and for hover hit-testing against the mouse cursor.
        var barRect = new Rectangle(
            (int)basePos.X,
            (int)basePos.Y,
            (int)(bg.SpriteWidth * scale),
            (int)(bg.SpriteHeight * scale));

        // Background frame. The UI system handles scaling internally, so the
        // sprite scale parameter is Vector2.One (matches vanilla HealthBar).
        context.DrawSprite(
            bg,
            (Rectangle?)null,
            basePos,
            Vector2.One,
            0f,
            Vector2.Zero,
            BackgroundTint,
            SpriteEffects.None);

        // Blue fill — vanilla HP drains top-to-bottom, so we crop the source
        // rect vertically and keep the BOTTOM portion. As ratio → 0 the top
        // of the fill recedes downward, mimicking HP.
        var fillInset = new Vector2(3f, 5f) * scale;
        var fillHeight = (int)Math.Round(fill.SpriteHeight * ratio);
        if (fillHeight > 0)
        {
            var topOffset = fill.SpriteHeight - fillHeight;
            var src = new Rectangle(0, topOffset, fill.SpriteWidth, fillHeight);
            // Shift draw position down by the missing height (in scaled pixels)
            // so the visible fill stays anchored to the bottom of the frame.
            var fillPos = basePos + fillInset + new Vector2(0f, topOffset * scale);
            context.DrawSprite(
                fill,
                src,
                fillPos,
                Vector2.One,
                0f,
                Vector2.Zero,
                ManaTint,
                SpriteEffects.None);
        }

        DrawHoverLabel(context, barRect, current, max, scale);
    }

    /// <summary>
    /// Mimics the vanilla HP tooltip: while the mouse cursor is inside the
    /// mana bar, draw "current / max Mana" next to it. Mouse position is in
    /// window pixel coordinates; <see cref="HealthBar.ActualBounds"/> is in
    /// the same coordinate space, so a direct <c>Rectangle.Contains</c> works.
    /// </summary>
    private static void DrawHoverLabel(UiRenderContext context, Rectangle barRect, float current, float max, float scale)
    {
        var mouse = Mouse.GetState().Position;
        if (!barRect.Contains(mouse)) return;

        var font = GetHoverFont();
        if (font is null) return;

        var text = $"{(int)Math.Round(current)} / {(int)Math.Round(max)} Mana";
        // Position the label to the right of the bar, vertically centered.
        var labelPos = new Vector2(barRect.Right + 4f * scale, barRect.Y + (barRect.Height - font.LineHeight * scale) * 0.5f);

        context.DrawString(
            font,
            text,
            labelPos,
            Color.White,
            Vector2.One * scale,
            0f,
            true,
            0f,
            -1);
    }

    private static SpriteFontBase? GetHoverFont()
    {
        if (_hoverFont is not null || _hoverFontFetchFailed) return _hoverFont;
        try
        {
            // CandideTextLabel.GetFont(CandideTextStyle) is private; reflect a
            // fresh instance and ask it for the MainSmall style's font so we
            // match the rest of the HUD's typography automatically.
            var label = new CandideTextLabel();
            var getFont = AccessTools.Method(typeof(CandideTextLabel), "GetFont");
            _hoverFont = getFont?.Invoke(label, [CandideTextStyle.MainSmall]) as SpriteFontBase;
        }
        catch (Exception ex)
        {
            _hoverFontFetchFailed = true;
            _logger?.Warn($"[mana-bar] Could not resolve hover label font: {ex.Message}. Tooltip will be hidden.");
        }
        return _hoverFont;
    }
}
