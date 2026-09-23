using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Writes the installed packages as a UniGetUI-compatible <c>.ubundle</c>, in place of a tab's
/// content, laid out as the mockup's export screen: what to include, a checklist of the installed
/// set with every package winget can reinstall checked, a count, and the file to write. Packages
/// winget cannot reinstall cannot be checked and always go under <c>incompatible_packages</c>.
/// Enter writes the file through <see cref="BundleExporter"/>, and Esc cancels.
/// </summary>
internal sealed class BundleExportScreen : FormView, IThemedView
{
    private const string Heading = " Export bundle";
    private const string Subtitle = "  UniGetUI-compatible .ubundle · export_version 3";
    private const string IncludeOptionsLabel = "Include per-package install options";
    private const string IncludeUpdatesLabel = "Include holds and ignored versions";
    private const string MarkedOnlyLabel = "Only marked rows";
    private const string FileLabel = " File  ";
    private const string CheckGap = "   ";

    private const int TitleRow = 0;
    private const int IncludeRow = 2;
    private const int MarkedOnlyRow = 3;
    private const int TableRow = 5;

    // The blank row, the count, and the file row under the table.
    private const int RowsBelowTable = 3;

    private static readonly CheckColumn[] Columns =
    [
        new("Name", 21),
        new("Id", 27),
        new("Version", 10),
        new("Notes", 0),
    ];

    // No BOM, as UniGetUI writes its bundles.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Shell _shell;
    private readonly CheckField _includeOptions;
    private readonly CheckField _includeUpdates;
    private readonly CheckField _markedOnly;
    private readonly CheckTable _table;
    private readonly FormTextField _file;
    private readonly View[] _fields;
    private readonly KeyHint[] _hints;

    private Theme _theme;
    private IReadOnlyList<PackageRow> _rows = [];
    private bool _hasChanges;

    public BundleExportScreen(Shell shell)
    {
        _shell = shell;
        _theme = shell.Theme;

        _includeOptions = new CheckField(_theme, IncludeOptionsLabel) { X = 1, Y = IncludeRow, IsChecked = true };
        _includeUpdates = new CheckField(_theme, IncludeUpdatesLabel)
        {
            X = 1 + CheckField.WidthFor(IncludeOptionsLabel) + CheckGap.Length,
            Y = IncludeRow,
            IsChecked = true,
        };
        _markedOnly = new CheckField(_theme, MarkedOnlyLabel) { X = 1, Y = MarkedOnlyRow };
        _includeOptions.Toggled += OnChanged;
        _includeUpdates.Toggled += OnChanged;
        _markedOnly.Toggled += ApplyMarkedOnly;

        _table = new CheckTable(_theme, Columns)
        {
            X = 0,
            Y = TableRow,
            Width = Dim.Fill(),
            Height = Dim.Fill(RowsBelowTable),
        };
        _table.Toggled += OnChanged;

        _file = CreateTextField(_theme);
        _file.X = DisplayWidth.Of(FileLabel) + 2;
        _file.Y = Pos.AnchorEnd(1);
        _file.Width = Dim.Fill(3);
        _file.Text = DefaultPath();
        _file.TextChanged += (_, _) => OnChanged();

        _fields = [_includeOptions, _includeUpdates, _markedOnly, _table, _file];
        _hints =
        [
            new(Key.Enter, "Export", Export, "⏎"),
            new(Key.Space, "Toggle", ToggleFocused, "␣"),
            new(Key.A, "All", CheckAll),
            new(Key.N, "None", CheckNone),
            new(Key.M, "Marked only", () => _markedOnly.Toggle()),
            new(Key.Esc, "Cancel", Close),
        ];

        Add(_fields);
        LoadRows();

        // The Installed tab may not have loaded yet when this opens from another tab; the rows fill in when it does.
        shell.InstalledChanged += LoadRows;
        shell.EnsureInstalledLoaded();
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override HelpGroup Help { get; } = new("Export bundle",
    [
        new("⏎", "export"),
        new("␣", "toggle"),
        new("a", "check all"),
        new("n", "check none"),
        new("m", "only marked rows"),
        new("Tab", "next field"),
        new("Esc", "cancel"),
    ]);

    public override bool HasUnsavedChanges => _hasChanges;

    protected override IReadOnlyList<View> Fields => _fields;

    public void ApplyTheme(Theme theme) => _theme = theme;

    /// <summary>The list has focus first, since picking packages is what the screen is for.</summary>
    public override void FocusFirstField() => _table.SetFocus();

    protected override bool HandleFormKey(Key key)
    {
        if (key == Key.Enter)
        {
            Export();
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
        var height = Viewport.Height;
        var dim = _theme.On(_theme.Dim);

        Move(0, TitleRow);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(Heading);
        SetAttribute(dim);
        AddStr(CellText.Fit(Subtitle, Math.Max(0, width - DisplayWidth.Of(Heading) - 1)));

        DrawCount(height - 2, width);
        DrawFileRow(height - 1);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shell.InstalledChanged -= LoadRows;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// <c>Documents\DESKTOP-2026-09-22.ubundle</c>, or the same name in the <c>WINGMAN_DATA_DIR</c>
    /// folder when that is set, so a harness run never writes into the real Documents.
    /// </summary>
    private static string DefaultPath()
    {
        var folder = WingmanApp.DataDirectoryOverride() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(folder, $"{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}.ubundle");
    }

    private void OnChanged()
    {
        _hasChanges = true;
        SetNeedsDraw();
    }

    /// <summary>Lists the installed set again, keeping each package's check when it was listed before and checking new ones as the defaults would.</summary>
    private void LoadRows()
    {
        var wasChecked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _rows.Count; i++)
        {
            wasChecked[_rows[i].Id] = _table.IsChecked(i);
        }

        _rows = [.. _shell.InstalledRows];
        var tableRows = new List<CheckRow>();
        foreach (var row in _rows)
        {
            tableRows.Add(new CheckRow(BundleExporter.IsCompatible(row), CellsFor(row)));
        }

        _table.SetRows(tableRows, i => wasChecked.TryGetValue(_rows[i].Id, out var isChecked) ? isChecked : IsCheckedByDefault(i));
        SetNeedsDraw();
    }

    private bool IsCheckedByDefault(int index) => !_markedOnly.IsChecked || _shell.Queue.Contains(_rows[index].Id);

    private IReadOnlyList<IReadOnlyList<CellSpan>> CellsFor(PackageRow row) =>
    [
        [new CellSpan(row.Name)],
        [new CellSpan(row.Id)],
        [new CellSpan(row.Version)],
        NotesFor(row),
    ];

    /// <summary><c>custom options</c> or <c>auto-update</c> in accent and <c>held</c> dim, or <c>skipped · not from winget</c> dim for a row winget cannot reinstall.</summary>
    private List<CellSpan> NotesFor(PackageRow row)
    {
        if (!BundleExporter.IsCompatible(row))
        {
            return [new CellSpan("skipped · not from winget", Tone.Dim)];
        }

        var notes = new List<CellSpan>();
        if (_shell.Options.GetInstallOptions(row.Id).AutoUpdatePackage)
        {
            notes.Add(new CellSpan("auto-update", Tone.Accent));
        }
        else if (_shell.Options.HasCustomInstallOptions(row.Id))
        {
            notes.Add(new CellSpan("custom options", Tone.Accent));
        }

        if (_shell.IsPinned(row.Id))
        {
            if (notes.Count > 0)
            {
                notes.Add(new CellSpan(" · ", Tone.Dim));
            }

            notes.Add(new CellSpan("held", Tone.Dim));
        }

        return notes;
    }

    private void CheckAll()
    {
        _markedOnly.IsChecked = false;
        _table.CheckWhere(_ => true);
        OnChanged();
    }

    private void CheckNone()
    {
        _markedOnly.IsChecked = false;
        _table.CheckWhere(_ => false);
        OnChanged();
    }

    /// <summary>Checks only the rows in the batch queue while <c>Only marked rows</c> is on, and every row once it is off.</summary>
    private void ApplyMarkedOnly()
    {
        _table.CheckWhere(IsCheckedByDefault);
        OnChanged();
    }

    /// <summary>What Space does in the focused field, for a click on <c>␣ Toggle</c>.</summary>
    private void ToggleFocused()
    {
        if (_table.HasFocus)
        {
            _table.ToggleCursorRow();
        }
        else if (Focused is CheckField check)
        {
            check.Toggle();
        }
    }

    private void Export()
    {
        var text = _file.Text.Trim();
        if (text.Length == 0)
        {
            _shell.SetStatus("Type the file to export to");
            _file.SetFocus();
            return;
        }

        var isChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_table.IsChecked(i))
            {
                isChecked.Add(_rows[i].Id);
            }
        }

        // Rows winget cannot reinstall are always passed on, so the bundle lists them under incompatible_packages.
        var bundle = BundleExporter.Build(
            _rows,
            _shell.Options,
            row => !BundleExporter.IsCompatible(row) || isChecked.Contains(row.Id),
            includeOptions: _includeOptions.IsChecked,
            includeUpdatesOptions: _includeUpdates.IsChecked);

        string path;
        try
        {
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(text));
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, BundleSerializer.Write(bundle), Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _shell.SetError($"Could not write the bundle: {ex.Message}");
            return;
        }

