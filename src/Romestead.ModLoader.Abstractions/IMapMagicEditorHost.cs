namespace Romestead.ModLoader;

/// <summary>
/// Optional in-world map / object editor host registered by the MapMagic client mod.
/// ClientCore forwards StandardMode update/draw/leave and attack suppression when a host is present.
/// </summary>
public interface IMapMagicEditorHost
{
    bool Active { get; }

    void UpdateWorldEditor(IModLogger? log, bool isMouseOverGui, int desktopWidth);

    /// <param name="spriteBatch">MonoGame <c>SpriteBatch</c>; typed as <see cref="object"/> so Abstractions stays game-agnostic.</param>
    void DrawInWorldUi(object spriteBatch);

    void DisableWorldEditor(IModLogger? log);
}
