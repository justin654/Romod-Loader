using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Romestead.ModLoader;

namespace Romestead.IconDumpMod;

/// <summary>
/// Developer-only mod. When the game is ready to render it walks every item,
/// resolves its icon to a sprite-sheet + frame (which only works inside the
/// running game, against a live GraphicsDevice), crops that frame to a PNG, and
/// writes the whole set + a manifest into a folder the Mod Value Editor reads.
///
/// Runs once: if the manifest already exists it skips, so it won't hitch every
/// launch. Delete the dump folder (or its manifest) to force a re-export.
/// </summary>
[ModManifest("romestead.icon-dump", "Icon Dump", "0.1.0", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class IconDumpMod : IRomesteadMod
{
    // Must match Romestead.ModValueEditor's IconDump.DumpDir.
    private static string DumpDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Romestead.ModValueEditor", "icon-dump");

    private static string ManifestPath => Path.Combine(DumpDir, "manifest.json");

    private IModLogger _log = null!;
    private bool _done;

    public void Initialize(IModContext context)
    {
        _log = context.Logger;
        context.Lifecycle.GameReady += OnGameReady;
        _log.Info($"Icon Dump ready. Icons export to: {DumpDir}");
    }

    private void OnGameReady()
    {
        if (_done) return;
        _done = true;

        try
        {
            if (File.Exists(ManifestPath))
            {
                _log.Info("Icon Dump: manifest already present, skipping. Delete it to re-export.");
                return;
            }
            Export();
        }
        catch (Exception ex)
        {
            _log.Error($"Icon Dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Export()
    {
        Directory.CreateDirectory(DumpDir);

        var itemDb = FindType("Shared.Data.ItemDataBase");
        var iconDb = FindType("Candide.Database.IconDataBase");
        var iconFlagType = FindType("CandideCreator.Shared.Graphics.IconFlag");

        var getAllItems = itemDb.GetMethod("GetAllItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(itemDb.FullName, "GetAllItems");
        var getIconForItem = itemDb.GetMethod("GetIconForItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(itemDb.FullName, "GetIconForItem");
        var iconMap = (IDictionary?)(iconDb.GetField("IconDataMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null))
            ?? throw new InvalidOperationException("IconDataBase.IconDataMap was null.");

        // Preferred icon sizes, largest first.
        var flags = new[] { "Large", "Medium", "Huge", "Small" }
            .Where(n => Enum.IsDefined(iconFlagType, n))
            .Select(n => Enum.Parse(iconFlagType, n))
            .ToArray();

        var items = ((IEnumerable)getAllItems.Invoke(null, null)!).Cast<object>().ToList();
        var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int ok = 0, miss = 0, fail = 0;

        foreach (var item in items)
        {
            var id = Field(item, "Id")?.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            try
            {
                var iconKey = getIconForItem.Invoke(null, [id]) as string;
                if (string.IsNullOrEmpty(iconKey) || !iconMap.Contains(iconKey)) { miss++; continue; }

                var iconData = iconMap[iconKey]!;
                var icon = ResolveIcon(iconData, flags);
                if (icon is null) { miss++; continue; }

                var sheet = Field(icon, "SpriteSheet");
                if (Field(sheet, "Texture") is not Texture2D texture) { miss++; continue; }

                var frame = ToInt(Field(icon, "Frame"));
                var sw = ToInt(Field(sheet, "SpriteWidth"));
                var sh = ToInt(Field(sheet, "SpriteHeight"));
                var cols = Math.Max(1, ToInt(Field(sheet, "ColumnCount")));
                if (sw <= 0 || sh <= 0) { miss++; continue; }

                // SpriteSheet.Offset is the grid's inset within the texture; ignoring
                // it shifts every crop up-left and bleeds in the neighbouring sprite.
                var offset = Field(sheet, "Offset") is Vector2 o ? o : Vector2.Zero;
                var src = new Rectangle(
                    (int)offset.X + (frame % cols) * sw,
                    (int)offset.Y + (frame / cols) * sh,
                    sw, sh);
                src = ClampToTexture(src, texture);
                if (src.Width <= 0 || src.Height <= 0) { miss++; continue; }

                if (ok < 4)
                    _log.Info($"Icon Dump sample: {id} icon={iconKey} frame={frame} sprite={sw}x{sh} cols={cols} offset={(int)offset.X},{(int)offset.Y} tex={texture.Width}x{texture.Height} rect={src.X},{src.Y},{src.Width},{src.Height}");

                var file = Sanitize(id) + ".png";
                CropToPng(texture, src, Path.Combine(DumpDir, file));
                manifest[id] = file;
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                if (fail <= 10) _log.Warn($"Icon Dump: {id} failed: {ex.Message}");
            }
        }

        File.WriteAllText(ManifestPath, BuildManifestJson(manifest));
        _log.Info($"Icon Dump complete: {ok} exported, {miss} without icons, {fail} failed -> {DumpDir}");
    }

    private static object? ResolveIcon(object iconData, object[] flags)
    {
        var type = iconData.GetType();
        var getOrNull = type.GetMethod("GetIconOrNull");
        var get = type.GetMethod("GetIcon");
        foreach (var flag in flags)
        {
            if (getOrNull is not null)
            {
                var nullable = getOrNull.Invoke(iconData, [flag]);
                if (nullable is not null) return nullable; // boxed Icon (Nullable unwraps on box)
            }
            else if (get is not null)
            {
                try { return get.Invoke(iconData, [flag]); } catch { /* try next flag */ }
            }
        }
        return null;
    }

    /// <summary>
    /// Crop <paramref name="src"/> out of <paramref name="texture"/> via a render
    /// target so it works regardless of the sheet's surface format (incl. DXT).
    /// </summary>
    private static void CropToPng(Texture2D texture, Rectangle src, string path)
    {
        var gd = texture.GraphicsDevice;
        var previous = gd.GetRenderTargets();
        // PreserveContents so the pixels survive unbinding the target before we
        // read them back (the default DiscardContents can blank the texture).
        using var rt = new RenderTarget2D(gd, src.Width, src.Height, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using (var batch = new SpriteBatch(gd))
        {
            gd.SetRenderTarget(rt);
            gd.Clear(Color.Transparent);
            batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
            batch.Draw(texture, new Rectangle(0, 0, src.Width, src.Height), src, Color.White);
            batch.End();
        }

        if (previous is { Length: > 0 }) gd.SetRenderTargets(previous);
        else gd.SetRenderTarget(null);

        using var fs = File.Create(path);
        rt.SaveAsPng(fs, src.Width, src.Height);
    }

    // -------------------- reflection / json helpers --------------------

    private static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, throwOnError: false);
            if (t is not null) return t;
        }
        throw new TypeLoadException($"Type not found in any loaded assembly: {fullName}");
    }

    private static object? Field(object? instance, string name) =>
        instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);

    private static int ToInt(object? value) => value is null ? 0 : Convert.ToInt32(value);

    private static Rectangle ClampToTexture(Rectangle r, Texture2D texture)
    {
        var x = Math.Clamp(r.X, 0, texture.Width);
        var y = Math.Clamp(r.Y, 0, texture.Height);
        var w = Math.Clamp(r.Width, 0, texture.Width - x);
        var h = Math.Clamp(r.Height, 0, texture.Height - y);
        return new Rectangle(x, y, w, h);
    }

    private static string Sanitize(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        return sb.ToString();
    }

    private static string BuildManifestJson(IDictionary<string, string> map)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"version\": 1,\n  \"icons\": {\n");
        var i = 0;
        foreach (var kv in map)
        {
            sb.Append("    ").Append(JsonString(kv.Key)).Append(": ").Append(JsonString(kv.Value));
            sb.Append(++i < map.Count ? ",\n" : "\n");
        }
        sb.Append("  }\n}\n");
        return sb.ToString();
    }

    private static string JsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            if (c is '"' or '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}
