using System.Drawing;
using System.Windows.Forms;

namespace Romestead.ModValueEditor;

/// <summary>
/// Central dark-theme palette and styled-control factory. Keeping all of the
/// colours and the "make a control look right" logic here means the form code
/// can stay about layout and behaviour instead of repeating BackColor/ForeColor
/// soup on every control.
/// </summary>
internal static class Theme
{
    // ---- Surfaces (darkest -> lightest) ----
    public static readonly Color Deep    = Color.FromArgb(18, 18, 22);   // wells, previews, log
    public static readonly Color Strip   = Color.FromArgb(22, 22, 26);   // status strips
    public static readonly Color Panel   = Color.FromArgb(24, 24, 28);   // side panels
    public static readonly Color Window  = Color.FromArgb(28, 28, 32);   // form background
    public static readonly Color Header   = Color.FromArgb(34, 34, 40);  // section headers
    public static readonly Color Input   = Color.FromArgb(40, 40, 46);   // editable fields
    public static readonly Color Border  = Color.FromArgb(60, 60, 70);

    // ---- Text ----
    public static readonly Color Text     = Color.FromArgb(232, 230, 227);
    public static readonly Color TextDim  = Color.FromArgb(190, 190, 200);
    public static readonly Color TextMute = Color.FromArgb(150, 150, 165);

    // ---- Accents / semantic ----
    public static readonly Color AccentBlue  = Color.FromArgb(60, 100, 160);
    public static readonly Color AccentGreen = Color.FromArgb(90, 130, 70);
    public static readonly Color Neutral     = Color.FromArgb(70, 70, 80);
    public static readonly Color Ok          = Color.FromArgb(120, 200, 130);
    public static readonly Color Warn        = Color.FromArgb(220, 190, 90);
    public static readonly Color Bad         = Color.FromArgb(220, 110, 110);

    public static readonly Font UiFont     = new("Segoe UI", 9f);
    public static readonly Font UiFontBold = new("Segoe UI", 9f, FontStyle.Bold);

    // -------------------- Factories --------------------

    public static Button Button(string text, Color back, Action onClick, int width = 0)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = width == 0,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Text,
            Cursor = Cursors.Hand,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0, 0, 6, 0),
        };
        if (width > 0) b.Width = width;
        b.FlatAppearance.BorderColor = ControlPaint.Dark(back, 0.05f);
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.12f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.10f);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static TextBox TextBox(bool readOnly = false)
    {
        var t = new TextBox
        {
            BackColor = readOnly ? Strip : Input,
            ForeColor = readOnly ? TextMute : Text,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = readOnly,
        };
        return t;
    }

    public static NumericUpDown Numeric(decimal min, decimal max, decimal value = 0,
        int decimals = 0, decimal increment = 1)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            DecimalPlaces = decimals,
            Increment = increment,
            Width = 120,
            BackColor = Input,
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
    }

    public static ComboBox ComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Input,
            ForeColor = Text,
        };
    }

    public static ListBox ListBox()
    {
        return new ListBox
        {
            BackColor = Deep,
            ForeColor = Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
        };
    }

    public static TreeView TreeView()
    {
        return new TreeView
        {
            BackColor = Deep,
            ForeColor = TextDim,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            ShowLines = false,
            ShowRootLines = true,
            FullRowSelect = true,
            ItemHeight = 22,
        };
    }

    public static CheckBox CheckBox(string text)
    {
        return new CheckBox
        {
            Text = text,
            ForeColor = TextDim,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
        };
    }

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextDim,
        Padding = new Padding(0, 6, 0, 0),
    };

    public static Label HeaderLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 26,
        ForeColor = TextDim,
        Font = UiFontBold,
        Padding = new Padding(10, 5, 0, 0),
        BackColor = Header,
    };

    /// <summary>
    /// A titled, dark "card": a header strip on top of a content area. Replaces
    /// the system <see cref="GroupBox"/>, which can't be cleanly dark-themed.
    /// </summary>
    public static Panel Section(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Window, Padding = new Padding(1) };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(HeaderLabel(title));
        return panel;
    }

    // -------------------- Dark menu rendering --------------------

    public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? (e.Item.Selected ? Color.White : Color.FromArgb(230, 230, 235))
                : Color.FromArgb(120, 120, 130);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(210, 210, 220);
            base.OnRenderArrow(e);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected              => Color.FromArgb(60, 90, 140);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 90, 140);
        public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(60, 90, 140);
        public override Color MenuItemBorder                => Color.FromArgb(60, 90, 140);
        public override Color MenuItemPressedGradientBegin  => Color.FromArgb(36, 36, 42);
        public override Color MenuItemPressedGradientEnd    => Color.FromArgb(36, 36, 42);
        public override Color ToolStripDropDownBackground   => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientBegin      => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientMiddle     => Color.FromArgb(36, 36, 42);
        public override Color ImageMarginGradientEnd        => Color.FromArgb(36, 36, 42);
        public override Color MenuBorder                    => Color.FromArgb(60, 60, 70);
        public override Color SeparatorDark                 => Color.FromArgb(60, 60, 70);
        public override Color SeparatorLight                => Color.FromArgb(40, 40, 46);
        public override Color MenuStripGradientBegin        => Color.FromArgb(28, 28, 32);
        public override Color MenuStripGradientEnd          => Color.FromArgb(28, 28, 32);
    }
}
