using Candide.GameModels;
using Candide.GameModels.Helpers;
using Candide.GameModels.Managers;
using Candide.GameModels.Models.Constructions;
using Candide.GameModels.Services;
using Candide.Input;
using Candide.PlayerMode.Construction;
using Candide.PlayerMode.Furniture;
using Candide.World;
using Candide.World.AutoMesh;
using CandideServer;
using CandideServer.MessageModels.Entities;
using CandideServer.MessageModels;
using CandideServer.ServerServices;
using CandideCreator.Shared.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using HarmonyLib;
using Romestead.ModLoader;
using Shared.Data.Furniture;
using Shared.Entity;
using Shared.Models;

namespace Romestead.MapMagic;

internal static class MapMagicHost
{
    private const string HelpWindowId = "romestead.live-map-editor.help";
    private const string PaletteWindowId = "romestead.live-map-editor.tile-palette";
    private const float DefaultTileSize = 16f;
    private const float EntityPickRadius = 28f;
    private const int HelpWindowWidth = 240;
    private const int PaletteWindowWidth = 330;
    private const int HelpWindowMargin = 300;
    private const int PaletteButtonWidth = 86;
    private const int PaletteColumns = 3;
    private const int EditorWindowY = 48;
    private const float EntityHealthStep = 25f;
    private static readonly HighlightedBuilding ConstructionHighlight = new();
    private static readonly Color TileHighlightColor = new(64, 235, 255, 210);
    private static readonly System.Reflection.MethodInfo? CursorWorldPosGetter =
        AccessTools.Method(typeof(FurnitureModeManager), "get_CursorWorldPos");
    private static readonly System.Reflection.MethodInfo? CreateZoneMeshMethod =
        AccessTools.Method(typeof(AutoMesher), "CreateZoneMesh");

    private static bool _active;
    private static LiveEditorMode _mode = LiveEditorMode.Objects;
    private static IModWindowHandle? _helpWindow;
    private static IModWindowHandle? _paletteWindow;
    private static string? _helpWindowSnapshot;
    private static string? _paletteWindowSnapshot;
    private static string? _constructionId;
    private static string? _entityBaseGuid;
    private static string? _status;
    private static EditorSelection? _selected;
    private static EditorSelection? _worldTarget;
    private static Vector3? _worldCursorPos;
    private static Guid? _worldCursorWorldId;
    private static string? _worldTargetReason;
    private static Guid? _highlightedEntityId;
    private static Point? _worldTileCursor;
    private static string? _worldTileReason;
    private static List<TileBrushCell>? _tileBrush;
    private static int _tileBrushRadius = 1;
    private static TileBrushShape _tileBrushShape = TileBrushShape.Single;
    private static Point? _lineAnchor;
    private static Point? _lastPaintTile;
    private static TilePaintMode? _lastPaintMode;
    private static bool _loggedCheatEnable;
    private static IModLogger? _modLogger;

    internal static IModLogger? ModLogger
    {
        get => _modLogger;
        set => _modLogger = value;
    }

    internal static bool Active => _active;

    internal static void ToggleWorldEditor(IModLogger? log)
    {
        if (_active)
        {
            DisableWorldEditor(log);
            return;
        }

        _active = true;
        _mode = LiveEditorMode.Objects;
        EnsureCheatsEnabled(log);
        SetStatus("Object editor ON. Left click select, right click delete, Interact clone, Place move. F9: tile mode. F8/Tab/Esc: exit.", log);
    }

    internal static void ToggleTileEditor(IModLogger? log)
    {
        if (_active && _mode == LiveEditorMode.Tiles)
        {
            DisableWorldEditor(log);
            return;
        }

        _active = true;
        _mode = LiveEditorMode.Tiles;
        ClearHighlights();
        EnsureCheatsEnabled(log);
        SetStatus("Tile editor ON. Left click samples brush area, hold right click paints, hold Interact paints ground, Delete clears structure. F9: exit tile mode. F8/Tab/Esc: exit editor.", log);
    }

