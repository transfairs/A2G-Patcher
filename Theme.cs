using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace A2GPatch
{
    /// <summary>The look the user picked under "Darstellung", remembered in the registry.</summary>
    internal enum ThemeChoice
    {
        /// <summary>As Windows' app mode says (the default).</summary>
        System,
        /// <summary>Always light.</summary>
        Light,
        /// <summary>Always dark.</summary>
        Dark,
    }

    /// <summary>
    /// The colours of the windows, light or dark. "Wie Windows" follows Windows' app mode (Settings,
    /// Personalisation, Colours), which exists from Windows 10 on; before that it is light.
    /// </summary>
    internal sealed class Theme
    {
        private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>Where the program keeps the user's choice; tests point it elsewhere.</summary>
        internal static string SettingsKey = @"HKEY_CURRENT_USER\Software\A2G-Patch";

        /// <summary>Whether Windows wants apps dark; tests replace it.</summary>
        internal static Func<bool> PrefersDark = () => MeansDark(Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", null));

        /// <summary>
        /// Whether dwmapi.dll and uxtheme.dll are there to ask for Windows' own dark looks: from Vista on
        /// (Windows 2000 has neither, XP no dwmapi.dll). Windows before 10 ignores the dark requests.
        /// Tests switch it off.
        /// </summary>
        internal static bool NativeLooks = Environment.OSVersion.Version.Major >= 6;

        /// <summary>The look on a light page: the colours the window always had.</summary>
        internal static readonly Theme Light = new Theme
        {
            Page = Color.FromArgb(0xF4, 0xF6, 0xF5), Line = Color.FromArgb(0xDD, 0xE2, 0xDF), Text = SystemColors.ControlText,
            GrayText = SystemColors.GrayText, Link = Color.Empty, ApplyDisabled = Color.FromArgb(0xD3, 0xD9, 0xD6),
            Groups = new[] { Color.FromArgb(0x1F, 0x6F, 0xB2), Color.FromArgb(0x1E, 0x8E, 0x4E), Color.FromArgb(0x0E, 0x7C, 0x86), Color.FromArgb(0x7B, 0x4F, 0xC0), Color.FromArgb(0xC0, 0x6A, 0x1E) },
            Surface = Color.White, DisabledSurface = Color.FromArgb(0xF1, 0xF3, 0xF2), Border = Color.FromArgb(0xD9, 0xDE, 0xDB),
            Track = Color.FromArgb(0xC3, 0xC9, 0xC6), Title = Color.FromArgb(0x1A, 0x1F, 0x1C), TileText = Color.FromArgb(0x58, 0x63, 0x5E),
            DisabledText = Color.FromArgb(0xA6, 0xAD, 0xAA), Pending = Color.FromArgb(0xE8, 0x8A, 0x00),
            Ring = SystemColors.ControlLight, Arc = SystemColors.Highlight, Done = Color.FromArgb(0x10, 0x8A, 0x3E),
        };

        /// <summary>The look on a dark page: the same layout, the group colours a little lighter so they read on it.</summary>
        internal static readonly Theme Dark = new Theme
        {
            IsDark = true,
            Page = Color.FromArgb(0x1B, 0x1F, 0x1D), Line = Color.FromArgb(0x34, 0x3A, 0x37), Text = Color.FromArgb(0xE4, 0xE8, 0xE6),
            GrayText = Color.FromArgb(0x8E, 0x98, 0x93), Link = Color.FromArgb(0x6C, 0xB4, 0xFF), ApplyDisabled = Color.FromArgb(0x34, 0x3A, 0x37),
            Groups = new[] { Color.FromArgb(0x4C, 0x9A, 0xE0), Color.FromArgb(0x3D, 0xB8, 0x6E), Color.FromArgb(0x3E, 0xB8, 0xC2), Color.FromArgb(0xA4, 0x80, 0xE8), Color.FromArgb(0xE8, 0x96, 0x4C) },
            Surface = Color.FromArgb(0x25, 0x2A, 0x27), DisabledSurface = Color.FromArgb(0x20, 0x24, 0x22), Border = Color.FromArgb(0x3A, 0x41, 0x3D),
            Track = Color.FromArgb(0x58, 0x60, 0x5C), Title = Color.FromArgb(0xF0, 0xF3, 0xF1), TileText = Color.FromArgb(0xA9, 0xB3, 0xAE),
            DisabledText = Color.FromArgb(0x5E, 0x66, 0x62), Pending = Color.FromArgb(0xF5, 0xA6, 0x23),
            Ring = Color.FromArgb(0x3A, 0x41, 0x3D), Arc = Color.FromArgb(0x4C, 0x9A, 0xE0), Done = Color.FromArgb(0x3D, 0xB8, 0x6E),
            Button = Color.FromArgb(0x2E, 0x34, 0x31), ButtonHover = Color.FromArgb(0x38, 0x3F, 0x3B),
            ButtonDown = Color.FromArgb(0x43, 0x4B, 0x47), ButtonBorder = Color.FromArgb(0x4A, 0x52, 0x4E),
        };

        /// <summary>The theme for a choice: for <see cref="ThemeChoice.System"/> the one Windows asks for right now.</summary>
        internal static Theme For(ThemeChoice choice) => choice == ThemeChoice.Dark || (choice == ThemeChoice.System && PrefersDark()) ? Dark : Light;

        /// <summary>The choice remembered last time; <see cref="ThemeChoice.System"/> if there is none.</summary>
        internal static ThemeChoice LoadChoice() => ParseChoice(Registry.GetValue(SettingsKey, "Theme", null));

        /// <summary>Remembers the choice for the next start.</summary>
        internal static void SaveChoice(ThemeChoice choice) => Registry.SetValue(SettingsKey, "Theme", choice.ToString());

        /// <summary>The choice a stored value names; anything else is <see cref="ThemeChoice.System"/>.</summary>
        internal static ThemeChoice ParseChoice(object value) =>
            value is string name && Enum.IsDefined(typeof(ThemeChoice), name) ? (ThemeChoice)Enum.Parse(typeof(ThemeChoice), name) : ThemeChoice.System;

        /// <summary>Whether a value of AppsUseLightTheme means dark: only a 0 does, a missing value is light.</summary>
        internal static bool MeansDark(object appsUseLightTheme) => appsUseLightTheme is int light && light == 0;

        /// <summary>Whether this is the dark theme (which also darkens title bar, scroll bars and buttons).</summary>
        public bool IsDark { get; private set; }
        /// <summary>Window background.</summary>
        public Color Page { get; private set; }
        /// <summary>The rule above the buttons.</summary>
        public Color Line { get; private set; }
        /// <summary>Labels.</summary>
        public Color Text { get; private set; }
        /// <summary>Secondary text such as the version number.</summary>
        public Color GrayText { get; private set; }
        /// <summary>Links; empty for Windows' own colour.</summary>
        public Color Link { get; private set; }
        /// <summary>The apply button when there is nothing to apply.</summary>
        public Color ApplyDisabled { get; private set; }
        /// <summary>The groups' colours (blue, the pitch's green, GOG's purple), by <see cref="PatchGroup"/>.</summary>
        public Color[] Groups { get; private set; }
        /// <summary>A tile.</summary>
        public Color Surface { get; private set; }
        /// <summary>A tile whose patch can't be applied.</summary>
        public Color DisabledSurface { get; private set; }
        /// <summary>A tile's edge.</summary>
        public Color Border { get; private set; }
        /// <summary>The track of a switch that is off.</summary>
        public Color Track { get; private set; }
        /// <summary>A tile's heading.</summary>
        public Color Title { get; private set; }
        /// <summary>A tile's text.</summary>
        public Color TileText { get; private set; }
        /// <summary>Heading and text of a disabled tile.</summary>
        public Color DisabledText { get; private set; }
        /// <summary>The dot of a choice not yet applied.</summary>
        public Color Pending { get; private set; }
        /// <summary>The busy indicator's ring.</summary>
        public Color Ring { get; private set; }
        /// <summary>The busy indicator's turning arc.</summary>
        public Color Arc { get; private set; }
        /// <summary>The busy indicator's tick.</summary>
        public Color Done { get; private set; }
        /// <summary>A plain button in the dark theme (the light one keeps Windows' buttons).</summary>
        public Color Button { get; private set; }
        /// <summary>A plain button under the mouse, dark theme.</summary>
        public Color ButtonHover { get; private set; }
        /// <summary>A plain button pressed, dark theme.</summary>
        public Color ButtonDown { get; private set; }
        /// <summary>A plain button's edge, dark theme.</summary>
        public Color ButtonBorder { get; private set; }

        /// <summary>
        /// Gives a plain button the theme's look: Windows' own when light, flat in the theme's
        /// colours when dark (Windows draws no dark push buttons for WinForms).
        /// </summary>
        internal void Style(Button button)
        {
            if (this.IsDark)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = this.Button;
                button.ForeColor = this.Text;
                button.FlatAppearance.BorderColor = this.ButtonBorder;
                button.FlatAppearance.MouseOverBackColor = this.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = this.ButtonDown;
            }
            else
            {
                button.FlatStyle = FlatStyle.Standard;
                button.ForeColor = this.Text;
                button.UseVisualStyleBackColor = true;
            }
        }

        /// <summary>Gives a menu the theme's colours, with the theme's text colour on every item.</summary>
        internal void Style(ToolStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(this)) { RoundedEdges = false };
            foreach (ToolStripItem item in menu.Items)
                item.ForeColor = this.Text;
        }

        /// <summary>The colours a menu is drawn in: the tile's surface, the blue group colour under the mouse and behind the check.</summary>
        internal sealed class MenuColors : ProfessionalColorTable
        {
            private readonly Theme theme;

            /// <summary>Colours from <paramref name="theme"/>.</summary>
            public MenuColors(Theme theme)
            {
                this.theme = theme;
                this.UseSystemColors = false;
            }

            private Color Selected => PatchTile.Blend(this.theme.Surface, this.theme.Groups[0], 0.25f);

            /// <summary>The menu's background.</summary>
            public override Color ToolStripDropDownBackground => this.theme.Surface;
            /// <summary>The margin left of the items, top.</summary>
            public override Color ImageMarginGradientBegin => this.theme.Surface;
            /// <summary>The margin left of the items, middle.</summary>
            public override Color ImageMarginGradientMiddle => this.theme.Surface;
            /// <summary>The margin left of the items, bottom.</summary>
            public override Color ImageMarginGradientEnd => this.theme.Surface;
            /// <summary>The menu's edge.</summary>
            public override Color MenuBorder => this.theme.Border;
            /// <summary>The edge of the item under the mouse.</summary>
            public override Color MenuItemBorder => this.Selected;
            /// <summary>The item under the mouse.</summary>
            public override Color MenuItemSelected => this.Selected;
            /// <summary>Behind the check mark.</summary>
            public override Color CheckBackground => this.theme.Groups[0];
            /// <summary>Behind the check mark under the mouse.</summary>
            public override Color CheckSelectedBackground => this.theme.Groups[0];
            /// <summary>Behind the check mark while pressed.</summary>
            public override Color CheckPressedBackground => this.theme.Groups[0];
        }

        /// <summary>
        /// Gives a drop-down list's list the theme's colours; the closed box Windows draws itself,
        /// dark through <see cref="DarkComboBox"/> (see <see cref="StyleNative"/>).
        /// </summary>
        internal void Style(ComboBox list)
        {
            list.BackColor = this.IsDark ? this.Button : SystemColors.Window;
            list.ForeColor = this.IsDark ? this.Text : SystemColors.WindowText;
        }

        /// <summary>
        /// Darkens a window's title bar, or brings the light one back (see <see cref="NativeLooks"/>).
        /// Windows 10 knows the attribute: 20 from version 2004 on, 19 before (the newer Windows
        /// rejects the old number, the older the new).
        /// </summary>
        internal static void StyleTitleBar(IntPtr window, bool dark)
        {
            if (!NativeLooks)
                return;
            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(window, 19, ref on, sizeof(int));
            DwmSetWindowAttribute(window, 20, ref on, sizeof(int));
        }

        /// <summary>Windows' dark look for scroll bars.</summary>
        internal const string DarkScrollBars = "DarkMode_Explorer";
        /// <summary>Windows' dark look for combo boxes (the closed box, its edge and arrow).</summary>
        internal const string DarkComboBox = "DarkMode_CFD";

        /// <summary>
        /// Gives a control Windows' own dark look (<paramref name="darkLook"/>), or the normal one back
        /// (Windows 10 or later, see <see cref="NativeLooks"/>).
        /// </summary>
        internal static void StyleNative(IntPtr window, bool dark, string darkLook)
        {
            if (NativeLooks)
                SetWindowTheme(window, dark ? darkLook : null, null);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
    }
}
