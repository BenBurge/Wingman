using System.Globalization;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Settings;
using Wingman.Core.Setup;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui.Tabs;

/// <summary>
/// <see cref="Shell.Settings"/> as a form, laid out as the mockup's settings screen: every change is
/// saved at once, and a theme change recolors the app at once. The Tools rows open the bundle
/// screens, which take the tab's content area as they do on the list tabs, as does the batch an
/// import runs; they also register or remove the scheduled tasks and update Wingman itself when
/// the host provides those services, and draw those actions dim with the reason when it does not.
/// </summary>
internal sealed class SettingsTab : ScreenHostTab
{
    private readonly SettingsForm _form;
    private bool _hasBeenShown;

    public SettingsTab(Shell shell)
        : base(shell, "Settings")
    {
        _form = new SettingsForm(shell, OpenBundleImport, OpenBundleExport);
        Add(_form);
    }

    protected override IReadOnlyList<KeyHint> TableHints => _form.Hints;

    protected override HelpGroup TabHelp => _form.Help;

    /// <summary>Focuses the first field the first time, and after that the one that had focus when the tab was left, which Terminal.Gui restores.</summary>
    public override void OnShown()
    {
        if (_hasBeenShown)
        {
            FocusContent();
            return;
        }

        _hasBeenShown = true;
        _form.FocusFirstField();
    }

    protected override void HideContent() => _form.Visible = false;

    protected override void ShowContent()
    {
        _form.Visible = true;
        _form.SetFocus();
    }

    protected override void FocusTable() => _form.SetFocus();

    private sealed class SettingsForm : FormView, IThemedView
    {
        // " Defaults" at column 1, then each row's label at 3 padded to 26, then its fields.
        private const int LabelLeft = 3;
        private const int LabelWidth = 26;
        private const int FieldLeft = LabelLeft + LabelWidth;
        private const string CheckGap = "   ";

        // Two cells rather than three, so the second toast checkbox still ends inside a 96-column window.
        private const string ToastGap = "  ";

        private const int DefaultsRow = 0;
        private const int ScopeRow = 1;
        private const int DefaultFlagsRow = 2;
        private const int BatchesRow = 3;
        private const int BatchFlagsRow = 4;
        private const int UpdatesRow = 5;
        private const int CheckRow = 6;
        private const int AutoInstallRow = 7;
        private const int ToastRow = 8;
        private const int AppearanceRow = 9;
        private const int ThemeRow = 10;
        private const int TrayRow = 11;
        private const int TrayFlagsRow = 12;
        private const int ToolsRow = 13;
        private const int ToolItemsRow = 14;
        private const int SetupRow = 15;
        private const int RestartRow = 16;
        private const int FooterRow = 18;

        private const string IntervalPrefix = "every ";
        private const string IntervalSuffix = " hours";
        private const int IntervalWidth = 3;
        private const string AutoInstallLabel = "packages marked auto-update";
        private const string OpenBracket = "[ ";
        private const string AutoInstallTimePrefix = "  at ";
        private const int AutoInstallTimeWidth = 5;
        private const string TimeFormat = "HH:mm";
        private const string ToastOnUpdatesLabel = "Toast when updates are found";
        private const string ShowTrayIconLabel = "Show tray icon";

        private const string AcceptAgreementsLabel = "Accept package agreements";
        private const string RestartLabel = "Restart as administrator";
        private const string ImportLabel = "Import bundle…";
        private const string ExportLabel = "Export bundle…";
        private const string RegisterLabel = "Register scheduled tasks (wingman setup)";
        private const string RegisterUnavailableLabel = "Register scheduled tasks";
        private const string RemoveLabel = "Remove scheduled tasks";
        private const string UpdateLabel = "Update Wingman";
        private const string WindowsOnly = "Windows only";

        private const string RegisterQuestion = "Register Wingman's scheduled tasks, startup entry, and shortcut? (y/n)";
        private const string RemoveQuestion = "Remove Wingman's scheduled tasks, startup entry, and shortcut? (y/n)";
        private const string SelfUpdateQuestion = "Quit Wingman and update it through winget? (y/n)";

