using HarmonyLib;
using Candide;
using Candide.Data;
using Candide.Database;
using Candide.GameModels.Managers;
using Candide.Scene;
using CandideCreator.Shared.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

internal static class ModEquipmentDisplayHost
{
    private static bool _displayDataInjected;

    public static void InjectDisplayData(ref List<CharacterDisplayData> displayData)
    {
        if (_displayDataInjected)
        {
            return;
        }

        _displayDataInjected = true;
        var registrations = EnumerateRegistrations().ToList();
        if (registrations.Count == 0)
        {
            return;
        }

        var ids = new HashSet<string>(displayData.Select(d => d.Id), StringComparer.Ordinal);
        var injected = 0;
        foreach (var registration in registrations)
        {
            if (!ids.Add(registration.DisplayId) ||
                CharacterDisplayDataBase.GetDisplayDataOrNull(registration.DisplayId).HasValue)
            {
                CoreState.Logger?.Warn($"[equipment-display] Display id '{registration.DisplayId}' already exists; skipping '{registration.Item.Id}'.");
                RegisterContentDiagnostic(registration.Item.Id, "EquipmentDisplay", registration.DisplayId, "SkippedDuplicate", "Display id already exists.");
                continue;
            }

            displayData.Add(CreateDisplayData(registration.DisplayId, registration.Display));
            injected++;
            RegisterContentDiagnostic(registration.Item.Id, "EquipmentDisplay", registration.DisplayId, "InjectedIntoCharacterDisplayLoad", "Added custom character display data.");
        }

        if (injected > 0)
        {
            CoreState.Logger?.Info($"[equipment-display] Injected {injected} mod equipment display definition(s).");
        }
    }

