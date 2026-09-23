using System.Drawing;
using System.Globalization;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// A tab with a <see cref="PackageTable"/> on the left and a <see cref="DetailsPane"/> for the
/// cursor row on the right, filled by a background load. Installed, Discover, and Updates are
/// these; they differ in their columns, what they load, and their keys. While the batch queue has
/// entries a <see cref="QueuePane"/> takes the details pane's place. An operation started from
/// the tab swaps either for a <see cref="LogPane"/> until it is over and dismissed.
/// </summary>
internal abstract class PackageListTab : ShellTab
{
    private const int WideLayoutWidth = 96;
    private const int WideLeftPaneWidth = 57;
    private const int NarrowLeftPanePercent = 60;

    // The left-pane column where m opens the menu on the cursor row, as the mockup draws it.
    private const int MenuColumn = 24;

    private static readonly HelpGroup LogHelp = new("Log",
    [
        new("Esc", "cancel"),
        new("↑↓", "scroll log"),
        new("⏎", "back"),
    ]);

    private readonly Line _divider;
    private readonly QueuePane _queuePane;
    private readonly KeyHint[] _runningHints;
    private readonly KeyHint[] _finishedHints;

    // Only the load this points at may touch the UI; an older one finishing late is ignored.
    private CancellationTokenSource? _loadCancellation;
    private int _leftPaneWidth = WideLeftPaneWidth;

    // True while a load's rows go into the table, so the cursor moving onto a surviving row is
    // not taken for the user moving it.
    private bool _isShowingRows;

    // Whether the key bar was last given the hints for a pinned cursor row.
    private bool _hintsAreForPinnedRow;

    protected PackageListTab(Shell shell, IWingetClient client, string title, IReadOnlyList<PackageColumn> columns)
        : base(title)
    {
        Shell = shell;
        Client = client;
        CanFocus = true;

        Table = new PackageTable(shell.Theme, columns)
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

        Details = new DetailsPane(shell.Theme, shell.App, client, shell.DetailsCache)
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            PinFor = shell.FindPin,
        };