        private static readonly string[] ScopeValues = ["", "user", "machine"];
        private static readonly ElevationMode[] ElevationValues = [ElevationMode.Auto, ElevationMode.Always, ElevationMode.Never];
        private static readonly string[] ElevationLabels = ["Auto", "Always", "Never"];

        private readonly Shell _shell;
        private readonly OptionRow _scope;
        private readonly CheckField _acceptAgreements;
        private readonly CheckField _includeUnknown;
        private readonly OptionRow _elevation;
        private readonly CheckField _continueOnFailure;
        private readonly FormTextField _interval;
        private readonly CheckField _checkAtLogin;
        private readonly CheckField _autoInstall;
        private readonly FormTextField _autoInstallTime;
        private readonly CheckField _toastOnUpdates;
        private readonly CheckField _toastOnBatch;
        private readonly OptionRow _themeOption;
        private readonly CheckField _showTrayIcon;
        private readonly CheckField _startTrayAtLogin;
        private readonly ActionField _import;
        private readonly ActionField _export;

        // Null without a setup executor, where the actions are drawn dim instead.
        private readonly ActionField? _register;
        private readonly ActionField? _remove;

        // Null when Wingman cannot restart itself elevated, because it already is or is not on Windows.
        private readonly ActionField? _restart;
        private readonly int _updateLeft;
        private readonly List<View> _fields;
        private readonly KeyHint[] _hints;

        // Added once the startup check finds a newer release; drawn dim with the reason until then.
        private ActionField? _update;
        private bool _isRunningSetup;

        private Theme _theme;

