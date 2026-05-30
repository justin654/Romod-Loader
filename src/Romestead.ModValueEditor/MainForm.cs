using Romestead.RomodFormat;
using Romestead.RomodFormat.Package;

namespace Romestead.ModValueEditor;

internal sealed class MainForm : Form
{
    private static readonly string[] OverrideDamageTypes =
        ["Slashing", "Piercing", "Bludgeoning", "Pyro", "Chloro", "Aqua", "Cosmo", "Necro"];

    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    // ---- Menu ----
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _miNew  = new("&New override mod...")  { ShortcutKeys = Keys.Control | Keys.N };
    private readonly ToolStripMenuItem _miOpen = new("&Open mod folder...")   { ShortcutKeys = Keys.Control | Keys.O };
    private readonly ToolStripMenuItem _miSave = new("&Save")                 { ShortcutKeys = Keys.Control | Keys.S };
    private readonly ToolStripMenuItem _miPack = new("&Pack .romod...");
    private readonly ToolStripMenuItem _miExit = new("E&xit");
    private readonly ToolStripMenuItem _miRefresh = new("&Refresh game items") { ShortcutKeys = Keys.F5 };
    private readonly ToolStripMenuItem _miIconDump = new("Open &icon dump folder");
    private readonly ToolStripMenuItem _miAbout = new("&About...");

    // ---- Manifest / toolbar ----
    private readonly TextBox _folderBox = Theme.TextBox(readOnly: true);
    private readonly TextBox _modIdBox = Theme.TextBox();
    private readonly TextBox _modNameBox = Theme.TextBox();
    private readonly TextBox _versionBox = Theme.TextBox();

    // ---- Left: catalog + overrides ----
    private readonly TextBox _catalogSearchBox = Theme.TextBox();
    private readonly TreeView _catalogTree = Theme.TreeView();
    private readonly TextBox _catalogDetailsBox = new()
    {
        Dock = DockStyle.Bottom,
        Height = 170,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Deep,
        ForeColor = Theme.TextDim,
        Font = new Font("Consolas", 9f),
    };
    private readonly ListBox _entryList = Theme.ListBox();

