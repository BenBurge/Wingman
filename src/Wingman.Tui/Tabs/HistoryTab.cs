using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Tui.Tabs;

/// <summary>
/// Every operation and batch in <see cref="Shell.History"/>, newest first, loaded on a background
/// task the first time the tab is shown, again on <c>r</c>, and after every batch once it has
/// loaded. The right pane shows the cursor row's log. <c>R</c> runs the row's operation again as a
/// batch of one, <c>o</c> opens the package's install options, and Del forgets the entry; the batch
/// screen and the editor take the tab's content area as they do on the list tabs.
/// </summary>
internal sealed class HistoryTab : ScreenHostTab, IThemedView
{
    private const int WideLayoutWidth = 96;
    private const int WideLeftPaneWidth = 57;
    private const int NarrowLeftPanePercent = 60;

    // The left-pane column where m opens the menu on the cursor row, as on the list tabs.
    private const int MenuColumn = 24;

    private static readonly HelpGroup Help = new("History",
    [
        new("⏎", "open the log"),
        new("R", "retry the operation"),
        new("o", "install options"),
        new("Del", "forget the entry"),
        new("r", "reload"),
    ]);

    private readonly HistoryTable _table;
    private readonly Line _divider;
    private readonly HistoryDetailsPane _details;
    private readonly KeyHint[] _hints;

    private int _leftPaneWidth = WideLeftPaneWidth;
    private bool _hasStartedLoading;

    // Only the load with this number may show its rows; an older one finishing late is ignored.
    private int _loadNumber;

    public HistoryTab(Shell shell)
        : base(shell, "History")
    {
        _table = new HistoryTable(shell.Theme)
        {
            X = 0,
            Y = 0,
            Width = WideLeftPaneWidth,
            Height = Dim.Fill(),
        };

        // Starts one row above and ends one row below the tab so it meets the shell's separators.
        _divider = new Line
        {
            Orientation = Orientation.Vertical,
            X = WideLeftPaneWidth,
            Y = -1,
            Height = Dim.Fill(-1),
            SuperViewRendersLineCanvas = true,
            LineAttribute = shell.Theme.On(shell.Theme.Border),
        };

        _details = new HistoryDetailsPane(shell.Theme)
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanRetry = row => KindOf(row) is not null,
        };

        _table.CursorChanged += ShowDetails;
        _table.RowActivated += _ => _details.SetFocus();
        _table.RowMenuRequested += OpenContextMenu;
        shell.BatchFinished += OnBatchFinished;

        _hints =
        [
            new(Key.Enter, "Open log", () => _details.SetFocus(), "⏎"),
            new(new Key('R'), "Retry", RetryCursorRow, "R"),
            new(Key.O, "Options", OpenOptionsForCursorRow),
            new(new Key('/'), "Filter", _table.FocusFilter),
            new(Key.Delete, "Forget", ForgetCursorRow, "Del"),
            new(Key.R, "Reload", Reload, IsOnBar: false),
        ];