        public SettingsForm(Shell shell, Action openImport, Action openExport)
        {
            _shell = shell;
            _theme = shell.Theme;
            var settings = shell.Settings;

            _scope = new OptionRow(_theme, ["default", "user", "machine"]) { X = FieldLeft, Y = ScopeRow };
            _scope.SelectedIndex = Array.IndexOf(ScopeValues, settings.DefaultScope);
            _scope.Picked += () => Save(() => settings.DefaultScope = ScopeValues[_scope.SelectedIndex]);

            _acceptAgreements = Check(AcceptAgreementsLabel, FieldLeft, DefaultFlagsRow, settings.AcceptAgreements);
            _acceptAgreements.Toggled += () => Save(() => settings.AcceptAgreements = _acceptAgreements.IsChecked);

            _includeUnknown = Check("Include unknown versions", FieldLeft + CheckField.WidthFor(AcceptAgreementsLabel) + CheckGap.Length, DefaultFlagsRow, settings.IncludeUnknown);
            _includeUnknown.Toggled += () => Save(() => settings.IncludeUnknown = _includeUnknown.IsChecked);

            _elevation = new OptionRow(_theme, ElevationLabels) { X = FieldLeft, Y = BatchFlagsRow };
            _elevation.SelectedIndex = Array.IndexOf(ElevationValues, settings.ElevationMode);
            _elevation.Picked += () => Save(() => settings.ElevationMode = ElevationValues[_elevation.SelectedIndex]);

            var continueLeft = FieldLeft + OptionRow.WidthFor(ElevationLabels) + CheckGap.Length;
            _continueOnFailure = Check("Continue on failure", continueLeft, BatchFlagsRow, settings.ContinueOnFailure);
            _continueOnFailure.Toggled += () => Save(() => settings.ContinueOnFailure = _continueOnFailure.IsChecked);

            _interval = TextBox(IntervalLeft, CheckRow, IntervalWidth, FormatHours(settings.CheckIntervalHours));
            _interval.HasFocusChanged += (_, _) => CommitOnLeave(_interval, CommitInterval);

            var checkAtLoginLeft = IntervalLeft + IntervalWidth + DisplayWidth.Of(" ]" + IntervalSuffix) + CheckGap.Length;
            _checkAtLogin = Check("and at login", checkAtLoginLeft, CheckRow, settings.CheckAtLogin);
            _checkAtLogin.Toggled += () => Save(() => settings.CheckAtLogin = _checkAtLogin.IsChecked);

            _autoInstall = Check(AutoInstallLabel, FieldLeft, AutoInstallRow, settings.AutoInstall);
            _autoInstall.Toggled += () => Save(() => settings.AutoInstall = _autoInstall.IsChecked);

            _autoInstallTime = TextBox(AutoInstallTimeLeft, AutoInstallRow, AutoInstallTimeWidth, settings.AutoInstallTime);
            _autoInstallTime.HasFocusChanged += (_, _) => CommitOnLeave(_autoInstallTime, CommitAutoInstallTime);

            _toastOnUpdates = Check(ToastOnUpdatesLabel, FieldLeft, ToastRow, settings.ToastOnUpdates);
            _toastOnUpdates.Toggled += () => Save(() => settings.ToastOnUpdates = _toastOnUpdates.IsChecked);

            var toastOnBatchLeft = FieldLeft + CheckField.WidthFor(ToastOnUpdatesLabel) + ToastGap.Length;
            _toastOnBatch = Check("Toast when a batch finishes", toastOnBatchLeft, ToastRow, settings.ToastOnBatch);
            _toastOnBatch.Toggled += () => Save(() => settings.ToastOnBatch = _toastOnBatch.IsChecked);

            _themeOption = new OptionRow(_theme, Theme.SettingNames) { X = FieldLeft, Y = ThemeRow };
            _themeOption.SelectedIndex = IndexOfIgnoringCase(Theme.SettingNames, settings.Theme);
            _themeOption.Picked += PickTheme;

            _showTrayIcon = Check(ShowTrayIconLabel, FieldLeft, TrayFlagsRow, settings.ShowTrayIcon);
            _showTrayIcon.Toggled += () => Save(() => settings.ShowTrayIcon = _showTrayIcon.IsChecked);

            var startTrayLeft = FieldLeft + CheckField.WidthFor(ShowTrayIconLabel) + CheckGap.Length;
            _startTrayAtLogin = Check("Start at login", startTrayLeft, TrayFlagsRow, settings.StartTrayAtLogin);
            _startTrayAtLogin.Toggled += () => Save(() => settings.StartTrayAtLogin = _startTrayAtLogin.IsChecked);

            _import = new ActionField(_theme, ImportLabel) { X = LabelLeft, Y = ToolItemsRow };
            _import.Pressed += openImport;
            _export = new ActionField(_theme, ExportLabel) { X = ExportLeft, Y = ToolItemsRow };
            _export.Pressed += openExport;

            if (shell.Services.Setup is not null)
            {
                _register = new ActionField(_theme, RegisterLabel) { X = RegisterLeft, Y = ToolItemsRow };
                _register.Pressed += () => AskRunSetup(remove: false);
                _remove = new ActionField(_theme, RemoveLabel) { X = LabelLeft, Y = SetupRow };
                _remove.Pressed += () => AskRunSetup(remove: true);
            }

            var removeWidth = ActionField.WidthFor(RemoveLabel);
            if (_remove is null)
            {
                removeWidth += DisplayWidth.Of(CheckGap + WindowsOnly);
            }

            _updateLeft = LabelLeft + removeWidth + CheckGap.Length;

            var canRestart = shell.CanRestartAsAdministrator && !shell.ProcessIsElevated;
            if (canRestart)
            {
                _restart = new ActionField(_theme, RestartLabel) { X = LabelLeft, Y = RestartRow };
                _restart.Pressed += shell.AskRestartAsAdministrator;
            }

            _fields =
            [
                _scope, _acceptAgreements, _includeUnknown,
                _elevation, _continueOnFailure,
                _interval, _checkAtLogin, _autoInstall, _autoInstallTime, _toastOnUpdates, _toastOnBatch,
                _themeOption,
                _showTrayIcon, _startTrayAtLogin,
                _import, _export,
            ];
            if (_register is not null && _remove is not null)
            {
                _fields.Add(_register);
                _fields.Add(_remove);
            }

            if (_restart is not null)
            {
                _fields.Add(_restart);
            }

            _hints =
            [
                new(Key.Tab, "Next field", () => FocusField(1)),
                new(Key.Space, "Toggle", ToggleFocused, "␣"),
                new(Key.Enter, "Activate", ActivateFocused, "⏎"),
            ];

            Add([.. _fields]);

            shell.SelfUpdateChecked += ShowSelfUpdateCheck;
            ShowSelfUpdateCheck();
        }

