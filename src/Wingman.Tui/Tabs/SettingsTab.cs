using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui.Tabs;

/// <summary>
/// <see cref="Shell.Settings"/> as a form, laid out as the mockup's settings screen: every change is
/// saved at once, and a theme change recolors the app at once. The groups that belong to phase 3
/// are drawn dim, with no fields.
/// </summary>
internal sealed class SettingsTab : ShellTab
{
    private readonly SettingsForm _form;
    private bool _hasBeenShown;

    public SettingsTab(Shell shell)
        : base("Settings")
    {
        CanFocus = true;
        _form = new SettingsForm(shell);
        Add(_form);
    }

    public override IReadOnlyList<KeyHint> Hints => _form.Hints;

    public override IReadOnlyList<HelpGroup> HelpGroups => [_form.Help];

    /// <summary>Focuses the first field the first time, and after that the one that had focus when the tab was left, which Terminal.Gui restores.</summary>
    public override void OnShown()
    {
        if (_hasBeenShown)
        {
            _form.SetFocus();
            return;
        }

        _hasBeenShown = true;
        _form.FocusFirstField();
    }

    private sealed class SettingsForm : FormView, IThemedView
    {
        // " Defaults" at column 1, then each row's label at 3 padded to 26, then its fields.
        private const int LabelLeft = 3;
        private const int LabelWidth = 26;
        private const int FieldLeft = LabelLeft + LabelWidth;
        private const string CheckGap = "   ";
        private const string PhaseNote = "phase 3";

        private const int DefaultsRow = 0;
        private const int ScopeRow = 1;
        private const int SourceRow = 2;
        private const int DefaultFlagsRow = 3;
        private const int BatchesRow = 4;
        private const int BatchFlagsRow = 5;
        private const int UpdatesRow = 6;
        private const int CheckRow = 7;
        private const int AutoInstallRow = 8;
        private const int ToastRow = 9;
        private const int AppearanceRow = 10;
        private const int ThemeRow = 11;
        private const int TrayRow = 12;
        private const int TrayFlagsRow = 13;
        private const int ToolsRow = 14;
        private const int ToolItemsRow = 15;
        private const int FooterRow = 17;

        private const string AcceptAgreementsLabel = "Accept package agreements";
        private const string AutoElevateLabel = "Auto-elevate when needed";
        private const string ImportLabel = "Import bundle…";
        private const string ExportLabel = "Export bundle…";
        private const string SetupText = "⏎ Register scheduled tasks (wingman setup)";
        private const string BundlesLaterText = "Bundles arrive in #47/#48";

        private static readonly string[] ScopeValues = ["", "user", "machine"];
        private static readonly string[] SourceValues = ["winget", "msstore", "all"];

        // The phase 3 rows as the mockup draws them, label then body.
        private static readonly (int Row, string Label, string Body)[] LaterRows =
        [
            (CheckRow, "Check for updates", "every [ 6 ] hours   [x] and at login"),
            (AutoInstallRow, "Auto-install", "[x] packages marked auto-update  at [ 03:00 ]"),
            (ToastRow, "", "[x] Toast when updates are found   [x] Toast when a batch finishes"),
            (TrayFlagsRow, "", "[x] Show tray icon   [x] Start at login"),
        ];

        private readonly Shell _shell;
        private readonly OptionRow _scope;
        private readonly OptionRow _source;
        private readonly CheckField _acceptAgreements;
        private readonly CheckField _includeUnknown;
        private readonly CheckField _autoElevate;
        private readonly CheckField _continueOnFailure;
        private readonly OptionRow _themeOption;
        private readonly ActionField _import;
        private readonly ActionField _export;
        private readonly View[] _fields;
        private readonly KeyHint[] _hints;

        private Theme _theme;

        public SettingsForm(Shell shell)
        {
            _shell = shell;
            _theme = shell.Theme;
            var settings = shell.Settings;

            _scope = new OptionRow(_theme, ["default", "user", "machine"]) { X = FieldLeft, Y = ScopeRow };
            _scope.SelectedIndex = Array.IndexOf(ScopeValues, settings.DefaultScope);
            _scope.Picked += () => Save(() => settings.DefaultScope = ScopeValues[_scope.SelectedIndex]);

            _source = new OptionRow(_theme, SourceValues) { X = FieldLeft, Y = SourceRow };
            _source.SelectedIndex = Array.IndexOf(SourceValues, settings.DefaultSource);
            _source.Picked += () => Save(() => settings.DefaultSource = SourceValues[_source.SelectedIndex]);

            _acceptAgreements = Check(AcceptAgreementsLabel, FieldLeft, DefaultFlagsRow, settings.AcceptAgreements);
            _acceptAgreements.Toggled += () => Save(() => settings.AcceptAgreements = _acceptAgreements.IsChecked);

            _includeUnknown = Check("Include unknown versions", FieldLeft + CheckField.WidthFor(AcceptAgreementsLabel) + CheckGap.Length, DefaultFlagsRow, settings.IncludeUnknown);
            _includeUnknown.Toggled += () => Save(() => settings.IncludeUnknown = _includeUnknown.IsChecked);

            _autoElevate = Check(AutoElevateLabel, FieldLeft, BatchFlagsRow, settings.AutoElevate);
            _autoElevate.Toggled += () => Save(() => settings.AutoElevate = _autoElevate.IsChecked);

            _continueOnFailure = Check("Continue on failure", FieldLeft + CheckField.WidthFor(AutoElevateLabel) + CheckGap.Length, BatchFlagsRow, settings.ContinueOnFailure);
            _continueOnFailure.Toggled += () => Save(() => settings.ContinueOnFailure = _continueOnFailure.IsChecked);

            _themeOption = new OptionRow(_theme, Theme.SettingNames) { X = FieldLeft, Y = ThemeRow };
            _themeOption.SelectedIndex = IndexOfIgnoringCase(Theme.SettingNames, settings.Theme);
            _themeOption.Picked += PickTheme;

            _import = new ActionField(_theme, ImportLabel) { X = LabelLeft, Y = ToolItemsRow };
            _import.Pressed += () => _shell.SetStatus(BundlesLaterText);
            _export = new ActionField(_theme, ExportLabel) { X = LabelLeft + ActionField.WidthFor(ImportLabel) + CheckGap.Length, Y = ToolItemsRow };
            _export.Pressed += () => _shell.SetStatus(BundlesLaterText);

            _fields =
            [
                _scope, _source, _acceptAgreements, _includeUnknown,
                _autoElevate, _continueOnFailure,
                _themeOption,
                _import, _export,
            ];
            _hints =
            [
                new(Key.Tab, "Next field", () => FocusField(1)),
                new(Key.Space, "Toggle", ToggleFocused, "␣"),
                new(Key.Enter, "Activate", ActivateFocused, "⏎"),
            ];

            Add(_fields);
        }

