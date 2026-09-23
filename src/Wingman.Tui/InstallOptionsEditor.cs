using System.Text;
using System.Text.Json;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Edits one package's saved <see cref="InstallOptions"/> and <see cref="UpdatesOptions"/> in place
/// of a list tab's content, laid out as the mockup's install options screen. Enter saves both to
/// <see cref="Shell.Options"/>, Esc cancels and asks first when something changed, and Ctrl+R puts
/// every field back to its default. Options the form has no field for keep their stored values.
/// </summary>
internal sealed class InstallOptionsEditor : FormView
{
    private const int LabelWidth = 22;

    // The label column, then "[ " before a text box.
    private const int FieldLeft = 1 + LabelWidth + 2;
    private const int FieldWidth = 40;
    private const int IgnoredVersionWidth = 8;

    // The three checkbox columns the mockup lines up.
    private const int FirstColumn = 1 + LabelWidth;
    private const int SecondColumn = FirstColumn + 26;
    private const int ThirdColumn = SecondColumn + 22;

    private const int TitleRow = 0;
    private const int ScopeRow = 2;
    private const int ArchitectureRow = 3;
    private const int VersionRow = 4;
    private const int FirstFlagRow = 5;
    private const int SecondFlagRow = 6;
    private const int InstallArgumentsRow = 8;
    private const int UpdateArgumentsRow = 9;
    private const int UninstallArgumentsRow = 10;
    private const int LocationRow = 11;
    private const int PreCommandRow = 13;
    private const int PostCommandRow = 14;
    private const int KillRow = 15;
    private const int UpdatesRow = 17;
    private const int FooterRow = 19;

    private const string IgnoredVersionLabel = "Ignored version [ ";
    private const string Footer = " Stored in package-options.json · exported into bundles as InstallationOptions";

    private static readonly string[] ScopeValues = ["", "machine", "user"];
    private static readonly string[] ArchitectureValues = ["", "x64", "arm64"];

    private readonly Shell _shell;
    private readonly Theme _theme;
    private readonly PackageRow _row;
    private readonly InstallOptions _savedInstall;
    private readonly UpdatesOptions _savedUpdates;

    private readonly OptionRow _scope;
    private readonly OptionRow _architecture;
    private readonly FormTextField _version;
    private readonly CheckField _interactive;
    private readonly CheckField _skipHashCheck;
    private readonly CheckField _preRelease;
    private readonly CheckField _runAsAdministrator;
    private readonly CheckField _removeData;
    private readonly FormTextField _installArguments;
    private readonly FormTextField _updateArguments;
    private readonly FormTextField _uninstallArguments;
    private readonly FormTextField _location;
    private readonly FormTextField _preCommand;
    private readonly FormTextField _postCommand;
    private readonly FormTextField _kill;
    private readonly CheckField _autoUpdate;
    private readonly CheckField _ignoreAll;
    private readonly FormTextField _ignoredVersion;

    private readonly View[] _fields;
    private readonly (int Row, string Label, FormTextField Field)[] _textRows;
    private readonly KeyHint[] _hints;

    // Where the options without a field come from, and what the list fields compare against to
    // tell an untouched field from an edited one; Ctrl+R replaces it with the defaults.
    private InstallOptions _base;