        // The message line wraps a path with no spaces out of sight, so a long one keeps its file name and loses its front.
        var message = $"Exported {bundle.Packages.Count} packages to ";
        var room = Math.Max(1, Viewport.Width - 2 - DisplayWidth.Of(message));
        _shell.LastBundlePath = path;
        Close();
        _shell.SetStatus(message + CellText.FitKeepingEnd(path, room));
    }

    /// <summary><c> 137 of 142 selected · 5 skipped: winget cannot reinstall them, listed under incompatible_packages</c>.</summary>
    private void DrawCount(int y, int width)
    {
        var selected = 0;
        var skipped = 0;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (!BundleExporter.IsCompatible(_rows[i]))
            {
                skipped++;
            }
            else if (_table.IsChecked(i))
            {
                selected++;
            }
        }

        var count = $" {selected} of {_rows.Count} selected";
        Move(0, y);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(CellText.Fit(count, width));
        if (skipped == 0)
        {
            return;
        }

        var used = DisplayWidth.Of(count);
        AddStr(CellText.Fit(" · ", Math.Max(0, width - used)));
        used += 3;
        SetAttribute(_theme.On(_theme.Dim));
        var note = $"{skipped} skipped: winget cannot reinstall them, listed under incompatible_packages";
        AddStr(CellText.Fit(note, Math.Max(0, width - used - 1)));
    }

    /// <summary><c> File  [ C:\Users\ben\Documents\desktop-2026-09-22.ubundle ]</c>, the brackets in accent while the box has focus.</summary>
    private void DrawFileRow(int y)
    {
        Attribute brackets = _file.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : _theme.On(_theme.Foreground);
        Move(0, y);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(FileLabel);
        SetAttribute(brackets);
        AddStr("[ ");
        Move(_file.Frame.X + _file.Frame.Width, y);
        AddStr(" ]");
    }
}
