using System.Globalization;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Settings;
using Wingman.Core.Setup;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// <see cref="Shell.Settings"/> as a form in two panes: the sections on the left, and the selected
/// section's settings on the right, each with a line saying what it does. Every change is saved at
/// once, and a theme change recolors the app at once. The Tools section opens the bundle screens,
/// which take the tab's content area as they do on the list tabs, as does the batch an import
/// runs; it also registers or removes the scheduled tasks and updates Wingman itself when the host
/// provides those services, and draws those actions dim with the reason when it does not.
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

    /// <summary>Shows Tools and runs the live <c>Update Wingman</c> action if the startup check found a newer release; the section already draws why otherwise.</summary>
    public void RunUpdateWingman() => _form.RunUpdateWingman();

    /// <summary>Focuses the section list the first time, and after that whatever had focus when the tab was left, which Terminal.Gui restores.</summary>
    public override void OnShown()
    {
        if (_hasBeenShown)
        {
            FocusContent();
            return;
        }

        _hasBeenShown = true;
        _form.FocusSections();
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
        private const int SectionListWidth = 20;
        private const int PaneLeft = SectionListWidth + 1;

        // The blank row and the footer under the section.
        private const int FooterRows = 2;
        private const string SavedPrefix = "Saved to ";
        private const string SavedSuffix = " as you change them";
        private const string ReasonGap = "   ";

        private const string IntervalPrefix = "every ";
        private const string IntervalSuffix = " hours";
        private const int IntervalWidth = 3;
        private const string AutoInstallTimePrefix = "at ";
        private const int AutoInstallTimeWidth = 5;
        private const string TimeFormat = "HH:mm";

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

        private static readonly string[] ScopeValues = ["", "user", "machine"];
        private static readonly ElevationMode[] ElevationValues = [ElevationMode.Auto, ElevationMode.Always, ElevationMode.Never];
        private static readonly string[] ElevationLabels = ["Auto", "Always", "Never"];
        private static readonly ElevationLauncher[] LauncherValues = [ElevationLauncher.Direct, ElevationLauncher.PowerShell];
        private static readonly string[] LauncherLabels = ["wingman.exe", "PowerShell"];

        private readonly Shell _shell;
        private readonly OptionRow _scope;
        private readonly CheckField _acceptAgreements;
        private readonly CheckField _includeUnknown;
        private readonly OptionRow _elevation;
        private readonly OptionRow _launcher;
        private readonly CheckField _continueOnFailure;
        private readonly FormTextField _interval;
        private readonly CheckField _checkAtLogin;
        private readonly CheckField _autoInstall;
        private readonly FormTextField _autoInstallTime;
        private readonly CheckField _toastOnUpdates;
        private readonly CheckField _toastOnBatch;
        private readonly CheckField _autoUpdateWingman;
        private readonly OptionRow _themeOption;
        private readonly CheckField _showTrayIcon;
        private readonly CheckField _startTrayAtLogin;
        private readonly ActionField _import;
        private readonly ActionField _export;

        // Null without a setup executor, where the actions are drawn dim instead.
        private readonly ActionField? _register;
        private readonly ActionField? _remove;

        // Hidden, and drawn dim with the reason, until the startup check finds a newer release.
        private readonly ActionField _update;

        // Null when Wingman cannot restart itself elevated, because it already is or is not on Windows.
        private readonly ActionField? _restart;

        private readonly SettingsSectionList _sectionList;
        private readonly Line _divider;
        private readonly SettingsSection[] _sections;
        private readonly SettingsSection _tools;
        private readonly KeyHint[] _hints;

        private bool _isRunningSetup;
        private bool _isDownloadingUpdate;

        private Theme _theme;

        public SettingsForm(Shell shell, Action openImport, Action openExport)
        {
            _shell = shell;
            _theme = shell.Theme;
            var settings = shell.Settings;

            // The divider below joins the window's separators only if every container up to the window renders into its line canvas.
            SuperViewRendersLineCanvas = true;

            _scope = new OptionRow(_theme, ["default", "user", "machine"]);
            _scope.SelectedIndex = Array.IndexOf(ScopeValues, settings.DefaultScope);
            _scope.Picked += () => Save(() => settings.DefaultScope = ScopeValues[_scope.SelectedIndex]);

            _acceptAgreements = Check("Accept package agreements", settings.AcceptAgreements);
            _acceptAgreements.Toggled += () => Save(() => settings.AcceptAgreements = _acceptAgreements.IsChecked);

            _includeUnknown = Check("Include unknown versions", settings.IncludeUnknown);
            _includeUnknown.Toggled += () => Save(() => settings.IncludeUnknown = _includeUnknown.IsChecked);

            _elevation = new OptionRow(_theme, ElevationLabels);
            _elevation.SelectedIndex = Array.IndexOf(ElevationValues, settings.ElevationMode);
            _elevation.Picked += () => Save(() => settings.ElevationMode = ElevationValues[_elevation.SelectedIndex]);

            // The host's elevation factory and restart read the saved setting at each prompt, so saving applies it.
            _launcher = new OptionRow(_theme, LauncherLabels);
            _launcher.SelectedIndex = Array.IndexOf(LauncherValues, settings.ElevationLauncher);
            _launcher.Picked += () => Save(() => settings.ElevationLauncher = LauncherValues[_launcher.SelectedIndex]);

            _continueOnFailure = Check("Continue on failure", settings.ContinueOnFailure);
            _continueOnFailure.Toggled += () => Save(() => settings.ContinueOnFailure = _continueOnFailure.IsChecked);

            _interval = TextBox(IntervalWidth, FormatHours(settings.CheckIntervalHours));
            _interval.HasFocusChanged += (_, _) => CommitOnLeave(_interval, CommitInterval);

            _checkAtLogin = Check("and at login", settings.CheckAtLogin);
            _checkAtLogin.Toggled += () => Save(() => settings.CheckAtLogin = _checkAtLogin.IsChecked);

            _autoInstall = Check("packages marked auto-update", settings.AutoInstall);
            _autoInstall.Toggled += () => Save(() => settings.AutoInstall = _autoInstall.IsChecked);

            _autoInstallTime = TextBox(AutoInstallTimeWidth, settings.AutoInstallTime);
            _autoInstallTime.HasFocusChanged += (_, _) => CommitOnLeave(_autoInstallTime, CommitAutoInstallTime);

            _toastOnUpdates = Check("Toast when updates are found", settings.ToastOnUpdates);
            _toastOnUpdates.Toggled += () => Save(() => settings.ToastOnUpdates = _toastOnUpdates.IsChecked);

            _toastOnBatch = Check("Toast when a batch finishes", settings.ToastOnBatch);
            _toastOnBatch.Toggled += () => Save(() => settings.ToastOnBatch = _toastOnBatch.IsChecked);

            _autoUpdateWingman = Check("Keep Wingman up to date automatically", settings.AutoUpdateWingman);
            _autoUpdateWingman.Toggled += () => Save(() => settings.AutoUpdateWingman = _autoUpdateWingman.IsChecked);

            _themeOption = new OptionRow(_theme, Theme.SettingNames);
            _themeOption.SelectedIndex = IndexOfIgnoringCase(Theme.SettingNames, settings.Theme);
            _themeOption.Picked += PickTheme;

            _showTrayIcon = Check("Show tray icon", settings.ShowTrayIcon);
            _showTrayIcon.Toggled += () => Save(() => settings.ShowTrayIcon = _showTrayIcon.IsChecked);

            _startTrayAtLogin = Check("Start at login", settings.StartTrayAtLogin);
            _startTrayAtLogin.Toggled += () => Save(() => settings.StartTrayAtLogin = _startTrayAtLogin.IsChecked);

            _import = new ActionField(_theme, ImportLabel);
            _import.Pressed += openImport;
            _export = new ActionField(_theme, ExportLabel);
            _export.Pressed += openExport;

            if (shell.Services.Setup is not null)
            {
                _register = new ActionField(_theme, RegisterLabel);
                _register.Pressed += () => AskRunSetup(remove: false);
                _remove = new ActionField(_theme, RemoveLabel);
                _remove.Pressed += () => AskRunSetup(remove: true);
            }

            _update = new ActionField(_theme, UpdateLabel) { Visible = false };
            _update.Pressed += AskSelfUpdate;

            var canRestart = shell.CanRestartAsAdministrator && !shell.ProcessIsElevated;
            if (canRestart)
            {
                _restart = new ActionField(_theme, RestartLabel);
                _restart.Pressed += shell.AskRestartAsAdministrator;
            }

            var defaults = new SettingsSection(_theme, "Defaults", "How winget installs packages");
            defaults.AddSetting("Install scope", [new(_scope)], "Where packages install when a package has no scope of its own.");
            defaults.AddSetting("", [new(_acceptAgreements)], "Pass --accept-package-agreements so installs do not stop at a license prompt.");
            defaults.AddSetting("", [new(_includeUnknown)], "List packages whose installed version winget cannot read.");

            var batches = new SettingsSection(_theme, "Batches", "How batches run and elevate");
            batches.AddSetting("Elevation", [new(_elevation)],
                "Auto elevates only what needs it; Always runs every batch elevated; Never lets winget prompt per installer.");
            batches.AddSetting("Elevate via", [new(_launcher)],
                "PowerShell helps when an admin-approval tool, such as Admin By Request, only allows PowerShell.");
            batches.AddSetting("", [new(_continueOnFailure)], "Keep running the rest of a batch after one operation fails.");

            var updates = new SettingsSection(_theme, "Updates", "Checking for updates, auto-install, and notifications");
            updates.AddSetting("Check for updates", [new(_interval, IntervalPrefix, IntervalSuffix), new(_checkAtLogin)],
                "How often the scheduled task checks winget and GitHub Releases.");
            updates.AddSetting("Auto-install", [new(_autoInstall), new(_autoInstallTime, AutoInstallTimePrefix)],
                "Upgrade packages marked auto-update at this time each day.");
            updates.AddSetting("Notifications", [new(_toastOnUpdates), new(_toastOnBatch)],
                "Windows notifications from the scheduled check and from batches.");
            updates.AddSetting("", [new(_autoUpdateWingman)],
                "Download, verify, and install new Wingman releases from GitHub on their own.");

            var appearance = new SettingsSection(_theme, "Appearance", "Colors");
            appearance.AddSetting("Theme", [new(_themeOption)], "Auto follows the Windows light or dark mode.");

            var tray = new SettingsSection(_theme, "Tray", "The tray icon");
            tray.AddSetting("", [new(_showTrayIcon), new(_startTrayAtLogin)],
                "The tray shows update badges and a menu; setup registers it to start at login.");

            _tools = new SettingsSection(_theme, "Tools", "Bundles, scheduled tasks, and Wingman itself");
            _tools.AddSetting("Bundles", [new(_import)], "Install packages from a UniGetUI .ubundle.");
            _tools.AddSetting("", [new(_export)], "Save installed packages and their options to a .ubundle file.");
            _tools.AddSetting("Scheduled tasks", [new(_register) { Unavailable = $"⏎ {RegisterUnavailableLabel}{ReasonGap}{WindowsOnly}" }],
                "Create the scheduled checks, the tray at login, the Start Menu shortcut, and wingman: links.");
            _tools.AddSetting("", [new(_remove) { Unavailable = $"⏎ {RemoveLabel}{ReasonGap}{WindowsOnly}" }], "Undo Register.");
            _tools.AddSetting("Wingman", [new(_update) { Unavailable = $"⏎ {UpdateLabel}" }], UpdateNote);
            _tools.AddSetting("", [new(_restart) { Unavailable = $"⏎ {RestartLabel}" }], RestartNote);

            _sections = [defaults, batches, updates, appearance, tray, _tools];
            var titles = new List<string>();
            foreach (var section in _sections)
            {
                titles.Add(section.Title);
                section.X = PaneLeft;
                section.Y = 0;
                section.Width = Dim.Fill();
                section.Height = Dim.Fill(FooterRows);
            }

            _sectionList = new SettingsSectionList(_theme, titles)
            {
                X = 0,
                Y = 0,
                Width = SectionListWidth,
                Height = Dim.Fill(),
            };
            _sectionList.SelectionChanged += ShowSelectedSection;

            // Starts one row above and ends one row below the form so it meets the shell's separators.
            _divider = new Line
            {
                Orientation = Orientation.Vertical,
                X = SectionListWidth,
                Y = -1,
                Height = Dim.Fill(-1),
                SuperViewRendersLineCanvas = true,
                LineAttribute = _theme.On(_theme.Border),
            };

            _hints =
            [
                new(Key.CursorUp, "Section", FocusSections, "↑↓"),
                new(Key.Tab, "Next field", FocusNext),
                new(Key.Space, "Toggle", ToggleFocused, "␣"),
                new(Key.Enter, "Activate", ActivateFocused, "⏎"),
            ];

            Add(_sectionList, _divider);
            Add([.. _sections]);
            ShowSelectedSection();

            shell.SelfUpdateChecked += ShowSelfUpdateCheck;
            ShowSelfUpdateCheck();
        }

        public override IReadOnlyList<KeyHint> Hints => _hints;

        public override HelpGroup Help { get; } = new("Settings",
        [
            new("↑↓", "choose a section, or the field above or below"),
            new("→ ⏎", "into the section"),
            new("Tab", "next field; after the last, back to the sections"),
            new("⇧Tab", "previous field"),
            new("Esc ←", "back to the sections (← outside radio options)"),
            new("←→", "move between options"),
            new("␣", "toggle or pick"),
            new("⏎", "activate, or save what was typed"),
        ]);

        protected override IReadOnlyList<View> Fields => CurrentSection.Fields;

        private SettingsSection CurrentSection => _sections[_sectionList.SelectedIndex];

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            _divider.LineAttribute = theme.On(theme.Border);
        }

        public void FocusSections() => _sectionList.SetFocus();

        /// <summary>Shows Tools and presses the live <c>Update Wingman</c> action; nothing to press before the startup check has found a newer release.</summary>
        public void RunUpdateWingman()
        {
            SelectSection(Array.IndexOf(_sections, _tools));
            if (!_update.Visible)
            {
                FocusSections();
                return;
            }

            _update.SetFocus();
            _update.Press();
        }

        /// <summary>
        /// Tab and Shift+Tab walk the section's fields and go back to the section list past either
        /// end, as Esc does, and left outside a radio list or text box. From the list, right, Enter,
        /// and Tab go into the section. In the section, up and down move between fields, except in a
        /// stacked radio list until its first or last option, and Enter activates the focused field.
        /// </summary>
        protected override bool HandleFormKey(Key key)
        {
            if (key == Key.Tab)
            {
                FocusNext();
                return true;
            }

            if (key == Key.Tab.WithShift)
            {
                FocusPrevious();
                return true;
            }

            if (_sectionList.HasFocus)
            {
                var entersSection = key == Key.CursorRight || key == Key.Enter;
                if (entersSection)
                {
                    FocusFieldAt(0);
                }

                return entersSection;
            }

            var isHorizontalArrow = key == Key.CursorLeft || key == Key.CursorRight;
            if (isHorizontalArrow && MostFocused is FormTextField)
            {
                return false;
            }

            if (key == Key.Esc || key == Key.CursorLeft)
            {
                FocusSections();
                return true;
            }

            if (key == Key.CursorUp || key == Key.CursorDown)
            {
                var step = key == Key.CursorUp ? -1 : 1;
                var fields = Fields;
                var current = FocusedFieldIndex();
                FocusFieldAt(Math.Clamp(current + step, 0, fields.Count - 1));
                return true;
            }

            if (key == Key.Enter)
            {
                ActivateFocused();
                return true;
            }

            return false;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var left = PaneLeft + 1;
            var width = Math.Max(0, Viewport.Width - left - 1);
            var pathWidth = width - DisplayWidth.Of(SavedPrefix + SavedSuffix);
            var path = CellText.FitKeepingEnd(_shell.SettingsStore.FilePath, pathWidth);
            Move(left, Viewport.Height - 1);
            SetAttribute(_theme.On(_theme.Dim));
            AddStr(CellText.Fit(SavedPrefix + path + SavedSuffix, width));
            return true;
        }

        /// <summary>The next field in the section, the first one from the section list, and the section list after the last one.</summary>
        private void FocusNext()
        {
            if (_sectionList.HasFocus)
            {
                FocusFieldAt(0);
                return;
            }

            var next = FocusedFieldIndex() + 1;
            if (next >= Fields.Count)
            {
                FocusSections();
                return;
            }

            FocusFieldAt(next);
        }

        /// <summary>The previous field in the section, the last one from the section list, and the section list before the first one.</summary>
        private void FocusPrevious()
        {
            if (_sectionList.HasFocus)
            {
                FocusFieldAt(Fields.Count - 1);
                return;
            }

            var previous = FocusedFieldIndex() - 1;
            if (previous < 0)
            {
                FocusSections();
                return;
            }

            FocusFieldAt(previous);
        }

        private void FocusFieldAt(int index)
        {
            var fields = Fields;
            if (index >= 0 && index < fields.Count)
            {
                fields[index].SetFocus();
            }
        }

        /// <summary>The focused field's place in the section's Tab order, or -1 when focus is elsewhere.</summary>
        private int FocusedFieldIndex()
        {
            var fields = Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].HasFocus)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SelectSection(int index)
        {
            _sectionList.SelectedIndex = index;
            ShowSelectedSection();
        }

        private void ShowSelectedSection()
        {
            var current = CurrentSection;
            foreach (var section in _sections)
            {
                section.Visible = section == current;
            }

            SetNeedsDraw();
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

        /// <summary><c>Update Wingman</c>'s status: the newer version, or why the action is unavailable.</summary>
        private string UpdateNote()
        {
            if (_shell.Services.SelfUpdate is null || _shell.Services.Releases is null)
            {
                return WindowsOnly;
            }

            if (_shell.SelfUpdateResult is not { } check)
            {
                return "checking…";
            }

            if (check.Latest is not { } latest)
            {
                return "could not reach GitHub";
            }

            return check.IsNewerAvailable ? $"{latest.Version} available" : "up to date";
        }

        /// <summary>What <c>Restart as administrator</c> does, or why it is unavailable.</summary>
        private string RestartNote()
        {
            if (_restart is not null)
            {
                return "Run elevated, so batches need no UAC prompt.";
            }

            return _shell.ProcessIsElevated ? "already administrator" : WindowsOnly;
        }

        private CheckField Check(string label, bool isChecked) => new(_theme, label) { IsChecked = isChecked };

        private FormTextField TextBox(int width, string text)
        {
            var field = CreateTextField(_theme);
            field.Width = width;
            field.Text = text;
            return field;
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
            var focused = MostFocused;
            if (focused is CheckField check)
            {
                check.Toggle();
            }
            else if (focused is OptionRow options)
            {
                options.PickHighlighted();
            }
        }

        /// <summary>What Enter does: the section list goes into its section, an action runs, a text box commits, and anything else toggles or picks as Space does.</summary>
        private void ActivateFocused()
        {
            if (_sectionList.HasFocus)
            {
                FocusFieldAt(0);
                return;
            }

            var focused = MostFocused;
            if (focused is ActionField action)
            {
                action.Press();
            }
            else if (focused == _interval)
            {
                CommitInterval();
            }
            else if (focused == _autoInstallTime)
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
            if (isNewerAvailable)
            {
                _update.Visible = true;
            }

            _tools.Refresh();
        }

        /// <summary>
        /// Asks before downloading the newer release's installer, then verifies and starts it and
        /// quits, since the installer stops every running Wingman to replace the executable; refused
        /// while a batch runs, since quitting would cancel it.
        /// </summary>
        private void AskSelfUpdate()
        {
            var services = _shell.Services;
            if (services.SelfUpdate is not { } starter
                || services.Downloader is not { } downloader
                || _shell.SelfUpdateResult is not { IsNewerAvailable: true, Latest: { } latest })
            {
                return;
            }

            if (_shell.IsBatchRunning)
            {
                _shell.SetStatus(Shell.BatchRunningText);
                return;
            }

            if (_isDownloadingUpdate)
            {
                _shell.SetStatus($"Wingman {latest.Version} is already downloading");
                return;
            }

            _shell.AskConfirm($"Download Wingman {latest.Version} and restart? (y/n)", () => DownloadUpdate(starter, downloader, latest));
        }

        private void DownloadUpdate(ISelfUpdateStarter starter, UpdateDownloader downloader, ReleaseInfo latest)
        {
            _isDownloadingUpdate = true;
            _shell.SetProgress($"Downloading Wingman {latest.Version}…");

            var app = _shell.App;
            var directory = _shell.Services.UpdateDirectory;
            _ = Task.Run(async () =>
            {
                string? setupPath = null;
                Exception? error = null;
                try
                {
                    setupPath = await downloader.DownloadVerifiedAsync(latest, directory, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                app.Invoke(() =>
                {
                    _isDownloadingUpdate = false;
                    InstallUpdate(starter, latest, setupPath, error);
                });
            });
        }

        /// <summary>Starts the verified installer and quits, or says why it cannot.</summary>
        private void InstallUpdate(ISelfUpdateStarter starter, ReleaseInfo latest, string? setupPath, Exception? error)
        {
            if (setupPath is null)
            {
                _shell.SetError($"Could not download Wingman {latest.Version}: {error?.Message}");
                return;
            }

            // A batch started while the download ran would be cut off by quitting now.
            if (_shell.IsBatchRunning)
            {
                _shell.SetError($"Wingman {latest.Version} is downloaded; choose Update Wingman again once the batch finishes");
                return;
            }

            // Marked before the installer starts, because it stops this process; the new version's
            // self-update --announce reads the marker to show the "Wingman updated" toast.
            var state = WingmanApp.CreateStateStore();
            state.Update(s => s.PendingUpdateVersion = latest.Version);
            try
            {
                starter.StartInstaller(setupPath);
            }
            catch (Exception ex)
            {
                state.Update(s => s.PendingUpdateVersion = "");
                _shell.SetError($"Could not start the Wingman installer: {ex.Message}");
                return;
            }

            _shell.App.RequestStop();
        }
    }
}