        public override IReadOnlyList<KeyHint> Hints => _hints;

        public override HelpGroup Help { get; } = new("Settings",
        [
            new("Tab", "next field"),
            new("⇧Tab", "previous field"),
            new("↑↓", "previous or next field"),
            new("←→", "move between options"),
            new("␣", "toggle or pick"),
            new("⏎", "activate"),
        ]);

        protected override IReadOnlyList<View> Fields => _fields;

        public void ApplyTheme(Theme theme) => _theme = theme;

        /// <summary>
        /// The arrows move between fields, except left and right in an option row, which it has
        /// already used; Enter activates the focused field whatever it is.
        /// </summary>
        protected override bool HandleFormKey(Key key)
        {
            if (key == Key.CursorUp || key == Key.CursorLeft)
            {
                FocusField(-1);
                return true;
            }

            if (key == Key.CursorDown || key == Key.CursorRight)
            {
                FocusField(1);
                return true;
            }

            if (key == Key.Enter)
            {
                ActivateFocused();
                return true;
            }

            return base.HandleFormKey(key);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var normal = _theme.On(_theme.Foreground);
            var dim = _theme.On(_theme.Dim);
            var header = _theme.On(_theme.Header);

            DrawText(1, DefaultsRow, "Defaults", header, width);
            DrawText(LabelLeft, ScopeRow, "Install scope", normal, width);
            DrawText(LabelLeft, SourceRow, "Source", normal, width);
            DrawText(1, BatchesRow, "Batches", header, width);
            DrawText(1, AppearanceRow, "Appearance", header, width);
            DrawText(LabelLeft, ThemeRow, "Theme", normal, width);
            DrawText(1, ToolsRow, "Tools", header, width);

            DrawText(1, UpdatesRow, "Updates", dim, width);
            DrawPhaseNote(UpdatesRow, width);
            DrawText(1, TrayRow, "Tray", dim, width);
            DrawPhaseNote(TrayRow, width);
            foreach (var (row, label, body) in LaterRows)
            {
                DrawText(LabelLeft, row, label.PadRight(LabelWidth) + body, dim, width);
            }

            var setupLeft = _export.Frame.Right + CheckGap.Length;
            DrawText(setupLeft, ToolItemsRow, $"{SetupText}   {PhaseNote}", dim, width);

            var footer = $"Saved to {_shell.SettingsStore.FilePath} as you change them";
            DrawText(1, FooterRow, footer, dim, width);
            return true;
        }

        private static int IndexOfIgnoringCase(IReadOnlyList<string> values, string value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private CheckField Check(string label, int x, int row, bool isChecked) =>
            new(_theme, label) { X = x, Y = row, IsChecked = isChecked };

        private void DrawText(int x, int y, string text, Attribute color, int width)
        {
            Move(x, y);
            SetAttribute(color);
            AddStr(CellText.Fit(text, Math.Max(0, width - x - 1)));
        }

        /// <summary>A dim <c>phase 3</c> at the right end of a group's title row.</summary>
        private void DrawPhaseNote(int row, int width)
        {
            var x = width - 1 - DisplayWidth.Of(PhaseNote);
            if (x > 1)
            {
                DrawText(x, row, PhaseNote, _theme.On(_theme.Dim), width);
            }
        }

        private void Save(Action change)
        {
            change();
            _shell.SaveSettings();
        }

        /// <summary>Stores the theme's name, <c>Auto</c> included, and recolors the app with what it resolves to.</summary>
        private void PickTheme()
        {
            var name = Theme.SettingNames[_themeOption.SelectedIndex];
            Save(() => _shell.Settings.Theme = name);
            _shell.ApplyTheme(Theme.ByName(name, _shell.ThemeDetector));
        }

        /// <summary>What Space does in the focused field, for a click on <c>␣ Toggle</c>.</summary>
        private void ToggleFocused()
        {
            if (Focused is CheckField check)
            {
                check.Toggle();
            }
            else if (Focused is OptionRow options)
            {
                options.PickHighlighted();
            }
        }

        /// <summary>What Enter does in the focused field: an action runs, and anything else toggles or picks as Space does.</summary>
        private void ActivateFocused()
        {
            if (Focused is ActionField action)
            {
                action.Press();
            }
            else
            {
                ToggleFocused();
            }
        }
    }
}
