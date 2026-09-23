using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui.Tabs;

/// <summary>
/// A tab with a <see cref="PackageTable"/> on the left and a <see cref="DetailsPane"/> for the
/// cursor row on the right, filled by a background load. Installed, Discover, and Updates are
/// these; they differ in their columns, what they load, and their keys.
/// </summary>
internal abstract class PackageListTab : ShellTab
{
    private const int WideLayoutWidth = 96;
    private const int WideLeftPaneWidth = 57;
    private const int NarrowLeftPanePercent = 60;

    private readonly Line _divider;

    // Only the load this points at may touch the UI; an older one finishing late is ignored.
    private CancellationTokenSource? _loadCancellation;
    private int _leftPaneWidth = WideLeftPaneWidth;

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
        };

        Table.CursorChanged += Details.Show;
        Table.RowActivated += _ => Details.SetFocus();

        Add(Table, _divider, Details);
    }

    protected Shell Shell { get; }

    protected IWingetClient Client { get; }

    protected PackageTable Table { get; }

    protected DetailsPane Details { get; }

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

    protected void SwitchPane()
    {
        if (Details.HasFocus)
        {
            Table.FocusTable();
        }
        else
        {
            Details.SetFocus();
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
        }
    }

    private void ShowRows(CancellationTokenSource cancellation, IReadOnlyList<PackageRow> rows)
    {
        if (cancellation != _loadCancellation)
        {
            return;
        }

        Table.SetRows(rows);
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
