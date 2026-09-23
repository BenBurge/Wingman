using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Updates;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// A tab with a <see cref="PackageTable"/> on the left and a <see cref="DetailsPane"/> for the
/// cursor row on the right, filled by a background load. Installed, Discover, and Updates are
/// these; they differ in their columns, what they load, and their keys. While the batch queue has
/// entries a <see cref="QueuePane"/> takes the details pane's place, and a tab can add a pane of its
/// own that takes it while toggled on. A batch started from the tab puts its
/// <see cref="BatchRunnerScreen"/> in place of both panes until it is over and dismissed, and the
/// install options editor and update policy dialog do the same until they close.
/// </summary>
internal abstract class PackageListTab : ShellTab
{
    private const int WideLayoutWidth = 96;
    private const int WideLeftPaneWidth = 57;
    private const int NarrowLeftPanePercent = 60;

    // The left-pane column where m opens the menu on the cursor row, as the mockup draws it.
    private const int MenuColumn = 24;

    private readonly Line _divider;
    private readonly QueuePane _queuePane;

    // Only the load this points at may touch the UI; an older one finishing late is ignored.
    private CancellationTokenSource? _loadCancellation;
    private int _leftPaneWidth = WideLeftPaneWidth;

    private BatchRunnerScreen? _batchScreen;
    private FormView? _form;