        Add(_table, _divider, _details);
    }

    protected override IReadOnlyList<KeyHint> TableHints => _hints;

    protected override HelpGroup TabHelp => Help;

    public override void OnShown()
    {
        FocusContent();
        if (!_hasStartedLoading)
        {
            Reload();
        }
    }

    public override void ShowContextMenu()
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        if (_table.CurrentRow is { } row && _table.CursorRowScreenPosition(MenuColumn) is { } position)
        {
            OpenContextMenu(row, position);
        }
    }

    public void ApplyTheme(Theme theme) => _divider.LineAttribute = theme.On(theme.Border);

    protected override void FocusTable() => _table.FocusTable();

    protected override void HideContent()
    {
        _table.Visible = false;
        _divider.Visible = false;
        _details.Visible = false;
    }

    protected override void ShowContent()
    {
        _table.Visible = true;
        _divider.Visible = true;
        _details.Visible = true;
        _table.FocusTable();
    }

    /// <summary>Sizes the panes from this tab's width, as the list tabs do.</summary>
    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        base.OnSubViewLayout(args);

        var tabWidth = Viewport.Width;
        var leftWidth = tabWidth >= WideLayoutWidth ? WideLeftPaneWidth : tabWidth * NarrowLeftPanePercent / 100;
        if (leftWidth != _leftPaneWidth)
        {
            _leftPaneWidth = leftWidth;
            _table.Width = leftWidth;
            _divider.X = leftWidth;
            _details.X = leftWidth + 1;
        }
    }

    /// <summary>Tab switches between the list and the log unless the batch screen or a form has the content area.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Tab)
        {
            if (!IsBatchShown && !IsFormShown)
            {
                SwitchPane();
            }

            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>What running the entry again means, or null for a batch, pin, or unpin entry, which has nothing to rerun.</summary>
    private static OperationKind? KindOf(HistoryRow row) => row.Entry.Operation switch
    {
        "install" => OperationKind.Install,
        "upgrade" => OperationKind.Upgrade,
        "uninstall" => OperationKind.Uninstall,
        _ => null,
    };

    private void SwitchPane()
    {
        if (_details.HasFocus)
        {
            _table.FocusTable();
        }
        else
        {
            _details.SetFocus();
        }
    }

    private void OnBatchFinished()
    {
        if (_hasStartedLoading)
        {
            Reload();
        }
    }

    private void Reload()
    {
        _hasStartedLoading = true;
        var loadNumber = ++_loadNumber;
        var history = Shell.History;
        var app = Shell.App;
        _ = Task.Run(() =>
        {
            try
            {
                var rows = HistoryRow.FromEntries(history, history.List());
                app.Invoke(() => ShowRows(loadNumber, rows));
            }
            catch (IOException ex)
            {
                app.Invoke(() => Shell.SetError($"Could not read history: {ex.Message}"));
            }
        });
    }

    private void ShowRows(int loadNumber, IReadOnlyList<HistoryRow> rows)
    {
        if (loadNumber == _loadNumber)
        {
            _table.SetRows(rows);
        }
    }

    private void ShowDetails(HistoryRow? row)
    {
        var log = "";
        if (row is not null)
        {
            try
            {
                log = Shell.History.ReadLog(row.Entry);
            }
            catch (IOException ex)
            {
                log = $"Could not read the log: {ex.Message}";
            }
        }

        _details.Show(row, log);
    }

    /// <summary>The installed row for the entry's package, or one with only its Id and name when it is not installed.</summary>
    private PackageRow PackageRowFor(HistoryRow row)
    {
        var entry = row.Entry;
        return Shell.FindInstalled(entry.PackageId) ?? new PackageRow(entry.PackageName, entry.PackageId, "", null, "");
    }

    private void RetryCursorRow()
    {
        if (_table.CurrentRow is { } row)
        {
            Retry(row);
        }
    }

    private void Retry(HistoryRow row)
    {
        if (KindOf(row) is not { } kind)
        {
            Shell.SetStatus($"Nothing to retry for a {row.Entry.Operation} entry");
            return;
        }

        if (Shell.IsBatchRunning)
        {
            Shell.SetStatus(Shell.BatchRunningText);
            return;
        }

        // Built when the question is answered, so the batch uses the package's options as they are then.
        var packageRow = PackageRowFor(row);
        Shell.AskConfirm(
            $"Retry {row.Entry.Operation} {packageRow.Id}? (y/n)",
            () => Shell.RunOperation(Shell.BuildOperation(kind, packageRow), this));
    }

    private void OpenOptionsForCursorRow()
    {
        if (_table.CurrentRow is { } row)
        {
            OpenOptionsFor(row);
        }
    }

    private void OpenOptionsFor(HistoryRow row)
    {
        if (row.IsBatch)
        {
            Shell.SetStatus("A batch entry has no package options");
            return;
        }

        OpenOptions(PackageRowFor(row));
    }

    private void ForgetCursorRow()
    {
        if (_table.CurrentRow is { } row)
        {
            Forget(row);
        }
    }

    private void Forget(HistoryRow row)
    {
        var entry = row.Entry;
        Shell.AskConfirm($"Forget {entry.Operation} {row.Package}? (y/n)", () =>
        {
            try
            {
                Shell.History.Delete(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Shell.SetError($"Could not forget {row.Package}: {ex.Message}");
                return;
            }

            Shell.SetStatus($"Forgot {entry.Operation} {row.Package}");
            Reload();
        });
    }

    private void OpenContextMenu(HistoryRow row, Point screenPosition)
    {
        var entries = new List<MenuEntry>();
        if (KindOf(row) is not null)
        {
            entries.Add(new("Retry", () => Retry(row)));
        }

        if (!row.IsBatch)
        {
            entries.Add(new("Install options…", () => OpenOptionsFor(row)));
        }

        entries.Add(new("Forget", () => Forget(row)));
        if (!row.IsBatch)
        {
            entries.Add(MenuEntry.Rule);
            entries.Add(new("Copy id", () => Shell.CopyId(row.Entry.PackageId)));
        }

        Shell.ShowContextMenu(row.Package, entries, screenPosition);
    }
}