        _queuePane = new QueuePane(shell.Theme, shell.Queue)
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
        };
        _queuePane.RunRequested += shell.RunQueue;
        _queuePane.ClearRequested += shell.ClearQueue;

        Log = new LogPane(shell.Theme)
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
        };
        Log.CancelRequested += AskCancel;
        Log.BackRequested += CloseLog;

        _runningHints =
        [
            new(Key.Esc, "Cancel", AskCancel),
            new(Key.CursorUp, "Scroll log", FocusLog, "↑↓"),
            new(Key.Tab, "Pane", SwitchPane),
        ];
        _finishedHints =
        [
            new(Key.Enter, "Back", CloseLog, "⏎"),
            new(Key.CursorUp, "Scroll log", FocusLog, "↑↓"),
        ];

        Table.IsMarked = row => shell.Queue.Contains(row.Id);
        Table.MarkedScheme = shell.Theme.CellScheme(shell.Theme.Accent);

        MarkHint = new(Key.Space, "Mark", MarkCursorRow, "␣");
        ClearHint = new(Key.C, "Clear", shell.ClearQueue);
        RunHint = new(Key.G, "Run", shell.RunQueue);

        Table.CursorChanged += OnCursorChanged;
        Table.RowActivated += _ => OnRowActivated();
        Table.RowMenuRequested += OpenContextMenu;
        shell.PinsChanged += OnPinsChanged;
        shell.Queue.Changed += OnQueueChanged;

        Add(Table, _divider, Details, _queuePane, Log);
    }

    public sealed override IReadOnlyList<KeyHint> Hints
    {
        get
        {
            if (!IsLogShown)
            {
                return TableHints;
            }

            return Log.IsRunning ? _runningHints : _finishedHints;
        }
    }

    public override bool ShowsHelpHint => !IsLogShown;

    public sealed override IReadOnlyList<HelpGroup> HelpGroups => IsLogShown ? [TabHelp, LogHelp] : [TabHelp];

    public override void ShowContextMenu()
    {
        if (Table.CurrentRow is { } row && Table.CursorRowScreenPosition(MenuColumn) is { } position)
        {
            OpenContextMenu(row, position);
        }
    }

    protected Shell Shell { get; }

    protected IWingetClient Client { get; }

    protected PackageTable Table { get; }

    protected DetailsPane Details { get; }

    protected LogPane Log { get; }

    /// <summary><c>␣ Mark</c>, which toggles the cursor row in the batch queue.</summary>
    protected KeyHint MarkHint { get; }

    /// <summary><c>c Clear</c>, which empties the batch queue.</summary>
    protected KeyHint ClearHint { get; }

    /// <summary><c>g Run</c>, which runs the batch queue.</summary>
    protected KeyHint RunHint { get; }

    /// <summary>The tab's own keys, shown while the details pane is.</summary>
    protected abstract IReadOnlyList<KeyHint> TableHints { get; }

    /// <summary>The tab's own keys in the help overlay.</summary>
    protected abstract HelpGroup TabHelp { get; }

    protected bool IsLogShown => Log.Visible;

    private bool IsQueueShown => _queuePane.Visible;

    protected bool IsCursorRowPinned => Table.CurrentRow is { } row && Shell.IsPinned(row.Id);

    /// <summary>
    /// Called on the UI thread after any operation finishes, from whichever tab. <paramref name="isOrigin"/>
    /// is true on the tab that started it.
    /// </summary>
    public abstract void RefreshAfterOperation(bool isOrigin);

    /// <summary>
    /// Runs <paramref name="fetch"/> on a background task with the table's spinner showing, then
    /// shows its rows and calls <see cref="OnLoaded"/>, or puts the error on the message line.
    /// Starting another load abandons this one.
    /// </summary>
    protected void Load(Func<CancellationToken, Task<IReadOnlyList<PackageRow>>> fetch)
    {
        // Not disposed: a source without a timer holds no resources, and the running load may still read its token.
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        Table.IsLoading = true;

        var app = Shell.App;
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await fetch(token);
                app.Invoke(() => ShowRows(cancellation, rows));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                app.Invoke(() => ShowFailure(cancellation, ex));
            }
        });
    }

    /// <summary>Called on the UI thread after a load's rows are in the table.</summary>
    protected abstract void OnLoaded(IReadOnlyList<PackageRow> rows);

    /// <summary>Called on the UI thread after <see cref="Shell.Pins"/> changes.</summary>
    protected virtual void OnPinsChanged()
    {
        Table.RefreshMarkers();
        Details.SetNeedsDraw();
        UpdateHintsForCursorRow();
    }

    /// <summary>Asks to confirm <paramref name="kind"/> on the cursor row, then runs it with its log in place of the details.</summary>
    protected void RunOperation(OperationKind kind)
    {
        if (Table.CurrentRow is { } row)
        {
            RunOperation(kind, row);
        }
    }

    /// <summary>Asks to confirm <paramref name="kind"/> on <paramref name="row"/>, then runs it with its log in place of the details.</summary>
    protected void RunOperation(OperationKind kind, PackageRow row)
    {
        if (Shell.Runner.IsRunning)
        {
            Shell.SetStatus(Shell.AlreadyRunningText);
            return;
        }

        Shell.AskConfirm(ConfirmQuestion(kind, row), () => StartOperation(kind, row.Id));
    }

    protected void TogglePin()
    {
        if (Table.CurrentRow is { } row)
        {
            Shell.TogglePin(row.Id);
        }
    }

    /// <summary>
    /// Takes <paramref name="row"/> out of the batch queue when it is there, or adds it with the
    /// operation the tab marks it for; a tab that has none for the row says why on the message line.
    /// </summary>
    protected abstract void ToggleMark(PackageRow row);

    /// <summary>Takes <paramref name="row"/> out of the queue when it is there, or queues <paramref name="kind"/> on it.</summary>
    protected void ToggleQueued(OperationKind kind, PackageRow row)
    {
        if (!Shell.Queue.Remove(row.Id))
        {
            Shell.Queue.Add(Shell.BuildOperation(kind, row));
        }
    }

    /// <summary><c>Unmark</c> for a queued row, <c>Mark for batch</c> otherwise; each runs <see cref="ToggleMark"/> on it.</summary>
    protected MenuEntry MarkMenuEntry(PackageRow row) =>
        new(Shell.Queue.Contains(row.Id) ? "Unmark" : "Mark for batch", () => ToggleMark(row));

    /// <summary><c> · 3 marked</c> for the queue entries among <paramref name="rows"/>, or empty when there are none.</summary>
    protected string MarkedSuffix(IReadOnlyList<PackageRow> rows)
    {
        var marked = MarkedCount(rows);
        return marked == 0 ? "" : $" · {marked} marked";
    }

    /// <summary>How many queue entries are for a package among <paramref name="rows"/>, counting an Id listed twice once.</summary>
    protected int MarkedCount(IReadOnlyList<PackageRow> rows)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            ids.Add(row.Id);
        }

        var count = 0;
        foreach (var item in Shell.Queue.Items)
        {
            if (ids.Contains(item.Row.Id))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The context menu's entries for <paramref name="row"/>. Each one runs what the tab's key for it
    /// runs, on this row even if the cursor has moved since the menu opened.
    /// </summary>
    protected abstract IReadOnlyList<MenuEntry> MenuEntries(PackageRow row);

    /// <summary><c>Copy id</c> and <c>Open homepage</c>, which end every tab's menu.</summary>
    protected IReadOnlyList<MenuEntry> PackageMenuEntries(PackageRow row) =>
    [
        new("Copy id", () => Shell.CopyId(row.Id)),
        new("Open homepage", () => Shell.OpenHomepage(row.Id)),
    ];

    /// <summary><c>Upgrade to 2.81.0</c>, or plain <c>Upgrade</c> when the row has no available version.</summary>
    protected static string UpgradeLabel(PackageRow row) =>
        string.IsNullOrEmpty(row.AvailableVersion) ? "Upgrade" : $"Upgrade to {row.AvailableVersion}";

    /// <summary>Focuses the log when it is showing, so coming back to the tab keeps the arrows on it, and the table otherwise.</summary>
    protected void FocusTableOrLog()
    {
        if (IsLogShown)
        {
            Log.SetFocus();
        }
        else
        {
            Table.FocusTable();
        }
    }

    protected void SwitchPane()
    {
        var rightPane = RightPane();
        if (rightPane.HasFocus)
        {
            Table.FocusTable();
        }
        else
        {
            rightPane.SetFocus();
        }
    }

    /// <summary>
    /// Sizes the panes from this tab's width, which is final by the time its subviews are laid
    /// out; a <c>Dim.Func</c> reading it could see the previous frame's width.
    /// </summary>
    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        base.OnSubViewLayout(args);

        var tabWidth = Viewport.Width;
        var leftWidth = tabWidth >= WideLayoutWidth ? WideLeftPaneWidth : tabWidth * NarrowLeftPanePercent / 100;
        if (leftWidth != _leftPaneWidth)
        {
            _leftPaneWidth = leftWidth;
            Table.Width = leftWidth;
            _divider.X = leftWidth;
            Details.X = leftWidth + 1;
            _queuePane.X = leftWidth + 1;
            Log.X = leftWidth + 1;
        }
    }

    /// <summary>
    /// Tab switches panes here rather than through the key bar, because a bar may leave <c>Tab Pane</c>
    /// off to make room. Esc is for when the log is shown but the table has focus; the log handles its own Esc.
    /// </summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Tab)
        {
            SwitchPane();
            return true;
        }

        if (key == Key.Esc && IsLogShown)
        {
            if (Log.IsRunning)
            {
                AskCancel();
            }
            else
            {
                CloseLog();
            }

            return true;
        }

        return base.OnKeyDown(key);
    }

    private static string ConfirmQuestion(OperationKind kind, PackageRow row)
    {
        if (kind == OperationKind.Install)
        {
            return $"Install {row.Id}? (y/n)";
        }

        if (kind == OperationKind.Uninstall)
        {
            return $"Uninstall {row.Id}? (y/n)";
        }

        return string.IsNullOrEmpty(row.AvailableVersion)
            ? $"Upgrade {row.Id}? (y/n)"
            : $"Upgrade {row.Id} to {row.AvailableVersion}? (y/n)";
    }

    private static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private void StartOperation(OperationKind kind, string id)
    {
        // Checked again because the question can outlive an operation started elsewhere.
        if (Shell.Runner.IsRunning)
        {
            Shell.SetStatus(Shell.AlreadyRunningText);
            return;
        }

        // Version stays null so winget picks the latest; the row's Available column can be stale.
        var request = new OperationRequest(id);
        var operation = new RunningOperation(kind, id);
        Log.Begin($"{operation.Verb} {id}", OperationRunner.CommandLine(kind, request));
        Shell.Runner.Start(kind, request, Log.Append, outcome => FinishOperation(operation, outcome));

        Details.Visible = false;
        _queuePane.Visible = false;
        Log.Visible = true;
        Log.SetFocus();
        Shell.RefreshHints(this);
    }

    private void FinishOperation(RunningOperation operation, OperationOutcome outcome)
    {
        var elapsed = Seconds(outcome.Elapsed);
        var subject = $"{operation.Verb} {operation.Id}";
        if (outcome.Succeeded)
        {
            Log.Finish(true, $"Done in {elapsed}");
            Shell.SetSuccess($"{PastTense(operation.Kind)} {operation.Id} in {elapsed}");
        }
        else if (outcome.WasCanceled)
        {
            Log.Finish(false, $"Canceled after {elapsed}");
            Shell.SetStatus($"Canceled {subject}");
        }
        else if (outcome.Result is { } result)
        {
            Log.Finish(false, $"Failed with exit code {result.ExitCode}");
            Shell.SetError($"{subject} failed with exit code {result.ExitCode}");
        }
        else
        {
            var message = outcome.Error?.Message ?? "unknown error";
            Log.Finish(false, $"Failed: {message}");
            Shell.SetError($"{subject} failed: {message}");
        }

        Shell.RefreshHints(this);
        Shell.RefreshAfterOperation(this);
    }

    private static string PastTense(OperationKind kind) => kind switch
    {
        OperationKind.Install => "Installed",
        OperationKind.Upgrade => "Upgraded",
        _ => "Uninstalled",
    };

    private void AskCancel()
    {
        if (Shell.Runner.Current is { } operation && Log.IsRunning)
        {
            Shell.AskConfirm($"Cancel {operation.Verb} {operation.Id}? (y/n)", Shell.Runner.Cancel);
        }
    }

    /// <summary>Puts the details pane back once the operation is over; does nothing while it runs.</summary>
    private void CloseLog()
    {
        if (!IsLogShown || Log.IsRunning)
        {
            return;
        }

        // Moved first, so hiding the log does not leave focus to Terminal.Gui's choice.
        if (Log.HasFocus)
        {
            Table.FocusTable();
        }

        Log.Visible = false;
        ShowQueueOrDetails();
        Shell.RefreshHints(this);
    }

    /// <summary>The pane right of the divider that is showing: the log, the queue, or the details.</summary>
    private View RightPane()
    {
        if (IsLogShown)
        {
            return Log;
        }

        return IsQueueShown ? _queuePane : Details;
    }

    /// <summary>Shows the queue while it has entries and the details otherwise, unless the log is showing.</summary>
    private void ShowQueueOrDetails()
    {
        if (IsLogShown)
        {
            return;
        }

        var showsQueue = Shell.Queue.Count > 0;

        // Moved first, so hiding the focused pane does not leave focus to Terminal.Gui's choice.
        var hidesFocusedPane = showsQueue ? Details.HasFocus : _queuePane.HasFocus;
        if (hidesFocusedPane)
        {
            Table.FocusTable();
        }

        Details.Visible = !showsQueue;
        _queuePane.Visible = showsQueue;
    }

    private void OnQueueChanged()
    {
        Table.RefreshMarkers();
        Table.RefreshCount();
        ShowQueueOrDetails();
    }

    private void MarkCursorRow()
    {
        if (Table.CurrentRow is { } row)
        {
            ToggleMark(row);
        }
    }

    private void FocusLog() => Log.SetFocus();

    private void OpenContextMenu(PackageRow row, Point screenPosition) =>
        Shell.ShowContextMenu(row.Name, MenuEntries(row), screenPosition);

    private void OnCursorChanged(PackageRow? row)
    {
        Details.Show(row);
        if (!_isShowingRows)
        {
            CloseLog();
        }

        UpdateHintsForCursorRow();
    }

    private void OnRowActivated()
    {
        if (!IsLogShown)
        {
            RightPane().SetFocus();
        }
        else if (Log.IsRunning)
        {
            Log.SetFocus();
        }
        else
        {
            CloseLog();
        }
    }

    /// <summary>Refreshes the key bar when the cursor row's pin state differs from what its hints were built for, such as <c>p Pin</c> against <c>p Unpin</c>.</summary>
    private void UpdateHintsForCursorRow()
    {
        var isPinned = IsCursorRowPinned;
        if (isPinned != _hintsAreForPinnedRow)
        {
            _hintsAreForPinnedRow = isPinned;
            Shell.RefreshHints(this);
        }
    }

    private void ShowRows(CancellationTokenSource cancellation, IReadOnlyList<PackageRow> rows)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        _isShowingRows = true;
        Table.SetRows(rows);
        _isShowingRows = false;
        Table.IsLoading = false;
        OnLoaded(rows);
    }

    private void ShowFailure(CancellationTokenSource cancellation, Exception ex)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        Table.IsLoading = false;
        Shell.SetError($"winget: {ex.Message}");
    }
}