    // The tab's own right pane, if it has one, and whether it is toggled on.
    private View? _extraPane;
    private bool _showsExtraPane;

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
            PolicyFor = PolicyText,
            HasCustomOptions = shell.Options.HasCustomInstallOptions,
        };

        _queuePane = new QueuePane(shell.Theme, shell.Queue)
        {
            X = WideLeftPaneWidth + 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
        };
        _queuePane.RunRequested += RunQueue;
        _queuePane.ClearRequested += shell.ClearQueue;

        Table.IsMarked = row => shell.Queue.Contains(row.Id);
        Table.MarkedScheme = shell.Theme.CellScheme(shell.Theme.Accent);

        MarkHint = new(Key.Space, "Mark", MarkCursorRow, "␣");
        ClearHint = new(Key.C, "Clear", shell.ClearQueue);
        RunHint = new(Key.G, "Run", RunQueue);
        OptionsHint = new(Key.O, "Options", OpenOptionsForCursorRow, IsOnBar: false);

        Table.CursorChanged += OnCursorChanged;
        Table.RowActivated += _ => OnRowActivated();
        Table.RowMenuRequested += OpenContextMenu;
        shell.PinsChanged += OnPinsChanged;
        shell.OptionsChanged += OnOptionsChanged;
        shell.Queue.Changed += OnQueueChanged;

        Add(Table, _divider, Details, _queuePane);
    }

    public sealed override IReadOnlyList<KeyHint> Hints => _batchScreen?.Hints ?? _form?.Hints ?? TableHints;

    public override bool ShowsHelpHint => !IsBatchShown;

    public sealed override IReadOnlyList<HelpGroup> HelpGroups
    {
        get
        {
            if (IsBatchShown)
            {
                return [BatchRunnerScreen.Help];
            }

            return _form is { } form ? [form.Help] : [TabHelp];
        }
    }

    public override void ShowContextMenu()
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        if (Table.CurrentRow is { } row && Table.CursorRowScreenPosition(MenuColumn) is { } position)
        {
            OpenContextMenu(row, position);
        }
    }

    /// <summary>Puts <paramref name="screen"/> in place of the table and the right pane, and gives it focus.</summary>
    public void ShowBatchScreen(BatchRunnerScreen screen)
    {
        _batchScreen = screen;
        Add(screen);

        // Focused first, so hiding the focused table does not leave focus to Terminal.Gui's choice.
        screen.SetFocus();
        HideList();
    }

    /// <summary>Takes the batch screen down and disposes it, putting the table and the right pane back.</summary>
    public void HideBatchScreen()
    {
        if (_batchScreen is not { } screen)
        {
            return;
        }

        _batchScreen = null;
        RestoreList(screen);
    }

    protected Shell Shell { get; }

    protected IWingetClient Client { get; }

    protected PackageTable Table { get; }

    protected DetailsPane Details { get; }

    /// <summary><c>␣ Mark</c>, which toggles the cursor row in the batch queue.</summary>
    protected KeyHint MarkHint { get; }

    /// <summary><c>c Clear</c>, which empties the batch queue.</summary>
    protected KeyHint ClearHint { get; }

    /// <summary><c>g Run</c>, which runs the batch queue.</summary>
    protected KeyHint RunHint { get; }

    /// <summary><c>o Options</c>, which opens the cursor row's install options; off the bar, since no tab has room for it at 96 columns.</summary>
    protected KeyHint OptionsHint { get; }

    /// <summary>The tab's own keys, shown while the table is.</summary>
    protected abstract IReadOnlyList<KeyHint> TableHints { get; }

    /// <summary>The tab's own keys in the help overlay.</summary>
    protected abstract HelpGroup TabHelp { get; }

    /// <summary>Whether a batch screen has the tab's content area.</summary>
    protected bool IsBatchShown => _batchScreen is not null;

    /// <summary>Whether the install options editor or the update policy dialog has the tab's content area.</summary>
    protected bool IsFormShown => _form is not null;

    /// <summary>Whether the tab's own right pane is toggled on, whether or not the table is showing.</summary>
    protected bool IsExtraPaneShown => _showsExtraPane;

    private bool IsQueueShown => _queuePane.Visible;

    /// <summary>
    /// Called on the UI thread after any batch finishes, from whichever tab. <paramref name="isOrigin"/>
    /// is true on the tab that started it.
    /// </summary>
    public abstract void RefreshAfterOperation(bool isOrigin);

    /// <summary>
    /// Runs <paramref name="fetch"/> on a background task with the table's spinner showing, then
    /// shows its rows, as <see cref="RowsToShow"/> picks them, and calls <see cref="OnLoaded"/>, or
    /// puts the error on the message line. Starting another load abandons this one.
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

    /// <summary>The rows of a finished load the table shows; all of them unless a tab leaves some out.</summary>
    protected virtual IReadOnlyList<PackageRow> RowsToShow(IReadOnlyList<PackageRow> loaded) => loaded;

    /// <summary>Called on the UI thread after a load's rows are in the table, with every row the load returned.</summary>
    protected abstract void OnLoaded(IReadOnlyList<PackageRow> rows);

    /// <summary>Called on the UI thread after <see cref="Shell.Pins"/> changes.</summary>
    protected virtual void OnPinsChanged()
    {
        Table.RefreshMarkers();
        Details.SetNeedsDraw();
    }

    /// <summary>Called on the UI thread after a package's options change, which can change its update policy.</summary>
    protected virtual void OnOptionsChanged()
    {
        Table.RefreshMarkers();
        Details.SetNeedsDraw();
    }

    /// <summary>
    /// The installed row whose update policy the details pane shows for <paramref name="row"/>, or
    /// null for none; the row itself by default, since Installed and Updates list installed packages.
    /// </summary>
    protected virtual PackageRow? PolicyRow(PackageRow row) => row;

    /// <summary>Asks to confirm <paramref name="kind"/> on the cursor row, then runs it as a batch of one.</summary>
    protected void RunOperation(OperationKind kind)
    {
        if (Table.CurrentRow is { } row)
        {
            RunOperation(kind, row);
        }
    }

    /// <summary>Asks to confirm <paramref name="kind"/> on <paramref name="row"/>, then runs it as a batch of one.</summary>
    protected void RunOperation(OperationKind kind, PackageRow row)
    {
        if (Shell.IsBatchRunning)
        {
            Shell.SetStatus(Shell.BatchRunningText);
            return;
        }

        // Built when the question is answered, so the batch uses the package's options as they are then.
        Shell.AskConfirm(ConfirmQuestion(kind, row), () => Shell.RunOperation(Shell.BuildOperation(kind, row), this));
    }

    /// <summary>Opens the install options editor for <paramref name="row"/> in place of the table and the right pane.</summary>
    protected void OpenOptions(PackageRow row) => ShowForm(new InstallOptionsEditor(Shell, row));

    /// <summary>Opens the update policy dialog for <paramref name="row"/> in place of the table and the right pane.</summary>
    protected void OpenPolicy(PackageRow row) => ShowForm(new UpdatePolicyDialog(Shell, row));

    protected void OpenPolicyForCursorRow()
    {
        if (Table.CurrentRow is { } row)
        {
            OpenPolicy(row);
        }
    }

    /// <summary><c>Install options…</c>, which opens the editor for <paramref name="row"/>.</summary>
    protected MenuEntry OptionsMenuEntry(PackageRow row) => new("Install options…", () => OpenOptions(row));

    /// <summary><c>Update policy…</c>, which opens the dialog for <paramref name="row"/>.</summary>
    protected MenuEntry PolicyMenuEntry(PackageRow row) => new("Update policy…", () => OpenPolicy(row));

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

    /// <summary>
    /// Focuses the batch screen or the form when one is showing, so coming back to the tab keeps the
    /// keys on it, and the table otherwise.
    /// </summary>
    protected void FocusContent()
    {
        if (_batchScreen is { } screen)
        {
            screen.SetFocus();
        }
        else if (_form is { } form)
        {
            form.SetFocus();
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

    /// <summary>Adds <paramref name="pane"/> right of the divider, hidden until <see cref="ToggleExtraPane"/> shows it.</summary>
    protected void AddExtraPane(View pane)
    {
        _extraPane = pane;
        pane.X = _leftPaneWidth + 1;
        pane.Y = 0;
        pane.Width = Dim.Fill();
        pane.Height = Dim.Fill();
        pane.Visible = false;
        Add(pane);
    }

    /// <summary>Shows the tab's own pane in place of the queue or the details, or puts them back.</summary>
    protected void ToggleExtraPane()
    {
        _showsExtraPane = !_showsExtraPane;
        ShowRightPane();
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
            if (_extraPane is { } pane)
            {
                pane.X = leftWidth + 1;
            }
        }
    }

    /// <summary>
    /// Tab switches panes here rather than through the key bar, because a bar may leave <c>Tab Pane</c>
    /// off to make room. The batch screen and the forms are the only pane while they show, so Tab
    /// does nothing then; a form moves between its fields before the key gets here.
    /// </summary>
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

    /// <summary><c>update</c>, <c>hold (blocking)</c>, <c>skip 1.2.3</c>, or <c>excluded</c>; null for a package with no installed row.</summary>
    private string? PolicyText(PackageRow row)
    {
        if (PolicyRow(row) is not { } installed)
        {
            return null;
        }

        return Shell.ResolvePolicy(installed) switch
        {
            UpdatePolicyKind.Hold => "hold (blocking)",
            UpdatePolicyKind.SkipVersion => $"skip {Shell.Options.GetUpdatesOptions(installed.Id).IgnoredVersion}",
            UpdatePolicyKind.Exclude => "excluded",
            _ => "update",
        };
    }

    private void OpenOptionsForCursorRow()
    {
        if (Table.CurrentRow is { } row)
        {
            OpenOptions(row);
        }
    }

    /// <summary>Puts <paramref name="form"/> in place of the table and the right pane until it closes, focused on its first field.</summary>
    private void ShowForm(FormView form)
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        _form = form;
        form.Closed += () => HideForm(form);
        Add(form);

        // Focused first, so hiding the focused table does not leave focus to Terminal.Gui's choice.
        form.FocusFirstField();
        HideList();
        Shell.RefreshHints(this);
    }

    private void HideForm(FormView form)
    {
        if (_form != form)
        {
            return;
        }

        _form = null;
        RestoreList(form);
        Shell.RefreshHints(this);
    }

    private void HideList()
    {
        Table.Visible = false;
        _divider.Visible = false;
        Details.Visible = false;
        _queuePane.Visible = false;
        if (_extraPane is { } pane)
        {
            pane.Visible = false;
        }
    }

    /// <summary>Puts the table and the right pane back in place of <paramref name="screen"/>, then removes and disposes it.</summary>
    private void RestoreList(View screen)
    {
        Table.Visible = true;
        _divider.Visible = true;
        Table.FocusTable();
        Remove(screen);
        screen.Dispose();
        ShowRightPane();
    }

    private void RunQueue() => Shell.RunQueue(this);

    /// <summary>The pane right of the divider that is showing: the tab's own, the queue, or the details.</summary>
    private View RightPane()
    {
        if (_showsExtraPane && _extraPane is { } pane)
        {
            return pane;
        }

        return IsQueueShown ? _queuePane : Details;
    }

    /// <summary>
    /// Shows the tab's own pane while it is toggled on, else the queue while it has entries, else
    /// the details, unless the batch screen or a form is showing.
    /// </summary>
    private void ShowRightPane()
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        var showsExtra = _showsExtraPane && _extraPane is not null;
        var showsQueue = !showsExtra && Shell.Queue.Count > 0;
        var showsDetails = !showsExtra && !showsQueue;

        // Moved first, so hiding the focused pane does not leave focus to Terminal.Gui's choice.
        var hidesFocusedPane = (!showsDetails && Details.HasFocus)
            || (!showsQueue && _queuePane.HasFocus)
            || (!showsExtra && _extraPane is { HasFocus: true });
        if (hidesFocusedPane)
        {
            Table.FocusTable();
        }

        Details.Visible = showsDetails;
        _queuePane.Visible = showsQueue;
        if (_extraPane is { } pane)
        {
            pane.Visible = showsExtra;
        }
    }

    private void OnQueueChanged()
    {
        Table.RefreshMarkers();
        Table.RefreshCount();
        ShowRightPane();
    }

    private void MarkCursorRow()
    {
        if (Table.CurrentRow is { } row)
        {
            ToggleMark(row);
        }
    }

    private void OpenContextMenu(PackageRow row, Point screenPosition) =>
        Shell.ShowContextMenu(row.Name, MenuEntries(row), screenPosition);

    private void OnCursorChanged(PackageRow? row) => Details.Show(row);

    private void OnRowActivated() => RightPane().SetFocus();

    private void ShowRows(CancellationTokenSource cancellation, IReadOnlyList<PackageRow> rows)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        Table.SetRows(RowsToShow(rows));
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