    public static void InjectPlayerSkinParts()
    {
        var injected = 0;
        foreach (var registration in EnumerateRegistrations())
        {
            foreach (var fragment in registration.Display.Fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment.TexturePath))
                {
                    continue;
                }

                if (TryInjectSkinPart(registration.Item.Id, fragment))
                {
                    injected++;
                }
            }
        }

        if (injected > 0)
        {
            CoreState.Logger?.Info($"[equipment-display] Injected {injected} custom player skin slice(s).");
        }
    }

    private static bool TryInjectSkinPart(string itemId, EquipmentDisplayFragmentDefinition fragment)
    {
        var texturePath = Path.GetFullPath(fragment.TexturePath!);
        if (!File.Exists(texturePath))
        {
            CoreState.Logger?.Warn($"[equipment-display] '{itemId}' skin texture '{texturePath}' was not found.");
            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "Failed", $"Texture not found: {texturePath}");
            return false;
        }

        if (!PlayerSkinManager.Parts.TryGetValue(fragment.SkinTag, out var parts))
        {
            CoreState.Logger?.Warn($"[equipment-display] '{itemId}' skin tag {fragment.SkinTag} does not exist.");
            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "Failed", $"Skin tag {fragment.SkinTag} does not exist.");
            return false;
        }

        if (parts.ContainsKey(fragment.SkinName))
        {
            CoreState.Logger?.Warn($"[equipment-display] Skin '{fragment.SkinName}' already exists for tag {fragment.SkinTag}; skipping '{itemId}'.");
            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "SkippedDuplicate", "Skin name already exists for this tag.");
            return false;
        }

        if (fragment.SpriteWidth <= 0 || fragment.SpriteHeight <= 0)
        {
            CoreState.Logger?.Warn($"[equipment-display] '{itemId}' skin '{fragment.SkinName}' has invalid frame size {fragment.SpriteWidth}x{fragment.SpriteHeight}.");
            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "Failed", "Frame width and height must be greater than zero.");
            return false;
        }

        try
        {
            var texture = LoadTexture(texturePath, itemId, fragment.SkinName);
            if (texture.Width % fragment.SpriteWidth != 0 || texture.Height % fragment.SpriteHeight != 0)
            {
                CoreState.Logger?.Warn(
                    $"[equipment-display] '{itemId}' skin '{fragment.SkinName}' texture {texture.Width}x{texture.Height} " +
                    $"is not evenly divisible by frame size {fragment.SpriteWidth}x{fragment.SpriteHeight}.");
            }

            var spriteSheet = CandideCreator.Shared.Content.SpriteSheet(
                texture,
                fragment.SpriteWidth,
                fragment.SpriteHeight,
                Vector2.Zero);

            parts.Add(fragment.SkinName, new PlayerSkinPart
            {
                Id = parts.Count,
                Name = fragment.SkinName,
                Slices = new Dictionary<string, PlayerSkinSlice>
                {
                    [string.Empty] = new()
                    {
                        SpacTag = fragment.SpacTag,
                        Layer = fragment.Layer,
                        HideBase = fragment.HideBaseSkin,
                        DepthOffset = fragment.DepthOffset,
                        SpriteSheetPath = texture.Name,
                        SpriteSheet = new Dictionary<string, SpriteSheet>
                        {
                            [string.Empty] = spriteSheet
                        }
                    }
                }
            });

            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "InjectedIntoPlayerSkinManager", $"Loaded texture from '{texturePath}'.");
            return true;
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error($"[equipment-display] Failed to load skin '{fragment.SkinName}' for item '{itemId}'.", ex);
            RegisterContentDiagnostic(itemId, "EquipmentDisplaySkin", fragment.SkinName, "Failed", ex.Message);
            return false;
        }
    }

    private static CharacterDisplayData CreateDisplayData(string displayId, EquipmentDisplayDefinition display)
    {
        var fragments = display.Fragments
            .Select(fragment =>
            {
                var palette = fragment.Palette.Count == 0
                    ? null
                    : fragment.Palette
                        .Select(p => (p.PaletteId, p.Row))
                        .ToArray();

                return new CharacterDisplayFragment
                {
                    SkinTag = fragment.SkinTag,
                    SkinName = fragment.SkinName,
                    Palette = palette!,
                    Layer = fragment.Layer
                };
            })
            .ToArray();

        return new CharacterDisplayData
        {
            Id = displayId,
            Fragments = fragments,
            SpacTagsToHide = display.SpacTagsToHide.Count == 0 ? null! : display.SpacTagsToHide.ToArray()
        };
    }

    private static Texture2D LoadTexture(string texturePath, string itemId, string skinName)
    {
        var graphicsDevice = ResolveGraphicsDevice()
            ?? throw new InvalidOperationException("Graphics device is not available yet for custom equipment display loading.");

        var texture = Texture2D.FromFile(graphicsDevice, texturePath);
        texture.Name = $"romestead_modding/equipment/{SanitizeContentPath(itemId)}/{SanitizeContentPath(skinName)}";
        CandideCreator.Shared.Content.DirectContentMap[texture.Name] = texture;
        return texture;
    }

    private static GraphicsDevice? ResolveGraphicsDevice()
    {
        if (Globals.GraphicsDevice is not null)
        {
            return Globals.GraphicsDevice;
        }

        var gameSceneGame = AccessTools.Field(typeof(GameSceneManager), "GameSceneGame")?.GetValue(null);
        return gameSceneGame is Game game ? game.GraphicsDevice : null;
    }

    private static IEnumerable<EquipmentDisplayRegistration> EnumerateRegistrations()
    {
        foreach (var item in ModRegistries.Items.Pending)
        {
            var equipment = item.Equipment;
            var display = equipment?.Display;
            if (equipment is null || display is null || display.Fragments.Count == 0)
            {
                continue;
            }

            var displayId = ModEquipmentDisplayIds.ResolveForItem(item.Id, equipment);
            if (string.IsNullOrWhiteSpace(displayId))
            {
                continue;
            }

            yield return new EquipmentDisplayRegistration(item, display, displayId);
        }
    }

    private static string GetModIdForItem(string itemId) =>
        ModRegistries.Diagnostics.Content
            .FirstOrDefault(content => content.ItemIds.Contains(itemId, StringComparer.Ordinal))?.ModId
        ?? "<unknown>";

    private static void RegisterContentDiagnostic(string itemId, string contentType, string contentId, string status, string detail)
    {
        ModRegistries.Diagnostics.RegisterContentDiagnostic(new ContentDiagnosticInfo(
            GetModIdForItem(itemId),
            contentType,
            contentId,
            status,
            detail));
    }

    private static string SanitizeContentPath(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        return new string(chars);
    }

    private sealed record EquipmentDisplayRegistration(ItemDefinition Item, EquipmentDisplayDefinition Display, string DisplayId);
}

[HarmonyPatch(typeof(CharacterDisplayDataBase), nameof(CharacterDisplayDataBase.AddCharacterDisplayData))]
internal static class CharacterDisplayDataBaseAddCharacterDisplayDataPatch
{
    private static void Prefix(ref List<CharacterDisplayData> __0) =>
        ModEquipmentDisplayHost.InjectDisplayData(ref __0);
}

[HarmonyPatch(typeof(PlayerSkinManager), nameof(PlayerSkinManager.Setup))]
internal static class PlayerSkinManagerSetupPatch
{
    private static void Postfix() => ModEquipmentDisplayHost.InjectPlayerSkinParts();
}