    // ---- Right: details + preview ----
    private readonly Panel _detailsPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Window };
    private readonly Panel _itemPanel = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Window };
    private readonly Panel _entityPanel = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Window };
    private readonly PictureBox _previewBox = new()
    {
        Width = 160,
        Height = 160,
        BorderStyle = BorderStyle.FixedSingle,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Theme.Deep,
    };

    // ---- Status bar ----
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Ok,
        Padding = new Padding(10, 0, 0, 0),
        Text = "Ready",
    };
    private readonly Label _catalogStatusLabel = new()
    {
        Dock = DockStyle.Right,
        AutoSize = false,
        Width = 240,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.TextMute,
        Padding = new Padding(0, 0, 12, 0),
    };

    private readonly SplitContainer _mainSplit = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _leftSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly SplitContainer _rightSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

    // ---- Detail editors ----
    private TextBox _itemIdBox = null!;
    private CheckBox _maxStackEnabled = null!;
    private NumericUpDown _maxStackBox = null!;
    private CheckBox _tierEnabled = null!;
    private NumericUpDown _tierBox = null!;
    private CheckBox _weaponEnabled = null!;
    private NumericUpDown _swingTimerBox = null!;
    private NumericUpDown _rangeBox = null!;
    private NumericUpDown _knockbackBox = null!;
    private NumericUpDown _energyBox = null!;
    private NumericUpDown _specialEnergyBox = null!;
    private NumericUpDown _stunBox = null!;
    private NumericUpDown _movementBox = null!;
    private ListBox _damageList = null!;
    private ComboBox _damageTypeBox = null!;
    private NumericUpDown _damageMinBox = null!;
    private NumericUpDown _damageMaxBox = null!;
    private TextBox _entityBaseIdBox = null!;
    private NumericUpDown _entityMaxHealthBox = null!;
    private TextBox _previewPathBox = null!;
    private NumericUpDown _frameWidthBox = null!;
    private NumericUpDown _frameHeightBox = null!;
    private NumericUpDown _frameIndexBox = null!;

    private OverrideDocument _document = new();
    private PreviewMetadata _preview = new();
    private IconDump _iconDump = IconDump.Load();
    private List<GameItemDefinition> _catalogItems = [];
    private GameItemDefinition? _selectedCatalogItem;
    private string? _modFolder;
    private bool _loading;
    private bool _catalogLoading;

    public MainForm()
    {
        Text = "Romestead Mod Value Editor";
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(980, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        // Docking order matters: every edge-docked bar (menu/top/status) must be
        // added before the Fill content, which is added last and brought to front.
        BuildMenu();
        BuildTopBar();
        BuildStatusBar();
        BuildEditor();
        BuildItemPanel();
        BuildEntityPanel();
        BuildPreviewPanel();
        WireTooltips();

        RefreshEntryList();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // SplitContainer min-sizes *and* distances must be applied once the
        // control actually has its final size, otherwise WinForms validates
        // them against the unsized (150px) default and throws.
        _mainSplit.Panel1MinSize = 280;
        _mainSplit.Panel2MinSize = 320;
        SetSplitter(_mainSplit, 420);
        SetSplitter(_leftSplit, (int)(_leftSplit.Height * 0.55));
        SetSplitter(_rightSplit, (int)(_rightSplit.Height * 0.62));
        _ = LoadGameItemCatalogAsync();
    }

    private static void SetSplitter(SplitContainer split, int distance)
    {
        var extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var min = split.Panel1MinSize;
        var max = extent - split.Panel2MinSize - split.SplitterWidth;
        if (max <= min)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(distance, min, max);
    }

    // -------------------- Layout --------------------

    private void BuildMenu()
    {
        _menu.BackColor = Theme.Window;
        _menu.ForeColor = Color.FromArgb(230, 230, 235);
        _menu.Renderer = new Theme.DarkMenuRenderer();
        _menu.Padding = new Padding(6, 2, 0, 2);

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            _miNew, _miOpen, _miSave, _miPack, new ToolStripSeparator(), _miExit,
        });

        var catalog = new ToolStripMenuItem("&Catalog");
        catalog.DropDownItems.AddRange(new ToolStripItem[] { _miRefresh, _miIconDump });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(_miAbout);

        _menu.Items.AddRange(new ToolStripItem[] { file, catalog, help });
        MainMenuStrip = _menu;

        _miNew.Click  += (_, _) => NewMod();
        _miOpen.Click += (_, _) => OpenMod();
        _miSave.Click += (_, _) => SaveMod();
        _miPack.Click += (_, _) => PackMod();
        _miExit.Click += (_, _) => Close();
        _miRefresh.Click += async (_, _) => await LoadGameItemCatalogAsync();
        _miIconDump.Click += (_, _) => OpenIconDumpFolder();
        _miAbout.Click += (_, _) => ShowAbout();

        Controls.Add(_menu);
    }

    private void BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = Theme.Deep };
        bar.Controls.Add(_statusLabel);
        bar.Controls.Add(_catalogStatusLabel);
        Controls.Add(bar);
    }

    private void BuildTopBar()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Strip };

        // --- Manifest row (bottom of the top bar) ---
        var manifest = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            ColumnCount = 6,
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Theme.Strip,
        };
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        manifest.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _modIdBox.Dock = DockStyle.Fill;
        _modNameBox.Dock = DockStyle.Fill;
        _versionBox.Dock = DockStyle.Fill;
        _versionBox.Text = "0.1.0";
        manifest.Controls.Add(Theme.FieldLabel("Mod ID"), 0, 0);
        manifest.Controls.Add(_modIdBox, 1, 0);
        manifest.Controls.Add(Theme.FieldLabel("Name"), 2, 0);
        manifest.Controls.Add(_modNameBox, 3, 0);
        manifest.Controls.Add(Theme.FieldLabel("Version"), 4, 0);
        manifest.Controls.Add(_versionBox, 5, 0);

        // --- Toolbar row (top of the top bar) ---
        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Strip, Padding = new Padding(10, 8, 10, 4) };
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Text = "No mod folder open";
        toolbar.Controls.Add(_folderBox);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.Strip };
        buttons.Controls.Add(Theme.Button("New", Theme.Neutral, NewMod));
        buttons.Controls.Add(Theme.Button("Open", Theme.Neutral, OpenMod));
        buttons.Controls.Add(Theme.Button("Save", Theme.AccentBlue, SaveMod));
        buttons.Controls.Add(Theme.Button("Pack .romod", Theme.AccentGreen, PackMod));
        var spacer = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Theme.Strip };
        toolbar.Controls.Add(spacer);
        toolbar.Controls.Add(buttons);

        top.Controls.Add(toolbar);
        top.Controls.Add(manifest);
        Controls.Add(top);
    }

    private void BuildEditor()
    {
        _mainSplit.BackColor = Theme.Border;
        _mainSplit.Panel1.BackColor = Theme.Panel;
        _mainSplit.Panel2.BackColor = Theme.Window;

        // ----- Left column: catalog (top) + overrides (bottom) -----
        var catalogContent = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(8, 6, 8, 8) };
        _catalogSearchBox.Dock = DockStyle.Top;
        _catalogSearchBox.PlaceholderText = "Filter game items...";
        _catalogTree.Dock = DockStyle.Fill;
        var catalogButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        catalogButtons.Controls.Add(Theme.Button("Refresh", Theme.Neutral, () => _ = LoadGameItemCatalogAsync()));
        catalogButtons.Controls.Add(Theme.Button("Create / select override", Theme.AccentBlue, CreateOverrideFromCatalogSelection));
        catalogContent.Controls.Add(_catalogTree);
        catalogContent.Controls.Add(catalogButtons);
        catalogContent.Controls.Add(_catalogDetailsBox);
        catalogContent.Controls.Add(_catalogSearchBox);

        var overridesContent = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(8, 6, 8, 8) };
        _entryList.Dock = DockStyle.Fill;
        var entryButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        entryButtons.Controls.Add(Theme.Button("Add item", Theme.Neutral, AddItem));
        entryButtons.Controls.Add(Theme.Button("Add entity", Theme.Neutral, AddEntity));
        entryButtons.Controls.Add(Theme.Button("Remove", Theme.Bad, RemoveSelected));
        overridesContent.Controls.Add(_entryList);
        overridesContent.Controls.Add(entryButtons);

        _leftSplit.Panel1.Controls.Add(Theme.Section("Game items", catalogContent));
        _leftSplit.Panel2.Controls.Add(Theme.Section("Overrides", overridesContent));
        _mainSplit.Panel1.Controls.Add(_leftSplit);

        // ----- Right column: properties (top) + preview (bottom) -----
        _detailsPanel.Controls.Add(_entityPanel);
        _detailsPanel.Controls.Add(_itemPanel);
        _rightSplit.Panel1.Controls.Add(Theme.Section("Override properties", _detailsPanel));
        _mainSplit.Panel2.Controls.Add(_rightSplit);

        Controls.Add(_mainSplit);
        _mainSplit.BringToFront();

        _entryList.SelectedIndexChanged += (_, _) => LoadSelectedEntry();
        _catalogTree.AfterSelect += (_, e) => SelectCatalogNode(e.Node);
        _catalogSearchBox.TextChanged += (_, _) => RefreshCatalogTree();
    }

    private void BuildItemPanel()
    {
        var table = DetailsTable();
        _itemIdBox = Theme.TextBox();
        _itemIdBox.Dock = DockStyle.Fill;
        _maxStackEnabled = Theme.CheckBox("Override max stack");
        _maxStackBox = Theme.Numeric(1, 999999, 99);
        _tierEnabled = Theme.CheckBox("Override tier");
        _tierBox = Theme.Numeric(-100, 100, 1);
        _weaponEnabled = Theme.CheckBox("Override weapon values");
        _swingTimerBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _rangeBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _knockbackBox = Theme.Numeric(-9999, 9999, decimals: 2, increment: 0.05m);
        _energyBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _specialEnergyBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _stunBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _movementBox = Theme.Numeric(-9999, 9999, 1, decimals: 2, increment: 0.05m);
        _damageList = Theme.ListBox();
        _damageList.Height = 90;
        _damageList.Dock = DockStyle.Fill;
        _damageTypeBox = Theme.ComboBox();
        _damageTypeBox.Items.AddRange(OverrideDamageTypes);
        _damageTypeBox.SelectedIndex = 1;
        _damageMinBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);
        _damageMaxBox = Theme.Numeric(0, 9999, decimals: 2, increment: 0.05m);

        AddRow(table, "Item ID", _itemIdBox);
        AddRow(table, "", _maxStackEnabled);
        AddRow(table, "Max stack", _maxStackBox);
        AddRow(table, "", _tierEnabled);
        AddRow(table, "Tier", _tierBox);
        AddRow(table, "", _weaponEnabled);
        AddRow(table, "Swing timer", _swingTimerBox);
        AddRow(table, "Attack range", _rangeBox);
        AddRow(table, "Knockback", _knockbackBox);
        AddRow(table, "Energy cost", _energyBox);
        AddRow(table, "Special energy", _specialEnergyBox);
        AddRow(table, "Stun power", _stunBox);
        AddRow(table, "Movement factor", _movementBox);
        AddRow(table, "Damage", _damageList);

        var damageControls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Window };
        damageControls.Controls.Add(_damageTypeBox);
        damageControls.Controls.Add(Theme.FieldLabel("Min"));
        damageControls.Controls.Add(_damageMinBox);
        damageControls.Controls.Add(Theme.FieldLabel("Max"));
        damageControls.Controls.Add(_damageMaxBox);
        damageControls.Controls.Add(Theme.Button("Add / update", Theme.Neutral, AddOrUpdateDamage));
        damageControls.Controls.Add(Theme.Button("Remove", Theme.Bad, RemoveDamage));
        AddRow(table, "", damageControls);
        AddRow(table, "", Theme.Button("Apply item changes", Theme.AccentBlue, ApplySelectedEntry, width: 180));

        StackTitled(_itemPanel, "Item override", table);
    }

    private void BuildEntityPanel()
    {
        var table = DetailsTable();
        _entityBaseIdBox = Theme.TextBox();
        _entityBaseIdBox.Dock = DockStyle.Fill;
        _entityMaxHealthBox = Theme.Numeric(1, 999999, 100, decimals: 2, increment: 1);
        AddRow(table, "Base GUID", _entityBaseIdBox);
        AddRow(table, "Max health", _entityMaxHealthBox);
        AddRow(table, "", Theme.Button("Apply entity changes", Theme.AccentBlue, ApplySelectedEntry, width: 180));
        StackTitled(_entityPanel, "Entity spawn default", table);
    }

    private void BuildPreviewPanel()
    {
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Window, Padding = new Padding(8) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(_previewBox, 0, 0);

        var table = DetailsTable();
        _previewPathBox = Theme.TextBox();
        _previewPathBox.Dock = DockStyle.Fill;
        _frameWidthBox = Theme.Numeric(1, 4096, 32);
        _frameHeightBox = Theme.Numeric(1, 4096, 32);
        _frameIndexBox = Theme.Numeric(0, 100000, 0);
        AddRow(table, "Image / XNB", _previewPathBox);
        AddRow(table, "", Theme.Button("Browse...", Theme.Neutral, BrowsePreviewImage));
        AddRow(table, "Frame width", _frameWidthBox);
        AddRow(table, "Frame height", _frameHeightBox);
        AddRow(table, "Frame index", _frameIndexBox);
        AddRow(table, "", Theme.Button("Save preview settings", Theme.AccentBlue, SavePreviewForSelection, width: 180));
        content.Controls.Add(table, 1, 0);

        _rightSplit.Panel2.Controls.Add(Theme.Section("Sprite preview (editor-only metadata)", content));
    }

    // -------------------- Tooltips --------------------

    private void WireTooltips()
    {
        _tips.SetToolTip(_catalogSearchBox, "Filter the game item catalog by id, icon, category or flags.");
        _tips.SetToolTip(_catalogTree, "Browse items shipped with the game. Select one, then create an override from it.");
        _tips.SetToolTip(_entryList, "Overrides this mod will write into value-overrides.value-override.toml.");
        _tips.SetToolTip(_modIdBox, "Unique mod id, e.g. justin.balance.");
        _tips.SetToolTip(_versionBox, "Semantic version written into the mod manifest.");
        _tips.SetToolTip(_previewBox, "Editor-only sprite preview. Not written into the packaged mod.");
    }

    // -------------------- Mod folder lifecycle --------------------

    private void NewMod()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for the new override mod" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _modFolder = dialog.SelectedPath;
        Directory.CreateDirectory(Path.Combine(_modFolder, "content"));
        _modIdBox.Text = "justin.balance";
        _modNameBox.Text = "Balance Overrides";
        _versionBox.Text = "0.1.0";
        _document = new OverrideDocument();
        _preview = new PreviewMetadata();
        SaveMod();
        SetFolder(_modFolder);
        RefreshEntryList();
    }

    private void OpenMod()
    {
        using var dialog = new FolderBrowserDialog { Description = "Open a .romod source folder" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _modFolder = dialog.SelectedPath;
        SetFolder(_modFolder);
        LoadManifest();
        _document = OverrideDocument.Load(GetOverridePath());
        _preview = PreviewMetadata.Load(GetPreviewPath());
        RefreshEntryList();
        Status("Loaded mod folder.");
    }

    private void SaveMod()
    {
        if (!EnsureFolder())
        {
            return;
        }

        SaveManifest();
        ApplySelectedEntry();
        _document.Save(GetOverridePath());
        _preview.Save(GetPreviewPath());
        Status("Saved.");
    }

    private void PackMod()
    {
        SaveMod();
        if (!EnsureFolder())
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Romestead mod package (*.romod)|*.romod",
            FileName = (_modIdBox.Text.Length == 0 ? "override" : _modIdBox.Text) + ".romod"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var result = RomodPackager.Pack(_modFolder!, dialog.FileName, new UiRomodLog(this));
            Status($"Packed {Path.GetFileName(result.OutputPath)}.");
            MessageBox.Show(this, $"Packed {result.FilesIncluded} file(s).\n{result.OutputPath}", "Pack complete");
        }
        catch (RomodFormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Pack failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // -------------------- Game item catalog --------------------

    private async Task LoadGameItemCatalogAsync()
    {
        if (_catalogLoading)
        {
            return;
        }

        _catalogLoading = true;
        _miRefresh.Enabled = false;
        SetCatalogStatus("Loading game items...", Theme.Warn);
        _iconDump = IconDump.Load();
        try
        {
            var gameRoot = GameRootPath();
            var items = await Task.Run(() => GameItemCatalogLoader.Load(gameRoot).ToList());
            _catalogItems = items;
            RefreshCatalogTree();
            SetCatalogStatus($"{items.Count} game item(s)", Theme.TextMute);
            Status(_iconDump.Available
                ? $"Loaded {items.Count} game item(s); {_iconDump.Count} icon(s) available."
                : $"Loaded {items.Count} game item(s). Run the Icon Dump mod in-game to preview icons.",
                _iconDump.Available ? Theme.Ok : Theme.Warn);
        }
        catch (Exception ex)
        {
            _catalogItems = [];
            RefreshCatalogTree();
            _catalogDetailsBox.Text = "Could not load game item catalog.\r\n\r\n" + ex.Message;
            SetCatalogStatus("Catalog failed to load", Theme.Bad);
            Status("Game item catalog failed to load.", Theme.Bad);
        }
        finally
        {
            _catalogLoading = false;
            _miRefresh.Enabled = true;
        }
    }

    private void RefreshCatalogTree()
    {
        var selectedId = _selectedCatalogItem?.Id;
        var filter = _catalogSearchBox.Text.Trim();
        TreeNode? nodeToSelect = null;
        _catalogTree.BeginUpdate();
        _catalogTree.Nodes.Clear();

        foreach (var group in _catalogItems
            .Where(item => MatchesCatalogFilter(item, filter))
            .GroupBy(item => item.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupNode = new TreeNode(group.Key) { ForeColor = Theme.TextMute };
            foreach (var item in group.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var node = new TreeNode(item.DisplayText) { Tag = item, ForeColor = Theme.Text };
                groupNode.Nodes.Add(node);
                if (string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    nodeToSelect = node;
                }
            }

            _catalogTree.Nodes.Add(groupNode);
        }

        _catalogTree.ExpandAll();
        if (nodeToSelect is not null)
        {
            _catalogTree.SelectedNode = nodeToSelect;
        }
        _catalogTree.EndUpdate();
    }

    private static bool MatchesCatalogFilter(GameItemDefinition item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Icon.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Flags.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectCatalogNode(TreeNode? node)
    {
        if (node?.Tag is not GameItemDefinition item)
        {
            return;
        }

        _selectedCatalogItem = item;
        _catalogDetailsBox.Text = item.ToDetailsText();
        ShowDumpedIcon(item.Id);
    }

    private void CreateOverrideFromCatalogSelection()
    {
        if (_selectedCatalogItem is not { } catalogItem)
        {
            Status("Select a game item first.", Theme.Warn);
            return;
        }

        var existing = _document.Items.FirstOrDefault(i => string.Equals(i.Id, catalogItem.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RefreshEntryList(existing);
            Status($"Selected existing override for {catalogItem.Id}.");
            return;
        }

        var item = new ItemOverride
        {
            Id = catalogItem.Id,
            MaxStackSize = catalogItem.MaxStackSize,
            Tier = catalogItem.Tier,
            Weapon = catalogItem.Weapon is null ? null : new WeaponOverride
            {
                SwingTimer = catalogItem.Weapon.SwingTimer,
                BaseAttackRange = catalogItem.Weapon.BaseAttackRange,
                BaseKnockback = catalogItem.Weapon.BaseKnockback,
                EnergyCost = catalogItem.Weapon.EnergyCost,
                SpecialEnergyCost = catalogItem.Weapon.SpecialEnergyCost,
                StunPower = catalogItem.Weapon.StunPower,
                MovementFactor = catalogItem.Weapon.MovementFactor
            }
        };

        if (item.Weapon is not null)
        {
            foreach (var damage in catalogItem.Weapon!.Damage.Where(IsSupportedOverrideDamage).Where(d => d.Min != 0 || d.Max != 0))
            {
                item.Weapon.Damage.Add(new DamageOverride { Type = damage.Type, Min = damage.Min, Max = damage.Max });
            }
        }

        _document.Items.Add(item);
        RefreshEntryList(item);
        Status($"Created override from {catalogItem.Id}.");
    }

    private static bool IsSupportedOverrideDamage(GameDamageRange damage) =>
        OverrideDamageTypes.Contains(damage.Type, StringComparer.OrdinalIgnoreCase);

    // -------------------- Override entries --------------------

    private void AddItem()
    {
        var item = new ItemOverride { Id = "item:id", MaxStackSize = 99 };
        _document.Items.Add(item);
        RefreshEntryList(item);
    }

    private void AddEntity()
    {
        var entity = new EntityHealthOverride { BaseId = Guid.Empty.ToString(), MaxHealth = 100 };
        _document.Entities.Add(entity);
        RefreshEntryList(entity);
    }

    private void RemoveSelected()
    {
        if (_entryList.SelectedItem is ItemOverride item)
        {
            _document.Items.Remove(item);
        }
        else if (_entryList.SelectedItem is EntityHealthOverride entity)
        {
            _document.Entities.Remove(entity);
        }

        RefreshEntryList();
    }

    private void LoadSelectedEntry()
    {
        _loading = true;
        _itemPanel.Visible = _entryList.SelectedItem is ItemOverride;
        _entityPanel.Visible = _entryList.SelectedItem is EntityHealthOverride;

        if (_entryList.SelectedItem is ItemOverride item)
        {
            _itemIdBox.Text = item.Id;
            _maxStackEnabled.Checked = item.MaxStackSize.HasValue;
            _maxStackBox.Value = Clamp(item.MaxStackSize ?? 99, _maxStackBox);
            _tierEnabled.Checked = item.Tier.HasValue;
            _tierBox.Value = Clamp(item.Tier ?? 1, _tierBox);
            var weapon = item.Weapon ?? new WeaponOverride();
            _weaponEnabled.Checked = item.Weapon is not null;
            SetFloat(_swingTimerBox, weapon.SwingTimer);
            SetFloat(_rangeBox, weapon.BaseAttackRange);
            SetFloat(_knockbackBox, weapon.BaseKnockback);
            SetFloat(_energyBox, weapon.EnergyCost);
            SetFloat(_specialEnergyBox, weapon.SpecialEnergyCost);
            SetFloat(_stunBox, weapon.StunPower);
            SetFloat(_movementBox, weapon.MovementFactor ?? 1f);
            RefreshDamageList(weapon);
        }
        else if (_entryList.SelectedItem is EntityHealthOverride entity)
        {
            _entityBaseIdBox.Text = entity.BaseId;
            _entityMaxHealthBox.Value = Clamp(entity.MaxHealth, _entityMaxHealthBox);
        }

        LoadPreviewForSelection();
        _loading = false;
    }

    private void ApplySelectedEntry()
    {
        if (_loading)
        {
            return;
        }

        if (_entryList.SelectedItem is ItemOverride item)
        {
            item.Id = _itemIdBox.Text.Trim();
            item.MaxStackSize = _maxStackEnabled.Checked ? (int)_maxStackBox.Value : null;
            item.Tier = _tierEnabled.Checked ? (int)_tierBox.Value : null;
            if (_weaponEnabled.Checked)
            {
                item.Weapon ??= new WeaponOverride();
                item.Weapon.SwingTimer = (float)_swingTimerBox.Value;
                item.Weapon.BaseAttackRange = (float)_rangeBox.Value;
                item.Weapon.BaseKnockback = (float)_knockbackBox.Value;
                item.Weapon.EnergyCost = (float)_energyBox.Value;
                item.Weapon.SpecialEnergyCost = (float)_specialEnergyBox.Value;
                item.Weapon.StunPower = (float)_stunBox.Value;
                item.Weapon.MovementFactor = (float)_movementBox.Value;
            }
            else
            {
                item.Weapon = null;
            }
            RefreshEntryList(item);
        }
        else if (_entryList.SelectedItem is EntityHealthOverride entity)
        {
            entity.BaseId = _entityBaseIdBox.Text.Trim();
            entity.MaxHealth = (float)_entityMaxHealthBox.Value;
            RefreshEntryList(entity);
        }
    }

    private void AddOrUpdateDamage()
    {
        if (_entryList.SelectedItem is not ItemOverride item)
        {
            return;
        }

        item.Weapon ??= new WeaponOverride();
        var type = _damageTypeBox.Text;
        var existing = item.Weapon.Damage.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            item.Weapon.Damage.Add(new DamageOverride { Type = type, Min = (float)_damageMinBox.Value, Max = (float)_damageMaxBox.Value });
        }
        else
        {
            existing.Min = (float)_damageMinBox.Value;
            existing.Max = (float)_damageMaxBox.Value;
        }

        _weaponEnabled.Checked = true;
        RefreshDamageList(item.Weapon);
    }

    private void RemoveDamage()
    {
        if (_entryList.SelectedItem is not ItemOverride item || item.Weapon is null || _damageList.SelectedItem is not DamageOverride damage)
        {
            return;
        }

        item.Weapon.Damage.Remove(damage);
        RefreshDamageList(item.Weapon);
    }

    // -------------------- Sprite preview --------------------

    private void BrowsePreviewImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Sprite files (*.xnb;*.png;*.bmp;*.jpg)|*.xnb;*.png;*.bmp;*.jpg|XNB files (*.xnb)|*.xnb|Images (*.png;*.bmp;*.jpg)|*.png;*.bmp;*.jpg|All files (*.*)|*.*",
            InitialDirectory = PreviewStartDirectory()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _previewPathBox.Text = dialog.FileName;
        SavePreviewForSelection();
    }

    private void SavePreviewForSelection()
    {
        var key = PreviewKey();
        if (key is null)
        {
            return;
        }

        _preview.Entries[key] = new PreviewEntry
        {
            ImagePath = _previewPathBox.Text.Trim(),
            FrameWidth = (int)_frameWidthBox.Value,
            FrameHeight = (int)_frameHeightBox.Value,
            FrameIndex = (int)_frameIndexBox.Value
        };
        UpdatePreviewImage();
    }

    private void LoadPreviewForSelection()
    {
        var key = PreviewKey();
        if (key is not null && _preview.Entries.TryGetValue(key, out var entry))
        {
            _previewPathBox.Text = entry.ImagePath;
            _frameWidthBox.Value = Clamp(entry.FrameWidth, _frameWidthBox);
            _frameHeightBox.Value = Clamp(entry.FrameHeight, _frameHeightBox);
            _frameIndexBox.Value = Clamp(entry.FrameIndex, _frameIndexBox);
        }
        else
        {
            _previewPathBox.Text = "";
            _frameWidthBox.Value = 32;
            _frameHeightBox.Value = 32;
            _frameIndexBox.Value = 0;
        }

        UpdatePreviewImage();
    }

    private void UpdatePreviewImage()
    {
        DisposePreview();

        var path = _previewPathBox.Text.Trim();
        if (!File.Exists(path))
        {
            // No manual image set: fall back to the item's real dumped icon.
            ShowDumpedIcon(CurrentItemId());
            return;
        }

        try
        {
            Status(path.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase) ? "Converting XNB preview..." : "Loading preview...");
            var resolvedPath = XnbPreviewExtractor.ResolvePreviewImage(path);
            using var source = new Bitmap(resolvedPath);
            var frameWidth = Math.Max(1, (int)_frameWidthBox.Value);
            var frameHeight = Math.Max(1, (int)_frameHeightBox.Value);
            var columns = Math.Max(1, source.Width / frameWidth);
            var index = Math.Max(0, (int)_frameIndexBox.Value);
            var x = (index % columns) * frameWidth;
            var y = (index / columns) * frameHeight;
            if (x + frameWidth > source.Width || y + frameHeight > source.Height)
            {
                _previewBox.Image = new Bitmap(source);
                Status("Preview frame is outside the sheet; showing full image.", Theme.Warn);
                return;
            }

            _previewBox.Image = source.Clone(new Rectangle(x, y, frameWidth, frameHeight), source.PixelFormat);
            Status("Preview loaded.");
        }
        catch (Exception ex)
        {
            Status($"Preview failed: {ex.Message}", Theme.Bad);
        }
    }

    private string? CurrentItemId() =>
        (_entryList.SelectedItem as ItemOverride)?.Id ?? _selectedCatalogItem?.Id;

    /// <summary>Show the real in-game icon exported by the Icon Dump mod, if available.</summary>
    private void ShowDumpedIcon(string? itemId)
    {
        DisposePreview();
        var path = _iconDump.TryGetPath(itemId);
        if (path is null)
        {
            return;
        }

        try
        {
            using var source = new Bitmap(path);
            _previewBox.Image = new Bitmap(source);
        }
        catch
        {
            // A bad/locked PNG just leaves the preview empty.
        }
    }

    private void DisposePreview()
    {
        var old = _previewBox.Image;
        _previewBox.Image = null;
        old?.Dispose();
    }

    // -------------------- List + manifest helpers --------------------

    private void RefreshEntryList(object? select = null)
    {
        _entryList.BeginUpdate();
        _entryList.Items.Clear();
        foreach (var entity in _document.Entities)
        {
            _entryList.Items.Add(entity);
        }
        foreach (var item in _document.Items)
        {
            _entryList.Items.Add(item);
        }
        _entryList.EndUpdate();
        if (select is not null)
        {
            _entryList.SelectedItem = select;
        }
        else if (_entryList.Items.Count > 0)
        {
            _entryList.SelectedIndex = 0;
        }
        else
        {
            _itemPanel.Visible = false;
            _entityPanel.Visible = false;
        }
    }

    private void RefreshDamageList(WeaponOverride weapon)
    {
        _damageList.Items.Clear();
        foreach (var damage in weapon.Damage)
        {
            _damageList.Items.Add(damage);
        }
    }

    private void SaveManifest()
    {
        File.WriteAllText(Path.Combine(_modFolder!, "romestead.mod.toml"),
            $"id = \"{Escape(_modIdBox.Text.Trim())}\"\n" +
            $"name = \"{Escape(_modNameBox.Text.Trim())}\"\n" +
            $"version = \"{Escape(_versionBox.Text.Trim())}\"\n" +
            "schemaVersion = 1\n" +
            "syncMode = \"RequiredOnClient\"\n" +
            "author = \"\"\n" +
            "description = \"Existing content value overrides.\"\n");
    }

    private void LoadManifest()
    {
        var path = Path.Combine(_modFolder!, "romestead.mod.toml");
        if (!File.Exists(path))
        {
            _modIdBox.Text = "justin.balance";
            _modNameBox.Text = "Balance Overrides";
            _versionBox.Text = "0.1.0";
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            var value = parts[1].Trim().Trim('"');
            if (key == "id") { _modIdBox.Text = value; }
            else if (key == "name") { _modNameBox.Text = value; }
            else if (key == "version") { _versionBox.Text = value; }
        }
    }

    private string? PreviewKey()
    {
        if (_entryList.SelectedItem is ItemOverride item)
        {
            return "item:" + item.Id;
        }

        if (_entryList.SelectedItem is EntityHealthOverride entity)
        {
            return "entity:" + entity.BaseId;
        }

        return null;
    }

    private bool EnsureFolder()
    {
        if (!string.IsNullOrWhiteSpace(_modFolder))
        {
            Directory.CreateDirectory(Path.Combine(_modFolder, "content"));
            return true;
        }

        MessageBox.Show(this, "Create or open a mod folder first.", "No mod folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void SetFolder(string folder)
    {
        _modFolder = folder;
        _folderBox.Text = folder;
    }

    private void OpenIconDumpFolder()
    {
        Directory.CreateDirectory(IconDump.DumpDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", IconDump.DumpDir) { UseShellExecute = true });
        }
        catch { /* opening a folder is best-effort */ }
    }

    private void ShowAbout()
    {
        var ver = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0";
        MessageBox.Show(this,
            $"Romestead Mod Value Editor\nVersion {ver}\n\n" +
            "Author tooling for Romestead value-override mods.\n" +
            "Edit item / entity values and pack them into a .romod.\n\n" +
            $"Game: {GameRootPath()}",
            "About",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string GetOverridePath() => Path.Combine(_modFolder!, "content", "value-overrides.value-override.toml");
    private string GetPreviewPath() => Path.Combine(_modFolder!, "editor.preview-map.json");

    private static string GameRootPath()
    {
        var steamRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            "romestead");
        return Directory.Exists(steamRoot) ? steamRoot : AppContext.BaseDirectory;
    }

    private static string GameContentPath() => Path.Combine(GameRootPath(), "Content");

    /// <summary>
    /// Item icons live in the interface icon sheets, so open the Browse dialog
    /// there by default rather than at the Content root.
    /// </summary>
    private static string PreviewStartDirectory()
    {
        var icons = Path.Combine(GameContentPath(), "media", "interface", "icons");
        if (Directory.Exists(icons)) return icons;
        if (Directory.Exists(GameContentPath())) return GameContentPath();
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // -------------------- Control factory glue --------------------

    private static TableLayoutPanel DetailsTable() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(10),
        BackColor = Theme.Window,
    };

    /// <summary>Fills <paramref name="host"/> with a titled header above an auto-sized table.</summary>
    private static void StackTitled(Panel host, string title, Control content)
    {
        content.Dock = DockStyle.Top;
        host.Controls.Add(content);
        host.Controls.Add(Theme.HeaderLabel(title));
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(Theme.FieldLabel(label), 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static decimal Clamp(float value, NumericUpDown box) => Clamp((decimal)value, box);
    private static decimal Clamp(int value, NumericUpDown box) => Clamp((decimal)value, box);
    private static decimal Clamp(decimal value, NumericUpDown box) => Math.Min(box.Maximum, Math.Max(box.Minimum, value));

    private static void SetFloat(NumericUpDown box, float? value) => box.Value = Clamp((decimal)(value ?? 0f), box);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private void Status(string text) => Status(text, Theme.Ok);

    private void Status(string text, Color color)
    {
        _statusLabel.ForeColor = color;
        _statusLabel.Text = text;
    }

    private void SetCatalogStatus(string text, Color color)
    {
        _catalogStatusLabel.ForeColor = color;
        _catalogStatusLabel.Text = text;
    }

    private sealed class UiRomodLog(Form owner) : IRomodLog
    {
        public void Info(string message) { }
        public void Warn(string message) => MessageBox.Show(owner, message, "Pack warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        public void Error(string message) => MessageBox.Show(owner, message, "Pack error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