        public override IReadOnlyList<KeyHint> Hints => _hints;

        public override HelpGroup Help { get; } = new("Settings",
        [
            new("Tab", "next field"),
            new("⇧Tab", "previous field"),
            new("↑↓", "previous or next field"),
            new("←→", "move between options"),
            new("␣", "toggle or pick"),
            new("⏎", "activate, or save what was typed"),
        ]);

        protected override IReadOnlyList<View> Fields => _fields;

        private static int IntervalLeft => FieldLeft + DisplayWidth.Of(IntervalPrefix + OpenBracket);

        private static int AutoInstallTimeLeft => FieldLeft + CheckField.WidthFor(AutoInstallLabel) + DisplayWidth.Of(AutoInstallTimePrefix + OpenBracket);

        private static int ExportLeft => LabelLeft + ActionField.WidthFor(ImportLabel) + CheckGap.Length;

        private static int RegisterLeft => ExportLeft + ActionField.WidthFor(ExportLabel) + CheckGap.Length;

        public void ApplyTheme(Theme theme) => _theme = theme;

        /// <summary>
        /// The arrows move between fields, except left and right in an option row or a text box,
        /// which have already used them; Enter activates the focused field whatever it is.
        /// </summary>
        protected override bool HandleFormKey(Key key)
        {
            var isHorizontalArrow = key == Key.CursorLeft || key == Key.CursorRight;
            if (isHorizontalArrow && Focused is FormTextField)
            {
                return false;
            }

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
            DrawText(1, BatchesRow, "Batches", header, width);
            DrawText(LabelLeft, BatchFlagsRow, "Elevation", normal, width);

            DrawText(1, UpdatesRow, "Updates", header, width);
            DrawText(LabelLeft, CheckRow, "Check for updates", normal, width);
            DrawText(FieldLeft, CheckRow, IntervalPrefix, normal, width);
            DrawBrackets(_interval);
            DrawText(IntervalLeft + IntervalWidth + 2, CheckRow, IntervalSuffix, normal, width);
            DrawText(LabelLeft, AutoInstallRow, "Auto-install", normal, width);
            DrawText(FieldLeft + CheckField.WidthFor(AutoInstallLabel), AutoInstallRow, AutoInstallTimePrefix, normal, width);
            DrawBrackets(_autoInstallTime);

            DrawText(1, AppearanceRow, "Appearance", header, width);
            DrawText(LabelLeft, ThemeRow, "Theme", normal, width);
            DrawText(1, TrayRow, "Tray", header, width);
            DrawText(1, ToolsRow, "Tools", header, width);

            DrawUnavailableSetup(width);
            DrawUpdateState(width);
            DrawUnavailableRestart(width);

            var footer = $"Saved to {_shell.SettingsStore.FilePath} as you change them";
            DrawText(1, FooterRow, footer, dim, width);
            return true;
        }

        private static string FormatHours(int hours) => hours.ToString(CultureInfo.InvariantCulture);

        /// <remarks>
        /// The insertion point goes back to the start because a box exactly as wide as its text
        /// scrolls one cell to keep the cursor visible at the end, and stays scrolled after it loses focus.
        /// </remarks>
        private static void CommitOnLeave(FormTextField field, Action commit)
        {
            if (!field.HasFocus)
            {
                commit();
                field.InsertionPoint = 0;
            }
        }

        /// <summary>Draws the setup actions dim, saying why, where their fields would be when there is no setup executor.</summary>
        private void DrawUnavailableSetup(int width)
        {
            if (_register is not null)
            {
                return;
            }

            var dim = _theme.On(_theme.Dim);
            DrawText(RegisterLeft, ToolItemsRow, $"⏎ {RegisterUnavailableLabel}{CheckGap}{WindowsOnly}", dim, width);
            DrawText(LabelLeft, SetupRow, $"⏎ {RemoveLabel}{CheckGap}{WindowsOnly}", dim, width);
        }