    internal static void DisableWorldEditor(IModLogger? log)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _mode = LiveEditorMode.Objects;
        ClearHighlights();
        _helpWindow?.Close();
        _helpWindow = null;
        _helpWindowSnapshot = null;
        _paletteWindow?.Close();
        _paletteWindow = null;
        _paletteWindowSnapshot = null;
        _worldTileCursor = null;
        _worldTileReason = null;
        _lineAnchor = null;
        SetStatus("Live map editor OFF.", log);
    }

    internal static void UpdateWorldEditor(IModLogger? log, bool isMouseOverGui, int desktopWidth)
    {
        if (PollToggleHotkey())
        {
            ToggleWorldEditor(log);
        }

        if (PollTileHotkey())
        {
            ToggleTileEditor(log);
        }

        if (!_active)
        {
            return;
        }

        UpdateWorldProbe(log, isMouseOverGui);
        HandleEditorInput(log, isMouseOverGui);
        UpdateHighlight();
        UpdateHelpWindow(desktopWidth);
    }

    internal static void DrawInWorldUi(SpriteBatch batch)
    {
        if (!_active)
        {
            return;
        }

        ConstructionHighlight.DrawInWorldUi(batch);
        DrawTileHighlight(batch);
    }

    private static bool PollToggleHotkey() => InputManager.Pressed(Keys.F8);
    private static bool PollTileHotkey() => InputManager.Pressed(Keys.F9);

    private static void UpdateWorldProbe(IModLogger? log, bool isMouseOverGui)
    {
        EnsureCheatsEnabled(log);

        if (isMouseOverGui)
        {
            return;
        }

        if (!TryGetCursorWorldPosition(out var cursorPos))
        {
            _worldTarget = null;
            _worldTargetReason = "cursor position unavailable.";
            return;
        }

        var currentWorldId = GameState.CurrentWorld?.Id;
        if (!currentWorldId.HasValue)
        {
            _worldTarget = null;
            _worldCursorPos = null;
            _worldCursorWorldId = null;
            _worldTargetReason = "current world unavailable.";
            return;
        }

        _worldCursorPos = cursorPos;
        _worldCursorWorldId = currentWorldId.Value;
        UpdateTileProbe(cursorPos);

        if (_mode == LiveEditorMode.Tiles)
        {
            _worldTarget = null;
            _worldTargetReason = null;
            return;
        }

        if (TryGetTargetAt(cursorPos, currentWorldId.Value, out var selection, out var reason))
        {
            _worldTarget = selection;
            _worldTargetReason = null;
            return;
        }

        _worldTarget = null;
        _worldTargetReason = reason;
    }

    private static void UpdateTileProbe(Vector3 cursorPos)
    {
        var tile = WorldPosToTile(cursorPos);
        _worldTileCursor = tile;
        _worldTileReason = TryGetClientTile(tile, out _)
            ? null
            : "tile outside loaded world grid.";
    }

    private static void UpdateHighlight()
    {
        ConstructionHighlight.SetEmpty();
        ClearEntityHighlightIfChanged(_worldTarget?.EntityId);

        if (!_active || _worldTarget is null)
        {
            return;
        }

        switch (_worldTarget.Kind)
        {
            case EditorSelectionKind.Building when _worldTarget.BuildingId.HasValue &&
                GameState.TryGetBuilding(_worldTarget.BuildingId.Value, out var building):
                ConstructionHighlight.Set(building, _worldTarget.TownId);
                return;

            case EditorSelectionKind.Decoration when _worldTarget.DecorationId.HasValue &&
                GameState.TryGetDecoration(_worldTarget.DecorationId.Value, out var decoration):
                ConstructionHighlight.Set(decoration, _worldTarget.TownId);
                return;

            case EditorSelectionKind.Entity when _worldTarget.EntityId.HasValue &&
                GameState.Entities is not null &&
                GameState.Entities.TryGetValue(_worldTarget.EntityId.Value, out var entity):
                ApplyEntityHighlight(entity);
                return;
        }
    }

    private static void DrawTileHighlight(SpriteBatch batch)
    {
        if (!_active || _mode != LiveEditorMode.Tiles || !_worldTileCursor.HasValue)
        {
            return;
        }

        try
        {
            foreach (var span in GetBrushPreviewSpans(_worldTileCursor.Value))
            {
                for (var y = span.Top; y < span.Bottom; y++)
                {
                    for (var x = span.Left; x < span.Right; x++)
                    {
                        ClientWorldTileHelper.TileHighlightSpriteSheet?.Draw(
                            batch,
                            new Vector2(x * DefaultTileSize, y * DefaultTileSize),
                            0,
                            TileHighlightColor,
                            1f,
                            1f,
                            1f);
                    }
                }
            }
        }
        catch
        {
            // Highlighting is editor feedback only; tile writes should not depend on draw assets.
        }
    }

    private static void ApplyEntityHighlight(EntityWrapper entity)
    {
        if (_highlightedEntityId.HasValue && _highlightedEntityId.Value != entity.Id)
        {
            ClearEntityHighlight(_highlightedEntityId.Value);
        }

        _highlightedEntityId = entity.Id;
        entity.Highlight = 256f;
        entity.FlashColor = new Vector4(1f, 0.95f, 0.1f, 0.45f);
    }

    private static void ClearHighlights()
    {
        ConstructionHighlight.SetEmpty();
        if (_highlightedEntityId.HasValue)
        {
            ClearEntityHighlight(_highlightedEntityId.Value);
            _highlightedEntityId = null;
        }
    }

    private static void ClearEntityHighlightIfChanged(Guid? currentEntityId)
    {
        if (!_highlightedEntityId.HasValue)
        {
            return;
        }

        if (currentEntityId.HasValue && currentEntityId.Value == _highlightedEntityId.Value)
        {
            return;
        }

        ClearEntityHighlight(_highlightedEntityId.Value);
        _highlightedEntityId = null;
    }

    private static void ClearEntityHighlight(Guid entityId)
    {
        try
        {
            if (GameState.Entities is not null && GameState.Entities.TryGetValue(entityId, out var entity))
            {
                entity.Highlight = 0f;
                entity.FlashColor = Vector4.Zero;
            }
        }
        catch
        {
            // Entity may have been removed by the authoritative server update.
        }
    }

    private static void HandleEditorInput(IModLogger? log, bool isMouseOverGui)
    {
        if (Keybinds.UiClose.Pressed() || Keybinds.Inventory.Pressed())
        {
            Keybinds.UiClose.PressConsumed = true;
            Keybinds.Inventory.PressConsumed = true;
            DisableWorldEditor(log);
            return;
        }

        if (isMouseOverGui)
        {
            return;
        }

        if (_mode == LiveEditorMode.Tiles)
        {
            HandleTileEditorInput(log);
            return;
        }

        if (HandleObjectStatHotkeys(log))
        {
            return;
        }

        if (Keybinds.UiPrimary.Pressed())
        {
            Keybinds.UiPrimary.PressConsumed = true;
            SelectTarget(log);
            return;
        }

        if (Keybinds.UiSecondary.Pressed())
        {
            Keybinds.UiSecondary.PressConsumed = true;
            DeleteTargetUnderCursor(log);
            return;
        }

        if (Keybinds.Interact.Pressed())
        {
            Keybinds.Interact.PressConsumed = true;
            CloneSelectedHere(log);
            return;
        }

        if (Keybinds.Place.Pressed())
        {
            Keybinds.Place.PressConsumed = true;
            MoveSelectedHere(log);
        }
    }

    private static void HandleTileEditorInput(IModLogger? log)
    {
        if (HandleTileBrushHotkeys(log))
        {
            return;
        }

        if (Keybinds.UiPrimary.Pressed())
        {
            Keybinds.UiPrimary.PressConsumed = true;
            SampleTileUnderCursor(log);
            return;
        }

        if (Keybinds.UiSecondary.Down())
        {
            Keybinds.UiSecondary.PressConsumed = true;
            PaintTileUnderCursor(TilePaintMode.FullTile, log);
            return;
        }

        if (Keybinds.Interact.Down())
        {
            Keybinds.Interact.PressConsumed = true;
            PaintTileUnderCursor(TilePaintMode.GroundOnly, log);
            return;
        }

        ResetContinuousPaintStateIfReleased();
    }

    private static bool HandleTileBrushHotkeys(IModLogger? log)
    {
        if (InputManager.Pressed(Keys.Delete))
        {
            ClearStructureUnderCursor(log);
            return true;
        }

        if (InputManager.Pressed(Keys.OemOpenBrackets))
        {
            _tileBrushRadius = Math.Max(1, _tileBrushRadius - 1);
            SetStatus($"Tile brush size: {_tileBrushRadius}.", log);
            return true;
        }

        if (InputManager.Pressed(Keys.OemCloseBrackets))
        {
            _tileBrushRadius = Math.Min(24, _tileBrushRadius + 1);
            SetStatus($"Tile brush size: {_tileBrushRadius}.", log);
            return true;
        }

        if (InputManager.Pressed(Keys.B))
        {
            _tileBrushShape = NextBrushShape(_tileBrushShape);
            SetStatus($"Tile brush shape: {_tileBrushShape}.", log);
            return true;
        }

        if (InputManager.Pressed(Keys.L))
        {
            if (!TryGetCachedTilePosition(out var tilePos, out var reason))
            {
                SetStatus($"Line anchor failed: {reason}", log);
                return true;
            }

            _lineAnchor = tilePos;
            _tileBrushShape = TileBrushShape.Line;
            SetStatus($"Line anchor set at {tilePos.X},{tilePos.Y}.", log);
            return true;
        }

        return false;
    }

    private static bool HandleObjectStatHotkeys(IModLogger? log)
    {
        if (InputManager.Pressed(Keys.H))
        {
            HealEntityTarget(log);
            return true;
        }

        if (InputManager.Pressed(Keys.PageUp))
        {
            NudgeEntityMaxHealth(EntityHealthStep, log);
            return true;
        }

        if (InputManager.Pressed(Keys.PageDown))
        {
            NudgeEntityMaxHealth(-EntityHealthStep, log);
            return true;
        }

        return false;
    }

    private static void UpdateHelpWindow(int desktopWidth)
    {
        if (!_active)
        {
            _helpWindow?.Close();
            _helpWindow = null;
            _helpWindowSnapshot = null;
            _paletteWindow?.Close();
            _paletteWindow = null;
            _paletteWindowSnapshot = null;
            return;
        }

        if (IsTilePaintHeld())
        {
            _helpWindow?.Close();
            _helpWindow = null;
            _paletteWindow?.Close();
            _paletteWindow = null;
            return;
        }

        if (_mode != LiveEditorMode.Tiles)
        {
            _paletteWindow?.Close();
            _paletteWindow = null;
            _paletteWindowSnapshot = null;
        }

        var cursor = _worldCursorPos.HasValue ? Format(_worldCursorPos.Value) : "move mouse over world";
        var mode = _mode == LiveEditorMode.Tiles ? "Tile" : "Object";
        var target = _mode == LiveEditorMode.Tiles
            ? FormatTileTarget()
            : _worldTarget?.Summary ?? _worldTargetReason ?? "none";
        var selected = _selected?.Summary ?? "none";
        var brush = FormatTileBrushPattern();
        var brushMode = $"{_tileBrushShape} size={_tileBrushRadius}" +
            (_lineAnchor.HasValue ? $" anchor={_lineAnchor.Value.X},{_lineAnchor.Value.Y}" : string.Empty);
        var status = _status ?? string.Empty;
        var entityStats = FormatEntityStatsForSnapshot();
        var snapshot = string.Join("|", mode, cursor, target, selected, brush, brushMode, entityStats, status);
        if (_helpWindow is { IsOpen: true } && string.Equals(_helpWindowSnapshot, snapshot, StringComparison.Ordinal))
        {
            UpdateTilePaletteWindow();
            return;
        }

        var controlRows = _mode == LiveEditorMode.Tiles
            ? BuildTileControlRows()
            : BuildObjectControlRows();

        var sections = new List<ModSection>
        {
            new ModSection
            {
                Title = "Controls",
                Rows = controlRows
            },
            new ModSection
            {
                Title = "Selection",
                Rows =
                {
                    new ModInfoRow { Label = "Mode", Value = mode },
                    new ModInfoRow { Label = "Cursor", Value = cursor },
                    new ModInfoRow { Label = "Target", Value = target },
                }
            }
        };

        if (_mode == LiveEditorMode.Tiles)
        {
            sections[1].Rows.Add(new ModInfoRow { Label = "Brush", Value = brush });
            sections[1].Rows.Add(new ModInfoRow { Label = "Shape", Value = brushMode });
        }
        else
        {
            sections[1].Rows.Add(new ModInfoRow { Label = "Selected", Value = selected });
            if (BuildEntityStatsSection() is { } statsSection)
            {
                sections.Add(statsSection);
            }
        }

        if (!string.IsNullOrWhiteSpace(_status))
        {
            sections[1].Rows.Add(new ModLabelRow { Text = _status, Style = ModUiTextStyle.BodyStrong });
        }

        var x = Math.Max(HelpWindowMargin, desktopWidth - HelpWindowWidth - HelpWindowMargin);
        _helpWindowSnapshot = snapshot;
        _helpWindow = ModRegistries.Windows.Open(new ModWindowDefinition
        {
            Id = HelpWindowId,
            Title = "Live Map Editor",
            Style = ModWindowStyle.Dark,
            Width = HelpWindowWidth,
            X = x,
            Y = EditorWindowY,
            ShowCloseButton = false,
            Sections = sections
        });

        UpdateTilePaletteWindow();
    }

    private static void UpdateTilePaletteWindow()
    {
        if (!_active || _mode != LiveEditorMode.Tiles)
        {
            _paletteWindow?.Close();
            _paletteWindow = null;
            _paletteWindowSnapshot = null;
            return;
        }

        if (IsTilePaintHeld())
        {
            _paletteWindow?.Close();
            _paletteWindow = null;
            return;
        }

        var snapshot = string.Join("|", "tile-palette", _tileBrushShape, _tileBrushRadius, FormatTileBrushPattern());
        if (_paletteWindow is { IsOpen: true } && string.Equals(_paletteWindowSnapshot, snapshot, StringComparison.Ordinal))
        {
            return;
        }

        _paletteWindowSnapshot = snapshot;
        _paletteWindow = ModRegistries.Windows.Open(new ModWindowDefinition
        {
            Id = PaletteWindowId,
            Title = "Tile Palette",
            Style = ModWindowStyle.Dark,
            Width = PaletteWindowWidth,
            X = 16,
            Y = EditorWindowY,
            ShowCloseButton = false,
            Sections =
            [
                new ModSection
                {
                    Title = "Selected Brush",
                    Rows =
                    {
                        new ModInfoRow { Label = "Brush", Value = FormatTileBrushPattern() },
                        new ModLabelRow { Text = "Pick a tile, then hold right click to paint.", Style = ModUiTextStyle.Body }
                    }
                },
                BuildRendererPaletteSection(),
                BuildGroundPaletteSection(),
                BuildStructurePaletteSection()
            ]
        });
    }

    private static ModSection BuildGroundPaletteSection()
    {
        var section = new ModSection { Title = "Ground Tiles" };
        section.Rows.AddRange(BuildPaletteRows(
            Enum.GetValues<WorldTile.GroundType>()
                .Where(ground => ground != WorldTile.GroundType.None)
                .Select(ground => new ModBarButton
                {
                    Label = ShortTileName(ground.ToString()),
                    Width = PaletteButtonWidth,
                    OnClick = _ => SelectPaletteGround(ground, ModLogger)
                })));
        return section;
    }

    private static ModSection BuildStructurePaletteSection()
    {
        var section = new ModSection { Title = "Structure Tiles" };
        section.Rows.AddRange(BuildPaletteRows(
            Enum.GetValues<WorldTile.StructureType>()
                .Select(structure => new ModBarButton
                {
                    Label = ShortTileName(structure.ToString()),
                    Width = PaletteButtonWidth,
                    OnClick = _ => SelectPaletteStructure(structure, ModLogger)
                })));
        return section;
    }

    private static ModSection BuildRendererPaletteSection() =>
        new()
        {
            Title = "Water/Lava",
            Rows =
            {
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "Water", Width = PaletteButtonWidth, OnClick = _ => SelectPaletteStructure(WorldTile.StructureType.Water, ModLogger) },
                        new ModBarButton { Label = "Swamp", Width = PaletteButtonWidth, OnClick = _ => SelectPaletteStructure(WorldTile.StructureType.SwampWater, ModLogger) }
                    ]
                },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "BasaltPit", Width = PaletteButtonWidth, OnClick = _ => SelectPaletteStructure(WorldTile.StructureType.BasaltPit, ModLogger) },
                        new ModBarButton { Label = "LavaFlow", Width = PaletteButtonWidth, OnClick = _ => SelectPaletteGround(WorldTile.GroundType.LavaFlow, ModLogger) }
                    ]
                }
            }
        };

    private static IEnumerable<ModUiRow> BuildPaletteRows(IEnumerable<ModBarButton> buttons)
    {
        var row = new List<ModBarButton>(PaletteColumns);
        foreach (var button in buttons)
        {
            row.Add(button);
            if (row.Count < PaletteColumns)
            {
                continue;
            }

            yield return new ModButtonBarRow { Buttons = row.ToArray() };
            row.Clear();
        }

        if (row.Count > 0)
        {
            yield return new ModButtonBarRow { Buttons = row.ToArray() };
        }
    }

    private static bool IsTilePaintHeld() =>
        _active &&
        _mode == LiveEditorMode.Tiles &&
        (Keybinds.UiSecondary.Down() || Keybinds.Interact.Down());

    private static string ShortTileName(string name)
    {
        var compact = name
            .Replace("Ground", "G", StringComparison.Ordinal)
            .Replace("Structure", "S", StringComparison.Ordinal)
            .Replace("Dungeon", "Dng", StringComparison.Ordinal)
            .Replace("Volcano", "Volc", StringComparison.Ordinal)
            .Replace("Desert", "Des", StringComparison.Ordinal)
            .Replace("GhostTown", "Ghost", StringComparison.Ordinal)
            .Replace("Construction", "Constr", StringComparison.Ordinal)
            .Replace("Concrete", "Conc", StringComparison.Ordinal)
            .Replace("CobbleStone", "Cobble", StringComparison.Ordinal)
            .Replace("Farmland", "Farm", StringComparison.Ordinal);

        const int maxLength = 10;
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static List<ModUiRow> BuildObjectControlRows() =>
    [
        new ModLabelRow { Text = "F8: toggle object editor", Style = ModUiTextStyle.BodyStrong },
        new ModLabelRow { Text = "F9: switch to tile editor", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Left click: select target", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Right click: delete target", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Interact: clone selected here", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Place: move selected here", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "H: heal, PgUp/PgDn: max HP", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Tab/Esc: exit editor", Style = ModUiTextStyle.Body },
    ];

    private static List<ModUiRow> BuildTileControlRows() =>
    [
        new ModLabelRow { Text = "F9: toggle tile editor", Style = ModUiTextStyle.BodyStrong },
        new ModLabelRow { Text = "F8: switch to object editor", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Left click: sample brush area", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Hold right click: paint copied area", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Hold Interact: paint copied ground", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Delete: clear structure on tile", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "B: brush shape, [ ]: size", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "L: set line anchor", Style = ModUiTextStyle.Body },
        new ModLabelRow { Text = "Tab/Esc: exit editor", Style = ModUiTextStyle.Body },
    ];

    private static ModSection? BuildEntityStatsSection()
    {
        if (!TryResolveEntityStatTarget(out var entity, out var label, out _))
        {
            return null;
        }

        var controllerName = entity.Controller?.GetType().Name ?? "none";
        var defaultHealth = LiveEntityDefinitionOverrides.TryGetMaxHealth(entity.BaseGuid, out var defaultMaxHealth)
            ? $"{defaultMaxHealth:0.#} max HP"
            : "vanilla";
        return new ModSection
        {
            Title = "Entity Stats",
            Rows =
            {
                new ModInfoRow { Label = "Editing", Value = label },
                new ModInfoRow { Label = "Health", Value = $"{SafeGetHealth(entity):0.#} / {SafeGetMaxHealth(entity):0.#}" },
                new ModInfoRow { Label = "Spawn default", Value = defaultHealth },
                new ModInfoRow { Label = "Controller", Value = controllerName },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "Heal", Width = 82, OnClick = _ => HealEntityTarget(ModLogger) },
                        new ModBarButton { Label = "+25 HP", Width = 82, OnClick = _ => NudgeEntityHealth(EntityHealthStep, ModLogger) }
                    ]
                },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "-25 HP", Width = 82, OnClick = _ => NudgeEntityHealth(-EntityHealthStep, ModLogger) },
                        new ModBarButton { Label = "1 HP", Width = 82, OnClick = _ => SetEntityHealthToOne(ModLogger) }
                    ]
                },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "Max +25", Width = 82, OnClick = _ => NudgeEntityMaxHealth(EntityHealthStep, ModLogger) },
                        new ModBarButton { Label = "Max -25", Width = 82, OnClick = _ => NudgeEntityMaxHealth(-EntityHealthStep, ModLogger) }
                    ]
                },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "Max x2", Width = 82, OnClick = _ => ScaleEntityMaxHealth(2f, ModLogger) },
                        new ModBarButton { Label = "Max /2", Width = 82, OnClick = _ => ScaleEntityMaxHealth(0.5f, ModLogger) }
                    ]
                },
                new ModButtonBarRow
                {
                    Buttons =
                    [
                        new ModBarButton { Label = "Save Default", Width = 112, OnClick = _ => SaveEntityHealthDefault(ModLogger) },
                        new ModBarButton { Label = "Clear Default", Width = 112, OnClick = _ => ClearEntityHealthDefault(ModLogger) }
                    ]
                }
            }
        };
    }

    private static string FormatEntityStatsForSnapshot()
    {
        if (!TryResolveEntityStatTarget(out var entity, out var label, out _))
        {
            return "entity:none";
        }

        var defaultHealth = LiveEntityDefinitionOverrides.TryGetMaxHealth(entity.BaseGuid, out var defaultMaxHealth)
            ? defaultMaxHealth.ToString("0.###")
            : "vanilla";
        return $"entity:{label}:{SafeGetHealth(entity):0.###}:{SafeGetMaxHealth(entity):0.###}:{defaultHealth}";
    }

    internal static ModSection BuildSection(IModLogger? log)
    {
        EnsureCheatsEnabled(log);

        var currentWorld = GameState.CurrentWorld;
        var cursorText = _worldCursorPos.HasValue
            ? $"{Format(_worldCursorPos.Value)} world={_worldCursorWorldId}"
            : "(move mouse over the world)";
        var hoverText = _worldTarget?.Summary ?? _worldTargetReason ?? "no cached world target.";

        var rows = new List<ModUiRow>
        {
            new ModLabelRow
            {
                Text = "Move the mouse over the world first, then click these buttons. The editor keeps the last world cursor/target while the pointer is over this menu.",
                Style = ModUiTextStyle.Body
            },
            new ModInfoRow
            {
                Label = "Current world",
                Value = currentWorld?.MapName ?? currentWorld?.Name ?? "(unknown)"
            },
            new ModInfoRow
            {
                Label = "World cursor",
                Value = cursorText
            },
            new ModInfoRow
            {
                Label = "World target",
                Value = hoverText
            },
            new ModInfoRow
            {
                Label = "Selected",
                Value = _selected?.Summary ?? "(none)"
            },
            new ModButtonBarRow
            {
                Buttons =
                [
                    new ModBarButton { Label = "Select World Target", OnClick = _ => SelectTarget(log) },
                    new ModBarButton { Label = "Delete World Target", OnClick = _ => DeleteTargetUnderCursor(log) },
                    new ModBarButton { Label = "Clone Selected To Cursor", OnClick = _ => CloneSelectedHere(log) },
                    new ModBarButton { Label = "Move Selected To Cursor", OnClick = _ => MoveSelectedHere(log) }
                ]
            },
            new ModTextInputRow
            {
                Label = "Construction Id",
                Value = _constructionId ?? string.Empty,
                Placeholder = "e.g. workbench:0",
                MaxLength = 200,
                OnChanged = (_, text) => _constructionId = text
            },
            new ModButtonBarRow
            {
                Buttons =
                [
                    new ModBarButton { Label = "Spawn Construction", OnClick = _ => SpawnConstructionAtCursor(log) },
                    new ModBarButton { Label = "Use Selected Construction", OnClick = _ => CopySelectedConstruction(log) }
                ]
            },
            new ModTextInputRow
            {
                Label = "Entity Base Guid",
                Value = _entityBaseGuid ?? string.Empty,
                Placeholder = "e.g. e293bda6-4b78-4cb8-8287-d91b6a9129dc",
                MaxLength = 64,
                OnChanged = (_, text) => _entityBaseGuid = text
            },
            new ModButtonBarRow
            {
                Buttons =
                [
                    new ModBarButton { Label = "Spawn Entity", OnClick = _ => SpawnEntityAtCursor(log) },
                    new ModBarButton { Label = "Use Selected Entity", OnClick = _ => CopySelectedEntity(log) }
                ]
            }
        };

        if (!string.IsNullOrWhiteSpace(_status))
        {
            rows.Add(new ModLabelRow { Text = _status, Style = ModUiTextStyle.BodyStrong });
        }

        return new ModSection
        {
            Title = "Live Map Editor",
            Rows = rows
        };
    }

    private static void SampleTileUnderCursor(IModLogger? log)
    {
        if (!TryGetCachedTilePosition(out var tilePos, out var reason))
        {
            SetStatus($"Sample failed: {reason}", log);
            return;
        }

        var cells = new List<TileBrushCell>();
        foreach (var samplePos in GetBrushTilePositions(tilePos))
        {
            if (TryGetClientTile(samplePos, out var tile))
            {
                cells.Add(new TileBrushCell(
                    new Point(samplePos.X - tilePos.X, samplePos.Y - tilePos.Y),
                    tile.Ground,
                    tile.Structure));
            }
        }

        if (cells.Count == 0)
        {
            SetStatus("Sample failed: brush area outside loaded world grid.", log);
            return;
        }

        _tileBrush = cells;
        ResetContinuousPaintState();
        SetStatus($"Sampled {cells.Count} tile(s) from {FormatBounds(GetBrushBounds(tilePos))}.", log);
        log?.Info($"[map-editor] sampled {cells.Count} tile(s) from {FormatBounds(GetBrushBounds(tilePos))}.");
    }

    private static void SelectPaletteGround(WorldTile.GroundType ground, IModLogger? log)
    {
        _tileBrush =
        [
            new TileBrushCell(Point.Zero, ground, WorldTile.StructureType.None)
        ];
        ResetContinuousPaintState();
        SetStatus($"Palette selected ground {ground}.", log);
    }

    private static void SelectPaletteStructure(WorldTile.StructureType structure, IModLogger? log)
    {
        _tileBrush =
        [
            new TileBrushCell(Point.Zero, null, structure)
        ];
        ResetContinuousPaintState();
        SetStatus($"Palette selected structure {structure}.", log);
    }

    private static void PaintTileUnderCursor(TilePaintMode paintMode, IModLogger? log)
    {
        if (_tileBrush is not { Count: > 0 } brush)
        {
            SetStatus("Paint failed: sample a brush area first with left click.", log);
            return;
        }

        if (!TryGetCachedTilePosition(out var tilePos, out var reason))
        {
            SetStatus($"Paint failed: {reason}", log);
            return;
        }

        if (!ShouldApplyContinuousPaint(tilePos, paintMode))
        {
            return;
        }

        var edits = BuildTilePatternEdits(tilePos, brush, paintMode).ToArray();
        foreach (var edit in edits)
        {
            ApplyTileEdit(edit.Bounds, edit.Ground, edit.Structure, log);
        }

        SetStatus($"Painted {paintMode} {brush.Count} tile(s) into {FormatBounds(GetPatternBounds(tilePos, brush))}.", log);
    }

    private static void ClearStructureUnderCursor(IModLogger? log)
    {
        if (!TryGetCachedTilePosition(out var tilePos, out var reason))
        {
            SetStatus($"Clear failed: {reason}", log);
            return;
        }

        var bounds = GetBrushBounds(tilePos);
        foreach (var span in GetBrushPreviewSpans(tilePos))
        {
            ApplyTileEdit(span, ground: null, structure: WorldTile.StructureType.None, log);
        }

        SetStatus($"Cleared structure {FormatBounds(bounds)}.", log);
    }

    private static bool ShouldApplyContinuousPaint(Point tilePos, TilePaintMode paintMode)
    {
        if (_lastPaintTile.HasValue && _lastPaintTile.Value == tilePos && _lastPaintMode == paintMode)
        {
            return false;
        }

        _lastPaintTile = tilePos;
        _lastPaintMode = paintMode;
        return true;
    }

    private static void ResetContinuousPaintStateIfReleased()
    {
        if (!Keybinds.UiSecondary.Down() && !Keybinds.Interact.Down())
        {
            ResetContinuousPaintState();
        }
    }

    private static void ResetContinuousPaintState()
    {
        _lastPaintTile = null;
        _lastPaintMode = null;
    }

    private static void ApplyTileEdit(
        Rectangle tileBounds,
        WorldTile.GroundType? ground,
        WorldTile.StructureType? structure,
        IModLogger? log)
    {
        var message = new WorldTilesFillRectangleMessage
        {
            TileBounds = tileBounds,
            Ground = ground,
            Structure = structure
        };

        WorldTileService.OnReceive_WorldTilesFillRectangle(message);
        WorldTileService.SendFillRectangle(tileBounds, ground, structure);
        ForceChunkRemesh(tileBounds);
        TryAppendRendererZone(tileBounds, ground, structure, log);
        log?.Info($"[map-editor] tile edit sent bounds={tileBounds} ground={ground?.ToString() ?? "(keep)"} structure={structure?.ToString() ?? "(keep)"}.");
    }

    private static void TryAppendRendererZone(
        Rectangle tileBounds,
        WorldTile.GroundType? ground,
        WorldTile.StructureType? structure,
        IModLogger? log)
    {
        if (!TryGetRendererZoneStyle(ground, structure, out var style))
        {
            return;
        }

        if (CreateZoneMeshMethod is null || ExteriorWorldHandler.World is not { } world)
        {
            return;
        }

        try
        {
            foreach (var chunk in world.GetLoadedChunksForTileRectangle(tileBounds))
            {
                var chunkBounds = world.GetTileRectangleForChunk(chunk);
                var clipped = Rectangle.Intersect(tileBounds, chunkBounds);
                if (clipped.Width <= 0 || clipped.Height <= 0)
                {
                    continue;
                }

                chunk.WaterZones ??= [];
                var found = new HashSet<Point>();
                for (var y = clipped.Top; y < clipped.Bottom; y++)
                {
                    for (var x = clipped.Left; x < clipped.Right; x++)
                    {
                        found.Add(new Point(x, y));
                    }
                }

                CreateZoneMeshMethod.Invoke(
                    null,
                    [chunk, style.Color, style.ZoneType, style.Height, found, (int)DefaultTileSize]);
            }

            log?.Info($"[map-editor] appended {style.ZoneType} renderer zone for {FormatBounds(tileBounds)}.");
        }
        catch (Exception ex)
        {
            log?.Warn($"[map-editor] renderer zone append failed: {ex.Message}");
        }
    }

    private static bool TryGetRendererZoneStyle(
        WorldTile.GroundType? ground,
        WorldTile.StructureType? structure,
        out RendererZoneStyle style)
    {
        if (structure == WorldTile.StructureType.Water)
        {
            style = new RendererZoneStyle(new Vector4(0.1f, 0.4f, 0.8f, 0.3f), WaterZone.WaterZoneType.Water, -3f);
            return true;
        }

        if (structure == WorldTile.StructureType.SwampWater)
        {
            style = new RendererZoneStyle(new Vector4(0f, 0.3f, 0.3f, 0.3f), WaterZone.WaterZoneType.Water, -3f);
            return true;
        }

        if (structure == WorldTile.StructureType.BasaltPit)
        {
            style = new RendererZoneStyle(new Vector4(1f, 0.75f, 0.1f, 0f), WaterZone.WaterZoneType.Lava, -32f);
            return true;
        }

        if (ground == WorldTile.GroundType.LavaFlow)
        {
            style = new RendererZoneStyle(new Vector4(1f, 0.75f, 0.1f, 0f), WaterZone.WaterZoneType.LavaFlow, 0f);
            return true;
        }

        style = default;
        return false;
    }

    private static void ForceChunkRemesh(Rectangle tileBounds)
    {
        try
        {
            var expanded = new Rectangle(
                Math.Max(0, tileBounds.X - 2),
                Math.Max(0, tileBounds.Y - 2),
                tileBounds.Width + 4,
                tileBounds.Height + 4);

            ExteriorWorldHandler.UpdateChunkRenders(expanded, needsToRemesh: true, renderOnlyTheChangedTiles: false);
        }
        catch
        {
            // The service receive path already updates tiles; this is only to refresh cached terrain meshes.
        }
    }

    private static bool TryGetCachedTilePosition(out Point tilePos, out string reason)
    {
        if (_worldTileCursor.HasValue)
        {
            tilePos = _worldTileCursor.Value;
            reason = string.Empty;
            return true;
        }

        tilePos = Point.Zero;
        reason = _worldTileReason ?? "move the mouse over the world first.";
        return false;
    }

    private static bool TryGetClientTile(Point tilePos, out WorldTile tile)
    {
        try
        {
            var tiles = ClientWorldTileHelper.GetTiles();
            if (tiles is null ||
                tilePos.X < 0 ||
                tilePos.Y < 0 ||
                tilePos.X >= tiles.GetLength(0) ||
                tilePos.Y >= tiles.GetLength(1))
            {
                tile = default;
                return false;
            }

            tile = tiles[tilePos.X, tilePos.Y];
            return true;
        }
        catch
        {
            tile = default;
            return false;
        }
    }

    private static Point WorldPosToTile(Vector3 worldPos)
    {
        var tileSize = GetTileSize();
        return new Point(
            (int)MathF.Floor(worldPos.X / tileSize),
            (int)MathF.Floor(worldPos.Y / tileSize));
    }

    private static TileBrushShape NextBrushShape(TileBrushShape shape) => shape switch
    {
        TileBrushShape.Single => TileBrushShape.Square,
        TileBrushShape.Square => TileBrushShape.Circle,
        TileBrushShape.Circle => TileBrushShape.Line,
        _ => TileBrushShape.Single
    };

    private static IEnumerable<Rectangle> GetBrushPreviewSpans(Point center) => _tileBrushShape switch
    {
        TileBrushShape.Single => [new Rectangle(center.X, center.Y, 1, 1)],
        TileBrushShape.Square => [GetSquareBrushBounds(center)],
        TileBrushShape.Circle => GetCircleBrushSpans(center),
        TileBrushShape.Line => GetLineBrushSpans(_lineAnchor ?? center, center),
        _ => [new Rectangle(center.X, center.Y, 1, 1)]
    };

    private static Rectangle GetBrushBounds(Point center)
    {
        var spans = GetBrushPreviewSpans(center).ToArray();
        var left = spans.Min(span => span.Left);
        var top = spans.Min(span => span.Top);
        var right = spans.Max(span => span.Right);
        var bottom = spans.Max(span => span.Bottom);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static IEnumerable<Point> GetBrushTilePositions(Point center)
    {
        var seen = new HashSet<Point>();
        foreach (var span in GetBrushPreviewSpans(center))
        {
            for (var y = span.Top; y < span.Bottom; y++)
            {
                for (var x = span.Left; x < span.Right; x++)
                {
                    var point = new Point(x, y);
                    if (seen.Add(point))
                    {
                        yield return point;
                    }
                }
            }
        }
    }

    private static IEnumerable<TileEdit> BuildTilePatternEdits(Point center, List<TileBrushCell> brush, TilePaintMode paintMode)
    {
        var cells = brush
            .Select(cell => new TilePaintCell(
                new Point(center.X + cell.Offset.X, center.Y + cell.Offset.Y),
                cell.Ground,
                paintMode == TilePaintMode.GroundOnly ? null : cell.Structure))
            .GroupBy(cell => new TilePaintRunKey(cell.Position.Y, cell.Ground, cell.Structure));

        foreach (var group in cells)
        {
            var ordered = group.Select(cell => cell.Position.X).OrderBy(x => x).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var spanStart = ordered[0];
            var previous = ordered[0];
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i] == previous + 1)
                {
                    previous = ordered[i];
                    continue;
                }

                yield return new TileEdit(
                    new Rectangle(spanStart, group.Key.Y, previous - spanStart + 1, 1),
                    group.Key.Ground,
                    group.Key.Structure);
                spanStart = previous = ordered[i];
            }

            yield return new TileEdit(
                new Rectangle(spanStart, group.Key.Y, previous - spanStart + 1, 1),
                group.Key.Ground,
                group.Key.Structure);
        }
    }

    private static Rectangle GetPatternBounds(Point center, List<TileBrushCell> brush)
    {
        var left = brush.Min(cell => center.X + cell.Offset.X);
        var top = brush.Min(cell => center.Y + cell.Offset.Y);
        var right = brush.Max(cell => center.X + cell.Offset.X) + 1;
        var bottom = brush.Max(cell => center.Y + cell.Offset.Y) + 1;
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static Rectangle GetSquareBrushBounds(Point center)
    {
        var radius = Math.Max(1, _tileBrushRadius);
        var diameter = radius * 2 - 1;
        return new Rectangle(center.X - radius + 1, center.Y - radius + 1, diameter, diameter);
    }

    private static IEnumerable<Rectangle> GetCircleBrushSpans(Point center)
    {
        var radius = Math.Max(1, _tileBrushRadius);
        var radiusSquared = radius * radius;
        for (var dy = -radius + 1; dy <= radius - 1; dy++)
        {
            var halfWidth = 0;
            for (var dx = 0; dx <= radius - 1; dx++)
            {
                if (dx * dx + dy * dy <= radiusSquared)
                {
                    halfWidth = dx;
                }
            }

            yield return new Rectangle(center.X - halfWidth, center.Y + dy, halfWidth * 2 + 1, 1);
        }
    }

    private static IEnumerable<Rectangle> GetLineBrushSpans(Point start, Point end)
    {
        var points = new HashSet<Point>();
        var x0 = start.X;
        var y0 = start.Y;
        var x1 = end.X;
        var y1 = end.Y;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            AddBrushStamp(points, new Point(x0, y0));
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = error * 2;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }

        foreach (var group in points.GroupBy(point => point.Y).OrderBy(group => group.Key))
        {
            var ordered = group.Select(point => point.X).OrderBy(x => x).ToArray();
            var spanStart = ordered[0];
            var previous = ordered[0];
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i] == previous + 1)
                {
                    previous = ordered[i];
                    continue;
                }

                yield return new Rectangle(spanStart, group.Key, previous - spanStart + 1, 1);
                spanStart = previous = ordered[i];
            }

            yield return new Rectangle(spanStart, group.Key, previous - spanStart + 1, 1);
        }
    }

    private static void AddBrushStamp(HashSet<Point> points, Point center)
    {
        if (_tileBrushRadius <= 1)
        {
            points.Add(center);
            return;
        }

        foreach (var span in GetCircleBrushSpans(center))
        {
            for (var x = span.Left; x < span.Right; x++)
            {
                points.Add(new Point(x, span.Y));
            }
        }
    }

    private static string FormatTileTarget()
    {
        if (!_worldTileCursor.HasValue)
        {
            return _worldTileReason ?? "none";
        }

        var tilePos = _worldTileCursor.Value;
        if (!TryGetClientTile(tilePos, out var tile))
        {
            return $"{tilePos.X},{tilePos.Y}: {_worldTileReason ?? "outside loaded world grid"}";
        }

        return $"{tilePos.X},{tilePos.Y}: {FormatTile(tile)}";
    }

    private static string FormatTile(WorldTile tile) => $"ground={tile.Ground} structure={tile.Structure}";

    private static string FormatTileBrushPattern()
    {
        if (_tileBrush is not { Count: > 0 } brush)
        {
            return "sample a brush area first";
        }

        return brush.Count == 1
            ? $"1 tile: {FormatTileBrush(brush[0])}"
            : $"{brush.Count} copied tile(s)";
    }

    private static string FormatTileBrush(TileBrushCell cell) =>
        $"ground={cell.Ground?.ToString() ?? "(keep)"} structure={cell.Structure?.ToString() ?? "(keep)"}";

    private static string FormatBounds(Rectangle bounds) =>
        bounds.Width == 1 && bounds.Height == 1
            ? $"at {bounds.X},{bounds.Y}"
            : $"bounds {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}";

    private static void SelectTarget(IModLogger? log)
    {
        if (!TryGetCachedTarget(out var selection, out var reason) || selection is null)
        {
            SetStatus($"Select failed: {reason}", log);
            return;
        }

        _selected = selection;
        if (!string.IsNullOrWhiteSpace(selection.ConstructionId))
        {
            _constructionId = selection.ConstructionId;
        }

        if (selection.BaseGuid.HasValue)
        {
            _entityBaseGuid = selection.BaseGuid.Value.ToString();
        }

        SetStatus($"Selected {selection.Summary}", log);
    }

    private static void DeleteTargetUnderCursor(IModLogger? log)
    {
        if (!TryGetCachedTarget(out var selection, out var reason) || selection is null)
        {
            SetStatus($"Delete failed: {reason}", log);
            return;
        }

        DeleteSelection(selection, log);
    }

    private static void CloneSelectedHere(IModLogger? log)
    {
        if (_selected is null)
        {
            SetStatus("Clone failed: no selected target.", log);
            return;
        }

        if (!TryGetCachedCursorPosition(out var cursorPos, out var reason))
        {
            SetStatus($"Clone failed: {reason}", log);
            return;
        }

        if (!CloneSelectionAt(_selected, cursorPos, log))
        {
            return;
        }

        SetStatus($"Clone requested for {_selected.Summary} at {Format(cursorPos)}.", log);
    }

    private static void MoveSelectedHere(IModLogger? log)
    {
        if (_selected is null)
        {
            SetStatus("Move failed: no selected target.", log);
            return;
        }

        if (!TryGetCachedCursorPosition(out var cursorPos, out var reason))
        {
            SetStatus($"Move failed: {reason}", log);
            return;
        }

        var movedSelection = _selected;
        if (!CloneSelectionAt(movedSelection, cursorPos, log))
        {
            return;
        }

        DeleteSelection(movedSelection, log);
        SetStatus($"Move requested for {movedSelection.Summary} to {Format(cursorPos)}.", log);
    }

    private static void SpawnConstructionAtCursor(IModLogger? log)
    {
        if (string.IsNullOrWhiteSpace(_constructionId))
        {
            SetStatus("Spawn failed: construction id is empty.", log);
            return;
        }

        if (!TryGetCachedCursorPosition(out var cursorPos, out var reason))
        {
            SetStatus($"Spawn failed: {reason}", log);
            return;
        }

        var townId = ResolveTownForConstruction(cursorPos, preferredTownId: _selected?.TownId);
        ConstructionsService.SendCreateConstructionCheat(_constructionId, ToVector2(cursorPos), townId);
        SetStatus($"Construction spawn requested: '{_constructionId}' at {Format(cursorPos)}.", log);
        log?.Info($"[map-editor] spawn construction '{_constructionId}' at {Format(cursorPos)} town={townId}.");
    }

    private static void CopySelectedConstruction(IModLogger? log)
    {
        if (_selected?.ConstructionId is not { Length: > 0 } constructionId)
        {
            SetStatus("No selected construction to copy.", log);
            return;
        }

        _constructionId = constructionId;
        SetStatus($"Copied construction id '{constructionId}'.", log);
    }

    private static void SpawnEntityAtCursor(IModLogger? log)
    {
        if (!Guid.TryParse(_entityBaseGuid, out var baseGuid))
        {
            SetStatus("Spawn failed: entity base guid is invalid.", log);
            return;
        }

        if (!TryGetCachedCursorPosition(out var cursorPos, out var reason))
        {
            SetStatus($"Spawn failed: {reason}", log);
            return;
        }

        var worldId = _worldCursorWorldId ?? GameState.CurrentWorld?.Id ?? Guid.Empty;
        if (worldId == Guid.Empty)
        {
            SetStatus("Spawn failed: current world is unavailable.", log);
            return;
        }

        EntityService.SendRequestSpawnEntity(new RequestSpawnEntityMessage
        {
            EntityBaseId = baseGuid,
            Position = cursorPos,
            Velocity = Vector3.Zero,
            Direction = 0f,
            WorldId = worldId,
            Parameters = new Dictionary<string, string>()
        });

        SetStatus($"Entity spawn requested: '{baseGuid}' at {Format(cursorPos)}.", log);
        log?.Info($"[map-editor] spawn entity base={baseGuid} world={worldId} pos={Format(cursorPos)}.");
    }

    private static void CopySelectedEntity(IModLogger? log)
    {
        if (_selected?.BaseGuid is not { } baseGuid)
        {
            SetStatus("No selected entity base guid to copy.", log);
            return;
        }

        _entityBaseGuid = baseGuid.ToString();
        SetStatus($"Copied entity base guid '{baseGuid}'.", log);
    }

    private static void HealEntityTarget(IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Heal failed: {reason}", log);
            return;
        }

        ApplyEntityHealth(entity.Id, SafeGetMaxHealth(entity), SafeGetMaxHealth(entity), log);
    }

    private static void SetEntityHealthToOne(IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Set HP failed: {reason}", log);
            return;
        }

        ApplyEntityHealth(entity.Id, 1f, SafeGetMaxHealth(entity), log);
    }

    private static void NudgeEntityHealth(float delta, IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Set HP failed: {reason}", log);
            return;
        }

        var maxHealth = SafeGetMaxHealth(entity);
        var health = Math.Clamp(SafeGetHealth(entity) + delta, 0f, maxHealth);
        ApplyEntityHealth(entity.Id, health, maxHealth, log);
    }

    private static void NudgeEntityMaxHealth(float delta, IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Set max HP failed: {reason}", log);
            return;
        }

        var oldMax = SafeGetMaxHealth(entity);
        var newMax = Math.Max(1f, oldMax + delta);
        var health = SafeGetHealth(entity);
        if (health >= oldMax - 0.01f && delta > 0f)
        {
            health = newMax;
        }

        ApplyEntityHealth(entity.Id, Math.Min(health, newMax), newMax, log);
    }

    private static void ScaleEntityMaxHealth(float scale, IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Set max HP failed: {reason}", log);
            return;
        }

        var oldMax = SafeGetMaxHealth(entity);
        var newMax = Math.Max(1f, oldMax * scale);
        var health = SafeGetHealth(entity);
        if (health >= oldMax - 0.01f && scale > 1f)
        {
            health = newMax;
        }

        ApplyEntityHealth(entity.Id, Math.Min(health, newMax), newMax, log);
    }

    private static void ApplyEntityHealth(Guid entityId, float health, float maxHealth, IModLogger? log)
    {
        maxHealth = Math.Max(1f, maxHealth);
        health = Math.Clamp(health, 0f, maxHealth);
        var clientUpdated = false;
        var serverUpdated = false;

        try
        {
            if (GameState.Entities is not null && GameState.Entities.TryGetValue(entityId, out var entity))
            {
                entity.MaxHealth = maxHealth;
                entity.Health = health;
                clientUpdated = true;
            }
        }
        catch (Exception ex)
        {
            log?.Info($"[map-editor] client health update failed for {entityId}: {ex.Message}");
        }

        try
        {
            if (ServerGameState.Entities is not null && ServerGameState.Entities.TryGetValue(entityId, out var serverEntity))
            {
                serverEntity.MaxHealth = maxHealth;
                serverEntity.Health = health;
                if (serverEntity.EntityWrapper is { } wrapper)
                {
                    wrapper.MaxHealth = maxHealth;
                    wrapper.Health = health;
                }

                EntityServerService.SendUpdateEntityHealth(serverEntity);
                serverUpdated = true;
            }
        }
        catch (Exception ex)
        {
            log?.Info($"[map-editor] server health update failed for {entityId}: {ex.Message}");
        }

        var scope = serverUpdated ? "client/server" : clientUpdated ? "client only" : "not found";
        SetStatus($"Set entity HP to {health:0.#}/{maxHealth:0.#} ({scope}).", log);
    }

    private static void SaveEntityHealthDefault(IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Save default failed: {reason}", log);
            return;
        }

        var maxHealth = SafeGetMaxHealth(entity);
        LiveEntityDefinitionOverrides.SetMaxHealth(entity.BaseGuid, maxHealth, log);
        SetStatus($"Saved spawn default for base {ShortGuid(entity.BaseGuid)}: {maxHealth:0.#} max HP.", log);
    }

    private static void ClearEntityHealthDefault(IModLogger? log)
    {
        if (!TryResolveEntityStatTarget(out var entity, out _, out var reason))
        {
            SetStatus($"Clear default failed: {reason}", log);
            return;
        }

        if (LiveEntityDefinitionOverrides.Clear(entity.BaseGuid, log))
        {
            SetStatus($"Cleared spawn default for base {ShortGuid(entity.BaseGuid)}.", log);
            return;
        }

        SetStatus($"No saved spawn default for base {ShortGuid(entity.BaseGuid)}.", log);
    }

    private static bool TryResolveEntityStatTarget(out EntityWrapper entity, out string label, out string reason)
    {
        if (TryResolveEntitySelection(_worldTarget, out entity))
        {
            label = $"hover {ShortGuid(entity.Id)}";
            reason = string.Empty;
            return true;
        }

        if (TryResolveEntitySelection(_selected, out entity))
        {
            label = $"selected {ShortGuid(entity.Id)}";
            reason = string.Empty;
            return true;
        }

        entity = null!;
        label = string.Empty;
        reason = "hover or select an entity first.";
        return false;
    }

    private static bool TryResolveEntitySelection(EditorSelection? selection, out EntityWrapper entity)
    {
        entity = null!;
        if (selection is not { Kind: EditorSelectionKind.Entity, EntityId: { } entityId } ||
            GameState.Entities is null ||
            !GameState.Entities.TryGetValue(entityId, out var resolved))
        {
            return false;
        }

        entity = resolved;
        return true;
    }

    private static float SafeGetHealth(EntityWrapper entity)
    {
        try
        {
            return entity.Health;
        }
        catch
        {
            return 0f;
        }
    }

    private static float SafeGetMaxHealth(EntityWrapper entity)
    {
        try
        {
            return Math.Max(1f, entity.MaxHealth);
        }
        catch
        {
            return 1f;
        }
    }

    private static string ShortGuid(Guid id)
    {
        var text = id.ToString("N");
        return text.Length <= 8 ? text : text[..8];
    }

    private static bool CloneSelectionAt(EditorSelection selection, Vector3 cursorPos, IModLogger? log)
    {
        if (!string.IsNullOrWhiteSpace(selection.ConstructionId))
        {
            var townId = ResolveTownForConstruction(cursorPos, selection.TownId);
            ConstructionsService.SendCreateConstructionCheat(selection.ConstructionId, ToVector2(cursorPos), townId);
            log?.Info($"[map-editor] clone construction '{selection.ConstructionId}' from {selection.Kind} at {Format(cursorPos)} town={townId}.");
            return true;
        }

        if (!selection.BaseGuid.HasValue)
        {
            SetStatus("Clone failed: selected target has no construction id or entity base guid.", log);
            return false;
        }

        var worldId = GameState.CurrentWorld?.Id ?? Guid.Empty;
        if (worldId == Guid.Empty)
        {
            SetStatus("Clone failed: current world is unavailable.", log);
            return false;
        }

        EntityService.SendRequestSpawnEntity(new RequestSpawnEntityMessage
        {
            EntityBaseId = selection.BaseGuid.Value,
            Position = cursorPos,
            Velocity = Vector3.Zero,
            Direction = 0f,
            WorldId = worldId,
            Parameters = new Dictionary<string, string>()
        });

        log?.Info($"[map-editor] clone entity base={selection.BaseGuid} from {selection.Kind} at {Format(cursorPos)}.");
        return true;
    }

    private static void DeleteSelection(EditorSelection selection, IModLogger? log)
    {
        switch (selection.Kind)
        {
            case EditorSelectionKind.Building when selection.BuildingId.HasValue:
                if (!BuildingsManager.TryRemoveBuilding(selection.BuildingId.Value))
                {
                    SetStatus($"Building removal failed: {selection.Summary}.", log);
                    return;
                }

                RemoveBuildingLocally(selection.BuildingId.Value);
                ClearDeletedSelection(selection);
                SetStatus($"Building removed: {selection.Summary}.", log);
                log?.Info($"[map-editor] remove building {selection.BuildingId.Value} ({selection.ConstructionId}).");
                return;

            case EditorSelectionKind.Decoration when selection.DecorationId.HasValue:
                if (!DecorationsManager.TryRemoveDecoration(selection.DecorationId.Value))
                {
                    SetStatus($"Decoration removal failed: {selection.Summary}.", log);
                    return;
                }

                RemoveDecorationLocally(selection.DecorationId.Value);
                ClearDeletedSelection(selection);
                SetStatus($"Decoration removed: {selection.Summary}.", log);
                log?.Info($"[map-editor] remove decoration {selection.DecorationId.Value} ({selection.ConstructionId}).");
                return;

            case EditorSelectionKind.Entity when selection.EntityId.HasValue:
                EntityService.SendRequestRemoveEntitySilent(selection.EntityId.Value);
                RemoveEntityLocally(selection.EntityId.Value);
                ClearDeletedSelection(selection);
                SetStatus($"Entity removed: {selection.Summary}.", log);
                log?.Info($"[map-editor] remove entity {selection.EntityId.Value} base={selection.BaseGuid}.");
                return;

            default:
                SetStatus("Delete failed: target selection is incomplete.", log);
                return;
        }
    }

    private static void RemoveBuildingLocally(Guid buildingId)
    {
        if (GameState.Buildings is null)
        {
            return;
        }

        if (GameState.Buildings.TryGetValue(buildingId, out var building) &&
            building.Model.TownId.HasValue &&
            GameState.Towns is not null &&
            GameState.Towns.TryGetValue(building.Model.TownId.Value, out var town))
        {
            town.Model.BuildingIds.Remove(buildingId);
        }

        GameState.Buildings.Remove(buildingId);
    }

    private static void RemoveDecorationLocally(Guid decorationId)
    {
        GameState.Decorations?.Remove(decorationId);
    }

    private static void RemoveEntityLocally(Guid entityId)
    {
        try
        {
            if (GameState.Entities is not null && GameState.Entities.TryGetValue(entityId, out var entity))
            {
                entity.Delete(new EntityRemoveInfo { RemoveType = EntityRemoveType.Removed });
                GameState.Entities.Remove(entityId);
            }
        }
        catch
        {
            GameState.Entities?.Remove(entityId);
        }
    }

    private static void ClearDeletedSelection(EditorSelection deleted)
    {
        if (ReferenceEquals(_worldTarget, deleted) || MatchesSelection(_worldTarget, deleted))
        {
            _worldTarget = null;
            _worldTargetReason = "target removed.";
        }

        if (ReferenceEquals(_selected, deleted) || MatchesSelection(_selected, deleted))
        {
            _selected = null;
        }

        ClearHighlights();
    }

    private static bool MatchesSelection(EditorSelection? left, EditorSelection right)
    {
        if (left is null || left.Kind != right.Kind)
        {
            return false;
        }

        return left.BuildingId == right.BuildingId &&
            left.DecorationId == right.DecorationId &&
            left.EntityId == right.EntityId;
    }

    private static Guid? ResolveTownForConstruction(Vector3 cursorPos, Guid? preferredTownId)
    {
        if (preferredTownId.HasValue)
        {
            return preferredTownId;
        }

        var closestTown = TownsManager.GetClosestTown(ToVector2(cursorPos));
        return closestTown?.Model?.Id;
    }

    private static bool TryGetCachedCursorPosition(out Vector3 cursorPos, out string reason)
    {
        if (_worldCursorPos.HasValue)
        {
            cursorPos = _worldCursorPos.Value;
            reason = string.Empty;
            return true;
        }

        cursorPos = Vector3.Zero;
        reason = "move the mouse over the world first.";
        return false;
    }

    private static bool TryGetCachedTarget(out EditorSelection? selection, out string reason)
    {
        if (_worldTarget is not null)
        {
            selection = _worldTarget;
            reason = string.Empty;
            return true;
        }

        selection = null;
        reason = _worldTargetReason ?? "move the mouse over a building, decoration, or entity first.";
        return false;
    }

    private static bool TryGetTargetAt(Vector3 cursorPos, Guid currentWorldId, out EditorSelection? selection, out string reason)
    {
        selection = null;
        reason = "no target found.";

        var tileSize = GetTileSize();
        var tile = new Point(
            (int)MathF.Floor(cursorPos.X / tileSize),
            (int)MathF.Floor(cursorPos.Y / tileSize));

        var building = GameState.Buildings?.Values
            .Where(candidate => IsBuildingInWorld(candidate, currentWorldId) && candidate.Model.TileBounds.Contains(tile))
            .OrderBy(candidate => candidate.Model.TileBounds.Width * candidate.Model.TileBounds.Height)
            .FirstOrDefault();
        if (building is not null)
        {
            selection = new EditorSelection
            {
                Kind = EditorSelectionKind.Building,
                Summary = $"Building {building.Model.ConstructionId} [{building.Model.Id}]",
                ConstructionId = building.Model.ConstructionId,
                TownId = building.Model.TownId,
                BuildingId = building.Model.Id
            };
            return true;
        }

        var decoration = GameState.Decorations?.Values
            .Where(candidate => candidate.Model.TileBounds.Contains(tile))
            .OrderBy(candidate => candidate.Model.TileBounds.Width * candidate.Model.TileBounds.Height)
            .FirstOrDefault();
        if (decoration is not null)
        {
            selection = new EditorSelection
            {
                Kind = EditorSelectionKind.Decoration,
                Summary = $"Decoration {decoration.Model.ConstructionId} [{decoration.Model.Id}]",
                ConstructionId = decoration.Model.ConstructionId,
                DecorationId = decoration.Model.Id
            };
            return true;
        }

        var localEntityId = GameState.LocalPlayer?.Character?.Entity?.Id;
        var nearestEntity = GameState.EntitySystem?
            .GetEntityWrappersList()
            .Where(entity =>
                entity is not null &&
                entity.WorldId == currentWorldId &&
                entity.Id != localEntityId)
            .Select(entity => new
            {
                Entity = entity,
                DistanceSquared = Vector2.DistanceSquared(entity.Position2, ToVector2(cursorPos))
            })
            .Where(candidate => candidate.DistanceSquared <= EntityPickRadius * EntityPickRadius)
            .OrderBy(candidate => candidate.DistanceSquared)
            .FirstOrDefault();
        if (nearestEntity is null)
        {
            return false;
        }

        var entityWrapper = nearestEntity.Entity;
        var furnitureId = TryGetFurnitureId(entityWrapper);
        selection = new EditorSelection
        {
            Kind = EditorSelectionKind.Entity,
            Summary = furnitureId is { Length: > 0 }
                ? $"Entity {furnitureId} [{entityWrapper.Id}] base={entityWrapper.BaseGuid}"
                : $"Entity [{entityWrapper.Id}] base={entityWrapper.BaseGuid}",
            BaseGuid = entityWrapper.BaseGuid,
            EntityId = entityWrapper.Id
        };
        return true;
    }

    private static bool IsBuildingInWorld(Building building, Guid worldId)
    {
        var model = building.Model;
        return model.ExteriorWorldId == worldId ||
            (model.InteriorWorldId.HasValue && model.InteriorWorldId.Value == worldId);
    }

    private static string? TryGetFurnitureId(EntityWrapper entity)
    {
        return FurnitureDataBase.TryGetFurnitureIdFromEntity(entity.BaseGuid, entity.Frame, out var furnitureId)
            ? furnitureId
            : null;
    }

    private static float GetTileSize()
    {
        try
        {
            var config = GameState.Config;
            if (config?.TileSize.X > 0f)
            {
                return config.TileSize.X;
            }
        }
        catch
        {
            // Fall back to the default tile size used by the game.
        }

        return DefaultTileSize;
    }

    private static bool TryGetCursorWorldPosition(out Vector3 cursorPos)
    {
        cursorPos = Vector3.Zero;
        try
        {
            if (CursorWorldPosGetter?.Invoke(null, []) is not Vector2 cursor)
            {
                return false;
            }

            var z = GameState.LocalPlayer?.Character?.Entity?.Position.Z ?? 0f;
            cursorPos = new Vector3(cursor, z);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureCheatsEnabled(IModLogger? log)
    {
        var changed = false;

        try
        {
            if (GameState.Config is { CheatsEnabled: false } clientConfig)
            {
                clientConfig.CheatsEnabled = true;
                changed = true;
            }
        }
        catch
        {
            // The config is not always ready while loading.
        }

        try
        {
            if (ServerGameState.Config is { CheatsEnabled: false } serverConfig)
            {
                serverConfig.CheatsEnabled = true;
                changed = true;
            }
        }
        catch
        {
            // Single-player server state can lag the client boot.
        }

        if (changed && !_loggedCheatEnable)
        {
            _loggedCheatEnable = true;
            log?.Info("[map-editor] enabled cheat-gated spawn/construction paths for live editing.");
        }
    }

    private static void SetStatus(string message, IModLogger? log)
    {
        _status = message;
        log?.Info($"[map-editor] {message}");
    }

    private static string Format(Vector3 position) =>
        $"{position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}";

    private static Vector2 ToVector2(Vector3 position) => new(position.X, position.Y);

    private enum LiveEditorMode
    {
        Objects,
        Tiles
    }

    private enum EditorSelectionKind
    {
        Building,
        Decoration,
        Entity
    }

    private enum TilePaintMode
    {
        FullTile,
        GroundOnly
    }

    private enum TileBrushShape
    {
        Single,
        Square,
        Circle,
        Line
    }

    private readonly record struct TileBrushCell(
        Point Offset,
        WorldTile.GroundType? Ground,
        WorldTile.StructureType? Structure);

    private readonly record struct TilePaintCell(
        Point Position,
        WorldTile.GroundType? Ground,
        WorldTile.StructureType? Structure);

    private readonly record struct TilePaintRunKey(
        int Y,
        WorldTile.GroundType? Ground,
        WorldTile.StructureType? Structure);

    private readonly record struct TileEdit(
        Rectangle Bounds,
        WorldTile.GroundType? Ground,
        WorldTile.StructureType? Structure);

    private readonly record struct RendererZoneStyle(
        Vector4 Color,
        WaterZone.WaterZoneType ZoneType,
        float Height);

    private sealed class EditorSelection
    {
        public required EditorSelectionKind Kind { get; init; }
        public required string Summary { get; init; }
        public string? ConstructionId { get; init; }
        public Guid? TownId { get; init; }
        public Guid? BuildingId { get; init; }
        public Guid? DecorationId { get; init; }
        public Guid? EntityId { get; init; }
        public Guid? BaseGuid { get; init; }
    }
}
