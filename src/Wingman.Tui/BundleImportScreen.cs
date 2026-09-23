using System.Text.Json;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Reads a <c>.ubundle</c> and runs what it asks for, in place of a tab's content, laid out as the
/// mockup's import screen. It first asks for the file; Enter reads it and shows the plan from
/// <see cref="BundleImportPlanner"/>, one row per package with install, upgrade, or keep, and
/// packages from other managers shown but unselectable. Enter then stores the checked packages'
/// options when asked to and runs their installs and upgrades as one batch on the tab that opened
/// the screen. Esc cancels either step.
/// </summary>
internal sealed class BundleImportScreen : FormView, IThemedView
{
    private const string Heading = " Import bundle";
    private const string FileLabel = " File  ";
    private const string ApplyOptionsLabel = "Apply the bundle's install options to package-options.json";
    private const string ApplyPinsText = "[x] Apply holds as winget pins";
    private const string ApplyPinsNote = "  bundles carry no holds, so there is nothing to pin";
    private const string ReadHint = " ⏎ reads the bundle and shows its plan; nothing runs until you confirm it.";

    private const int TitleRow = 0;
    private const int FileRow = 2;
    private const int ReadHintRow = 4;
    private const int TableRow = 2;
    private const int PlanIndent = 7;

    // The blank row, the plan summary, the two options, and the elevation line under the table.
    private const int RowsBelowTable = 5;

    private static readonly CheckColumn[] Columns =
    [
        new("Name", 20),
        new("Id", 25),
        new("Bundle", 9),
        new("Installed", 10),
        new("Plan", 0),
    ];

    private readonly Shell _shell;
    private readonly ScreenHostTab _host;
    private readonly FormTextField _file;
    private readonly CheckTable _table;
    private readonly CheckField _applyOptions;
    private readonly KeyHint[] _fileHints;
    private readonly KeyHint[] _planHints;

    private Theme _theme;
    private View[] _fields;
    private Bundle? _bundle;
    private string _fileName = "";
    private IReadOnlyList<ImportPlanRow> _plan = [];
    private bool _hasChanges;

    public BundleImportScreen(Shell shell, ScreenHostTab host)
    {
        _shell = shell;
        _host = host;
        _theme = shell.Theme;

        _file = CreateTextField(_theme);
        _file.X = DisplayWidth.Of(FileLabel) + 2;
        _file.Y = FileRow;
        _file.Width = Dim.Fill(3);
        _file.Text = shell.LastBundlePath ?? DefaultFolder();
        _file.TextChanged += (_, _) => OnChanged();

        _table = new CheckTable(_theme, Columns)
        {
            X = 0,
            Y = TableRow,
            Width = Dim.Fill(),
            Height = Dim.Fill(RowsBelowTable),
            Visible = false,
        };
        _table.Toggled += OnChanged;

        _applyOptions = new CheckField(_theme, ApplyOptionsLabel)
        {
            X = PlanIndent,
            Y = Pos.AnchorEnd(3),
            IsChecked = true,
            Visible = false,
        };
        _applyOptions.Toggled += OnChanged;

        _fields = [_file];
        _fileHints =
        [
            new(Key.Enter, "Read", Read, "⏎"),
            new(Key.Esc, "Cancel", Close),
        ];
        _planHints =
        [
            new(Key.Enter, "Run plan", Run, "⏎"),
            new(Key.Space, "Toggle", ToggleFocused, "␣"),
            new(Key.O, "View options", ShowCursorRowOptions),
            new(Key.A, "All", () => CheckAll(true)),
            new(Key.N, "None", () => CheckAll(false)),
            new(Key.Esc, "Cancel", Close),
        ];

        Add(_file, _table, _applyOptions);
        shell.InstalledChanged += OnInstalledChanged;
    }

    public override IReadOnlyList<KeyHint> Hints => IsPlanShown ? _planHints : _fileHints;

    public override HelpGroup Help { get; } = new("Import bundle",
    [
        new("⏎", "read the file, then run the plan"),
        new("␣", "toggle"),
        new("o", "view the row's options"),
        new("a", "check all"),
        new("n", "check none"),
        new("Tab", "next field"),
        new("Esc", "cancel"),
    ]);

    public override bool HasUnsavedChanges => _hasChanges;

    protected override IReadOnlyList<View> Fields => _fields;

    private bool IsPlanShown => _bundle is not null;

    public void ApplyTheme(Theme theme) => _theme = theme;

