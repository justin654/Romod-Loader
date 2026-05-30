using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Romestead.ModLoader;

namespace Romestead.MapMagic;

internal sealed class MapMagicEditorBridge : IMapMagicEditorHost
{
    public bool Active => MapMagicHost.Active;

    public void UpdateWorldEditor(IModLogger? log, bool isMouseOverGui, int desktopWidth) =>
        MapMagicHost.UpdateWorldEditor(log, isMouseOverGui, desktopWidth);

    public void DrawInWorldUi(object spriteBatch) =>
        MapMagicHost.DrawInWorldUi((SpriteBatch)spriteBatch);

    public void DisableWorldEditor(IModLogger? log) =>
        MapMagicHost.DisableWorldEditor(log);
}