        /// <summary>
        /// Draws <c>Update Wingman</c> dim with why it is unavailable, or the newer version after the
        /// live action once the startup check found one.
        /// </summary>
        private void DrawUpdateState(int width)
        {
            var dim = _theme.On(_theme.Dim);
            if (_update is not null)
            {
                var noteLeft = _updateLeft + ActionField.WidthFor(UpdateLabel);
                DrawText(noteLeft, SetupRow, CheckGap + UpdateNote(), dim, width);
                return;
            }

            DrawText(_updateLeft, SetupRow, $"⏎ {UpdateLabel}{CheckGap}{UpdateNote()}", dim, width);
        }

        private string UpdateNote()
        {
            if (_shell.Services.SelfUpdate is null)
            {
                return WindowsOnly;
            }

            if (_shell.SelfUpdateResult is not { } check)
            {
                return "checking…";
            }

            return check.IsNewerAvailable ? $"{check.AvailableVersion} available" : "up to date";
        }

        /// <summary>
        /// Draws the restart action dim, with why it is unavailable, where the field would be when
        /// Wingman cannot restart itself elevated.
        /// </summary>
        private void DrawUnavailableRestart(int width)
        {
            if (_restart is not null)
            {
                return;
            }

            var reason = _shell.ProcessIsElevated ? "already administrator" : WindowsOnly;
            DrawText(LabelLeft, RestartRow, $"⏎ {RestartLabel}   {reason}", _theme.On(_theme.Dim), width);
        }