    /// <summary>
    /// <c>scope=machine, skipHash, args=--silent</c>: the options that differ from the defaults, or
    /// <c>default options</c> when none do.
    /// </summary>
    internal static string Describe(InstallOptions options)
    {
        var parts = new List<string>();
        void AddText(string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add($"{name}={value}");
            }
        }

        void AddList(string name, IReadOnlyList<string> values)
        {
            if (values.Count > 0)
            {
                parts.Add($"{name}={InstallOptionsEditor.FormatArguments(values)}");
            }
        }

        AddText("scope", options.InstallationScope);
        AddText("arch", options.Architecture);
        AddText("version", options.Version);
        if (options.InteractiveInstallation)
        {
            parts.Add("interactive");
        }

        if (options.SkipHashCheck)
        {
            parts.Add("skipHash");
        }

        if (options.RunAsAdministrator)
        {
            parts.Add("admin");
        }

        if (options.PreRelease)
        {
            parts.Add("preRelease");
        }

        if (options.AutoUpdatePackage)
        {
            parts.Add("autoUpdate");
        }

        AddList("args", options.CustomParameters_Install);
        AddList("updateArgs", options.CustomParameters_Update);
        AddText("location", options.CustomInstallLocation);
        AddText("preInstall", options.PreInstallCommand);
        AddText("postInstall", options.PostInstallCommand);
        AddText("preUpdate", options.PreUpdateCommand);
        AddText("postUpdate", options.PostUpdateCommand);
        AddList("kill", options.KillBeforeOperation);
        return parts.Count == 0 ? "default options" : string.Join(", ", parts);
    }

    protected override bool HandleFormKey(Key key)
    {
        if (key == Key.Enter)
        {
            if (IsPlanShown)
            {
                Run();
            }
            else
            {
                Read();
            }

            return true;
        }

        if (key == Key.Esc)
        {
            Close();
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
        var width = Viewport.Width;
        var dim = _theme.On(_theme.Dim);

        Move(0, TitleRow);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(Heading);
        SetAttribute(dim);
        var subtitle = IsPlanShown ? $"  {_fileName} · {_plan.Count} packages" : "  choose a .ubundle file";
        AddStr(CellText.Fit(subtitle, Math.Max(0, width - DisplayWidth.Of(Heading) - 1)));

        if (IsPlanShown)
        {
            DrawPlanFooter(width, Viewport.Height);
        }
        else
        {
            DrawFileRow();
            Move(0, ReadHintRow);
            SetAttribute(dim);
            AddStr(CellText.Fit(ReadHint, Math.Max(0, width - 1)));
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shell.InstalledChanged -= OnInstalledChanged;
        }

        base.Dispose(disposing);
    }

    /// <summary>The <c>WINGMAN_DATA_DIR</c> folder when that is set, the Documents folder otherwise, ending in a separator so a file name can be typed after it.</summary>
    private static string DefaultFolder()
    {
        var folder = WingmanApp.DataDirectoryOverride() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
    }

    private static bool IsActionable(ImportPlanRow row) => row.Action is ImportAction.Install or ImportAction.Upgrade;

    private static bool IsSelectable(ImportPlanRow row) => row.Action is not (ImportAction.Incompatible or ImportAction.Skip);

    private void OnChanged()
    {
        _hasChanges = true;
        SetNeedsDraw();
    }

    private void Read()
    {
        var text = _file.Text.Trim();
        Bundle bundle;
        string path;
        try
        {
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(text));
            bundle = BundleSerializer.Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            _shell.SetError($"Could not read bundle: {ex.Message}");
            return;
        }

        // A bundle written by hand can say null where UniGetUI writes an empty list.
        bundle.Packages ??= [];
        bundle.IncompatiblePackages ??= [];

        _bundle = bundle;
        _fileName = Path.GetFileName(path);
        _shell.LastBundlePath = path;
        _hasChanges = true;

        _file.Visible = false;
        _table.Visible = true;
        _applyOptions.Visible = true;
        _fields = [_table, _applyOptions];
        _table.SetFocus();
        Plan(keepChecks: false);

        // The plan needs the installed set; when the Installed tab has not loaded it yet, it is planned again once it has.
        _shell.EnsureInstalledLoaded();
        _shell.RefreshHints(_host);
    }

    private void OnInstalledChanged()
    {
        if (IsPlanShown)
        {
            Plan(keepChecks: true);
        }
    }

    /// <summary>Plans the bundle against the installed set, whose rows with an available version are the upgrades, checking every selectable row unless <paramref name="keepChecks"/>.</summary>
    private void Plan(bool keepChecks)
    {
        var installed = _shell.InstalledRows;
        var upgrades = new List<PackageRow>();
        foreach (var row in installed)
        {
            if (!string.IsNullOrEmpty(row.AvailableVersion))
            {
                upgrades.Add(row);
            }
        }

        var wasChecked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; keepChecks && i < _plan.Count; i++)
        {
            wasChecked[_plan[i].Package.Id] = _table.IsChecked(i);
        }

        _plan = BundleImportPlanner.Plan(_bundle!, installed, upgrades);
        var rows = new List<CheckRow>();
        foreach (var row in _plan)
        {
            rows.Add(new CheckRow(IsSelectable(row), CellsFor(row)));
        }

        _table.SetRows(rows, i => wasChecked.GetValueOrDefault(_plan[i].Package.Id, true));
        SetNeedsDraw();
    }

    private IReadOnlyList<IReadOnlyList<CellSpan>> CellsFor(ImportPlanRow row)
    {
        var installed = row.InstalledVersion.Length == 0 ? "—" : row.InstalledVersion;
        return
        [
            [new CellSpan(row.Package.Name)],
            [new CellSpan(row.Package.Id)],
            [new CellSpan(row.Package.Version)],
            [new CellSpan(installed)],
            PlanSpans(row),
        ];
    }

    /// <summary><c>install</c>, <c>upgrade</c>, or <c>keep</c>, then <c>⚙ options</c> and <c>post-cmd</c> when the bundle sets them; <c>⊘ Scoop package</c> or <c>⊘ incompatible</c> for a row that cannot run.</summary>
    private static List<CellSpan> PlanSpans(ImportPlanRow row)
    {
        var package = row.Package;
        switch (row.Action)
        {
            case ImportAction.Incompatible:
                var reason = package.ManagerName.Length == 0 ? "incompatible" : $"{package.ManagerName} package";
                return [new CellSpan($"⊘ {reason}", Tone.Dim)];

            case ImportAction.Skip:
                return [new CellSpan("skip", Tone.Dim)];
        }

        var spans = row.Action switch
        {
            ImportAction.Install => new List<CellSpan> { new("install", Tone.Ok) },
            ImportAction.Upgrade => [new("upgrade", Tone.Info)],
            _ => [new("keep", Tone.Dim)],
        };

        if (package.InstallationOptions is { } options)
        {
            spans.Add(new CellSpan("  ⚙ options", Tone.Accent));
            var hasPostCommand = options.PostInstallCommand.Length > 0 || options.PostUpdateCommand.Length > 0;
            if (hasPostCommand)
            {
                spans.Add(new CellSpan("  post-cmd", Tone.Dim));
            }
        }

        return spans;
    }

    private void CheckAll(bool isChecked)
    {
        _table.CheckWhere(_ => isChecked);
        OnChanged();
    }

    private void ToggleFocused()
    {
        if (_table.HasFocus)
        {
            _table.ToggleCursorRow();
        }
        else if (_applyOptions.HasFocus)
        {
            _applyOptions.Toggle();
        }
    }

    /// <summary>Puts the cursor row's install options from the bundle on the message line, since the plan has no room for them.</summary>
    private void ShowCursorRowOptions()
    {
        if (_table.CursorIndex < 0)
        {
            return;
        }

        var package = _plan[_table.CursorIndex].Package;
        var text = package.InstallationOptions is { } options
            ? $"{package.Id}: {Describe(options)}"
            : $"{package.Id} has no install options in the bundle";
        _shell.SetStatus(text);
    }

    /// <summary>The checked rows the plan installs or upgrades, as the batch will run them: with the bundle's options when they are to be applied, the stored ones otherwise.</summary>
    private List<QueuedOperation> BuildOperations()
    {
        var operations = new List<QueuedOperation>();
        for (var i = 0; i < _plan.Count; i++)
        {
            var planRow = _plan[i];
            if (!_table.IsChecked(i) || !IsActionable(planRow))
            {
                continue;
            }

            var package = planRow.Package;
            var options = _applyOptions.IsChecked && package.InstallationOptions is { } bundled
                ? bundled
                : _shell.Options.GetInstallOptions(package.Id);

            if (planRow.Action == ImportAction.Install)
            {
                var row = new PackageRow(package.Name, package.Id, package.Version, null, "winget");
                var plan = OperationRequestFactory.Create(OperationKind.Install, row, _shell.Settings, options);
                operations.Add(new QueuedOperation(OperationKind.Install, row, plan));
                continue;
            }

            var installed = _shell.FindInstalled(package.Id) ?? new PackageRow(package.Name, package.Id, planRow.InstalledVersion, null, "winget");
            var upgradePlan = OperationRequestFactory.Create(OperationKind.Upgrade, installed, _shell.Settings, options);

            // Without an available version winget knows of, the upgrade targets the bundle's newer one.
            if (string.IsNullOrEmpty(installed.AvailableVersion))
            {
                upgradePlan = upgradePlan with { Request = upgradePlan.Request with { Version = package.Version } };
            }

            operations.Add(new QueuedOperation(OperationKind.Upgrade, installed, upgradePlan));
        }

        return operations;
    }

    private List<BundlePackage> CheckedPackagesWithOptions()
    {
        var packages = new List<BundlePackage>();
        for (var i = 0; i < _plan.Count; i++)
        {
            var package = _plan[i].Package;
            var hasOptions = package.InstallationOptions is not null || package.Updates is not null;
            if (_table.IsChecked(i) && hasOptions)
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    private void Run()
    {
        if (_shell.IsBatchRunning)
        {
            _shell.SetStatus(Shell.BatchRunningText);
            return;
        }

        var operationCount = BuildOperations().Count;
        var optionCount = _applyOptions.IsChecked ? CheckedPackagesWithOptions().Count : 0;
        if (operationCount == 0 && optionCount == 0)
        {
            _shell.SetStatus(Shell.NothingToRunText);
            return;
        }

        var question = operationCount == 0
            ? $"Apply the options of {optionCount} packages from {_fileName}? (y/n)"
            : $"Run {operationCount} {(operationCount == 1 ? "operation" : "operations")} from {_fileName}? (y/n)";
        _shell.AskConfirm(question, RunConfirmed);
    }

    /// <summary>Stores the options first, so the operations built afterward and any retry use them too.</summary>
    private void RunConfirmed()
    {
        if (_applyOptions.IsChecked && !_shell.ImportOptions(CheckedPackagesWithOptions()))
        {
            return;
        }

        var operations = BuildOperations();
        Close();
        if (operations.Count == 0)
        {
            _shell.SetStatus($"Applied the options from {_fileName}");
            return;
        }

        _shell.RunOperations(operations, _host);
    }

    /// <summary>
    /// The plan summary, <c>Plan  31 install · 4 upgrade · 19 already installed · 3 skipped</c>, the
    /// two options, and how many of the checked operations need elevation.
    /// </summary>
    private void DrawPlanFooter(int width, int height)
    {
        var summary = BundleImportPlanner.Summarize(_plan);
        var y = height - 4;
        Move(0, y);
        var used = 0;
        void Part(string text, Attribute color)
        {
            SetAttribute(color);
            var fitted = CellText.Fit(text, Math.Max(0, width - 1 - used));
            AddStr(fitted);
            used += DisplayWidth.Of(fitted);
        }

        var normal = _theme.On(_theme.Foreground);
        Part(" ", normal);
        Part("Plan", _theme.On(_theme.Foreground, TextStyle.Bold));
        Part("  ", normal);
        Part($"{summary.Install} install", _theme.On(_theme.Ok));
        Part(" · ", normal);
        Part($"{summary.Upgrade} upgrade", _theme.On(_theme.Info));
        Part($" · {summary.Keep} already installed · ", normal);
        Part($"{summary.Skip + summary.Incompatible} skipped", _theme.On(_theme.Dim));

        var dim = _theme.On(_theme.Dim);
        Move(PlanIndent, height - 2);
        SetAttribute(dim);
        AddStr(CellText.Fit(ApplyPinsText + ApplyPinsNote, Math.Max(0, width - PlanIndent - 1)));

        Move(PlanIndent, height - 1);
        AddStr(CellText.Fit(ElevationText(), Math.Max(0, width - PlanIndent - 1)));
    }

    /// <summary><c>2 of 35 operations need elevation. One UAC prompt will be shown.</c></summary>
    private string ElevationText()
    {
        var operations = BuildOperations();
        var elevated = operations.Count(operation => operation.Plan.RequiresElevation);
        if (operations.Count == 0)
        {
            return "No operations checked.";
        }

        if (elevated == 0)
        {
            return "No elevation needed.";
        }

        return $"{elevated} of {operations.Count} operations need elevation. One UAC prompt will be shown.";
    }

    /// <summary><c> File  [ C:\Users\ben\Documents\laptop.ubundle ]</c>, the brackets in accent while the box has focus.</summary>
    private void DrawFileRow()
    {
        Attribute brackets = _file.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : _theme.On(_theme.Foreground);
        Move(0, FileRow);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(FileLabel);
        SetAttribute(brackets);
        AddStr("[ ");
        Move(_file.Frame.X + _file.Frame.Width, FileRow);
        AddStr(" ]");
    }
}