    public InstallOptionsEditor(Shell shell, PackageRow row)
    {
        _shell = shell;
        _theme = shell.Theme;
        _row = row;
        _savedInstall = shell.Options.GetInstallOptions(row.Id);
        _savedUpdates = shell.Options.GetUpdatesOptions(row.Id);
        _base = Clone(_savedInstall);

        _scope = new OptionRow(_theme, ["default", "machine", "user"]) { X = FirstColumn, Y = ScopeRow };
        _architecture = new OptionRow(_theme, ["default", "x64", "arm64"]) { X = FirstColumn, Y = ArchitectureRow };
        _version = TextBox(VersionRow, FieldLeft, FieldWidth, "latest");
        _interactive = Check("Interactive install", FirstColumn, FirstFlagRow);
        _skipHashCheck = Check("Skip hash check", SecondColumn, FirstFlagRow);
        _preRelease = Check("Pre-release", ThirdColumn, FirstFlagRow);
        _runAsAdministrator = Check("Run as administrator", FirstColumn, SecondFlagRow);
        _removeData = Check("Remove data on uninstall", SecondColumn, SecondFlagRow);
        _installArguments = TextBox(InstallArgumentsRow, FieldLeft, FieldWidth);
        _updateArguments = TextBox(UpdateArgumentsRow, FieldLeft, FieldWidth);
        _uninstallArguments = TextBox(UninstallArgumentsRow, FieldLeft, FieldWidth);
        _location = TextBox(LocationRow, FieldLeft, FieldWidth, "default");
        _preCommand = TextBox(PreCommandRow, FieldLeft, FieldWidth);
        _postCommand = TextBox(PostCommandRow, FieldLeft, FieldWidth);
        _kill = TextBox(KillRow, FieldLeft, FieldWidth);
        _autoUpdate = Check("Auto-update", FirstColumn, UpdatesRow);
        _ignoreAll = Check("Ignore all updates", FirstColumn + 18, UpdatesRow);
        var ignoredVersionLeft = FirstColumn + 18 + 25 + DisplayWidth.Of(IgnoredVersionLabel);
        _ignoredVersion = TextBox(UpdatesRow, ignoredVersionLeft, IgnoredVersionWidth);

        _fields =
        [
            _scope, _architecture, _version,
            _interactive, _skipHashCheck, _preRelease, _runAsAdministrator, _removeData,
            _installArguments, _updateArguments, _uninstallArguments, _location,
            _preCommand, _postCommand, _kill,
            _autoUpdate, _ignoreAll, _ignoredVersion,
        ];
        _textRows =
        [
            (VersionRow, "Version to install", _version),
            (InstallArgumentsRow, "Install arguments", _installArguments),
            (UpdateArgumentsRow, "Update arguments", _updateArguments),
            (UninstallArgumentsRow, "Uninstall arguments", _uninstallArguments),
            (LocationRow, "Install location", _location),
            (PreCommandRow, "Pre-install command", _preCommand),
            (PostCommandRow, "Post-install command", _postCommand),
            (KillRow, "Kill before operation", _kill),
        ];

        _hints =
        [
            new(Key.Enter, "Save", Save, "⏎"),
            new(Key.Esc, "Cancel", Cancel),
            new(Key.Tab, "Next field", () => FocusField(1)),
            new(Key.Space, "Toggle", ToggleFocused, "␣"),
            new(Key.R.WithCtrl, "Reset", Reset, "Ctrl+R"),
        ];

        Add(_fields);
        Show(_base, _savedUpdates);
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override HelpGroup Help { get; } = new("Install options",
    [
        new("⏎", "save"),
        new("Esc", "cancel"),
        new("Tab", "next field"),
        new("⇧Tab", "previous field"),
        new("↑↓", "previous or next field"),
        new("←→", "move between options"),
        new("␣", "toggle or pick"),
        new("Ctrl+R", "reset to defaults"),
    ]);

    protected override IReadOnlyList<View> Fields => _fields;

    /// <summary>Splits on spaces, keeping a double-quoted stretch together without its quotes.</summary>
    internal static List<string> ParseArguments(string text)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var isQuoted = false;
        var hasArgument = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                isQuoted = !isQuoted;
                hasArgument = true;
            }
            else if (char.IsWhiteSpace(character) && !isQuoted)
            {
                if (hasArgument)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasArgument = false;
                }
            }
            else
            {
                current.Append(character);
                hasArgument = true;
            }
        }

        if (hasArgument)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    /// <summary>Joins with spaces, quoting an argument that is empty or has a space in it, so <see cref="ParseArguments"/> gives the list back.</summary>
    internal static string FormatArguments(IReadOnlyList<string> arguments)
    {
        var parts = new List<string>();
        foreach (var argument in arguments)
        {
            var needsQuotes = argument.Length == 0 || argument.Any(char.IsWhiteSpace);
            parts.Add(needsQuotes ? $"\"{argument}\"" : argument);
        }

        return string.Join(' ', parts);
    }

    /// <summary>Splits on commas when there are any, so a process name with a space survives, and on spaces otherwise.</summary>
    internal static List<string> ParseProcessNames(string text)
    {
        char[] separators = text.Contains(',') ? [','] : [' ', '\t'];
        return [.. text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    protected override bool HandleFormKey(Key key)
    {
        if (key == Key.Enter)
        {
            Save();
            return true;
        }

        if (key == Key.Esc)
        {
            Cancel();
            return true;
        }

        if (key == Key.R.WithCtrl)
        {
            Reset();
            return true;
        }

        if (key == Key.CursorUp || key == Key.CursorDown)
        {
            FocusField(key == Key.CursorUp ? -1 : 1);
            return true;
        }

        return base.HandleFormKey(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = _theme.On(_theme.Foreground);
        var dim = _theme.On(_theme.Dim);
        var width = Viewport.Width;

        Move(0, TitleRow);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(" Install options");
        SetAttribute(dim);
        var subtitle = $"  {_row.Id} · saved per package, used on every upgrade";
        AddStr(CellText.Fit(subtitle, Math.Max(0, width - DisplayWidth.Of(" Install options") - 1)));

        DrawLabel(ScopeRow, "Scope", normal);
        DrawLabel(ArchitectureRow, "Architecture", normal);
        foreach (var (row, label, field) in _textRows)
        {
            DrawLabel(row, label, normal);
            DrawBrackets(field);
        }

        DrawLabel(UpdatesRow, "Updates", normal);
        Move(_ignoredVersion.Frame.X - DisplayWidth.Of(IgnoredVersionLabel), UpdatesRow);
        SetAttribute(normal);
        AddStr(IgnoredVersionLabel.TrimEnd('[', ' '));
        DrawBrackets(_ignoredVersion);

        Move(0, FooterRow);
        SetAttribute(dim);
        AddStr(CellText.Fit(Footer, Math.Max(0, width - 1)));
        return true;
    }

    /// <summary>A click on a text box's label focuses the box, as a click on the box itself does.</summary>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.IsLeftClick() || mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        foreach (var (row, _, field) in _textRows)
        {
            if (position.Y == row && position.X < FieldLeft)
            {
                field.SetFocus();
                return true;
            }
        }

        return true;
    }

    // A deep copy, lists included, so edits never reach the store's own instance before Save.
    private static InstallOptions Clone(InstallOptions options) =>
        JsonSerializer.Deserialize<InstallOptions>(JsonSerializer.Serialize(options))!;

    private static string ValueOf(OptionRow row, string[] values, string current) =>
        row.SelectedIndex >= 0 ? values[row.SelectedIndex] : current;

    /// <summary>The stored list when the box still shows it unedited, so quoting never rewrites it; the box's text split up otherwise.</summary>
    private static List<string> ListFrom(FormTextField field, List<string> stored, string shown, Func<string, List<string>> parse) =>
        field.Text == shown ? [.. stored] : parse(field.Text);

    private static int IndexOf(string[] values, string value)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private FormTextField TextBox(int row, int x, int width, string placeholder = "")
    {
        var field = CreateTextField(_theme);
        field.X = x;
        field.Y = row;
        field.Width = width;
        field.Placeholder = placeholder;
        return field;
    }

    private CheckField Check(string label, int x, int row) => new(_theme, label) { X = x, Y = row };

    private void DrawLabel(int row, string label, Attribute color)
    {
        Move(0, row);
        SetAttribute(color);
        AddStr(" " + label);
    }

    /// <summary>Draws <c>[ </c> and <c> ]</c> around <paramref name="field"/>, in accent while it has focus.</summary>
    private void DrawBrackets(FormTextField field)
    {
        var frame = field.Frame;
        SetAttribute(field.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : _theme.On(_theme.Foreground));
        Move(frame.X - 2, frame.Y);
        AddStr("[ ");
        Move(frame.X + frame.Width, frame.Y);
        AddStr(" ]");
    }

    private void Show(InstallOptions install, UpdatesOptions updates)
    {
        _scope.SelectedIndex = IndexOf(ScopeValues, install.InstallationScope);
        _architecture.SelectedIndex = IndexOf(ArchitectureValues, install.Architecture);
        _version.Text = install.Version;
        _interactive.IsChecked = install.InteractiveInstallation;
        _skipHashCheck.IsChecked = install.SkipHashCheck;
        _preRelease.IsChecked = install.PreRelease;
        _runAsAdministrator.IsChecked = install.RunAsAdministrator;
        _removeData.IsChecked = install.RemoveDataOnUninstall;
        _installArguments.Text = FormatArguments(install.CustomParameters_Install);
        _updateArguments.Text = FormatArguments(install.CustomParameters_Update);
        _uninstallArguments.Text = FormatArguments(install.CustomParameters_Uninstall);
        _location.Text = install.CustomInstallLocation;
        _preCommand.Text = install.PreInstallCommand;
        _postCommand.Text = install.PostInstallCommand;
        _kill.Text = string.Join(", ", install.KillBeforeOperation);
        _autoUpdate.IsChecked = install.AutoUpdatePackage;
        _ignoreAll.IsChecked = updates.UpdatesIgnored;
        _ignoredVersion.Text = updates.IgnoredVersion;
        SetNeedsDraw();
    }

    private (InstallOptions Install, UpdatesOptions Updates) Build()
    {
        var install = Clone(_base);
        install.InstallationScope = ValueOf(_scope, ScopeValues, _base.InstallationScope);
        install.Architecture = ValueOf(_architecture, ArchitectureValues, _base.Architecture);
        install.Version = _version.Text.Trim();
        install.InteractiveInstallation = _interactive.IsChecked;
        install.SkipHashCheck = _skipHashCheck.IsChecked;
        install.PreRelease = _preRelease.IsChecked;
        install.RunAsAdministrator = _runAsAdministrator.IsChecked;
        install.RemoveDataOnUninstall = _removeData.IsChecked;
        install.CustomParameters_Install = ListFrom(
            _installArguments, _base.CustomParameters_Install, FormatArguments(_base.CustomParameters_Install), ParseArguments);
        install.CustomParameters_Update = ListFrom(
            _updateArguments, _base.CustomParameters_Update, FormatArguments(_base.CustomParameters_Update), ParseArguments);
        install.CustomParameters_Uninstall = ListFrom(
            _uninstallArguments, _base.CustomParameters_Uninstall, FormatArguments(_base.CustomParameters_Uninstall), ParseArguments);
        install.CustomInstallLocation = _location.Text.Trim();
        install.PreInstallCommand = _preCommand.Text.Trim();
        install.PostInstallCommand = _postCommand.Text.Trim();
        install.KillBeforeOperation = ListFrom(
            _kill, _base.KillBeforeOperation, string.Join(", ", _base.KillBeforeOperation), ParseProcessNames);
        install.AutoUpdatePackage = _autoUpdate.IsChecked;

        var updates = new UpdatesOptions
        {
            UpdatesIgnored = _ignoreAll.IsChecked,
            IgnoredVersion = _ignoredVersion.Text.Trim(),
        };
        return (install, updates);
    }

    private bool HasChanges()
    {
        var (install, updates) = Build();
        return !install.Equals(_savedInstall) || !updates.Equals(_savedUpdates);
    }

    private void Save()
    {
        var (install, updates) = Build();
        if (_shell.SaveOptions(_row.Id, install, updates))
        {
            Close();
        }
    }

    private void Cancel()
    {
        if (HasChanges())
        {
            _shell.AskConfirm($"Discard changes to {_row.Id}? (y/n)", Close);
        }
        else
        {
            Close();
        }
    }

    /// <summary>Clears every field, and the options the form has no field for once saved, since reset means nothing customized.</summary>
    private void Reset()
    {
        _base = new InstallOptions();
        Show(_base, new UpdatesOptions());
    }

    /// <summary>What Space does in the focused field, for a click on <c>␣ Toggle</c>.</summary>
    private void ToggleFocused()
    {
        foreach (var field in _fields)
        {
            if (!field.HasFocus)
            {
                continue;
            }

            if (field is CheckField check)
            {
                check.Toggle();
            }
            else if (field is OptionRow options)
            {
                options.PickHighlighted();
            }
        }
    }
}