        /// <summary>Draws <c>[ </c> and <c> ]</c> around <paramref name="field"/>, in accent while it has focus.</summary>
        private void DrawBrackets(FormTextField field)
        {
            var left = field.Frame.X;
            var right = left + field.Frame.Width;
            SetAttribute(field.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : _theme.On(_theme.Foreground));
            Move(left - 2, field.Frame.Y);
            AddStr(OpenBracket);
            Move(right, field.Frame.Y);
            AddStr(" ]");
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

        private FormTextField TextBox(int x, int row, int width, string text)
        {
            var field = CreateTextField(_theme);
            field.X = x;
            field.Y = row;
            field.Width = width;
            field.Text = text;
            return field;
        }

        private void DrawText(int x, int y, string text, Attribute color, int width)
        {
            Move(x, y);
            SetAttribute(color);
            AddStr(CellText.Fit(text, Math.Max(0, width - x - 1)));
        }

        private void Save(Action change)
        {
            change();
            _shell.SaveSettings();
        }

        /// <summary>Saves the interval when the box holds a whole number of hours in range, and puts the saved value back with an error otherwise.</summary>
        private void CommitInterval()
        {
            var settings = _shell.Settings;
            var isNumber = int.TryParse(_interval.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var hours);
            if (!isNumber || hours < 1 || hours > 168)
            {
                _interval.Text = FormatHours(settings.CheckIntervalHours);
                _shell.SetError("Check for updates every 1 to 168 hours");
                return;
            }

            _interval.Text = FormatHours(hours);
            if (hours != settings.CheckIntervalHours)
            {
                Save(() => settings.CheckIntervalHours = hours);
            }
        }

        /// <summary>Saves the auto-install time when the box holds a 24-hour <c>HH:mm</c>, and puts the saved value back with an error otherwise.</summary>
        private void CommitAutoInstallTime()
        {
            var settings = _shell.Settings;
            var isTime = TimeOnly.TryParseExact(
                _autoInstallTime.Text.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
            if (!isTime)
            {
                _autoInstallTime.Text = settings.AutoInstallTime;
                _shell.SetError("Auto-install time must be 24-hour HH:mm, such as 03:00");
                return;
            }

            var text = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
            _autoInstallTime.Text = text;
            if (text != settings.AutoInstallTime)
            {
                Save(() => settings.AutoInstallTime = text);
            }
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

        /// <summary>What Enter does in the focused field: an action runs, a text box commits, and anything else toggles or picks as Space does.</summary>
        private void ActivateFocused()
        {
            if (Focused is ActionField action)
            {
                action.Press();
            }
            else if (Focused == _interval)
            {
                CommitInterval();
            }
            else if (Focused == _autoInstallTime)
            {
                CommitAutoInstallTime();
            }
            else
            {
                ToggleFocused();
            }
        }

        /// <summary>
        /// Asks before registering or removing everything <see cref="SetupPlanner"/> plans from the
        /// current settings, then applies it on a background task and reports the outcome counts.
        /// </summary>
        private void AskRunSetup(bool remove)
        {
            if (_shell.Services.Setup is not { } executor)
            {
                return;
            }

            if (_isRunningSetup)
            {
                _shell.SetStatus("Setup is already running");
                return;
            }

            _shell.AskConfirm(remove ? RemoveQuestion : RegisterQuestion, () => RunSetup(executor, remove));
        }

        private void RunSetup(ISetupExecutor executor, bool remove)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _shell.SetError("Could not find Wingman's executable to register");
                return;
            }

            var plan = SetupPlanner.Build(_shell.Settings, exePath);
            _isRunningSetup = true;
            _shell.SetStatus(remove ? "Removing scheduled tasks…" : "Registering scheduled tasks…");

            var app = _shell.App;
            _ = Task.Run(async () =>
            {
                IReadOnlyList<SetupResult> results = [];
                Exception? error = null;
                try
                {
                    results = await executor.ApplyAsync(plan, remove, dryRun: false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                app.Invoke(() =>
                {
                    _isRunningSetup = false;
                    ReportSetup(remove, results, error);
                });
            });
        }

        /// <summary><c>Registered: 3 created, 1 unchanged</c>, or the first failure as an error.</summary>
        private void ReportSetup(bool remove, IReadOnlyList<SetupResult> results, Exception? error)
        {
            if (error is not null)
            {
                _shell.SetError($"Setup failed: {error.Message}");
                return;
            }

            var counts = new List<(string Outcome, int Count)>();
            foreach (var result in results)
            {
                if (result.Outcome == SetupResult.Failed)
                {
                    _shell.SetError($"Setup failed on {result.Item.Name}: {result.Error}");
                    return;
                }

                var index = counts.FindIndex(entry => entry.Outcome == result.Outcome);
                if (index < 0)
                {
                    counts.Add((result.Outcome, 1));
                }
                else
                {
                    counts[index] = (result.Outcome, counts[index].Count + 1);
                }
            }

            var parts = new List<string>();
            foreach (var (outcome, count) in counts)
            {
                parts.Add($"{count} {outcome}");
            }

            var verb = remove ? "Removed" : "Registered";
            _shell.SetSuccess($"{verb}: {string.Join(", ", parts)}");
        }

        /// <summary>Makes <c>Update Wingman</c> a live action once the startup check has found a newer release.</summary>
        private void ShowSelfUpdateCheck()
        {
            var isNewerAvailable = _shell.SelfUpdateResult is { IsNewerAvailable: true };
            if (isNewerAvailable && _update is null)
            {
                _update = new ActionField(_theme, UpdateLabel) { X = _updateLeft, Y = SetupRow };
                _update.Pressed += AskSelfUpdate;
                var restartIndex = _restart is null ? -1 : _fields.IndexOf(_restart);
                _fields.Insert(restartIndex < 0 ? _fields.Count : restartIndex, _update);
                Add(_update);
            }

            SetNeedsDraw();
        }

        /// <summary>
        /// Asks before starting winget's upgrade of Wingman in a process that outlives this one, then
        /// quits so winget can replace the executable; refused while a batch runs, since quitting
        /// would cancel it.
        /// </summary>
        private void AskSelfUpdate()
        {
            if (_shell.Services.SelfUpdate is not { } starter)
            {
                return;
            }

            if (_shell.IsBatchRunning)
            {
                _shell.SetStatus(Shell.BatchRunningText);
                return;
            }

            _shell.AskConfirm(SelfUpdateQuestion, () =>
            {
                try
                {
                    starter.StartDetachedUpgrade();
                }
                catch (Exception ex)
                {
                    _shell.SetError($"Could not start the update: {ex.Message}");
                    return;
                }

                _shell.App.RequestStop();
            });
        }
    }
}
